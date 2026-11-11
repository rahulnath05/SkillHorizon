import { useCallback, useEffect, useState } from "react";
import type { ShellState } from "./shell";

let requestToken = "";

function readShell(value: ShellState): ShellState {
  requestToken = value.requestToken ?? "";
  return {
    ...value,
    subjectId: value.subjectId ?? null,
    displayName: value.displayName ?? null,
    legalEntityId: value.legalEntityId ?? null,
    reason: value.reason ?? null,
    activeWorkspace: value.activeWorkspace ?? null,
    correlationId: value.correlationId ?? null,
    workspaces: value.workspaces ?? [],
    identities: value.identities ?? [],
    requestToken,
  };
}

export async function getShell(): Promise<ShellState> {
  const response = await fetch("/api/shell", {
    headers: { Accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) throw new Error("SkillHorizon could not be reached.");
  return readShell((await response.json()) as ShellState);
}

async function postJson(path: string, body: unknown): Promise<Response> {
  return fetch(path, {
    method: "POST",
    headers: {
      Accept: "application/json",
      "Content-Type": "application/json",
      ...(requestToken ? { RequestVerificationToken: requestToken } : {}),
    },
    body: JSON.stringify(body),
  });
}

async function readError(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { error?: string; detail?: string };
    if (body.error) return body.error;
    if (body.detail) return body.detail;
  } catch {
    // The host answered without a JSON body.
  }
  return "That request was not accepted.";
}

export async function signIn(subjectId: string): Promise<ShellState> {
  const response = await postJson("/api/signin", { subjectId });
  if (response.status === 204) return getShell();
  throw new Error(await readError(response));
}

export async function chooseWorkspace(workspace: string): Promise<ShellState> {
  const response = await postJson("/api/workspace", { workspace });
  if (response.status === 204) return getShell();
  throw new Error(await readError(response));
}

export type DirectReport = {
  employeeId: string;
  displayName: string;
  legalEntityId: string;
  countryCode: string;
};

export async function getTeam(): Promise<DirectReport[]> {
  const response = await fetch("/api/team", {
    headers: { Accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) throw new Error(await readError(response));
  const body = (await response.json()) as { people?: DirectReport[] };
  return body.people ?? [];
}

export type FrameworkLevel = {
  levelId: string;
  code: string;
  name: string;
  rank: number;
};

export type FrameworkProfile = {
  profileId: string;
  code: string;
  name: string;
  levelName: string;
  levelRank: number;
};

export type FrameworkView = {
  frameworkId: string;
  code: string;
  name: string;
  levels: FrameworkLevel[];
  profiles: FrameworkProfile[];
};

export type SkillTarget = {
  skillId: string;
  skillName: string;
  competencyName: string;
  practiceName: string;
  proficiencyRank: number;
  proficiencyName: string;
};

export type BenchmarkVersion = {
  benchmarkVersionId: string;
  jobProfileId: string;
  profileName: string;
  levelName: string;
  levelRank: number;
  taxonomyVersionId: string;
  versionNumber: number;
  status: string;
  targets: SkillTarget[];
};

export type Proficiency = {
  code: string;
  name: string;
  rank: number;
};

export type Standards = {
  source: "authoring" | "reference";
  frameworks: FrameworkView[];
  drafts: { draft: BenchmarkVersion; proficiencies: Proficiency[] }[];
  history: BenchmarkVersion[];
};

export async function getStandards(): Promise<Standards> {
  const response = await fetch("/api/standards", {
    headers: { Accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) throw new Error(await readError(response));
  return (await response.json()) as Standards;
}

export async function saveBenchmarkDraft(body: {
  jobProfileId: string;
  taxonomyVersionId: string;
  targets: { skillId: string; proficiencyRank: number }[];
}): Promise<void> {
  const response = await postJson("/api/benchmarks/draft", body);
  if (!response.ok) throw new Error(await readError(response));
}

export type SkillGap = {
  skillName: string;
  currentName: string;
  currentRank: number;
  targetName: string;
  targetRank: number;
};

export type AssessmentSummary = {
  wave: { waveId: string; profileName: string; population: number } | null;
  gap: SkillGap[];
};

export type SittingChoice = { rank: number; label: string };

export type SittingItem = {
  itemId: string;
  skillCode: string;
  prompt: string;
  choices: SittingChoice[];
};

export type OpenSitting = {
  token: string;
  sittingId: string;
  item: SittingItem | null;
  readyToSeal: boolean;
};

export type EmployeeHome = {
  atTarget: number;
  belowTarget: number;
  openSteps: number;
  sittingOpen: boolean;
};

export type PersonSkill = {
  skillCode: string;
  skillName: string;
  currentRank: number;
  currentName: string;
  targetRank: number;
  targetName: string;
};

export type EvidenceDocument = {
  evidenceId: string;
  fileName: string;
  contentType: string;
  byteLength: number;
  recordedAt: string;
  stepTitle: string;
};

async function readJson<T>(path: string): Promise<T> {
  const response = await fetch(path, { headers: { Accept: "application/json" }, cache: "no-store" });
  if (!response.ok) throw new Error(await readError(response));
  return (await response.json()) as T;
}

export function getHome(): Promise<EmployeeHome> {
  return readJson<EmployeeHome>("/api/home");
}

export async function getSkills(): Promise<PersonSkill[]> {
  const body = await readJson<{ skills?: PersonSkill[] }>("/api/skills");
  return body.skills ?? [];
}

export type EmployeeTask = {
  stepId: string;
  title: string;
  skillCode: string;
  dueOn: string;
  status: string;
  evidenceName?: string | null;
};

export function getTasks(): Promise<EmployeeTask[]> {
  return readJson<{ tasks?: EmployeeTask[] }>("/api/tasks").then((body) => body.tasks ?? []);
}

export function getTask(stepId: string): Promise<EmployeeTask> {
  return readJson<EmployeeTask>(`/api/tasks/${stepId}`);
}

export async function closeTask(stepId: string, body: { fileName: string; contentBase64: string }): Promise<EmployeeTask> {
  const response = await postJson(`/api/tasks/${stepId}/close`, body);
  if (!response.ok) throw new Error(await readError(response));
  return (await response.json()) as EmployeeTask;
}

export async function getDocuments(): Promise<EvidenceDocument[]> {
  const body = await readJson<{ documents?: EvidenceDocument[] }>("/api/documents");
  return body.documents ?? [];
}

export async function getAssessment(): Promise<AssessmentSummary> {
  const response = await fetch("/api/assessment", {
    headers: { Accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) throw new Error(await readError(response));
  const body = (await response.json()) as AssessmentSummary;
  return { wave: body.wave ?? null, gap: body.gap ?? [] };
}

export async function startSitting(waveId: string): Promise<OpenSitting> {
  const response = await postJson("/api/sittings/start", { waveId });
  if (!response.ok) throw new Error(await readError(response));
  return (await response.json()) as OpenSitting;
}

export async function answerSitting(body: {
  token: string;
  sittingId: string;
  itemId: string;
  rank: number;
}): Promise<OpenSitting> {
  const response = await postJson("/api/sittings/answer", body);
  if (!response.ok) throw new Error(await readError(response));
  const next = (await response.json()) as Omit<OpenSitting, "token">;
  return { token: body.token, sittingId: body.sittingId, item: next.item ?? null, readyToSeal: next.readyToSeal };
}

export async function sealSitting(body: { token: string; sittingId: string }): Promise<SkillGap[]> {
  const response = await postJson("/api/sittings/seal", body);
  if (!response.ok) throw new Error(await readError(response));
  const sealed = (await response.json()) as { gap?: SkillGap[] };
  return sealed.gap ?? [];
}

export type PathStep = {
  stepId: string;
  skillCode: string;
  title: string;
  provider: string;
  status: string;
  evidenceName?: string | null;
};

export type DevelopmentPath = {
  pathId: string;
  benchmarkVersionId: string;
  steps: PathStep[];
};

export async function openPath(): Promise<DevelopmentPath> {
  const response = await postJson("/api/paths", {});
  if (!response.ok) throw new Error(await readError(response));
  const body = (await response.json()) as DevelopmentPath;
  return { ...body, steps: body.steps ?? [] };
}

export async function addEvidence(body: { stepId: string; fileName: string; contentBase64: string }): Promise<DevelopmentPath> {
  const response = await postJson("/api/paths/evidence", body);
  if (!response.ok) throw new Error(await readError(response));
  const next = (await response.json()) as DevelopmentPath;
  return { ...next, steps: next.steps ?? [] };
}

export type ReportPath = {
  employeeId: string;
  displayName: string;
  path: DevelopmentPath | null;
};

export type TeamMember = {
  employeeId: string;
  displayName: string;
  position: string;
  attention: boolean;
};

export type TeamSnapshot = {
  asOf: string;
  reportCount: number;
  attentionCount: number;
  members: TeamMember[];
};

export type ProfileReview = {
  employeeId: string;
  displayName: string;
  jobProfileId: string;
  profileName: string;
};

export type AssessmentReviewItem = {
  employeeId: string;
  displayName: string;
  resultId: string;
};

export type PendingEvidence = {
  stepId: string;
  subjectId: string;
  displayName: string;
  title: string;
  fileName: string;
};

export type TeamSkillMeasure = {
  skillCode: string;
  skillName: string;
  currentRank: number;
  currentName: string;
  targetRank: number;
  targetName: string;
};

export type TeamSkillPerson = {
  employeeId: string;
  displayName: string;
  position: string;
  skills: TeamSkillMeasure[];
};

export type CoachingCommitment = {
  commitmentId: string;
  employeeId: string;
  displayName: string;
  title: string;
  dueOn: string;
};

export function getTeamSnapshot(): Promise<TeamSnapshot | null> {
  return readJson<{ snapshot?: TeamSnapshot | null }>("/api/team/snapshot").then((body) => body.snapshot ?? null);
}

export function getTeamReviews(): Promise<{ profiles: ProfileReview[]; assessments: AssessmentReviewItem[] }> {
  return readJson<{ profiles?: ProfileReview[]; assessments?: AssessmentReviewItem[] }>("/api/team/reviews").then((body) => ({
    profiles: body.profiles ?? [],
    assessments: body.assessments ?? [],
  }));
}

export function getTeamEvidence(): Promise<PendingEvidence[]> {
  return readJson<{ evidence?: PendingEvidence[] }>("/api/team/evidence").then((body) => body.evidence ?? []);
}

export function getTeamSkills(): Promise<TeamSkillPerson[]> {
  return readJson<{ people?: TeamSkillPerson[] }>("/api/team/skills").then((body) => body.people ?? []);
}

export function getCommitments(): Promise<CoachingCommitment[]> {
  return readJson<{ commitments?: CoachingCommitment[] }>("/api/team/commitments").then((body) => body.commitments ?? []);
}

async function postOk(path: string, body: unknown): Promise<void> {
  const response = await postJson(path, body);
  if (!response.ok) throw new Error(await readError(response));
}

export function confirmProfile(subjectId: string): Promise<void> {
  return postOk("/api/team/profile-confirmations", { subjectId });
}

export function reviewAssessment(subjectId: string, note: string): Promise<void> {
  return postOk("/api/team/assessment-reviews", { subjectId, note });
}

export function fileCorrection(subjectId: string, note: string): Promise<void> {
  return postOk("/api/team/corrections", { subjectId, note });
}

export function createCommitment(body: { subjectId: string; title: string; dueOn: string }): Promise<void> {
  return postOk("/api/team/commitments", body);
}

export async function getTeamPaths(): Promise<ReportPath[]> {
  const response = await fetch("/api/team/paths", {
    headers: { Accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) throw new Error(await readError(response));
  const body = (await response.json()) as { people?: ReportPath[] };
  return body.people ?? [];
}

export type PackRow = {
  rowId: string;
  jobProfileId: string;
  profileCode: string;
  profileName: string;
  classification: string;
  exceptionNote?: string | null;
  benchmarkVersionId?: string | null;
};

export type AttestationPack = {
  packId: string;
  practiceCode: string;
  status: string;
  submittedBy: string;
  taxonomyVersionId?: string | null;
  leadSubjectId?: string | null;
  sealedAt?: string | null;
  rows: PackRow[];
};

export async function getPacks(practice?: string): Promise<AttestationPack[]> {
  const path = practice ? `/api/packs?practice=${encodeURIComponent(practice)}` : "/api/packs";
  const response = await fetch(path, { headers: { Accept: "application/json" }, cache: "no-store" });
  if (!response.ok) throw new Error(await readError(response));
  const body = (await response.json()) as { packs?: AttestationPack[] };
  return body.packs ?? [];
}

export async function submitPack(body: {
  practiceCode: string;
  rows: { jobProfileId: string; classification: string; exceptionNote?: string }[];
}): Promise<AttestationPack> {
  const response = await postJson("/api/packs", body);
  if (!response.ok) throw new Error(await readError(response));
  const pack = (await response.json()) as AttestationPack;
  return { ...pack, rows: pack.rows ?? [] };
}

export async function sealPack(packId: string): Promise<AttestationPack> {
  const response = await postJson(`/api/packs/${packId}/seal`, {});
  if (!response.ok) throw new Error(await readError(response));
  const pack = (await response.json()) as AttestationPack;
  return { ...pack, rows: pack.rows ?? [] };
}

export type PracticeSnapshot = {
  practiceCode: string;
  asOf: string;
  dirty: boolean;
  memberCount: number;
  measuredCount: number;
  gapCount: number;
  cohortCount: number;
};

export async function getPracticeReadiness(practice: string): Promise<PracticeSnapshot | null> {
  const response = await fetch(`/api/practice/readiness?practice=${encodeURIComponent(practice)}`, {
    headers: { Accept: "application/json" },
    cache: "no-store",
  });
  if (!response.ok) throw new Error(await readError(response));
  const body = (await response.json()) as { snapshot?: PracticeSnapshot | null };
  return body.snapshot ?? null;
}

export async function createCohort(body: {
  practiceCode: string;
  countryCode?: string;
  orgUnit?: string;
  jobProfileId: string;
  memberIds: string[];
}): Promise<void> {
  const response = await postJson("/api/cohorts", body);
  if (!response.ok) throw new Error(await readError(response));
}

export async function validateStep(body: { subjectId: string; stepId: string }): Promise<void> {
  const response = await postJson("/api/validations", body);
  if (!response.ok) throw new Error(await readError(response));
}

export type CatalogueSkill = { code: string; name: string };

export type CatalogueCompetency = {
  code: string;
  name: string;
  practiceCode: string;
  practiceName: string;
  skills: CatalogueSkill[];
};

export type CatalogueTaxonomy = {
  source: "authoring" | "reference";
  version: {
    taxonomyVersionId: string;
    versionNumber: number;
    status: string;
    competencies: CatalogueCompetency[];
    proficiencies: Proficiency[];
  } | null;
};

export type CatalogueBenchmark = {
  profile: { profileId: string; profileName: string; frameworkName: string; levelName: string };
  taxonomyVersionId: string;
  benchmarkVersionId: string | null;
  versionNumber: number | null;
  proficiencies: Proficiency[];
  skills: { skillId: string; skillName: string; competencyName: string; practiceName: string; proficiencyRank: number }[];
};

export type ProfileRequest = {
  requestId: string;
  managerEmployeeId: string;
  reportEmployeeId: string;
  displayName: string;
  note: string;
  recordedAt: string;
  profileId: string | null;
  profileName: string | null;
};

export type SentDraft = {
  benchmarkVersionId: string;
  jobProfileId: string;
  profileName: string;
  versionNumber: number;
  sentAt: string;
};

export type CatalogueOverview = {
  snapshots: { practiceCode: string; asOf: string; memberCount: number; measuredCount: number; gapCount: number; cohortCount: number }[];
  requestCount: number;
  approvalCount: number;
};

async function readOk<T>(path: string): Promise<T> {
  const response = await fetch(path, { headers: { Accept: "application/json" }, cache: "no-store" });
  if (!response.ok) throw new Error(await readError(response));
  return (await response.json()) as T;
}

export function getCatalogueTaxonomy(): Promise<CatalogueTaxonomy> {
  return readOk("/api/catalogue/taxonomy");
}

export function saveTaxonomyDraft(body: {
  practices: { code: string; name: string; competencies: { code: string; name: string; skills: { code: string; name: string }[] }[] }[];
  proficiencies: { code: string; name: string; rank: number }[];
}): Promise<void> {
  return postJson("/api/catalogue/taxonomy/draft", body).then(async (response) => {
    if (!response.ok) throw new Error(await readError(response));
  });
}

export function getCatalogueBenchmark(profileId: string): Promise<CatalogueBenchmark> {
  return readOk(`/api/catalogue/benchmark?profileId=${encodeURIComponent(profileId)}`);
}

export function getProfileRequests(): Promise<ProfileRequest[]> {
  return readOk<{ requests?: ProfileRequest[] }>("/api/catalogue/requests").then((body) => body.requests ?? []);
}

export function openProfileRequest(requestId: string, body: { profileName: string; frameworkCode: string; levelCode: string }): Promise<void> {
  return postJson(`/api/catalogue/requests/${requestId}/open`, body).then(async (response) => {
    if (!response.ok) throw new Error(await readError(response));
  });
}

export function getApprovals(): Promise<SentDraft[]> {
  return readOk<{ drafts?: SentDraft[] }>("/api/catalogue/approvals").then((body) => body.drafts ?? []);
}

export function sendDraft(benchmarkVersionId: string): Promise<void> {
  return postJson("/api/catalogue/submissions", { benchmarkVersionId }).then(async (response) => {
    if (!response.ok) throw new Error(await readError(response));
  });
}

export function getCatalogueReview(): Promise<SentDraft[]> {
  return readOk<{ drafts?: SentDraft[] }>("/api/catalogue/review").then((body) => body.drafts ?? []);
}

export function getCatalogueOverview(): Promise<CatalogueOverview> {
  return readOk("/api/catalogue/overview");
}

export async function publishBenchmark(benchmarkVersionId: string): Promise<void> {
  const response = await postJson("/api/benchmarks/publish", { benchmarkVersionId });
  if (!response.ok) throw new Error(await readError(response));
}

export async function signOut(): Promise<void> {
  const response = await postJson("/api/signout", {});
  if (response.status !== 204) throw new Error(await readError(response));
  requestToken = "";
}

export function useShell() {
  const [shell, setShell] = useState<ShellState | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const reload = useCallback(async () => {
    try {
      const next = await getShell();
      setShell(next);
      setProblem(null);
      return next;
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "SkillHorizon could not be reached.");
      return null;
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  return { shell, setShell, problem, reload };
}
