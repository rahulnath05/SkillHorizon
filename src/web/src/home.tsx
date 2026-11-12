import { useEffect, useState } from "react";
import { NavLink, useParams } from "react-router-dom";
import { getDocuments, getHome, getSkills, getTasks, type EmployeeHome as HomeFigures, type EvidenceDocument, type PersonSkill } from "./api";
import { PageHead, PersonaFrame, type RailItem } from "./frame";
import { DevelopmentPathPanel } from "./path";
import { Sitting } from "./sitting";
import { PublishedStandards } from "./standards";
import { TaskPage, TasksPage } from "./tasks";
import { formatAsOf, useTitle } from "./ui";

const screens: RailItem[] = [
  { id: "dashboard", label: "Dashboard", icon: "◫" },
  { id: "skills", label: "My skills", icon: "◇" },
  { id: "path", label: "My path", icon: "↗" },
  { id: "tasks", label: "My tasks", icon: "≡" },
  { id: "assessment", label: "Assessment", icon: "✓" },
  { id: "documents", label: "My documents", icon: "↑" },
];

export function EmployeeHome() {
  const { screen, stepId } = useParams();
  const current = stepId || screen === "tasks" ? "tasks" : screens.some((item) => item.id === screen) ? screen! : "dashboard";
  const [openTasks, setOpenTasks] = useState<number | undefined>(undefined);

  useEffect(() => {
    let live = true;
    const refresh = () => {
      void getTasks()
        .then((tasks) => {
          if (live) setOpenTasks(tasks.length);
        })
        .catch(() => {
          if (live) setOpenTasks(undefined);
        });
    };
    refresh();
    window.addEventListener("skillhorizon-tasks", refresh);
    return () => {
      live = false;
      window.removeEventListener("skillhorizon-tasks", refresh);
    };
  }, [current]);

  const items = screens.map((item) => ({
    ...item,
    count: item.id === "tasks" ? openTasks : undefined,
  }));
  const page = stepId ? "Task" : screens.find((item) => item.id === current)?.label ?? "Dashboard";

  return (
    <PersonaFrame
      label="Employee"
      kicker="Your skills, path, and evidence"
      mark="E"
      page={page}
      items={items}
      currentId={current}
    >
      {current === "skills" ? <SkillsPage /> : null}
      {current === "path" ? <DevelopmentPathPanel /> : null}
      {current === "tasks" && stepId ? <TaskPage /> : null}
      {current === "tasks" && !stepId ? <TasksPage /> : null}
      {current === "assessment" ? <Sitting /> : null}
      {current === "documents" ? <DocumentsPage /> : null}
      {current === "dashboard" ? <DashboardPage /> : null}
    </PersonaFrame>
  );
}

function DashboardPage() {
  const [home, setHome] = useState<HomeFigures | null>(null);
  const [skills, setSkills] = useState<PersonSkill[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("Dashboard");

  useEffect(() => {
    let live = true;
    void Promise.all([getHome(), getSkills()])
      .then(([nextHome, nextSkills]) => {
        if (!live) return;
        setHome(nextHome);
        setSkills(nextSkills);
        setProblem(null);
      })
      .catch((error: unknown) => {
        if (!live) return;
        setProblem(error instanceof Error ? error.message : "The dashboard could not be loaded.");
      });
    return () => {
      live = false;
    };
  }, []);

  const measured = skills?.filter((skill) => skill.currentName).length ?? 0;
  return (
    <section data-screen="dashboard">
      <PageHead
        eyebrow="Employee"
        title="Your progress"
        lede="Skills against the benchmark in force, open path steps, and whether a sitting is still open."
      />
      {problem ? <Problem text={problem} onRetry={() => window.location.reload()} /> : null}
      <div className="stat-row">
        <NavLink to="/workspace/skills">
          <span className="kicker">At target</span>
          <strong>{home ? home.atTarget : "—"}</strong>
          <span className="hint">Sealed at or above the bar</span>
        </NavLink>
        <NavLink to="/workspace/skills">
          <span className="kicker">Below target</span>
          <strong>{home ? home.belowTarget : "—"}</strong>
          <span className="hint">Sealed short of the bar</span>
        </NavLink>
        <NavLink to="/workspace/path">
          <span className="kicker">Open path steps</span>
          <strong>{home ? home.openSteps : "—"}</strong>
          <span className="hint">Still assigned on your path</span>
        </NavLink>
        <NavLink to="/workspace/assessment" className={home?.sittingOpen ? "ink" : undefined}>
          <span className="kicker">Sitting</span>
          <strong>{home ? (home.sittingOpen ? "Open" : "None") : "—"}</strong>
          <span className="hint">{home?.sittingOpen ? "A sitting is still open" : "No sitting is open"}</span>
        </NavLink>
      </div>
      <div className="split">
        <article className="surface-card">
          <header>
            <div>
              <h2>Your path</h2>
              <p className="card-note">Steps close a gap. They do not move a sealed level.</p>
            </div>
            <NavLink to="/workspace/tasks">Open my tasks</NavLink>
          </header>
          {!home ? <p className="empty-note">The home read has not returned.</p> : null}
          {home && home.openSteps === 0 ? <p className="empty-note">No path step is open.</p> : null}
          {home && home.openSteps > 0 ? (
            <p className="empty-note">{home.openSteps} step{home.openSteps === 1 ? "" : "s"} still assigned. The path lists each one.</p>
          ) : null}
        </article>
        <aside className="surface-card">
          <header>
            <div>
              <h2>Needs you</h2>
              <p className="card-note">From the same reads as the rail</p>
            </div>
          </header>
          <ul className="gap-list">
            <li>
              <b>Assessment</b>
              <span className={home?.sittingOpen ? "chip amber" : "chip quiet"}>{home?.sittingOpen ? "Sitting open" : "No sitting"}</span>
            </li>
            <li>
              <b>Skills sealed</b>
              <span className="chip quiet">{skills ? `${measured} named` : "Not loaded"}</span>
            </li>
          </ul>
        </aside>
      </div>
      <article className="surface-card">
        <header>
          <div>
            <h2>Your skills</h2>
            <p className="card-note">{skills ? `${skills.length} on the benchmark in force` : "Not loaded"}</p>
          </div>
          <NavLink to="/workspace/skills">Open my skills</NavLink>
        </header>
        <SkillSheet skills={skills} />
      </article>
    </section>
  );
}

function SkillsPage() {
  const [skills, setSkills] = useState<PersonSkill[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("My skills");

  useEffect(() => {
    let live = true;
    void getSkills()
      .then((next) => {
        if (!live) return;
        setSkills(next);
        setProblem(null);
      })
      .catch((error: unknown) => {
        if (!live) return;
        setProblem(error instanceof Error ? error.message : "The skills could not be loaded.");
      });
    return () => {
      live = false;
    };
  }, []);

  return (
    <section data-screen="skills">
      <PageHead
        eyebrow="Employee"
        title="My skills"
        lede="Each skill on the benchmark in force, with the sealed proficiency and the gap. Nothing here is a sample score."
      />
      {problem ? <Problem text={problem} onRetry={() => window.location.reload()} /> : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>Sealed against the bar</h2>
            <p className="card-note">The marker is the target rank</p>
          </div>
        </header>
        <SkillSheet skills={skills} />
      </article>
      <PublishedStandards />
    </section>
  );
}

function DocumentsPage() {
  const [documents, setDocuments] = useState<EvidenceDocument[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("My documents");

  useEffect(() => {
    let live = true;
    void getDocuments()
      .then((next) => {
        if (!live) return;
        setDocuments(next);
        setProblem(null);
      })
      .catch((error: unknown) => {
        if (!live) return;
        setProblem(error instanceof Error ? error.message : "The documents could not be loaded.");
      });
    return () => {
      live = false;
    };
  }, []);

  return (
    <section data-screen="documents">
      <PageHead
        eyebrow="Employee"
        title="My documents"
        lede="Evidence stored for your path. The file stays in this zone. An upload closes a step. It does not change your sealed proficiency."
      />
      {problem ? <Problem text={problem} onRetry={() => window.location.reload()} /> : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>On the record</h2>
            <p className="card-note">{documents ? `${documents.length} stored` : "Not loaded"}</p>
          </div>
          <NavLink to="/workspace/tasks">Hand in from a task</NavLink>
        </header>
        {documents && documents.length === 0 ? <p className="empty-note">No documents are stored.</p> : null}
        {documents && documents.length > 0 ? (
          <ul className="gap-list">
            {documents.map((document) => (
              <li key={document.evidenceId}>
                <b>{document.fileName}</b>
                <span>{document.stepTitle} · {formatAsOf(document.recordedAt)}</span>
                <a href={`/api/documents/${document.evidenceId}`}>Download</a>
              </li>
            ))}
          </ul>
        ) : null}
      </article>
      <p className="callout"><i aria-hidden="true">i</i><span><b>The file is yours to download.</b> This list does not show the file bytes on the page.</span></p>
    </section>
  );
}

export function SkillSheet({ skills }: { skills: PersonSkill[] | null }) {
  if (!skills) return <p className="empty-note">The skills have not been loaded.</p>;
  if (skills.length === 0) return <p className="empty-note">Nothing is sealed yet.</p>;
  return (
    <div className="skill-sheet">
      <div className="skill-head">
        <span>Skill</span>
        <span>Target</span>
        <span>Where you are</span>
        <span>Gap</span>
      </div>
      {skills.map((skill) => {
        const measured = Boolean(skill.currentName);
        const hit = measured && skill.currentRank >= skill.targetRank;
        return (
          <div className="skill-row" key={skill.skillCode}>
            <div>
              <b>{skill.skillName}</b>
              <span className="ref">{skill.skillCode}</span>
            </div>
            <div className="skill-target">{skill.targetName}</div>
            <div
              className={measured ? "skill-bar" : "skill-bar none"}
              role="img"
              aria-label={`${skill.skillName}: ${measured ? skill.currentName : "not measured"}, target ${skill.targetName}`}
            >
              {measured ? <i className={hit ? "ok" : undefined} style={{ width: `${track(skill.currentRank)}%` }} /> : null}
              <span className="mark" style={{ left: `${track(skill.targetRank)}%` }} />
            </div>
            <div className={measured ? (hit ? "gap-ok" : "gap-low") : "gap-none"}>
              {measured ? (hit ? "At target" : `${skill.targetRank - skill.currentRank} below`) : "Not measured"}
            </div>
          </div>
        );
      })}
    </div>
  );
}

function track(rank: number) {
  if (!Number.isFinite(rank) || rank <= 1) return 0;
  if (rank >= 5) return 100;
  return ((rank - 1) / 4) * 100;
}

function Problem({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <p className="gate-alert" role="alert">
      {text}
      <button type="button" className="team-retry" onClick={onRetry}>Try again</button>
    </p>
  );
}
