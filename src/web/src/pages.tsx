import { useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { chooseWorkspace, signIn, signOut, useShell } from "./api";
import { activeWorkspaceCode, resolveDestination, type ShellState } from "./shell";
import { Gate, Loading, Platform, useTitle } from "./ui";
import { EmployeeHome } from "./home";
import { PracticeHome } from "./practice";
import { ManagerHome } from "./manager";
import { CatalogueHome } from "./catalogue";
import { AdminHome } from "./admin";
import { workspaceCopy } from "./workspaces";

type PersonaChoice = {
  label: string;
  subjectId: string;
  workspace?: string;
};

const personaChoices: PersonaChoice[] = [
  { label: "Employee", subjectId: "person-single" },
  { label: "People manager", subjectId: "person-multi", workspace: "people_manager" },
  { label: "L&D", subjectId: "person-ld" },
  { label: "Competency lead", subjectId: "person-lead" },
  { label: "Platform admin", subjectId: "person-admin" },
];

function destinationFor(shell: ShellState) {
  return resolveDestination(shell);
}

export function ResolvePage() {
  const navigate = useNavigate();
  const { shell, problem, reload } = useShell();

  useEffect(() => {
    if (!shell) return;
    navigate(destinationFor(shell), { replace: true });
  }, [shell, navigate]);

  if (problem) {
    return (
      <Gate screen="unavailable" title="SkillHorizon" lede="The workspace list could not be loaded.">
        <div className="gate-sso">
          <h2>Try again</h2>
          <p className="sso-sub" role="alert">{problem}</p>
          <button type="button" className="sso-go" onClick={() => void reload()}>Try again</button>
        </div>
      </Gate>
    );
  }

  return <Loading label="Loading SkillHorizon…" />;
}

export function SignInPage() {
  const navigate = useNavigate();
  const { shell, setShell, problem, reload } = useShell();
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [current, setCurrent] = useState<string | null>(null);

  useEffect(() => {
    if (!shell?.authenticated || shell.status === "unauthenticated") return;
    navigate(destinationFor(shell), { replace: true });
  }, [shell, navigate]);

  if (problem && !shell) {
    return (
      <Gate screen="sign-in" title="SkillHorizon">
        <div className="gate-sso">
          <h2>Sign in</h2>
          <p className="sso-sub" role="alert">{problem}</p>
          <button type="button" className="sso-go" onClick={() => void reload()}>Try again</button>
        </div>
      </Gate>
    );
  }

  if (!shell) return <Loading label="Loading SkillHorizon…" />;

  const available = new Set(shell.identities.map((identity) => identity.subjectId));
  const choices = personaChoices.filter((persona) => available.has(persona.subjectId));

  async function onChoose(persona: PersonaChoice) {
    setPending(true);
    setCurrent(persona.subjectId);
    setError(null);
    try {
      let next = await signIn(persona.subjectId);
      if (persona.workspace) next = await chooseWorkspace(persona.workspace);
      setShell(next);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Sign-in did not succeed.");
      setPending(false);
      setCurrent(null);
    }
  }

  return (
    <Gate screen="sign-in" title="SkillHorizon" lede={shell.localDevelopment ? undefined : "Sign in with your work account to open your workspace."}>
      {error ? <p className="gate-alert" role="alert">{error}</p> : null}
      {shell.localDevelopment ? (
        choices.length === 0 ? (
          <div className="gate-sso">
            <h2>Sign in</h2>
            <p className="sso-sub">No development identities are configured for the local identity provider.</p>
          </div>
        ) : (
          <div className="persona-panel">
            <div className="persona-choices" role="group" aria-label="Personas" aria-busy={pending}>
              {choices.map((persona) => {
                const selected = current === persona.subjectId;
                return (
                  <button
                    key={persona.subjectId}
                    type="button"
                    className={selected ? "persona-choice is-current" : "persona-choice"}
                    aria-current={selected ? "true" : undefined}
                    disabled={pending}
                    onClick={() => void onChoose(persona)}
                  >
                    {persona.label}
                  </button>
                );
              })}
            </div>
          </div>
        )
      ) : (
        <div className="gate-sso">
          <span className="sso-mark" aria-hidden="true">S</span>
          <h2>SkillHorizon</h2>
          <p className="sso-sub">Sign in with your work account to open your workspace.</p>
          <a className="sso-go" href="/signin/oidc">Continue with Organization SSO</a>
          <p className="sso-meta">OpenID Connect · single sign-on</p>
          <p className="sso-note">
            <b>There is no password to set here.</b> SkillHorizon never holds one — sign-in goes to your organisation’s identity provider and comes back. What you can see is decided by the workspaces you hold, not by this screen.
          </p>
        </div>
      )}
    </Gate>
  );
}

export function EntryPage() {
  const navigate = useNavigate();
  const { shell, setShell, problem, reload } = useShell();
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    if (!shell) return;
    if (!shell.authenticated || shell.status !== "ok" || shell.workspaces.length < 2) {
      navigate(destinationFor(shell), { replace: true });
    }
  }, [shell, navigate]);

  if (problem && !shell) {
    return (
      <Gate screen="entry" title="Choose a workspace" lede="The workspace list could not be loaded.">
        <div className="gate-sso">
          <p className="sso-sub" role="alert">{problem}</p>
          <button type="button" className="sso-go" onClick={() => void reload()}>Try again</button>
        </div>
      </Gate>
    );
  }

  if (!shell || shell.workspaces.length < 2) return <Loading label="Loading SkillHorizon…" />;

  async function onChoose(workspace: string) {
    setPending(true);
    setError(null);
    try {
      const next = await chooseWorkspace(workspace);
      setShell(next);
      navigate("/workspace", { replace: true });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "That workspace is not held.");
      setPending(false);
    }
  }

  async function onSignOut() {
    setPending(true);
    try {
      await signOut();
      navigate("/signin", { replace: true });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "Sign-out did not succeed.");
      setPending(false);
    }
  }

  return (
    <Gate
      screen="entry"
      title="Choose a workspace"
      lede="You hold more than one workspace. Enter the one you are working in now."
      foot={
        <>
          The list is only the workspaces this sign-in holds.
          <button type="button" className="gate-link" onClick={() => void onSignOut()} disabled={pending}>Sign out</button>
        </>
      }
    >
      {error ? <p className="gate-alert" role="alert">{error}</p> : null}
      <div className="gate-grid">
        {shell.workspaces.map((code) => {
          const copy = workspaceCopy(code);
          return (
            <button
              key={code}
              type="button"
              className="gate-card"
              data-workspace={code}
              disabled={pending}
              onClick={() => void onChoose(code)}
            >
              <span className="gate-av" aria-hidden="true">{copy.initials}</span>
              <span>
                <b>{copy.label}</b>
                <em>{copy.detail}</em>
              </span>
            </button>
          );
        })}
      </div>
    </Gate>
  );
}

export function WorkspacePage() {
  const navigate = useNavigate();
  const { shell, problem, reload } = useShell();

  useEffect(() => {
    if (!shell) return;
    if (destinationFor(shell) !== "/workspace") navigate(destinationFor(shell), { replace: true });
  }, [shell, navigate]);

  if (problem && !shell) {
    return (
      <Gate screen="workspace" title="SkillHorizon" lede="The workspace could not be opened.">
        <div className="gate-sso">
          <p className="sso-sub" role="alert">{problem}</p>
          <button type="button" className="sso-go" onClick={() => void reload()}>Try again</button>
        </div>
      </Gate>
    );
  }

  const code = shell ? activeWorkspaceCode(shell) : null;
  if (!shell || !code || destinationFor(shell) !== "/workspace") return <Loading label="Loading SkillHorizon…" />;

  const copy = workspaceCopy(code);
  return (
    <Platform shell={shell}>
      <main className="workspace" id="main" data-screen="workspace" data-workspace={code}>
        {code === "employee" ? <EmployeeHome /> : null}
        {code === "people_manager" ? <ManagerHome /> : null}
        {code === "learning_and_development" ? <CatalogueHome /> : null}
        {code === "competency_lead" ? <PracticeHome /> : null}
        {code === "platform_admin" ? <AdminHome /> : null}
        {code === "employee" || code === "people_manager" || code === "learning_and_development" || code === "competency_lead" || code === "platform_admin" ? null : (
        <div className="workspace-in">
          <p className="eyebrow">Active workspace</p>
          <h1>{copy.label}</h1>
          <p>{copy.detail}</p>
          <p>This shell is empty. The workspace opens here when its features are built.</p>
          {shell.workspaces.length > 1 ? (
            <p><Link to="/entry">Choose a different workspace</Link></p>
          ) : null}
        </div>
        )}
      </main>
    </Platform>
  );
}

export function RefusedPage() {
  const navigate = useNavigate();
  const { shell, problem, reload } = useShell();
  const [pending, setPending] = useState(false);
  useTitle("SkillHorizon");

  useEffect(() => {
    if (!shell) return;
    const destination = destinationFor(shell);
    if (!destination.startsWith("/refused")) navigate(destination, { replace: true });
  }, [shell, navigate]);

  if (problem && !shell) {
    return (
      <Gate screen="refused" title="SkillHorizon" lede="The workspace list could not be loaded.">
        <div className="gate-sso">
          <p className="sso-sub" role="alert">{problem}</p>
          <button type="button" className="sso-go" onClick={() => void reload()}>Try again</button>
        </div>
      </Gate>
    );
  }

  if (!shell) return <Loading label="Loading SkillHorizon…" />;

  const destination = destinationFor(shell);
  if (!destination.startsWith("/refused")) return <Loading label="Loading SkillHorizon…" />;

  const reason = destination.endsWith("grant")
    ? "grant"
    : destination.endsWith("unavailable")
      ? "unavailable"
      : "unmapped";
  const title = reason === "grant"
    ? "No workspace is held"
    : reason === "unavailable"
      ? "Routing unavailable"
      : "Not mapped to a zone";
  const text = reason === "grant"
    ? "No workspace is held for this sign-in, so there is nothing to open."
    : reason === "unavailable"
      ? "Zone routing is unavailable. This sign-in was not placed in another zone."
      : "You are not mapped to a zone. SkillHorizon will not place an unmapped person into another zone.";

  async function onSignOut() {
    setPending(true);
    try {
      await signOut();
      navigate("/signin", { replace: true });
    } catch {
      setPending(false);
    }
  }

  return (
    <Gate screen="refused" title="SkillHorizon" lede={title}>
      <div className="gate-sso" data-reason={reason}>
        <span className="sso-mark" aria-hidden="true">S</span>
        <h2>{title}</h2>
        <p className="sso-sub" role="alert">{text}</p>
        {reason === "unavailable" ? (
          <button type="button" className="sso-go" onClick={() => void reload()}>Try again</button>
        ) : (
          <button type="button" className="sso-go" onClick={() => void onSignOut()} disabled={pending}>Sign out</button>
        )}
        <p className="sso-note">
          {reason === "unavailable"
            ? "The control registry could not be reached. This sign-in was not placed in another zone."
            : "Sign out, then try a different identity if you are using the local development provider."}
        </p>
        {shell.correlationId ? <p className="sso-meta">Reference <span className="mono">{shell.correlationId}</span></p> : null}
        {reason === "unavailable" ? (
          <button type="button" className="sso-back" onClick={() => void onSignOut()} disabled={pending}>Sign out</button>
        ) : null}
      </div>
    </Gate>
  );
}
