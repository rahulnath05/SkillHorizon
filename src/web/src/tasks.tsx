import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { closeTask, getTask, getTasks, type EmployeeTask } from "./api";
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
  return (
    <section data-screen="tasks">
      <p className="eyebrow">Employee</p>
      <h1>My tasks</h1>
      <p>Work you still owe on the open path. Handing something in closes the step. It does not change your sealed proficiency.</p>
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {tasks ? (
        <ul className="home-figures">
          <li><b>{tasks.length}</b><span>Open</span></li>
          <li><b>{overdue}</b><span>Overdue</span></li>
        </ul>
      ) : null}
      {tasks && tasks.length === 0 ? <p>Nothing is owed.</p> : null}
      {tasks && tasks.length > 0 ? (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr><th>Task</th><th>Due</th><th>Status</th></tr>
            </thead>
            <tbody>
              {tasks.map((task) => (
                <tr key={task.stepId}>
                  <td><Link to={`/workspace/tasks/${task.stepId}`}>{task.title}</Link></td>
                  <td>{task.dueOn}</td>
                  <td>{owed(task.dueOn)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}

export function TaskPage() {
  const { stepId } = useParams();
  const [task, setTask] = useState<EmployeeTask | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("Task");
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
  return (
    <section data-screen="task">
      <p className="eyebrow">Employee</p>
      <p><Link to="/workspace/tasks">My tasks</Link></p>
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {task ? (
        <>
          <h1>{task.title}</h1>
          <p>Hand in what you did. Closing writes the file onto this step and leaves your sealed proficiency where it is.</p>
          <ul className="home-figures">
            <li><b>{task.dueOn}</b><span>Due</span></li>
            <li><b>{task.status === "assigned" ? owed(task.dueOn) : "Closed"}</b><span>Status</span></li>
          </ul>
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
                  })
                  .catch((error: unknown) => {
                    setProblem(error instanceof Error ? error.message : "That task was not closed.");
                  })
                  .finally(() => setPending(false));
              }}
            >
              <h2>Close it</h2>
              <input name="evidence" type="file" />
              <button type="submit" className="draft-save" disabled={pending}>Hand it in and close</button>
            </form>
          ) : (
            <p>This step is closed.{task.evidenceName ? ` Evidence: ${task.evidenceName}.` : ""} Your sealed proficiency is unchanged.</p>
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
