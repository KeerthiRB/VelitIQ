# VelitIQ — Technical Architecture & Hybrid Agentic RAG Design

## 1. Why Agentic Hybrid RAG + Structured Tabular Engine?

Traditional naive chunk-and-embed RAG fails in banking offboarding scenarios for three reasons:
1. **Spreadsheets (`CSV` & `Excel`) lose relational and formula semantics** when sliced into token chunks.
2. **Exact banking tokens** (`SWIFT_MT103`, `GL_EOD_POST_MASTER`, `PRD-GL-04`, `NOSTRO-USD-101`) require lexical ranking alongside dense semantic vectors.
3. **Tacit knowledge** lives in the departing employee's head unless actively elicited at upload time.

VelitIQ solves all three:
- **Contextual Hybrid Retrieval (`PostgresKnowledgeStore.SearchHybridAsync`)**:
  - Prepends `[Department | Team | Author | Document | Section Path | Page/Sheet]` metadata to every chunk prior to embedding.
  - Fuses **Dense Cosine Similarity** (`pgvector` HNSW `<=>` when installed, or `.NET 10` `System.Numerics.Tensors.TensorPrimitives.CosineSimilarity` hardware SIMD on stock Windows PostgreSQL) with **PostgreSQL `tsvector` GIN Full-Text Search (`ts_rank_cd`)** using **Reciprocal Rank Fusion ($k = 60$)**.
- **Structured Spreadsheet Engine (`tabular_sheets` + `tabular_rows`)**:
  - Extracts worksheet schemas, numeric min/max/sum statistics, and **Excel cell formulas (`ClosedXML` `FormulaA1`)** into PostgreSQL `JSONB`, exposed via the `query_tabular_sheet` MCP tool.
- **Active Offboarding Gap Elicitation (`KnowledgeIngestionService`)**:
  - Generates targeted clarification questions upon upload. When answered via `POST /api/gaps/{id}/answer`, the verified response is immediately embedded as an `AuthorGapAnswer` chunk with boosted RRF priority.

## 2. End-to-End MCP Protocol Flow

1. User submits a scoped question in Next.js (`AgenticCopilotView`) -> `POST /api/chat/ask`.
2. `AgenticRagOrchestrator` initializes **`Mcp.Client.Net` (`v1.0.1`)** over `StreamableHttpClientTransport` against `/mcp` (`McpHttpServer` `v1.2.0`), forwarding the user's signed Bearer JWT.
3. `McpHttpServer` evaluates compiled **`[McpAuthorize]`** delegates (`RequireAuthenticatedStaff`, `RequireGlobalAdmin`) to dynamically filter `tools/list` and guard `tools/call`, `resources/read`, and `prompts/get`.
4. Multi-step tool calls (`search_hybrid_knowledge`, `list_accessible_spreadsheets`, `query_tabular_sheet`, `read_document_section`, `get_departing_user_dossier`) execute against PostgreSQL 17 with strict `SecurityContext.CanReadResource(...)` enforcement.
5. Grounded Markdown, MCP execution latency traces, source citations, and structured spreadsheet tables return to the UI.
