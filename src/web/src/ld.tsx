import { useCallback, useEffect, useMemo, useState, type FormEvent } from "react";
import { NavLink } from "react-router-dom";
import {
  activateTaxonomySkill,
  admitRolling,
  admitWave,
  createCatalogueProfile,
  getActivationCentre,
  getCatalogueBoards,
  getStandards,
  openProfileRequest,
  publishBenchmark,
  saveBenchmarkDraft,
  saveTaxonomyDraft,
  sendDraft,
  setActivationBudget,
  useShell,
  type BoardProfile,
  type BoardSkill,
  type CatalogueBoard,
  type Standards,
} from "./api";
import { PackSubmission } from "./practice";
import { PageHead } from "./frame";
import { formatAsOf, useTitle } from "./ui";

function greeting(name: string) {
  const hour = new Date().getHours();
  const hello = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
  return `${hello}, ${name}.`;
}

function readyProfile(profile: BoardProfile) {
  return profile.skills.length > 0 && profile.skills.every((skill) => skill.levelSet && skill.rank !== null);
}

function missingLevels(profile: BoardProfile) {
  if (profile.skills.length === 0) return 0;
  return profile.skills.filter((skill) => !skill.levelSet || skill.rank === null).length;
}

export function OverviewPage() {
  const { shell } = useShell();
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const name = shell?.displayName?.trim() || "";
  useTitle(name ? greeting(name) : "Overview");
  const load = useCallback(async () => {
    setBoard(await getCatalogueBoards());
    setProblem(null);
  }, []);
  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The overview could not be loaded."));
  }, [load]);

  const profiles = board?.profiles ?? [];
  const readyProfiles = profiles.filter(readyProfile).length;
  const needing = profiles.length - readyProfiles;
  const newRequests = (board?.requests ?? []).filter((request) => request.requestStatus === "new" || request.requestStatus === "needs-context").length;
  const changes = (board?.competencies ?? []).filter((competency) => competency.approvalStatus === "changes-requested").length;
  const readyPacks = (board?.competencies ?? []).filter((competency) => {
    if (competency.approvalStatus !== "draft" || competency.profiles.length === 0) return false;
    return competency.profiles.every(readyProfile) && competency.profiles.some((profile) => profile.status === "draft" && !profile.sent);
  }).length;
  const skills = board?.taxonomy?.skills ?? [];
  const used = new Set(profiles.flatMap((profile) => profile.skills.map((skill) => skill.code)));
  const draftSkills = skills.filter((skill) => skill.status === "draft");
  const unused = skills.filter((skill) => !used.has(skill.code));
  const draftInUse = draftSkills.filter((skill) => used.has(skill.code));
  const variance = skills.filter((skill) => {
    const ranks = new Set(profiles.flatMap((profile) => profile.skills.filter((item) => item.code === skill.code && item.levelSet && item.rank !== null).map((item) => item.rank)));
    return ranks.size > 1;
  });
  const overlap = board?.overlaps[0];

  return (
    <section data-screen="overview">
      <PageHead
        eyebrow="L&D competency benchmarking"
        title={name ? greeting(name) : "Overview"}
        lede="See what is ready, what needs attention, and what can be sent to each competency lead."
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {board ? (
        <>
          <div className="stat-row">
            <NavLink to="/workspace/approvals">
              <span className="kicker">Competencies ready to send</span>
              <strong>{readyPacks}</strong>
              <span className="hint">Complete packs awaiting submission</span>
            </NavLink>
            <NavLink to="/workspace/workspace">
              <span className="kicker">Profiles needing benchmarks</span>
              <strong>{needing}</strong>
              <span className="hint">{readyProfiles} of {profiles.length} profiles complete</span>
            </NavLink>
            <NavLink to="/workspace/requests">
              <span className="kicker">New profile requests</span>
              <strong>{newRequests}</strong>
              <span className="hint">Need triage or additional context</span>
            </NavLink>
            <NavLink to="/workspace/approvals" className="ink">
              <span className="kicker">Changes requested</span>
              <strong>{changes}</strong>
              <span className="hint">Pack returned by a competency lead</span>
            </NavLink>
          </div>
          <div className="ld-split">
            <article className="surface-card">
              <header>
                <div>
                  <h2>Competency readiness</h2>
                  <p className="card-note">Default and requested profiles included</p>
                </div>
                <NavLink to="/workspace/approvals">View approval queue</NavLink>
              </header>
              {board.competencies.length === 0 ? <p className="empty-note">No competency is stored.</p> : null}
              {board.competencies.map((competency) => {
                const total = competency.profiles.length;
                const ready = competency.profiles.filter(readyProfile).length;
                const percent = total === 0 ? 0 : Math.round((ready / total) * 100);
                const requests = board.requests.filter((request) => request.competencyCode === competency.code && (request.requestStatus === "new" || request.requestStatus === "needs-context")).length;
                return (
                  <div className="readiness-row" key={competency.code}>
                    <div>
                      <strong>{competency.name}</strong>
                      <span>{competency.comLead || "No lead stored"} · competency lead</span>
                    </div>
                    <div>
                      <div className="meter-label"><span>{ready} of {total} profiles ready</span><b>{percent}%</b></div>
                      <div className="meter"><i style={{ width: `${percent}%` }} data-full={percent === 100 ? "true" : "false"} /></div>
                    </div>
                    <div className="pill-row">
                      <span className={competency.approvalStatus === "approved" || competency.approvalStatus === "review" ? "pill mint" : "pill amber"}>{statusLabel(competency.approvalStatus)}</span>
                      {requests > 0 ? <span className="pill grey">{requests} request</span> : null}
                    </div>
                    {competency.approvalStatus === "approved" ? (
                      <NavLink className="draft-save" to="/workspace/activation">Activate</NavLink>
                    ) : (
                      <NavLink className="draft-publish" to="/workspace/workspace">Open</NavLink>
                    )}
                  </div>
                );
              })}
            </article>
            <aside>
              <div className="aside-head">
                <div>
                  <h2>New profile requests</h2>
                  <p className="card-note">Across all competencies</p>
                </div>
                <NavLink to="/workspace/requests">View all</NavLink>
              </div>
              <div className="request-stack">
                {board.requests.slice(0, 3).map((request) => (
                  <NavLink key={request.requestId} to="/workspace/requests" className="request-mini">
                    <span className={request.requestStatus === "new" ? "pill amber" : "pill grey"}>{(request.requestStatus ?? "new").replace("-", " ")}</span>
                    <time>{formatAsOf(request.recordedAt)}</time>
                    <strong>{request.title || request.displayName}</strong>
                    <span>{request.competencyName || request.note}</span>
                  </NavLink>
                ))}
                {board.requests.length === 0 ? <p className="empty-note">No profile request is waiting.</p> : null}
              </div>
            </aside>
          </div>
          <div className="ld-split">
            <section className="surface-card">
              <header>
                <div>
                  <h2>What needs your attention</h2>
                  <p className="card-note">Built from open levels, requests, and approval status</p>
                </div>
              </header>
              {board.attention.length === 0 ? <p className="empty-note">Nothing is waiting on these rows.</p> : null}
              {board.attention.map((item) => (
                <NavLink key={`${item.tone}-${item.title}`} className="task-row" to={`/workspace/${item.screen}`}>
                  <span className={item.tone === "approved" ? "pill mint" : "pill amber"}>{item.tone}</span>
                  <span className="task-main"><strong>{item.title}</strong><small>{item.detail}</small></span>
                  <span>{item.action} →</span>
                </NavLink>
              ))}
            </section>
            <aside className="surface-card">
              <header>
                <div>
                  <h2>Taxonomy health</h2>
                  <p className="card-note">Secondary governance monitoring</p>
                </div>
                <NavLink to="/workspace/taxonomy">Open taxonomy</NavLink>
              </header>
              <div className="health-grid">
                <div><strong>{draftSkills.length}</strong><span>draft skills</span></div>
                <div><strong>{unused.length}</strong><span>unused skills</span></div>
                <div><strong>{draftInUse.length}</strong><span>draft skills in use</span></div>
                <div><strong>{variance.length}</strong><span>level variances</span></div>
              </div>
              {overlap ? (
                <div className="overlap">
                  <span className="pill amber">Possible overlap</span>
                  <strong>{overlap.title}</strong>
                  <p>{overlap.detail}</p>
                </div>
              ) : <p className="empty-note">No overlap note is stored.</p>}
            </aside>
          </div>
        </>
      ) : null}
    </section>
  );
}

function statusLabel(status: string) {
  if (status === "changes-requested") return "Changes requested";
  if (status === "review") return "With com-lead";
  if (status === "approved") return "Approved";
  return "Draft";
}

export function WorkspacePage() {
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [standards, setStandards] = useState<Standards | null>(null);
  const [competencyCode, setCompetencyCode] = useState("");
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState<"all" | "incomplete" | "ready">("all");
  const [selected, setSelected] = useState<string[]>([]);
  const [edits, setEdits] = useState<Record<string, BoardSkill[]>>({});
  const [dirty, setDirty] = useState<string[]>([]);
  const [modal, setModal] = useState<null | "profile" | "skill" | "taxonomy">(null);
  const [skillFor, setSkillFor] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("Benchmark profiles side by side.");
  const load = useCallback(async () => {
    const [next, catalogue] = await Promise.all([getCatalogueBoards(), getStandards()]);
    setBoard(next);
    setStandards(catalogue);
    setCompetencyCode((current) => current || next.competencies[0]?.code || "");
    setProblem(null);
  }, []);
  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The workspace could not be loaded."));
  }, [load]);

  const competency = board?.competencies.find((item) => item.code === competencyCode) ?? board?.competencies[0];
  const profiles = useMemo(() => {
    const source = competency ? board?.profiles.filter((profile) => profile.competencyCode === competency.code) ?? [] : board?.profiles ?? [];
    const query = search.trim().toLowerCase();
    return source.filter((profile) => {
      const match = filter === "all" || (filter === "ready" ? readyProfile(profile) : !readyProfile(profile));
      const text = `${profile.name} ${profile.band} ${profile.family}`.toLowerCase();
      return match && (!query || text.includes(query));
    });
  }, [board, competency, filter, search]);
  const open = (board?.profiles ?? []).filter((profile) => selected.includes(profile.profileId) && (!competency || profile.competencyCode === competency.code));
  const skillsOf = (profile: BoardProfile) => edits[profile.profileId] ?? profile.skills;
  const missing = open.reduce((total, profile) => total + missingLevels({ ...profile, skills: skillsOf(profile) }), 0);

  function toggle(profileId: string) {
    setSelected((current) => {
      if (current.includes(profileId)) return current.filter((id) => id !== profileId);
      if (current.length >= 4) return current;
      return [...current, profileId];
    });
  }

  function setRank(profile: BoardProfile, skillId: string, rank: number | null) {
    const skills = skillsOf(profile).map((skill) => skill.skillId === skillId ? { ...skill, rank, levelSet: rank !== null } : skill);
    setEdits((current) => ({ ...current, [profile.profileId]: skills }));
    setDirty((current) => current.includes(profile.profileId) ? current : [...current, profile.profileId]);
  }

  function addSkill(profile: BoardProfile, skillId: string) {
    const taxonomySkill = board?.taxonomy?.skills.find((skill) => skill.skillId === skillId);
    if (!taxonomySkill) return;
    const skills = [...skillsOf(profile), { skillId, code: taxonomySkill.code, name: taxonomySkill.name, category: taxonomySkill.category, rank: null, levelSet: false, suggestedRank: board?.taxonomy?.proficiencies[0]?.rank ?? null }];
    setEdits((current) => ({ ...current, [profile.profileId]: skills }));
    setDirty((current) => current.includes(profile.profileId) ? current : [...current, profile.profileId]);
    setModal(null);
  }

  async function saveAll() {
    if (!board) return;
    setPending(true);
    try {
      for (const profileId of dirty) {
        const profile = board.profiles.find((item) => item.profileId === profileId);
        if (!profile) continue;
        const skills = skillsOf(profile).filter((skill) => skill.rank !== null);
        if (skills.length === 0 || skillsOf(profile).some((skill) => skill.rank === null)) {
          throw new Error(`${profile.name} still has a level to set.`);
        }
        const taxonomyVersionId = profile.taxonomyVersionId || (board.taxonomy?.status === "published" ? board.taxonomy.taxonomyVersionId : "");
        if (!taxonomyVersionId) throw new Error("A benchmark is pinned to a published taxonomy version.");
        await saveBenchmarkDraft({
          jobProfileId: profile.profileId,
          taxonomyVersionId,
          targets: skills.map((skill) => ({ skillId: skill.skillId, proficiencyRank: skill.rank as number })),
        });
      }
      setDirty([]);
      setEdits({});
      await load();
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The drafts could not be saved.");
    } finally {
      setPending(false);
    }
  }

  const readyCount = (competency?.profiles ?? []).filter(readyProfile).length;
  const totalCount = competency?.profiles.length ?? 0;
  const percent = totalCount === 0 ? 0 : Math.round((readyCount / totalCount) * 100);
  const requestCount = (board?.requests ?? []).filter((request) => request.competencyCode === competency?.code).length;

  return (
    <section data-screen="workspace">
      <PageHead
        eyebrow="Multi-profile benchmarking"
        title="Benchmark profiles side by side."
        lede="Select up to four profiles, set skill levels, and save all changes together."
        action={(
          <div className="head-actions">
            <label>
              Competency
              <select value={competency?.code ?? ""} onChange={(event) => { setCompetencyCode(event.target.value); setSelected([]); }}>
                {(board?.competencies ?? []).map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
              </select>
            </label>
            <button type="button" className="draft-save" onClick={() => setModal("profile")}>New profile</button>
            <NavLink to="/workspace/market">Market demand</NavLink>
          </div>
        )}
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {competency ? (
        <div className="progress-strip">
          <strong>{competency.name}</strong>
          <span>{readyCount} of {totalCount} profiles benchmarked</span>
          <div className="meter"><i style={{ width: `${percent}%` }} /></div>
          <b>{percent}%</b>
          {requestCount > 0 ? <NavLink to="/workspace/requests">{requestCount} profile request{requestCount === 1 ? "" : "s"}</NavLink> : null}
        </div>
      ) : null}
      <div className="composer">
        <aside>
          <label>
            Search profiles
            <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search profiles…" />
          </label>
          <div className="filter-row" role="group" aria-label="Profile status filter">
            {(["all", "incomplete", "ready"] as const).map((item) => (
              <button key={item} type="button" className={filter === item ? "is-on" : undefined} onClick={() => setFilter(item)}>{item}</button>
            ))}
          </div>
          <p className="card-note">{profiles.length} profiles · select up to 4</p>
          {profiles.map((profile) => {
            const on = selected.includes(profile.profileId);
            const progress = profile.skills.length;
            const done = profile.skills.filter((skill) => skill.levelSet).length;
            return (
              <button key={profile.profileId} type="button" className={on ? "picker is-on" : "picker"} onClick={() => toggle(profile.profileId)}>
                <span aria-hidden="true">{on ? "✓" : ""}</span>
                <span><strong>{profile.name}</strong><small>{done}/{progress} skills</small></span>
                <i data-ready={readyProfile(profile) ? "true" : "false"} />
              </button>
            );
          })}
        </aside>
        <div>
          <header className="composer-bar"><strong>{open.length} profiles open</strong><span>Edit levels directly in each card</span></header>
          <div className="card-row">
            {open.length === 0 ? <p className="empty-note">Select a profile. Choose profiles from the left to work on them together.</p> : null}
            {open.map((profile) => {
              const skills = skillsOf(profile);
              const unsaved = dirty.includes(profile.profileId);
              return (
                <article key={profile.profileId} className={unsaved ? "profile-card is-dirty" : "profile-card"}>
                  <header>
                    <div>
                      <span>{profile.band || profile.levelName} · {profile.family || profile.frameworkName}</span>
                      <h2>{profile.name}</h2>
                    </div>
                    <button type="button" aria-label={`Remove ${profile.name} from workspace`} onClick={() => toggle(profile.profileId)}>×</button>
                  </header>
                  <p>{profile.purpose || "No purpose is stored."}</p>
                  <div className="pill-row">
                    <span className={readyProfile({ ...profile, skills }) ? "pill mint" : "pill amber"}>{readyProfile({ ...profile, skills }) ? "Benchmark ready" : `${missingLevels({ ...profile, skills })} levels missing`}</span>
                    {unsaved ? <span>Unsaved</span> : null}
                  </div>
                  {skills.map((skill) => (
                    <label key={skill.skillId} className="level-row">
                      <span><strong>{skill.code}</strong> {skill.name}<small>{skill.category || "Taxonomy skill"}{skill.rank === null && skill.suggestedRank ? ` · suggested L${skill.suggestedRank}` : ""}</small></span>
                      <select aria-label={`Target level for ${skill.name}`} value={skill.rank ?? ""} onChange={(event) => setRank(profile, skill.skillId, event.target.value === "" ? null : Number(event.target.value))}>
                        <option value="">Set</option>
                        {(board?.taxonomy?.proficiencies ?? []).map((item) => <option key={item.rank} value={item.rank}>L{item.rank}</option>)}
                      </select>
                    </label>
                  ))}
                  <footer>
                    <button type="button" onClick={() => { setSkillFor(profile.profileId); setModal("skill"); }}>Add benchmark skill</button>
                    {profile.benchmarkVersionId && profile.status === "draft" ? (
                      <button
                        type="button"
                        disabled={pending}
                        onClick={() => {
                          setPending(true);
                          void publishBenchmark(profile.benchmarkVersionId as string)
                            .then(load)
                            .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The benchmark could not be published."))
                            .finally(() => setPending(false));
                        }}
                      >
                        Publish version
                      </button>
                    ) : null}
                  </footer>
                </article>
              );
            })}
          </div>
          <div className="save-bar">
            <div>
              <strong>{missing === 0 ? "Selected profiles are complete" : `${missing} benchmark levels missing`}</strong>
              <span>{dirty.length} profile{dirty.length === 1 ? "" : "s"} with unsaved changes</span>
            </div>
            <button type="button" className="draft-save" disabled={pending || dirty.length === 0} onClick={() => void saveAll()}>Save all changes</button>
            <NavLink to="/workspace/approvals">Review approval readiness</NavLink>
          </div>
        </div>
      </div>
      <PackSubmission />
      {modal === "profile" ? (
        <ProfileDialog
          standards={standards}
          competencies={board?.competencies ?? []}
          pending={pending}
          onClose={() => setModal(null)}
          onCreate={(body) => {
            setPending(true);
            void createCatalogueProfile(body)
              .then(load)
              .then(() => setModal(null))
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The profile was not created."))
              .finally(() => setPending(false));
          }}
          onTaxonomy={() => setModal("taxonomy")}
        />
      ) : null}
      {modal === "skill" ? (
        <dialog open className="ld-dialog">
          <form method="dialog">
            <h2>Add a benchmark skill</h2>
            <ul>
              {(board?.taxonomy?.skills ?? []).filter((skill) => {
                const profile = board?.profiles.find((item) => item.profileId === skillFor);
                return profile ? !skillsOf(profile).some((item) => item.skillId === skill.skillId) : false;
              }).map((skill) => (
                <li key={skill.skillId}>
                  <button type="button" onClick={() => {
                    const profile = board?.profiles.find((item) => item.profileId === skillFor);
                    if (profile) addSkill(profile, skill.skillId);
                  }}>{skill.name}<small>{skill.category}</small></button>
                </li>
              ))}
            </ul>
            <button type="button" onClick={() => setModal("taxonomy")}>Create a taxonomy skill</button>
            <button type="button" onClick={() => setModal(null)}>Close</button>
          </form>
        </dialog>
      ) : null}
      {modal === "taxonomy" && board?.taxonomy ? (
        <TaxonomyDialog
          board={board}
          pending={pending}
          onClose={() => setModal(null)}
          onSave={(body) => {
            setPending(true);
            void saveTaxonomyDraft(body)
              .then(load)
              .then(() => setModal(null))
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That draft was not saved."))
              .finally(() => setPending(false));
          }}
        />
      ) : null}
    </section>
  );
}

function ProfileDialog({
  standards,
  competencies,
  pending,
  onClose,
  onCreate,
  onTaxonomy,
}: {
  standards: Standards | null;
  competencies: CatalogueBoard["competencies"];
  pending: boolean;
  onClose: () => void;
  onCreate: (body: { profileName: string; frameworkCode: string; levelCode: string; purpose: string; band: string; family: string; competencyCode: string }) => void;
  onTaxonomy: () => void;
}) {
  const [name, setName] = useState("");
  const [frameworkCode, setFrameworkCode] = useState(standards?.frameworks[0]?.code ?? "");
  const framework = standards?.frameworks.find((item) => item.code === frameworkCode) ?? standards?.frameworks[0];
  const [levelCode, setLevelCode] = useState(framework?.levels[0]?.code ?? "");
  const [band, setBand] = useState("");
  const [family, setFamily] = useState("");
  const [purpose, setPurpose] = useState("");
  const [competencyCode, setCompetencyCode] = useState(competencies[0]?.code ?? "");
  return (
    <dialog open className="ld-dialog">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          onCreate({ profileName: name.trim(), frameworkCode, levelCode, purpose, band, family, competencyCode });
        }}
      >
        <h2>Add a profile to benchmark</h2>
        <label>Competency<select value={competencyCode} onChange={(event) => setCompetencyCode(event.target.value)}>{competencies.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label>
        <label>Profile name<input value={name} onChange={(event) => setName(event.target.value)} required /></label>
        <label>Band<input value={band} onChange={(event) => setBand(event.target.value)} required /></label>
        <label>Family<input value={family} onChange={(event) => setFamily(event.target.value)} /></label>
        <label>Purpose<textarea value={purpose} onChange={(event) => setPurpose(event.target.value)} /></label>
        <label>Framework<select value={frameworkCode} onChange={(event) => { setFrameworkCode(event.target.value); const next = standards?.frameworks.find((item) => item.code === event.target.value); setLevelCode(next?.levels[0]?.code ?? ""); }}>{(standards?.frameworks ?? []).map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label>
        <label>Level<select value={levelCode} onChange={(event) => setLevelCode(event.target.value)}>{(framework?.levels ?? []).map((level) => <option key={level.code} value={level.code}>{level.name}</option>)}</select></label>
        <button type="button" onClick={onTaxonomy}>Skill not in the taxonomy?</button>
        <div className="dialog-actions">
          <button type="button" onClick={onClose}>Cancel</button>
          <button type="submit" className="draft-save" disabled={pending || name.trim().length === 0}>Create benchmark draft</button>
        </div>
      </form>
    </dialog>
  );
}

function TaxonomyDialog({
  board,
  pending,
  onClose,
  onSave,
}: {
  board: CatalogueBoard;
  pending: boolean;
  onClose: () => void;
  onSave: (body: { practices: { code: string; name: string; competencies: { code: string; name: string; skills: { code: string; name: string }[] }[] }[]; proficiencies: { code: string; name: string; rank: number }[] }) => void;
}) {
  const [code, setCode] = useState("");
  const [name, setName] = useState("");
  return (
    <dialog open className="ld-dialog">
      <form
        onSubmit={(event) => {
          event.preventDefault();
          const taxonomy = board.taxonomy;
          if (!taxonomy) return;
          const practices = new Map<string, { code: string; name: string; competencies: Map<string, { code: string; name: string; skills: { code: string; name: string }[] }> }>();
          for (const skill of taxonomy.skills) {
            const practice = practices.get(skill.practiceCode) ?? { code: skill.practiceCode, name: skill.practiceName, competencies: new Map() };
            const competency = practice.competencies.get(skill.competencyCode) ?? { code: skill.competencyCode, name: skill.competencyName, skills: [] };
            competency.skills.push({ code: skill.code, name: skill.name });
            practice.competencies.set(skill.competencyCode, competency);
            practices.set(skill.practiceCode, practice);
          }
          const practice = [...practices.values()][0];
          const competency = practice ? [...practice.competencies.values()][0] : undefined;
          if (practice && competency) competency.skills.push({ code: code.trim(), name: name.trim() });
          onSave({
            practices: [...practices.values()].map((item) => ({ code: item.code, name: item.name, competencies: [...item.competencies.values()] })),
            proficiencies: taxonomy.proficiencies.map((item) => ({ code: item.code, name: item.name, rank: item.rank })),
          });
        }}
      >
        <h2>New taxonomy skill</h2>
        <p className="card-note">The draft command stores the skill code and name on the taxonomy version.</p>
        <label>Skill code<input value={code} onChange={(event) => setCode(event.target.value)} required /></label>
        <label>Skill name<input value={name} onChange={(event) => setName(event.target.value)} required /></label>
        <div className="dialog-actions">
          <button type="button" onClick={onClose}>Cancel</button>
          <button type="submit" className="draft-save" disabled={pending}>Save draft</button>
        </div>
      </form>
    </dialog>
  );
}

export function TaxonomyPage() {
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("all");
  const [selected, setSelected] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [draftCode, setDraftCode] = useState("");
  const [draftName, setDraftName] = useState("");
  useTitle("Create once. Reuse across profiles.");
  const load = useCallback(async () => {
    setBoard(await getCatalogueBoards());
    setProblem(null);
  }, []);
  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The taxonomy could not be loaded."));
  }, [load]);
  const skills = board?.taxonomy?.skills ?? [];
  const categories = [...new Set(skills.map((skill) => skill.category).filter((item) => item.length > 0))];
  const filtered = skills.filter((skill) => {
    const query = search.trim().toLowerCase();
    const categoryMatch = category === "all" || skill.category === category;
    const text = `${skill.name} ${skill.category} ${skill.definition}`.toLowerCase();
    return categoryMatch && (!query || text.includes(query));
  });
  const current = filtered.find((skill) => skill.skillId === selected) ?? filtered[0];
  const usages = (board?.profiles ?? []).flatMap((profile) => profile.skills.filter((skill) => current && skill.code === current.code).map((skill) => ({ profile, skill })));
  return (
    <section data-screen="taxonomy">
      <PageHead
        eyebrow="Shared skill language"
        title="Create once. Reuse across profiles."
        lede="The taxonomy keeps skill names, definitions, and proficiency meaning consistent. Job profiles reference these shared skills and set only the target level."
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      <div className="stat-row cols-3">
        <div><span className="kicker">Taxonomy skills</span><strong>{skills.length}</strong></div>
        <div><span className="kicker">Skill categories</span><strong>{categories.length}</strong></div>
        <div><span className="kicker">Draft definitions</span><strong>{skills.filter((skill) => skill.status === "draft").length}</strong></div>
      </div>
      <div className="composer">
        <aside>
          <label>Search skill taxonomy<input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search skills, categories, definitions…" /></label>
          <div className="filter-row">
            <button type="button" className={category === "all" ? "is-on" : undefined} onClick={() => setCategory("all")}>All</button>
            {categories.map((item) => <button key={item} type="button" className={category === item ? "is-on" : undefined} onClick={() => setCategory(item)}>{item}</button>)}
          </div>
          <p className="card-note">{filtered.length} skills</p>
          {filtered.length === 0 ? <p className="empty-note">No taxonomy skills match this filter.</p> : null}
          {filtered.map((skill) => {
            const count = (board?.profiles ?? []).filter((profile) => profile.skills.some((item) => item.code === skill.code)).length;
            return (
              <button key={skill.skillId} type="button" className={current?.skillId === skill.skillId ? "picker is-on" : "picker"} onClick={() => setSelected(skill.skillId)}>
                <span><strong>{skill.code}</strong> {skill.name}<small>{skill.category || "Uncategorised"} · {count} profile{count === 1 ? "" : "s"}</small></span>
                <em className={skill.status === "active" ? "pill mint" : "pill amber"}>{skill.status}</em>
              </button>
            );
          })}
        </aside>
        <div className="taxonomy-detail">
          {current ? (
            <>
              <header>
                <div>
                  <span className="pill mint">{current.category || current.competencyName}</span>
                  <span className="pill grey">Shared proficiency model</span>
                  <h2>{current.name}</h2>
                  <p>{current.definition || "No definition is stored."}</p>
                </div>
                <div>
                  <span className={current.status === "active" ? "pill mint" : "pill amber"}>{current.status}</span>
                  {current.status === "draft" ? (
                    <button type="button" className="draft-publish" disabled={pending} onClick={() => {
                      setPending(true);
                      void activateTaxonomySkill(current.code).then(load).catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The skill was not marked active.")).finally(() => setPending(false));
                    }}>Mark active</button>
                  ) : null}
                </div>
              </header>
              <h3>One meaning wherever the skill is used</h3>
              {(board?.taxonomy?.proficiencies ?? []).map((level) => (
                <div className="level-meaning" key={level.rank}><strong>L{level.rank}</strong><span>{level.name}</span><p>{level.meaning || "No shared meaning is stored."}</p></div>
              ))}
              <h3>Used in {usages.length} profile{usages.length === 1 ? "" : "s"}</h3>
              <div className="pill-row">
                {usages.length === 0 ? <span>Not mapped to a profile yet.</span> : usages.map(({ profile, skill }) => (
                  <NavLink key={profile.profileId} to="/workspace/workspace">{profile.name} {skill.rank ? `L${skill.rank}` : "Level not set"}</NavLink>
                ))}
              </div>
            </>
          ) : <p className="empty-note">No skill selected. Choose a taxonomy skill to inspect its shared definition.</p>}
          {board?.taxonomy ? (
            <form className="task-close" onSubmit={(event: FormEvent) => {
              event.preventDefault();
              const taxonomy = board.taxonomy;
              if (!taxonomy) return;
              const practices = new Map<string, { code: string; name: string; competencies: Map<string, { code: string; name: string; skills: { code: string; name: string }[] }> }>();
              for (const skill of taxonomy.skills) {
                const practice = practices.get(skill.practiceCode) ?? { code: skill.practiceCode, name: skill.practiceName, competencies: new Map() };
                const competency = practice.competencies.get(skill.competencyCode) ?? { code: skill.competencyCode, name: skill.competencyName, skills: [] };
                competency.skills.push({ code: skill.code, name: skill.name });
                practice.competencies.set(skill.competencyCode, competency);
                practices.set(skill.practiceCode, practice);
              }
              const host = [...practices.values()][0];
              const competency = host ? [...host.competencies.values()][0] : undefined;
              if (competency) competency.skills.push({ code: draftCode.trim(), name: draftName.trim() });
              setPending(true);
              void saveTaxonomyDraft({
                practices: [...practices.values()].map((item) => ({ code: item.code, name: item.name, competencies: [...item.competencies.values()] })),
                proficiencies: taxonomy.proficiencies.map((item) => ({ code: item.code, name: item.name, rank: item.rank })),
              }).then(() => { setDraftCode(""); setDraftName(""); return load(); }).catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That draft was not saved.")).finally(() => setPending(false));
            }}>
              <h2>Draft edit</h2>
              <label>Skill code<input value={draftCode} onChange={(event) => setDraftCode(event.target.value)} required /></label>
              <label>Skill name<input value={draftName} onChange={(event) => setDraftName(event.target.value)} required /></label>
              <button type="submit" className="draft-save" disabled={pending}>Save draft</button>
            </form>
          ) : <p className="empty-note">No taxonomy version is in this zone.</p>}
        </div>
      </div>
    </section>
  );
}

export function RequestsPage() {
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [standards, setStandards] = useState<Standards | null>(null);
  const [requestId, setRequestId] = useState("");
  const [profileName, setProfileName] = useState("");
  const [frameworkCode, setFrameworkCode] = useState("");
  const [levelCode, setLevelCode] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("New profile requests in one place.");
  const load = useCallback(async () => {
    const [next, catalogue] = await Promise.all([getCatalogueBoards(), getStandards()]);
    setBoard(next);
    setStandards(catalogue);
    setFrameworkCode((current) => current || catalogue.frameworks[0]?.code || "");
    setLevelCode((current) => current || catalogue.frameworks[0]?.levels[0]?.code || "");
    setProblem(null);
  }, []);
  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "Requests could not be loaded."));
  }, [load]);
  const framework = standards?.frameworks.find((item) => item.code === frameworkCode) ?? standards?.frameworks[0];
  const requests = board?.requests ?? [];
  return (
    <section data-screen="requests">
      <PageHead
        eyebrow="Requests from competencies"
        title="New profile requests in one place."
        lede="Review the context, then open or create the benchmark directly in the shared workspace."
        action={<NavLink className="draft-save" to="/workspace/workspace">Create without a request</NavLink>}
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {requests.length === 0 ? <p className="empty-note">No profile request is waiting.</p> : null}
      {requests.map((request) => {
        const profile = board?.profiles.find((item) => item.profileId === request.profileId) ?? null;
        const done = profile ? profile.skills.filter((skill) => skill.levelSet).length : 0;
        const total = profile?.skills.length ?? 0;
        const label = profile ? (readyProfile(profile) ? "Benchmark ready" : "Benchmark in progress") : request.requestStatus === "needs-context" ? "Needs context" : request.requestStatus === "waiting" ? "Waiting on com-lead" : "New request";
        const action = profile ? (readyProfile(profile) ? "View benchmark" : "Continue benchmark") : "Open as draft";
        return (
          <article key={request.requestId} className="request-card">
            <div>
              <span className={label === "Benchmark ready" ? "pill mint" : label === "New request" || label === "Benchmark in progress" ? "pill amber" : "pill grey"}>{label}</span>
              <h2>{request.title || request.displayName}</h2>
              <p>{request.competencyName || "Competency"} · {formatAsOf(request.recordedAt)}</p>
            </div>
            <div>
              <p>{request.note}</p>
              {request.evidence ? <p>{request.evidence}</p> : null}
              <p>Requested by {request.managerEmployeeId}</p>
            </div>
            <div>
              {profile ? <p>{done} of {total} skill levels set</p> : null}
              {profile ? <NavLink className="draft-save" to="/workspace/workspace">{action}</NavLink> : (
                <button type="button" className="draft-save" onClick={() => { setRequestId(request.requestId); setProfileName(request.title || request.displayName); }}>{action}</button>
              )}
            </div>
          </article>
        );
      })}
      {requests.length > 0 ? (
        <form className="task-close" onSubmit={(event) => {
          event.preventDefault();
          setPending(true);
          void openProfileRequest(requestId, { profileName, frameworkCode, levelCode })
            .then(() => { setProfileName(""); return load(); })
            .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That request was not opened."))
            .finally(() => setPending(false));
        }}>
          <h2>Open as a draft profile</h2>
          <label>Request<select value={requestId} onChange={(event) => setRequestId(event.target.value)}>{requests.map((request) => <option key={request.requestId} value={request.requestId}>{request.title || request.displayName}</option>)}</select></label>
          <label>Profile name<input value={profileName} onChange={(event) => setProfileName(event.target.value)} required /></label>
          <label>Framework<select value={frameworkCode} onChange={(event) => { setFrameworkCode(event.target.value); const next = standards?.frameworks.find((item) => item.code === event.target.value); setLevelCode(next?.levels[0]?.code || ""); }}>{(standards?.frameworks ?? []).map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label>
          <label>Level<select value={levelCode} onChange={(event) => setLevelCode(event.target.value)}>{(framework?.levels ?? []).map((level) => <option key={level.code} value={level.code}>{level.name}</option>)}</select></label>
          <button type="submit" className="draft-save" disabled={pending || profileName.trim().length === 0 || requestId.length === 0}>Open draft</button>
        </form>
      ) : null}
    </section>
  );
}

export function ApprovalsPage() {
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  useTitle("Send only complete competency packs.");
  const load = useCallback(async () => {
    setBoard(await getCatalogueBoards());
    setProblem(null);
  }, []);
  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The approval queue could not be loaded."));
  }, [load]);
  return (
    <section data-screen="approvals">
      <PageHead
        eyebrow="Com-lead handoff"
        title="Send only complete competency packs."
        lede="A pack becomes available as soon as every included profile has a benchmark level for each skill."
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      <div className="team-scroll">
        <table className="team-table">
          <thead>
            <tr><th>Competency</th><th>Benchmark readiness</th><th>Status</th><th>Action</th></tr>
          </thead>
          <tbody>
            {(board?.competencies ?? []).map((competency) => {
              const total = competency.profiles.length;
              const ready = competency.profiles.filter(readyProfile).length;
              const percent = total === 0 ? 0 : Math.round((ready / total) * 100);
              const remaining = competency.profiles.filter((profile) => !readyProfile(profile)).length;
              const unsent = competency.profiles.filter((profile) => profile.status === "draft" && !profile.sent && profile.benchmarkVersionId);
              const sent = competency.profiles.find((profile) => profile.sent);
              const approved = competency.approvalStatus === "approved";
              const changes = competency.approvalStatus === "changes-requested";
              const withLead = competency.approvalStatus === "review" || (Boolean(sent) && unsent.length === 0 && remaining === 0);
              const readyToSend = !approved && !changes && !withLead && remaining === 0 && unsent.length > 0;
              const label = approved ? "Approved" : changes ? "Changes requested" : withLead ? "With com-lead" : readyToSend ? "Ready to send" : `${remaining} incomplete`;
              return (
                <tr key={competency.code}>
                  <td><strong>{competency.name}</strong><span className="team-id">{competency.comLead || "No lead stored"} · competency lead</span></td>
                  <td>
                    <div className="meter-label"><span>{ready} / {total} profiles</span><b>{percent}%</b></div>
                    <div className="meter"><i style={{ width: `${percent}%` }} /></div>
                  </td>
                  <td><span className={approved || withLead || readyToSend ? "pill mint" : "pill amber"}>{label}</span>{sent?.sentAt ? <span className="team-id">{formatAsOf(sent.sentAt)}</span> : null}</td>
                  <td>
                    {approved ? <NavLink to="/workspace/activation">Activate baseline</NavLink> : null}
                    {changes || (!approved && !withLead && !readyToSend) ? <NavLink to="/workspace/workspace">{changes ? "Resolve changes" : "Finish profiles"}</NavLink> : null}
                    {withLead && !approved ? <span>Sent</span> : null}
                    {readyToSend ? (
                      <button type="button" className="draft-save" disabled={pending} onClick={() => {
                        setPending(true);
                        void Promise.all(unsent.map((profile) => sendDraft(profile.benchmarkVersionId as string)))
                          .then(load)
                          .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That draft was not sent."))
                          .finally(() => setPending(false));
                      }}>Send to com-lead</button>
                    ) : null}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      {(board?.competencies.length ?? 0) === 0 ? <p className="empty-note">No competency pack is stored.</p> : null}
      <aside className="handoff">
        <p>Simple handoff rule</p>
        <h2>Complete profiles move together. Incomplete profiles stay with L&D.</h2>
        <p>Competency leads receive the profile purpose, benchmark skills, target levels, and change rationale.</p>
        <span>L&D prepares · competency lead approves</span>
      </aside>
    </section>
  );
}

export function ActivationPage() {
  useTitle("Activate the approved benchmark.");
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [centre, setCentre] = useState<Awaited<ReturnType<typeof getActivationCentre>> | null>(null);
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
    const [nextBoard, nextCentre, standards] = await Promise.all([getCatalogueBoards(), getActivationCentre(), getStandards()]);
    setBoard(nextBoard);
    setCentre(nextCentre);
    const published = standards.history.filter((item) => item.status === "published");
    setVersions(published.map((item) => ({ id: item.benchmarkVersionId, name: item.profileName })));
    setVersionId((current) => current || published[0]?.benchmarkVersionId || "");
    setWaveId((current) => current || nextCentre.waves[0]?.waveId || "");
    setBudget(nextCentre.sittingBudget === null ? "" : String(nextCentre.sittingBudget));
  }, []);
  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The activation centre could not be loaded."));
  }, [load]);
  const waves = centre?.waves ?? [];
  const admitted = waves.reduce((total, wave) => total + wave.admittedCount, 0);
  const population = waves.reduce((total, wave) => total + wave.population, 0);
  const published = (board?.baselines.length ?? 0) > 0;
  const mapped = waves.length > 0 && waves.every((wave) => wave.admittedCount >= wave.population);
  const launched = waves.length > 0;
  const snapshot = board?.snapshots[0];
  const approved = board?.competencies.find((competency) => competency.approvalStatus === "approved");
  const chipName = approved?.name || board?.baselines[0]?.profileName || "No baseline stored";
  function run(work: () => Promise<unknown>, done: string) {
    setPending(true);
    setNotice(null);
    void work().then(load).then(() => { setProblem(null); setNotice(done); }).catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The activation centre refused that.")).finally(() => setPending(false));
  }
  return (
    <section data-screen="activation-centre">
      <PageHead
        eyebrow="Post-approval activation"
        title="Activate the approved benchmark."
        lede="Publish the approved baseline, map the population, launch evidence-based assessment, and monitor readiness from the stored snapshot."
        action={<div className="approved-chip"><span>Published baseline</span><strong>{chipName}</strong></div>}
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      <ol className="step-track">
        <li data-state={published ? "done" : "current"}><span>01</span><strong>Publish baseline</strong><small>{published ? "Complete" : "Your next step"}</small></li>
        <li data-state={mapped ? "done" : published ? "current" : "wait"}><span>02</span><strong>Map population</strong><small>{mapped ? "Complete" : published ? "Your next step" : "Waiting"}</small></li>
        <li data-state={launched ? "done" : mapped ? "current" : "wait"}><span>03</span><strong>Launch assessment</strong><small>{launched ? "Complete" : mapped ? "Your next step" : "Waiting"}</small></li>
        <li data-state={snapshot ? "done" : launched ? "current" : "wait"}><span>04</span><strong>Readiness snapshot</strong><small>{snapshot ? "Complete" : launched ? "Your next step" : "Waiting"}</small></li>
      </ol>
      <article className="surface-card step-card">
        <div>
          <span className={published ? "pill mint" : "pill amber"}>{published ? "Published" : "Not live"}</span>
          <h2>Publish the approved baseline</h2>
          <p>{board?.baselines.length ?? 0} published profile versions are effective for assessment.</p>
          <ul>{(board?.baselines ?? []).map((baseline) => <li key={`${baseline.profileName}-${baseline.versionNumber}`}><strong>{baseline.profileName}</strong> version {baseline.versionNumber}{baseline.publishedAt ? ` · ${formatAsOf(baseline.publishedAt)}` : ""}{baseline.comLead ? ` · ${baseline.comLead}` : ""}</li>)}</ul>
        </div>
        {published ? <p>Baseline versions already published stay immutable. A newer draft is published from the benchmark workspace.</p> : <p>No published benchmark is stored.</p>}
      </article>
      <article className="surface-card step-card">
        <h2>Map people to approved profiles</h2>
        <p>{admitted} / {population || admitted} mapped from open waves.</p>
        {mapped ? <p className="empty-note">All {population} people are mapped.</p> : waves.length === 0 ? <p className="empty-note">No wave is open in this zone.</p> : null}
        <ul>
          {waves.flatMap((wave) => wave.subjectIds.map((subjectId) => (
            <li key={`${wave.waveId}-${subjectId}`}><strong>{subjectId}</strong><span>{wave.profileName}</span></li>
          )))}
        </ul>
      </article>
      <article className="surface-card step-card">
        <h2>Launch evidence-based assessment</h2>
        <p>{board?.cycle?.cycleName ?? "No cycle name is stored."}{board?.cycle?.dueOn ? ` · due ${board.cycle.dueOn}` : ""}</p>
        <div className="source-row">
          <label><input type="checkbox" checked={board?.cycle?.selfAssessment ?? false} disabled readOnly /> Self-assessment</label>
          <label><input type="checkbox" checked={board?.cycle?.managerValidation ?? false} disabled readOnly /> Manager validation</label>
          <label><input type="checkbox" checked={board?.cycle?.workEvidence ?? false} disabled readOnly /> Work evidence</label>
        </div>
        <form onSubmit={(event) => {
          event.preventDefault();
          const subjectIds = subjects.split(/\s+/).map((item) => item.trim()).filter((item) => item.length > 0);
          if (!versionId || subjectIds.length === 0) {
            setProblem("Choose a published benchmark and name the people to launch.");
            return;
          }
          run(() => admitWave(versionId, subjectIds), "The wave is open.");
        }}>
          <h3>Launch</h3>
          <label>Published benchmark<select value={versionId} onChange={(event) => setVersionId(event.target.value)}>{versions.map((version) => <option key={version.id} value={version.id}>{version.name}</option>)}</select></label>
          <label>People, one identifier per line<textarea value={subjects} onChange={(event) => setSubjects(event.target.value)} /></label>
          <button type="submit" className="draft-publish" disabled={pending || !versionId}>Launch assessment cycle</button>
        </form>
        {snapshot ? <p>Assessment progress from the snapshot: {snapshot.measuredCount} measured, {snapshot.gapCount} below the bar, {snapshot.memberCount} members.</p> : <p>No practice snapshot is stored yet.</p>}
      </article>
      <section>
        <h2>Readiness snapshot</h2>
        {snapshot ? (
          <div className="stat-row">
            <div><span className="kicker">Measured</span><strong>{snapshot.measuredCount}</strong><span className="hint">of {snapshot.memberCount} members</span></div>
            <div><span className="kicker">Not yet measured</span><strong>{Math.max(snapshot.memberCount - snapshot.measuredCount, 0)}</strong></div>
            <div><span className="kicker">Material gaps</span><strong>{snapshot.gapCount}</strong></div>
            <div><span className="kicker">Cohorts</span><strong>{snapshot.cohortCount}</strong></div>
          </div>
        ) : <p className="empty-note">Launch the assessment to calculate readiness.</p>}
      </section>
      <form className="task-close" onSubmit={(event) => {
        event.preventDefault();
        const sittingBudget = Number(budget);
        if (!Number.isInteger(sittingBudget) || sittingBudget < 1) {
          setProblem("The zone budget is a whole number of at least one sitting.");
          return;
        }
        run(() => setActivationBudget(sittingBudget), "The zone budget is stored.");
      }}>
        <h2>Zone budget</h2>
        <p>{centre?.sittingBudget ?? "—"} sittings. {waves.length} open waves. {admitted} admitted.</p>
        <label>Sittings<input inputMode="numeric" value={budget} onChange={(event) => setBudget(event.target.value)} required /></label>
        <button type="submit" className="draft-save" disabled={pending}>Save budget</button>
      </form>
      <form className="task-close" onSubmit={(event) => {
        event.preventDefault();
        if (!waveId || joiner.trim().length === 0) {
          setProblem("Choose an open wave and name the person who joined after it opened.");
          return;
        }
        run(() => admitRolling(waveId, joiner.trim()), "Rolling intake admitted one person.");
      }}>
        <h2>Rolling intake</h2>
        <label>Open wave<select value={waveId} onChange={(event) => setWaveId(event.target.value)}>{waves.map((wave) => <option key={wave.waveId} value={wave.waveId}>{wave.profileName} · {wave.admittedCount} admitted</option>)}</select></label>
        <label>Person who joined after the wave opened<input value={joiner} onChange={(event) => setJoiner(event.target.value)} /></label>
        <button type="submit" className="draft-save" disabled={pending || !waveId}>Admit one</button>
      </form>
    </section>
  );
}

function Problem({ text, onRetry }: { text: string; onRetry: () => void }) {
  return (
    <p className="gate-alert" role="alert">
      {text}
      <button type="button" className="team-retry" onClick={onRetry}>Try again</button>
    </p>
  );
}
