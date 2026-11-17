import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  getItemBank,
  getMarks,
  getPanel,
  getStudioMeasures,
  nameExpert,
  writeItem,
  type BankItem,
  type BankMark,
  type PanelExpert,
  type StudioMeasures,
} from "./api";
import { PageHead } from "./frame";
import { formatAsOf, useTitle } from "./ui";

export function StudioPage({ screen }: { screen: "bank" | "marking" | "panel" }) {
  if (screen === "marking") return <MarkingPage />;
  if (screen === "panel") return <PanelPage />;
  return <BankPage />;
}

function BankPage() {
  useTitle("Item bank");
  const [items, setItems] = useState<BankItem[]>([]);
  const [skillCode, setSkillCode] = useState("");
  const [difficulty, setDifficulty] = useState("3");
  const [prompt, setPrompt] = useState("");
  const [choices, setChoices] = useState("Beginner\nAdvanced");
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    setItems(await getItemBank());
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The item bank could not be loaded."));
  }, [load]);

  function save(event: FormEvent) {
    event.preventDefault();
    const level = Number(difficulty);
    const labels = choices.split("\n").map((line) => line.trim()).filter((line) => line.length > 0);
    if (!Number.isInteger(level) || level < 1 || level > 5) {
      setProblem("Difficulty is a whole number from 1 to 5.");
      return;
    }
    setPending(true);
    setNotice(null);
    void writeItem({
      skillCode: skillCode.trim(),
      prompt: prompt.trim(),
      difficulty: level,
      choices: labels.map((label, index) => ({ rank: index + 1, label })),
    })
      .then(() => load())
      .then(() => {
        setProblem(null);
        setNotice("The item is in the bank.");
        setPrompt("");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The item was refused."))
      .finally(() => setPending(false));
  }

  return (
    <section data-screen="item-bank">
      <PageHead eyebrow="Item bank" title="What exists, and how hard it is." lede="Difficulty spread runs L1 to L5. The counts below are the stored bank rows, and writing an item still uses the assessment authoring grant." />
      <BankDepth />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {items.length === 0 ? <p>No item is stored.</p> : (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr>
                <th>Skill</th>
                <th>Difficulty</th>
                <th>Kind</th>
                <th>Prompt</th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.itemId} data-item={item.itemId}>
                  <td>{item.skillCode}</td>
                  <td>{item.difficulty}</td>
                  <td>{item.rubric ? "Typed answer" : "Selected answer"}</td>
                  <td>{item.prompt}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <form className="task-close" onSubmit={save}>
        <h2>Write an item</h2>
        <label>
          Skill
          <input value={skillCode} onChange={(event) => setSkillCode(event.target.value)} required />
        </label>
        <label>
          Difficulty
          <select value={difficulty} onChange={(event) => setDifficulty(event.target.value)}>
            <option value="1">1</option>
            <option value="2">2</option>
            <option value="3">3</option>
            <option value="4">4</option>
            <option value="5">5</option>
          </select>
        </label>
        <label>
          Prompt
          <textarea value={prompt} onChange={(event) => setPrompt(event.target.value)} required />
        </label>
        <label>
          Choices, one per line
          <textarea value={choices} onChange={(event) => setChoices(event.target.value)} required />
        </label>
        <button type="submit" className="draft-save" disabled={pending}>Save item</button>
      </form>
    </section>
  );
}

function MarkingPage() {
  useTitle("Rubric and marking");
  const [items, setItems] = useState<BankItem[]>([]);
  const [marks, setMarks] = useState<BankMark[]>([]);
  const [skillCode, setSkillCode] = useState("");
  const [difficulty, setDifficulty] = useState("2");
  const [prompt, setPrompt] = useState("");
  const [rubric, setRubric] = useState("1|copy|Repeats the question\n2|owner|Names who must be told");
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    const [bank, marked] = await Promise.all([getItemBank(), getMarks()]);
    setItems(bank.filter((item) => item.rubric));
    setMarks(marked);
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "Rubrics could not be loaded."));
  }, [load]);

  function save(event: FormEvent) {
    event.preventDefault();
    const level = Number(difficulty);
    setPending(true);
    setNotice(null);
    void writeItem({
      skillCode: skillCode.trim(),
      prompt: prompt.trim(),
      difficulty: level,
      rubric: rubric.trim(),
    })
      .then(() => load())
      .then(() => {
        setProblem(null);
        setNotice("The rubric is stored on the item.");
        setPrompt("");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The rubric was refused."))
      .finally(() => setPending(false));
  }

  return (
    <section data-screen="rubric-marking">
      <PageHead eyebrow="ET-02 · rubric-scored free text" title="Typed answers are marked, and the marking is checked." lede="A typed answer is marked in the assessment database against the rubric on the item. Agreement figures are stored without copying the response into another table." />
      <MarkingWalk marks={marks} />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {items.length === 0 ? <p>No rubric is stored.</p> : items.map((item) => (
        <article key={item.itemId} data-rubric={item.itemId}>
          <h2>{item.skillCode} · difficulty {item.difficulty}</h2>
          <p>{item.prompt}</p>
          <pre>{item.rubric}</pre>
        </article>
      ))}
      {marks.length === 0 ? <p>No typed answer has been marked.</p> : (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr>
                <th>Skill</th>
                <th>Band</th>
                <th>Marked</th>
                <th>Response</th>
              </tr>
            </thead>
            <tbody>
              {marks.map((mark) => (
                <tr key={`${mark.itemId}-${mark.markedAt}`}>
                  <td>{mark.skillCode}</td>
                  <td>{mark.band}</td>
                  <td>{formatAsOf(mark.markedAt)}</td>
                  <td>{mark.responseBody}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <form className="task-close" onSubmit={save}>
        <h2>Store a rubric</h2>
        <label>
          Skill
          <input value={skillCode} onChange={(event) => setSkillCode(event.target.value)} required />
        </label>
        <label>
          Difficulty
          <select value={difficulty} onChange={(event) => setDifficulty(event.target.value)}>
            <option value="1">1</option>
            <option value="2">2</option>
            <option value="3">3</option>
            <option value="4">4</option>
            <option value="5">5</option>
          </select>
        </label>
        <label>
          Prompt
          <textarea value={prompt} onChange={(event) => setPrompt(event.target.value)} required />
        </label>
        <label>
          Rubric, one band per line: rank|cue|what the band means
          <textarea value={rubric} onChange={(event) => setRubric(event.target.value)} required />
        </label>
        <button type="submit" className="draft-save" disabled={pending}>Save rubric</button>
      </form>
    </section>
  );
}

function PanelPage() {
  useTitle("SME panel");
  const [panel, setPanel] = useState<PanelExpert[]>([]);
  const [domainName, setDomainName] = useState("Data");
  const [expertName, setExpertName] = useState("");
  const [credentials, setCredentials] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    setPanel(await getPanel());
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The panel could not be loaded."));
  }, [load]);

  function save(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setNotice(null);
    void nameExpert({ domainName: domainName.trim(), expertName: expertName.trim(), credentials: credentials.trim() })
      .then(() => load())
      .then(() => {
        setProblem(null);
        setNotice("The expert is named for that domain.");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The panel refused that."))
      .finally(() => setPending(false));
  }

  return (
    <section data-screen="sme-panel">
      <PageHead eyebrow="ET-04 · named subject-matter experts" title="Every domain has a person behind it." lede="Items are authored and reviewed by named experts with stated credentials. Sign-off is dated. The panel row holds no responses." />
      <ExpertCards />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {panel.length === 0 ? <p>No expert is named.</p> : (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr>
                <th>Domain</th>
                <th>Expert</th>
                <th>Credentials</th>
              </tr>
            </thead>
            <tbody>
              {panel.map((expert) => (
                <tr key={expert.smeId} data-domain={expert.domainName}>
                  <td>{expert.domainName}</td>
                  <td>{expert.expertName}</td>
                  <td>{expert.credentials}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <form className="task-close" onSubmit={save}>
        <h2>Name an expert</h2>
        <label>
          Domain
          <input value={domainName} onChange={(event) => setDomainName(event.target.value)} required />
        </label>
        <label>
          Expert
          <input value={expertName} onChange={(event) => setExpertName(event.target.value)} required />
        </label>
        <label>
          Credentials
          <textarea value={credentials} onChange={(event) => setCredentials(event.target.value)} required />
        </label>
        <button type="submit" className="draft-save" disabled={pending}>Save panel row</button>
      </form>
    </section>
  );
}

export function EnginePage() {
  useTitle("Adaptive engine");
  return (
    <section data-screen="engine">
      <PageHead eyebrow="ET-01 · adaptive within the sitting" title="The next question depends on the last answer." lede="Exposure, selection, and stopping are stored rows. They do not include a response body." />
      <EngineBoard />
    </section>
  );
}

function useMeasures() {
  const [measures, setMeasures] = useState<StudioMeasures | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useEffect(() => {
    void getStudioMeasures()
      .then(setMeasures)
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The studio measures could not be loaded."));
  }, []);
  return { measures, problem };
}

function EngineBoard() {
  const { measures, problem } = useMeasures();
  const selection = measures?.selection ?? [];
  const first = selection[0];
  const last = selection[selection.length - 1];
  const stopping = measures?.stopping[0];
  return (
    <>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <div className="stat-row">
        <div><span className="kicker">Opens at</span><strong>{first ? first.estimate : "—"}</strong><span className="hint">{first?.ruleName ?? "No selection row"}</span></div>
        <div><span className="kicker">Items used</span><strong>{selection.length}</strong><span className="hint">{stopping ? `cap ${stopping.itemCap}` : "No stopping rule"}</span></div>
        <div><span className="kicker">Final estimate</span><strong>{last ? last.estimate : "—"}</strong></div>
        <div className="ink"><span className="kicker">Final band</span><strong>{last ? `±${last.band}` : "—"}</strong><span className="hint">{stopping?.detail ?? ""}</span></div>
      </div>
      <article className="surface-card">
        <header><h2>Selection</h2><p className="card-note">{selection[0]?.sittingLabel ?? "No item-selection trace is available."}</p></header>
        {selection.length === 0 ? <p className="empty-note">No item-selection trace is available.</p> : (
          <table className="team-table"><thead><tr><th>Step</th><th>Skill</th><th>Rule</th><th>Estimate</th><th>Band</th><th>Outcome</th></tr></thead><tbody>
            {selection.map((row) => <tr key={`${row.sittingLabel}-${row.stepNumber}`}><td>{row.stepNumber}</td><td>{row.skillCode}</td><td>{row.ruleName}</td><td>{row.estimate}</td><td>{row.band}</td><td>{row.outcome}</td></tr>)}
          </tbody></table>
        )}
      </article>
      <div className="engine-layout">
        <article>
          <strong>Exposure</strong>
          {(measures?.exposure.length ?? 0) === 0 ? <p className="empty-note">No exposure record is available.</p> : (
            <ul>{measures?.exposure.map((row) => <li key={`${row.skillCode}-${row.itemLabel}`}><b>{row.skillCode}</b> {row.itemLabel}: {row.exposedCount} against a {row.ceilingPercent}% ceiling</li>)}</ul>
          )}
        </article>
        <article>
          <strong>Stopping</strong>
          {(measures?.stopping.length ?? 0) === 0 ? <p className="empty-note">No stopping rule has been recorded.</p> : measures?.stopping.map((row) => <p key={row.ruleName}><b>{row.ruleName}</b> {row.detail}</p>)}
        </article>
        <article>
          <strong>What this is not</strong>
          <p>These rows are the stored engine trace. They are not a fixed form, and they do not include response text.</p>
        </article>
      </div>
    </>
  );
}

function MarkingWalk({ marks }: { marks: BankMark[] }) {
  const { measures } = useMeasures();
  const criteria = measures?.criteria ?? [];
  const record = measures?.records.find((row) => row.itemId === criteria[0]?.itemId) ?? null;
  const versions = (measures?.versions ?? []).filter((row) => record && row.itemId === record.itemId);
  const sample = marks[0];
  return (
    <>
      <article className="surface-card">
        <header><h2>One item, start to finish</h2></header>
        {criteria.length === 0 ? <p className="empty-note">No rubric criterion is stored.</p> : (
          <>
            <p>{criteria[0]?.prompt}</p>
            {sample ? <blockquote>{sample.responseBody}</blockquote> : <p className="empty-note">No typed answer has been marked.</p>}
            <table className="team-table"><thead><tr><th>Criterion</th><th>Weight</th><th>Meaning</th><th>Machine</th><th>Human</th></tr></thead><tbody>
              {criteria.map((row) => <tr key={`${row.itemId}-${row.criterionName}`}><td>{row.criterionName}</td><td>{row.weightPercent}%</td><td>{row.description}</td><td>{row.machineScore}</td><td>{row.humanScore}</td></tr>)}
            </tbody></table>
          </>
        )}
      </article>
      {record ? (
        <article className="surface-card">
          <header><h2>The item record</h2></header>
          <dl className="kv">
            <div><dt>Anchor skill</dt><dd>{record.anchorSkill}</dd></div>
            <div><dt>Target level</dt><dd>{record.targetLevel}</dd></div>
            <div><dt>Item type</dt><dd>{record.itemType}</dd></div>
            <div><dt>Rubric reference</dt><dd>{record.rubricReference}</dd></div>
            <div><dt>Author</dt><dd>{record.authorName}</dd></div>
            <div><dt>Reviewer</dt><dd>{record.reviewerName}</dd></div>
            <div><dt>Version</dt><dd>{record.versionLabel}</dd></div>
            <div><dt>Status</dt><dd>{record.statusLabel}</dd></div>
          </dl>
          <table className="team-table"><tbody>{versions.map((row) => <tr key={row.versionLabel}><td>{row.versionLabel}</td><td>{row.recordedOn}</td><td>{row.note}</td></tr>)}</tbody></table>
        </article>
      ) : null}
      <article className="surface-card">
        <header><h2>Agreement with human markers</h2><p className="card-note">Stored study. Response text stays on the mark row.</p></header>
        {(measures?.agreements.length ?? 0) === 0 ? <p className="empty-note">No agreement row is stored.</p> : (
          <table className="team-table"><thead><tr><th>Skill</th><th>Sample</th><th>Exact</th><th>Within one band</th><th>Weighted agreement</th><th>State</th></tr></thead><tbody>
            {measures?.agreements.map((row) => <tr key={row.skillCode}><td>{row.skillName}</td><td>{row.sampleCount}</td><td>{row.exactPercent}%</td><td>{row.adjacentPercent}%</td><td>{row.weightedAgreement}</td><td>{row.weightedAgreement >= 0.7 ? "Ships" : "Held back"}</td></tr>)}
          </tbody></table>
        )}
        {measures?.heldNote ? <p>{measures.heldNote}</p> : null}
      </article>
    </>
  );
}

function ExpertCards() {
  const { measures, problem } = useMeasures();
  const thin = (measures?.bankDepth ?? []).filter((row) => row.state === "thin");
  return (
    <>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <div className="expert-grid">
        {(measures?.experts ?? []).map((expert) => (
          <article key={expert.smeId} className="surface-card">
            <h2>{expert.expertName}</h2>
            <p>{expert.domainName}</p>
            <p>{expert.credentials}</p>
            <p>Owns {expert.skillsOwned || "no skill list"}</p>
            <p>{expert.authoredCount} authored · {expert.reviewedCount} reviewed{expert.signedOn ? ` · signed ${expert.signedOn}` : ""}</p>
          </article>
        ))}
      </div>
      {(measures?.experts.length ?? 0) === 0 ? <p className="empty-note">No expert is named.</p> : null}
      <article className="surface-card">
        <header><h2>Where the panel does not reach yet</h2></header>
        {thin.length === 0 ? <p className="empty-note">No bank row is marked too thin.</p> : (
          <table className="team-table"><thead><tr><th>Skill</th><th>Owner</th><th>State</th></tr></thead><tbody>
            {thin.map((row) => <tr key={row.skillCode}><td>{row.skillCode}</td><td>{row.smeName}</td><td>{row.state}</td></tr>)}
          </tbody></table>
        )}
      </article>
    </>
  );
}

function BankDepth() {
  const { measures, problem } = useMeasures();
  return (
    <>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {(measures?.bankDepth.length ?? 0) === 0 ? <p className="empty-note">No bank depth row is stored.</p> : (
        <table className="team-table"><thead><tr><th>Anchor skill</th><th>Population</th><th>Typed</th><th>L1–L5</th><th>SME</th><th>State</th></tr></thead><tbody>
          {measures?.bankDepth.map((row) => <tr key={row.skillCode}><td>{row.skillCode}</td><td>{row.populationBand}</td><td>{row.typedPercent}%</td><td>{(row.spread ?? []).join(" · ")}</td><td>{row.smeName}</td><td>{row.state === "live" ? "Live" : row.state === "review" ? "In review" : "Too thin"}</td></tr>)}
        </tbody></table>
      )}
      <article className="surface-card">
        <header><h2>Plan to close the thin banks</h2></header>
        {(measures?.bankPlans.length ?? 0) === 0 ? <p className="empty-note">No authoring plan is stored.</p> : (
          <table className="team-table"><thead><tr><th>Skill</th><th>Now</th><th>Target</th><th>Author</th><th>Reviewer</th><th>Due</th></tr></thead><tbody>
            {measures?.bankPlans.map((row) => <tr key={row.skillCode}><td>{row.skillCode}<span className="team-id">{row.phase}</span></td><td>{row.currentCount}</td><td>{row.targetDepth}</td><td>{row.authorName}</td><td>{row.reviewerName}</td><td>{row.dueOn}</td></tr>)}
          </tbody></table>
        )}
      </article>
      <p className="card-note">IB-01 · every stored bank row is listed. Thin rows have no population to calibrate.</p>
    </>
  );
}
