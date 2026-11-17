import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { PageHead, PersonaFrame, type RailItem } from "./frame";
import {
  acceptPracticeRequest,
  decideException,
  filePracticeRequest,
  getCatalogueTaxonomy,
  getPacks,
  getPracticeBriefing,
  getPracticeProfile,
  getPracticeProgression,
  getStandards,
  markReviewed,
  sealPack,
  submitPack,
  useShell,
  type AttestationPack,
  type CatalogueTaxonomy,
  type PracticeBriefing,
  type ProfileLens,
  type Standards,
} from "./api";
import { formatAsOf, useTitle } from "./ui";
import { initials } from "./workspaces";

function greeting(name: string) {
  const hour = new Date().getHours();
  const hello = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
  return `${hello}, ${name}.`;
}

function profileDone(row: { classification: string; reviewedAt?: string | null; decision?: string | null }) {
  return row.classification === "aligned" ? Boolean(row.reviewedAt) : Boolean(row.decision);
}

function practiceLabel(code: string) {
  return code ? `${code.charAt(0).toUpperCase()}${code.slice(1)}` : "this practice";
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
  const { shell } = useShell();
  const name = shell?.displayName?.trim() || "";
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
  const screenLabel = screens.find((item) => item.id === current)?.label ?? "Briefing";
  const page = current === "briefing" ? "Morning briefing" : current === "requests" ? "Profile request" : current === "review" ? "Approval pack · Review room" : current === "sign-off" ? "Final seal" : screenLabel;
  return (
    <PersonaFrame
      label="Competency Lead"
      kicker={name ? `${name} · Data` : "Data"}
      mark={name ? initials(name) : "CL"}
      crumb="Data"
      navLabel="Your practice"
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
  const { shell } = useShell();
  const name = shell?.displayName?.trim() || "";
  useTitle(name ? greeting(name) : "Briefing");
  const { briefing, problem } = useBriefing();
  const shown = briefing?.pack ?? null;
  const pack = shown?.status === "submitted" ? shown : null;
  const snapshot = briefing?.snapshot ?? null;
  const rows = shown?.rows ?? [];
  const aligned = rows.filter((row) => row.classification === "aligned").length;
  const exceptions = rows.filter((row) => row.classification !== "aligned").length;
  const openRequests = (briefing?.requests ?? []).filter((request) => !request.acceptedAt).length;
  const practice = practiceLabel(shown?.practiceCode || practiceCode);
  const stamp = pack ? "Needs your judgment" : shown?.status === "sealed" ? "Sealed" : "No open pack";
  const done = rows.filter(profileDone).length;
  const remaining = rows.length - done;
  return (
    <section data-screen="briefing">
      <PageHead
        eyebrow={pack ? "Practice stewardship · Decision waiting" : "Practice stewardship"}
        title={name ? greeting(name) : "Briefing"}
        lede={pack
          ? "One decision is waiting. L&D has prepared the new market baseline; your job is to attest the bar for the practice, not rebuild their work."
          : "The open pack, the profile requests in this practice, and the latest readiness snapshot."}
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <div className="briefing-grid">
        <article className="decision-letter">
          <div className="letter-top">
            <div>
              <p className="eyebrow">Approval pack</p>
              <h2>{pack ? `Set the next market bar for ${practice}` : shown ? "The latest pack is sealed" : "No pack is open"}</h2>
            </div>
            <span className={pack ? "stamp" : "stamp ready"}>{stamp}</span>
          </div>
          {shown ? (
            <div className="verdict">
              <p><strong>{done} done.</strong> {remaining} remaining.</p>
            </div>
          ) : null}
          <div className="letter-meta">
            <div className="meta-item"><small>Prepared by</small><b>{shown?.submittedBy || "—"}</b></div>
            <div className="meta-item"><small>Requests</small><b>{openRequests} open</b></div>
            <div className="meta-item"><small>Snapshot</small><b>{snapshot ? formatAsOf(snapshot.asOf) : "None"}</b></div>
            <div className="meta-item"><small>Cohorts</small><b>{snapshot ? snapshot.cohortCount : "None"}</b></div>
          </div>
          {shown ? (
            <div className="letter-actions">
              <Link className="fill-btn" to="/workspace/review">Enter the review room <span aria-hidden="true">→</span></Link>
            </div>
          ) : null}
        </article>
        <aside className="margin">
          <h3>At a glance</h3>
          <div className="margin-stat"><strong>{rows.length}</strong><span>job profiles in one attestation pack</span></div>
          <div className="margin-stat"><strong>{aligned}</strong><span>aligned with the prior approved bar</span></div>
          <div className="margin-stat"><strong>{exceptions}</strong><span>material changes requiring judgment</span></div>
          <p className="source-note" data-as-of={snapshot?.asOf ?? undefined}>
            {snapshot
              ? `As of ${formatAsOf(snapshot.asOf)}. ${snapshot.memberCount} members, ${snapshot.measuredCount} measured, ${snapshot.gapCount} below the bar.`
              : "No practice snapshot has been written yet."}
            {snapshot?.dirty ? " This snapshot is stale. The last as-of still stands until the projection rewrites it." : ""}
          </p>
        </aside>
      </div>
      <div className="flowline" aria-label="Approval journey">
        <div className={pack ? "flowstep current" : "flowstep"}><small>01 · Arrive</small><b>Understand the ask</b></div>
        <div className="flowstep"><small>02 · Clear</small><b>{aligned === 1 ? "Move 1 together" : `Move ${aligned} together`}</b></div>
        <div className="flowstep"><small>03 · Judge</small><b>{exceptions === 1 ? "Resolve 1 shift" : `Resolve ${exceptions} shifts`}</b></div>
        <div className="flowstep"><small>04 · Seal</small><b>Set the practice bar</b></div>
        <div className={shown?.status === "sealed" ? "flowstep current" : "flowstep"}><small>05 · Steer</small><b>Read market readiness</b></div>
      </div>
    </section>
  );
}

function ReviewPage() {
  useTitle("Review room");
  const { briefing, problem, setProblem, load } = useBriefing();
  const [pending, setPending] = useState(false);
  const [filter, setFilter] = useState<"all" | "done" | "remaining">("all");
  const [picked, setPicked] = useState<string[]>([]);
  const [openId, setOpenId] = useState<string | null>(null);
  const [composer, setComposer] = useState<null | "counter" | "note">(null);
  const [composerText, setComposerText] = useState("");
  const [drawerNote, setDrawerNote] = useState("");
  const [lens, setLens] = useState<ProfileLens | null>(null);
  const [lenses, setLenses] = useState<Record<string, ProfileLens>>({});
  const [lensProblem, setLensProblem] = useState<string | null>(null);
  const pack = briefing?.pack ?? null;
  const rows = pack?.rows ?? [];
  const aligned = rows.filter((row) => row.classification === "aligned");
  const exceptions = rows.filter((row) => row.classification !== "aligned");
  const waiting = aligned.filter((row) => !row.reviewedAt);
  const settled = rows.filter(profileDone).length;
  const visible = rows.filter((row) => filter === "all" || (filter === "done" ? profileDone(row) : !profileDone(row)));
  const open = rows.find((row) => row.rowId === openId) ?? null;
  const openProfileId = open?.jobProfileId ?? "";
  const closeDrawer = () => {
    setComposer(null);
    setComposerText("");
    setDrawerNote("");
    setOpenId(null);
  };
  const showAfter = waiting.length === 0 && aligned.some((row) => row.reviewedAt) && exceptions.some((row) => !row.decision);
  const profileKey = rows.map((row) => row.jobProfileId).join(",");
  useEffect(() => {
    const ids = profileKey.split(",").filter((id) => id.length > 0);
    if (ids.length === 0) return;
    let live = true;
    Promise.all(ids.map((id) => getPracticeProfile(practiceCode, id).then((item) => [id, item] as const).catch(() => null)))
      .then((pairs) => {
        if (!live) return;
        const next: Record<string, ProfileLens> = {};
        for (const pair of pairs) if (pair) next[pair[0]] = pair[1];
        setLenses(next);
      });
    return () => { live = false; };
  }, [profileKey]);
  useEffect(() => {
    if (!openProfileId) {
      setLens(null);
      return;
    }
    const known = lenses[openProfileId];
    if (known) {
      setLens(known);
      setLensProblem(null);
      return;
    }
    let live = true;
    setLensProblem(null);
    getPracticeProfile(practiceCode, openProfileId)
      .then((next) => { if (live) setLens(next); })
      .catch((error: unknown) => { if (live) setLensProblem(error instanceof Error ? error.message : "The profile could not be read."); });
    return () => { live = false; };
  }, [openProfileId, lenses]);
  return (
    <section data-screen="review">
      <div className="review-head">
        <div>
          <p className="eyebrow">Approval pack · Review room</p>
          <h1>See the whole practice.<br />Open only what matters.</h1>
          <p className="lede">Every profile stays visible as part of the same system. Scan the portfolio, move aligned profiles as a batch, and open a row when an exception needs a decision.</p>
        </div>
        {pack ? (
          <div className="seal-badge">
            <strong>{settled} / {rows.length}</strong>
            <span>profiles attested</span>
          </div>
        ) : null}
      </div>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {!pack ? <p className="empty-note">No pack is open.</p> : null}
      {pack ? (
        <>
          <div className="review-control">
            <div className="filters" role="group" aria-label="Profile status">
              <button type="button" aria-pressed={filter === "all"} onClick={() => setFilter("all")}>All {rows.length}</button>
              <button type="button" aria-pressed={filter === "done"} onClick={() => setFilter("done")}>Done {settled}</button>
              <button type="button" aria-pressed={filter === "remaining"} onClick={() => setFilter("remaining")}>Remaining {rows.length - settled}</button>
            </div>
            <span className="control-note">Proposed anchors sit on the row. Open it for the brief and the full list.</span>
            <Link className="outline-btn" to="/workspace/requests">+ New market profile</Link>
            {pack.status === "submitted" ? (
              <button
                type="button"
                className="fill-btn"
                disabled={pending || picked.length === 0}
                onClick={() => {
                  const chosen = rows.filter((row) => picked.includes(row.rowId));
                  const approvable = chosen.filter((row) => row.classification === "aligned" && !row.reviewedAt);
                  if (approvable.length === 0) {
                    setProblem("Approve applies to a remaining aligned profile. An exception is decided on its row.");
                    return;
                  }
                  setPending(true);
                  void markReviewed(pack.packId, approvable.map((row) => row.rowId))
                    .then(() => load())
                    .then(() => notePracticeChange())
                    .then(() => { setPicked([]); setProblem(null); })
                    .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That approval was refused."))
                    .finally(() => setPending(false));
                }}
              >
                Approve
              </button>
            ) : null}
          </div>
          <div className="pack">
            <div className="pack-columns">
              <span className="sr-only">Select</span><span>Profile</span><span>Decision anchors · proposed benchmark</span><span>Market signal</span><span>Status</span>
            </div>
            {[...visible.reduce((groups, row) => {
              const band = lenses[row.jobProfileId]?.band || "Practice";
              const list = groups.get(band) ?? [];
              list.push(row);
              groups.set(band, list);
              return groups;
            }, new Map<string, typeof visible>()).entries()].map(([band, bandRows]) => (
              <div className="band" key={band}>
                <div className="band-head"><h3>{band}</h3><span>{bandRows.length} in this band</span></div>
                {bandRows.map((row, index) => {
                  const detail = lenses[row.jobProfileId];
                  const exception = row.classification !== "aligned";
                  const signed = exception ? Boolean(row.decision) : Boolean(row.reviewedAt);
                  const preview = (detail?.targets ?? []).slice(0, 3);
                  const extra = (detail?.targets.length ?? 0) - preview.length;
                  const family = [detail?.familyName, detail?.band].filter((item) => item && item.length > 0).join(" · ") || row.profileCode;
                  const checked = picked.includes(row.rowId);
                  return (
                    <div
                      key={row.rowId}
                      className={`profile-row${exception ? " exception" : ""}${signed ? " signed" : ""}`}
                    >
                      <label className="row-check">
                        <input
                          type="checkbox"
                          checked={checked}
                          aria-label={`Select ${row.profileName}`}
                          onChange={() => setPicked((current) => current.includes(row.rowId) ? current.filter((id) => id !== row.rowId) : [...current, row.rowId])}
                        />
                      </label>
                      <button
                        type="button"
                        className="profile-open"
                        onClick={() => { setComposer(null); setComposerText(""); setDrawerNote(""); setOpenId(row.rowId); }}
                      >
                      <span className="profile-main">
                        <span className="profile-index">{signed ? "✓" : String(index + 1).padStart(2, "0")}</span>
                        <span>
                          <span className="profile-name">{row.profileName}</span>
                          <span className="profile-family">{family}</span>
                          {exception && row.exceptionNote ? <span className="profile-hint">{row.exceptionNote}</span> : null}
                        </span>
                      </span>
                      <span className="targets">
                        {preview.map((target) => (
                          <span className={target.prior !== target.proposed ? "anchor-chip changed" : "anchor-chip"} key={target.skillName}>
                            <span className="anchor-label">{target.skillName}</span>
                            <b>{target.proposed}</b>
                          </span>
                        ))}
                        {extra > 0 ? <span className="anchor-more">+{extra}</span> : null}
                      </span>
                      <span className={row.classification === "aligned" ? "chip mint" : "chip amber"}>{labels[row.classification] ?? row.classification}</span>
                      <span className={signed && !exception ? "chip mint" : exception && !signed ? "chip amber" : "chip quiet"}>{signed ? (exception ? "Decided" : "Signed") : exception ? "Review" : "Clear"}</span>
                      </button>
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
          {showAfter ? (
            <div className="batch-after">
              <p><b>{aligned.length} aligned {aligned.length === 1 ? "profile" : "profiles"} moved together.</b> {exceptions.filter((row) => !row.decision).length} still need a decision.</p>
              <button type="button" className="fill-btn" onClick={() => setFilter("remaining")}>Show remaining</button>
            </div>
          ) : null}
        </>
      ) : null}
      {open && pack ? (
        <div className="gap-overlay drawer-host" onMouseDown={(event) => { if (event.target === event.currentTarget) closeDrawer(); }}>
          <aside className="profile-drawer" role="dialog" aria-modal="true" aria-labelledby="profile-sheet-title">
            <button type="button" className="drawer-close" aria-label="Close profile" onClick={closeDrawer}>×</button>
            <p className="drawer-kicker">{[lens?.band, lens?.familyName].filter((item) => item && item.length > 0).join(" · ") || (labels[open.classification] ?? open.classification)}</p>
            <h2 id="profile-sheet-title">{open.profileName}</h2>
            <p className="lede">{lens ? `${lens.targets.length} anchors${lens.levelName ? ` · ${lens.levelName}` : ""}` : "Reading the profile."}</p>
            {lens?.purpose ? (
              <div className="role-brief">
                <small>Profile brief</small>
                <p>{lens.purpose}</p>
              </div>
            ) : null}
            {open.exceptionNote ? <p>{open.exceptionNote}</p> : null}
            {drawerNote ? <p>Note: {drawerNote}</p> : null}
            {lensProblem ? <p className="gate-alert" role="alert">{lensProblem}</p> : null}
            <h3>Benchmark targets</h3>
            <div className="target-head"><span>Anchor skill</span><span>Proposed</span></div>
            {(lens?.targets ?? []).map((target) => (
              <div className="target-row" key={target.skillName}>
                <b>{target.skillName}</b>
                <span className={target.prior !== target.proposed ? "level changed" : "level"}>{target.proposed}</span>
              </div>
            ))}
            {lens && lens.targets.length === 0 ? <p>No benchmark targets are stored for this profile.</p> : null}
            {open.decision ? <p>Decision recorded: {open.decision}</p> : null}
            {composer ? (
              <form
                className="drawer-composer"
                onSubmit={(event) => {
                  event.preventDefault();
                  const text = composerText.trim();
                  if (text.length === 0) return;
                  if (composer === "note") {
                    setDrawerNote(text);
                    setComposer(null);
                    setComposerText("");
                    return;
                  }
                  if (open.classification === "aligned" || pack.status !== "submitted") {
                    setProblem("A counter-proposal is recorded on an exception, not on an aligned profile.");
                    return;
                  }
                  setPending(true);
                  void decideException(pack.packId, open.rowId, text)
                    .then(() => load())
                    .then(() => notePracticeChange())
                    .then(() => { setProblem(null); setComposer(null); setComposerText(""); })
                    .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That decision was refused."))
                    .finally(() => setPending(false));
                }}
              >
                <label>
                  {composer === "counter" ? "Counter-proposal" : "Note"}
                  <input value={composerText} onChange={(event) => setComposerText(event.target.value)} />
                </label>
                <button type="submit" className="fill-btn" disabled={pending || composerText.trim().length === 0}>{composer === "counter" ? "Record decision" : "Keep note"}</button>
              </form>
            ) : null}
            <div className="drawer-actions">
              {pack.status === "submitted" && open.classification === "aligned" ? (
                <button
                  type="button"
                  className={open.reviewedAt ? "outline-btn signed-btn" : "fill-btn"}
                  disabled={pending || Boolean(open.reviewedAt)}
                  onClick={() => {
                    if (open.reviewedAt) return;
                    setPending(true);
                    void markReviewed(pack.packId, [open.rowId])
                      .then(() => load())
                      .then(() => notePracticeChange())
                      .then(() => setProblem(null))
                      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That review was refused."))
                      .finally(() => setPending(false));
                  }}
                >
                  {open.reviewedAt ? "✓ Profile signed" : "Accept profile"}
                </button>
              ) : null}
              {pack.status === "submitted" && open.classification !== "aligned" ? (
                <button
                  type="button"
                  className={open.decision ? "outline-btn signed-btn" : "fill-btn"}
                  disabled={pending || Boolean(open.decision)}
                  onClick={() => {
                    if (open.decision) return;
                    setPending(true);
                    void decideException(pack.packId, open.rowId, "Accepted")
                      .then(() => load())
                      .then(() => notePracticeChange())
                      .then(() => setProblem(null))
                      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That decision was refused."))
                      .finally(() => setPending(false));
                  }}
                >
                  {open.decision ? "✓ Profile signed" : "Accept profile"}
                </button>
              ) : null}
              <button type="button" className="outline-btn" onClick={() => { setComposer("counter"); setComposerText(open.decision ?? ""); }}>Counter-propose</button>
              <button type="button" className="quiet-btn" onClick={() => { setComposer("note"); setComposerText(drawerNote); }}>Add note</button>
            </div>
          </aside>
        </div>
      ) : null}
    </section>
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
  const notesReady = exceptions.every((row) => Boolean(row.exceptionNote));
  const attested = alignedReviewed + exceptionsDecided;
  const ready = rows.length > 0 && alignedReviewed === aligned.length && exceptionsDecided === exceptions.length;
  const sealed = pack?.status === "sealed";
  return (
    <section data-screen="sign-off">
      <div className="review-head">
        <div>
          <p className="eyebrow">Practice attestation · Final seal</p>
          <h1>Lock the baseline the practice will be measured against.</h1>
          <p className="lede">Every profile must be attested before you seal. This version becomes the contract for assessment, development, and staffing decisions.</p>
        </div>
        {pack ? (
          <div className="seal-badge">
            <strong>{attested}</strong>
            <span>of {rows.length} attested</span>
          </div>
        ) : null}
      </div>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {!pack ? <p className="empty-note">No pack is open.</p> : null}
      {pack && !sealed ? (
        <>
          <div className="briefing-grid">
            <article className="decision-letter">
              <div className="letter-top">
                <div>
                  <p className="eyebrow">Data practice</p>
                  <h2>Attestation checklist</h2>
                </div>
                <span className={ready ? "stamp ready" : "stamp"}>{ready ? "Ready to seal" : "In progress"}</span>
              </div>
              <ul className="seal-checklist">
                <li className="done">L&D sent this pack{pack.submittedBy ? ` · ${pack.submittedBy}` : ""}</li>
                <li className={notesReady ? "done" : "pending"}>{notesReady ? "Each material shift carries a note" : "A material shift is missing its note"}</li>
                <li className={ready ? "done" : "pending"}>{ready ? `All ${rows.length} profiles are attested` : `${rows.length - attested} of ${rows.length} profiles still open`}</li>
                <li className={exceptionsDecided === exceptions.length ? "done" : "pending"}>{exceptionsDecided === exceptions.length ? "Exception decisions are recorded" : `${exceptions.length - exceptionsDecided} exception${exceptions.length - exceptionsDecided === 1 ? "" : "s"} still ${exceptions.length - exceptionsDecided === 1 ? "needs" : "need"} a decision`}</li>
              </ul>
              <p className="seal-verdict"><strong>{ready ? "The pack can be sealed." : "Resolve every open profile in the review room first."}</strong> Seal stays refused while an exception has no recorded decision.</p>
              <div className="letter-actions">
                <button
                  type="button"
                  className="fill-btn"
                  disabled={pending || !ready}
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
                  {ready ? "Seal the baseline" : `${rows.length - attested} still open`}
                </button>
                <Link className="outline-btn" to="/workspace/review">Return to review room</Link>
              </div>
            </article>
            <aside className="margin">
              <h3>What sealing does</h3>
              <div className="margin-stat"><strong>{rows.length}</strong><span>job profiles become the live practice bar</span></div>
              <div className="margin-stat"><strong>{exceptions.length}</strong><span>material shifts keep their recorded reason</span></div>
              <div className="margin-stat"><strong>{rows.length - attested}</strong><span>profiles still open before the seal</span></div>
              <p className="source-note">After seal, the attested pack is the baseline for this practice.</p>
            </aside>
          </div>
          <div className="pack seal-roster">
            <div className="pack-columns seal-columns">
              <span>Profile</span>
              <span>Attestation</span>
            </div>
            {rows.map((row) => {
              const done = row.classification === "aligned" ? Boolean(row.reviewedAt) : Boolean(row.decision);
              return (
                <div className="seal-line" key={row.rowId}>
                  <span>
                    <b>{row.profileName}</b>
                    <small>{labels[row.classification] ?? row.classification}</small>
                  </span>
                  <span className={done ? "chip mint" : "chip amber"}>{done ? "Signed" : "Pending"}</span>
                </div>
              );
            })}
          </div>
          <div className="flowline" aria-label="Approval journey">
            <div className="flowstep"><small>01 · Arrive</small><b>Understand the ask</b></div>
            <div className="flowstep"><small>02 · Clear</small><b>{aligned.length === 1 ? "Move 1 together" : `Move ${aligned.length} together`}</b></div>
            <div className="flowstep"><small>03 · Judge</small><b>{exceptions.length === 1 ? "Resolve 1 shift" : `Resolve ${exceptions.length} shifts`}</b></div>
            <div className="flowstep current"><small>04 · Seal</small><b>Set the practice bar</b></div>
            <div className="flowstep"><small>05 · Steer</small><b>Read market readiness</b></div>
          </div>
        </>
      ) : null}
      {pack && sealed ? (
        <div className="seal-complete">
          <div className="seal-hero">
            <div className="seal-mark" aria-hidden="true">✓</div>
            <div>
              <p className="eyebrow">Baseline locked</p>
              <h2>Bar set for Data.</h2>
              <p>{rows.length} profiles are now the sealed baseline. Material shifts keep their recorded decision.</p>
            </div>
          </div>
          <div className="letter-actions">
            <Link className="outline-btn" to="/workspace/review">View signed roster</Link>
          </div>
        </div>
      ) : null}
    </section>
  );
}

type GapPath = "lead" | "ld";

function requestStanding(request: { profileName?: string | null; requestStatus?: string }) {
  if (request.profileName) return "Benchmark in progress";
  if (request.requestStatus === "needs-context") return "Needs context";
  if (request.requestStatus === "waiting") return "Waiting on com-lead";
  if (request.requestStatus === "in-progress") return "Benchmark in progress";
  return "New request";
}

function RequestsPage() {
  useTitle("Profile requests");
  const { briefing, problem, setProblem, load } = useBriefing();
  const [pending, setPending] = useState(false);
  const [studio, setStudio] = useState<GapPath | null>(null);
  const [profileName, setProfileName] = useState("");
  const [band, setBand] = useState("");
  const [purpose, setPurpose] = useState("");
  const [held, setHeld] = useState<string | null>(null);
  const requests = briefing?.requests ?? [];
  return (
    <section data-screen="practice-requests">
      <header className="req-page-head">
        <p className="eyebrow">Practice demand · Profile request</p>
        <p className="lede">When work does not map to the catalogue, choose who leads the first draft—you as competency lead, or L&D from your evidence.</p>
      </header>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <div className="req-hero">
        <div className="req-hero-mark" aria-hidden="true"><span className="orb" /></div>
        <p className="req-hero-kicker">New profile request</p>
        <h1>Define a profile the catalogue does not cover yet.</h1>
        <p className="req-hero-sub"><strong>Draft as competency lead</strong> when you want to set anchor targets from market evidence. <strong>Request L&D authoring</strong> when you want the profile written from your brief.</p>
        <div className="req-hero-actions">
          <button type="button" className="btn-req-hero btn-req-hero--lead" onClick={() => setStudio("lead")}>Draft as competency lead</button>
          <button type="button" className="btn-req-hero btn-req-hero--ld" onClick={() => setStudio("ld")}>Request L&D authoring</button>
        </div>
        <p className="req-hero-foot">Returns in your approval pack</p>
      </div>
      {held ? <p className="req-held" role="status">{held}</p> : null}
      {requests.length > 0 ? (
        <div className="req-queue">
          <h2>In this practice</h2>
          <p>The same requests L&D sees. A request that has not been opened stays as it was filed. Accepting one does not publish a benchmark.</p>
          <div className="request-list">
            {requests.map((request) => {
              const standing = requestStanding(request);
              const tone = standing === "Benchmark in progress" ? "mint" : standing === "New request" || standing === "Needs context" ? "amber" : "grey";
              const inProgress = Boolean(request.profileName) || request.requestStatus === "in-progress";
              const title = request.title?.trim() || request.note;
              return (
                <article key={request.requestId} className="request-card">
                  <div>
                    <span className={`pill ${tone}`}>{standing}</span>
                    <h2>{title}</h2>
                    <p>{request.competencyName || "Competency"} · {formatAsOf(request.recordedAt)}</p>
                  </div>
                  <div className="request-note">
                    {request.note && request.note !== title ? <p>{request.note}</p> : null}
                    {request.evidence ? <p className="evidence">{request.evidence}</p> : null}
                    <p>{request.profileName ? `Profile opened: ${request.profileName}` : "Not started."}</p>
                    {request.skills && request.skills.length > 0 ? (
                      <ul className="request-skills">
                        {request.skills.map((skill) => (
                          <li key={skill.name}><b>{skill.name}</b><span>{skill.level}</span></li>
                        ))}
                      </ul>
                    ) : null}
                  </div>
                  <div className="request-action">
                    {!inProgress && !request.acceptedAt ? (
                      <button
                        type="button"
                        className="outline-btn"
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
                    ) : !inProgress && request.acceptedAt ? <em>Accepted</em> : null}
                  </div>
                </article>
              );
            })}
          </div>
        </div>
      ) : null}
      {studio ? (
        <GapStudio
          path={studio}
          profileName={profileName}
          band={band}
          purpose={purpose}
          onPath={setStudio}
          onProfileName={setProfileName}
          onBand={setBand}
          onPurpose={setPurpose}
          onClose={() => setStudio(null)}
          onHold={(message) => {
            setHeld(message);
            setStudio(null);
          }}
          onFile={async (body) => {
            try {
              await filePracticeRequest(body);
              await load();
              notePracticeChange();
              setProblem(null);
              setHeld(null);
              setStudio(null);
            } catch (error) {
              setProblem(error instanceof Error ? error.message : "That request was refused.");
              throw error;
            }
          }}
        />
      ) : null}
    </section>
  );
}

function GapStudio({
  path,
  profileName,
  band,
  purpose,
  onPath,
  onProfileName,
  onBand,
  onPurpose,
  onClose,
  onHold,
  onFile,
}: {
  path: GapPath;
  profileName: string;
  band: string;
  purpose: string;
  onPath: (path: GapPath) => void;
  onProfileName: (value: string) => void;
  onBand: (value: string) => void;
  onPurpose: (value: string) => void;
  onClose: () => void;
  onHold: (message: string) => void;
  onFile: (body: {
    practiceCode: string;
    profileName: string;
    purpose: string;
    path: GapPath;
    competencyCode: string;
    anchors: { name: string; rank: string }[];
  }) => Promise<void>;
}) {
  const [taxonomy, setTaxonomy] = useState<CatalogueTaxonomy | null>(null);
  const [taxonomyProblem, setTaxonomyProblem] = useState<string | null>(null);
  const [rows, setRows] = useState<{ id: string; code: string; rank: string }[]>([
    { id: "anchor-1", code: "", rank: "" },
  ]);
  const [sending, setSending] = useState(false);
  const [fileProblem, setFileProblem] = useState<string | null>(null);
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  useEffect(() => {
    let live = true;
    getCatalogueTaxonomy()
      .then((next) => {
        if (!live) return;
        setTaxonomy(next);
        setTaxonomyProblem(null);
      })
      .catch((error: unknown) => {
        if (!live) return;
        setTaxonomyProblem(error instanceof Error ? error.message : "The taxonomy could not be read.");
      });
    return () => { live = false; };
  }, []);
  const version = taxonomy?.version ?? null;
  const practiceSkills = (version?.competencies ?? []).filter((competency) => competency.practiceCode.toLowerCase() === practiceCode);
  const competencies = practiceSkills.length > 0 ? practiceSkills : (version?.competencies ?? []);
  const proficiencies = version?.proficiencies ?? [];
  const skills = competencies.flatMap((competency) => competency.skills.map((skill) => ({
    code: skill.code,
    name: skill.name,
    competency: competency.code,
  })));
  const used = new Set(rows.map((row) => row.code).filter((code) => code.length > 0));
  const hold = () => onHold(profileName.trim()
    ? `${profileName.trim()} is held on this page. It is not sent, and it does not publish a benchmark.`
    : "That draft is held on this page. It is not sent, and it does not publish a benchmark.");
  return (
    <div className="gap-overlay" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section className="gap-studio" role="dialog" aria-modal="true" aria-labelledby="gap-title">
        <header className="gap-head">
          <div>
            <p className="eyebrow">Market gap studio</p>
            <h2 id="gap-title">Design a role the default catalogue missed</h2>
            <p>Start from demand evidence. Choose whether L&D authors it or you draft the first benchmark.</p>
          </div>
          <button type="button" className="gap-close" aria-label="Close market gap studio" onClick={onClose}>×</button>
        </header>
        <div className="gap-work">
          <p className="eyebrow">Choose the ownership model</p>
          <div className="path-switch">
            <button type="button" className={path === "lead" ? "path-option active" : "path-option"} aria-pressed={path === "lead"} onClick={() => onPath("lead")}>
              <b>Competency lead draft</b>
              <span>You set anchor targets from market evidence; L&D completes taxonomy hygiene.</span>
            </button>
            <button type="button" className={path === "ld" ? "path-option active" : "path-option"} aria-pressed={path === "ld"} onClick={() => onPath("ld")}>
              <b>L&D-authored profile</b>
              <span>Send evidence and role intent; L&D writes the profile.</span>
            </button>
          </div>
          <div className="draft-grid">
            <label className="field">Proposed profile<input value={profileName} onChange={(event) => onProfileName(event.target.value)} /></label>
            <label className="field">Designation band<input value={band} onChange={(event) => onBand(event.target.value)} /></label>
            <label className="field full">Market purpose<textarea rows={2} value={purpose} onChange={(event) => onPurpose(event.target.value)} /></label>
          </div>
          {path === "lead" ? (
            <div className="draft-anchors">
              <p className="eyebrow">Proposed anchor benchmark</p>
              {taxonomyProblem ? <p className="gate-alert" role="alert">{taxonomyProblem}</p> : null}
              <div className="anchor-columns" aria-hidden="true">
                <span>Taxonomy skill</span>
                <span>Target level</span>
              </div>
              {rows.map((row) => (
                <div className="anchor-row" key={row.id}>
                  <select
                    aria-label="Taxonomy skill"
                    value={row.code}
                    onChange={(event) => setRows((current) => current.map((item) => item.id === row.id ? { ...item, code: event.target.value } : item))}
                  >
                    <option value="">{taxonomy ? "Choose a shared skill" : "Reading the taxonomy"}</option>
                    {skills.filter((skill) => skill.code === row.code || !used.has(skill.code)).map((skill) => (
                      <option key={skill.code} value={skill.code}>{skill.name} · {competencies.find((item) => item.code === skill.competency)?.name || skill.competency}</option>
                    ))}
                  </select>
                  <select
                    aria-label="Target level"
                    value={row.rank}
                    onChange={(event) => setRows((current) => current.map((item) => item.id === row.id ? { ...item, rank: event.target.value } : item))}
                  >
                    <option value="">Set later</option>
                    {proficiencies.map((item) => <option key={item.rank} value={item.rank}>L{item.rank}</option>)}
                  </select>
                  {rows.length > 1 ? (
                    <button type="button" aria-label="Remove skill" onClick={() => setRows((current) => current.filter((item) => item.id !== row.id))}>×</button>
                  ) : <span />}
                </div>
              ))}
              <button
                type="button"
                className="text-link"
                disabled={skills.length > 0 && used.size >= skills.length}
                onClick={() => setRows((current) => [...current, { id: `anchor-${Date.now()}`, code: "", rank: "" }])}
              >
                Add another skill
              </button>
              <p className="card-note">The skill name and proficiency meaning come from the taxonomy. This profile sets only the target level.</p>
            </div>
          ) : null}
        </div>
        <footer className="gap-foot">
          {fileProblem ? <p className="gate-alert" role="alert">{fileProblem}</p> : null}
          <button type="button" className="outline-btn" onClick={hold} disabled={sending}>Save draft</button>
          <button
            type="button"
            className="fill-btn"
            disabled={sending}
            onClick={() => {
              if (profileName.trim().length === 0) {
                setFileProblem("Name the profile before sending the request.");
                return;
              }
              const chosen = rows.filter((row) => row.code.length > 0);
              setFileProblem(null);
              setSending(true);
              void onFile({
                practiceCode,
                profileName: profileName.trim(),
                purpose,
                path,
                competencyCode: skills.find((skill) => skill.code === chosen[0]?.code)?.competency ?? "",
                anchors: chosen.map((row) => ({
                  name: skills.find((skill) => skill.code === row.code)?.name ?? row.code,
                  rank: row.rank,
                })),
              })
                .catch((error: unknown) => setFileProblem(error instanceof Error ? error.message : "That request was refused."))
                .finally(() => setSending(false));
            }}
          >
            {path === "lead" ? "Submit competency lead draft to L&D" : "Submit authoring request to L&D"}
          </button>
        </footer>
      </section>
    </div>
  );
}

function ComingSoon({ screen, title }: { screen: string; title: string }) {
  useTitle(title);
  return (
    <section data-screen={screen}>
      <PageHead eyebrow={title} title="Coming soon." lede="This part of the workspace is not open yet." />
    </section>
  );
}

function BaselinePage() {
  return <ComingSoon screen="baseline" title="Baseline" />;
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
  return <ComingSoon screen="market" title="Market demand" />;
}

function ReadinessPage() {
  return <ComingSoon screen="readiness" title="Readiness" />;
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
