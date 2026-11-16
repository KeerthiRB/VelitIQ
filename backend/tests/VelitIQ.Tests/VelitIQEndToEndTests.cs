using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MCPClient;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using VelitIQ.Core.DTOs;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure.Data;
using VelitIQ.Infrastructure.Parsers;
using VelitIQ.Infrastructure.Security;
using VelitIQ.Mcp;

namespace VelitIQ.Tests;

public sealed class VelitIQEndToEndTests : IAsyncLifetime
{
    private const string TestConnString = "Host=127.0.0.1;Port=5432;Database=velitiq_test;Username=postgres;Password=postgres";
    private WebApplicationFactory<Program> _factory = null!;
    private string _tempStorageDir = null!;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAsync()
    {
        _tempStorageDir = Path.Combine(Path.GetTempPath(), "velitiq_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempStorageDir);

        // Clean test tables before running E2E suite
        await using (var conn = new NpgsqlConnection(TestConnString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DROP SCHEMA public CASCADE; CREATE SCHEMA public;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, cfg) =>
            {
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = TestConnString,
                    ["Database:AutoMigrateAndSeed"] = "true",
                    ["Storage:LocalRootPath"] = _tempStorageDir,
                    ["AzureFoundry:Mode"] = "LocalDeterministic"
                });
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IMcpLoopbackClientFactory>();
                services.AddSingleton<IMcpLoopbackClientFactory>(_ => new TestServerMcpLoopbackFactory(() => _factory.CreateClient()));
            });
        });

        // Trigger host initialization & seed
        using var initClient = _factory.CreateClient();
        var health = await initClient.GetAsync("/api/health");
        health.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        if (Directory.Exists(_tempStorageDir))
        {
            try { Directory.Delete(_tempStorageDir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task MultiFormatParsers_ExtractFormulasHeadingsTablesAndPdfLayout()
    {
        // 1. Excel (.xlsx) with formulas
        var excelParser = new ExcelDocumentParser();
        await using var xlsx = SampleArtifactBuilder.BuildNostroExcelWorkbook();
        var excelRes = await excelParser.ParseAsync(xlsx, "Nostro.xlsx");
        Assert.Equal(2, excelRes.PageOrSheetCount);
        Assert.True(excelRes.FormulaCount >= 6);
        Assert.Contains(excelRes.Sheets[0].Formulas, f => f.Formula.Contains("SUM", StringComparison.OrdinalIgnoreCase));

        // 2. Word (.docx) with headings & Markdown tables
        var wordParser = new WordDocumentParser();
        await using var docx = SampleArtifactBuilder.BuildCoreBankingWordRunbook();
        var wordRes = await wordParser.ParseAsync(docx, "Runbook.docx");
        Assert.NotEmpty(wordRes.Chunks);
        Assert.Contains(wordRes.Chunks, c => c.Content.Contains("GL_EOD_POST_MASTER") && c.Content.Contains("|"));

        // 3. PDF (OneNote-exported layout)
        var pdfParser = new PdfDocumentParser();
        await using var pdf = SampleArtifactBuilder.BuildOneNoteExportedPdf("OneNote Handover", ["Line 1: SWIFT_MT103", "Line 2: PRD-PG-CORE-01"]);
        var pdfRes = await pdfParser.ParseAsync(pdf, "OneNoteExport.pdf");
        Assert.Equal(1, pdfRes.PageOrSheetCount);
        Assert.Contains(pdfRes.Chunks, c => c.Content.Contains("SWIFT_MT103"));

        // 4. CSV + Secret Guardrail Scanner
        var csvParser = new CsvDocumentParser();
        var csvBytes = Encoding.UTF8.GetBytes("System,ConnStr\nGL_DB,\"Server=prd;Password=LeakedProdPass99!\"\n");
        await using var csvStream = new MemoryStream(csvBytes);
        var csvRes = await csvParser.ParseAsync(csvStream, "Connections.csv");
        Assert.Single(csvRes.Sheets);

        var scanner = new SecretGuardrailScanner();
        var findings = scanner.ScanParsedDocument(Guid.NewGuid(), csvRes);
        Assert.NotEmpty(findings);
    }

    [Fact]
    public async Task ChineseWallRbac_EnforcesTeamIsolation_CrossTeamSharing_AndAdminAccess()
    {
        using var client = _factory.CreateClient();

        // 1. Sign in as Treasury Lead (Arjun Verma)
        var treasuryLogin = await LoginAsync(client, "arjun.verma@horizonbank.com", "VelitIQ@2026!");
        SetBearer(client, treasuryLogin.AccessToken);

        var treasuryDocs = await client.GetFromJsonAsync<List<KnowledgeDocument>>("/api/documents", JsonOpts);
        Assert.NotNull(treasuryDocs);
        // Should see Treasury TeamOnly Nostro Excel + 2 BankWideShared docs (CSV + OneNote PDF) = 3 docs!
        // Must NOT see Core Banking TeamOnly Word Runbook or Quant Risk TeamOnly VaR Excel!
        Assert.Contains(treasuryDocs!, d => d.OriginalFileName.Contains("SWIFT_Nostro_Reconciliation_Model.xlsx"));
        Assert.Contains(treasuryDocs!, d => d.OriginalFileName.Contains("OneNote_Export_SWIFT_GL_Architecture.pdf"));
        Assert.DoesNotContain(treasuryDocs!, d => d.OriginalFileName.Contains("Confidential_Intraday_VaR_Desk_Limits.xlsx"));
        Assert.DoesNotContain(treasuryDocs!, d => d.OriginalFileName.Contains("Core_GL_Batch_Failure_Runbook.docx"));

        // 2. Sign in as Global Admin (Keerthi RB) -> sees ALL 5 documents across all 3 Departments!
        var adminLogin = await LoginAsync(client, "admin@horizonbank.com", "VelitIQ@2026!");
        SetBearer(client, adminLogin.AccessToken);

        var adminDocs = await client.GetFromJsonAsync<List<KnowledgeDocument>>("/api/documents", JsonOpts);
        Assert.NotNull(adminDocs);
        Assert.Equal(5, adminDocs!.Count);

        var quantRiskDoc = adminDocs.Single(d => d.OriginalFileName.Contains("Confidential_Intraday_VaR_Desk_Limits.xlsx"));

        // Verify direct URL access to Quant Risk doc by Treasury Lead returns 403 Forbidden and logs RBAC denial!
        SetBearer(client, treasuryLogin.AccessToken);
        var forbiddenResp = await client.GetAsync($"/api/documents/{quantRiskDoc.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResp.StatusCode);

        // 3. Sign in as Cross-Team Auditor -> also sees ALL 5 documents!
        var auditorLogin = await LoginAsync(client, "auditor@horizonbank.com", "VelitIQ@2026!");
        SetBearer(client, auditorLogin.AccessToken);
        var auditorDocs = await client.GetFromJsonAsync<List<KnowledgeDocument>>("/api/documents", JsonOpts);
        Assert.Equal(5, auditorDocs!.Count);
    }

    [Fact]
    public async Task McpClientNet_And_McpHttpServer_EnforceCallbacks_Tools_ResourceTemplates_AndPrompts()
    {
        using var authHttp = _factory.CreateClient();
        var treasuryLogin = await LoginAsync(authHttp, "arjun.verma@horizonbank.com", "VelitIQ@2026!");
        var adminLogin = await LoginAsync(authHttp, "admin@horizonbank.com", "VelitIQ@2026!");

        // 1. Unauthenticated MCP Client -> [McpAuthorize] hides protected tools!
        await using (var anonMcp = await McpClientBuilder
            .ForStreamableHttp(new Uri("http://localhost/mcp"), _factory.CreateClient(), false)
            .BuildAndInitializeAsync())
        {
            var anonTools = await anonMcp.ListToolsAsync();
            Assert.Empty(anonTools.Tools);

            // Public status resource is readable
            var statusRes = await anonMcp.ReadResourceAsync("velitiq://platform/status");
            Assert.Contains("McpHttpServer v1.2.0", statusRes.Contents[0].Text);
        }

        // 2. Treasury TeamLead MCP Client -> sees standard RAG & spreadsheet tools, NOT admin governance_audit_overview!
        await using (var treasuryMcp = await McpClientBuilder
            .ForStreamableHttp(new Uri("http://localhost/mcp"), _factory.CreateClient(), false)
            .WithHeader("Authorization", $"Bearer {treasuryLogin.AccessToken}")
            .BuildAndInitializeAsync())
        {
            var tTools = await treasuryMcp.ListToolsAsync();
            Assert.Contains(tTools.Tools, t => t.Name == "search_hybrid_knowledge");
            Assert.Contains(tTools.Tools, t => t.Name == "query_tabular_sheet");
            Assert.DoesNotContain(tTools.Tools, t => t.Name == "governance_audit_overview");

            // Dynamic Resource Template: velitiq://teams/{teamSlug}/handover-pack
            var packRes = await treasuryMcp.ReadResourceAsync("velitiq://teams/treasury-settlement/handover-pack");
            Assert.Contains("Priya Nair", packRes.Contents[0].Text);

            // Prompt Template resolution
            var promptRes = await treasuryMcp.GetPromptAsync("successor_onboarding_plan", new
            {
                departingUserName = "Priya Nair",
                teamName = "Treasury & SWIFT Settlement"
            });
            Assert.Contains("Priya Nair", promptRes.Messages[0].Content.Text);
        }

        // 3. Global Admin MCP Client -> sees admin tool governance_audit_overview AND executes it!
        await using (var adminMcp = await McpClientBuilder
            .ForStreamableHttp(new Uri("http://localhost/mcp"), _factory.CreateClient(), false)
            .WithHeader("Authorization", $"Bearer {adminLogin.AccessToken}")
            .BuildAndInitializeAsync())
        {
            var aTools = await adminMcp.ListToolsAsync();
            Assert.Contains(aTools.Tools, t => t.Name == "governance_audit_overview");

            var auditCall = await adminMcp.CallToolAsync("governance_audit_overview", new { });
            Assert.False(auditCall.IsError);
            Assert.Contains("recentAuditEvents", auditCall.GetText());
        }
    }

    [Fact]
    public async Task AgenticCopilot_TabularAggregation_Upload_AndGapAnswerIndexing_WorkEndToEnd()
    {
        using var client = _factory.CreateClient();
        var adminLogin = await LoginAsync(client, "admin@horizonbank.com", "VelitIQ@2026!");
        SetBearer(client, adminLogin.AccessToken);

        // 1. Ask Agentic Copilot a multi-source Nostro Excel + Author Gap Clarification question
        var chatReq = new AgentChatRequest(
            "What is the total unreconciled break across Nostro accounts in the Excel workbook, and how are Nostro breaks above $25,000 signed off during December?");
        var chatHttpResp = await client.PostAsJsonAsync("/api/chat/ask", chatReq, JsonOpts);
        chatHttpResp.EnsureSuccessStatusCode();
        var chatRes = await chatHttpResp.Content.ReadFromJsonAsync<AgentChatResponse>(JsonOpts);

        Assert.NotNull(chatRes);
        Assert.Contains(chatRes!.McpToolTraces, t => t.ToolName == "search_hybrid_knowledge");
        Assert.Contains(chatRes.McpToolTraces, t => t.ToolName == "query_tabular_sheet");
        Assert.NotNull(chatRes.StructuredTableResult);
        Assert.Equal("Nostro_Balances", chatRes.StructuredTableResult!.SheetName);
        Assert.NotEmpty(chatRes.Citations);
        Assert.Contains("TREAS-RECON-P1", chatRes.AnswerMarkdown); // Retrieved from pre-answered AuthorGapAnswer!

        // 2. Answer an Open Handover Gap Question for Rahul Menon with unique tribal knowledge & verify immediate RAG retrieval!
        var gaps = await client.GetFromJsonAsync<List<HandoverGapQuestion>>("/api/gaps", JsonOpts);
        var openGap = gaps!.First(g => g.Status == GapQuestionStatus.Open);

        var answerResp = await client.PostAsJsonAsync(
            $"/api/gaps/{openGap.Id}/answer",
            new AnswerGapQuestionRequest("Emergency manual override requires token OVERRIDE_KEY_DEC_8842 approved by Head of Core Ledger."),
            JsonOpts);
        answerResp.EnsureSuccessStatusCode();

        var searchResp = await client.PostAsJsonAsync(
            "/api/search/hybrid",
            new HybridSearchRequest("OVERRIDE_KEY_DEC_8842"),
            JsonOpts);
        var searchHits = await searchResp.Content.ReadFromJsonAsync<List<HybridSearchHitDto>>(JsonOpts);
        Assert.NotNull(searchHits);
        Assert.Contains(searchHits!, h => h.Content.Contains("OVERRIDE_KEY_DEC_8842") && h.SourceType == "AuthorGapAnswer");

        // 3. Upload a new CSV via multipart/form-data containing a secret warning & verify full ingestion!
        var departingUsers = await client.GetFromJsonAsync<List<DepartingUserProfile>>("/api/departing-users", JsonOpts);
        var targetUser = departingUsers!.First();

        using var form = new MultipartFormDataContent();
        var csvPayload = Encoding.UTF8.GetBytes("Job_Name,Server,Backup_Cred\nEOD_WIRE_SYNC,PRD-WIRE-09,\"Password=TempWirePass2026!\"\n");
        var fileContent = new ByteArrayContent(csvPayload);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "Wire_Sync_Jobs.csv");
        form.Add(new StringContent(targetUser.Id.ToString()), "departingUserId");
        form.Add(new StringContent("Wire Sync Batch Inventory"), "title");
        form.Add(new StringContent("Runbook"), "category");
        form.Add(new StringContent("TeamOnly"), "visibility");

        var uploadResp = await client.PostAsync("/api/documents/upload", form);
        uploadResp.EnsureSuccessStatusCode();
        var uploadedDoc = await uploadResp.Content.ReadFromJsonAsync<KnowledgeDocument>(JsonOpts);
        Assert.NotNull(uploadedDoc);
        Assert.Equal(IngestionStatus.Completed, uploadedDoc!.Status);
        Assert.True(uploadedDoc.SecretWarningCount >= 1);
    }

    [Fact]
    public async Task McpStdioServer_ConnectsViaMcpClientNet_AndQueriesPostgresKnowledgePack()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var cliDll = Path.Combine(repoRoot, "backend", "src", "VelitIQ.Mcp.Cli", "bin", "Debug", "net10.0", "VelitIQ.Mcp.Cli.dll");
        Assert.True(File.Exists(cliDll), $"Expected STDIO CLI assembly at {cliDll}");

        var dotnetExe = File.Exists(@"C:\Program Files\dotnet\dotnet.exe")
            ? @"C:\Program Files\dotnet\dotnet.exe"
            : "dotnet";

        var envVars = new Dictionary<string, string?>
        {
            ["VELITIQ_DB_CONNECTION"] = TestConnString,
            ["VELITIQ_ROLE"] = "CrossTeamViewer"
        };

        await using var stdioClient = await McpClientBuilder
            .ForStdio(dotnetExe, $"\"{cliDll}\"", repoRoot, envVars)
            .WithClientInfo("VelitIQ.StdioVerificationClient", "1.0.1")
            .WithTimeout(TimeSpan.FromSeconds(20))
            .BuildAndInitializeAsync();

        var tools = await stdioClient.ListToolsAsync();
        Assert.Contains(tools.Tools, t => t.Name == "velitiq_search");
        Assert.Contains(tools.Tools, t => t.Name == "velitiq_list_departing_staff");

        var searchCall = await stdioClient.CallToolAsync("velitiq_search", new { query = "GL_EOD_POST_MASTER" });
        Assert.False(searchCall.IsError);
        Assert.Contains("PRD-GL-04", searchCall.GetText());

        var infoResource = await stdioClient.ReadResourceAsync("velitiq://stdio/info");
        Assert.Contains("Keerukee.MCPServer.Stdio 1.0.3", infoResource.Contents[0].Text);
    }

    private static async Task<AuthTokenResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password), JsonOpts);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AuthTokenResponse>(JsonOpts))!;
    }

    private static void SetBearer(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private sealed class TestServerMcpLoopbackFactory(Func<HttpClient> factory) : IMcpLoopbackClientFactory
    {
        public (Uri EndpointUri, HttpClient HttpClient, bool DisposeClient) CreateLoopbackHttpTarget() =>
            (new Uri("http://localhost/mcp"), factory(), true);
    }
}
