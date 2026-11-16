# Security, Chinese-Wall RBAC & Banking Governance

## 1. Access Control Matrix (`Department -> Team -> User`)

| Role | Own Team (`TeamOnly`) | Same Dept (`DepartmentShared`) | Bank-Wide (`BankWideShared`) | Other Teams (`TeamOnly`) | Admin & Audit Console |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Employee** | Read / Upload / Answer Gaps | Read / Query | Read / Query | Denied (`403`) | Denied |
| **TeamLead** | Full Team Read / Write / Visibility Toggle | Read / Query | Read / Query | Denied (`403`) | Denied |
| **CrossTeamViewer** *(Audit / Risk)* | Read / Query | Read / Query | Read / Query | **Read / Query All** | Read Audit & Alerts |
| **Admin** *(Global)* | **Full Read / Write** | **Full Read / Write** | **Full Read / Write** | **Full Read / Write** | **Full Admin CRUD** |

## 2. Multi-Layer Enforcement
1. **REST Layer (`VelitIQ.Api`):** Every document listing, chunk inspection, download, gap question, and spreadsheet query validates `SecurityContext.CanReadResource(departmentId, teamId, visibility)`. Unauthorized direct ID access returns `403 Forbidden` and writes an immutable `RBAC_DOCUMENT_ACCESS_DENIED` audit entry.
2. **MCP Protocol Layer (`McpHttpServer` `v1.2.0`):**
   - `[McpAuthorize(nameof(RequireAuthenticatedStaff))]` and `[McpAuthorize(nameof(RequireGlobalAdmin))]` compile to zero-reflection startup delegates, hiding restricted tools from `tools/list` and blocking unauthorized `tools/call` or `resources/read` requests (`-32001`).
   - Inside every MCP tool, `PostgresKnowledgeStore` filters candidate vectors and `JSONB` spreadsheet sheets against the caller's JWT claims before ranking.

## 3. Upload Credential & PII Guardrails
`SecretGuardrailScanner` inspects extracted text and spreadsheet cells during ingestion for:
- Database/storage connection strings (`Password=`, `AccountKey=`, `SharedAccessKey=`)
- Cloud/AI tokens (`sk-...`, `AIza...`, `ghp_...`)
- Private keys (`-----BEGIN ... PRIVATE KEY-----`)
- Hardcoded secret assignments and SSN patterns

Matches are stored only as **redacted previews** (`Pass****26!`) in `secret_scan_findings` for Admin review and acknowledgement.
