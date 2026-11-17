import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  declareLevel,
  getActivationCentre,
  getDeclared,
  getIntake,
  getPilot,
  getProgrammeSnapshot,
  getSkillAverages,
  getStudioMeasures,
  takePilot,
  takeProgrammeSnapshot,
  type ActivationWave,
  type DeclaredRow,
  type IntakeSummary,
  type ProgrammeSnapshot,
  type SkillAverage,
  type StudioMeasures,
} from "./api";
import { PageHead } from "./frame";
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
      <PageHead
        eyebrow={kind === "scorecard" ? "DR-09 · programme scorecard" : "DR-04 · live through the window"}
        title={kind === "scorecard" ? "Where wave 1 actually stands." : "Who has sat, who has not, and how long it took."}
        lede={kind === "scorecard" ? "One line per question, using the stored snapshot and the geography rows. Completion is shown from those rows." : "The geography table is stored by place. The sitting snapshot remains underneath it."}
      />
      {kind === "scorecard" ? <ScorecardBoard /> : <GeographyBoard />}
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
      <PageHead eyebrow="Assessment Studio" title="What the questions are, and who stands behind them." lede="The overview reads the operations snapshot, the admissions, and the stored blockers. It does not invent a count." />
      <StudioOverviewBoard intake={intake} snapshot={snapshot} />
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
      <PageHead eyebrow="DR-06 · declared against assessed" title="What people said, next to what was measured." lede="The skill averages are grouped from declared levels and sealed proficiencies. A person row is still recorded underneath." />
      <SkillAverageTable />
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
      <PageHead eyebrow="DR-03 · the pilot" title="What the pilot proved, and what it did not." lede="Item statistics are stored by skill. The wave snapshot stays available underneath." />
      <PilotStats />
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
      <PageHead eyebrow="PR-08 · outside the wave" title="People who join after the window opened." lede="The steps and talent groups are stored rows. The people admitted after the wave opened are listed underneath." />
      <RollingBoard intake={intake} />
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

function useMeasures() {
  const [measures, setMeasures] = useState<StudioMeasures | null>(null);
  useEffect(() => {
    void getStudioMeasures().then(setMeasures).catch(() => setMeasures(null));
  }, []);
  return measures;
}

function StudioOverviewBoard({ intake, snapshot }: { intake: IntakeSummary | null; snapshot: ProgrammeSnapshot | null }) {
  const measures = useMeasures();
  const items = measures?.bankDepth.reduce((total, row) => total + (row.spread ?? []).reduce((sum, value) => sum + value, 0), 0) ?? 0;
  const typed = measures?.bankDepth.filter((row) => row.typedPercent > 0).length ?? 0;
  const experts = measures?.experts.length ?? 0;
  const thin = measures?.bankDepth.filter((row) => row.state === "thin").length ?? 0;
  return (
    <>
      <div className="stat-row">
        <div><span className="kicker">Items live</span><strong>{items}</strong><span className="hint">Sum of the stored L1–L5 spreads</span></div>
        <div><span className="kicker">Typed-answer banks</span><strong>{typed}</strong></div>
        <div><span className="kicker">SMEs on panel</span><strong>{experts}</strong></div>
        <div className="ink"><span className="kicker">Needs authoring</span><strong>{thin}</strong><span className="hint">Thin bank rows</span></div>
      </div>
      <p className="card-note">ET-01 · difficulty moves inside a sitting. The adaptive engine page reads the stored selection trace.</p>
      <article className="surface-card">
        <header><h2>What is blocking a full run</h2></header>
        {(measures?.blockers.length ?? 0) === 0 ? <p className="empty-note">No blocker is stored.</p> : (
          <table className="team-table"><tbody>{measures?.blockers.map((row) => <tr key={row.title}><td><b>{row.title}</b></td><td>{row.detail}</td><td>{row.stateLabel}</td></tr>)}</tbody></table>
        )}
      </article>
      {snapshot ? <p>Operations snapshot: {snapshot.completedCount} completed, {snapshot.openCount} open.</p> : null}
      {intake ? <p>{intake.launchCount} launch admissions, {intake.rollingCount} rolling admissions.</p> : null}
    </>
  );
}

function ScorecardBoard() {
  const measures = useMeasures();
  const card = measures?.scorecard;
  if (!card) return null;
  return (
    <>
      <div className="stat-row">
        <div><span className="kicker">Invited</span><strong>{card.invited}</strong><span className="hint">Geography rows</span></div>
        <div><span className="kicker">Assessed</span><strong>{card.sat}</strong><span className="hint">{card.completed} completed sittings in the snapshot</span></div>
        <div><span className="kicker">Not started</span><strong>{card.notStarted}</strong></div>
        <div className="ink"><span className="kicker">Sitting time</span><strong>{card.durationSeconds}s</strong></div>
      </div>
      <table className="team-table"><thead><tr><th>Question</th><th>Answer</th><th>State</th></tr></thead><tbody>
        {card.questions.map((row) => <tr key={row.question}><td>{row.question}</td><td>{row.answer}</td><td>{row.stateLabel}</td></tr>)}
      </tbody></table>
    </>
  );
}

function GeographyBoard() {
  const measures = useMeasures();
  const rows = measures?.geography ?? [];
  const notStarted = rows.reduce((total, row) => total + row.notStartedCount, 0);
  const target = rows[0]?.targetPercent;
  return (
    <>
      <div className="stat-row">
        <div><span className="kicker">Places</span><strong>{rows.length}</strong></div>
        <div><span className="kicker">Not started</span><strong>{notStarted}</strong></div>
        <div><span className="kicker">Target</span><strong>{target ?? "—"}%</strong></div>
      </div>
      {rows.length === 0 ? <p className="empty-note">No geography row is stored.</p> : (
        <table className="team-table"><thead><tr><th>Geography</th><th>Invited</th><th>Sat</th><th>Completion</th><th>Median time</th><th>Not started</th></tr></thead><tbody>
          {rows.map((row) => {
            const rate = row.invitedCount === 0 ? 0 : Math.round((row.satCount / row.invitedCount) * 1000) / 10;
            return <tr key={row.placeName}><td>{row.placeName}</td><td>{row.invitedCount}</td><td>{row.satCount}</td><td>{rate}% against {row.targetPercent}%</td><td>{row.medianSeconds}s</td><td>{row.notStartedCount}</td></tr>;
          })}
        </tbody></table>
      )}
      <p className="card-note">The target on each row is the stored target. Places under it are the ones to push.</p>
    </>
  );
}

function SkillAverageTable() {
  const [rows, setRows] = useState<SkillAverage[]>([]);
  const [note, setNote] = useState<string | null>(null);
  useEffect(() => {
    void getSkillAverages().then((body) => { setRows(body.averages); setNote(body.note); }).catch(() => { setRows([]); setNote(null); });
  }, []);
  return (
    <>
      {rows.length === 0 ? <p className="empty-note">No declared or assessed average is stored.</p> : (
        <table className="team-table"><thead><tr><th>Anchor skill</th><th>Declared by</th><th>Declared avg</th><th>Assessed avg</th><th>Gap</th><th>Aliases mapped</th></tr></thead><tbody>
          {rows.map((row) => {
            const gap = row.declaredAverage !== null && row.assessedAverage !== null ? row.declaredAverage - row.assessedAverage : null;
            return <tr key={row.skillCode}><td>{row.skillName}</td><td>{row.declaredBy}</td><td>{row.declaredAverage ?? "—"}</td><td>{row.assessedAverage ?? "—"}</td><td>{gap === null ? "not measured" : gap.toFixed(2)}</td><td>{row.aliasCount}</td></tr>;
          })}
        </tbody></table>
      )}
      {note ? <p className="card-note">{note}</p> : null}
    </>
  );
}

function PilotStats() {
  const measures = useMeasures();
  const rows = measures?.itemStatistics ?? [];
  const held = rows.filter((row) => row.disposition === "held").length;
  return (
    <>
      <div className="stat-row">
        <div><span className="kicker">Skills measured</span><strong>{rows.length}</strong></div>
        <div className="ink"><span className="kicker">Items held back</span><strong>{held}</strong></div>
      </div>
      {rows.length === 0 ? <p className="empty-note">No item statistic is stored.</p> : (
        <table className="team-table"><thead><tr><th>Anchor skill</th><th>Items</th><th>Difficulty</th><th>Discrimination</th><th>Median time</th><th>Verdict</th></tr></thead><tbody>
          {rows.map((row) => <tr key={row.skillCode}><td>{row.skillCode}</td><td>{row.itemCount}</td><td>{row.difficulty}</td><td>{row.discrimination}</td><td>{row.medianSeconds}s</td><td>{row.disposition === "held" ? "Held back" : "Passed"}</td></tr>)}
        </tbody></table>
      )}
      {measures?.heldNote ? <p>{measures.heldNote}</p> : null}
    </>
  );
}

function RollingBoard({ intake }: { intake: IntakeSummary | null }) {
  const measures = useMeasures();
  const groups = measures?.rollingGroups ?? [];
  const joined = groups.reduce((total, row) => total + row.joinedCount, 0);
  const assessed = groups.reduce((total, row) => total + row.assessedCount, 0);
  const plans = groups.reduce((total, row) => total + row.planCount, 0);
  const waiting = groups.reduce((total, row) => total + row.waitingCount, 0);
  return (
    <>
      <div className="stat-row">
        <div><span className="kicker">Joined</span><strong>{joined}</strong></div>
        <div><span className="kicker">Assessed</span><strong>{assessed}</strong></div>
        <div><span className="kicker">Plans issued</span><strong>{plans}</strong></div>
        <div className="ink"><span className="kicker">Still inside the window</span><strong>{waiting}</strong></div>
      </div>
      <article className="surface-card">
        <header><h2>How a joiner is picked up</h2></header>
        <table className="team-table"><tbody>{(measures?.rollingSteps ?? []).map((step) => <tr key={step.sortOrder}><td><b>{step.sortOrder}. {step.title}</b></td><td>{step.detail}</td></tr>)}</tbody></table>
      </article>
      <table className="team-table"><thead><tr><th>Talent group</th><th>Joined</th><th>Assessed</th><th>Plan issued</th><th>Waiting</th></tr></thead><tbody>
        {groups.map((row) => <tr key={row.talentGroup}><td>{row.talentGroup}</td><td>{row.joinedCount}</td><td>{row.assessedCount}</td><td>{row.planCount}</td><td>{row.waitingCount}</td></tr>)}
      </tbody></table>
      <p className="card-note">Rolling intake uses the same item bank. {intake?.rollingCount ?? 0} people are on the admission rows.</p>
    </>
  );
}
