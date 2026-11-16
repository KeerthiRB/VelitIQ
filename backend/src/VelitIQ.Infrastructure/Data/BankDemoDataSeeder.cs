using Microsoft.Extensions.Logging;
using VelitIQ.Core.Configuration;
using VelitIQ.Core.Models;
using VelitIQ.Infrastructure.Ingestion;
using VelitIQ.Infrastructure.Security;

namespace VelitIQ.Infrastructure.Data;

public sealed class BankDemoDataSeeder
{
    private readonly PostgresKnowledgeStore _store;
    private readonly KnowledgeIngestionService _ingestion;
    private readonly ILogger<BankDemoDataSeeder> _logger;

    public BankDemoDataSeeder(
        PostgresKnowledgeStore store,
        KnowledgeIngestionService ingestion,
        ILogger<BankDemoDataSeeder> logger)
    {
        _store = store;
        _ingestion = ingestion;
        _logger = logger;
    }

    public async Task SeedIfEmptyAsync(CancellationToken ct = default)
    {
        var existingDepts = await _store.GetDepartmentsAsync(ct);
        if (existingDepts.Count > 0)
        {
            _logger.LogInformation("Database already contains {Count} departments; skipping initial seed.", existingDepts.Count);
            return;
        }

        _logger.LogInformation("Seeding Horizon Bank December Knowledge Offboarding dataset...");

        // 1. Create Bank Departments
        var deptOps = await _store.CreateDepartmentAsync(new Department
        {
            Name = "Operations & Treasury",
            Code = "OPS",
            Description = "Global Payment Rails, SWIFT/Fedwire Settlement, Nostro Reconciliation & Liquidity."
        }, ct);

        var deptTech = await _store.CreateDepartmentAsync(new Department
        {
            Name = "Enterprise Technology",
            Code = "TECH",
            Description = "Core Banking Ledger, Real-Time Kafka Event Mesh, API Gateways & Batch Engineering."
        }, ct);

        var deptRisk = await _store.CreateDepartmentAsync(new Department
        {
            Name = "Risk & Compliance",
            Code = "RISK",
            Description = "Quantitative Market Risk, Counterparty VaR Limits, Stress Testing & Regulatory Reporting."
        }, ct);

        // 2. Create Teams with On-Prem AD Group & Future Entra ID Group Mappings
        var teamTreasury = await _store.CreateTeamAsync(new Team
        {
            DepartmentId = deptOps.Id,
            Name = "Treasury & SWIFT Settlement",
            Slug = "treasury-settlement",
            Description = "Owns SWIFT MT103/MT202 cross-border wires, Fedwire cut-offs, and daily Nostro break resolution.",
            AdGroupName = "CN=SG-VelitIQ-TreasuryOps",
            EntraGroupId = "entra-grp-treasury-ops-001",
            ManagerEmail = "arjun.verma@horizonbank.com"
        }, ct);

        var teamCoreTech = await _store.CreateTeamAsync(new Team
        {
            DepartmentId = deptTech.Id,
            Name = "Core Banking & Ledger Platform",
            Slug = "core-banking-platform",
            Description = "Owns Core General Ledger (GL) posting batch, Kafka ledger streams, and API integration.",
            AdGroupName = "CN=SG-VelitIQ-CoreBanking",
            EntraGroupId = "entra-grp-core-banking-002",
            ManagerEmail = "meera.krishnan@horizonbank.com"
        }, ct);

        var teamQuantRisk = await _store.CreateTeamAsync(new Team
        {
            DepartmentId = deptRisk.Id,
            Name = "Quantitative Market & Credit Risk",
            Slug = "quant-market-risk",
            Description = "Owns Intraday VaR calculation engines, Murex/Calypso stress grids, and desk limit monitoring.",
            AdGroupName = "CN=SG-VelitIQ-QuantRisk",
            EntraGroupId = "entra-grp-quant-risk-003",
            ManagerEmail = "david.chen@horizonbank.com"
        }, ct);

        // 3. Create Seeded Bank Users (Password: VelitIQ@2026!)
        var defaultPwHash = PasswordHasher.HashPassword("VelitIQ@2026!");

        var adminUser = new AppUser
        {
            Username = "keerthi.rb",
            Email = "admin@horizonbank.com",
            DisplayName = "Keerthi RB (Global Admin)",
            JobTitle = "Chief Enterprise Knowledge & AI Architect",
            PasswordHash = defaultPwHash,
            AuthSource = "Local",
            Role = UserRole.Admin,
            PrimaryTeamId = null
        };
        await _store.UpsertAppUserAsync(adminUser, ct);

        var crossTeamAuditor = new AppUser
        {
            Username = "marcus.vance",
            Email = "auditor@horizonbank.com",
            DisplayName = "Marcus Vance (Cross-Team Audit)",
            JobTitle = "VP — Operational Risk & Audit Governance",
            PasswordHash = defaultPwHash,
            AuthSource = "ActiveDirectory",
            Role = UserRole.CrossTeamViewer,
            PrimaryTeamId = null
        };
        await _store.UpsertAppUserAsync(crossTeamAuditor, ct);

        var treasuryLead = new AppUser
        {
            Username = "arjun.verma",
            Email = "arjun.verma@horizonbank.com",
            DisplayName = "Arjun Verma (Treasury Lead)",
            JobTitle = "AVP — Treasury Settlement Successor",
            PasswordHash = defaultPwHash,
            AuthSource = "ActiveDirectory",
            Role = UserRole.TeamLead,
            PrimaryTeamId = teamTreasury.Id
        };
        await _store.UpsertAppUserAsync(treasuryLead, ct);

        var rahulAppUser = new AppUser
        {
            Username = "rahul.menon",
            Email = "rahul.menon@horizonbank.com",
            DisplayName = "Rahul Menon (Core Tech — Exiting Dec 18)",
            JobTitle = "Principal Core Banking Architect",
            PasswordHash = defaultPwHash,
            AuthSource = "ActiveDirectory",
            Role = UserRole.Employee,
            PrimaryTeamId = teamCoreTech.Id
        };
        await _store.UpsertAppUserAsync(rahulAppUser, ct);

        var elenaAppUser = new AppUser
        {
            Username = "elena.rostova",
            Email = "elena.rostova@horizonbank.com",
            DisplayName = "Elena Rostova (Quant Risk — Exiting Dec 21)",
            JobTitle = "Director — Quantitative Market Risk",
            PasswordHash = defaultPwHash,
            AuthSource = "ActiveDirectory",
            Role = UserRole.TeamLead,
            PrimaryTeamId = teamQuantRisk.Id
        };
        await _store.UpsertAppUserAsync(elenaAppUser, ct);

        // 4. Create December Departing Employee Profiles
        var priyaDeparting = await _store.UpsertDepartingUserAsync(new DepartingUserProfile
        {
            DepartmentId = deptOps.Id,
            TeamId = teamTreasury.Id,
            FullName = "Priya Nair",
            Email = "priya.nair@horizonbank.com",
            EmployeeId = "HB-EMP-10482",
            JobTitle = "VP — Global SWIFT & Nostro Settlements",
            ExitDate = new DateOnly(2026, 12, 14),
            SuccessorName = "Arjun Verma",
            SuccessorEmail = "arjun.verma@horizonbank.com",
            SystemsOwned = ["SWIFT_ALLIANCE_GW", "FRB_FEDLINE_PRD", "NOSTRO_RECON_ENGINE"],
            RecurringSchedules = ["Daily 17:00 EST SWIFT Cut-off", "Daily 08:30 EST Nostro Break Sign-off"],
            KeyContacts = ["treasury-recon@horizonbank.com", "swift-support@horizonbank.com"]
        }, ct);

        var rahulDeparting = await _store.UpsertDepartingUserAsync(new DepartingUserProfile
        {
            UserId = rahulAppUser.Id,
            DepartmentId = deptTech.Id,
            TeamId = teamCoreTech.Id,
            FullName = "Rahul Menon",
            Email = "rahul.menon@horizonbank.com",
            EmployeeId = "HB-EMP-20915",
            JobTitle = "Principal Core Banking & Ledger Architect",
            ExitDate = new DateOnly(2026, 12, 18),
            SuccessorName = "Meera Krishnan",
            SuccessorEmail = "meera.krishnan@horizonbank.com",
            SystemsOwned = ["PRD-GL-04", "KAFKA_LEDGER_FEED", "GL_EOD_POST_MASTER"],
            RecurringSchedules = ["Daily 21:30 EST GL Batch Posting", "Month-End Close Freeze T-1"],
            KeyContacts = ["core-ledger-oncall@horizonbank.com", "gl-controllers@horizonbank.com"]
        }, ct);

        var elenaDeparting = await _store.UpsertDepartingUserAsync(new DepartingUserProfile
        {
            UserId = elenaAppUser.Id,
            DepartmentId = deptRisk.Id,
            TeamId = teamQuantRisk.Id,
            FullName = "Elena Rostova",
            Email = "elena.rostova@horizonbank.com",
            EmployeeId = "HB-EMP-31704",
            JobTitle = "Director — Quantitative VaR & Stress Models",
            ExitDate = new DateOnly(2026, 12, 21),
            SuccessorName = "David Chen",
            SuccessorEmail = "david.chen@horizonbank.com",
            SystemsOwned = ["MUREX_HVAR_V4", "CALYPSO_MC_SIM", "RISK_GRID_PRD_09"],
            RecurringSchedules = ["Daily 06:00 EST Pre-Market VaR Sign-off", "Weekly CCAR Stress Run"],
            KeyContacts = ["market-risk-quants@horizonbank.com"]
        }, ct);

        var adminSecCtx = await BuildStoreContext(adminUser);

        // 5. Ingest Real Multi-Format Artifacts (Excel with formulas, CSV, Word .docx, and OneNote-exported PDF!)
        await using (var excelNostro = SampleArtifactBuilder.BuildNostroExcelWorkbook())
        {
            await _ingestion.IngestDocumentAsync(
                excelNostro,
                "SWIFT_Nostro_Reconciliation_Model.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                priyaDeparting.Id,
                "SWIFT & Nostro Daily Reconciliation Workbook",
                DocumentCategory.FinancialOrRiskModel,
                VisibilityScope.TeamOnly,
                ["nostro", "swift", "reconciliation", "treasury"],
                adminSecCtx,
                "127.0.0.1",
                ct);
        }

        await using (var csvCutoff = SampleArtifactBuilder.BuildCutoffCalendarCsv())
        {
            await _ingestion.IngestDocumentAsync(
                csvCutoff,
                "Payment_Rails_December_Cutoff_Schedule.csv",
                "text/csv",
                priyaDeparting.Id,
                "2026 December Year-End Payment Cut-off Matrix",
                DocumentCategory.ProcessAndSop,
                VisibilityScope.BankWideShared, // Shared across ALL teams!
                ["fedwire", "swift", "cutoff", "december", "shared"],
                adminSecCtx,
                "127.0.0.1",
                ct);
        }

        await using (var wordRunbook = SampleArtifactBuilder.BuildCoreBankingWordRunbook())
        {
            await _ingestion.IngestDocumentAsync(
                wordRunbook,
                "Core_GL_Batch_Failure_Runbook.docx",
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                rahulDeparting.Id,
                "Core General Ledger EOD Batch & Kafka Failure Runbook",
                DocumentCategory.Runbook,
                VisibilityScope.TeamOnly,
                ["gl", "kafka", "ctrl-m", "eod-batch", "recovery"],
                adminSecCtx,
                "127.0.0.1",
                ct);
        }

        var oneNoteLines = new[]
        {
            "Exported from Microsoft OneNote — Enterprise Payment & Ledger Integration Notebook",
            "Author: Rahul Menon | Shared Cross-Team Handover Reference (Tech + Treasury Ops)",
            "1. SWIFT_MT103 Inbound Flow: Messages enter via SWIFT_ALLIANCE_GW -> ISO20022 Translator -> KAFKA_LEDGER_FEED.",
            "2. Duplicate Detection Key: Composite UETR + ValueDate + OrderingBIC cached for 72 hours in PRD-PG-CORE-01.",
            "3. December Freeze Protocol: Schema changes on GL_RECON_STG are frozen from December 12 through January 4.",
            "4. Emergency Bypass: Only VP Treasury Ops + Principal Core Architect together may toggle feature flag ALLOW_LATE_WIRE_POST."
        };
        await using (var pdfOneNote = SampleArtifactBuilder.BuildOneNoteExportedPdf("OneNote Export: Cross-Team SWIFT to GL Architecture Notes", oneNoteLines))
        {
            await _ingestion.IngestDocumentAsync(
                pdfOneNote,
                "OneNote_Export_SWIFT_GL_Architecture.pdf",
                "application/pdf",
                rahulDeparting.Id,
                "OneNote Export: SWIFT MT103 to Core GL Integration Notes",
                DocumentCategory.OneNoteExport,
                VisibilityScope.BankWideShared, // Cross-team shared!
                ["onenote-export", "swift-mt103", "architecture", "december-freeze"],
                adminSecCtx,
                "127.0.0.1",
                ct);
        }

        await using (var excelRisk = SampleArtifactBuilder.BuildQuantRiskExcelWorkbook())
        {
            await _ingestion.IngestDocumentAsync(
                excelRisk,
                "Confidential_Intraday_VaR_Desk_Limits.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                elenaDeparting.Id,
                "Confidential Intraday 99% VaR Trading Desk Limits",
                DocumentCategory.FinancialOrRiskModel,
                VisibilityScope.TeamOnly, // Chinese-walled to Quant Risk + Global Admin + Cross-Team Auditor!
                ["var", "market-risk", "murex", "confidential"],
                adminSecCtx,
                "127.0.0.1",
                ct);
        }

        // Pre-answer one Handover Gap Question for Priya Nair to demonstrate how answered Gap Q&As become searchable RAG chunks!
        var priyaGaps = await _store.GetGapQuestionsAsync(adminSecCtx, departingUserId: priyaDeparting.Id, ct: ct);
        if (priyaGaps.Count > 0)
        {
            await _ingestion.AnswerGapQuestionAndIndexAsync(
                priyaGaps[0].Id,
                "During December year-end close, any Nostro break above $25,000 on NOSTRO-USD-101 or NOSTRO-GBP-309 requires sign-off in ServiceNow queue TREAS-RECON-P1 before 11:00 EST, and temporary FX timing differences under $5,000 auto-clear on T+1.",
                adminSecCtx,
                "127.0.0.1",
                ct);
        }

        _logger.LogInformation("Seeded 3 Departments, 3 Teams, 5 Bank Users, 3 December Departing Staff, and 5 Multi-Format Artifacts.");
    }

    private static Task<SecurityContext> BuildStoreContext(AppUser admin) =>
        Task.FromResult(new SecurityContext
        {
            UserId = admin.Id,
            Username = admin.Username,
            Email = admin.Email,
            DisplayName = admin.DisplayName,
            Role = UserRole.Admin
        });
}
