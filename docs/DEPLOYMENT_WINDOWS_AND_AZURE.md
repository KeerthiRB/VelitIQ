# Deployment Guide — On-Prem Windows Server Now & Azure Cutover Later

## Phase A: On-Prem Windows Server Deployment (Pre-Cloud Approval)

### 1. Install PostgreSQL 17 on Windows Server
1. Install official EnterpriseDB **PostgreSQL 17.x Windows x64** (`C:\Program Files\PostgreSQL\17`) as a Windows Service, or run `.\scripts\setup-local-postgres.ps1` for portable initialization.
2. Create database `velitiq` (VelitIQ automatically creates all tables, GIN indexes, and seed structures on startup).
3. *(Optional)* To enable native `pgvector` HNSW indexing on Windows Server, run `.\scripts\install-pgvector-windows.ps1` from an Administrator VS 2022 x64 Native Tools prompt. Even without `vector.dll`, VelitIQ runs out-of-the-box using PostgreSQL `real[]` + `.NET 10` SIMD cosine similarity + `tsvector` GIN full-text search.

### 2. Build Release Artifacts
```powershell
.\scripts\publish-windows-server.ps1
```
Produces:
- `publish\VelitIQ.Api` (ASP.NET Core `.NET 10` API + `/mcp` `McpHttpServer`)
- `publish\VelitIQ.Mcp.Cli` (STDIO MCP Server)
- `frontend\.next` (Optimized Next.js production bundle)

### 3. Configure Azure AI Foundry & Active Directory / LDAP
Edit `publish\VelitIQ.Api\appsettings.json`:
- Set `AzureFoundry:AzureOpenAiUrl`, `AzureFoundry:ApiKey`, `ChatDeployment`, and `EmbeddingDeployment`.
- Set `Auth:Ldap:Enabled = true` with your domain controller (`Host`, `Domain`, `BaseDn`) and map each team's Active Directory Security Group `CN=...` in the VelitIQ Governance Console.
- Set `Auth:EnableDemoPersonaSelector = false` for production.
- Set `Storage:LocalRootPath` to a dedicated disk or backed-up bank UNC file share (e.g., `D:\VelitIQ_Storage`).

### 4. Host as Windows Service or IIS Site
- **Windows Service:** `sc.exe create VelitIQApi binPath= "D:\Apps\VelitIQ.Api\VelitIQ.Api.exe --urls http://0.0.0.0:5180" start= auto`
- **IIS:** Install the **.NET 10 Windows Server Hosting Bundle**, point an IIS Site (`No Managed Code`) to `publish\VelitIQ.Api`, and enable **Windows Authentication** if using `/api/auth/windows-sso`.

---

## Phase B: Zero-Rewrite Azure Migration (Post-Approval)

When Azure deployment is approved, **zero C# or TypeScript code changes are required**—update only configuration:

1. **Database -> Azure Database for PostgreSQL Flexible Server:**
   - Enable `VECTOR` under *Server Parameters -> `azure.extensions`* (`VelitIQ` automatically detects `pgvector` and builds the `HNSW` cosine index).
   - Update `Database:ConnectionString`.
2. **Storage -> Azure Blob Storage:**
   - Set `"Storage:Provider": "AzureBlob"` and `"Storage:AzureBlobConnectionString"`.
3. **Identity -> Microsoft Entra ID:**
   - Set `"Auth:EntraId:Enabled": true`, `"TenantId"`, `"ClientId"`, and `"Audience"`, and populate each Team's `EntraGroupId` in the Governance Console.
