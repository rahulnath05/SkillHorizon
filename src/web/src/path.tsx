import { useCallback, useEffect, useState } from "react";
import { addEvidence, getTeamPaths, openPath, validateStep, type DevelopmentPath, type ReportPath } from "./api";

export function DevelopmentPathPanel() {
  const [path, setPath] = useState<DevelopmentPath | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

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

  return (
    <section className="path" aria-labelledby="path-title" data-screen="path">
      <p className="eyebrow">Development path</p>
      <h2 id="path-title">Your path</h2>
      <p>Each step closes one gap against the published benchmark. A step names its provider.</p>
      {problem ? (
        <p className="gate-alert" role="alert">
          {problem}
          <button type="button" className="team-retry" onClick={() => void load()}>Try again</button>
        </p>
      ) : null}
      {path && path.steps.length === 0 ? <p>Nothing is on the path. The sealed result meets the published benchmark.</p> : null}
      {path && path.steps.length > 0 ? (
        <ul className="gap-list">
          {path.steps.map((step) => (
            <li key={step.stepId}>
              <b>{step.title}</b>
              <span>{step.provider === "external" ? "External" : "SkillHorizon assessment"} · {step.status}</span>
              {step.evidenceName ? <span>Evidence {step.evidenceName}</span> : null}
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
                      .then((next) => {
                        setPath(next);
                        setProblem(null);
                      })
                      .catch((error: unknown) => {
                        setProblem(error instanceof Error ? error.message : "That evidence was not stored.");
                      })
                      .finally(() => setPending(false));
                  }}
                >
                  <input name="evidence" type="file" />
                  <button type="submit" className="draft-save" disabled={pending}>Add evidence</button>
                </form>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
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
