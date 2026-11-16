"use client";

import React, { useCallback, useEffect, useState } from "react";
import {
  Bot,
  Building2,
  FolderKanban,
  LayoutDashboard,
  LogOut,
  ShieldCheck,
  Users,
} from "lucide-react";
import { velitApi } from "@/lib/api";
import { AuthTokenResponse, DashboardMetrics } from "@/lib/types";
import { LoginView } from "@/components/LoginView";
import { DashboardView } from "@/components/DashboardView";
import { OrganizationHubView } from "@/components/OrganizationHubView";
import { KnowledgeVaultView } from "@/components/KnowledgeVaultView";
import { AgenticCopilotView } from "@/components/AgenticCopilotView";
import { GovernanceAdminView } from "@/components/GovernanceAdminView";

const STORAGE_KEY = "velitiq_auth_session_v1";

export default function HomePage() {
  const [auth, setAuth] = useState<AuthTokenResponse | null>(null);
  const [activeTab, setActiveTab] = useState<string>("dashboard");
  const [focusedUserId, setFocusedUserId] = useState<string | null>(null);
  const [dashboard, setDashboard] = useState<DashboardMetrics | null>(null);

  useEffect(() => {
    try {
      const saved = sessionStorage.getItem(STORAGE_KEY);
      if (saved) {
        setAuth(JSON.parse(saved));
      }
    } catch {
      // ignore
    }
  }, []);

  const refreshDashboard = useCallback(async () => {
    if (!auth?.accessToken) return;
    try {
      const m = await velitApi.getDashboard(auth.accessToken);
      setDashboard(m);
    } catch {
      // If token expired or invalid, reset session
      sessionStorage.removeItem(STORAGE_KEY);
      setAuth(null);
    }
  }, [auth?.accessToken]);

  useEffect(() => {
    if (auth?.accessToken) {
      refreshDashboard();
    }
  }, [auth?.accessToken, refreshDashboard]);

  function handleAuthenticated(session: AuthTokenResponse) {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    setAuth(session);
    setActiveTab("dashboard");
  }

  function handleLogout() {
    sessionStorage.removeItem(STORAGE_KEY);
    setAuth(null);
    setDashboard(null);
  }

  if (!auth) {
    return <LoginView onAuthenticated={handleAuthenticated} />;
  }

  const canViewGovernance =
    auth.user.role === "Admin" || auth.user.role === "CrossTeamViewer";
  const canAdminOrLead =
    auth.user.role === "Admin" || auth.user.role === "TeamLead";

  const navItems = [
    { id: "dashboard", label: "Readiness Dashboard", icon: LayoutDashboard },
    { id: "org", label: "Teams & Departing Staff", icon: Users },
    { id: "vault", label: "Knowledge Vault & Upload", icon: FolderKanban },
    { id: "copilot", label: "Agentic MCP Copilot", icon: Bot },
    ...(canViewGovernance
      ? [{ id: "governance", label: "Governance & Audit", icon: ShieldCheck }]
      : []),
  ];

  return (
    <div className="min-h-screen bg-slate-950 flex flex-col">
      {/* Top Enterprise Header */}
      <header className="sticky top-0 z-30 bg-slate-900/95 backdrop-blur border-b border-slate-800 px-4 sm:px-8 py-3.5">
        <div className="max-w-7xl mx-auto flex flex-wrap items-center justify-between gap-4">
          <div className="flex items-center gap-3">
            <div className="h-9 w-9 rounded-xl bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center">
              <Building2 className="h-5 w-5 text-white" />
            </div>
            <div>
              <div className="text-sm font-bold text-white tracking-tight">
                Horizon VelitIQ
              </div>
              <div className="text-[11px] text-slate-400">
                Bank Offboarding &amp; Agentic MCP Knowledge Platform
              </div>
            </div>
          </div>

          <nav className="flex items-center gap-1 flex-wrap">
            {navItems.map((item) => {
              const Icon = item.icon;
              const active = activeTab === item.id;
              return (
                <button
                  key={item.id}
                  onClick={() => setActiveTab(item.id)}
                  data-testid={`nav-${item.id}`}
                  className={`px-3.5 py-2 rounded-xl text-xs font-medium transition flex items-center gap-1.5 cursor-pointer ${
                    active
                      ? "bg-blue-600 text-white shadow"
                      : "text-slate-300 hover:bg-slate-800 hover:text-white"
                  }`}
                >
                  <Icon className="h-3.5 w-3.5" />
                  {item.label}
                </button>
              );
            })}
          </nav>

          <div className="flex items-center gap-3">
            <div className="text-right hidden sm:block">
              <div className="text-xs font-semibold text-white">
                {auth.user.displayName}
              </div>
              <div className="text-[11px] text-slate-400">
                <span className="text-indigo-300 font-medium">
                  {auth.user.role}
                </span>{" "}
                &bull; {auth.user.primaryTeamName || "All Departments"}
              </div>
            </div>

            <button
              onClick={handleLogout}
              data-testid="logout-btn"
              title="Switch Role / Sign Out"
              className="px-3 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-xs font-medium text-slate-200 flex items-center gap-1.5 cursor-pointer"
            >
              <LogOut className="h-3.5 w-3.5" />
              Switch User
            </button>
          </div>
        </div>
      </header>

      {/* Main Content */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-8 py-6">
        {activeTab === "dashboard" && (
          <DashboardView
            metrics={dashboard}
            onSelectDepartingUser={(uid) => {
              setFocusedUserId(uid);
              setActiveTab("org");
            }}
            onNavigateTab={setActiveTab}
          />
        )}

        {activeTab === "org" && (
          <OrganizationHubView
            token={auth.accessToken}
            initialUserId={focusedUserId}
            canAdminOrLead={canAdminOrLead}
            onDataChanged={refreshDashboard}
          />
        )}

        {activeTab === "vault" && (
          <KnowledgeVaultView
            token={auth.accessToken}
            onDataChanged={refreshDashboard}
          />
        )}

        {activeTab === "copilot" && (
          <AgenticCopilotView token={auth.accessToken} />
        )}

        {activeTab === "governance" && canViewGovernance && (
          <GovernanceAdminView
            token={auth.accessToken}
            isAdmin={auth.user.role === "Admin"}
            onDataChanged={refreshDashboard}
          />
        )}
      </main>
    </div>
  );
}
