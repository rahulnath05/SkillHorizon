import { useCallback, useEffect, useState } from "react";
import { NavLink, useParams } from "react-router-dom";
import { getDocuments, getHome, getSkills, type EmployeeHome, type EvidenceDocument, type PersonSkill } from "./api";
import { DevelopmentPathPanel } from "./path";
import { Sitting } from "./sitting";
import { PublishedStandards } from "./standards";
import { TaskPage, TasksPage } from "./tasks";
import { useTitle } from "./ui";

const screens = [
  { id: "dashboard", label: "Dashboard" },
  { id: "skills", label: "My skills" },
  { id: "path", label: "My path" },
  { id: "tasks", label: "My tasks" },
  { id: "assessment", label: "Assessment" },
  { id: "documents", label: "My documents" },
];

export function EmployeeHome() {
  const { screen, stepId } = useParams();
  const current = stepId || screen === "tasks" ? "tasks" : screens.some((item) => item.id === screen) ? screen! : "dashboard";
  return (
    <div className="home">
      <nav className="home-nav" aria-label="Employee">
        {screens.map((item) => (
          <NavLink key={item.id} to={`/workspace/${item.id}`} end={item.id === "dashboard"} aria-current={current === item.id ? "page" : undefined}>
            {item.label}
          </NavLink>
        ))}
      </nav>
      <div className="home-main">
        {current === "skills" ? <SkillsPage /> : null}
        {current === "path" ? <DevelopmentPathPanel /> : null}
        {current === "tasks" && stepId ? <TaskPage /> : null}
        {current === "tasks" && !stepId ? <TasksPage /> : null}
        {current === "assessment" ? <Sitting /> : null}
        {current === "documents" ? <DocumentsPage /> : null}
        {current === "dashboard" ? <DashboardPage /> : null}
      </div>
    </div>
  );
}

function DashboardPage() {
  const [home, setHome] = useState<EmployeeHome | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("Dashboard");
  const load = useCallback(async () => {
    try {
      setHome(await getHome());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The dashboard could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return (
    <section data-screen="dashboard">
      <p className="eyebrow">Employee</p>
      <h1>Dashboard</h1>
      <p>Skills against the benchmark in force, open path steps, and whether a sitting is still open.</p>
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {home ? (
        <ul className="home-figures">
          <li><b>{home.atTarget}</b><span>At target</span></li>
          <li><b>{home.belowTarget}</b><span>Below target</span></li>
          <li><b>{home.openSteps}</b><span>Open path steps</span></li>
          <li><b>{home.sittingOpen ? "Open" : "None"}</b><span>Sitting</span></li>
        </ul>
      ) : null}
    </section>
  );
}

function SkillsPage() {
  const [skills, setSkills] = useState<PersonSkill[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("My skills");
  const load = useCallback(async () => {
    try {
      setSkills(await getSkills());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The skills could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return (
    <section data-screen="skills">
      <p className="eyebrow">Employee</p>
      <h1>My skills</h1>
      <p>Each skill on the benchmark in force, with the sealed proficiency and the gap.</p>
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {skills && skills.length === 0 ? <p>Nothing is sealed yet.</p> : null}
      {skills && skills.length > 0 ? (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr><th>Skill</th><th>Sealed</th><th>Target</th><th>Gap</th></tr>
            </thead>
            <tbody>
              {skills.map((skill) => (
                <tr key={skill.skillCode}>
                  <td>{skill.skillName}</td>
                  <td>{skill.currentName || "Not measured"}</td>
                  <td>{skill.targetName}</td>
                  <td>{skill.currentRank >= skill.targetRank ? "At target" : `${skill.targetRank - skill.currentRank} below`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      <PublishedStandards />
    </section>
  );
}

function DocumentsPage() {
  const [documents, setDocuments] = useState<EvidenceDocument[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("My documents");
  const load = useCallback(async () => {
    try {
      setDocuments(await getDocuments());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The documents could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return (
    <section data-screen="documents">
      <p className="eyebrow">Employee</p>
      <h1>My documents</h1>
      <p>Evidence stored for your path. The file stays in this zone.</p>
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {documents && documents.length === 0 ? <p>No documents are stored.</p> : null}
      {documents && documents.length > 0 ? (
        <ul className="gap-list">
          {documents.map((document) => (
            <li key={document.evidenceId}>
              <b>{document.fileName}</b>
              <span>{document.stepTitle}</span>
              <a href={`/api/documents/${document.evidenceId}`}>Download</a>
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

function Problem({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <p className="gate-alert" role="alert">
      {text}
      <button type="button" className="team-retry" onClick={onRetry}>Try again</button>
    </p>
  );
}
