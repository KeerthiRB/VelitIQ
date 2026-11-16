# GitHub Copilot & Office IDE Instructions — Horizon VelitIQ

Read [`AGENTS.md`](../AGENTS.md) and [`docs/02_SYSTEM_ARCHITECTURE_AND_FLOWS.md`](../docs/02_SYSTEM_ARCHITECTURE_AND_FLOWS.md) before proposing architectural or code changes.

## Key Guardrails
1. **Tech Stack:** `.NET 10.0` (`VelitIQ.sln`), `PostgreSQL 17` (`Dapper` + `Npgsql` + `pgvector`/SIMD + `tsvector` GIN), `Next.js 16` (`TypeScript` + `Tailwind CSS`), and `Keerthi_RB` MCP NuGet packages (`McpHttpServer v1.2.0`, `Mcp.Client.Net v1.0.1`, `Keerukee.MCPServer.Stdio v1.0.3`).
2. **Bank Chinese-Wall Security:** Always enforce `SecurityContext.CanReadResource(departmentId, teamId, visibility)` and `SecurityContext.CanWriteToTeam(teamId)` on all data access, REST endpoints, and `[McpTool]` / `[McpResourceTemplate]` methods.
3. **Office Configuration Seams:** Keep `AzureFoundryOptions` (`AzureOpenAiUrl` + `ApiKey`), `AuthOptions` (`Ldap` + `EntraId`), and `StorageOptions` (`LocalDisk` + `AzureBlob`) externalized in `appsettings.json` / environment variables—never hardcode URLs, domain controllers, or secrets.
4. **Spreadsheet Integrity:** Keep dual-path ingestion intact (`document_chunks` for semantic/lexical discovery + `tabular_sheets`/`tabular_rows` for exact aggregations and Excel formula inspection).
