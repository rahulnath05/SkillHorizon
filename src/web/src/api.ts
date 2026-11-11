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
