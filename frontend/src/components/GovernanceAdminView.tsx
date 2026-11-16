"use client";

import React, { useEffect, useState } from "react";
import {
  CheckCircle2,
  ClipboardList,
  PlusCircle,
  ServerCog,
  ShieldAlert,
} from "lucide-react";
import { velitApi } from "@/lib/api";
import {
  AuditLog,
  Department,
  SecretScanFinding,
  Team,
} from "@/lib/types";

interface GovernanceAdminViewProps {
  token: string;
  isAdmin: boolean;
  onDataChanged: () => void;
}

export function GovernanceAdminView({
  token,
  isAdmin,
  onDataChanged,
}: GovernanceAdminViewProps) {
  const [departments, setDepartments] = useState<Department[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [secrets, setSecrets] = useState<SecretScanFinding[]>([]);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);
  const [health, setHealth] = useState<Record<string, unknown> | null>(null);

  const [deptForm, setDeptForm] = useState({
    name: "",
    code: "",
    description: "",
  });
  const [teamForm, setTeamForm] = useState({
    departmentId: "",
    name: "",
    description: "",
    adGroupName: "CN=SG-VelitIQ-",
    entraGroupId: "",
    managerEmail: "",
  });

  async function loadGovernanceData() {
    const [depts, tms, secs, logs, hlth] = await Promise.all([
      velitApi.getDepartments(token),
      velitApi.getTeams(token),
      velitApi.getSecretFindings(token),
      velitApi.getAuditLogs(token, 60),
      velitApi.getHealth(),
    ]);
    setDepartments(depts);
    setTeams(tms);
    setSecrets(secs);
    setAuditLogs(logs);
    setHealth(hlth);
    if (depts.length > 0 && !teamForm.departmentId) {
      setTeamForm((prev) => ({ ...prev, departmentId: depts[0].id }));
    }
  }

  useEffect(() => {
    loadGovernanceData();
  }, [token]);

  async function handleCreateDept(e: React.FormEvent) {
    e.preventDefault();
    await velitApi.createDepartment(token, deptForm);
    setDeptForm({ name: "", code: "", description: "" });
    await loadGovernanceData();
    onDataChanged();
  }

  async function handleCreateTeam(e: React.FormEvent) {
    e.preventDefault();
    await velitApi.createTeam(token, teamForm);
    setTeamForm((prev) => ({
      ...prev,
      name: "",
      description: "",
      adGroupName: "CN=SG-VelitIQ-",
      entraGroupId: "",
      managerEmail: "",
    }));
    await loadGovernanceData();
    onDataChanged();
  }

  async function handleAckSecret(id: string) {
    await velitApi.acknowledgeSecret(token, id);
    await loadGovernanceData();
    onDataChanged();
  }

  return (
    <div className="space-y-6">
      {/* Office Windows Server & Azure Cutover Readiness Panel */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-3">
        <h2 className="text-base font-bold text-white flex items-center gap-2">
          <ServerCog className="h-5 w-5 text-indigo-400" />
          Office On-Prem Windows Server &amp; Azure AI Foundry Cutover Status
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3 text-xs">
          <div className="p-3.5 rounded-xl bg-slate-950 border border-slate-800">
            <div className="text-slate-400">AI Provider Configuration</div>
            <div className="font-semibold text-white mt-1">
              {String(health?.aiMode || "Checking...")}
            </div>
            <p className="text-[11px] text-slate-400 mt-1">
              Set <code>AzureFoundry:AzureOpenAiUrl</code> &amp;{" "}
              <code>AzureFoundry:ApiKey</code> in <code>appsettings.json</code>{" "}
              at the office to switch live to Azure AI Foundry.
            </p>
          </div>

          <div className="p-3.5 rounded-xl bg-slate-950 border border-slate-800">
            <div className="text-slate-400">Identity &amp; Directory Mode</div>
            <div className="font-semibold text-white mt-1">
              {String(health?.authMode || "HybridAdAndLocal")} (LDAP:{" "}
              {health?.ldapEnabled ? "Enabled" : "Standby"} | Entra ID:{" "}
              {health?.entraIdEnabled ? "Enabled" : "Ready"})
            </div>
            <p className="text-[11px] text-slate-400 mt-1">
              Enable <code>Auth:Ldap:Enabled = true</code> for On-Prem AD now;
              flip <code>Auth:EntraId:Enabled = true</code> on Azure migration.
            </p>
          </div>

          <div className="p-3.5 rounded-xl bg-slate-950 border border-slate-800">
            <div className="text-slate-400">Storage &amp; MCP Endpoints</div>
            <div className="font-semibold text-white mt-1">
              {String(health?.storageProvider || "LocalDisk")}
            </div>
            <p className="text-[11px] text-slate-400 mt-1">
              MCP Streamable HTTP live at <code>/mcp</code> + OAuth Discovery at{" "}
              <code>/.well-known/oauth-protected-resource</code>.
            </p>
          </div>
        </div>
      </div>

      {/* Admin Organization & AD Mapping Provisioning */}
      {isAdmin && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          <form
            onSubmit={handleCreateDept}
            className="bg-slate-900 border border-slate-800 rounded-2xl p-5 space-y-3 text-xs"
          >
            <h3 className="text-sm font-bold text-white flex items-center gap-2">
              <PlusCircle className="h-4 w-4 text-blue-400" />
              Create Bank Department
            </h3>
            <div className="grid grid-cols-3 gap-2">
              <input
                required
                placeholder="Department Name"
                value={deptForm.name}
                onChange={(e) =>
                  setDeptForm({ ...deptForm, name: e.target.value })
                }
                className="col-span-2 rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
              />
              <input
                required
                placeholder="Code (e.g. CIB)"
                value={deptForm.code}
                onChange={(e) =>
                  setDeptForm({ ...deptForm, code: e.target.value })
                }
                className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
              />
            </div>
            <input
              placeholder="Description"
              value={deptForm.description}
              onChange={(e) =>
                setDeptForm({ ...deptForm, description: e.target.value })
              }
              className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
            />
            <button
              type="submit"
              className="px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-500 font-semibold text-white cursor-pointer"
            >
              Add Department
            </button>
          </form>

          <form
            onSubmit={handleCreateTeam}
            className="bg-slate-900 border border-slate-800 rounded-2xl p-5 space-y-3 text-xs"
          >
            <h3 className="text-sm font-bold text-white flex items-center gap-2">
              <PlusCircle className="h-4 w-4 text-emerald-400" />
              Create Team with AD &amp; Entra Group Mapping
            </h3>
            <div className="grid grid-cols-2 gap-2">
              <select
                value={teamForm.departmentId}
                onChange={(e) =>
                  setTeamForm({ ...teamForm, departmentId: e.target.value })
                }
                className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
              >
                {departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
              <input
                required
                placeholder="Team Name"
                value={teamForm.name}
                onChange={(e) =>
                  setTeamForm({ ...teamForm, name: e.target.value })
                }
                className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
              />
              <input
                placeholder="On-Prem AD Group CN"
                value={teamForm.adGroupName}
                onChange={(e) =>
                  setTeamForm({ ...teamForm, adGroupName: e.target.value })
                }
                className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
              />
              <input
                placeholder="Azure Entra Group ObjectId"
                value={teamForm.entraGroupId}
                onChange={(e) =>
                  setTeamForm({ ...teamForm, entraGroupId: e.target.value })
                }
                className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
              />
            </div>
            <button
              type="submit"
              className="px-4 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 font-semibold text-white cursor-pointer"
            >
              Add Mapped Team
            </button>
          </form>
        </div>
      )}

      {/* Mapped Teams Directory */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-3">
        <h3 className="text-sm font-bold text-white">
          Active Directory &harr; Entra ID Team Security Group Mappings ({teams.length})
        </h3>
        <div className="overflow-x-auto">
          <table className="w-full text-left text-xs text-slate-300">
            <thead>
              <tr className="border-b border-slate-800 text-slate-400">
                <th className="py-2 px-3">Department</th>
                <th className="py-2 px-3">Team</th>
                <th className="py-2 px-3">On-Prem AD Group</th>
                <th className="py-2 px-3">Target Entra ID Group</th>
              </tr>
            </thead>
            <tbody>
              {teams.map((t) => (
                <tr key={t.id} className="border-b border-slate-900">
                  <td className="py-2 px-3">{t.departmentName}</td>
                  <td className="py-2 px-3 font-semibold text-white">{t.name}</td>
                  <td className="py-2 px-3 font-mono text-indigo-300">
                    {t.adGroupName || "—"}
                  </td>
                  <td className="py-2 px-3 font-mono text-emerald-300">
                    {t.entraGroupId || "—"}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {/* Secret Guardrail Findings */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-3">
        <h3 className="text-sm font-bold text-white flex items-center gap-2">
          <ShieldAlert className="h-4 w-4 text-rose-400" />
          Credential &amp; Sensitive Data Guardrail Alerts ({secrets.length})
        </h3>
        {secrets.length === 0 ? (
          <p className="text-xs text-emerald-400 flex items-center gap-1.5">
            <CheckCircle2 className="h-4 w-4" />
            Zero unacknowledged credential leaks across indexed handover artifacts.
          </p>
        ) : (
          <div className="space-y-2">
            {secrets.map((s) => (
              <div
                key={s.id}
                className="p-3 rounded-xl bg-slate-950 border border-slate-800 flex items-center justify-between text-xs"
              >
                <div>
                  <span className="font-semibold text-rose-300">
                    [{s.severity}] {s.ruleName}
                  </span>{" "}
                  at {s.location} &bull; Redacted: <code>{s.redactedPreview}</code>
                </div>
                {isAdmin && !s.isAcknowledged && (
                  <button
                    onClick={() => handleAckSecret(s.id)}
                    className="px-3 py-1 rounded bg-slate-800 hover:bg-slate-700 text-slate-200 cursor-pointer"
                  >
                    Acknowledge
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Immutable Security Audit Trail */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-3">
        <h3 className="text-sm font-bold text-white flex items-center gap-2">
          <ClipboardList className="h-4 w-4 text-amber-400" />
          Immutable Bank Governance &amp; MCP Audit Log
        </h3>
        <div className="overflow-x-auto max-h-80 overflow-y-auto">
          <table className="w-full text-left text-[11px] text-slate-300">
            <thead>
              <tr className="border-b border-slate-800 text-slate-400">
                <th className="py-2 px-2">Timestamp</th>
                <th className="py-2 px-2">Actor</th>
                <th className="py-2 px-2">Role</th>
                <th className="py-2 px-2">Action</th>
                <th className="py-2 px-2">Details</th>
              </tr>
            </thead>
            <tbody>
              {auditLogs.map((log) => (
                <tr key={log.id} className="border-b border-slate-900">
                  <td className="py-1.5 px-2 whitespace-nowrap text-slate-400">
                    {new Date(log.timestamp).toLocaleString()}
                  </td>
                  <td className="py-1.5 px-2">{log.actorEmail || "system"}</td>
                  <td className="py-1.5 px-2">{log.actorRole}</td>
                  <td className="py-1.5 px-2 font-mono text-indigo-300">
                    {log.action}
                  </td>
                  <td className="py-1.5 px-2 font-mono text-slate-400 max-w-md truncate">
                    {log.detailsJson}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
