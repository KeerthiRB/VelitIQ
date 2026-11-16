export type UserRole = "Employee" | "TeamLead" | "CrossTeamViewer" | "Admin";
export type VisibilityScope = "TeamOnly" | "DepartmentShared" | "BankWideShared";
export type DocumentFormat = "Pdf" | "Word" | "Excel" | "Csv";
export type DocumentCategory =
  | "Runbook"
  | "ArchitectureAndDesign"
  | "FinancialOrRiskModel"
  | "OneNoteExport"
  | "ProcessAndSop"
  | "ContactsAndEscalations"
  | "DataMapping";

export interface UserProfile {
  id: string;
  username: string;
  email: string;
  displayName: string;
  jobTitle: string;
  authSource: string;
  role: UserRole;
  primaryTeamId?: string | null;
  primaryTeamName?: string | null;
  primaryDepartmentId?: string | null;
  primaryDepartmentName?: string | null;
  accessibleTeamIds: string[];
}

export interface AuthTokenResponse {
  accessToken: string;
  expiresAt: string;
  user: UserProfile;
}

export interface DemoPersona {
  email: string;
  displayName: string;
  jobTitle: string;
  role: UserRole;
  teamName: string;
  departmentName: string;
  scenarioHighlight: string;
}

export interface Department {
  id: string;
  name: string;
  code: string;
  description: string;
  createdAt: string;
}

export interface Team {
  id: string;
  departmentId: string;
  departmentName: string;
  name: string;
  slug: string;
  description: string;
  adGroupName: string;
  entraGroupId: string;
  managerEmail: string;
}

export interface DepartingUserProfile {
  id: string;
  userId?: string | null;
  departmentId: string;
  departmentName: string;
  teamId: string;
  teamName: string;
  fullName: string;
  email: string;
  employeeId: string;
  jobTitle: string;
  exitDate: string;
  successorName: string;
  successorEmail: string;
  status: string;
  readinessScore: number;
  systemsOwned: string[];
  recurringSchedules: string[];
  keyContacts: string[];
  executiveSummary: string;
  updatedAt: string;
}

export interface KnowledgeDocument {
  id: string;
  departmentId: string;
  departmentName: string;
  teamId: string;
  teamName: string;
  departingUserId: string;
  departingUserName: string;
  title: string;
  originalFileName: string;
  contentType: string;
  fileSizeBytes: number;
  format: DocumentFormat;
  category: DocumentCategory;
  visibility: VisibilityScope;
  tags: string[];
  status: string;
  statusMessage: string;
  summary: string;
  pageOrSheetCount: number;
  chunkCount: number;
  formulaCount: number;
  diagramOrImageCount: number;
  secretWarningCount: number;
  createdAt: string;
}

export interface DocumentChunk {
  id: string;
  documentId: string;
  chunkIndex: number;
  sourceType: string;
  sectionPath: string;
  pageOrSheetName: string;
  content: string;
  tokenEstimate: number;
}

export interface TabularColumnSchema {
  name: string;
  normalizedName: string;
  dataType: string;
  sampleValues: string[];
  numericMin?: number | null;
  numericMax?: number | null;
  numericSum?: number | null;
}

export interface SpreadsheetFormulaInfo {
  cellAddress: string;
  formula: string;
  cachedValue: string;
  referencedSheets: string[];
}

export interface TabularSheet {
  id: string;
  documentId: string;
  documentTitle: string;
  sheetName: string;
  sheetIndex: number;
  rowCount: number;
  columnCount: number;
  columnsJson: string;
  formulasJson: string;
  summaryMarkdown: string;
}

export interface TabularQueryResult {
  sheetId: string;
  documentId: string;
  documentTitle: string;
  sheetName: string;
  TotalSheetRows?: number;
  totalSheetRows: number;
  matchedRowCount: number;
  columns: TabularColumnSchema[];
  formulas: SpreadsheetFormulaInfo[];
  rows: Record<string, unknown>[];
  aggregationSummary?: Record<string, unknown> | null;
}

export interface HandoverGapQuestion {
  id: string;
  departingUserId: string;
  departingUserName: string;
  teamId: string;
  teamName: string;
  documentId?: string | null;
  documentTitle?: string | null;
  category: string;
  priority: string;
  question: string;
  contextSnippet: string;
  answer?: string | null;
  answeredByName?: string | null;
  answeredAt?: string | null;
  status: "Open" | "Answered" | "Dismissed";
  createdAt: string;
}

export interface SecretScanFinding {
  id: string;
  documentId: string;
  ruleName: string;
  severity: "Info" | "Warning" | "Critical";
  location: string;
  redactedPreview: string;
  isAcknowledged: boolean;
  createdAt: string;
}

export interface AuditLog {
  id: string;
  timestamp: string;
  actorEmail: string;
  actorRole: string;
  action: string;
  resourceType: string;
  resourceId: string;
  detailsJson: string;
  ipAddress: string;
}

export interface DashboardMetrics {
  totalDepartments: number;
  totalTeams: number;
  totalDepartingUsers: number;
  departingInDecemberCount: number;
  averageReadinessScore: number;
  totalDocuments: number;
  totalIndexedChunks: number;
  totalTabularSheets: number;
  openGapQuestionsCount: number;
  answeredGapQuestionsCount: number;
  unacknowledgedSecretsCount: number;
  aiProviderMode: string;
  isPgVectorEnabled: boolean;
  formatBreakdown: { format: string; count: number }[];
  teamReadiness: {
    teamId: string;
    teamName: string;
    departmentName: string;
    departingUsersCount: number;
    documentsCount: number;
    openGapsCount: number;
    averageReadinessScore: number;
  }[];
  upcomingExits: DepartingUserProfile[];
}

export interface McpToolExecutionTrace {
  toolName: string;
  argumentsSummary: string;
  resultSummary: string;
  durationMs: number;
}

export interface SourceCitation {
  documentId: string;
  documentTitle: string;
  fileName: string;
  format: string;
  teamName: string;
  departingUserName: string;
  location: string;
  excerpt: string;
  visibility: VisibilityScope;
}

export interface AgentChatResponse {
  answerMarkdown: string;
  aiModeUsed: string;
  mcpTransportUsed: string;
  mcpToolTraces: McpToolExecutionTrace[];
  citations: SourceCitation[];
  structuredTableResult?: TabularQueryResult | null;
}
