import { useCallback, useEffect, useState } from "react";
import {
  createCohort,
  getPacks,
  getCatalogueReview,
  getPracticeReadiness,
  getStandards,
  sealPack,
  submitPack,
  type AttestationPack,
  type PracticeSnapshot,
  type Standards,
} from "./api";

function SentForReview() {
  const [drafts, setDrafts] = useState<Awaited<ReturnType<typeof getCatalogueReview>>>([]);
  const [problem, setProblem] = useState<string | null>(null);
  const load = useCallback(async () => {
    try {
      setDrafts(await getCatalogueReview());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The review queue could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return (
    <section data-screen="lead-review">
      <h2>Sent for review</h2>
      <p>Only drafts L&D has sent are waiting. An unsent draft is absent.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {drafts.length === 0 ? <p>No draft is waiting.</p> : null}
      <ul className="gap-list">
        {drafts.map((draft) => (
          <li key={draft.benchmarkVersionId}><b>{draft.profileName}</b><span>Draft version {draft.versionNumber}</span></li>
        ))}
      </ul>
    </section>
  );
}

const labels: Record<string, string> = {
  aligned: "Aligned",
  tightening: "Tightening",
  new_bar: "New bar",
};

export function PackSubmission() {
  const [standards, setStandards] = useState<Standards | null>(null);
  const [packs, setPacks] = useState<AttestationPack[]>([]);
  const [practiceCode, setPracticeCode] = useState("data");
  const [profileId, setProfileId] = useState("");
  const [classification, setClassification] = useState("aligned");
  const [exceptionNote, setExceptionNote] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    const [nextStandards, nextPacks] = await Promise.all([getStandards(), getPacks()]);
    setStandards(nextStandards);
    setPacks(nextPacks);
    const first = nextStandards.frameworks.flatMap((framework) => framework.profiles)[0];
    setProfileId((current) => current || first?.profileId || "");
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => {
      setProblem(error instanceof Error ? error.message : "The packs could not be loaded.");
    });
  }, [load]);

  const profiles = standards?.frameworks.flatMap((framework) => framework.profiles) ?? [];

  return (
    <section className="path" aria-labelledby="pack-title" data-screen="pack">
      <p className="eyebrow">Approval pack</p>
      <h2 id="pack-title">Submit a pack</h2>
      <p>Each row names a profile as aligned, tightening, or a new bar. An exception stays on the row.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <form
        className="path"
        onSubmit={(event) => {
          event.preventDefault();
          setPending(true);
          void submitPack({
            practiceCode,
            rows: [{
              jobProfileId: profileId,
              classification,
              exceptionNote: classification === "aligned" ? undefined : exceptionNote,
            }],
          })
            .then(() => load())
            .then(() => setProblem(null))
            .catch((error: unknown) => {
              setProblem(error instanceof Error ? error.message : "That pack was not submitted.");
            })
            .finally(() => setPending(false));
        }}
      >
        <label>
          Practice
          <input value={practiceCode} onChange={(event) => setPracticeCode(event.target.value)} />
        </label>
        <label>
          Profile
          <select value={profileId} onChange={(event) => setProfileId(event.target.value)}>
            {profiles.map((profile) => (
              <option key={profile.profileId} value={profile.profileId}>{profile.name}</option>
            ))}
          </select>
        </label>
        <label>
          Classification
          <select value={classification} onChange={(event) => setClassification(event.target.value)}>
            <option value="aligned">Aligned</option>
            <option value="tightening">Tightening</option>
            <option value="new_bar">New bar</option>
          </select>
        </label>
        {classification !== "aligned" ? (
          <label>
            Exception
            <input value={exceptionNote} onChange={(event) => setExceptionNote(event.target.value)} />
          </label>
        ) : null}
        <button type="submit" className="draft-save" disabled={pending || !profileId}>Submit pack</button>
      </form>
      <ul className="gap-list">
        {packs.map((pack) => (
          <li key={pack.packId}>
            <b>{pack.practiceCode}</b>
            <span>{pack.status}</span>
            <span>{pack.rows.map((row) => `${row.profileName} · ${labels[row.classification] ?? row.classification}`).join(", ")}</span>
          </li>
        ))}
      </ul>
    </section>
  );
}

export function PracticePanel() {
  const practice = "data";
  const [packs, setPacks] = useState<AttestationPack[]>([]);
  const [snapshot, setSnapshot] = useState<PracticeSnapshot | null>(null);
  const [memberIds, setMemberIds] = useState("person-single");
  const [countryCode, setCountryCode] = useState("IN");
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    const [nextPacks, nextSnapshot] = await Promise.all([getPacks(practice), getPracticeReadiness(practice)]);
    setPacks(nextPacks);
    setSnapshot(nextSnapshot);
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => {
      setProblem(error instanceof Error ? error.message : "The practice could not be loaded.");
    });
  }, [load]);

  const profileId = packs.flatMap((pack) => pack.rows)[0]?.jobProfileId ?? "";

  return (
    <section className="path" aria-labelledby="practice-title" data-screen="practice">
      <p className="eyebrow">Practice stewardship</p>
      <h1 id="practice-title">Data</h1>
      <SentForReview />
      <p>A seal locks the published versions in this pack. The readiness figures are the stored snapshot.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <ul className="gap-list">
        {packs.map((pack) => (
          <li key={pack.packId}>
            <b>{pack.status === "sealed" ? "Sealed" : "Submitted"}</b>
            {pack.rows.map((row) => (
              <span key={row.rowId}>
                {row.profileName} · {labels[row.classification] ?? row.classification}
                {row.exceptionNote ? ` · ${row.exceptionNote}` : ""}
              </span>
            ))}
            {pack.status === "submitted" ? (
              <button
                type="button"
                className="draft-publish"
                disabled={pending}
                onClick={() => {
                  setPending(true);
                  void sealPack(pack.packId)
                    .then(() => load())
                    .then(() => setProblem(null))
                    .catch((error: unknown) => {
                      setProblem(error instanceof Error ? error.message : "That seal was refused.");
                    })
                    .finally(() => setPending(false));
                }}
              >
                Seal the baseline
              </button>
            ) : null}
          </li>
        ))}
      </ul>
      <h2>Readiness</h2>
      {snapshot ? (
        <>
          <p data-as-of={snapshot.asOf}>As of {new Date(snapshot.asOf).toLocaleString()}</p>
          <p>{snapshot.memberCount} members · {snapshot.measuredCount} measured · {snapshot.gapCount} below the bar · {snapshot.cohortCount} cohorts</p>
          {snapshot.dirty ? <p role="status">This snapshot is stale. The last as-of still stands until the projection rewrites it.</p> : null}
        </>
      ) : (
        <p>No practice snapshot has been written yet.</p>
      )}
      <h2>Cohort</h2>
      <p>Name the people. A cohort is not everyone on the job profile.</p>
      <form
        className="path"
        onSubmit={(event) => {
          event.preventDefault();
          setPending(true);
          void createCohort({
            practiceCode: practice,
            countryCode,
            jobProfileId: profileId,
            memberIds: memberIds.split(",").map((id) => id.trim()).filter((id) => id.length > 0),
          })
            .then(() => load())
            .then(() => setProblem(null))
            .catch((error: unknown) => {
              setProblem(error instanceof Error ? error.message : "That cohort was refused.");
            })
            .finally(() => setPending(false));
        }}
      >
        <label>
          Country
          <input value={countryCode} onChange={(event) => setCountryCode(event.target.value)} />
        </label>
        <label>
          Members
          <input value={memberIds} onChange={(event) => setMemberIds(event.target.value)} />
        </label>
        <button type="submit" className="draft-save" disabled={pending || !profileId}>Create cohort</button>
      </form>
    </section>
  );
}
