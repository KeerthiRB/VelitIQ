"use client";

import React, { useEffect, useState } from "react";
import {
  AlertTriangle,
  Calculator,
  Download,
  Eye,
  FileSpreadsheet,
  FileText,
  Globe2,
  Lock,
  UploadCloud,
  X,
} from "lucide-react";
import { velitApi } from "@/lib/api";
import {
  Department,
  DepartingUserProfile,
  DocumentCategory,
  DocumentChunk,
  KnowledgeDocument,
  SecretScanFinding,
  SpreadsheetFormulaInfo,
  TabularColumnSchema,
  TabularQueryResult,
  TabularSheet,
  Team,
  VisibilityScope,
} from "@/lib/types";

interface KnowledgeVaultViewProps {
  token: string;
  onDataChanged: () => void;
}

export function KnowledgeVaultView({
  token,
  onDataChanged,
}: KnowledgeVaultViewProps) {
  const [departments, setDepartments] = useState<Department[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [departingUsers, setDepartingUsers] = useState<DepartingUserProfile[]>([]);
  const [documents, setDocuments] = useState<KnowledgeDocument[]>([]);

  const [filterDept, setFilterDept] = useState("");
  const [filterTeam, setFilterTeam] = useState("");
  const [filterUser, setFilterUser] = useState("");
  const [filterFormat, setFilterFormat] = useState("");

  // Upload Form State
  const [uploadUserId, setUploadUserId] = useState("");
  const [uploadFile, setUploadFile] = useState<File | null>(null);
  const [uploadTitle, setUploadTitle] = useState("");
  const [uploadCategory, setUploadCategory] = useState<DocumentCategory>("Runbook");
  const [uploadVisibility, setUploadVisibility] = useState<VisibilityScope>("TeamOnly");
  const [uploadTags, setUploadTags] = useState("");
  const [uploading, setUploading] = useState(false);
  const [uploadMessage, setUploadMessage] = useState<{
    type: "ok" | "err";
    text: string;
  } | null>(null);

  // Document Inspector Modal State
  const [inspected, setInspected] = useState<{
    document: KnowledgeDocument;
    chunks: DocumentChunk[];
    sheets: TabularSheet[];
    secretFindings: SecretScanFinding[];
  } | null>(null);
  const [activeSheetResult, setActiveSheetResult] =
    useState<TabularQueryResult | null>(null);
  const [aggCol, setAggCol] = useState("");
  const [aggFn, setAggFn] = useState("sum");

  async function refreshCatalog() {
    const docs = await velitApi.getDocuments(token, {
      departmentId: filterDept || undefined,
      teamId: filterTeam || undefined,
      departingUserId: filterUser || undefined,
      format: filterFormat || undefined,
    });
    setDocuments(docs);
  }

  useEffect(() => {
    Promise.all([
      velitApi.getDepartments(token),
      velitApi.getTeams(token),
      velitApi.getDepartingUsers(token),
    ]).then(([depts, tms, usrs]) => {
      setDepartments(depts);
      setTeams(tms);
      setDepartingUsers(usrs);
      if (usrs.length > 0 && !uploadUserId) {
        setUploadUserId(usrs[0].id);
      }
    });
  }, [token]);

  useEffect(() => {
    refreshCatalog();
  }, [token, filterDept, filterTeam, filterUser, filterFormat]);

  async function handleUpload(e: React.FormEvent) {
    e.preventDefault();
    if (!uploadFile || !uploadUserId) return;
    setUploading(true);
    setUploadMessage(null);
    try {
      const fd = new FormData();
      fd.append("file", uploadFile);
      fd.append("departingUserId", uploadUserId);
      if (uploadTitle.trim()) fd.append("title", uploadTitle.trim());
      fd.append("category", uploadCategory);
      fd.append("visibility", uploadVisibility);
      fd.append("tags", uploadTags);

      const created = await velitApi.uploadDocument(token, fd);
      setUploadMessage({
        type: "ok",
        text: `Ingested '${created.originalFileName}' -> ${created.chunkCount} chunks, ${created.formulaCount} formulas, ${created.secretWarningCount} guardrail warnings.`,
      });
      setUploadFile(null);
      setUploadTitle("");
      setUploadTags("");
      await refreshCatalog();
      onDataChanged();
    } catch (err) {
      setUploadMessage({
        type: "err",
        text: err instanceof Error ? err.message : "Upload failed",
      });
    } finally {
      setUploading(false);
    }
  }

  async function openInspector(docId: string) {
    const details = await velitApi.getDocumentDetails(token, docId);
    setInspected(details);
    setActiveSheetResult(null);
    if (details.sheets.length > 0) {
      const firstSheet = details.sheets[0];
      const cols: TabularColumnSchema[] = JSON.parse(firstSheet.columnsJson || "[]");
      const firstNum = cols.find((c) => c.dataType === "number")?.name || "";
      setAggCol(firstNum);
      const res = await velitApi.querySpreadsheet(token, {
        sheetId: firstSheet.id,
        aggregateColumn: firstNum || undefined,
        aggregateFunction: firstNum ? "sum" : undefined,
        limit: 20,
      });
      setActiveSheetResult(res);
    }
  }

  async function runSheetAggregation(sheetId: string) {
    const res = await velitApi.querySpreadsheet(token, {
      sheetId,
      aggregateColumn: aggCol || undefined,
      aggregateFunction: aggCol ? aggFn : undefined,
      limit: 25,
    });
    setActiveSheetResult(res);
  }

  async function handleChangeVisibility(docId: string, vis: VisibilityScope) {
    const updated = await velitApi.updateDocumentVisibility(token, docId, vis);
    if (inspected) {
      setInspected({ ...inspected, document: updated });
    }
    await refreshCatalog();
    onDataChanged();
  }

  const visibilityBadge = (v: VisibilityScope) => {
    if (v === "BankWideShared")
      return "bg-emerald-500/15 text-emerald-300 border-emerald-500/30";
    if (v === "DepartmentShared")
      return "bg-sky-500/15 text-sky-300 border-sky-500/30";
    return "bg-amber-500/15 text-amber-300 border-amber-500/30";
  };

  return (
    <div className="space-y-6">
      {/* Multi-Format Ingestion Studio */}
      <form
        onSubmit={handleUpload}
        className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-4"
      >
        <div className="flex items-center justify-between flex-wrap gap-2">
          <div>
            <h2 className="text-base font-bold text-white flex items-center gap-2">
              <UploadCloud className="h-5 w-5 text-blue-400" />
              Multi-Format Handover Ingestion Studio
            </h2>
            <p className="text-xs text-slate-400">
              Supports <strong>CSV</strong>, <strong>Excel (.xlsx/.xls with Formulas)</strong>,{" "}
              <strong>Word (.docx)</strong>, and <strong>PDF (incl. OneNote Exports)</strong>.
              Automatically generates Contextual Chunks, SQL Sheet Tables, Secret Guardrail Scans &amp; AI Gap Questions.
            </p>
          </div>
        </div>

        {uploadMessage && (
          <div
            className={`p-3 rounded-xl text-xs border ${
              uploadMessage.type === "ok"
                ? "bg-emerald-950/40 border-emerald-700/50 text-emerald-200"
                : "bg-rose-950/40 border-rose-700/50 text-rose-200"
            }`}
          >
            {uploadMessage.text}
          </div>
        )}

        <div className="grid grid-cols-1 md:grid-cols-12 gap-3 text-xs">
          <div className="md:col-span-4">
            <label className="block text-slate-300 mb-1">
              Target Team &rarr; Departing Employee
            </label>
            <select
              value={uploadUserId}
              onChange={(e) => setUploadUserId(e.target.value)}
              required
              className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
            >
              {departingUsers.map((u) => (
                <option key={u.id} value={u.id}>
                  [{u.teamName}] {u.fullName} (Exit: {u.exitDate})
                </option>
              ))}
            </select>
          </div>

          <div className="md:col-span-4">
            <label className="block text-slate-300 mb-1">
              Artifact File (.csv, .xlsx, .docx, .pdf)
            </label>
            <input
              type="file"
              accept=".csv,.xlsx,.xlsm,.xls,.docx,.pdf"
              required
              onChange={(e) => setUploadFile(e.target.files?.[0] || null)}
              className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-1.5 text-slate-200 file:mr-3 file:py-1 file:px-2.5 file:rounded file:border-0 file:bg-blue-600 file:text-white file:text-xs"
            />
          </div>

          <div className="md:col-span-2">
            <label className="block text-slate-300 mb-1">Category</label>
            <select
              value={uploadCategory}
              onChange={(e) =>
                setUploadCategory(e.target.value as DocumentCategory)
              }
              className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
            >
              <option value="Runbook">Runbook</option>
              <option value="FinancialOrRiskModel">Financial / Risk Model</option>
              <option value="OneNoteExport">OneNote PDF Export</option>
              <option value="ArchitectureAndDesign">Architecture &amp; Design</option>
              <option value="ProcessAndSop">Process &amp; SOP</option>
              <option value="DataMapping">Data Mapping</option>
            </select>
          </div>

          <div className="md:col-span-2">
            <label className="block text-slate-300 mb-1">Access Scope</label>
            <select
              value={uploadVisibility}
              onChange={(e) =>
                setUploadVisibility(e.target.value as VisibilityScope)
              }
              className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
            >
              <option value="TeamOnly">TeamOnly (Chinese Wall)</option>
              <option value="DepartmentShared">DepartmentShared</option>
              <option value="BankWideShared">BankWideShared (Cross-Team)</option>
            </select>
          </div>

          <div className="md:col-span-5">
            <input
              type="text"
              value={uploadTitle}
              onChange={(e) => setUploadTitle(e.target.value)}
              placeholder="Optional custom document title..."
              className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
            />
          </div>

          <div className="md:col-span-5">
            <input
              type="text"
              value={uploadTags}
              onChange={(e) => setUploadTags(e.target.value)}
              placeholder="Comma-separated tags (e.g. swift, eod-batch, reconciliation)"
              className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
            />
          </div>

          <div className="md:col-span-2">
            <button
              type="submit"
              disabled={uploading || !uploadFile}
              className="w-full h-full py-2 px-4 rounded-lg bg-blue-600 hover:bg-blue-500 disabled:opacity-50 font-semibold text-white transition cursor-pointer"
            >
              {uploading ? "Indexing..." : "Ingest & Index"}
            </button>
          </div>
        </div>
      </form>

      {/* Filter Bar */}
      <div className="bg-slate-900 border border-slate-800 rounded-xl p-4 flex flex-wrap items-center gap-3 text-xs">
        <span className="font-semibold text-slate-300">Filter Vault:</span>
        <select
          value={filterDept}
          onChange={(e) => {
            setFilterDept(e.target.value);
            setFilterTeam("");
          }}
          className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-1.5 text-white"
        >
          <option value="">All Departments</option>
          {departments.map((d) => (
            <option key={d.id} value={d.id}>
              {d.name}
            </option>
          ))}
        </select>

        <select
          value={filterTeam}
          onChange={(e) => setFilterTeam(e.target.value)}
          className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-1.5 text-white"
        >
          <option value="">All Teams</option>
          {teams
            .filter((t) => !filterDept || t.departmentId === filterDept)
            .map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
        </select>

        <select
          value={filterUser}
          onChange={(e) => setFilterUser(e.target.value)}
          className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-1.5 text-white"
        >
          <option value="">All Departing Staff</option>
          {departingUsers.map((u) => (
            <option key={u.id} value={u.id}>
              {u.fullName}
            </option>
          ))}
        </select>

        <select
          value={filterFormat}
          onChange={(e) => setFilterFormat(e.target.value)}
          className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-1.5 text-white"
        >
          <option value="">All Formats</option>
          <option value="Excel">Excel (.xlsx)</option>
          <option value="Csv">CSV (.csv)</option>
          <option value="Word">Word (.docx)</option>
          <option value="Pdf">PDF / OneNote Export (.pdf)</option>
        </select>

        <span className="ml-auto text-slate-400">
          Showing <strong className="text-white">{documents.length}</strong>{" "}
          authorized artifact(s)
        </span>
      </div>

      {/* Document Cards Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {documents.map((doc) => (
          <div
            key={doc.id}
            className="bg-slate-900 border border-slate-800 hover:border-slate-700 rounded-2xl p-5 flex flex-col justify-between gap-4"
          >
            <div className="space-y-2">
              <div className="flex items-start justify-between gap-2">
                <div className="flex items-center gap-2">
                  {doc.format === "Excel" || doc.format === "Csv" ? (
                    <FileSpreadsheet className="h-5 w-5 text-emerald-400 shrink-0" />
                  ) : (
                    <FileText className="h-5 w-5 text-blue-400 shrink-0" />
                  )}
                  <h3 className="text-sm font-bold text-white">{doc.title}</h3>
                </div>
                <span
                  className={`text-[11px] px-2 py-0.5 rounded-full border shrink-0 flex items-center gap-1 ${visibilityBadge(
                    doc.visibility
                  )}`}
                >
                  {doc.visibility === "TeamOnly" ? (
                    <Lock className="h-3 w-3" />
                  ) : (
                    <Globe2 className="h-3 w-3" />
                  )}
                  {doc.visibility}
                </span>
              </div>

              <div className="text-xs text-indigo-300 font-medium">
                {doc.departmentName} &rarr; {doc.teamName} &rarr;{" "}
                <span className="text-amber-300">{doc.departingUserName}</span>
              </div>

              <p className="text-xs text-slate-400 line-clamp-3">
                {doc.summary}
              </p>
            </div>

            <div className="pt-3 border-t border-slate-800 flex items-center justify-between gap-2 text-[11px] text-slate-400">
              <div className="flex items-center gap-2 flex-wrap">
                <span className="px-2 py-0.5 rounded bg-slate-800 text-slate-200">
                  {doc.format}
                </span>
                <span>{doc.chunkCount} chunks</span>
                {doc.formulaCount > 0 && (
                  <span className="text-emerald-400">
                    {doc.formulaCount} formulas
                  </span>
                )}
                {doc.secretWarningCount > 0 && (
                  <span className="text-rose-400 flex items-center gap-1">
                    <AlertTriangle className="h-3 w-3" />
                    {doc.secretWarningCount} alert(s)
                  </span>
                )}
              </div>

              <div className="flex items-center gap-2">
                <button
                  onClick={() => openInspector(doc.id)}
                  className="px-2.5 py-1.5 rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-200 flex items-center gap-1 cursor-pointer"
                >
                  <Eye className="h-3.5 w-3.5" />
                  Inspect
                </button>
                <button
                  onClick={() =>
                    velitApi.downloadDocumentBlob(
                      token,
                      doc.id,
                      doc.originalFileName
                    )
                  }
                  className="px-2.5 py-1.5 rounded-lg bg-blue-600/20 hover:bg-blue-600/40 text-blue-300 flex items-center gap-1 cursor-pointer"
                >
                  <Download className="h-3.5 w-3.5" />
                </button>
              </div>
            </div>
          </div>
        ))}
      </div>

      {/* Deep Document & Spreadsheet Inspector Modal */}
      {inspected && (
        <div className="fixed inset-0 z-50 bg-black/75 flex items-center justify-center p-4 overflow-y-auto">
          <div className="bg-slate-900 border border-slate-700 rounded-2xl max-w-4xl w-full max-h-[88vh] overflow-y-auto p-6 space-y-5">
            <div className="flex items-start justify-between gap-4">
              <div>
                <h3 className="text-base font-bold text-white">
                  {inspected.document.title}
                </h3>
                <p className="text-xs text-slate-400">
                  {inspected.document.originalFileName} &bull;{" "}
                  {inspected.document.teamName} &bull; Author:{" "}
                  {inspected.document.departingUserName}
                </p>
              </div>
              <div className="flex items-center gap-2">
                <select
                  value={inspected.document.visibility}
                  onChange={(e) =>
                    handleChangeVisibility(
                      inspected.document.id,
                      e.target.value as VisibilityScope
                    )
                  }
                  className="rounded-lg bg-slate-950 border border-slate-700 px-2.5 py-1.5 text-xs text-white"
                >
                  <option value="TeamOnly">TeamOnly</option>
                  <option value="DepartmentShared">DepartmentShared</option>
                  <option value="BankWideShared">BankWideShared</option>
                </select>
                <button
                  onClick={() => setInspected(null)}
                  className="p-1.5 rounded-lg bg-slate-800 text-slate-300 hover:text-white cursor-pointer"
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            </div>

            {inspected.secretFindings.length > 0 && (
              <div className="p-3.5 rounded-xl bg-rose-950/40 border border-rose-800/50 text-xs text-rose-200 space-y-1">
                <div className="font-semibold flex items-center gap-1.5">
                  <AlertTriangle className="h-4 w-4 text-rose-400" />
                  Sensitive Credential / PII Guardrail Warnings
                </div>
                {inspected.secretFindings.map((sf) => (
                  <div key={sf.id}>
                    &bull; [{sf.severity}] {sf.ruleName} at {sf.location}:{" "}
                    <code>{sf.redactedPreview}</code>
                  </div>
                ))}
              </div>
            )}

            {/* Interactive Spreadsheet SQL & Formula Inspector */}
            {inspected.sheets.length > 0 && activeSheetResult && (
              <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 space-y-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <div className="text-xs font-semibold text-emerald-300 flex items-center gap-1.5">
                    <Calculator className="h-4 w-4" />
                    Structured Tabular Engine — Sheet:{" "}
                    {activeSheetResult.sheetName} ({activeSheetResult.matchedRowCount}{" "}
                    rows)
                  </div>

                  <div className="flex items-center gap-2 text-xs">
                    <select
                      value={aggCol}
                      onChange={(e) => setAggCol(e.target.value)}
                      className="rounded bg-slate-900 border border-slate-700 px-2 py-1 text-white"
                    >
                      <option value="">Select Numeric Column</option>
                      {activeSheetResult.columns
                        .filter((c) => c.dataType === "number")
                        .map((c) => (
                          <option key={c.name} value={c.name}>
                            {c.name}
                          </option>
                        ))}
                    </select>
                    <select
                      value={aggFn}
                      onChange={(e) => setAggFn(e.target.value)}
                      className="rounded bg-slate-900 border border-slate-700 px-2 py-1 text-white"
                    >
                      <option value="sum">SUM</option>
                      <option value="avg">AVG</option>
                      <option value="min">MIN</option>
                      <option value="max">MAX</option>
                    </select>
                    <button
                      type="button"
                      onClick={() =>
                        runSheetAggregation(activeSheetResult.sheetId)
                      }
                      className="px-2.5 py-1 rounded bg-emerald-600 text-white font-medium cursor-pointer"
                    >
                      Compute
                    </button>
                  </div>
                </div>

                {activeSheetResult.aggregationSummary && (
                  <div className="text-xs text-emerald-200 bg-emerald-950/30 border border-emerald-800/40 rounded-lg px-3 py-2">
                    Exact {String(activeSheetResult.aggregationSummary.function)}(
                    <code>{String(activeSheetResult.aggregationSummary.column)}</code>
                    ) ={" "}
                    <strong className="text-sm">
                      {Number(
                        activeSheetResult.aggregationSummary.value || 0
                      ).toLocaleString()}
                    </strong>
                  </div>
                )}

                {activeSheetResult.formulas.length > 0 && (
                  <div className="text-xs space-y-1">
                    <div className="font-semibold text-slate-300">
                      Extracted Excel Formulas ({activeSheetResult.formulas.length}):
                    </div>
                    <div className="flex flex-wrap gap-1.5">
                      {activeSheetResult.formulas
                        .slice(0, 8)
                        .map((f: SpreadsheetFormulaInfo) => (
                          <span
                            key={f.cellAddress}
                            className="font-mono text-[11px] px-2 py-0.5 rounded bg-slate-900 border border-slate-800 text-amber-300"
                          >
                            {f.cellAddress}: {f.formula} &rarr; {f.cachedValue}
                          </span>
                        ))}
                    </div>
                  </div>
                )}

                <div className="overflow-x-auto">
                  <table className="w-full text-left text-[11px] text-slate-300 border-collapse">
                    <thead>
                      <tr className="border-b border-slate-800 text-slate-400">
                        {activeSheetResult.columns.map((c) => (
                          <th key={c.name} className="py-1.5 px-2">
                            {c.name}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {activeSheetResult.rows.slice(0, 10).map((row, idx) => (
                        <tr key={idx} className="border-b border-slate-900">
                          {activeSheetResult.columns.map((c) => (
                            <td key={c.name} className="py-1.5 px-2">
                              {row[c.name] !== null && row[c.name] !== undefined
                                ? String(row[c.name])
                                : ""}
                            </td>
                          ))}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {/* Contextual RAG Chunks */}
            <div className="space-y-2">
              <h4 className="text-xs font-semibold uppercase tracking-wider text-slate-400">
                Indexed Contextual Chunks ({inspected.chunks.length})
              </h4>
              <div className="space-y-2 max-h-64 overflow-y-auto pr-1">
                {inspected.chunks.map((ch) => (
                  <div
                    key={ch.id}
                    className="p-3 rounded-xl bg-slate-950 border border-slate-800 text-xs space-y-1"
                  >
                    <div className="flex items-center justify-between text-[11px] text-indigo-300">
                      <span>
                        #{ch.chunkIndex} &bull; {ch.sectionPath}
                      </span>
                      <span className="text-slate-400">
                        {ch.pageOrSheetName} ({ch.sourceType})
                      </span>
                    </div>
                    <p className="text-slate-300 whitespace-pre-wrap">
                      {ch.content}
                    </p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
