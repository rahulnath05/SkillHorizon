export type ShellStatus = "unauthenticated" | "ok" | "refused" | "unavailable";

export type ShellIdentity = {
  subjectId: string;
  legalEntityId: string;
  displayName: string;
};

export type ShellState = {
  authenticated: boolean;
  localDevelopment: boolean;
  subjectId: string | null;
  displayName: string | null;
  legalEntityId: string | null;
  status: ShellStatus;
  reason: "unmapped" | "grant" | null;
  workspaces: string[];
  activeWorkspace: string | null;
  identities: ShellIdentity[];
  correlationId: string | null;
  requestToken: string;
};

export function resolveDestination(shell: ShellState): string {
  if (!shell.authenticated || shell.status === "unauthenticated") return "/signin";
  if (shell.status === "unavailable") return "/refused?reason=unavailable";
  if (shell.status === "refused") return "/refused?reason=unmapped";
  if (shell.workspaces.length === 0) return "/refused?reason=grant";
  if (shell.workspaces.length === 1) return "/workspace";
  if (shell.activeWorkspace && shell.workspaces.includes(shell.activeWorkspace)) return "/workspace";
  return "/entry";
}

export function activeWorkspaceCode(shell: ShellState): string | null {
  if (shell.activeWorkspace && shell.workspaces.includes(shell.activeWorkspace)) return shell.activeWorkspace;
  if (shell.workspaces.length === 1) return shell.workspaces[0];
  return null;
}
