import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { closeTask, getTask, getTasks, type EmployeeTask } from "./api";
import { PageHead } from "./frame";
import { useTitle } from "./ui";

export function TasksPage() {
  const [tasks, setTasks] = useState<EmployeeTask[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("My tasks");
  const load = useCallback(async () => {
    try {
      setTasks(await getTasks());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The tasks could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  const overdue = tasks?.filter((task) => owed(task.dueOn) === "Overdue").length ?? 0;
  const open = tasks?.length ?? 0;
  return (
    <section data-screen="tasks">
      <PageHead
        eyebrow="Employee · My tasks"
        title="What you owe, and by when"
        lede="Work still assigned on the open path. Handing something in closes the step. It does not change your sealed proficiency."
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      <div className="stat-row cols-2">
        <div className="stat-card">
          <span className="kicker">Open</span>
          <strong>{tasks ? open : "—"}</strong>
          <span className="hint">Assigned steps</span>
        </div>
        <div className="stat-card">
          <span className="kicker">Overdue</span>
          <strong>{tasks ? overdue : "—"}</strong>
          <span className="hint">Due date already passed</span>
        </div>
      </div>
      <article className="surface-card">
        <header>
          <div>
            <h2>Open tasks</h2>
            <p className="card-note">{tasks ? `${open} still assigned` : "Not loaded"}</p>
          </div>
          <Link to="/workspace/path">Open my path</Link>
        </header>
        {tasks && tasks.length === 0 ? <p className="empty-note">Nothing is owed.</p> : null}
        {tasks?.map((task) => {
          const state = owed(task.dueOn);
          return (
            <Link key={task.stepId} className="task-row" to={`/workspace/tasks/${task.stepId}`}>
              <span className="task-kind">{task.skillCode}</span>
              <span className="task-main">
                <b>{task.title}</b>
                <span>Closes the gap on {task.skillCode}</span>
              </span>
              <span className="task-due">
                <b>{task.dueOn}</b>
                <span>due</span>
              </span>
              <span className={state === "Overdue" ? "chip coral" : "chip mint"}>{state}</span>
            </Link>
          );
        })}
      </article>
      <p className="callout"><i aria-hidden="true">i</i><span><b>Handing something in closes the step. It does not move your level.</b> Your sealed proficiency moves when a sitting is sealed.</span></p>
    </section>
  );
}

export function TaskPage() {
  const { stepId } = useParams();
  const [task, setTask] = useState<EmployeeTask | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle(task?.title ?? "Task");
  const load = useCallback(async () => {
    if (!stepId) return;
    try {
      setTask(await getTask(stepId));
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "That task could not be opened.");
    }
  }, [stepId]);
  useEffect(() => { void load(); }, [load]);
  const state = task ? (task.status === "assigned" ? owed(task.dueOn) : "Closed") : null;
  return (
    <section data-screen="task">
      <p className="back-link"><Link to="/workspace/tasks">My tasks</Link></p>
      <PageHead
        eyebrow={task ? `Task · ${task.skillCode}` : "Task"}
        title={task?.title ?? "Task"}
        lede="Hand in what you did. Closing writes the file onto this step and leaves your sealed proficiency where it is."
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {task ? (
        <>
          <div className="meta-row">
            <div><span>Due</span><b>{task.dueOn}</b></div>
            <div><span>Status</span><b>{state}</b></div>
            <div><span>Skill</span><b>{task.skillCode}</b></div>
            <div><span>Evidence</span><b>{task.evidenceName || "None stored"}</b></div>
          </div>
          {task.status === "assigned" ? (
            <form
              className="task-close"
              onSubmit={(event) => {
                event.preventDefault();
                const input = event.currentTarget.elements.namedItem("evidence");
                const file = input instanceof HTMLInputElement ? input.files?.[0] : undefined;
                if (!file || !stepId) return;
                setPending(true);
                void file.arrayBuffer()
                  .then((buffer) => closeTask(stepId, { fileName: file.name, contentBase64: encode(new Uint8Array(buffer)) }))
                  .then((closed) => {
                    setTask(closed);
                    setProblem(null);
                    window.dispatchEvent(new Event("skillhorizon-tasks"));
                  })
                  .catch((error: unknown) => {
                    setProblem(error instanceof Error ? error.message : "That task was not closed.");
                  })
                  .finally(() => setPending(false));
              }}
            >
              <h2>Close it</h2>
              <p>The file is stored on this step. The page does not show the file bytes.</p>
              <label>
                Evidence file
                <input name="evidence" type="file" required />
              </label>
              <button type="submit" className="draft-publish" disabled={pending}>Hand it in and close</button>
            </form>
          ) : (
            <p className="callout"><i aria-hidden="true">✓</i><span><b>Closed.</b> {task.evidenceName ? `Evidence: ${task.evidenceName}. ` : ""}Your sealed proficiency is unchanged.</span></p>
          )}
        </>
      ) : null}
    </section>
  );
}

function owed(dueOn: string) {
  const today = new Date().toISOString().slice(0, 10);
  return dueOn < today ? "Overdue" : "Open";
}

function encode(bytes: Uint8Array): string {
  let text = "";
  for (const byte of bytes) text += String.fromCharCode(byte);
  return btoa(text);
}

function Problem({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <p className="gate-alert" role="alert">
      {text}
      <button type="button" className="team-retry" onClick={onRetry}>Try again</button>
    </p>
  );
}
