import { describe, expect, it } from "vitest";
import { activeWorkspaceCode, resolveDestination, type ShellState } from "./shell";

function shell(overrides: Partial<ShellState> = {}): ShellState {
  return {
    authenticated: true,
    localDevelopment: true,
    subjectId: "person-multi",
    displayName: "person-multi",
    legalEntityId: "LE-LOCAL",
    status: "ok",
    reason: null,
    workspaces: ["employee", "people_manager"],
    activeWorkspace: null,
    identities: [],
    correlationId: "proof",
    requestToken: "token",
    ...overrides,
  };
}

describe("resolveDestination", () => {
  it("sends an anonymous person to sign-in", () => {
    expect(resolveDestination(shell({ authenticated: false, status: "unauthenticated", workspaces: [] }))).toBe("/signin");
  });

  it("refuses an unmapped person", () => {
    expect(resolveDestination(shell({ status: "refused", reason: "unmapped", workspaces: [] }))).toBe("/refused?reason=unmapped");
  });

  it("refuses a person who holds no workspace", () => {
    expect(resolveDestination(shell({ workspaces: [], reason: "grant" }))).toBe("/refused?reason=grant");
  });

  it("opens the only workspace directly", () => {
    expect(resolveDestination(shell({ subjectId: "person-single", workspaces: ["employee"], activeWorkspace: "employee" }))).toBe("/workspace");
    expect(resolveDestination(shell({ workspaces: ["employee"], activeWorkspace: null }))).toBe("/workspace");
  });

  it("stops on the entry screen only when more than one workspace is held and none is active", () => {
    expect(resolveDestination(shell())).toBe("/entry");
    expect(resolveDestination(shell({ activeWorkspace: "people_manager" }))).toBe("/workspace");
    expect(resolveDestination(shell({ activeWorkspace: "platform_admin" }))).toBe("/entry");
  });

  it("shows routing failure without opening a workspace", () => {
    expect(resolveDestination(shell({ status: "unavailable", workspaces: [] }))).toBe("/refused?reason=unavailable");
  });
});

describe("activeWorkspaceCode", () => {
  it("uses the held workspace when only one exists", () => {
    expect(activeWorkspaceCode(shell({ workspaces: ["employee"], activeWorkspace: null }))).toBe("employee");
  });

  it("ignores a workspace the person does not hold", () => {
    expect(activeWorkspaceCode(shell({ activeWorkspace: "platform_admin" }))).toBeNull();
  });
});
