import { useCallback, useEffect, useRef, useState } from "react";
import { NavLink, useParams } from "react-router-dom";
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

const screens = [
  { id: "overview", label: "Overview" },
  { id: "team", label: "My Team" },
  { id: "reviews", label: "Reviews & Evidence" },
  { id: "coaching", label: "Coaching" },
  { id: "skills", label: "Team Skills" },
];

export function ManagerHome() {
  const { screen } = useParams();
  const current = screens.some((item) => item.id === screen) ? screen! : "overview";
  return (
    <div className="home">
      <nav className="home-nav" aria-label="People manager">
        {screens.map((item) => (
          <NavLink key={item.id} to={`/workspace/${item.id}`} aria-current={current === item.id ? "page" : undefined}>
            {item.label}
          </NavLink>
        ))}
      </nav>
      <div className="home-main">
        {current === "team" ? <TeamPage /> : null}
        {current === "reviews" ? <ReviewsPage /> : null}
        {current === "coaching" ? <CoachingPage /> : null}
        {current === "skills" ? <SkillsPage /> : null}
        {current === "overview" ? <OverviewPage /> : null}
      </div>
    </div>
  );
}

function OverviewPage() {
  const [snapshot, setSnapshot] = useState<TeamSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("Overview");
  const load = useLoader(async () => {
    setSnapshot(await getTeamSnapshot());
    setProblem(null);
  }, (error) => setProblem(error));
  const attention = snapshot?.members.filter((member) => member.attention) ?? [];
  return (
    <section data-screen="overview">
      <p className="eyebrow">People manager</p>
      <h1>Overview</h1>
      <p>Report counts and who needs attention, taken from the team snapshot.</p>
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {snapshot ? (
        <>
          <ul className="home-figures">
            <li><b>{snapshot.reportCount}</b><span>Direct reports</span></li>
            <li><b>{snapshot.attentionCount}</b><span>Need attention</span></li>
          </ul>
          <p>As of {snapshot.asOf}</p>
          {attention.length === 0 ? <p>No one in this snapshot is below the benchmark.</p> : null}
          {attention.length > 0 ? (
            <ul className="gap-list">
              {attention.map((member) => (
                <li key={member.employeeId}><b>{member.displayName}</b><span>Below the benchmark</span></li>
              ))}
            </ul>
          ) : null}
        </>
      ) : problem ? null : <p>The team snapshot has not been written.</p>}
    </section>
  );
}

function TeamPage() {
  const [snapshot, setSnapshot] = useState<TeamSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [note, setNote] = useState("");
  const [subjectId, setSubjectId] = useState("");
  const [pending, setPending] = useState(false);
  useTitle("My Team");
  const load = useLoader(async () => {
    const next = await getTeamSnapshot();
    setSnapshot(next);
    setSubjectId((current) => current || next?.members[0]?.employeeId || "");
    setProblem(null);
  }, (error) => setProblem(error));
  return (
    <section data-screen="team">
      <p className="eyebrow">People manager</p>
      <h1>My Team</h1>
      <p>Your team is the direct reports in the snapshot. A person outside the relationship is not listed.</p>
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {snapshot && snapshot.members.length === 0 ? <p>No one reports to you.</p> : null}
      {snapshot && snapshot.members.length > 0 ? (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr><th>Person</th><th>Position</th><th>Attention</th></tr>
            </thead>
            <tbody>
              {snapshot.members.map((member) => (
                <tr key={member.employeeId} data-report={member.employeeId}>
                  <td>{member.displayName}</td>
                  <td>{positionLabel(member.position)}</td>
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
    void action().then(load).catch((error: unknown) => {
      setProblem(error instanceof Error ? error.message : "That review was refused.");
    }).finally(() => setPending(false));
  };
  return (
    <section data-screen="reviews">
      <p className="eyebrow">People manager</p>
      <h1>Reviews & Evidence</h1>
      <p>Confirm the profile, review the assessment context, and validate evidence you can see. Evidence validation records the reporting line in force.</p>
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      <h2>Profile confirmation</h2>
      {profiles.length === 0 ? <p>No profile is waiting.</p> : null}
      <ul className="gap-list">
        {profiles.map((item) => (
          <li key={item.employeeId}>
            <b>{item.displayName}</b>
            <span>{item.profileName}</span>
            <button type="button" className="draft-save" disabled={pending} onClick={() => run(() => confirmProfile(item.employeeId))}>Confirm profile</button>
          </li>
        ))}
      </ul>
      <h2>Assessment context</h2>
      {assessments.length === 0 ? <p>No assessment is waiting.</p> : null}
      <ul className="gap-list">
        {assessments.map((item) => (
          <li key={item.resultId}>
            <b>{item.displayName}</b>
            <span>Sealed result</span>
            <button type="button" className="draft-save" disabled={pending} onClick={() => run(() => reviewAssessment(item.employeeId, "Context reviewed."))}>Review context</button>
          </li>
        ))}
      </ul>
      <h2>Evidence</h2>
      {evidence.length === 0 ? <p>No evidence is waiting.</p> : null}
      <ul className="gap-list">
        {evidence.map((item) => (
          <li key={item.stepId}>
            <b>{item.displayName}</b>
            <span>{item.title} · {item.fileName}</span>
            <button type="button" className="draft-save" disabled={pending} onClick={() => run(() => validateStep({ subjectId: item.subjectId, stepId: item.stepId }))}>Validate evidence</button>
          </li>
        ))}
      </ul>
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
      <p className="eyebrow">People manager</p>
      <h1>Coaching</h1>
      <p>Commitments you make for a direct report.</p>
      {problem ? <Problem text={problem} onRetry={load} /> : null}
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
      {commitments.length === 0 ? <p>No commitments are stored.</p> : null}
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
      <p className="eyebrow">People manager</p>
      <h1>Team Skills</h1>
      <p>Sealed proficiency against the benchmark for the direct reports in the snapshot.</p>
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {people && people.length === 0 ? <p>The team snapshot has not been written.</p> : null}
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
