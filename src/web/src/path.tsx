import { useCallback, useEffect, useState } from "react";
import { NavLink } from "react-router-dom";
import { addEvidence, getTeamPaths, openPath, validateStep, type DevelopmentPath, type ReportPath } from "./api";
import { PageHead } from "./frame";
import { useTitle } from "./ui";

export function DevelopmentPathPanel() {
  const [path, setPath] = useState<DevelopmentPath | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("My path");

  const load = useCallback(async () => {
    try {
      setPath(await openPath());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The path could not be opened.");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const steps = path?.steps ?? [];
  const closed = steps.filter((step) => step.status === "completed").length;
  const next = steps.find((step) => step.status === "assigned");

  return (
    <section className="path" data-screen="path">
      <PageHead
        eyebrow="Employee · My path"
        title="Your path"
        lede="Each step closes one gap against the published benchmark. A step names its provider. Handing work in closes the step. It does not move your sealed proficiency."
      />
      {problem ? (
        <p className="gate-alert" role="alert">
          {problem}
          <button type="button" className="team-retry" onClick={() => void load()}>Try again</button>
        </p>
      ) : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>The sequence</h2>
            <p className="card-note">{path ? `${closed} of ${steps.length} closed` : "Not opened"}</p>
          </div>
          <NavLink to="/workspace/tasks">Open my tasks</NavLink>
        </header>
        {!path ? <p className="empty-note">The path has not been opened.</p> : null}
        {path && steps.length === 0 ? <p className="empty-note">Nothing is on the path. The sealed result meets the published benchmark.</p> : null}
        {steps.length > 0 ? (
          <>
            <div className="seqbar" aria-hidden="true">
              {steps.map((step) => (
                <i key={step.stepId} className={`seq ${step.status === "completed" ? "done" : step.status === "assigned" ? "now" : "todo"}`} title={step.title} />
              ))}
            </div>
            <p className="seqfoot">
              {next ? <>Next: <b>{next.title}</b></> : "Every step on this path is closed."}
            </p>
          </>
        ) : null}
      </article>
      {steps.length > 0 ? (
        <article className="surface-card">
          <header>
            <div>
              <h2>Steps</h2>
              <p className="card-note">Assigned steps can take a file</p>
            </div>
          </header>
          <ul className="gap-list">
            {steps.map((step) => (
              <li key={step.stepId}>
                <b>{step.title}</b>
                <span>{step.provider === "external" ? "External" : "SkillHorizon assessment"} · {step.skillCode} · {stepLabel(step.status)}</span>
                {step.evidenceName ? <span>Evidence {step.evidenceName}</span> : null}
                <span className={step.status === "completed" ? "chip mint" : "chip amber"}>{stepLabel(step.status)}</span>
                {step.status === "assigned" ? (
                  <form
                    onSubmit={(event) => {
                      event.preventDefault();
                      const input = event.currentTarget.elements.namedItem("evidence");
                      const file = input instanceof HTMLInputElement ? input.files?.[0] : undefined;
                      if (!file) return;
                      setPending(true);
                      void file.arrayBuffer()
                        .then((buffer) => addEvidence({
                          stepId: step.stepId,
                          fileName: file.name,
                          contentBase64: encode(new Uint8Array(buffer)),
                        }))
                        .then((nextPath) => {
                          setPath(nextPath);
                          setProblem(null);
                        })
                        .catch((error: unknown) => {
                          setProblem(error instanceof Error ? error.message : "That evidence was not stored.");
                        })
                        .finally(() => setPending(false));
                    }}
                  >
                    <label>
                      Evidence file
                      <input name="evidence" type="file" />
                    </label>
                    <button type="submit" className="draft-save" disabled={pending}>Add evidence</button>
                  </form>
                ) : null}
              </li>
            ))}
          </ul>
        </article>
      ) : null}
      <p className="callout"><i aria-hidden="true">i</i><span><b>Why the level stays put.</b> An upload shows the step was handed in. Your sealed proficiency moves when a sitting is sealed, not when a file is stored.</span></p>
    </section>
  );
}

function stepLabel(status: string) {
  if (status === "completed") return "Closed";
  if (status === "assigned") return "Open";
  return status;
}

export function TeamPaths() {
  const [people, setPeople] = useState<ReportPath[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    try {
      setPeople(await getTeamPaths());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The paths could not be loaded.");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const steps = (people ?? []).flatMap((person) => (person.path?.steps ?? []).map((step) => ({ person, step })));

  return (
    <section className="path" aria-labelledby="validation-title" data-screen="validation">
      <p className="eyebrow">Validation</p>
      <h2 id="validation-title">Report paths</h2>
      <p>A validation records the manager and the reporting line that are in force now.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {people && steps.length === 0 ? <p>No development path is open for this team.</p> : null}
      {steps.length > 0 ? (
        <ul className="gap-list">
          {steps.map(({ person, step }) => (
            <li key={step.stepId}>
              <b>{person.displayName}</b>
              <span>{step.title} · {step.status}{step.evidenceName ? ` · ${step.evidenceName}` : ""}</span>
              {step.status === "assigned" ? (
                <button
                  type="button"
                  className="draft-publish"
                  disabled={pending}
                  onClick={() => {
                    setPending(true);
                    void validateStep({ subjectId: person.employeeId, stepId: step.stepId })
                      .then(() => load())
                      .catch((error: unknown) => {
                        setProblem(error instanceof Error ? error.message : "That validation was refused.");
                      })
                      .finally(() => setPending(false));
                  }}
                >
                  Validate
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}

function encode(bytes: Uint8Array): string {
  let text = "";
  for (const byte of bytes) text += String.fromCharCode(byte);
  return btoa(text);
}
