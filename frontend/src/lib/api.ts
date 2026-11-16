import {
  AgentChatResponse,
  AuditLog,
  AuthTokenResponse,
  DashboardMetrics,
  Department,
  DepartingUserProfile,
  DemoPersona,
  DocumentChunk,
  HandoverGapQuestion,
  KnowledgeDocument,
  SecretScanFinding,
  TabularQueryResult,
  TabularSheet,
  Team,
  VisibilityScope,
} from "./types";

const API_BASE = process.env.NEXT_PUBLIC_API_BASE_URL || "http://127.0.0.1:5180";

async function apiFetch<T>(
  path: string,
  token?: string | null,
  init?: RequestInit
): Promise<T> {
  const headers = new Headers(init?.headers);
  if (token) {
    headers.set("Authorization", `Bearer ${token}`);
  }
  if (!(init?.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }

  const res = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers,
  });

  if (!res.ok) {
    let errText = `HTTP ${res.status}`;
    try {
      const errJson = await res.json();
      if (errJson?.error) errText = errJson.error;
    } catch {
      // ignore
    }
    throw new Error(errText);
  }

  return res.json() as Promise<T>;
}

export const velitApi = {
  baseUrl: API_BASE,

  getHealth: () => apiFetch<Record<string, unknown>>("/api/health"),

  getDemoPersonas: () => apiFetch<DemoPersona[]>("/api/auth/personas"),

  loginCredentials: (usernameOrEmail: string, password: string, domain?: string) =>
    apiFetch<AuthTokenResponse>("/api/auth/login", null, {
      method: "POST",
      body: JSON.stringify({ usernameOrEmail, password, domain }),
    }),

  loginDemoPersona: (email: string) =>
    apiFetch<AuthTokenResponse>("/api/auth/demo-login", null, {
      method: "POST",
      body: JSON.stringify({ email }),
    }),

  loginWindowsSso: () =>
    apiFetch<AuthTokenResponse>("/api/auth/windows-sso", null, {
      method: "POST",
      credentials: "include",
    }),

  getDashboard: (token: string) =>
    apiFetch<DashboardMetrics>("/api/dashboard", token),

  getDepartments: (token: string) =>
    apiFetch<Department[]>("/api/organization/departments", token),

  createDepartment: (
    token: string,
    payload: { name: string; code: string; description: string }
  ) =>
    apiFetch<Department>("/api/organization/departments", token, {
      method: "POST",
      body: JSON.stringify(payload),
    }),

  getTeams: (token: string, departmentId?: string) =>
    apiFetch<Team[]>(
      `/api/organization/teams${departmentId ? `?departmentId=${departmentId}` : ""}`,
      token
    ),

  createTeam: (
    token: string,
    payload: {
      departmentId: string;
      name: string;
      description: string;
      adGroupName: string;
      entraGroupId: string;
      managerEmail: string;
    }
  ) =>
    apiFetch<Team>("/api/organization/teams", token, {
      method: "POST",
      body: JSON.stringify(payload),
    }),

  getDepartingUsers: (token: string, departmentId?: string, teamId?: string) => {
    const params = new URLSearchParams();
    if (departmentId) params.set("departmentId", departmentId);
    if (teamId) params.set("teamId", teamId);
    const qs = params.toString();
    return apiFetch<DepartingUserProfile[]>(
      `/api/departing-users${qs ? `?${qs}` : ""}`,
      token
    );
  },

  getDepartingUserDetails: (token: string, id: string) =>
    apiFetch<{
      profile: DepartingUserProfile;
      canAccessTeamPrivate: boolean;
      canWrite: boolean;
      documents: KnowledgeDocument[];
      gapQuestions: HandoverGapQuestion[];
    }>(`/api/departing-users/${id}`, token),

  createDepartingUser: (
    token: string,
    payload: {
      teamId: string;
      fullName: string;
      email: string;
      employeeId: string;
      jobTitle: string;
      exitDate: string;
      successorName: string;
      successorEmail: string;
    }
  ) =>
    apiFetch<DepartingUserProfile>("/api/departing-users", token, {
      method: "POST",
      body: JSON.stringify(payload),
    }),

  getDocuments: (
    token: string,
    filters?: {
      departmentId?: string;
      teamId?: string;
      departingUserId?: string;
      format?: string;
    }
  ) => {
    const params = new URLSearchParams();
    if (filters?.departmentId) params.set("departmentId", filters.departmentId);
    if (filters?.teamId) params.set("teamId", filters.teamId);
    if (filters?.departingUserId) params.set("departingUserId", filters.departingUserId);
    if (filters?.format) params.set("format", filters.format);
    const qs = params.toString();
    return apiFetch<KnowledgeDocument[]>(`/api/documents${qs ? `?${qs}` : ""}`, token);
  },

  getDocumentDetails: (token: string, id: string) =>
    apiFetch<{
      document: KnowledgeDocument;
      chunks: DocumentChunk[];
      sheets: TabularSheet[];
      secretFindings: SecretScanFinding[];
    }>(`/api/documents/${id}`, token),

  updateDocumentVisibility: (token: string, id: string, visibility: VisibilityScope) =>
    apiFetch<KnowledgeDocument>(`/api/documents/${id}/visibility`, token, {
      method: "PATCH",
      body: JSON.stringify(visibility),
    }),

  uploadDocument: (token: string, formData: FormData) =>
    apiFetch<KnowledgeDocument>("/api/documents/upload", token, {
      method: "POST",
      body: formData,
    }),

  downloadDocumentBlob: async (token: string, id: string, fileName: string) => {
    const res = await fetch(`${API_BASE}/api/documents/${id}/download`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!res.ok) throw new Error(`Download failed (${res.status})`);
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = fileName;
    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  },

  getGapQuestions: (token: string, teamId?: string, departingUserId?: string) => {
    const params = new URLSearchParams();
    if (teamId) params.set("teamId", teamId);
    if (departingUserId) params.set("departingUserId", departingUserId);
    const qs = params.toString();
    return apiFetch<HandoverGapQuestion[]>(`/api/gaps${qs ? `?${qs}` : ""}`, token);
  },

  answerGapQuestion: (token: string, gapId: string, answer: string) =>
    apiFetch<HandoverGapQuestion>(`/api/gaps/${gapId}/answer`, token, {
      method: "POST",
      body: JSON.stringify({ answer }),
    }),

  querySpreadsheet: (
    token: string,
    payload: {
      sheetId: string;
      filterColumn?: string;
      filterOperator?: string;
      filterValue?: string;
      aggregateColumn?: string;
      aggregateFunction?: string;
      limit?: number;
    }
  ) =>
    apiFetch<TabularQueryResult>("/api/spreadsheets/query", token, {
      method: "POST",
      body: JSON.stringify(payload),
    }),

  askAgent: (
    token: string,
    payload: {
      question: string;
      departmentId?: string;
      teamId?: string;
      departingUserId?: string;
      promptTemplateName?: string;
      history?: { role: string; content: string }[];
    }
  ) =>
    apiFetch<AgentChatResponse>("/api/chat/ask", token, {
      method: "POST",
      body: JSON.stringify(payload),
    }),

  getAuditLogs: (token: string, limit = 100) =>
    apiFetch<AuditLog[]>(`/api/admin/audit-logs?limit=${limit}`, token),

  getSecretFindings: (token: string) =>
    apiFetch<SecretScanFinding[]>("/api/admin/secret-findings", token),

  acknowledgeSecret: (token: string, id: string) =>
    apiFetch<{ acknowledged: boolean }>(
      `/api/admin/secret-findings/${id}/acknowledge`,
      token,
      { method: "POST" }
    ),
};
