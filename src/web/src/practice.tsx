import { useCallback, useEffect, useState } from "react";
import { NavLink, useParams } from "react-router-dom";
import {
  acceptPracticeRequest,
  createCohort,
  decideException,
  getPacks,
  getCatalogueReview,
  getPracticeBriefing,
  getStandards,
  markReviewed,
  sealPack,
  submitPack,
  type AttestationPack,
  type PracticeBriefing,
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

const practiceCode = "data";

const screens = [
  { id: "briefing", label: "Briefing" },
  { id: "requests", label: "Profile requests" },
  { id: "review", label: "Review room" },
  { id: "sign-off", label: "Sign-off" },
];

export function PracticeHome() {
  const { screen } = useParams();
  const current = screens.some((item) => item.id === screen) ? screen! : "briefing";
  return (
    <div className="home">
      <nav className="home-nav" aria-label="Competency lead">
        {screens.map((item) => (
          <NavLink key={item.id} to={`/workspace/${item.id}`} aria-current={current === item.id ? "page" : undefined}>
            {item.label}
          </NavLink>
        ))}
      </nav>
      <div className="home-main">
        {current === "requests" ? <RequestsPage /> : null}
        {current === "review" ? <ReviewPage /> : null}
        {current === "sign-off" ? <SignOffPage /> : null}
        {current === "briefing" ? <BriefingPage /> : null}
      </div>
    </div>
  );
}

function useBriefing() {
  const [briefing, setBriefing] = useState<PracticeBriefing | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const load = useCallback(async () => {
    try {
      setBriefing(await getPracticeBriefing(practiceCode));
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The practice could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return { briefing, problem, setProblem, load };
}

function BriefingPage() {
  const { briefing, problem, setProblem, load } = useBriefing();
  const [memberIds, setMemberIds] = useState("person-single");
  const [countryCode, setCountryCode] = useState("IN");
  const [pending, setPending] = useState(false);
  const shown = briefing?.pack ?? null;
  const pack = shown?.status === "submitted" ? shown : null;
  const snapshot = briefing?.snapshot ?? null;
  const profileId = shown?.rows[0]?.jobProfileId ?? "";
  const aligned = pack?.rows.filter((row) => row.classification === "aligned").length ?? 0;
  const exceptions = pack?.rows.filter((row) => row.classification !== "aligned").length ?? 0;
  return (
    <section data-screen="briefing">
      <p className="eyebrow">Practice stewardship</p>
      <h1>Briefing</h1>
      <p>The open pack, the profile requests in this practice, and the latest readiness snapshot.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <ul className="home-figures">
        <li><b>{pack ? pack.rows.length : 0}</b><span>Profiles in the open pack</span></li>
        <li><b>{aligned}</b><span>Aligned</span></li>
        <li><b>{exceptions}</b><span>Exceptions</span></li>
        <li><b>{briefing?.requests.length ?? 0}</b><span>Profile requests</span></li>
      </ul>
      {pack ? (
        <article>
          <h2>{pack.practiceCode}</h2>
          <p>{pack.status === "sealed" ? "Sealed" : "Submitted"} by {pack.submittedBy}</p>
          <ul className="gap-list">
            {pack.rows.map((row) => (
              <li key={row.rowId}>
                <b>{row.profileName}</b>
                <span>{labels[row.classification] ?? row.classification}{row.exceptionNote ? ` · ${row.exceptionNote}` : ""}</span>
              </li>
            ))}
          </ul>
        </article>
      ) : shown ? <p>The latest pack is sealed.</p> : <p>No pack is open.</p>}
      <h2>Readiness</h2>
      {snapshot ? (
        <>
          <p data-as-of={snapshot.asOf}>As of {snapshot.asOf}</p>
          <p>{snapshot.memberCount} members · {snapshot.measuredCount} measured · {snapshot.gapCount} below the bar · {snapshot.cohortCount} cohorts</p>
          {snapshot.dirty ? <p role="status">This snapshot is stale. The last as-of still stands until the projection rewrites it.</p> : null}
        </>
      ) : <p>No practice snapshot has been written yet.</p>}
      <SentForReview />
      <h2>Cohort</h2>
      <p>Name the people. A cohort is not everyone on the job profile.</p>
      <form
        className="task-close"
        onSubmit={(event) => {
          event.preventDefault();
          setPending(true);
          void createCohort({
            practiceCode,
            countryCode,
            jobProfileId: profileId,
            memberIds: memberIds.split(",").map((id) => id.trim()).filter((id) => id.length > 0),
          })
            .then(() => load())
            .then(() => setProblem(null))
            .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That cohort was refused."))
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

function ReviewPage() {
  const { briefing, problem, setProblem, load } = useBriefing();
  const [pending, setPending] = useState(false);
  const pack = briefing?.pack ?? null;
  const aligned = pack?.rows.filter((row) => row.classification === "aligned") ?? [];
  const waiting = aligned.filter((row) => !row.reviewedAt);
  return (
    <section data-screen="review">
      <p className="eyebrow">Approval pack</p>
      <h1>Review room</h1>
      <p>Aligned profiles can be marked together. An exception stays on its own row.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {!pack ? <p>No pack is open.</p> : null}
      {pack ? (
        <>
          <div className="team-scroll">
            <table className="team-table">
              <thead>
                <tr><th>Profile</th><th>Classification</th><th>Status</th></tr>
              </thead>
              <tbody>
                {pack.rows.map((row) => (
                  <tr key={row.rowId}>
                    <td><strong>{row.profileName}</strong>{row.exceptionNote ? <span className="team-id">{row.exceptionNote}</span> : null}</td>
                    <td>{labels[row.classification] ?? row.classification}</td>
                    <td>{row.classification === "aligned" ? (row.reviewedAt ? "Reviewed" : "Clear") : (row.decision ? row.decision : "Needs a decision")}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {pack.status === "submitted" && waiting.length > 0 ? (
            <p className="draft-actions">
              <button
                type="button"
                className="draft-save"
                disabled={pending}
                onClick={() => {
                  setPending(true);
                  void markReviewed(pack.packId, waiting.map((row) => row.rowId))
                    .then(() => load())
                    .then(() => setProblem(null))
                    .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That batch was refused."))
                    .finally(() => setPending(false));
                }}
              >
                Approve {waiting.length} aligned
              </button>
            </p>
          ) : null}
          {pack.status === "submitted" ? pack.rows.filter((row) => row.classification !== "aligned" && !row.decision).map((row) => (
            <ExceptionDecision
              key={row.rowId}
              profileName={row.profileName}
              note={row.exceptionNote ?? ""}
              pending={pending}
              onSubmit={(decision) => {
                setPending(true);
                void decideException(pack.packId, row.rowId, decision)
                  .then(() => load())
                  .then(() => setProblem(null))
                  .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That decision was refused."))
                  .finally(() => setPending(false));
              }}
            />
          )) : null}
        </>
      ) : null}
    </section>
  );
}

function ExceptionDecision({ profileName, note, pending, onSubmit }: {
  profileName: string;
  note: string;
  pending: boolean;
  onSubmit: (decision: string) => void;
}) {
  const [decision, setDecision] = useState("");
  return (
    <form
      className="task-close"
      onSubmit={(event) => {
        event.preventDefault();
        onSubmit(decision);
      }}
    >
      <h2>{profileName}</h2>
      <p>{note}</p>
      <label>
        Decision
        <input value={decision} onChange={(event) => setDecision(event.target.value)} />
      </label>
      <button type="submit" className="draft-save" disabled={pending || decision.trim().length === 0}>Record decision</button>
    </form>
  );
}

function SignOffPage() {
  const { briefing, problem, setProblem, load } = useBriefing();
  const [pending, setPending] = useState(false);
  const pack = briefing?.pack ?? null;
  const rows = pack?.rows ?? [];
  const aligned = rows.filter((row) => row.classification === "aligned");
  const exceptions = rows.filter((row) => row.classification !== "aligned");
  const alignedReviewed = aligned.filter((row) => row.reviewedAt).length;
  const exceptionsDecided = exceptions.filter((row) => row.decision).length;
  return (
    <section data-screen="sign-off">
      <p className="eyebrow">Practice attestation</p>
      <h1>Sign-off</h1>
      <p>Seal stays the command already built. It is refused while an exception has no recorded decision.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {!pack ? <p>No pack is open.</p> : null}
      {pack ? (
        <>
          <ul className="gap-list">
            <li><b>{alignedReviewed === aligned.length ? "Done" : "Waiting"}</b><span>Aligned profiles reviewed ({alignedReviewed} of {aligned.length})</span></li>
            <li><b>{exceptionsDecided === exceptions.length ? "Done" : "Waiting"}</b><span>Exception decisions recorded ({exceptionsDecided} of {exceptions.length})</span></li>
            <li><b>{pack.status === "sealed" ? "Sealed" : "Open"}</b><span>Baseline for {pack.practiceCode}</span></li>
          </ul>
          {pack.status === "submitted" ? (
            <p className="draft-actions">
              <button
                type="button"
                className="draft-publish"
                disabled={pending}
                onClick={() => {
                  setPending(true);
                  void sealPack(pack.packId)
                    .then(() => load())
                    .then(() => setProblem(null))
                    .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That seal was refused."))
                    .finally(() => setPending(false));
                }}
              >
                Seal the baseline
              </button>
            </p>
          ) : <p>This pack is sealed.</p>}
        </>
      ) : null}
    </section>
  );
}

function RequestsPage() {
  const { briefing, problem, setProblem, load } = useBriefing();
  const [pending, setPending] = useState(false);
  const requests = briefing?.requests ?? [];
  return (
    <section data-screen="practice-requests">
      <p className="eyebrow">Practice demand</p>
      <h1>Profile requests</h1>
      <p>These are the requests for people in this practice. Accepting one does not publish a benchmark.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {requests.length === 0 ? <p>No profile request is waiting.</p> : null}
      <ul className="gap-list">
        {requests.map((request) => (
          <li key={request.requestId}>
            <b>{request.displayName}</b>
            <span>{request.note}</span>
            {request.acceptedAt ? <span>Accepted</span> : (
              <button
                type="button"
                className="draft-save"
                disabled={pending}
                onClick={() => {
                  setPending(true);
                  void acceptPracticeRequest(request.requestId, practiceCode)
                    .then(() => load())
                    .then(() => setProblem(null))
                    .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That request was refused."))
                    .finally(() => setPending(false));
                }}
              >
                Accept
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
