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
    const body = (await response.json()) as { error?: string };
    if (body.error) return body.error;
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
