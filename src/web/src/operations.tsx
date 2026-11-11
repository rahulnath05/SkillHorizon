import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  declareLevel,
  getActivationCentre,
  getDeclared,
  getIntake,
  getPilot,
  getProgrammeSnapshot,
  takePilot,
  takeProgrammeSnapshot,
  type ActivationWave,
  type DeclaredRow,
  type IntakeSummary,
  type ProgrammeSnapshot,
} from "./api";
import { formatAsOf, useTitle } from "./ui";

export function OperationsPage({ screen }: { screen: "studio" | "scorecard" | "operations" | "declared" | "pilot" | "rolling" }) {
  if (screen === "scorecard") return <SnapshotPage kind="scorecard" />;
  if (screen === "operations") return <SnapshotPage kind="operations" />;
  if (screen === "declared") return <DeclaredPage />;
  if (screen === "pilot") return <PilotPage />;
  if (screen === "rolling") return <RollingPage />;
  return <OverviewPage />;
}

function SnapshotFigures({ snapshot }: { snapshot: ProgrammeSnapshot }) {
  return (
    <ul className="home-figures">
      <li>
        <b>{snapshot.completedCount}</b>
        <span>Sittings completed</span>
      </li>
      <li>
        <b>{snapshot.openCount}</b>
        <span>Still open</span>
      </li>
      <li>
        <b>{snapshot.durationSeconds}s</b>
        <span>Completed sitting time</span>
      </li>
    </ul>
  );
}

function SnapshotPage({ kind }: { kind: "operations" | "scorecard" }) {
  const title = kind === "scorecard" ? "Programme scorecard" : "Assessment operations";
  useTitle(title);
  const [snapshot, setSnapshot] = useState<ProgrammeSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    setSnapshot(await getProgrammeSnapshot(kind));
  }, [kind]);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The snapshot could not be loaded."));
  }, [load]);

  return (
    <section data-screen={kind}>
      <p className="eyebrow">Assessment studio</p>
      <h1>{title}</h1>
      <p>This snapshot counts sittings completed, sittings still open, and the total time of completed sittings. It stores no response text.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {snapshot ? (
        <>
          <p>As of {formatAsOf(snapshot.asOf)}</p>
          <SnapshotFigures snapshot={snapshot} />
        </>
      ) : (
        <p>No snapshot has been taken.</p>
      )}
      <button
        type="button"
        className="draft-save"
        disabled={pending}
        onClick={() => {
          setPending(true);
          void takeProgrammeSnapshot()
            .then(() => load())
            .then(() => setProblem(null))
            .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The snapshot was refused."))
            .finally(() => setPending(false));
        }}
      >
        Take snapshot
      </button>
    </section>
  );
}

function OverviewPage() {
  useTitle("Studio overview");
  const [intake, setIntake] = useState<IntakeSummary | null>(null);
  const [snapshot, setSnapshot] = useState<ProgrammeSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    const [nextIntake, nextSnapshot] = await Promise.all([getIntake("/api/studio/overview"), getProgrammeSnapshot("operations")]);
    setIntake(nextIntake);
    setSnapshot(nextSnapshot);
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The overview could not be loaded."));
  }, [load]);

  return (
    <section data-screen="studio-overview">
      <p className="eyebrow">Assessment studio</p>
      <h1>Studio overview</h1>
      <p>The overview reads the operations snapshot and the admissions stored for launch and rolling intake.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {snapshot ? (
        <>
          <p>Operations snapshot as of {formatAsOf(snapshot.asOf)}</p>
          <SnapshotFigures snapshot={snapshot} />
        </>
      ) : <p>No operations snapshot has been taken.</p>}
      {intake ? (
        <ul className="home-figures">
          <li>
            <b>{intake.launchCount}</b>
            <span>Launch admissions</span>
          </li>
          <li>
            <b>{intake.rollingCount}</b>
            <span>Rolling admissions</span>
          </li>
        </ul>
      ) : null}
      <button
        type="button"
        className="draft-save"
        disabled={pending}
        onClick={() => {
          setPending(true);
          void takeProgrammeSnapshot()
            .then(() => load())
            .then(() => setProblem(null))
            .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The snapshot was refused."))
            .finally(() => setPending(false));
        }}
      >
        Take snapshot
      </button>
    </section>
  );
}

function DeclaredPage() {
  useTitle("Declared versus assessed");
  const [rows, setRows] = useState<DeclaredRow[]>([]);
  const [subjectId, setSubjectId] = useState("");
  const [skillCode, setSkillCode] = useState("");
  const [declaredRank, setDeclaredRank] = useState("2");
  const [declaredName, setDeclaredName] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    setRows(await getDeclared());
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The comparison could not be loaded."));
  }, [load]);

  function save(event: FormEvent) {
    event.preventDefault();
    const rank = Number(declaredRank);
    if (!Number.isInteger(rank) || rank < 1) {
      setProblem("A declared level is a positive rank.");
      return;
    }
    setPending(true);
    setNotice(null);
    void declareLevel({ subjectId: subjectId.trim(), skillCode: skillCode.trim(), declaredRank: rank, declaredName: declaredName.trim() })
      .then(() => load())
      .then(() => {
        setProblem(null);
        setNotice("The declared level is stored apart from the sealed result.");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The declaration was refused."))
      .finally(() => setPending(false));
  }

  return (
    <section data-screen="declared">
      <p className="eyebrow">Assessment studio</p>
      <h1>Declared versus assessed</h1>
      <p>A declared level is its own row. Writing one leaves the sealed proficiency unchanged.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {rows.length === 0 ? <p>No declared or assessed level is stored.</p> : (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr>
                <th>Person</th>
                <th>Skill</th>
                <th>Declared</th>
                <th>Assessed</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={`${row.subjectId}-${row.skillCode}`}>
                  <td>{row.subjectId}</td>
                  <td>{row.skillCode}</td>
                  <td>{row.declaredRank === null ? "Not declared" : `${row.declaredName} · ${row.declaredRank}`}</td>
                  <td>{row.assessedRank === null ? "Not measured" : `${row.assessedName} · ${row.assessedRank}`}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <form className="task-close" onSubmit={save}>
        <h2>Record a declared level</h2>
        <label>
          Person
          <input value={subjectId} onChange={(event) => setSubjectId(event.target.value)} required />
        </label>
        <label>
          Skill
          <input value={skillCode} onChange={(event) => setSkillCode(event.target.value)} required />
        </label>
        <label>
          Rank
          <input value={declaredRank} onChange={(event) => setDeclaredRank(event.target.value)} inputMode="numeric" required />
        </label>
        <label>
          Level name
          <input value={declaredName} onChange={(event) => setDeclaredName(event.target.value)} required />
        </label>
        <button type="submit" className="draft-save" disabled={pending}>Save declaration</button>
      </form>
    </section>
  );
}

function PilotPage() {
  useTitle("Pilot report");
  const [waves, setWaves] = useState<ActivationWave[]>([]);
  const [waveId, setWaveId] = useState("");
  const [snapshot, setSnapshot] = useState<ProgrammeSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    void getActivationCentre()
      .then((centre) => {
        setWaves(centre.waves);
        setWaveId((current) => current || centre.waves[0]?.waveId || "");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The waves could not be loaded."));
  }, []);

  useEffect(() => {
    if (!waveId) return;
    void getPilot(waveId)
      .then((next) => {
        setSnapshot(next);
        setProblem(null);
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The pilot report could not be loaded."));
  }, [waveId]);

  return (
    <section data-screen="pilot">
      <p className="eyebrow">Assessment studio</p>
      <h1>Pilot report</h1>
      <p>The report is a snapshot for one wave: sittings completed, sittings still open, and the total time of completed sittings.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {waves.length === 0 ? <p>No wave is open.</p> : (
        <label className="task-close">
          Wave
          <select value={waveId} onChange={(event) => setWaveId(event.target.value)}>
            {waves.map((wave) => (
              <option key={wave.waveId} value={wave.waveId}>{wave.profileName} · {wave.admittedCount} admitted</option>
            ))}
          </select>
        </label>
      )}
      {snapshot ? (
        <>
          <p>As of {formatAsOf(snapshot.asOf)}</p>
          <SnapshotFigures snapshot={snapshot} />
        </>
      ) : waveId ? <p>No pilot snapshot has been taken for this wave.</p> : null}
      <button
        type="button"
        className="draft-save"
        disabled={pending || !waveId}
        onClick={() => {
          setPending(true);
          void takePilot(waveId)
            .then((next) => {
              setSnapshot(next);
              setProblem(null);
            })
            .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The pilot report was refused."))
            .finally(() => setPending(false));
        }}
      >
        Take pilot snapshot
      </button>
    </section>
  );
}

function RollingPage() {
  useTitle("Rolling intake");
  const [intake, setIntake] = useState<IntakeSummary | null>(null);
  const [snapshot, setSnapshot] = useState<ProgrammeSnapshot | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const load = useCallback(async () => {
    const [nextIntake, nextSnapshot] = await Promise.all([getIntake("/api/studio/rolling"), getProgrammeSnapshot("operations")]);
    setIntake(nextIntake);
    setSnapshot(nextSnapshot);
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "Rolling intake could not be loaded."));
  }, [load]);

  return (
    <section data-screen="rolling">
      <p className="eyebrow">Assessment studio</p>
      <h1>Rolling intake</h1>
      <p>People admitted after a wave opened. This page reads those admissions and the operations snapshot.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {snapshot ? <p>Operations snapshot as of {formatAsOf(snapshot.asOf)}. {snapshot.completedCount} completed, {snapshot.openCount} still open.</p> : <p>No operations snapshot has been taken.</p>}
      {intake && intake.rolling.length === 0 ? <p>No one has joined by rolling intake.</p> : null}
      {intake && intake.rolling.length > 0 ? (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr>
                <th>Person</th>
                <th>Wave</th>
                <th>Admitted</th>
              </tr>
            </thead>
            <tbody>
              {intake.rolling.map((person) => (
                <tr key={`${person.waveId}-${person.subjectId}`}>
                  <td>{person.subjectId}</td>
                  <td>{person.waveId}</td>
                  <td>{formatAsOf(person.admittedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
