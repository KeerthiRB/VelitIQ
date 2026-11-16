"use client";

import React from "react";
import {
  AlertTriangle,
  CalendarClock,
  CheckCircle2,
  Database,
  FileSpreadsheet,
  FileText,
  Layers,
  ShieldAlert,
  Sparkles,
  Users,
  ArrowRight,
} from "lucide-react";
import { DashboardMetrics } from "@/lib/types";

interface DashboardViewProps {
  metrics: DashboardMetrics | null;
  onSelectDepartingUser: (userId: string) => void;
  onNavigateTab: (tab: string) => void;
}

export function DashboardView({
  metrics,
  onSelectDepartingUser,
  onNavigateTab,
}: DashboardViewProps) {
  if (!metrics) {
    return (
      <div className="p-8 text-sm text-slate-400">
        Loading December offboarding telemetry...
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Architecture & Engine Status Banner */}
      <div className="bg-gradient-to-r from-slate-900 via-indigo-950/60 to-slate-900 border border-indigo-500/30 rounded-2xl p-5 flex flex-wrap items-center justify-between gap-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-indigo-300">
            <Sparkles className="h-4 w-4" />
            December Bank Knowledge Continuity Command Center
          </div>
          <p className="text-sm text-slate-300">
            Active AI Engine:{" "}
            <span className="font-semibold text-white">{metrics.aiProviderMode}</span>{" "}
            &bull; Hybrid Store:{" "}
            <span className="font-semibold text-emerald-300">
              PostgreSQL 17 ({metrics.isPgVectorEnabled ? "pgvector HNSW + GIN FTS" : ".NET 10 SIMD Cosine + GIN FTS"})
            </span>
          </p>
        </div>
        <div className="flex items-center gap-2.5">
          <button
            onClick={() => onNavigateTab("vault")}
            className="px-3.5 py-2 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-medium text-slate-100 border border-slate-700 transition cursor-pointer"
          >
            Upload Handover Docs
          </button>
          <button
            onClick={() => onNavigateTab("copilot")}
            className="px-3.5 py-2 rounded-lg bg-blue-600 hover:bg-blue-500 text-xs font-semibold text-white transition flex items-center gap-1.5 cursor-pointer"
          >
            Ask Agentic MCP Copilot
            <ArrowRight className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      {/* KPI Strip */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5">
          <div className="flex items-center justify-between text-slate-400 text-xs font-medium">
            <span>December Departures</span>
            <CalendarClock className="h-4 w-4 text-amber-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-white">
              {metrics.departingInDecemberCount}
            </span>
            <span className="text-xs text-slate-400">
              across {metrics.totalTeams} authorized team(s)
            </span>
          </div>
          <div className="mt-2 text-xs text-amber-300">
            Avg Exit Readiness: <strong>{metrics.averageReadinessScore}%</strong>
          </div>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5">
          <div className="flex items-center justify-between text-slate-400 text-xs font-medium">
            <span>Indexed Knowledge Artifacts</span>
            <FileText className="h-4 w-4 text-blue-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-white">
              {metrics.totalDocuments}
            </span>
            <span className="text-xs text-slate-400">
              ({metrics.totalIndexedChunks} contextual chunks)
            </span>
          </div>
          <div className="mt-2 flex items-center gap-2 flex-wrap text-[11px] text-slate-400">
            {metrics.formatBreakdown.map((fb) => (
              <span
                key={fb.format}
                className="px-1.5 py-0.5 rounded bg-slate-800 text-slate-300"
              >
                {fb.format}: {fb.count}
              </span>
            ))}
          </div>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5">
          <div className="flex items-center justify-between text-slate-400 text-xs font-medium">
            <span>Queryable Spreadsheets (SQL/JSONB)</span>
            <FileSpreadsheet className="h-4 w-4 text-emerald-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-white">
              {metrics.totalTabularSheets}
            </span>
            <span className="text-xs text-slate-400">CSV &amp; Excel worksheets</span>
          </div>
          <div className="mt-2 text-xs text-emerald-300">
            Exact SUM / AVG &amp; Formula Extraction active
          </div>
        </div>

        <div className="bg-slate-900 border border-slate-800 rounded-xl p-5">
          <div className="flex items-center justify-between text-slate-400 text-xs font-medium">
            <span>AI Handover Gap Elicitation</span>
            <AlertTriangle className="h-4 w-4 text-rose-400" />
          </div>
          <div className="mt-2 flex items-baseline gap-2">
            <span className="text-2xl font-bold text-white">
              {metrics.openGapQuestionsCount}
            </span>
            <span className="text-xs text-slate-400">open clarifications</span>
          </div>
          <div className="mt-2 flex items-center justify-between text-xs">
            <span className="text-emerald-400 flex items-center gap-1">
              <CheckCircle2 className="h-3.5 w-3.5" />
              {metrics.answeredGapQuestionsCount} indexed into RAG
            </span>
            {metrics.unacknowledgedSecretsCount > 0 && (
              <span className="text-rose-400 flex items-center gap-1">
                <ShieldAlert className="h-3.5 w-3.5" />
                {metrics.unacknowledgedSecretsCount} secret alert(s)
              </span>
            )}
          </div>
        </div>
      </div>

      {/* Team Readiness & Upcoming Exits */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        <div className="lg:col-span-6 bg-slate-900 border border-slate-800 rounded-2xl p-6">
          <h3 className="text-sm font-semibold text-white flex items-center gap-2 mb-4">
            <Layers className="h-4 w-4 text-indigo-400" />
            Team-Wise Handover Readiness
          </h3>
          <div className="space-y-4">
            {metrics.teamReadiness.map((tr) => (
              <div
                key={tr.teamId}
                className="p-4 rounded-xl bg-slate-950/70 border border-slate-800/80 space-y-2"
              >
                <div className="flex items-center justify-between">
                  <div>
                    <div className="text-sm font-semibold text-white">
                      {tr.teamName}
                    </div>
                    <div className="text-xs text-slate-400">
                      {tr.departmentName} &bull; {tr.departingUsersCount} departing
                      &bull; {tr.documentsCount} artifact(s) &bull; {tr.openGapsCount} open gap(s)
                    </div>
                  </div>
                  <span className="text-sm font-bold text-emerald-400">
                    {tr.averageReadinessScore}%
                  </span>
                </div>
                <div className="w-full h-2 rounded-full bg-slate-800 overflow-hidden">
                  <div
                    className="h-full rounded-full bg-gradient-to-r from-blue-500 to-emerald-400 transition-all"
                    style={{ width: `${Math.min(100, tr.averageReadinessScore)}%` }}
                  />
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="lg:col-span-6 bg-slate-900 border border-slate-800 rounded-2xl p-6">
          <h3 className="text-sm font-semibold text-white flex items-center gap-2 mb-4">
            <Users className="h-4 w-4 text-amber-400" />
            December Departing Employees (User-Wise Dossiers)
          </h3>
          <div className="space-y-3">
            {metrics.upcomingExits.map((u) => (
              <div
                key={u.id}
                onClick={() => onSelectDepartingUser(u.id)}
                className="p-4 rounded-xl bg-slate-950/70 hover:bg-slate-800/70 border border-slate-800 hover:border-blue-500/40 transition cursor-pointer flex items-center justify-between gap-4"
              >
                <div>
                  <div className="text-sm font-semibold text-white">
                    {u.fullName}{" "}
                    <span className="text-xs font-normal text-slate-400">
                      ({u.employeeId})
                    </span>
                  </div>
                  <div className="text-xs text-slate-400">
                    {u.jobTitle} &bull; <strong className="text-slate-300">{u.teamName}</strong>
                  </div>
                  <div className="text-[11px] text-slate-400 mt-1">
                    Successor: <span className="text-slate-200">{u.successorName}</span>
                  </div>
                </div>
                <div className="text-right shrink-0">
                  <span className="inline-block text-xs font-medium px-2.5 py-0.5 rounded-full bg-amber-500/15 text-amber-300 border border-amber-500/30">
                    Exit: {u.exitDate}
                  </span>
                  <div className="text-xs font-semibold text-emerald-400 mt-1.5">
                    Readiness: {u.readinessScore}%
                  </div>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}
