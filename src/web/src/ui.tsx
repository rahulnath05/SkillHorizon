import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { useNavigate } from "react-router-dom";
import { signOut } from "./api";
import type { ShellState } from "./shell";
import { initials, workspaceCopy } from "./workspaces";

export function formatAsOf(value: string): string {
  const time = new Date(value);
  if (Number.isNaN(time.getTime())) return value;
  return `${time.toLocaleString("en-IN", {
    timeZone: "Asia/Kolkata",
    day: "numeric",
    month: "short",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  })} IST`;
}

export function useTitle(title: string) {
  useEffect(() => {
    document.title = title === "SkillHorizon" ? title : `${title} · SkillHorizon`;
  }, [title]);
}

export function Loading({ label }: { label: string }) {
  return (
    <div className="boot">
      <div className="loading" role="status">
        <div className="loading-card">
          <span className="spinner" aria-hidden="true" />
          <span>{label}</span>
        </div>
      </div>
    </div>
  );
}

export function Gate({
  screen,
  title,
  lede,
  foot,
  children,
}: {
  screen: string;
  title: string;
  lede: string;
  foot?: ReactNode;
  children: ReactNode;
}) {
  useTitle(title);
  return (
    <div className="gate" data-screen={screen}>
      <a className="skip" href="#main">Skip to content</a>
      <div className="gate-in">
        <div className="gate-head">
          <span className="gate-mark" aria-hidden="true">S</span>
          <h1 id="main">{title}</h1>
          <p>{lede}</p>
        </div>
        {children}
        {foot ? <div className="gate-foot">{foot}</div> : null}
      </div>
    </div>
  );
}

export function Platform({ shell, children }: { shell: ShellState; children: ReactNode }) {
  const navigate = useNavigate();
  const menuId = useId();
  const chipRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [leaving, setLeaving] = useState(false);
  const name = shell.displayName || shell.subjectId || "Signed in";
  const code = shell.activeWorkspace && shell.workspaces.includes(shell.activeWorkspace)
    ? shell.activeWorkspace
    : shell.workspaces.length === 1
      ? shell.workspaces[0]
      : null;
  const role = code ? workspaceCopy(code).label : "Choose a workspace";

  useEffect(() => {
    if (!open) return;
    const onPointer = (event: PointerEvent) => {
      const target = event.target as Node;
      if (!menuRef.current?.contains(target) && !chipRef.current?.contains(target)) setOpen(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("pointerdown", onPointer);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("pointerdown", onPointer);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  async function onSignOut() {
    setLeaving(true);
    try {
      await signOut();
      navigate("/signin", { replace: true });
    } catch {
      setLeaving(false);
    }
  }

  return (
    <div className="platform">
      <a className="skip" href="#main">Skip to content</a>
      <header className="platform-bar">
        <div className="platform-brand">
          <span className="platform-mark" aria-hidden="true">S</span>
          <strong>SkillHorizon</strong>
        </div>
        <button
          ref={chipRef}
          type="button"
          className="who-chip"
          aria-haspopup="menu"
          aria-expanded={open}
          aria-controls={menuId}
          onClick={() => setOpen((value) => !value)}
        >
          <span className="who-av" aria-hidden="true">{initials(name)}</span>
          <span className="who-txt">
            <b>{name}</b>
            <i>{role}</i>
          </span>
          <span className="who-car" aria-hidden="true">▾</span>
        </button>
        <div ref={menuRef} id={menuId} className="who-menu" role="menu" hidden={!open} aria-label="Account">
          <p>Signed in as {name}</p>
          {shell.workspaces.length > 1 ? (
            <button type="button" role="menuitem" onClick={() => navigate("/entry")}>
              <span className="mav" aria-hidden="true">↔</span>
              Choose a different workspace
            </button>
          ) : null}
          <button type="button" role="menuitem" onClick={() => void onSignOut()} disabled={leaving}>
            <span className="mav" aria-hidden="true">←</span>
            Sign out
          </button>
        </div>
      </header>
      {children}
    </div>
  );
}
