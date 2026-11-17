import { useCallback, useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { PageHead, PersonaFrame, type RailItem } from "./frame";
import {
  acceptPracticeRequest,
  createCohort,
  decideException,
  getPacks,
  getCatalogueReview,
  getPracticeBaseline,
  getPracticeBriefing,
  getPracticeMarket,
  getPracticePicture,
  getPracticeProgression,
  getStandards,
  markReviewed,
  sealPack,
  submitPack,
  type AttestationPack,
  type PracticeBriefing,
  type PracticePicture,
  type Standards,
} from "./api";
import { formatAsOf, useTitle } from "./ui";

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
            .then(() => notePracticeChange())
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

const screens: RailItem[] = [
  { id: "briefing", label: "Briefing", icon: "◫" },
  { id: "requests", label: "Profile requests", icon: "+" },
  { id: "review", label: "Review room", icon: "≡" },
  { id: "sign-off", label: "Sign-off", icon: "✓" },
  { id: "baseline", label: "Baseline", icon: "▦" },
  { id: "progression", label: "Progression", icon: "↗" },
  { id: "market", label: "Market demand", icon: "◎" },
  { id: "readiness", label: "Readiness", icon: "↗" },
];

export function PracticeHome() {
  const { screen } = useParams();
  const current = screens.some((item) => item.id === screen) ? screen! : "briefing";
  const { briefing, load } = useBriefing();
  useEffect(() => {
    const onChange = () => { void load(); };
    window.addEventListener("skillhorizon-practice", onChange);
    return () => window.removeEventListener("skillhorizon-practice", onChange);
  }, [load]);
  const pack = briefing?.pack?.status === "submitted" ? briefing.pack : null;
  const requestCount = (briefing?.requests ?? []).filter((request) => !request.acceptedAt).length;
  const reviewCount = pack
    ? pack.rows.filter((row) => (row.classification === "aligned" ? !row.reviewedAt : !row.decision)).length
    : 0;
  const items = screens.map((item) => ({
    ...item,
    count: item.id === "briefing" ? (pack ? 1 : 0) : item.id === "requests" ? requestCount : item.id === "review" ? reviewCount : undefined,
  }));
  const page = screens.find((item) => item.id === current)?.label ?? "Briefing";
  return (
    <PersonaFrame
      label="Competency lead"
      kicker="Data practice"
      mark="CL"
      page={page}
      items={items}
      currentId={current}
    >
      {current === "requests" ? <RequestsPage /> : null}
      {current === "review" ? <ReviewPage /> : null}
      {current === "sign-off" ? <SignOffPage /> : null}
      {current === "baseline" ? <BaselinePage /> : null}
      {current === "progression" ? <ProgressionPage /> : null}
      {current === "market" ? <MarketPage /> : null}
      {current === "readiness" ? <ReadinessPage /> : null}
      {current === "briefing" ? <BriefingPage /> : null}
    </PersonaFrame>
  );
}

function notePracticeChange() {
  window.dispatchEvent(new Event("skillhorizon-practice"));
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
  useTitle("Briefing");
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
  const openRequests = (briefing?.requests ?? []).filter((request) => !request.acceptedAt).length;
  const stamp = pack ? "Pack open" : shown?.status === "sealed" ? "Sealed" : "No open pack";
  return (
    <section data-screen="briefing">
      <PageHead
        eyebrow="Practice stewardship"
        title="Briefing"
        lede="The open pack, the profile requests in this practice, and the latest readiness snapshot."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <div className="briefing-grid">
        <article className="decision-letter">
          <div className="letter-top">
            <div>
              <p className="eyebrow">Data practice</p>
              <h2>{pack ? `${pack.rows.length} profiles in the open pack` : shown ? "The latest pack is sealed" : "No pack is open"}</h2>
            </div>
            <span className={pack ? "stamp" : "stamp ready"}>{stamp}</span>
          </div>
          <div className="letter-meta">
            <div><small>Aligned</small><b>{aligned}</b></div>
            <div><small>Exceptions</small><b>{exceptions}</b></div>
            <div><small>Requests</small><b>{openRequests}</b></div>
            <div><small>Snapshot</small><b>{snapshot ? formatAsOf(snapshot.asOf) : "None"}</b></div>
          </div>
          {pack ? (
            <ul className="gap-list">
              {pack.rows.map((row) => (
                <li key={row.rowId}>
                  <b>{row.profileName}</b>
                  <span className={row.classification === "aligned" ? "chip mint" : "chip amber"}>{labels[row.classification] ?? row.classification}</span>
                  {row.exceptionNote ? <span>{row.exceptionNote}</span> : null}
                </li>
              ))}
            </ul>
          ) : <p className="empty-note">{shown ? "The latest pack is sealed." : "No pack is open."}</p>}
          <p>{pack ? `Submitted by ${pack.submittedBy}.` : "A sealed pack is not waiting on a decision."}</p>
        </article>
        <aside className="margin">
          <h2>Readiness</h2>
          {snapshot ? (
            <>
              <p data-as-of={snapshot.asOf}>As of {formatAsOf(snapshot.asOf)}</p>
              <div className="margin-stat"><strong>{snapshot.memberCount}</strong><span>Members</span></div>
              <div className="margin-stat"><strong>{snapshot.measuredCount}</strong><span>Measured</span></div>
              <div className="margin-stat"><strong>{snapshot.gapCount}</strong><span>Below the bar</span></div>
              <div className="margin-stat"><strong>{snapshot.cohortCount}</strong><span>Cohorts</span></div>
              {snapshot.dirty ? <p role="status">This snapshot is stale. The last as-of still stands until the projection rewrites it.</p> : null}
            </>
          ) : <p className="empty-note">No practice snapshot has been written yet.</p>}
        </aside>
      </div>
      <div className="flowline">
        <div className={pack ? "flowstep current" : "flowstep"}><small>1</small><b>{pack ? "Pack open" : shown ? "Pack sealed" : "No pack"}</b></div>
        <div className="flowstep"><small>2</small><b>{openRequests === 0 ? "No open requests" : `${openRequests} profile requests`}</b></div>
        <div className="flowstep"><small>3</small><b>Review room</b></div>
        <div className="flowstep"><small>4</small><b>{snapshot ? "Snapshot stored" : "Snapshot missing"}</b></div>
      </div>
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
            .then(() => notePracticeChange())
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
  useTitle("Review room");
  const { briefing, problem, setProblem, load } = useBriefing();
  const [pending, setPending] = useState(false);
  const pack = briefing?.pack ?? null;
  const aligned = pack?.rows.filter((row) => row.classification === "aligned") ?? [];
  const waiting = aligned.filter((row) => !row.reviewedAt);
  const settled = pack ? pack.rows.filter((row) => (row.classification === "aligned" ? Boolean(row.reviewedAt) : Boolean(row.decision))).length : 0;
  return (
    <section data-screen="review">
      <div className="page-head">
        <div>
          <p className="eyebrow">Approval pack</p>
          <h1>Review room</h1>
          <p className="lede">Aligned profiles can be marked together. An exception stays on its own row, and a batch review does not include it.</p>
        </div>
        {pack ? (
          <div className="seal-badge">
            <strong>{settled}/{pack.rows.length}</strong>
            <span>Rows settled</span>
          </div>
        ) : null}
      </div>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {!pack ? <p className="empty-note">No pack is open.</p> : null}
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
                    <td><span className={row.classification === "aligned" ? "chip mint" : "chip amber"}>{labels[row.classification] ?? row.classification}</span></td>
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
            .then(() => notePracticeChange())
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
            .then(() => notePracticeChange())
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
  useTitle("Sign-off");
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
      <PageHead
        eyebrow="Practice attestation"
        title="Sign-off"
        lede="Seal stays the command already built. It is refused while an exception has no recorded decision."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {!pack ? <p className="empty-note">No pack is open.</p> : null}
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
            .then(() => notePracticeChange())
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
  useTitle("Profile requests");
  const { briefing, problem, setProblem, load } = useBriefing();
  const [pending, setPending] = useState(false);
  const requests = briefing?.requests ?? [];
  return (
    <section data-screen="practice-requests">
      <PageHead
        eyebrow="Practice demand"
        title="Profile requests"
        lede="These are the requests for people in this practice. Accepting one does not publish a benchmark."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {requests.length === 0 ? <p className="empty-note">No profile request is waiting.</p> : null}
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
            .then(() => notePracticeChange())
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

function BaselinePage() {
  useTitle("Baseline");
  const picture = usePicture(getPracticeBaseline);
  const heatmap = picture.value;
  return (
    <section data-screen="baseline">
      <PageHead
        eyebrow="Attested baseline"
        title="Baseline"
        lede="Each cell is the attested target for a skill at a job level. The page reads the stored snapshot."
      />
      {picture.problem ? <p className="gate-alert" role="alert">{picture.problem}</p> : null}
      {heatmap?.asOf ? <p data-as-of={heatmap.asOf}>As of {formatAsOf(heatmap.asOf)}</p> : <p>No baseline snapshot has been written yet.</p>}
      {heatmap && heatmap.cells.length === 0 && heatmap.asOf ? <p>No attested targets are in this snapshot.</p> : null}
      {heatmap && heatmap.cells.length > 0 ? (
        <table className="team-table">
          <thead>
            <tr><th>Skill</th><th>Level</th><th>Attested target</th></tr>
          </thead>
          <tbody>
            {heatmap.cells.map((cell) => (
              <tr key={`${cell.skillCode}-${cell.levelCode}`}>
                <td><strong>{cell.skillName}</strong></td>
                <td>{cell.levelName}</td>
                <td>{cell.proficiencyName}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </section>
  );
}

function ProgressionPage() {
  useTitle("Progression");
  const picture = usePicture(getPracticeProgression);
  const progression = picture.value;
  return (
    <section data-screen="progression">
      <PageHead
        eyebrow="Paths in this practice"
        title="Progression"
        lede="Path and step counts for people in the practice cohorts. The page reads the stored snapshot."
      />
      {picture.problem ? <p className="gate-alert" role="alert">{picture.problem}</p> : null}
      {progression?.asOf ? <p data-as-of={progression.asOf}>As of {formatAsOf(progression.asOf)}</p> : <p>No progression snapshot has been written yet.</p>}
      {progression?.asOf ? (
        <ul className="home-figures">
          <li><b>{progression.pathCount}</b><span>Paths</span></li>
          <li><b>{progression.stepCount}</b><span>Steps</span></li>
          <li><b>{progression.openStepCount}</b><span>Open steps</span></li>
        </ul>
      ) : null}
    </section>
  );
}

function MarketPage() {
  useTitle("Market demand");
  const picture = usePicture(getPracticeMarket);
  const demand = picture.value ?? null;
  return (
    <section data-screen="market">
      <PageHead
        eyebrow="Published demand"
        title="Market demand"
        lede="The published note and counts for this practice. A draft stays in the authoring zone until it is published."
      />
      {picture.problem ? <p className="gate-alert" role="alert">{picture.problem}</p> : null}
      {demand ? (
        <>
          {demand.publishedAt ? <p data-as-of={demand.publishedAt}>Published {formatAsOf(demand.publishedAt)}</p> : null}
          <p data-market-note={demand.note}>{demand.note}</p>
          <ul className="home-figures">
            <li><b>{demand.postingCount}</b><span>Postings</span></li>
            <li><b>{demand.roleCount}</b><span>Roles</span></li>
          </ul>
        </>
      ) : picture.ready && !picture.problem ? <p>No market demand has been published.</p> : null}
    </section>
  );
}

function ReadinessPage() {
  useTitle("Readiness");
  const picture = usePicture(getPracticePicture);
  const readiness = picture.value;
  const snapshot = readiness?.snapshot ?? null;
  return (
    <section data-screen="readiness">
      <PageHead
        eyebrow="This zone"
        title="Readiness"
        lede="Talent-group and country cuts are snapshots held in this zone. The cross-zone total stays the sum of each zone's stored counts."
      />
      {picture.problem ? <p className="gate-alert" role="alert">{picture.problem}</p> : null}
      {snapshot ? (
        <>
          <p data-as-of={snapshot.asOf}>As of {formatAsOf(snapshot.asOf)}</p>
          <ul className="home-figures">
            <li><b>{snapshot.memberCount}</b><span>Members in this zone</span></li>
            <li><b>{snapshot.measuredCount}</b><span>Measured</span></li>
            <li><b>{snapshot.gapCount}</b><span>Below the bar</span></li>
            <li><b>{snapshot.cohortCount}</b><span>Cohorts</span></li>
          </ul>
        </>
      ) : <p>No practice snapshot has been written yet.</p>}
      <CutList title="Country" cuts={readiness?.countries ?? []} />
      <CutList title="Talent group" cuts={readiness?.talentGroups ?? []} />
    </section>
  );
}

function CutList({ title, cuts }: { title: string; cuts: PracticePicture["countries"] }) {
  return (
    <>
      <h2>{title}</h2>
      {cuts.length === 0 ? <p>No {title.toLowerCase()} cut is in this snapshot.</p> : (
        <ul className="gap-list">
          {cuts.map((cut) => (
            <li key={cut.key}>
              <b>{cut.key}</b>
              <span>{cut.memberCount} members · {cut.measuredCount} measured · {cut.gapCount} below the bar</span>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}

function usePicture<T>(load: (practice: string) => Promise<T>) {
  const [value, setValue] = useState<T | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [ready, setReady] = useState(false);
  useEffect(() => {
    let live = true;
    load(practiceCode)
      .then((next) => {
        if (!live) return;
        setValue(next);
        setReady(true);
      })
      .catch((error: unknown) => {
        if (!live) return;
        setProblem(error instanceof Error ? error.message : "The snapshot could not be read.");
        setReady(true);
      });
    return () => {
      live = false;
    };
  }, [load]);
  return { value, problem, ready };
}
