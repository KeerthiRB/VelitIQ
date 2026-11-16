# 02. System Architecture, Data Flows & Database Design

**Document Classification:** Enterprise Architecture & Engineering Specification  
**Technology Stack:** `.NET 10.0` (ASP.NET Core), `Next.js 16` (React / TypeScript / Tailwind CSS), `PostgreSQL 17`, `Keerthi_RB` MCP NuGet Ecosystem  

---

## 1. End-to-End Component Architecture

Horizon VelitIQ follows a **Clean Modular Architecture** hosting both a high-performance REST API (for binary multipart uploads and portal administration) and a standards-compliant **Model Context Protocol (MCP) Streamable HTTP Server** (`/mcp`) inside a unified `.NET 10` deployment unit—eliminating microservice sprawl and message-broker dependencies on Windows Server.

```mermaid
flowchart TB
    subgraph Presentation["Presentation & External AI Clients"]
        WebUI["VelitIQ Web Portal\n(Next.js 16 / React / TypeScript)"]
        ExtMCP["External MCP Hosts\n(Devin Desktop, Cursor, Claude, MCP Inspector)"]
        DesktopIDE["Local CLI / IDE MCP Hosts\n(VS Code, Claude Desktop)"]
    end

    subgraph ApiHost["VelitIQ.Api (.NET 10 ASP.NET Core Host — Port 5180)"]
        AuthMW["JWT & Identity Middleware\n(AD/LDAP, Windows SSO, Local, Entra ID)"]
        RestAPI["REST Endpoint Layer\n(/api/auth, /api/documents, /api/gaps, /api/chat, /api/admin)"]
        Orchestrator["AgenticRagOrchestrator\n(Powered by Mcp.Client.Net v1.0.1)"]
        McpServer["Embedded MCP HTTP Server (/mcp)\n(Powered by McpHttpServer v1.2.0 + [McpAuthorize])"]
    end

    subgraph CompanionCLI["VelitIQ.Mcp.Cli (.NET 10 Console)"]
        StdioHost["STDIO MCP Server\n(Powered by Keerukee.MCPServer.Stdio v1.0.3)"]
    end

    subgraph CoreInfra["VelitIQ.Infrastructure (Pluggable Services)"]
        Ingest["KnowledgeIngestionService\n(Contextual Chunking + Readiness Scoring)"]
        Parsers["Format-Specific Parsers\n(Excel ClosedXML, CSV RFC4180, Word OpenXML, PDF PdfPig)"]
        Scanner["SecretGuardrailScanner\n(Regex Credential & PII Detection)"]
        AiRouter["CompositeAiService\n(AzureFoundryAiService + LocalDeterministicAiService)"]
        BlobRouter["IFileStorageService\n(LocalDisk / UNC Share OR Azure Blob Storage)"]
    end

    subgraph Persistence["PostgreSQL 17 Unified Knowledge Store"]
        OrgTables["Relational Governance\n(departments, teams, app_users, departing_users, audit_logs)"]
        ChunkTable["Hybrid RAG Store (document_chunks)\n(pgvector HNSW / .NET 10 SIMD + tsvector GIN FTS)"]
        TabTables["Structured Spreadsheet Store\n(tabular_sheets + tabular_rows JSONB + Excel Formulas)"]
    end

    WebUI -->|"HTTPS REST + Bearer JWT"| AuthMW
    AuthMW --> RestAPI
    RestAPI -->|"Upload / Admin / Gaps"| Ingest
    RestAPI -->|"POST /api/chat/ask"| Orchestrator
    Orchestrator -->|"Streamable HTTP JSON-RPC\n+ Caller Bearer JWT"| McpServer
    ExtMCP -->|"Streamable HTTP JSON-RPC (/mcp)\n+ Bearer JWT"| AuthMW
    AuthMW --> McpServer
    DesktopIDE -->|"stdin / stdout JSON-RPC"| StdioHost

    Ingest --> Parsers
    Ingest --> Scanner
    Ingest --> AiRouter
    Ingest --> BlobRouter
    Ingest --> Persistence

    Orchestrator --> AiRouter
    McpServer --> Persistence
    McpServer --> AiRouter
    StdioHost --> Persistence
```

---

## 2. Role of the `Keerthi_RB` NuGet Ecosystem

VelitIQ consumes three zero-dependency `.NET 10` libraries published under [`https://www.nuget.org/profiles/Keerthi_RB`](https://www.nuget.org/profiles/Keerthi_RB):

1. **`McpHttpServer` (`v1.2.0`)** in `VelitIQ.Mcp` & `VelitIQ.Api`:
   - Exposes `/mcp` over **Streamable HTTP**, negotiating MCP protocol versions `2024-11-05` through `2026-07-28` automatically.
   - Exposes `/.well-known/oauth-protected-resource` (RFC 9728) for OAuth 2.0 discovery.
   - Compiles `[McpAuthorize]` callbacks (`RequireAuthenticatedStaff`, `RequireGlobalAdmin`) into zero-reflection startup delegates that dynamically filter `tools/list` and guard `tools/call`, `resources/read`, and `prompts/get`.
   - Enables the **SEP-2663 Tasks Extension** (`io.modelcontextprotocol/tasks`).
2. **`Mcp.Client.Net` (`v1.0.1`)** in `VelitIQ.Mcp`:
   - Used by `AgenticRagOrchestrator` (`McpClientBuilder.ForStreamableHttp(...)`) to connect to `/mcp` with the logged-in user's JWT header.
   - Dynamically discovers authorized tools (`ListToolsAsync`), resolves prompt templates (`GetPromptAsync`), and executes multi-turn tool calls (`CallToolAsync`) so the web UI and external MCP clients share a single source of truth.
3. **`Keerukee.MCPServer.Stdio` (`v1.0.3`)** in `VelitIQ.Mcp.Cli`:
   - Provides a standalone STDIO server (`McpServerHost`) for local CLI and IDE integrations (`velitiq_search`, `velitiq_list_departing_staff`, `velitiq://stdio/info`).

---

## 3. Architectural Flow 1: Multi-Format Ingestion & Active Gap Elicitation

When a departing employee or team lead uploads a file (`CSV`, `Excel`, `Word`, or `PDF`), the backend executes a deterministic 7-stage pipeline:

```mermaid
sequenceDiagram
    autonumber
    actor User as Departing Staff / Lead
    participant UI as Next.js Upload Studio
    participant API as VelitIQ.Api (/api/documents/upload)
    participant Ingest as KnowledgeIngestionService
    participant Parser as Format Parser (Excel/CSV/Word/PDF)
    participant Scan as SecretGuardrailScanner
    participant AI as Azure AI Foundry (or Local Engine)
    participant PG as PostgreSQL 17

    User->>UI: Select Team -> Departing User, Visibility & File
    UI->>API: POST multipart/form-data + Bearer JWT
    API->>Ingest: Verify CanWriteToTeam(teamId) & Compute SHA-256
    Ingest->>Ingest: Persist Raw Binary via IFileStorageService
    Ingest->>Parser: Route by Extension (.xlsx, .csv, .docx, .pdf)
    Parser-->>Ingest: Chunks + Section Paths + Sheets/Rows + Formulas + Diagrams
    Ingest->>Scan: Scan Chunks for Passwords / API Keys / Private Keys
    Scan-->>PG: Save Redacted Findings (secret_scan_findings)
    Ingest->>AI: Generate Contextual Embeddings ([Dept|Team|Author|Doc|Section] + Text)
    AI-->>Ingest: 1536-dim Vectors
    Ingest->>PG: Insert document_chunks (Vector + tsvector GIN) & tabular_sheets / tabular_rows (JSONB)
    Ingest->>AI: Analyze Document for Missing Handover Context & Update Dossier
    AI-->>Ingest: Summary + Systems + Schedules + 3 Targeted Gap Questions
    Ingest->>PG: Save Gap Questions, Update Executive Dossier, Recalculate Readiness % & Write AuditLog
    API-->>UI: Return Completed KnowledgeDocument Telemetry
```

### Format-Specific Extraction Details
* **Excel (`ExcelDocumentParser.cs`):** Uses `ClosedXML` (with `ExcelDataReader` fallback for legacy `.xls`) to iterate worksheets, infer column data types (`number`, `date`, `boolean`, `string`), compute `Min`/`Max`/`Sum` metadata, extract every cell's `FormulaA1` (`=D2-E2`, `=SUM(...)`, cross-sheet references) into a dedicated `FormulaCatalog` chunk, and store every row in `tabular_rows` (`JSONB`).
* **CSV (`CsvDocumentParser.cs`):** RFC-4180 streaming parser auto-detecting `,`, `;`, `\t`, and `|` delimiters, preserving quoted multi-line values, building typed column schemas, and populating both `tabular_rows` and batch text chunks.
* **Word (`WordDocumentParser.cs`):** Uses `DocumentFormat.OpenXml` to walk paragraphs and tables in document order, tracking `Heading1`–`Heading6` breadcrumbs and converting `<w:tbl>` elements into aligned Markdown tables.
* **PDF & OneNote Exports (`PdfDocumentParser.cs`):** Uses `PdfPig` word bounding boxes (`Bottom` / `Left` coordinates) to reconstruct horizontal lines and wide side-by-side OneNote canvas columns while extracting embedded diagram/screenshot PNGs (`page.GetImages()`) for multimodal **Azure AI Foundry Vision** transcription.

---

## 4. Architectural Flow 2: Human-in-the-Loop Gap Clarification Indexing

When a departing employee answers an AI-generated Handover Gap Question (`POST /api/gaps/{id}/answer`):
1. The answer is saved on `handover_gap_questions` with author attribution and timestamp.
2. `KnowledgeIngestionService.AnswerGapQuestionAndIndexAsync` wraps the Q&A pair in a contextual header (`SourceType = "AuthorGapAnswer"`), generates its embedding vector, and inserts it directly into `document_chunks`.
3. During future queries, `SearchHybridAsync` applies a `1.35x` Reciprocal Rank Fusion priority boost to matching `AuthorGapAnswer` chunks so verified author clarifications surface prominently alongside source documents.
4. The departing employee's **Executive Dossier** and **Readiness Score (`0–100%`)** are immediately recalculated.

---

## 5. Architectural Flow 3: Agentic MCP Hybrid RAG & Tabular Execution

```mermaid
sequenceDiagram
    autonumber
    actor Successor as Successor / Analyst
    participant UI as Next.js Copilot View
    participant Orch as AgenticRagOrchestrator (Mcp.Client.Net)
    participant MCP as McpHttpServer (/mcp)
    participant AI as Azure AI Foundry (Chat + Tool Calling)
    participant PG as PostgreSQL 17

    Successor->>UI: Ask Scoped Question (Optional Dept / Team / User / Prompt Filter)
    UI->>Orch: POST /api/chat/ask + Bearer JWT
    Orch->>MCP: InitializeAsync() + ListToolsAsync() (Bearer JWT Header)
    MCP-->>Orch: Authorized Tool Schemas ([McpAuthorize] Filtered)
    loop Up to 4 Agentic Reasoning Steps
        Orch->>AI: Send Conversation + Tool Definitions + Prior Tool Outputs
        AI-->>Orch: Request Tool Calls (e.g. search_hybrid_knowledge, query_tabular_sheet)
        Orch->>MCP: CallToolAsync(toolName, arguments)
        MCP->>PG: Enforce SecurityContext.CanReadResource() -> Execute RRF Search / JSONB Aggregation
        PG-->>MCP: Grounded Chunks / Computed Sums & Formulas
        MCP-->>Orch: JSON-RPC Tool Result
    end
    AI-->>Orch: Final Grounded Markdown Answer
    Orch->>PG: Append AGENTIC_RAG_QUERY Audit Log
    Orch-->>UI: Markdown + MCP Latency Traces + Citations + Structured Table Output
```

### Hybrid Reciprocal Rank Fusion (RRF) Formula
For every candidate chunk authorized by `SecurityContext.CanReadResource(...)`, VelitIQ computes:
1. **Dense Vector Rank ($r_{\text{vec}}$):** Ranked by cosine similarity (`1 - (embedding_vec <=> query_vec)` via `pgvector` HNSW index when installed, or `.NET 10` hardware-accelerated SIMD `TensorPrimitives.CosineSimilarity` over `real[]`).
2. **Lexical Rank ($r_{\text{lex}}$):** Ranked by PostgreSQL `ts_rank_cd(search_vector, to_tsquery('english', ...))` backed by a stored `GIN` index.
3. **Fused Score ($k = 60$):**
   $$\text{Score}_{\text{RRF}} = \left(\frac{1}{60 + r_{\text{vec}}} + \frac{1.25}{60 + r_{\text{lex}}}\right) \times \beta_{\text{source}}$$
   where $\beta_{\text{source}} = 1.35$ for verified `AuthorGapAnswer` chunks and $1.0$ otherwise.

---

## 6. PostgreSQL 17 Entity-Relationship Model

```mermaid
erDiagram
    DEPARTMENTS ||--o{ TEAMS : "contains"
    TEAMS ||--o{ APP_USERS : "primary_team"
    DEPARTMENTS ||--o{ DEPARTING_USERS : "categorizes"
    TEAMS ||--o{ DEPARTING_USERS : "owns"
    DEPARTING_USERS ||--o{ KNOWLEDGE_DOCUMENTS : "authors"
    KNOWLEDGE_DOCUMENTS ||--o{ DOCUMENT_CHUNKS : "indexed_as"
    KNOWLEDGE_DOCUMENTS ||--o{ TABULAR_SHEETS : "contains_sheets"
    TABULAR_SHEETS ||--o{ TABULAR_ROWS : "stores_rows"
    DEPARTING_USERS ||--o{ HANDOVER_GAP_QUESTIONS : "elicited_for"
    KNOWLEDGE_DOCUMENTS ||--o{ SECRET_SCAN_FINDINGS : "scanned_for"

    DEPARTMENTS {
        uuid id PK
        text name
        text code
    }
    TEAMS {
        uuid id PK
        uuid department_id FK
        text name
        text slug
        text ad_group_name
        text entra_group_id
    }
    DEPARTING_USERS {
        uuid id PK
        uuid team_id FK
        text full_name
        date exit_date
        int readiness_score
        text[] systems_owned
        text executive_summary
    }
    KNOWLEDGE_DOCUMENTS {
        uuid id PK
        uuid team_id FK
        uuid departing_user_id FK
        int format
        int visibility
        int chunk_count
        int formula_count
    }
    DOCUMENT_CHUNKS {
        uuid id PK
        uuid document_id FK
        int visibility
        text source_type
        text contextual_content
        real[] embedding
        tsvector search_vector
    }
    TABULAR_SHEETS {
        uuid id PK
        uuid document_id FK
        text sheet_name
        jsonb columns_json
        jsonb formulas_json
    }
    TABULAR_ROWS {
        bigserial id PK
        uuid sheet_id FK
        jsonb row_data_json
    }
```
