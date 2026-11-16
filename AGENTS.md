# AGENTS.md — Horizon VelitIQ Context for Office IDEs & AI Coding Agents

This document provides complete domain, architectural, and engineering context for AI coding assistants (Antigravity, Devin, Cursor, GitHub Copilot, Claude Code, Visual Studio) working on **Horizon VelitIQ** inside the bank's corporate environment.

---

## 1. Mission & Business Context

**Horizon VelitIQ** is an enterprise banking knowledge continuity and **Agentic Hybrid RAG** application built to capture, categorize, audit, and query institutional knowledge from employees departing the bank (specifically targeting December departures).

### Core Domain Invariants (Never Violate These)
1. **Strict `Department -> Team -> Departing User` Categorization:** Every uploaded document (`KnowledgeDocument`), contextual text chunk (`DocumentChunk`), structured spreadsheet worksheet (`TabularSheet`), and AI clarification question (`HandoverGapQuestion`) belongs to a `Department`, `Team`, and `DepartingUserProfile`.
2. **Chinese-Wall RBAC Enforcement (`SecurityContext.CanReadResource`):**
   - `VisibilityScope.TeamOnly` (default): Readable **only** by members of that `Team`, `UserRole.CrossTeamViewer` (Audit/Risk), and `UserRole.Admin`.
   - `VisibilityScope.DepartmentShared`: Readable by any team in the same `Department`, plus `CrossTeamViewer` and `Admin`.
   - `VisibilityScope.BankWideShared`: Readable by any authenticated bank user.
   - **Rule:** Every new query endpoint or MCP tool MUST resolve `SecurityContext` and filter data through `CanReadResource(...)` / `CanWriteToTeam(...)`.
3. **Dual-Path Spreadsheet Handling (Never Chunk-Only CSV/Excel):**
   - `CSV` and `Excel` files must populate **both** semantic discovery chunks (`document_chunks`) **and** structured JSONB rows + Excel formulas (`tabular_sheets` / `tabular_rows`) so numerical queries use `query_tabular_sheet` instead of hallucinating from text chunks.
4. **First-Class Consumption of `Keerthi_RB` NuGet Packages:**
   - Streamable HTTP MCP Server (`/mcp`): **`McpHttpServer` (`1.2.0`)** in `VelitIQ.Mcp` / `VelitIQ.Api`.
   - Agentic Orchestrator Client: **`Mcp.Client.Net` (`1.0.1`)** in `VelitIQ.Mcp/AgenticRagOrchestrator.cs`.
   - Local Companion STDIO Server: **`Keerukee.MCPServer.Stdio` (`1.0.3`)** in `VelitIQ.Mcp.Cli`.

---

## 2. Pluggable Infrastructure Seams (On-Prem Windows Server -> Azure)

All environment differences between developer laptops, the On-Premises Windows Server, and future Microsoft Azure hosting are controlled strictly via configuration (`backend/src/VelitIQ.Api/appsettings.json` or environment variables):

- **AI Models (`IAiService` -> `CompositeAiService.cs`):**
  - Uses **`AzureFoundryAiService.cs`** (`AzureFoundry:AzureOpenAiUrl` + `AzureFoundry:ApiKey` for Chat, Vision, and Embeddings) whenever credentials are configured.
  - Automatically falls back to **`LocalDeterministicAiService.cs`** when `Mode = "Auto"` and `ApiKey` is empty, allowing full offline compilation and automated E2E testing.
- **Authentication (`IAuthService` -> `BankAuthService.cs`):**
  - Supports **On-Premises Active Directory LDAP Bind** (`Auth:Ldap:Enabled = true` via `System.DirectoryServices.Protocols`), **Windows Integrated SSO** (`/api/auth/windows-sso`), **Local PBKDF2 Accounts**, and **Microsoft Entra ID OIDC** (`Auth:EntraId:Enabled = true`).
- **Database (`PostgresKnowledgeStore.cs`):**
  - Targets **PostgreSQL 17**. Automatically detects whether the `pgvector` extension (`CREATE EXTENSION IF NOT EXISTS vector`) is installed:
    - If available (e.g., Azure Database for PostgreSQL Flexible Server or Windows PG with `vector.dll`), uses native `vector(1536)` + `HNSW` cosine index.
    - If unavailable on a stock Windows PostgreSQL instance, uses `real[]` + `.NET 10` hardware-accelerated SIMD `TensorPrimitives.CosineSimilarity` + PostgreSQL `tsvector` GIN full-text search fused via Reciprocal Rank Fusion (RRF).
- **File Storage (`IFileStorageService`):**
  - Switches between `LocalDiskFileStorageService` (`Storage:Provider = "LocalDisk"`) and `AzureBlobFileStorageService` (`Storage:Provider = "AzureBlob"`).

---

## 3. Important Technical Conventions

- **`.NET 10` x64 SDK:** Ensure 64-bit `dotnet` (`C:\Program Files\dotnet\dotnet.exe` on Windows) is used rather than `Program Files (x86)`.
- **Enum Naming Note:** The file-type enum in `VelitIQ.Core.Models` is named `ArtifactFormat` (`Pdf`, `Word`, `Excel`, `Csv`)—do **not** rename it to `DocumentFormat`, as `DocumentFormat` collides with the root namespace of `DocumentFormat.OpenXml`.
- **Documentation Suite:** Always keep the 5 architecture & management documents in `docs/01_...` through `docs/05_...` synchronized when modifying schemas, endpoints, or security policies.

---

## 4. Build, Run & Test Commands

```powershell
# 1. Build entire .NET 10 solution (0 warnings, 0 errors expected)
dotnet build VelitIQ.sln

# 2. Run xUnit Integration & MCP E2E Tests (requires PostgreSQL running on localhost:5432 with DB 'velitiq_test')
dotnet test VelitIQ.sln

# 3. Build Next.js Frontend (TypeScript + Production Bundle Check)
cd frontend
npm run build

# 4. Launch Full Local Stack (PostgreSQL 17 + .NET 10 API/MCP on :5180 + Next.js on :3000)
.\scripts\start-dev.ps1
```
