import { useCallback, useEffect, useState, type FormEvent } from "react";
import {
  getItemBank,
  getMarks,
  getPanel,
  nameExpert,
  writeItem,
  type BankItem,
  type BankMark,
  type PanelExpert,
} from "./api";
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
      <p className="eyebrow">Assessment studio</p>
      <h1>Item bank</h1>
      <p>Each item names a skill and a difficulty. Writing an item uses the assessment authoring grant. An employee grant does not open this bank.</p>
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
      <p className="eyebrow">Assessment studio</p>
      <h1>Rubric and marking</h1>
      <p>A typed answer is marked in the assessment database against the rubric on the item. The sealed result stores a proficiency and a result id.</p>
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
      <p className="eyebrow">Assessment studio</p>
      <h1>SME panel</h1>
      <p>Name the expert for a domain and state their credentials. The panel row holds no responses.</p>
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
