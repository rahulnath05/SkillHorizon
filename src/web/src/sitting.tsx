import { useCallback, useEffect, useState } from "react";
import {
  answerSitting,
  getAssessment,
  sealSitting,
  startSitting,
  type AssessmentSummary,
  type OpenSitting,
  type SkillGap,
} from "./api";
import { useTitle } from "./ui";

export function Sitting() {
  useTitle("Your assessment");
  const [summary, setSummary] = useState<AssessmentSummary | null>(null);
  const [open, setOpen] = useState<OpenSitting | null>(null);
  const [rank, setRank] = useState<number | null>(null);
  const [gap, setGap] = useState<SkillGap[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    try {
      const next = await getAssessment();
      setSummary(next);
      if (next.gap.length > 0) setGap(next.gap);
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The assessment could not be loaded.");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const shownGap = gap ?? (summary && summary.gap.length > 0 ? summary.gap : null);

  return (
    <section className="sitting" aria-labelledby="sitting-title" data-screen="sitting">
      <p className="eyebrow">Employee</p>
      <h1 id="sitting-title">Your assessment</h1>
      <p>Answer the items for the benchmark version this wave admitted. The sealed result records a proficiency for each skill.</p>
      {problem ? (
        <p className="gate-alert" role="alert">
          {problem}
          <button type="button" className="team-retry" onClick={() => void load()}>Try again</button>
        </p>
      ) : null}
      {shownGap ? <GapList gap={shownGap} /> : null}
      {!shownGap && summary && !summary.wave ? <p>No assessment wave is open for you.</p> : null}
      {!shownGap && summary?.wave && !open ? (
        <button
          type="button"
          className="draft-publish"
          disabled={pending}
          onClick={() => {
            setPending(true);
            void startSitting(summary.wave!.waveId)
              .then((next) => {
                setOpen(next);
                setRank(null);
                setProblem(null);
              })
              .catch((error: unknown) => {
                setProblem(error instanceof Error ? error.message : "The sitting could not be opened.");
              })
              .finally(() => setPending(false));
          }}
        >
          Start assessment
        </button>
      ) : null}
      {!shownGap && open?.item ? (
        <form
          onSubmit={(event) => {
            event.preventDefault();
            if (rank === null) return;
            setPending(true);
            void answerSitting({
              token: open.token,
              sittingId: open.sittingId,
              itemId: open.item!.itemId,
              rank,
            })
              .then((next) => {
                setOpen(next);
                setRank(null);
                setProblem(null);
              })
              .catch((error: unknown) => {
                setProblem(error instanceof Error ? error.message : "That response was not saved.");
              })
              .finally(() => setPending(false));
          }}
        >
          <p className="sitting-prompt">{open.item.prompt}</p>
          <fieldset className="sitting-choices">
            <legend className="sr">Choose a response</legend>
            {open.item.choices.map((choice) => (
              <label key={choice.rank}>
                <input
                  type="radio"
                  name="response"
                  checked={rank === choice.rank}
                  onChange={() => setRank(choice.rank)}
                />
                <span>{choice.label}</span>
              </label>
            ))}
          </fieldset>
          <button type="submit" className="draft-publish" disabled={pending || rank === null}>Save response</button>
        </form>
      ) : null}
      {!shownGap && open?.readyToSeal ? (
        <button
          type="button"
          className="draft-publish"
          disabled={pending}
          onClick={() => {
            setPending(true);
            void sealSitting({ token: open.token, sittingId: open.sittingId })
              .then((next) => {
                setGap(next);
                setOpen(null);
                setProblem(null);
              })
              .catch((error: unknown) => {
                setProblem(error instanceof Error ? error.message : "The result could not be sealed.");
              })
              .finally(() => setPending(false));
          }}
        >
          Seal result
        </button>
      ) : null}
    </section>
  );
}

function GapList({ gap }: { gap: SkillGap[] }) {
  return (
    <div>
      <h2>Gap</h2>
      <ul className="gap-list">
        {gap.map((skill) => (
          <li key={skill.skillName}>
            <b>{skill.skillName}</b>
            <span>{skill.currentName || "Not measured"} · target {skill.targetName}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
