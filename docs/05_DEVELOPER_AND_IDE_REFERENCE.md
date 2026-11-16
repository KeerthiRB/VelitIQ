# 05. Developer & Office IDE Reference Guide

**Document Classification:** Engineering Reference & Extension Playbook  
**Audience:** Software Engineers & AI Coding Assistants (Antigravity, Devin, Cursor, GitHub Copilot, Visual Studio) operating on the bank's internal network  

---

## 1. Repository Map & Module Responsibilities

```text
VelitIQ/
├── AGENTS.md                               # Root AI IDE / Coding Agent architectural context & rules
├── README.md                               # Executive & developer landing page
├── global.json                             # .NET 10 SDK configuration (rollForward: latestMajor)
├── VelitIQ.sln                             # Complete 6-project .NET 10 solution
├── scripts/
│   ├── setup-local-postgres.ps1            # Zero-admin local PostgreSQL 17.4 download, initdb & DB bootstrap
│   ├── install-pgvector-windows.ps1        # Compiles official pgvector on Windows with VS C++ nmake
│   ├── publish-windows-server.ps1          # Release build script for Windows Server (API + CLI + Next.js)
│   └── start-dev.ps1                       # One-command local startup (Postgres + API/MCP + Next.js)
├── backend/
│   ├── src/
│   │   ├── VelitIQ.Core/                   # Pure domain models, enums, DTOs, options & interfaces
│   │   │   ├── Configuration/VelitIQOptions.cs
│   │   │   ├── DTOs/ApiContracts.cs
│   │   │   ├── Interfaces/ServiceInterfaces.cs
│   │   │   └── Models/DomainModels.cs
│   │   ├── VelitIQ.Infrastructure/         # Data access, parsers, AI providers, AD/LDAP auth & storage
│   │   │   ├── Ai/AzureFoundryAiService.cs
│   │   │   ├── Ai/LocalDeterministicAiService.cs
│   │   │   ├── Ai/CompositeAiService.cs
│   │   │   ├── Data/PostgresKnowledgeStore.cs
│   │   │   ├── Data/BankDemoDataSeeder.cs
│   │   │   ├── Data/SampleArtifactBuilder.cs
│   │   │   ├── Ingestion/KnowledgeIngestionService.cs
│   │   │   ├── Parsers/ (Csv, Excel, Word, Pdf, TextChunkingHelper)
│   │   │   ├── Security/ (BankAuthService, SecretGuardrailScanner)
│   │   │   └── Storage/FileStorageServices.cs
│   │   ├── VelitIQ.Mcp/                    # Keerthi_RB McpHttpServer handlers & Mcp.Client.Net orchestrator
│   │   │   ├── VelitIQKnowledgeMcpHandler.cs
│   │   │   └── AgenticRagOrchestrator.cs
│   │   ├── VelitIQ.Api/                    # .NET 10 Web Host (REST endpoints + /mcp Streamable HTTP)
│   │   │   ├── Program.cs
│   │   │   └── appsettings.json
│   │   └── VelitIQ.Mcp.Cli/                # Companion STDIO MCP Server (Keerukee.MCPServer.Stdio)
│   │       └── Program.cs
│   └── tests/
│       └── VelitIQ.Tests/                  # End-to-end xUnit test suite against real PostgreSQL
│           └── VelitIQEndToEndTests.cs
└── frontend/                               # Next.js 16 (App Router, TypeScript, Tailwind CSS)
    └── src/
        ├── app/ (layout.tsx, page.tsx)
        ├── components/ (LoginView, DashboardView, OrganizationHubView, KnowledgeVaultView, AgenticCopilotView, GovernanceAdminView)
        └── lib/ (types.ts, api.ts)
```

---

## 2. Complete REST API Surface (`VelitIQ.Api`)

| Method & Route | Auth | Purpose |
| :--- | :--- | :--- |
| `GET /api/health` | Public | Live diagnostics: PostgreSQL status, `pgvector` mode, AI provider mode, auth mode, MCP package versions. |
| `GET /api/auth/personas` | Public | Lists demo RBAC personas when `Auth:EnableDemoPersonaSelector = true`. |
| `POST /api/auth/login` | Public | Authenticates via On-Prem AD/LDAP Bind (`System.DirectoryServices.Protocols`) or Local PBKDF2 account; returns Bearer JWT. |
| `POST /api/auth/windows-sso` | Windows Auth | Exchanges IIS/Windows Negotiate identity & AD groups for a normalized VelitIQ Bearer JWT. |
| `POST /api/auth/demo-login` | Dev/UAT | One-click persona sign-in (disabled when `EnableDemoPersonaSelector = false`). |
| `GET /api/auth/me` | Bearer JWT | Returns current user profile, role, and accessible `Team` IDs. |
| `GET /api/dashboard` | Bearer JWT | Scoped December exit readiness KPIs, format breakdown, and upcoming departures. |
| `GET / POST /api/organization/departments` | Bearer / Admin | Lists or creates bank Departments (`OPS`, `TECH`, `RISK`, etc.). |
| `GET / POST /api/organization/teams` | Bearer / Admin | Lists or creates Teams with `AdGroupName` and `EntraGroupId` mappings. |
| `GET / POST /api/departing-users` | Bearer / Lead | Lists or registers departing employees with exit dates & successors. |
| `GET /api/departing-users/{id}` | Bearer JWT | Returns departing employee dossier, RBAC-filtered documents, and handover gap Q&As. |
| `GET /api/documents` | Bearer JWT | Lists documents authorized by `SecurityContext.CanReadResource(...)`. |
| `POST /api/documents/upload` | Bearer (Write) | Multipart upload (`file`, `departingUserId`, `title`, `category`, `visibility`, `tags`) triggering full ingestion. |
| `GET /api/documents/{id}` | Bearer JWT | Returns document metadata, contextual chunks, spreadsheet schemas, and secret alerts (`403` if unauthorized). |
| `GET /api/documents/{id}/download` | Bearer JWT | Downloads original binary artifact and records `DOC_DOWNLOAD` audit log. |
| `PATCH /api/documents/{id}/visibility` | Bearer (Write) | Updates visibility (`TeamOnly`, `DepartmentShared`, `BankWideShared`) across document, chunks, and sheets. |
| `GET /api/gaps` & `POST /api/gaps/{id}/answer` | Bearer JWT | Lists AI handover gap questions or saves an answer and immediately indexes it into Hybrid RAG. |
| `GET /api/spreadsheets` & `POST /api/spreadsheets/query` | Bearer JWT | Lists authorized CSV/Excel sheets and executes filter/group/aggregate (`sum`, `avg`, `min`, `max`, `count`) + formula queries. |
| `POST /api/search/hybrid` | Bearer JWT | Executes direct Hybrid Vector + Lexical RRF search. |
| `POST /api/chat/ask` | Bearer JWT | Invokes `AgenticRagOrchestrator` over `Mcp.Client.Net` -> `/mcp`. |
| `GET /api/admin/audit-logs` & `/secret-findings` | Admin / Auditor | Governance endpoints for immutable audit trail and credential leak alerts. |

---

## 3. Complete MCP Server Surface (`/mcp` via `McpHttpServer v1.2.0`)

Defined in `backend/src/VelitIQ.Mcp/VelitIQKnowledgeMcpHandler.cs`:

### MCP Tools (`[McpTool]`)
1. **`search_hybrid_knowledge`** (`[McpAuthorize(nameof(RequireAuthenticatedStaff))]`): Hybrid Vector + PostgreSQL `tsvector` RRF search with optional `departmentId`, `teamId`, `departingUserId`, `topK`.
2. **`list_accessible_spreadsheets`** (`[McpAuthorize(nameof(RequireAuthenticatedStaff))]`): Lists authorized CSV/Excel sheets, column schemas, numeric columns, and formula counts.
3. **`query_tabular_sheet`** (`[McpAuthorize(nameof(RequireAuthenticatedStaff))]`): Runs row filtering and numeric aggregations (`sum`, `avg`, `min`, `max`, `count`) + returns Excel formulas.
4. **`read_document_section`** (`[McpAuthorize(nameof(RequireAuthenticatedStaff))]`): Reads sequential contextual chunks from an authorized document.
5. **`get_departing_user_dossier`** (`[McpAuthorize(nameof(RequireAuthenticatedStaff))]`): Fetches December readiness scores, owned systems, schedules, and executive dossiers.
6. **`governance_audit_overview`** (`[McpAuthorize(nameof(RequireGlobalAdmin))]`): Admin-only summary of unacknowledged secret warnings and recent audit logs.

### MCP Resources & Templates (`[McpResource]` / `[McpResourceTemplate]`)
* `velitiq://platform/status` (Public JSON telemetry)
* `velitiq://teams/{teamSlug}/handover-pack` (Authenticated team handover inventory)
* `velitiq://users/{userId}/dossier` (Authenticated employee Markdown handover dossier)

### MCP Prompts (`[McpPrompt]`)
* `successor_onboarding_plan(departingUserName, teamName)`
* `december_exit_gap_audit(teamName)`

---

## 4. Common Office Customization Recipes

### Recipe A: Adding a New Document Format Parser (e.g., `.msg` Email or `.md` Markdown)
1. Note: `ArtifactFormat` enum in `VelitIQ.Core/Models/DomainModels.cs` is intentionally named `ArtifactFormat` (not `DocumentFormat`) to avoid namespace collision with `DocumentFormat.OpenXml`.
2. Add the new enum value to `ArtifactFormat` and extension mapping in `KnowledgeIngestionService.DetectFormat(fileName)`.
3. Implement `IDocumentFormatParser` in `VelitIQ.Infrastructure/Parsers/` and register `services.AddSingleton<IDocumentFormatParser, YourNewParser>()` in `InfrastructureServiceExtensions.cs`.

### Recipe B: Adding a New MCP Tool
1. Add an `async Task<string>` method decorated with `[McpAuthorize(nameof(RequireAuthenticatedStaff))]` and `[McpTool("tool_name", "description", ReadOnlyHint = true)]` inside `VelitIQKnowledgeMcpHandler.cs`.
2. Always call `var caller = await ResolveCallerSecurityContextAsync(ct);` at the top of the method to enforce bank Chinese-wall RBAC!
3. Both external MCP clients (Devin Desktop, Cursor) and `AgenticRagOrchestrator` (`Mcp.Client.Net` `ListToolsAsync()`) will automatically discover and use the new tool with zero changes to `AgenticRagOrchestrator.cs` when running against Azure AI Foundry.

### Recipe C: Changing Embedding Dimensions (e.g., `text-embedding-3-large` at `3072` dims)
1. Update `Database:EmbeddingDimensions` and `AzureFoundry:EmbeddingDimensions` in `appsettings.json` (e.g., `3072`) before indexing production documents.
2. Both `AzureFoundryAiService`, `LocalDeterministicAiService`, and `PostgresKnowledgeStore` (`vector(N)`) read this setting dynamically.
