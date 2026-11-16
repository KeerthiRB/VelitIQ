"use client";

import React, { useEffect, useState } from "react";
import {
  Building2,
  KeyRound,
  Lock,
  ShieldCheck,
  Sparkles,
  UserCheck,
  AlertCircle,
  MonitorCheck,
} from "lucide-react";
import { velitApi } from "@/lib/api";
import { AuthTokenResponse, DemoPersona } from "@/lib/types";

interface LoginViewProps {
  onAuthenticated: (auth: AuthTokenResponse) => void;
}

export function LoginView({ onAuthenticated }: LoginViewProps) {
  const [personas, setPersonas] = useState<DemoPersona[]>([]);
  const [usernameOrEmail, setUsernameOrEmail] = useState("admin@horizonbank.com");
  const [password, setPassword] = useState("VelitIQ@2026!");
  const [domain, setDomain] = useState("HORIZONBANK");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    velitApi
      .getDemoPersonas()
      .then(setPersonas)
      .catch(() => setPersonas([]));
  }, []);

  async function handleFormLogin(e: React.FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const res = await velitApi.loginCredentials(usernameOrEmail, password, domain);
      onAuthenticated(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Authentication failed");
    } finally {
      setLoading(false);
    }
  }

  async function handlePersonaLogin(email: string) {
    setError(null);
    setLoading(true);
    try {
      const res = await velitApi.loginDemoPersona(email);
      onAuthenticated(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Demo login failed");
    } finally {
      setLoading(false);
    }
  }

  async function handleWindowsSso() {
    setError(null);
    setLoading(true);
    try {
      const res = await velitApi.loginWindowsSso();
      onAuthenticated(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Windows Integrated SSO unavailable");
    } finally {
      setLoading(false);
    }
  }

  const roleBadgeColor = (role: string) => {
    switch (role) {
      case "Admin":
        return "bg-amber-500/15 text-amber-300 border-amber-500/30";
      case "CrossTeamViewer":
        return "bg-purple-500/15 text-purple-300 border-purple-500/30";
      case "TeamLead":
        return "bg-emerald-500/15 text-emerald-300 border-emerald-500/30";
      default:
        return "bg-sky-500/15 text-sky-300 border-sky-500/30";
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col justify-center py-10 px-4 sm:px-6 lg:px-8">
      <div className="max-w-6xl w-full mx-auto grid grid-cols-1 lg:grid-cols-12 gap-8 items-start">
        {/* Left: Active Directory / Domain Login Card */}
        <div className="lg:col-span-5 bg-slate-900/90 border border-slate-800 rounded-2xl p-8 shadow-2xl">
          <div className="flex items-center gap-3 mb-6">
            <div className="h-11 w-11 rounded-xl bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center shadow-lg">
              <Building2 className="h-6 w-6 text-white" />
            </div>
            <div>
              <h1 className="text-xl font-bold tracking-tight text-white">
                Horizon VelitIQ
              </h1>
              <p className="text-xs text-slate-400">
                December Offboarding & Agentic MCP Knowledge Vault
              </p>
            </div>
          </div>

          <div className="mb-6 p-3.5 rounded-xl bg-blue-950/40 border border-blue-800/40 text-xs text-blue-200 flex items-start gap-2.5">
            <ShieldCheck className="h-4 w-4 text-blue-400 shrink-0 mt-0.5" />
            <div>
              <span className="font-semibold">Enterprise Identity Ready:</span>{" "}
              Supports On-Prem Active Directory (LDAP &amp; Windows Integrated SSO),
              Bootstrap Local Accounts, and zero-rewrite Azure Entra ID OIDC cutover.
            </div>
          </div>

          {error && (
            <div className="mb-5 p-3 rounded-xl bg-red-950/60 border border-red-800 text-xs text-red-200 flex items-center gap-2">
              <AlertCircle className="h-4 w-4 text-red-400 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          <form onSubmit={handleFormLogin} className="space-y-4">
            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1.5">
                AD Domain
              </label>
              <input
                type="text"
                value={domain}
                onChange={(e) => setDomain(e.target.value)}
                className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3.5 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                placeholder="HORIZONBANK"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1.5">
                AD Username or Bank Email
              </label>
              <input
                type="text"
                value={usernameOrEmail}
                onChange={(e) => setUsernameOrEmail(e.target.value)}
                required
                className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3.5 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
                placeholder="sAMAccountName or user@horizonbank.com"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1.5">
                Password
              </label>
              <input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3.5 py-2 text-sm text-white focus:outline-none focus:border-blue-500"
              />
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full py-2.5 px-4 rounded-lg bg-blue-600 hover:bg-blue-500 disabled:opacity-50 text-sm font-semibold text-white transition flex items-center justify-center gap-2 cursor-pointer"
            >
              <KeyRound className="h-4 w-4" />
              {loading ? "Authenticating..." : "Sign In (AD / LDAP / Bank Credentials)"}
            </button>
          </form>

          <div className="mt-4 pt-4 border-t border-slate-800">
            <button
              type="button"
              onClick={handleWindowsSso}
              disabled={loading}
              className="w-full py-2 px-4 rounded-lg bg-slate-800 hover:bg-slate-700 text-xs font-medium text-slate-200 transition flex items-center justify-center gap-2 cursor-pointer"
            >
              <MonitorCheck className="h-4 w-4 text-emerald-400" />
              Sign In with Windows Integrated Domain SSO
            </button>
          </div>
        </div>

        {/* Right: RBAC Persona Quick-Select for Testing Team Isolation vs Cross-Team vs Admin */}
        <div className="lg:col-span-7 bg-slate-900/60 border border-slate-800 rounded-2xl p-8">
          <div className="flex items-center justify-between mb-4">
            <div>
              <h2 className="text-base font-semibold text-white flex items-center gap-2">
                <Sparkles className="h-4 w-4 text-amber-400" />
                RBAC &amp; Chinese-Wall Verification Personas
              </h2>
              <p className="text-xs text-slate-400 mt-0.5">
                One-click sign-in to test Team-level isolation, Cross-Team shared artifacts, and Global Admin governance.
              </p>
            </div>
            <span className="text-[11px] px-2.5 py-1 rounded-full bg-slate-800 text-slate-300 border border-slate-700">
              .NET 10 + MCP Suite
            </span>
          </div>

          <div className="space-y-3">
            {personas.map((p) => (
              <div
                key={p.email}
                onClick={() => !loading && handlePersonaLogin(p.email)}
                data-testid={`persona-${p.email}`}
                className="group p-4 rounded-xl bg-slate-950/80 hover:bg-slate-800/80 border border-slate-800 hover:border-blue-500/50 transition cursor-pointer flex items-start justify-between gap-4"
              >
                <div className="space-y-1">
                  <div className="flex items-center gap-2.5 flex-wrap">
                    <span className="text-sm font-semibold text-white group-hover:text-blue-300 transition">
                      {p.displayName}
                    </span>
                    <span
                      className={`text-[11px] font-medium px-2 py-0.5 rounded-full border ${roleBadgeColor(
                        p.role
                      )}`}
                    >
                      {p.role}
                    </span>
                    <span className="text-[11px] text-slate-400">
                      {p.departmentName} &bull; {p.teamName}
                    </span>
                  </div>
                  <p className="text-xs text-slate-400">{p.scenarioHighlight}</p>
                </div>
                <button
                  type="button"
                  className="shrink-0 px-3 py-1.5 rounded-lg bg-blue-600/20 group-hover:bg-blue-600 text-blue-300 group-hover:text-white text-xs font-medium transition flex items-center gap-1.5"
                >
                  <UserCheck className="h-3.5 w-3.5" />
                  Enter
                </button>
              </div>
            ))}
          </div>

          <div className="mt-6 p-4 rounded-xl bg-slate-950/70 border border-slate-800/80 text-xs text-slate-400 flex items-center gap-3">
            <Lock className="h-4 w-4 text-slate-500 shrink-0" />
            <span>
              Powered by <strong className="text-slate-200">Keerthi_RB</strong> NuGet ecosystem:{" "}
              <code className="text-indigo-300">McpHttpServer v1.2.0</code>,{" "}
              <code className="text-indigo-300">Mcp.Client.Net v1.0.1</code>, and{" "}
              <code className="text-indigo-300">Keerukee.MCPServer.Stdio v1.0.3</code>.
            </span>
          </div>
        </div>
      </div>
    </div>
  );
}
