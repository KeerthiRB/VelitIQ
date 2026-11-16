# Horizon VelitIQ — Enterprise Banking Knowledge Continuity & Agentic MCP Platform

[![.NET 10.0](https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![Next.js](https://img.shields.io/badge/Next.js-16-000000?style=flat-square&logo=next.js)](https://nextjs.org/)
[![PostgreSQL 17](https://img.shields.io/badge/PostgreSQL-17-336791?style=flat-square&logo=postgresql)](https://www.postgresql.org/)
[![MCP Powered](https://img.shields.io/badge/MCP-Keerthi__RB%20Suite-4F46E5?style=flat-square)](https://www.nuget.org/profiles/Keerthi_RB)

**Horizon VelitIQ** is an enterprise-grade knowledge capture, offboarding governance, and **Agentic Hybrid RAG** platform purpose-built for collecting, categorizing (`Department -> Team -> Departing Employee`), auditing, and querying institutional knowledge from employees departing the bank.

---

## Key Capabilities

1. **Hierarchical `Department -> Team -> User` Knowledge Taxonomy & RBAC**
   - Strictly isolates **`TeamOnly`** sensitive artifacts (Chinese walls across *Treasury Ops*, *Core Banking Tech*, and *Quantitative Risk*), while supporting **`DepartmentShared`** and **`BankWideShared`** cross-team artifacts plus **`Admin`** and **`CrossTeamViewer`** (Audit/Risk) oversight roles.
2. **Dual-Path Multi-Format Ingestion (`CSV`, `Excel`, `Word`, `PDF / OneNote Exports`)**
   - **Excel (`.xlsx`/`.xls`) & CSV (`.csv`):** Extracts worksheets, infers column schemas, captures **underlying Excel formulas (`=SUM(...)`, `=D2-E2`, cross-sheet references)**, and loads rows into PostgreSQL `JSONB` tables for **exact `SUM`/`AVG`/`MIN`/`MAX` SQL queries** (eliminating vector-RAG number hallucinations).
   - **Word (`.docx`):** Preserves heading hierarchies (`H1 -> H2 -> H3` breadcrumbs) and converts Word tables into clean Markdown tables.
   - **PDF (`.pdf` & OneNote-exported PDFs):** Reconstructs spatial horizontal/column layouts, counts embedded diagrams/screenshots, and supports multimodal **Azure AI Foundry Vision** diagram analysis.
3. **Active AI Handover Gap Elicitation ("Offboarding Interviewer")**
   - Automatically inspects uploaded files to generate an **Executive Handover Dossier**, compute a **December Exit Readiness Score (`0–100%`)**, and ask **targeted clarification questions** about undocumented formulas, acronyms, or batch failure steps. Answered clarifications are immediately embedded and indexed into Hybrid RAG as `AuthorGapAnswer` chunks.
4. **Agentic Hybrid RAG Built on [`Keerthi_RB`](https://www.nuget.org/profiles/Keerthi_RB) NuGet Packages**
   - Combines **Dense Vector Cosine Similarity** (`pgvector` HNSW index when installed, with automatic `.NET 10` hardware-accelerated SIMD `TensorPrimitives.CosineSimilarity` fallback on stock Windows PostgreSQL) + **PostgreSQL `tsvector` GIN Full-Text Lexical Ranking** fused via **Reciprocal Rank Fusion (RRF)**.
5. **Sensitive Credential / PII Guardrail Scanner & Immutable Bank Audit Trail**
   - Flags leaked connection strings, API keys, private keys, or SSN patterns on upload and logs all authentication, ingestion, download, RBAC denial, and MCP tool events.

---

## `Keerthi_RB` NuGet Ecosystem Integration

| Package | Version | Project | Architectural Role |
| :--- | :--- | :--- | :--- |
| [`McpHttpServer`](https://www.nuget.org/packages/McpHttpServer) | `1.2.0` | `VelitIQ.Mcp` / `VelitIQ.Api` | Exposes `/mcp` Streamable HTTP endpoint (`2024-11-05` through `2026-07-28`), `[McpTool]`, `[McpResourceTemplate]`, `[McpPrompt]`, SEP-2663 Tasks, RFC 9728 OAuth 2.0 discovery, and zero-reflection `[McpAuthorize]` RBAC callbacks. |
| [`Mcp.Client.Net`](https://www.nuget.org/packages/Mcp.Client.Net) | `1.0.1` | `VelitIQ.Mcp` | Powers `AgenticRagOrchestrator` over `StreamableHttpClientTransport`, propagating caller Bearer JWTs to dynamically discover (`ListToolsAsync`) and invoke (`CallToolAsync`, `GetPromptAsync`, `ReadResourceAsync`) authorized MCP tools. |
| [`Keerukee.MCPServer.Stdio`](https://www.nuget.org/packages/Keerukee.MCPServer.Stdio) | `1.0.3` | `VelitIQ.Mcp.Cli` | Standalone STDIO MCP server enabling Claude Desktop, Cursor, VS Code, or Devin CLI agents to query authorized PostgreSQL handover packs directly. |

---

## Quick Start (Local Development — Zero Cloud Keys Required)

### Prerequisites
- **.NET 10 SDK** (`x64`)
- **Node.js 20+**
- **PostgreSQL 17** (automatically downloaded & initialized in user-space by `scripts/setup-local-postgres.ps1` if not already installed)

### Run in One Command (PowerShell)
```powershell
.\scripts\start-dev.ps1
```
- **Next.js Portal:** `http://localhost:3000`
- **.NET 10 REST API:** `http://127.0.0.1:5180/api/health`
- **Streamable HTTP MCP Endpoint:** `http://127.0.0.1:5180/mcp`
- **OAuth 2.0 Protected Resource Metadata:** `http://127.0.0.1:5180/.well-known/oauth-protected-resource`

> **Note on Laptop vs. Office AI Mode:** When `AzureFoundry:ApiKey` is blank, `CompositeAiService` automatically runs in **Local Deterministic Mode** so every ingestion, hybrid search, spreadsheet aggregation, and `Mcp.Client.Net <-> McpHttpServer` workflow executes end-to-end offline.

---

## Office Configuration (Azure AI Foundry + On-Prem Active Directory)

When deploying on the office workstation or **On-Prem Windows Server**, update `backend/src/VelitIQ.Api/appsettings.json` (or environment variables):

```json
{
  "Database": {
    "ConnectionString": "Host=127.0.0.1;Port=5432;Database=velitiq;Username=velitiq_svc;Password=STRONG_DB_PASSWORD"
  },
  "AzureFoundry": {
    "Mode": "AzureFoundry",
    "AzureOpenAiUrl": "https://YOUR-FOUNDRY-RESOURCE.openai.azure.com",
    "ApiKey": "YOUR-AZURE-FOUNDRY-API-KEY",
    "ApiVersion": "2024-10-21",
    "ChatDeployment": "gpt-4o",
    "VisionDeployment": "gpt-4o",
    "EmbeddingDeployment": "text-embedding-3-small",
    "EmbeddingDimensions": 1536
  },
  "Auth": {
    "Mode": "HybridAdAndLocal",
    "EnableDemoPersonaSelector": false,
    "Ldap": {
      "Enabled": true,
      "Host": "dc01.yourbank.internal",
      "Port": 389,
      "UseSsl": false,
      "Domain": "YOURBANK",
      "BaseDn": "DC=yourbank,DC=internal",
      "AdminGroupCn": "CN=SG-VelitIQ-Admins",
      "CrossTeamGroupCn": "CN=SG-VelitIQ-CrossTeamViewers"
    }
  }
}
```

See detailed guides in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/DEPLOYMENT_WINDOWS_AND_AZURE.md`](docs/DEPLOYMENT_WINDOWS_AND_AZURE.md), and [`docs/SECURITY_AND_GOVERNANCE.md`](docs/SECURITY_AND_GOVERNANCE.md).

---

## Running Automated Tests

```powershell
dotnet test VelitIQ.sln
```
Runs full integration and E2E tests against PostgreSQL (`velitiq_test`), verifying all 4 document parsers (`CSV`, `Excel` formulas, `Word` OpenXML, `PDF` OneNote exports), Chinese-wall RBAC isolation, live `Mcp.Client.Net <-> McpHttpServer` `[McpAuthorize]` tool/resource/prompt execution, and gap-answer RAG re-indexing.
