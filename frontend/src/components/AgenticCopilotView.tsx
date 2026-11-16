"use client";

import React, { useEffect, useState } from "react";
import {
  Bot,
  Cpu,
  FileCheck2,
  Send,
  Sparkles,
  Table2,
  Terminal,
} from "lucide-react";
import { velitApi } from "@/lib/api";
import {
  AgentChatResponse,
  Department,
  DepartingUserProfile,
  Team,
} from "@/lib/types";

interface AgenticCopilotViewProps {
  token: string;
}

interface ChatEntry {
  question: string;
  response: AgentChatResponse;
}

const SUGGESTED_PROMPTS = [
  "What is the total unreconciled break across Nostro accounts in the Excel model, and how are breaks above $25,000 escalated during December year-end?",
  "How do we recover if CTRL-M job GL_EOD_POST_MASTER fails with ERR_UNBALANCED_JOURNAL_904 on PRD-GL-04?",
  "What are the December year-end payment cut-off times for SWIFT_MT103 and FEDWIRE_FUNDS, and when is the GL schema freeze?",
  "What are the confidential Intraday 99% VaR trading desk limits and headroom in Murex?",
];

export function AgenticCopilotView({ token }: AgenticCopilotViewProps) {
  const [departments, setDepartments] = useState<Department[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [departingUsers, setDepartingUsers] = useState<DepartingUserProfile[]>([]);

  const [scopeDept, setScopeDept] = useState("");
  const [scopeTeam, setScopeTeam] = useState("");
  const [scopeUser, setScopeUser] = useState("");
  const [promptTemplate, setPromptTemplate] = useState("");

  const [question, setQuestion] = useState("");
  const [loading, setLoading] = useState(false);
  const [entries, setEntries] = useState<ChatEntry[]>([]);

  useEffect(() => {
    Promise.all([
      velitApi.getDepartments(token),
      velitApi.getTeams(token),
      velitApi.getDepartingUsers(token),
    ]).then(([depts, tms, usrs]) => {
      setDepartments(depts);
      setTeams(tms);
      setDepartingUsers(usrs);
    });
  }, [token]);

  async function submitQuestion(qText: string) {
    const trimmed = qText.trim();
    if (!trimmed || loading) return;
    setLoading(true);
    try {
      const history = entries.flatMap((e) => [
        { role: "user", content: e.question },
        { role: "assistant", content: e.response.answerMarkdown },
      ]);
      const res = await velitApi.askAgent(token, {
        question: trimmed,
        departmentId: scopeDept || undefined,
        teamId: scopeTeam || undefined,
        departingUserId: scopeUser || undefined,
        promptTemplateName: promptTemplate || undefined,
        history,
      });
      setEntries((prev) => [...prev, { question: trimmed, response: res }]);
      setQuestion("");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="space-y-5">
      {/* Scope & MCP Template Bar */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <Bot className="h-5 w-5 text-indigo-400" />
            <h2 className="text-base font-bold text-white">
              Agentic MCP Hybrid RAG Copilot
            </h2>
          </div>
          <span className="text-[11px] font-mono px-2.5 py-1 rounded-full bg-indigo-950/70 text-indigo-300 border border-indigo-700/50">
            Mcp.Client.Net v1.0.1 &harr; McpHttpServer v1.2.0 (/mcp)
          </span>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3 text-xs">
          <select
            value={scopeDept}
            onChange={(e) => {
              setScopeDept(e.target.value);
              setScopeTeam("");
            }}
            className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
          >
            <option value="">Scope: All Authorized Departments</option>
            {departments.map((d) => (
              <option key={d.id} value={d.id}>
                Dept: {d.name}
              </option>
            ))}
          </select>

          <select
            value={scopeTeam}
            onChange={(e) => setScopeTeam(e.target.value)}
            className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
          >
            <option value="">Scope: All Authorized Teams</option>
            {teams
              .filter((t) => !scopeDept || t.departmentId === scopeDept)
              .map((t) => (
                <option key={t.id} value={t.id}>
                  Team: {t.name}
                </option>
              ))}
          </select>

          <select
            value={scopeUser}
            onChange={(e) => setScopeUser(e.target.value)}
            className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
          >
            <option value="">Scope: All Departing Employees</option>
            {departingUsers.map((u) => (
              <option key={u.id} value={u.id}>
                User: {u.fullName} ({u.teamName})
              </option>
            ))}
          </select>

          <select
            value={promptTemplate}
            onChange={(e) => setPromptTemplate(e.target.value)}
            className="rounded-lg bg-slate-950 border border-indigo-700/60 px-3 py-2 text-indigo-200"
          >
            <option value="">MCP Prompt: Standard Grounded Q&amp;A</option>
            <option value="successor_onboarding_plan">
              [McpPrompt] Successor 30-Day Takeover Briefing
            </option>
            <option value="december_exit_gap_audit">
              [McpPrompt] December Exit Risk &amp; Knowledge Audit
            </option>
          </select>
        </div>

        <div className="flex flex-wrap gap-2 pt-1">
          {SUGGESTED_PROMPTS.map((sp, i) => (
            <button
              key={i}
              type="button"
              onClick={() => submitQuestion(sp)}
              disabled={loading}
              className="text-left text-[11px] px-3 py-1.5 rounded-lg bg-slate-950 hover:bg-slate-800 border border-slate-800 text-slate-300 transition cursor-pointer"
            >
              <Sparkles className="h-3 w-3 inline mr-1.5 text-amber-400" />
              {sp}
            </button>
          ))}
        </div>
      </div>

      {/* Conversation Stream */}
      <div className="space-y-4">
        {entries.map((entry, idx) => (
          <div
            key={idx}
            className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-4"
          >
            <div className="text-sm font-semibold text-blue-300">
              Q: {entry.question}
            </div>

            {/* MCP Tool Execution Trace Badges */}
            {entry.response.mcpToolTraces.length > 0 && (
              <div className="p-3 rounded-xl bg-slate-950 border border-slate-800 space-y-1.5">
                <div className="text-[11px] font-semibold text-indigo-300 flex items-center gap-1.5">
                  <Terminal className="h-3.5 w-3.5" />
                  MCP Protocol Tool Invocations ({entry.response.mcpTransportUsed})
                </div>
                <div className="flex flex-wrap gap-2">
                  {entry.response.mcpToolTraces.map((tr, tIdx) => (
                    <span
                      key={tIdx}
                      className="text-[11px] font-mono px-2.5 py-1 rounded bg-indigo-950/60 border border-indigo-800/50 text-indigo-200"
                    >
                      {tr.toolName}() &rarr; {tr.resultSummary} ({tr.durationMs}ms)
                    </span>
                  ))}
                </div>
              </div>
            )}

            {/* Answer Markdown */}
            <div
              data-testid="copilot-answer"
              className="text-xs sm:text-sm text-slate-200 whitespace-pre-wrap leading-relaxed bg-slate-950/60 border border-slate-800/70 rounded-xl p-4"
            >
              {entry.response.answerMarkdown}
            </div>

            {/* Structured Spreadsheet Result if Returned */}
            {entry.response.structuredTableResult && (
              <div className="p-4 rounded-xl bg-slate-950 border border-emerald-800/40 space-y-2">
                <div className="text-xs font-semibold text-emerald-300 flex items-center gap-1.5">
                  <Table2 className="h-4 w-4" />
                  Live Spreadsheet Query Output:{" "}
                  {entry.response.structuredTableResult.documentTitle} (
                  {entry.response.structuredTableResult.sheetName})
                </div>
                <div className="overflow-x-auto">
                  <table className="w-full text-left text-[11px] text-slate-300">
                    <thead>
                      <tr className="border-b border-slate-800 text-slate-400">
                        {entry.response.structuredTableResult.columns.map((c) => (
                          <th key={c.name} className="py-1 px-2">
                            {c.name}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {entry.response.structuredTableResult.rows
                        .slice(0, 6)
                        .map((r, rIdx) => (
                          <tr key={rIdx} className="border-b border-slate-900">
                            {entry.response.structuredTableResult!.columns.map(
                              (c) => (
                                <td key={c.name} className="py-1 px-2">
                                  {r[c.name] !== null && r[c.name] !== undefined
                                    ? String(r[c.name])
                                    : ""}
                                </td>
                              )
                            )}
                          </tr>
                        ))}
                    </tbody>
                  </table>
                </div>
              </div>
            )}

            {/* Grounded Source Citations */}
            {entry.response.citations.length > 0 && (
              <div className="space-y-2">
                <div className="text-[11px] font-semibold uppercase tracking-wider text-slate-400 flex items-center gap-1.5">
                  <FileCheck2 className="h-3.5 w-3.5 text-emerald-400" />
                  Grounded Citations ({entry.response.citations.length})
                </div>
                <div className="grid grid-cols-1 md:grid-cols-2 gap-2">
                  {entry.response.citations.map((cit, cIdx) => (
                    <div
                      key={cIdx}
                      className="p-3 rounded-xl bg-slate-950 border border-slate-800 text-xs space-y-1"
                    >
                      <div className="font-semibold text-white">
                        {cit.documentTitle} &bull;{" "}
                        <span className="text-amber-300">{cit.location}</span>
                      </div>
                      <div className="text-[11px] text-slate-400">
                        Team: {cit.teamName} &bull; Author:{" "}
                        {cit.departingUserName} &bull; Scope: {cit.visibility}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            )}
          </div>
        ))}
      </div>

      {/* Input Box */}
      <form
        onSubmit={(e) => {
          e.preventDefault();
          submitQuestion(question);
        }}
        className="bg-slate-900 border border-slate-800 rounded-2xl p-4 flex gap-3"
      >
        <input
          type="text"
          value={question}
          onChange={(e) => setQuestion(e.target.value)}
          placeholder="Ask across authorized runbooks, OneNote PDF exports, Excel reconciliation formulas, or December handover dossiers..."
          className="flex-1 rounded-xl bg-slate-950 border border-slate-700 px-4 py-2.5 text-sm text-white focus:outline-none focus:border-blue-500"
        />
        <button
          type="submit"
          disabled={loading || !question.trim()}
          className="px-5 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-sm font-semibold text-white flex items-center gap-2 cursor-pointer"
        >
          <Cpu className="h-4 w-4" />
          {loading ? "Reasoning via MCP..." : "Ask Copilot"}
        </button>
      </form>
    </div>
  );
}
