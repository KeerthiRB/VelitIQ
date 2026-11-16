# Frontend Agent Context — Horizon VelitIQ (`frontend/`)

This directory contains the **Next.js 16 (App Router, TypeScript, Tailwind CSS)** web portal for **Horizon VelitIQ**.

For full system vision, `.NET 10` API & MCP contracts, RBAC rules, and PostgreSQL architecture, read:
- [`../AGENTS.md`](../AGENTS.md)
- [`../docs/02_SYSTEM_ARCHITECTURE_AND_FLOWS.md`](../docs/02_SYSTEM_ARCHITECTURE_AND_FLOWS.md)
- [`../docs/05_DEVELOPER_AND_IDE_REFERENCE.md`](../docs/05_DEVELOPER_AND_IDE_REFERENCE.md)

## Key Frontend Modules
- `src/lib/types.ts` — Strongly-typed DTO contracts mirroring `VelitIQ.Core.DTOs`.
- `src/lib/api.ts` — Typed REST client (`NEXT_PUBLIC_API_BASE_URL`, defaults to `http://127.0.0.1:5180`).
- `src/components/LoginView.tsx` — Active Directory / LDAP login, Windows Integrated SSO trigger, and Dev/UAT RBAC persona selector.
- `src/components/DashboardView.tsx` — December exit readiness KPIs, format breakdown, and team scorecards.
- `src/components/OrganizationHubView.tsx` — `Department -> Team -> Departing User` taxonomy explorer, handover dossiers, and interactive AI Gap Q&A.
- `src/components/KnowledgeVaultView.tsx` — Multi-format upload studio (`CSV`, `Excel`, `Word`, `PDF`), spreadsheet SQL/formula inspector, and visibility toggle.
- `src/components/AgenticCopilotView.tsx` — Scoped Agentic RAG Copilot displaying live MCP tool execution traces, structured spreadsheet tables, and grounded citations.
- `src/components/GovernanceAdminView.tsx` — Department/Team provisioning, AD-to-Entra ID group mappings, secret guardrail alerts, and immutable audit trail.
