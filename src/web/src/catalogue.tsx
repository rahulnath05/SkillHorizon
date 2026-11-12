import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { NavLink, useParams } from "react-router-dom";
import { PageHead, PersonaFrame, type RailItem } from "./frame";
import {
  admitRolling,
  admitWave,
  getActivationCentre,
  getApprovals,
  getCatalogueBenchmark,
  getCatalogueMarket,
  getCatalogueOverview,
  getCatalogueTaxonomy,
  getProfileRequests,
  getStandards,
  openProfileRequest,
  publishBenchmark,
  publishMarket,
  setActivationBudget,
  saveBenchmarkDraft,
  saveMarketDraft,
  saveTaxonomyDraft,
  sendDraft,
  type ActivationCentre,
  type CatalogueBenchmark,
  type CatalogueOverview,
  type CatalogueTaxonomy,
  type ProfileRequest,
  type SentDraft,
  type Standards,
} from "./api";
import { PackSubmission } from "./practice";
import { OperationsPage } from "./operations";
import { EnginePage, StudioPage } from "./studio";
import { formatAsOf, useTitle } from "./ui";

const primaryNav: RailItem[] = [
  { id: "overview", label: "Overview", icon: "◫" },
  { id: "workspace", label: "Benchmark workspace", icon: "≡" },
  { id: "taxonomy", label: "Skill taxonomy", icon: "◇" },
  { id: "requests", label: "Profile requests", icon: "+" },
  { id: "approvals", label: "Approval queue", icon: "✓" },
  { id: "activation", label: "Activation centre", icon: "→" },
  { id: "studio", label: "Assessment & programme", icon: "▦" },
];

const studioNav = [
  { id: "studio", icon: "◫", label: "Overview" },
  { id: "engine", icon: "≡", label: "Adaptive engine", pill: "ET-01" },
  { id: "marking", icon: "◇", label: "Rubric & marking", pill: "ET-02" },
  { id: "panel", icon: "✓", label: "SME panel", pill: "ET-04" },
  { id: "bank", icon: "→", label: "Item bank", pill: "IB-01" },
  { id: "scorecard", icon: "▦", label: "Programme scorecard", pill: "DR-09" },
  { id: "operations", icon: "◔", label: "Assessment operations", pill: "DR-04" },
  { id: "declared", icon: "◎", label: "Declared vs assessed", pill: "DR-06" },
  { id: "pilot", icon: "✔", label: "Pilot report", pill: "DR-03" },
  { id: "rolling", icon: "↻", label: "Rolling intake", pill: "PR-08" },
];

const studioIds = new Set(studioNav.map((item) => item.id).concat("assessment"));

export function CatalogueHome() {
  const { screen } = useParams();
  const known = primaryNav.some((item) => item.id === screen) || studioIds.has(screen ?? "") || screen === "market";
  const raw = known ? screen! : "overview";
  const studioCurrent = raw === "assessment" ? "studio" : studioIds.has(raw) ? raw : null;
  const inStudio = studioCurrent !== null;
  const [counts, setCounts] = useState<{ requests?: number; approvals?: number; activation?: number }>({});

  useEffect(() => {
    let live = true;
    void Promise.all([getCatalogueOverview(), getActivationCentre()])
      .then(([overview, centre]) => {
        if (!live) return;
        setCounts({
          requests: overview.requestCount,
          approvals: overview.approvalCount,
          activation: centre.waves.length,
        });
      })
      .catch(() => {
        if (live) setCounts({});
      });
    return () => {
      live = false;
    };
  }, [raw]);

  const items = primaryNav.map((item) => ({
    ...item,
    count: item.id === "requests" ? counts.requests : item.id === "approvals" ? counts.approvals : item.id === "activation" ? counts.activation : undefined,
  }));
  const studioLabel = studioNav.find((item) => item.id === studioCurrent)?.label ?? "Overview";
  const page = inStudio ? studioLabel : raw === "market" ? "Market demand" : primaryNav.find((item) => item.id === raw)?.label ?? "Overview";

  return (
    <PersonaFrame
      label="L&D"
      kicker="Learning and development"
      mark="LD"
      section={inStudio ? "Assessment and programme" : raw === "market" ? "Benchmark workspace" : undefined}
      page={page}
      items={items}
      currentId={inStudio ? studioCurrent! : raw}
      highlightId={inStudio ? "studio" : raw === "market" ? "workspace" : raw}
      studio={{
        parentId: "studio",
        open: inStudio,
        currentId: studioCurrent ?? "studio",
        items: studioNav,
      }}
    >
      {raw === "taxonomy" ? <TaxonomyPage /> : null}
      {raw === "workspace" ? <WorkspacePage /> : null}
      {raw === "requests" ? <RequestsPage /> : null}
      {raw === "approvals" ? <ApprovalsPage /> : null}
      {raw === "market" ? <MarketPage /> : null}
      {raw === "activation" ? <ActivationPage /> : null}
      {studioCurrent === "engine" ? <EnginePage /> : null}
      {studioCurrent === "bank" || studioCurrent === "marking" || studioCurrent === "panel" ? <StudioPage screen={studioCurrent} /> : null}
      {studioCurrent === "studio" ? <OperationsPage screen="studio" /> : null}
      {studioCurrent === "scorecard" || studioCurrent === "operations" || studioCurrent === "declared" || studioCurrent === "pilot" || studioCurrent === "rolling" ? (
        <OperationsPage screen={studioCurrent} />
      ) : null}
      {raw === "overview" ? <OverviewPage /> : null}
    </PersonaFrame>
  );
}

function OverviewPage() {
  const [overview, setOverview] = useState<CatalogueOverview | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("Overview");
  const load = useLoader(async () => {
    setOverview(await getCatalogueOverview());
    setProblem(null);
  }, (error) => setProblem(error));
  const snapshots = overview?.snapshots ?? [];
  const gaps = snapshots.reduce((total, snapshot) => total + snapshot.gapCount, 0);
  return (
    <section data-screen="overview">
      <PageHead
        eyebrow="L&D competency benchmarking"
        title="Overview"
        lede="See what is ready, what needs attention, and what can be sent to each competency lead. Counts come from the catalogue, and a missing snapshot stays empty."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {overview ? (
        <>
          <div className="stat-row">
            <NavLink to="/workspace/requests">
              <span className="kicker">Profile requests</span>
              <strong>{overview.requestCount}</strong>
              <span className="hint">Waiting in the catalogue</span>
            </NavLink>
            <NavLink to="/workspace/approvals">
              <span className="kicker">Sent for approval</span>
              <strong>{overview.approvalCount}</strong>
              <span className="hint">Drafts already with a competency lead</span>
            </NavLink>
            <NavLink to="/workspace/workspace">
              <span className="kicker">Practice snapshots</span>
              <strong>{snapshots.length}</strong>
              <span className="hint">{snapshots.length === 0 ? "None written yet" : "Practices with a stored snapshot"}</span>
            </NavLink>
            <div className="stat-card ink">
              <span className="kicker">Below the bar</span>
              <strong>{gaps}</strong>
              <span className="hint">Sum of the stored snapshots</span>
            </div>
          </div>
          <div className="split">
            <article className="surface-card">
              <header>
                <div>
                  <h2>Practice readiness</h2>
                  <p className="card-note">Stored snapshots only</p>
                </div>
                <NavLink to="/workspace/approvals">Approval queue</NavLink>
              </header>
              {snapshots.length === 0 ? <p className="empty-note">No practice snapshot has been written.</p> : null}
              <ul className="gap-list">
                {snapshots.map((snapshot) => (
                  <li key={snapshot.practiceCode}>
                    <b>{snapshot.practiceCode}</b>
                    <span>As of {snapshot.asOf} · {snapshot.memberCount} members · {snapshot.measuredCount} measured · {snapshot.gapCount} below the bar · {snapshot.cohortCount} cohorts</span>
                  </li>
                ))}
              </ul>
            </article>
            <aside className="surface-card">
              <header>
                <div>
                  <h2>Where to work next</h2>
                  <p className="card-note">The same reads as the rail</p>
                </div>
              </header>
              <ul className="gap-list">
                <li><NavLink to="/workspace/taxonomy">Skill taxonomy</NavLink><span>Draft the catalogue</span></li>
                <li><NavLink to="/workspace/workspace">Benchmark workspace</NavLink><span>Targets and market demand</span></li>
                <li><NavLink to="/workspace/studio">Assessment and programme</NavLink><span>Studio, operations, and intake</span></li>
              </ul>
            </aside>
          </div>
        </>
      ) : null}
    </section>
  );
}

function TaxonomyPage() {
  const [taxonomy, setTaxonomy] = useState<CatalogueTaxonomy | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [practiceCode, setPracticeCode] = useState("");
  const [practiceName, setPracticeName] = useState("");
  const [competencyCode, setCompetencyCode] = useState("");
  const [competencyName, setCompetencyName] = useState("");
  const [skillCode, setSkillCode] = useState("");
  const [skillName, setSkillName] = useState("");
  useTitle("Skill taxonomy");
  const load = useLoader(async () => {
    setTaxonomy(await getCatalogueTaxonomy());
    setProblem(null);
  }, (error) => setProblem(error));
  const version = taxonomy?.version ?? null;
  return (
    <section data-screen="taxonomy">
      <PageHead
        eyebrow="L&D"
        title="Skill taxonomy"
        lede="One taxonomy version. A draft edit stays in this authoring zone."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {version ? <p>Version {version.versionNumber} · {version.status}</p> : <p>No taxonomy version is in this zone.</p>}
      {version?.competencies.map((competency) => (
        <article key={`${competency.practiceCode}-${competency.code}`}>
          <h2>{competency.name}</h2>
          <p>{competency.practiceName}</p>
          <ul className="gap-list">
            {competency.skills.map((skill) => (
              <li key={skill.code}><b>{skill.name}</b><span>{skill.code}</span></li>
            ))}
          </ul>
        </article>
      ))}
      {version ? (
        <form
          className="task-close"
          onSubmit={(event) => {
            event.preventDefault();
            setPending(true);
            void saveTaxonomyDraft(withSkill(version, {
              practiceCode,
              practiceName,
              competencyCode,
              competencyName,
              skillCode,
              skillName,
            }))
              .then(() => {
                setSkillCode("");
                setSkillName("");
                return load();
              })
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That draft was not saved."))
              .finally(() => setPending(false));
          }}
        >
          <h2>Draft edit</h2>
          <label>Practice code<input value={practiceCode} onChange={(event) => setPracticeCode(event.target.value)} /></label>
          <label>Practice name<input value={practiceName} onChange={(event) => setPracticeName(event.target.value)} /></label>
          <label>Competency code<input value={competencyCode} onChange={(event) => setCompetencyCode(event.target.value)} /></label>
          <label>Competency name<input value={competencyName} onChange={(event) => setCompetencyName(event.target.value)} /></label>
          <label>Skill code<input value={skillCode} onChange={(event) => setSkillCode(event.target.value)} /></label>
          <label>Skill name<input value={skillName} onChange={(event) => setSkillName(event.target.value)} /></label>
          <button type="submit" className="draft-save" disabled={pending || skillCode.trim().length === 0 || skillName.trim().length === 0}>Save draft</button>
        </form>
      ) : null}
    </section>
  );
}

function WorkspacePage() {
  const [standards, setStandards] = useState<Standards | null>(null);
  const [profileId, setProfileId] = useState("");
  const [benchmark, setBenchmark] = useState<CatalogueBenchmark | null>(null);
  const [ranks, setRanks] = useState<Record<string, number>>({});
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("Benchmark workspace");
  const loadStandards = useLoader(async () => {
    const next = await getStandards();
    setStandards(next);
    const first = next.frameworks.flatMap((framework) => framework.profiles)[0];
    setProfileId((current) => current || first?.profileId || "");
    setProblem(null);
  }, (error) => setProblem(error));
  useEffect(() => {
    if (!profileId) return;
    void getCatalogueBenchmark(profileId)
      .then((next) => {
        setBenchmark(next);
        setRanks(Object.fromEntries(next.skills.map((skill) => [skill.skillId, skill.proficiencyRank])));
        setProblem(null);
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That profile could not be opened."));
  }, [profileId]);
  const profiles = standards?.frameworks.flatMap((framework) => framework.profiles) ?? [];
  return (
    <section data-screen="workspace">
      <PageHead
        eyebrow="L&D"
        title="Benchmark workspace"
        lede="Edit draft targets for one profile. Publish uses the command already built. Market demand stays on this workspace."
        action={<NavLink to="/workspace/market">Market demand</NavLink>}
      />
      {problem ? <Problem text={problem} onRetry={loadStandards} /> : null}
      <label>
        Profile
        <select value={profileId} onChange={(event) => setProfileId(event.target.value)}>
          {profiles.map((profile) => (
            <option key={profile.profileId} value={profile.profileId}>{profile.name}</option>
          ))}
        </select>
      </label>
      {benchmark ? (
        <form
          className="draft"
          onSubmit={(event) => {
            event.preventDefault();
            setPending(true);
            void saveBenchmarkDraft({
              jobProfileId: benchmark.profile.profileId,
              taxonomyVersionId: benchmark.taxonomyVersionId,
              targets: benchmark.skills.map((skill) => ({ skillId: skill.skillId, proficiencyRank: ranks[skill.skillId] ?? skill.proficiencyRank })),
            })
              .then(() => getCatalogueBenchmark(profileId))
              .then((next) => {
                setBenchmark(next);
                setProblem(null);
              })
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The draft could not be saved."))
              .finally(() => setPending(false));
          }}
        >
          <h2>{benchmark.profile.profileName}</h2>
          <p>{benchmark.profile.frameworkName} · {benchmark.profile.levelName}. {benchmark.benchmarkVersionId ? `Draft version ${benchmark.versionNumber}.` : "No draft is open for this profile."}</p>
          <div className="team-scroll">
            <table className="team-table">
              <thead>
                <tr><th>Skill</th><th>Target</th></tr>
              </thead>
              <tbody>
                {benchmark.skills.map((skill) => (
                  <tr key={skill.skillId}>
                    <td><strong>{skill.skillName}</strong><span className="team-id">{skill.practiceName} · {skill.competencyName}</span></td>
                    <td>
                      <select value={ranks[skill.skillId] ?? skill.proficiencyRank} onChange={(event) => setRanks((current) => ({ ...current, [skill.skillId]: Number(event.target.value) }))}>
                        {benchmark.proficiencies.map((item) => (
                          <option key={item.rank} value={item.rank}>{item.name}</option>
                        ))}
                      </select>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="draft-actions">
            <button type="submit" className="draft-save" disabled={pending}>Save draft</button>
            <button
              type="button"
              className="draft-publish"
              disabled={pending || !benchmark.benchmarkVersionId}
              onClick={() => {
                if (!benchmark.benchmarkVersionId) return;
                setPending(true);
                void publishBenchmark(benchmark.benchmarkVersionId)
                  .then(() => getCatalogueBenchmark(profileId))
                  .then((next) => {
                    setBenchmark(next);
                    setProblem(null);
                  })
                  .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The benchmark could not be published."))
                  .finally(() => setPending(false));
              }}
            >
              Publish version
            </button>
          </p>
        </form>
      ) : null}
      <PackSubmission />
    </section>
  );
}

function RequestsPage() {
  const [requests, setRequests] = useState<ProfileRequest[]>([]);
  const [standards, setStandards] = useState<Standards | null>(null);
  const [requestId, setRequestId] = useState("");
  const [profileName, setProfileName] = useState("");
  const [frameworkCode, setFrameworkCode] = useState("");
  const [levelCode, setLevelCode] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("Profile requests");
  const load = useLoader(async () => {
    const [nextRequests, nextStandards] = await Promise.all([getProfileRequests(), getStandards()]);
    setRequests(nextRequests);
    setStandards(nextStandards);
    setRequestId((current) => current || nextRequests.find((request) => !request.profileId)?.requestId || nextRequests[0]?.requestId || "");
    const framework = nextStandards.frameworks[0];
    setFrameworkCode((current) => current || framework?.code || "");
    setLevelCode((current) => current || framework?.levels[0]?.code || "");
    setProblem(null);
  }, (error) => setProblem(error));
  const framework = standards?.frameworks.find((item) => item.code === frameworkCode) ?? standards?.frameworks[0];
  return (
    <section data-screen="requests">
      <PageHead
        eyebrow="L&D"
        title="Profile requests"
        lede="A request filed by a manager can become a draft profile. Opening it does not publish a benchmark."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {requests.length === 0 ? <p>No profile request is waiting.</p> : null}
      <ul className="gap-list">
        {requests.map((request) => (
          <li key={request.requestId}>
            <b>{request.displayName}</b>
            <span>{request.note}</span>
            <span>{request.profileName ? `Draft profile ${request.profileName}` : "Not opened"}</span>
          </li>
        ))}
      </ul>
      {requests.length > 0 ? (
        <form
          className="task-close"
          onSubmit={(event) => {
            event.preventDefault();
            setPending(true);
            void openProfileRequest(requestId, { profileName, frameworkCode, levelCode })
              .then(() => {
                setProfileName("");
                return load();
              })
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That request was not opened."))
              .finally(() => setPending(false));
          }}
        >
          <h2>Open as a draft profile</h2>
          <label>
            Request
            <select value={requestId} onChange={(event) => setRequestId(event.target.value)}>
              {requests.map((request) => (
                <option key={request.requestId} value={request.requestId}>{request.displayName}</option>
              ))}
            </select>
          </label>
          <label>Profile name<input value={profileName} onChange={(event) => setProfileName(event.target.value)} /></label>
          <label>
            Framework
            <select value={frameworkCode} onChange={(event) => {
              setFrameworkCode(event.target.value);
              const next = standards?.frameworks.find((item) => item.code === event.target.value);
              setLevelCode(next?.levels[0]?.code || "");
            }}>
              {(standards?.frameworks ?? []).map((item) => (
                <option key={item.code} value={item.code}>{item.name}</option>
              ))}
            </select>
          </label>
          <label>
            Level
            <select value={levelCode} onChange={(event) => setLevelCode(event.target.value)}>
              {(framework?.levels ?? []).map((level) => (
                <option key={level.code} value={level.code}>{level.name}</option>
              ))}
            </select>
          </label>
          <button type="submit" className="draft-save" disabled={pending || profileName.trim().length === 0}>Open draft</button>
        </form>
      ) : null}
    </section>
  );
}

function ApprovalsPage() {
  const [sent, setSent] = useState<SentDraft[]>([]);
  const [standards, setStandards] = useState<Standards | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("Approval queue");
  const load = useLoader(async () => {
    const [nextSent, nextStandards] = await Promise.all([getApprovals(), getStandards()]);
    setSent(nextSent);
    setStandards(nextStandards);
    setProblem(null);
  }, (error) => setProblem(error));
  const sentIds = new Set(sent.map((draft) => draft.benchmarkVersionId));
  const unsent = standards?.drafts.filter((bundle) => !sentIds.has(bundle.draft.benchmarkVersionId)) ?? [];
  return (
    <section data-screen="approvals">
      <PageHead
        eyebrow="L&D"
        title="Approval queue"
        lede="Drafts L&D has sent. An unsent draft is not waiting on the competency lead."
      />
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      <h2>Sent</h2>
      {sent.length === 0 ? <p>No draft has been sent.</p> : null}
      <ul className="gap-list">
        {sent.map((draft) => (
          <li key={draft.benchmarkVersionId}><b>{draft.profileName}</b><span>Draft version {draft.versionNumber}</span></li>
        ))}
      </ul>
      <h2>Unsent drafts</h2>
      {unsent.length === 0 ? <p>Every open draft has been sent.</p> : null}
      <ul className="gap-list">
        {unsent.map((bundle) => (
          <li key={bundle.draft.benchmarkVersionId}>
            <b>{bundle.draft.profileName}</b>
            <span>Draft version {bundle.draft.versionNumber}</span>
            <button
              type="button"
              className="draft-save"
              disabled={pending}
              onClick={() => {
                setPending(true);
                void sendDraft(bundle.draft.benchmarkVersionId)
                  .then(load)
                  .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That draft was not sent."))
                  .finally(() => setPending(false));
              }}
            >
              Send
            </button>
          </li>
        ))}
      </ul>
    </section>
  );
}

function withSkill(version: NonNullable<CatalogueTaxonomy["version"]>, addition: {
  practiceCode: string;
  practiceName: string;
  competencyCode: string;
  competencyName: string;
  skillCode: string;
  skillName: string;
}) {
  const practices = new Map<string, { code: string; name: string; competencies: Map<string, { code: string; name: string; skills: { code: string; name: string }[] }> }>();
  for (const competency of version.competencies) {
    const practice = practices.get(competency.practiceCode) ?? { code: competency.practiceCode, name: competency.practiceName, competencies: new Map() };
    practice.competencies.set(competency.code, {
      code: competency.code,
      name: competency.name,
      skills: competency.skills.map((skill) => ({ code: skill.code, name: skill.name })),
    });
    practices.set(competency.practiceCode, practice);
  }
  const practiceCode = addition.practiceCode.trim() || version.competencies[0]?.practiceCode || "practice";
  const practiceName = addition.practiceName.trim() || version.competencies[0]?.practiceName || "Practice";
  const competencyCode = addition.competencyCode.trim() || version.competencies[0]?.code || "competency";
  const competencyName = addition.competencyName.trim() || version.competencies[0]?.name || "Competency";
  const practice = practices.get(practiceCode) ?? { code: practiceCode, name: practiceName, competencies: new Map() };
  const competency = practice.competencies.get(competencyCode) ?? { code: competencyCode, name: competencyName, skills: [] };
  competency.skills.push({ code: addition.skillCode.trim(), name: addition.skillName.trim() });
  practice.competencies.set(competencyCode, competency);
  practices.set(practiceCode, practice);
  return {
    practices: [...practices.values()].map((item) => ({
      code: item.code,
      name: item.name,
      competencies: [...item.competencies.values()],
    })),
    proficiencies: version.proficiencies.map((item) => ({ code: item.code, name: item.name, rank: item.rank })),
  };
}

function useLoader(action: () => Promise<void>, onError: (message: string) => void) {
  const actionRef = useRef(action);
  const errorRef = useRef(onError);
  actionRef.current = action;
  errorRef.current = onError;
  const load = useCallback(async () => {
    try {
      await actionRef.current();
    } catch (error) {
      errorRef.current(error instanceof Error ? error.message : "The catalogue could not be loaded.");
    }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return load;
}

function Problem({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <p className="gate-alert" role="alert">
      {text}
      <button type="button" className="team-retry" onClick={onRetry}>Try again</button>
    </p>
  );
}

function MarketPage() {
  useTitle("Market demand");
  const [note, setNote] = useState("");
  const [postingCount, setPostingCount] = useState("");
  const [roleCount, setRoleCount] = useState("");
  const [draftNote, setDraftNote] = useState<string | null>(null);
  const [publishedNote, setPublishedNote] = useState<string | null>(null);
  const [publishedAt, setPublishedAt] = useState<string | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    const market = await getCatalogueMarket("data");
    setDraftNote(market.draft?.note ?? null);
    setPublishedNote(market.published?.note ?? null);
    setPublishedAt(market.published?.publishedAt ?? null);
    if (market.draft) {
      setNote(market.draft.note);
      setPostingCount(String(market.draft.postingCount));
      setRoleCount(String(market.draft.roleCount));
    }
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "Market demand could not be loaded."));
  }, [load]);

  function save(event: FormEvent) {
    event.preventDefault();
    const postings = Number(postingCount);
    const roles = Number(roleCount);
    if (!Number.isInteger(postings) || !Number.isInteger(roles) || postings < 0 || roles < 0) {
      setProblem("Postings and roles are whole numbers.");
      return;
    }
    setPending(true);
    void saveMarketDraft({
      practiceCode: "data",
      note,
      postingCount: postings,
      roleCount: roles,
    })
      .then(() => load())
      .then(() => setProblem(null))
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The draft was refused."))
      .finally(() => setPending(false));
  }

  function publish() {
    setPending(true);
    void publishMarket("data")
      .then(() => load())
      .then(() => setProblem(null))
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "Publish was refused."))
      .finally(() => setPending(false));
  }

  return (
    <section data-screen="catalogue-market">
      <PageHead
        eyebrow="Authoring zone"
        title="Market demand"
        lede="Write the note and counts for the data practice. Publishing is what the competency lead can read. An empty draft stays empty until you save one."
        action={<NavLink to="/workspace/workspace">Back to benchmarks</NavLink>}
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <form className="task-close" onSubmit={save}>
        <h2>Draft</h2>
        <label>
          Note
          <textarea value={note} onChange={(event) => setNote(event.target.value)} maxLength={500} required />
        </label>
        <label>
          Postings
          <input inputMode="numeric" value={postingCount} onChange={(event) => setPostingCount(event.target.value)} required />
        </label>
        <label>
          Roles
          <input inputMode="numeric" value={roleCount} onChange={(event) => setRoleCount(event.target.value)} required />
        </label>
        <button type="submit" className="draft-save" disabled={pending || note.trim().length === 0}>Save draft</button>
        <button type="button" className="draft-publish" disabled={pending || draftNote === null} onClick={publish}>Publish</button>
      </form>
      {draftNote ? <p data-draft-note={draftNote}>Draft: {draftNote}</p> : <p>No draft is stored.</p>}
      {publishedNote ? (
        <p data-published-note={publishedNote}>Published copy{publishedAt ? ` · ${formatAsOf(publishedAt)}` : ""}: {publishedNote}</p>
      ) : <p>Nothing has been published for this practice.</p>}
    </section>
  );
}

function ActivationPage() {
  useTitle("Activation centre");
  const [centre, setCentre] = useState<ActivationCentre | null>(null);
  const [versions, setVersions] = useState<{ id: string; name: string }[]>([]);
  const [budget, setBudget] = useState("");
  const [versionId, setVersionId] = useState("");
  const [subjects, setSubjects] = useState("");
  const [waveId, setWaveId] = useState("");
  const [joiner, setJoiner] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    const [next, standards] = await Promise.all([getActivationCentre(), getStandards()]);
    setCentre(next);
    const published = standards.history.filter((item) => item.status === "published");
    setVersions(published.map((item) => ({ id: item.benchmarkVersionId, name: item.profileName })));
    setVersionId((current) => (current && published.some((item) => item.benchmarkVersionId === current) ? current : published[0]?.benchmarkVersionId ?? ""));
    setWaveId((current) => (current && next.waves.some((wave) => wave.waveId === current) ? current : next.waves[0]?.waveId ?? ""));
    return next;
  }, []);

  useEffect(() => {
    void load()
      .then((next) => setBudget(next.sittingBudget === null ? "" : String(next.sittingBudget)))
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The activation centre could not be loaded."));
  }, [load]);

  function run(work: () => Promise<unknown>, done: string, selectNewest = false) {
    setPending(true);
    setNotice(null);
    void work()
      .then(() => load())
      .then((next) => {
        if (selectNewest)
          setWaveId(next.waves[0]?.waveId ?? "");
        setProblem(null);
        setNotice(done);
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The activation centre refused that."))
      .finally(() => setPending(false));
  }

  function saveBudget(event: FormEvent) {
    event.preventDefault();
    const sittingBudget = Number(budget);
    if (!Number.isInteger(sittingBudget) || sittingBudget < 1) {
      setProblem("The zone budget is a whole number of at least one sitting.");
      return;
    }
    run(() => setActivationBudget(sittingBudget), "The zone budget is stored.");
  }

  function launch(event: FormEvent) {
    event.preventDefault();
    const subjectIds = subjects.split(/\s+/).map((item) => item.trim()).filter((item) => item.length > 0);
    if (!versionId || subjectIds.length === 0) {
      setProblem("Choose a published benchmark and name the people to launch.");
      return;
    }
    run(() => admitWave(versionId, subjectIds), "The wave is open.", true);
  }

  function roll(event: FormEvent) {
    event.preventDefault();
    if (!waveId || joiner.trim().length === 0) {
      setProblem("Choose an open wave and name the person who joined after it opened.");
      return;
    }
    run(() => admitRolling(waveId, joiner.trim()), "Rolling intake admitted one person. The other admissions stay.");
  }

  const waves = centre?.waves ?? [];
  const admitted = waves.reduce((total, wave) => total + wave.admittedCount, 0);

  return (
    <section data-screen="activation-centre">
      <PageHead
        eyebrow="Post-approval activation"
        title="Activation centre"
        lede="Launch uses the zone sitting budget. Rolling intake admits one mapped person into an open wave and leaves the other admissions in place."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      <ul className="home-figures">
        <li><b data-budget={centre?.sittingBudget ?? ""}>{centre?.sittingBudget ?? "—"}</b><span>Zone budget</span></li>
        <li><b>{waves.length}</b><span>Open waves</span></li>
        <li><b data-admitted={admitted}>{admitted}</b><span>Admitted</span></li>
      </ul>
      <form className="task-close" onSubmit={saveBudget}>
        <h2>Zone budget</h2>
        <label>
          Sittings
          <input inputMode="numeric" value={budget} onChange={(event) => setBudget(event.target.value)} required />
        </label>
        <button type="submit" className="draft-save" disabled={pending}>Save budget</button>
      </form>
      {waves.length === 0 ? <p>No wave is open in this zone.</p> : (
        <div className="team-scroll">
          <table className="team-table">
            <thead>
              <tr>
                <th>Opened</th>
                <th>Profile</th>
                <th>Admitted</th>
                <th>People</th>
              </tr>
            </thead>
            <tbody>
              {waves.map((wave) => (
                <tr key={wave.waveId} data-wave={wave.waveId}>
                  <td>{formatAsOf(wave.openedAt)}</td>
                  <td>{wave.profileName}</td>
                  <td>{wave.admittedCount}</td>
                  <td>{wave.subjectIds.join(", ")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <form className="task-close" onSubmit={launch}>
        <h2>Launch</h2>
        <label>
          Published benchmark
          <select value={versionId} onChange={(event) => setVersionId(event.target.value)} required>
            {versions.length === 0 ? <option value="">No published benchmark</option> : null}
            {versions.map((version) => <option key={version.id} value={version.id}>{version.name}</option>)}
          </select>
        </label>
        <label>
          People, one identifier per line
          <textarea value={subjects} onChange={(event) => setSubjects(event.target.value)} required />
        </label>
        <button type="submit" className="draft-publish" disabled={pending || !versionId}>Launch</button>
      </form>
      <form className="task-close" onSubmit={roll}>
        <h2>Rolling intake</h2>
        <label>
          Open wave
          <select value={waveId} onChange={(event) => setWaveId(event.target.value)} required>
            {waves.length === 0 ? <option value="">No open wave</option> : null}
            {waves.map((wave) => <option key={wave.waveId} value={wave.waveId}>{wave.profileName} · {wave.admittedCount} admitted · {formatAsOf(wave.openedAt)}</option>)}
          </select>
        </label>
        <label>
          Person who joined after the wave opened
          <input value={joiner} onChange={(event) => setJoiner(event.target.value)} required />
        </label>
        <button type="submit" className="draft-save" disabled={pending || !waveId}>Admit one</button>
      </form>
    </section>
  );
}
