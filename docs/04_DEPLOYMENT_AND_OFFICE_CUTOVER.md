# 04. Office Network Setup, Windows Server Deployment & Azure Cutover Guide

**Document Classification:** DevOps, Infrastructure & Office Cutover Runbook  
**Target Environments:** Corporate Office Workstation, On-Premises Windows Server (IIS / Windows Service), Microsoft Azure  

---

## 1. Bringing the Codebase into the Bank Office Network

When cloning `https://github.com/KeerthiRB/VelitIQ.git` onto an office machine or On-Premises Windows Server:

### Prerequisites
1. **.NET 10 SDK / Hosting Bundle (`x64`)**
2. **Node.js 20 LTS or 22 LTS (`x64`)**
3. **PostgreSQL 17 (`x64`)** — Either standard EnterpriseDB Windows Service (`C:\Program Files\PostgreSQL\17`) or user-space setup via `.\scripts\setup-local-postgres.ps1`.

### Step 1: Configure Azure AI Foundry (`appsettings.json` or Environment Variables)
Open `backend/src/VelitIQ.Api/appsettings.json` and populate the `AzureFoundry` section with your bank-approved **Azure AI Foundry / Azure OpenAI** endpoint and API key:

```json
"AzureFoundry": {
  "Mode": "AzureFoundry",
  "AzureOpenAiUrl": "https://YOUR-BANK-FOUNDRY-RESOURCE.openai.azure.com",
  "ApiKey": "YOUR-AZURE-FOUNDRY-API-KEY",
  "ApiVersion": "2024-10-21",
  "ChatDeployment": "gpt-4o",
  "VisionDeployment": "gpt-4o",
  "EmbeddingDeployment": "text-embedding-3-small",
  "EmbeddingDimensions": 1536
}
```
* **Automatic Mode Behavior:** When `"Mode": "Auto"` (default), `CompositeAiService` automatically uses **Azure AI Foundry** as soon as `AzureOpenAiUrl` and `ApiKey` are non-empty, and falls back to `LocalDeterministicAiService` on disconnected developer laptops. Setting `"Mode": "AzureFoundry"` enforces cloud-only execution in production.
* **Environment Variable Override (Recommended for Windows Server):** Instead of storing secrets in `appsettings.json`, set Windows Machine/Service environment variables:
  * `AzureFoundry__Mode=AzureFoundry`
  * `AzureFoundry__AzureOpenAiUrl=https://...`
  * `AzureFoundry__ApiKey=...`
  * `Database__ConnectionString=Host=127.0.0.1;Port=5432;Database=velitiq;Username=velitiq_svc;Password=...`
  * `Auth__JwtSigningKey=<64+ char random production secret>`

---

## 2. Configuring On-Premises Active Directory & LDAP

To authenticate bank employees against your corporate Active Directory Domain Controller, update `Auth` in `appsettings.json`:

```json
"Auth": {
  "Mode": "HybridAdAndLocal",
  "EnableDemoPersonaSelector": false,
  "Ldap": {
    "Enabled": true,
    "Host": "dc01.corp.yourbank.com",
    "Port": 389,
    "UseSsl": false,
    "Domain": "YOURBANK",
    "BaseDn": "DC=corp,DC=yourbank,DC=com",
    "AdminGroupCn": "CN=SG-VelitIQ-Admins",
    "CrossTeamGroupCn": "CN=SG-VelitIQ-CrossTeamViewers"
  }
}
```
* **`"Mode": "HybridAdAndLocal"`** attempts Active Directory LDAP bind first and permits fallback to local bootstrap Admin accounts if needed. Switch to **`"Mode": "ActiveDirectoryOnly"`** once AD group mappings (`CN=SG-VelitIQ-...`) are verified in the **Governance & Audit** UI tab.
* **LDAPS (Port 636):** Set `"Port": 636` and `"UseSsl": true` when connecting over TLS to the domain controller.
* **Windows Integrated Domain SSO:** When hosted behind IIS with **Windows Authentication** enabled, clicking *"Sign In with Windows Integrated Domain SSO"* on the login screen authenticates the user's domain Kerberos/NTLM token automatically via `/api/auth/windows-sso`.

---

## 3. PostgreSQL 17 & `pgvector` on Windows Server

1. Install **PostgreSQL 17** on the Windows Server and create database `velitiq`:
   ```sql
   CREATE DATABASE velitiq ENCODING 'UTF8';
   ```
2. On first startup, `VelitIQ.Api` automatically creates all tables, foreign keys, `JSONB` GIN indexes, and `tsvector` full-text search indexes.
   * **Note on Seed Data:** For a clean production database without demo employees/files, set `"Database:AutoMigrateAndSeed": false` in production `appsettings.json` (schema initialization still runs automatically; only demo seeding is skipped).
3. **Optional Native `pgvector` Installation on Windows Server:**
   * **Out-of-the-Box Behavior:** Even if `pgvector` (`vector.dll`) is *not* installed on the Windows Server, VelitIQ runs with zero errors using PostgreSQL `real[]` + `.NET 10` hardware-accelerated SIMD `TensorPrimitives.CosineSimilarity` + `tsvector` GIN full-text RRF.
   * **Enabling Native HNSW Index:** If Visual Studio C++ Build Tools are available on a build machine, run `.\scripts\install-pgvector-windows.ps1` and restart PostgreSQL—VelitIQ auto-detects `CREATE EXTENSION vector` and builds the `HNSW` cosine index automatically.

---

## 4. Publishing & Hosting on On-Premises Windows Server

Run the included release packaging script:
```powershell
.\scripts\publish-windows-server.ps1
```

### Backend Hosting (`publish\VelitIQ.Api`)
* **Option A — Native Windows Service:**
  ```powershell
  sc.exe create "HorizonVelitIQApi" binPath= "D:\VelitIQ\VelitIQ.Api\VelitIQ.Api.exe --urls http://0.0.0.0:5180" start= auto
  sc.exe start "HorizonVelitIQApi"
  ```
* **Option B — IIS Reverse Proxy / In-Process Hosting:**
  1. Install the **.NET 10 Windows Server Hosting Bundle**.
  2. Create an IIS Application Pool with **.NET CLR Version = `No Managed Code`**.
  3. Point the IIS Site to `D:\VelitIQ\VelitIQ.Api` and add your corporate HTTPS certificate + frontend URL under `Cors:AllowedOrigins`.

### Frontend Hosting (`frontend`)
* Set `NEXT_PUBLIC_API_BASE_URL=https://velitiq-api.corp.yourbank.com` prior to `npm run build`, and run Next.js (`npx next start -p 3000`) as a Windows service (via NSSM / PM2 / IIS ARR reverse proxy).

---

## 5. Connecting Devin Desktop, Cursor, Claude & MCP Inspector

External MCP clients inside the bank network can connect directly to VelitIQ:

### Streamable HTTP (`/mcp` — Recommended)
```json
{
  "mcpServers": {
    "velitiq-bank-knowledge": {
      "type": "http",
      "url": "http://<windows-server-host>:5180/mcp",
      "headers": {
        "Authorization": "Bearer <USER_JWT_TOKEN>"
      }
    }
  }
}
```

### Local STDIO (`VelitIQ.Mcp.Cli`)
```json
{
  "mcpServers": {
    "velitiq-stdio": {
      "command": "dotnet",
      "args": ["D:/VelitIQ/VelitIQ.Mcp.Cli/VelitIQ.Mcp.Cli.dll"],
      "env": {
        "VELITIQ_DB_CONNECTION": "Host=127.0.0.1;Port=5432;Database=velitiq;Username=velitiq_svc;Password=...",
        "VELITIQ_ROLE": "CrossTeamViewer"
      }
    }
  }
}
```

---

## 6. Zero-Rewrite Azure Migration Checklist (Post-Approval)

Once Azure hosting approval is granted:
1. **Database:** Provision **Azure Database for PostgreSQL Flexible Server**, enable `VECTOR` under `azure.extensions`, restore `pg_dump` from Windows Server, and update `Database:ConnectionString`.
2. **File Storage:** Provision an **Azure Storage Account**, set `"Storage:Provider": "AzureBlob"`, `"Storage:AzureBlobConnectionString"`, and copy existing files from `storage_data/` into container `velitiq-knowledge-artifacts`.
3. **Authentication:** Register the VelitIQ App Registration in **Microsoft Entra ID**, set `"Auth:EntraId:Enabled": true` (`TenantId`, `ClientId`, `Audience`), and verify each Team's `EntraGroupId` in the Governance Console.
