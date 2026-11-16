"use client";

import React, { useEffect, useState } from "react";
import {
  Building,
  CheckCircle2,
  FileText,
  HelpCircle,
  Plus,
  Send,
  UserMinus,
  Users,
  Shield,
} from "lucide-react";
import { velitApi } from "@/lib/api";
import {
  Department,
  DepartingUserProfile,
  HandoverGapQuestion,
  KnowledgeDocument,
  Team,
} from "@/lib/types";

interface OrganizationHubViewProps {
  token: string;
  initialUserId?: string | null;
  canAdminOrLead: boolean;
  onDataChanged: () => void;
}

export function OrganizationHubView({
  token,
  initialUserId,
  canAdminOrLead,
  onDataChanged,
}: OrganizationHubViewProps) {
  const [departments, setDepartments] = useState<Department[]>([]);
  const [teams, setTeams] = useState<Team[]>([]);
  const [users, setUsers] = useState<DepartingUserProfile[]>([]);

  const [selectedDeptId, setSelectedDeptId] = useState<string>("");
  const [selectedTeamId, setSelectedTeamId] = useState<string>("");
  const [selectedUserId, setSelectedUserId] = useState<string>(initialUserId || "");

  const [userDetail, setUserDetail] = useState<{
    profile: DepartingUserProfile;
    canAccessTeamPrivate: boolean;
    canWrite: boolean;
    documents: KnowledgeDocument[];
    gapQuestions: HandoverGapQuestion[];
  } | null>(null);

  const [answerDrafts, setAnswerDrafts] = useState<Record<string, string>>({});
  const [submittingGapId, setSubmittingGapId] = useState<string | null>(null);

  const [showAddModal, setShowAddModal] = useState(false);
  const [newProfile, setNewProfile] = useState({
    teamId: "",
    fullName: "",
    email: "",
    employeeId: "HB-EMP-",
    jobTitle: "",
    exitDate: "2026-12-22",
    successorName: "",
    successorEmail: "",
  });

  useEffect(() => {
    Promise.all([
      velitApi.getDepartments(token),
      velitApi.getTeams(token),
      velitApi.getDepartingUsers(token),
    ]).then(([depts, tms, usrs]) => {
      setDepartments(depts);
      setTeams(tms);
      setUsers(usrs);
      if (!selectedUserId && usrs.length > 0) {
        setSelectedUserId(initialUserId || usrs[0].id);
      }
      if (tms.length > 0) {
        setNewProfile((prev) => ({ ...prev, teamId: tms[0].id }));
      }
    });
  }, [token, initialUserId]);

  useEffect(() => {
    if (!selectedUserId) return;
    velitApi
      .getDepartingUserDetails(token, selectedUserId)
      .then(setUserDetail)
      .catch(() => setUserDetail(null));
  }, [token, selectedUserId]);

  const filteredTeams = selectedDeptId
    ? teams.filter((t) => t.departmentId === selectedDeptId)
    : teams;

  const filteredUsers = users.filter((u) => {
    if (selectedDeptId && u.departmentId !== selectedDeptId) return false;
    if (selectedTeamId && u.teamId !== selectedTeamId) return false;
    return true;
  });

  async function handleAnswerGap(gapId: string) {
    const answer = answerDrafts[gapId]?.trim();
    if (!answer || !selectedUserId) return;
    setSubmittingGapId(gapId);
    try {
      await velitApi.answerGapQuestion(token, gapId, answer);
      setAnswerDrafts((prev) => ({ ...prev, [gapId]: "" }));
      const refreshed = await velitApi.getDepartingUserDetails(token, selectedUserId);
      setUserDetail(refreshed);
      const updatedUsers = await velitApi.getDepartingUsers(token);
      setUsers(updatedUsers);
      onDataChanged();
    } finally {
      setSubmittingGapId(null);
    }
  }

  async function handleCreateDepartingUser(e: React.FormEvent) {
    e.preventDefault();
    const created = await velitApi.createDepartingUser(token, newProfile);
    const updatedUsers = await velitApi.getDepartingUsers(token);
    setUsers(updatedUsers);
    setSelectedUserId(created.id);
    setShowAddModal(false);
    onDataChanged();
  }

  return (
    <div className="space-y-6">
      {/* Top Filter Bar: Department -> Team -> User Categorization */}
      <div className="bg-slate-900 border border-slate-800 rounded-2xl p-5 flex flex-wrap items-center justify-between gap-4">
        <div className="flex flex-wrap items-center gap-3">
          <div className="flex items-center gap-2 text-xs font-semibold text-slate-300">
            <Building className="h-4 w-4 text-blue-400" />
            <span>Taxonomy Filter:</span>
          </div>

          <select
            value={selectedDeptId}
            onChange={(e) => {
              setSelectedDeptId(e.target.value);
              setSelectedTeamId("");
            }}
            className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-1.5 text-xs text-white"
          >
            <option value="">All Departments ({departments.length})</option>
            {departments.map((d) => (
              <option key={d.id} value={d.id}>
                {d.code} — {d.name}
              </option>
            ))}
          </select>

          <select
            value={selectedTeamId}
            onChange={(e) => setSelectedTeamId(e.target.value)}
            className="rounded-lg bg-slate-950 border border-slate-700 px-3 py-1.5 text-xs text-white"
          >
            <option value="">All Teams ({filteredTeams.length})</option>
            {filteredTeams.map((t) => (
              <option key={t.id} value={t.id}>
                {t.name}
              </option>
            ))}
          </select>
        </div>

        {canAdminOrLead && (
          <button
            onClick={() => setShowAddModal(true)}
            className="px-3.5 py-2 rounded-lg bg-blue-600 hover:bg-blue-500 text-xs font-semibold text-white flex items-center gap-1.5 cursor-pointer"
          >
            <Plus className="h-3.5 w-3.5" />
            Register December Departing Staff
          </button>
        )}
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Left List of Categorized Departing Staff */}
        <div className="lg:col-span-4 space-y-3">
          {filteredUsers.map((u) => {
            const active = u.id === selectedUserId;
            return (
              <div
                key={u.id}
                onClick={() => setSelectedUserId(u.id)}
                className={`p-4 rounded-xl border transition cursor-pointer ${
                  active
                    ? "bg-blue-950/40 border-blue-500"
                    : "bg-slate-900 border-slate-800 hover:border-slate-700"
                }`}
              >
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm font-semibold text-white">
                    {u.fullName}
                  </span>
                  <span className="text-[11px] px-2 py-0.5 rounded-full bg-amber-500/15 text-amber-300 border border-amber-500/30">
                    {u.exitDate}
                  </span>
                </div>
                <div className="text-xs text-slate-400 mt-1">{u.jobTitle}</div>
                <div className="mt-2 flex items-center justify-between text-[11px]">
                  <span className="text-indigo-300 font-medium">
                    {u.departmentName} &rarr; {u.teamName}
                  </span>
                  <span className="text-emerald-400 font-semibold">
                    {u.readinessScore}% Ready
                  </span>
                </div>
              </div>
            );
          })}
        </div>

        {/* Right Dossier & Active Gap Elicitation Panel */}
        <div className="lg:col-span-8 space-y-6">
          {userDetail ? (
            <>
              <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-4">
                <div className="flex flex-wrap items-start justify-between gap-4">
                  <div>
                    <div className="flex items-center gap-2.5">
                      <UserMinus className="h-5 w-5 text-amber-400" />
                      <h2 className="text-lg font-bold text-white">
                        {userDetail.profile.fullName}
                      </h2>
                      <span className="text-xs px-2.5 py-0.5 rounded-full bg-slate-800 text-slate-300">
                        {userDetail.profile.employeeId}
                      </span>
                    </div>
                    <p className="text-xs text-slate-400 mt-1">
                      {userDetail.profile.jobTitle} &bull;{" "}
                      <strong className="text-slate-200">
                        {userDetail.profile.departmentName} / {userDetail.profile.teamName}
                      </strong>{" "}
                      &bull; Successor:{" "}
                      <strong className="text-emerald-300">
                        {userDetail.profile.successorName}
                      </strong>
                    </p>
                  </div>

                  <div className="text-right">
                    <div className="text-2xl font-bold text-emerald-400">
                      {userDetail.profile.readinessScore}%
                    </div>
                    <div className="text-[11px] text-slate-400">
                      Exit Readiness Score
                    </div>
                  </div>
                </div>

                {!userDetail.canAccessTeamPrivate && (
                  <div className="p-3 rounded-xl bg-amber-950/40 border border-amber-700/40 text-xs text-amber-200 flex items-center gap-2">
                    <Shield className="h-4 w-4 text-amber-400 shrink-0" />
                    <span>
                      Chinese-Wall Policy Active: Showing only{" "}
                      <strong>Shared Cross-Team</strong> documents for this team.
                      Team-private models &amp; runbooks are restricted to{" "}
                      <strong>{userDetail.profile.teamName}</strong> members, Auditors, and Global Admins.
                    </span>
                  </div>
                )}

                {userDetail.profile.systemsOwned.length > 0 && (
                  <div className="flex flex-wrap gap-1.5">
                    {userDetail.profile.systemsOwned.map((sys) => (
                      <span
                        key={sys}
                        className="text-[11px] font-mono px-2 py-0.5 rounded bg-indigo-950/70 text-indigo-300 border border-indigo-800/50"
                      >
                        {sys}
                      </span>
                    ))}
                  </div>
                )}

                <div className="p-4 rounded-xl bg-slate-950 border border-slate-800 text-xs text-slate-300 whitespace-pre-wrap leading-relaxed">
                  {userDetail.profile.executiveSummary ||
                    "Upload documents to synthesize executive handover dossier."}
                </div>

                {/* User's Categorized Documents */}
                <div>
                  <h3 className="text-xs font-semibold uppercase tracking-wider text-slate-400 mb-2.5 flex items-center gap-1.5">
                    <FileText className="h-3.5 w-3.5 text-blue-400" />
                    Visible Handover Documents ({userDetail.documents.length})
                  </h3>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                    {userDetail.documents.map((doc) => (
                      <div
                        key={doc.id}
                        className="p-3 rounded-xl bg-slate-950/80 border border-slate-800 text-xs space-y-1"
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="font-semibold text-white truncate">
                            {doc.title}
                          </span>
                          <span className="px-2 py-0.5 rounded bg-slate-800 text-[10px] text-slate-300 shrink-0">
                            {doc.format}
                          </span>
                        </div>
                        <div className="text-[11px] text-slate-400 truncate">
                          {doc.originalFileName} &bull;{" "}
                          <span className="text-indigo-300">{doc.visibility}</span>
                        </div>
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              {/* Active AI Handover Gap Interviewer */}
              <div className="bg-slate-900 border border-slate-800 rounded-2xl p-6 space-y-4">
                <div>
                  <h3 className="text-sm font-semibold text-white flex items-center gap-2">
                    <HelpCircle className="h-4 w-4 text-amber-400" />
                    AI Handover Gap Elicitation Q&amp;A ({userDetail.gapQuestions.length})
                  </h3>
                  <p className="text-xs text-slate-400 mt-0.5">
                    Targeted questions generated during document ingestion to capture tacit tribal knowledge before December exit. Every submitted answer is automatically embedded &amp; indexed into Hybrid RAG.
                  </p>
                </div>

                {userDetail.gapQuestions.length === 0 ? (
                  <p className="text-xs text-slate-500">
                    No handover gap questions visible for your current team scope.
                  </p>
                ) : (
                  <div className="space-y-3">
                    {userDetail.gapQuestions.map((g) => (
                      <div
                        key={g.id}
                        className="p-4 rounded-xl bg-slate-950 border border-slate-800 space-y-2.5"
                      >
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-[11px] px-2 py-0.5 rounded bg-indigo-500/15 text-indigo-300 border border-indigo-500/30">
                            {g.category} &bull; {g.priority} Priority
                          </span>
                          <span
                            className={`text-[11px] font-medium flex items-center gap-1 ${
                              g.status === "Answered"
                                ? "text-emerald-400"
                                : "text-amber-400"
                            }`}
                          >
                            {g.status === "Answered" && (
                              <CheckCircle2 className="h-3.5 w-3.5" />
                            )}
                            {g.status === "Answered"
                              ? "Answered & Indexed in RAG"
                              : "Awaiting Author Clarification"}
                          </span>
                        </div>

                        <p className="text-xs font-medium text-slate-100 leading-relaxed">
                          {g.question}
                        </p>

                        {g.status === "Answered" ? (
                          <div className="p-3 rounded-lg bg-emerald-950/30 border border-emerald-800/40 text-xs text-emerald-200">
                            <strong>Verified Clarification ({g.answeredByName}):</strong>{" "}
                            {g.answer}
                          </div>
                        ) : userDetail.canWrite ? (
                          <div className="flex gap-2">
                            <input
                              type="text"
                              value={answerDrafts[g.id] || ""}
                              onChange={(e) =>
                                setAnswerDrafts((prev) => ({
                                  ...prev,
                                  [g.id]: e.target.value,
                                }))
                              }
                              placeholder="Enter clarification to index into team knowledge base..."
                              className="flex-1 rounded-lg bg-slate-900 border border-slate-700 px-3 py-2 text-xs text-white focus:outline-none focus:border-blue-500"
                            />
                            <button
                              type="button"
                              disabled={submittingGapId === g.id}
                              onClick={() => handleAnswerGap(g.id)}
                              className="px-3.5 py-2 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-xs font-semibold text-white flex items-center gap-1.5 shrink-0 cursor-pointer"
                            >
                              <Send className="h-3.5 w-3.5" />
                              Save &amp; Index
                            </button>
                          </div>
                        ) : null}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </>
          ) : (
            <div className="bg-slate-900 border border-slate-800 rounded-2xl p-8 text-center text-xs text-slate-400">
              Select a departing employee on the left to inspect their categorized handover dossier.
            </div>
          )}
        </div>
      </div>

      {/* Add Departing Staff Modal */}
      {showAddModal && (
        <div className="fixed inset-0 z-50 bg-black/70 flex items-center justify-center p-4">
          <form
            onSubmit={handleCreateDepartingUser}
            className="bg-slate-900 border border-slate-700 rounded-2xl max-w-lg w-full p-6 space-y-4"
          >
            <h3 className="text-base font-bold text-white flex items-center gap-2">
              <Users className="h-4 w-4 text-blue-400" />
              Register Departing Bank Employee
            </h3>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
              <div className="sm:col-span-2">
                <label className="block text-slate-300 mb-1">Team</label>
                <select
                  value={newProfile.teamId}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, teamId: e.target.value })
                  }
                  required
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                >
                  {teams.map((t) => (
                    <option key={t.id} value={t.id}>
                      {t.departmentName} — {t.name}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-slate-300 mb-1">Full Name</label>
                <input
                  required
                  value={newProfile.fullName}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, fullName: e.target.value })
                  }
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div>
                <label className="block text-slate-300 mb-1">Employee ID</label>
                <input
                  required
                  value={newProfile.employeeId}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, employeeId: e.target.value })
                  }
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div>
                <label className="block text-slate-300 mb-1">Bank Email</label>
                <input
                  type="email"
                  required
                  value={newProfile.email}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, email: e.target.value })
                  }
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div>
                <label className="block text-slate-300 mb-1">Exit Date</label>
                <input
                  type="date"
                  required
                  value={newProfile.exitDate}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, exitDate: e.target.value })
                  }
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div className="sm:col-span-2">
                <label className="block text-slate-300 mb-1">Job Title</label>
                <input
                  required
                  value={newProfile.jobTitle}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, jobTitle: e.target.value })
                  }
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div>
                <label className="block text-slate-300 mb-1">Successor Name</label>
                <input
                  required
                  value={newProfile.successorName}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, successorName: e.target.value })
                  }
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                />
              </div>
              <div>
                <label className="block text-slate-300 mb-1">Successor Email</label>
                <input
                  type="email"
                  required
                  value={newProfile.successorEmail}
                  onChange={(e) =>
                    setNewProfile({ ...newProfile, successorEmail: e.target.value })
                  }
                  className="w-full rounded-lg bg-slate-950 border border-slate-700 px-3 py-2 text-white"
                />
              </div>
            </div>

            <div className="flex justify-end gap-2 pt-2">
              <button
                type="button"
                onClick={() => setShowAddModal(false)}
                className="px-4 py-2 rounded-lg bg-slate-800 text-xs text-slate-300 cursor-pointer"
              >
                Cancel
              </button>
              <button
                type="submit"
                className="px-4 py-2 rounded-lg bg-blue-600 hover:bg-blue-500 text-xs font-semibold text-white cursor-pointer"
              >
                Save Profile
              </button>
            </div>
          </form>
        </div>
      )}
    </div>
  );
}
