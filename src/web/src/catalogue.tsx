import { useCallback, useEffect, useRef, useState } from "react";
import { NavLink, useParams } from "react-router-dom";
import {
  getApprovals,
  getCatalogueBenchmark,
  getCatalogueOverview,
  getCatalogueTaxonomy,
  getProfileRequests,
  getStandards,
  openProfileRequest,
  publishBenchmark,
  saveBenchmarkDraft,
  saveTaxonomyDraft,
  sendDraft,
  type CatalogueBenchmark,
  type CatalogueOverview,
  type CatalogueTaxonomy,
  type ProfileRequest,
  type SentDraft,
  type Standards,
} from "./api";
import { PackSubmission } from "./practice";
import { useTitle } from "./ui";

const screens = [
  { id: "overview", label: "Overview" },
  { id: "taxonomy", label: "Skill taxonomy" },
  { id: "workspace", label: "Benchmark workspace" },
  { id: "requests", label: "Profile requests" },
  { id: "approvals", label: "Approval queue" },
];

export function CatalogueHome() {
  const { screen } = useParams();
  const current = screens.some((item) => item.id === screen) ? screen! : "overview";
  return (
    <div className="home">
      <nav className="home-nav" aria-label="L and D">
        {screens.map((item) => (
          <NavLink key={item.id} to={`/workspace/${item.id}`} aria-current={current === item.id ? "page" : undefined}>
            {item.label}
          </NavLink>
        ))}
      </nav>
      <div className="home-main">
        {current === "taxonomy" ? <TaxonomyPage /> : null}
        {current === "workspace" ? <WorkspacePage /> : null}
        {current === "requests" ? <RequestsPage /> : null}
        {current === "approvals" ? <ApprovalsPage /> : null}
        {current === "overview" ? <OverviewPage /> : null}
      </div>
    </div>
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
  return (
    <section data-screen="overview">
      <p className="eyebrow">L&D</p>
      <h1>Overview</h1>
      <p>Readiness snapshots and the queues. Responses stay in the learning service.</p>
      {problem ? <Problem text={problem} onRetry={load} /> : null}
      {overview ? (
        <>
          <ul className="home-figures">
            <li><b>{overview.requestCount}</b><span>Profile requests</span></li>
            <li><b>{overview.approvalCount}</b><span>Sent for approval</span></li>
          </ul>
          {overview.snapshots.length === 0 ? <p>No practice snapshot has been written.</p> : null}
          {overview.snapshots.map((snapshot) => (
            <article key={snapshot.practiceCode}>
              <h2>{snapshot.practiceCode}</h2>
              <p>As of {snapshot.asOf}</p>
              <p>{snapshot.memberCount} members · {snapshot.measuredCount} measured · {snapshot.gapCount} below the bar · {snapshot.cohortCount} cohorts</p>
            </article>
          ))}
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
      <p className="eyebrow">L&D</p>
      <h1>Skill taxonomy</h1>
      <p>One taxonomy version. A draft edit stays in this authoring zone.</p>
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
      <p className="eyebrow">L&D</p>
      <h1>Benchmark workspace</h1>
      <p>Edit draft targets for one profile. Publish uses the command already built.</p>
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
      <p className="eyebrow">L&D</p>
      <h1>Profile requests</h1>
      <p>A request filed by a manager can become a draft profile. Opening it does not publish a benchmark.</p>
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
      <p className="eyebrow">L&D</p>
      <h1>Approval queue</h1>
      <p>Drafts L&D has sent. An unsent draft is not waiting on the competency lead.</p>
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
