import { useCallback, useEffect, useRef, useState } from "react";
import { NavLink, useParams } from "react-router-dom";
import { PageHead, PersonaFrame, type RailItem } from "./frame";
import {
  confirmProfile,
  createCommitment,
  fileCorrection,
  getCommitments,
  getTeamEvidence,
  getTeamReviews,
  getTeamSkills,
  getTeamSnapshot,
  reviewAssessment,
  validateStep,
  type AssessmentReviewItem,
  type CoachingCommitment,
  type PendingEvidence,
  type ProfileReview,
  type TeamSkillPerson,
  type TeamSnapshot,
} from "./api";
import { PublishedStandards } from "./standards";
import { useTitle } from "./ui";

const screens: RailItem[] = [
  { id: "overview", label: "Overview", icon: "◫" },
  { id: "team", label: "My Team", icon: "≡" },
  { id: "reviews", label: "Reviews & Evidence", icon: "✓" },
  { id: "coaching", label: "Coaching", icon: "↗" },
  { id: "skills", label: "Team Skills", icon: "◇" },
];

export function ManagerHome() {
  const { screen } = useParams();
  const current = screens.some((item) => item.id === screen) ? screen! : "overview";
  const [reviewCount, setReviewCount] = useState<number | undefined>(undefined);
  useEffect(() => {
    let live = true;
    const refresh = () => {
      void Promise.all([getTeamReviews(), getTeamEvidence()])
        .then(([reviews, evidence]) => {
          if (!live) return;
          setReviewCount(reviews.profiles.length + reviews.assessments.length + evidence.length);
        })
        .catch(() => {
          if (live) setReviewCount(undefined);
        });
    };
    refresh();
    window.addEventListener("skillhorizon-team", refresh);
    return () => {
      live = false;
      window.removeEventListener("skillhorizon-team", refresh);
    };
  }, [current]);
  const items = screens.map((item) => ({
    ...item,
    count: item.id === "reviews" ? reviewCount : undefined,
  }));
  const page = screens.find((item) => item.id === current)?.label ?? "Overview";
  return (
    <PersonaFrame
      label="People manager"
      kicker="Direct reports"
      mark="PM"
      page={page}
      items={items}
      currentId={current}
    >
      {current === "team" ? <TeamPage /> : null}
      {current === "reviews" ? <ReviewsPage /> : null}
      {current === "coaching" ? <CoachingPage /> : null}
      {current === "skills" ? <SkillsPage /> : null}
      {current === "overview" ? <OverviewPage /> : null}
    </PersonaFrame>
  );
}

function OverviewPage() {
  const [snapshot, setSnapshot] = useState<TeamSnapshot | null>(null);
  const [profiles, setProfiles] = useState<ProfileReview[]>([]);
  const [assessments, setAssessments] = useState<AssessmentReviewItem[]>([]);
  const [evidence, setEvidence] = useState<PendingEvidence[]>([]);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("Overview");
  const load = useLoader(async () => {
    const [next, reviews, pendingEvidence] = await Promise.all([getTeamSnapshot(), getTeamReviews(), getTeamEvidence()]);
    setSnapshot(next);
    setProfiles(reviews.profiles);
    setAssessments(reviews.assessments);
    setEvidence(pendingEvidence);
    setProblem(null);
  }, (error) => setProblem(error));
  const attention = snapshot?.members.filter((member) => member.attention) ?? [];
  return (
    <section data-screen="overview">
      <PageHead
        eyebrow="People manager · Daily briefing"
        title="Overview"
        lede="Direct reports only. Profile fit, assessment context, and evidence come from the team reads. A missing snapshot stays empty."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      <div className="stat-row">
        <NavLink to="/workspace/reviews">
          <span className="kicker">Profiles to confirm</span>
          <strong>{profiles.length}</strong>
          <span className="hint">Waiting on your confirmation</span>
        </NavLink>
        <NavLink to="/workspace/reviews">
          <span className="kicker">Assessment reviews</span>
          <strong>{assessments.length}</strong>
          <span className="hint">Sealed results with context still open</span>
        </NavLink>
        <NavLink to="/workspace/reviews">
          <span className="kicker">Evidence</span>
          <strong>{evidence.length}</strong>
          <span className="hint">Waiting on the reporting line</span>
        </NavLink>
        <NavLink to="/workspace/team" className="ink">
          <span className="kicker">Need attention</span>
          <strong>{snapshot ? snapshot.attentionCount : "—"}</strong>
          <span className="hint">{snapshot ? `${snapshot.reportCount} direct reports` : "No team snapshot yet"}</span>
        </NavLink>
      </div>
      <div className="split">
        <article className="surface-card">
          <header>
            <div>
              <h2>Needs your attention</h2>
              <p className="card-note">People below the benchmark in the snapshot</p>
            </div>
            <NavLink to="/workspace/reviews">View queue</NavLink>
          </header>
          {!snapshot ? <p className="empty-note">The team snapshot has not been written.</p> : null}
          {snapshot && attention.length === 0 ? <p className="empty-note">No one in this snapshot is below the benchmark.</p> : null}
          {attention.length > 0 ? (
            <ul className="gap-list">
              {attention.map((member) => (
                <li key={member.employeeId}>
                  <b>{member.displayName}</b>
                  <span className="chip amber">Below the benchmark</span>
                </li>
              ))}
            </ul>
          ) : null}
        </article>
        <aside className="surface-card">
          <header>
            <div>
              <h2>Team snapshot</h2>
              <p className="card-note">{snapshot ? `As of ${snapshot.asOf}` : "Not written"}</p>
            </div>
            <NavLink to="/workspace/team">Open team</NavLink>
          </header>
          {snapshot ? (
            <ul className="gap-list">
              {snapshot.members.map((member) => (
                <li key={member.employeeId}>
                  <b>{member.displayName}</b>
                  <span className={member.attention ? "chip amber" : "chip quiet"}>{positionLabel(member.position)}</span>
                </li>
              ))}
              {snapshot.members.length === 0 ? <li><span>No one reports to you.</span></li> : null}
            </ul>
          ) : <p className="empty-note">Direct reports appear here after the snapshot is written.</p>}
        </aside>
      </div>
    </section>
  );
}

function TeamPage() {
  const [snapshot, setSnapshot] = useState<TeamSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [subjectId, setSubjectId] = useState("");
  const [pending, setPending] = useState(false);
  const [filter, setFilter] = useState<"all" | "attention" | "met">("all");
  useTitle("My Team");
  const load = useLoader(async () => {
    const next = await getTeamSnapshot();
    setSnapshot(next);
    setSubjectId((current) => current || next?.members[0]?.employeeId || "");
    setProblem(null);
  }, (error) => setProblem(error));
  const members = (snapshot?.members ?? []).filter((member) => {
    if (filter === "attention") return member.attention;
    if (filter === "met") return member.position === "met";
    return true;
  });
  return (
    <section data-screen="team">
      <PageHead
        eyebrow="People manager"
        title="My Team"
        lede="Your team is the direct reports in the snapshot. A person outside the relationship is not listed."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {!snapshot && !problem ? <p className="empty-note">The team snapshot has not been written.</p> : null}
      <div className="filters" role="group" aria-label="Team filter">
        <button type="button" aria-pressed={filter === "all"} onClick={() => setFilter("all")}>All</button>
        <button type="button" aria-pressed={filter === "attention"} onClick={() => setFilter("attention")}>Below benchmark</button>
        <button type="button" aria-pressed={filter === "met"} onClick={() => setFilter("met")}>At benchmark</button>
      </div>
      {snapshot && snapshot.members.length === 0 ? <p className="empty-note">No one reports to you.</p> : null}
      {snapshot && snapshot.members.length > 0 && members.length === 0 ? <p className="empty-note">No direct report matches this filter.</p> : null}
      {members.length > 0 ? (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr><th>Person</th><th>Position</th><th>Attention</th></tr>
            </thead>
            <tbody>
              {members.map((member) => (
                <tr key={member.employeeId} data-report={member.employeeId}>
                  <td>{member.displayName}</td>
                  <td><span className={member.position === "met" ? "chip mint" : member.position === "gap" ? "chip amber" : "chip quiet"}>{positionLabel(member.position)}</span></td>
                  <td>{member.attention ? "Below the benchmark" : "None"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
      {snapshot && snapshot.members.length > 0 ? (
        <form
          className="task-close"
          onSubmit={(event) => {
            event.preventDefault();
            setPending(true);
            void fileCorrection(subjectId, note)
              .then(() => {
                setNote("");
                setProblem(null);
              })
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That correction was not stored."))
              .finally(() => setPending(false));
          }}
        >
          <h2>Request profile correction</h2>
          <p>The request is stored. It does not change a published benchmark.</p>
          <label>
            Person
            <select value={subjectId} onChange={(event) => setSubjectId(event.target.value)}>
              {snapshot.members.map((member) => (
                <option key={member.employeeId} value={member.employeeId}>{member.displayName}</option>
              ))}
            </select>
          </label>
          <label>
            Note
            <input value={note} onChange={(event) => setNote(event.target.value)} />
          </label>
          <button type="submit" className="draft-save" disabled={pending || note.trim().length === 0}>File request</button>
        </form>
      ) : null}
    </section>
  );
}

function ReviewsPage() {
  const [profiles, setProfiles] = useState<ProfileReview[]>([]);
  const [assessments, setAssessments] = useState<AssessmentReviewItem[]>([]);
  const [evidence, setEvidence] = useState<PendingEvidence[]>([]);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [filter, setFilter] = useState<"all" | "profile" | "assessment" | "evidence">("all");
  useTitle("Reviews & Evidence");
  const load = useLoader(async () => {
    const [reviews, pendingEvidence] = await Promise.all([getTeamReviews(), getTeamEvidence()]);
    setProfiles(reviews.profiles);
    setAssessments(reviews.assessments);
    setEvidence(pendingEvidence);
    setProblem(null);
  }, (error) => setProblem(error));
  const run = (action: () => Promise<void>) => {
    setPending(true);
    void action().then(load).then(() => window.dispatchEvent(new Event("skillhorizon-team"))).catch((error: unknown) => {
      setProblem(error instanceof Error ? error.message : "That review was refused.");
    }).finally(() => setPending(false));
  };
  return (
    <section data-screen="reviews">
      <PageHead
        eyebrow="People manager"
        title="Reviews and evidence"
        lede="Confirm the profile, review the assessment context, and validate evidence you can see. Evidence validation records the reporting line in force."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      <div className="filters" role="group" aria-label="Review filter">
        <button type="button" aria-pressed={filter === "all"} onClick={() => setFilter("all")}>All</button>
        <button type="button" aria-pressed={filter === "profile"} onClick={() => setFilter("profile")}>Profiles</button>
        <button type="button" aria-pressed={filter === "assessment"} onClick={() => setFilter("assessment")}>Assessments</button>
        <button type="button" aria-pressed={filter === "evidence"} onClick={() => setFilter("evidence")}>Evidence</button>
      </div>
      {filter === "all" || filter === "profile" ? (
        <article className="surface-card">
          <header><div><h2>Profile confirmation</h2><p className="card-note">Direct reports only</p></div></header>
          {profiles.length === 0 ? <p className="empty-note">No profile is waiting.</p> : null}
          <ul className="gap-list">
            {profiles.map((item) => (
              <li key={item.employeeId}>
                <b>{item.displayName}</b>
                <span>{item.profileName}</span>
                <button type="button" className="draft-save" disabled={pending} onClick={() => run(() => confirmProfile(item.employeeId))}>Confirm profile</button>
              </li>
            ))}
          </ul>
        </article>
      ) : null}
      {filter === "all" || filter === "assessment" ? (
        <article className="surface-card">
          <header><div><h2>Assessment context</h2></div></header>
          {assessments.length === 0 ? <p className="empty-note">No assessment is waiting.</p> : null}
          <ul className="gap-list">
            {assessments.map((item) => (
              <li key={item.resultId}>
                <b>{item.displayName}</b>
                <span>Sealed result</span>
                <button type="button" className="draft-save" disabled={pending} onClick={() => run(() => reviewAssessment(item.employeeId, "Context reviewed."))}>Review context</button>
              </li>
            ))}
          </ul>
        </article>
      ) : null}
      {filter === "all" || filter === "evidence" ? (
        <article className="surface-card">
          <header><div><h2>Evidence</h2></div></header>
          {evidence.length === 0 ? <p className="empty-note">No evidence is waiting.</p> : null}
          <ul className="gap-list">
            {evidence.map((item) => (
              <li key={item.stepId}>
                <b>{item.displayName}</b>
                <span>{item.title} · {item.fileName}</span>
                <button type="button" className="draft-save" disabled={pending} onClick={() => run(() => validateStep({ subjectId: item.subjectId, stepId: item.stepId }))}>Validate evidence</button>
              </li>
            ))}
          </ul>
        </article>
      ) : null}
    </section>
  );
}

function CoachingPage() {
  const [snapshot, setSnapshot] = useState<TeamSnapshot | null>(null);
  const [commitments, setCommitments] = useState<CoachingCommitment[]>([]);
  const [problem, setProblem] = useState<string | null>(null);
  const [subjectId, setSubjectId] = useState("");
  const [title, setTitle] = useState("");
  const [dueOn, setDueOn] = useState("");
  const [pending, setPending] = useState(false);
  useTitle("Coaching");
  const load = useLoader(async () => {
    const [next, rows] = await Promise.all([getTeamSnapshot(), getCommitments()]);
    setSnapshot(next);
    setCommitments(rows);
    setSubjectId((current) => current || next?.members[0]?.employeeId || "");
    setProblem(null);
  }, (error) => setProblem(error));
  return (
    <section data-screen="coaching">
      <PageHead
        eyebrow="People manager"
        title="Coaching"
        lede="Commitments you make for a direct report. Stalled-progress days are not stored, so this page does not invent a check-in count."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {!snapshot && !problem ? <p className="empty-note">The team snapshot has not been written, so a commitment cannot be stored.</p> : null}
      {snapshot && snapshot.members.length === 0 ? <p className="empty-note">No one reports to you, so a commitment cannot be stored.</p> : null}
      {snapshot && snapshot.members.length > 0 ? (
        <form
          className="task-close"
          onSubmit={(event) => {
            event.preventDefault();
            setPending(true);
            void createCommitment({ subjectId, title, dueOn })
              .then(() => {
                setTitle("");
                return load();
              })
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That commitment was not stored."))
              .finally(() => setPending(false));
          }}
        >
          <h2>New commitment</h2>
          <label>
            Person
            <select value={subjectId} onChange={(event) => setSubjectId(event.target.value)}>
              {snapshot.members.map((member) => (
                <option key={member.employeeId} value={member.employeeId}>{member.displayName}</option>
              ))}
            </select>
          </label>
          <label>
            Title
            <input value={title} onChange={(event) => setTitle(event.target.value)} />
          </label>
          <label>
            Due
            <input type="date" value={dueOn} onChange={(event) => setDueOn(event.target.value)} />
          </label>
          <button type="submit" className="draft-save" disabled={pending || title.trim().length === 0 || dueOn.length === 0}>Store commitment</button>
        </form>
      ) : null}
      {commitments.length === 0 ? <p className="empty-note">No commitments are stored.</p> : null}
      {commitments.length > 0 ? (
        <ul className="gap-list">
          {commitments.map((commitment) => (
            <li key={commitment.commitmentId}>
              <b>{commitment.displayName}</b>
              <span>{commitment.title}</span>
              <span>Due {commitment.dueOn}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

function SkillsPage() {
  const [people, setPeople] = useState<TeamSkillPerson[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("Team Skills");
  const load = useLoader(async () => {
    setPeople(await getTeamSkills());
    setProblem(null);
  }, (error) => setProblem(error));
  return (
    <section data-screen="skills">
      <PageHead
        eyebrow="People manager"
        title="Team skills"
        lede="Sealed proficiency against the benchmark for the direct reports in the snapshot."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {people && people.length === 0 ? <p className="empty-note">The team snapshot has not been written.</p> : null}
      {people?.map((person) => (
        <article key={person.employeeId}>
          <h2>{person.displayName}</h2>
          <p>{positionLabel(person.position)}</p>
          {person.skills.length === 0 ? <p>Nothing is sealed yet.</p> : null}
          {person.skills.length > 0 ? (
            <div className="team-scroll">
              <table className="team-table">
                <thead>
                  <tr><th>Skill</th><th>Sealed</th><th>Target</th><th>Gap</th></tr>
                </thead>
                <tbody>
                  {person.skills.map((skill) => (
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
        </article>
      ))}
      <PublishedStandards />
    </section>
  );
}

function positionLabel(position: string) {
  if (position === "met") return "At benchmark";
  if (position === "gap") return "Below benchmark";
  return "Not measured";
}

function useLoader(action: () => Promise<void>, onError: (message: string) => void) {
  const actionRef = useRef(action);
  const errorRef = useRef(onError);
  actionRef.current = action;
  errorRef.current = onError;
  const load = useCallback(async () => {
    try {
      await actionRef.current();
    } catch (error) {
      errorRef.current(error instanceof Error ? error.message : "The team could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return load;
}

function Problem({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <p className="gate-alert" role="alert">
      {text}
      <button type="button" className="team-retry" onClick={onRetry}>Try again</button>
    </p>
  );
}
