import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { NavLink, useSearchParams } from "react-router-dom";
import {
  activateTaxonomySkill,
  admitWave,
  createCatalogueProfile,
  getActivationCentre,
  getCatalogueBoards,
  getStandards,
  openProfileRequest,
  saveBenchmarkDraft,
  saveTaxonomyDraft,
  sendDraft,
  useShell,
  type BoardProfile,
  type BoardSkill,
  type BoardTaxonomy,
  type CatalogueBoard,
  type Standards,
} from "./api";
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
        lede="See what is ready, what needs attention, and what can be sent to each com-lead—without stepping through a process."
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {board ? (
        <>
          <div className="stat-row">
            <NavLink to="/workspace/approvals">
              <span className="kicker">Competencies ready to send</span>
              <strong>{readyPacks}</strong>
              <span className="hint">complete packs awaiting submission</span>
            </NavLink>
            <NavLink to="/workspace/workspace">
              <span className="kicker">Profiles needing benchmarks</span>
              <strong>{needing}</strong>
              <span className="hint">{readyProfiles} of {profiles.length} profiles complete</span>
            </NavLink>
            <NavLink to="/workspace/requests">
              <span className="kicker">New profile requests</span>
              <strong>{newRequests}</strong>
              <span className="hint">need triage or additional context</span>
            </NavLink>
            <NavLink to="/workspace/approvals" className="ink">
              <span className="kicker">Changes requested</span>
              <strong>{changes}</strong>
              <span className="hint">pack returned by a com-lead</span>
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
                      <span>{competency.comLead || "No lead stored"} · com-lead</span>
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
                      <NavLink className="fill-btn" to="/workspace/activation">Activate</NavLink>
                    ) : (
                      <NavLink className="outline-btn" to="/workspace/workspace">Open</NavLink>
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
                    <span className="mini-top">
                      <span className={request.requestStatus === "new" ? "pill amber" : "pill grey"}>{(request.requestStatus ?? "new").replace("-", " ")}</span>
                      <time>{formatAsOf(request.recordedAt)}</time>
                    </span>
                    <strong>{request.title || request.displayName}</strong>
                    <span>{request.competencyName}</span>
                  </NavLink>
                ))}
                {board.requests.length === 0 ? <p className="empty-note">No profile request is waiting.</p> : null}
              </div>
            </aside>
          </div>
          <div className="ld-split wide">
            <section>
              <div className="aside-head">
                <div>
                  <h2>What needs your attention</h2>
                  <p className="card-note">Prioritized by submission impact and age</p>
                </div>
                <button type="button" className="text-link" disabled title="Mira is not available yet">Ask Mira to prioritize</button>
              </div>
              <div className="panel">
                {board.attention.length === 0 ? <p className="empty-note">Nothing is waiting on these rows.</p> : null}
                {board.attention.map((item) => (
                  <NavLink key={`${item.tone}-${item.title}`} className="attention-row" to={`/workspace/${item.screen}`}>
                    <span className={item.tone === "approved" ? "pill mint" : "pill amber"}>{attentionLabel(item.tone, item.detail)}</span>
                    <span className="task-main"><strong>{item.title}</strong><small>{item.detail}</small></span>
                    <span className="go">{item.action} →</span>
                  </NavLink>
                ))}
              </div>
            </section>
            <aside>
              <div className="aside-head">
                <div>
                  <h2>Taxonomy health</h2>
                  <p className="card-note">Secondary governance monitoring</p>
                </div>
                <NavLink to="/workspace/taxonomy">Open taxonomy</NavLink>
              </div>
              <div className="panel health-panel">
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
              </div>
            </aside>
          </div>
        </>
      ) : null}
    </section>
  );
}

function attentionLabel(tone: string, detail: string) {
  if (tone === "approved") return "Approved";
  if (tone === "returned") return "Returned";
  if (tone === "request") return "New request";
  const count = detail.match(/^(\d+)/)?.[1];
  if (tone === "missing") return count ? `${count} level${count === "1" ? "" : "s"} missing` : "Levels missing";
  return tone;
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
  const [searchParams] = useSearchParams();
  const openedCompetency = useRef("");
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
  useEffect(() => {
    if (searchParams.get("new") === "1") setModal("profile");
  }, [searchParams]);

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

  function addSkill(profile: BoardProfile, skillId: string, rank: number | null = null) {
    const taxonomySkill = board?.taxonomy?.skills.find((skill) => skill.skillId === skillId);
    if (!taxonomySkill) return;
    const skills = [...skillsOf(profile), { skillId, code: taxonomySkill.code, name: taxonomySkill.name, category: taxonomySkill.category, rank, levelSet: rank !== null, suggestedRank: board?.taxonomy?.proficiencies[0]?.rank ?? null }];
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
        const publishedTaxonomyId = board.taxonomy?.status === "published"
          ? board.taxonomy.taxonomyVersionId
          : standards?.history.find((item) => item.status === "published")?.taxonomyVersionId ?? "";
        const taxonomyVersionId = profile.taxonomyVersionId || publishedTaxonomyId;
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
  useEffect(() => {
    if (!competency || !board || openedCompetency.current === competency.code) return;
    openedCompetency.current = competency.code;
    const ids = board.profiles.filter((profile) => profile.competencyCode === competency.code).slice(0, 2).map((profile) => profile.profileId);
    setSelected(ids);
  }, [board, competency]);

  return (
    <section data-screen="workspace">
      <PageHead
        eyebrow="Multi-profile benchmarking"
        title="Benchmark profiles side by side."
        lede="Select up to four profiles, set skill levels, and save all changes together."
        action={(
          <div className="head-actions">
            <label>
              <span className="sr-only">Competency</span>
              <select value={competency?.code ?? ""} onChange={(event) => { setCompetencyCode(event.target.value); setSelected([]); }}>
                {(board?.competencies ?? []).map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}
              </select>
            </label>
            <button type="button" className="fill-btn" onClick={() => setModal("profile")}>+ New profile</button>
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
          {requestCount > 0 ? <NavLink className="pill amber" to="/workspace/requests">{requestCount} profile request{requestCount === 1 ? "" : "s"}</NavLink> : null}
        </div>
      ) : null}
      <div className="composer">
        <aside>
          <label>
            <span className="sr-only">Search profiles</span>
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
                <span className="tick" aria-hidden="true">{on ? "✓" : ""}</span>
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
              const band = profile.band || profile.levelName;
              const family = profile.family || profile.frameworkName;
              return (
                <article key={profile.profileId} className={unsaved ? "profile-card is-dirty" : "profile-card"}>
                  <header>
                    <div className="card-title">
                      <span>{[band, family].filter((item) => item.length > 0).join(" · ")}</span>
                      <button type="button" className="close-x" aria-label={`Remove ${profile.name} from workspace`} onClick={() => toggle(profile.profileId)}>×</button>
                    </div>
                    <h2>{profile.name}</h2>
                    <p>{profile.purpose || "No purpose is stored."}</p>
                    <div className="pill-row">
                      <span className={readyProfile({ ...profile, skills }) ? "pill mint" : "pill amber"}>{readyProfile({ ...profile, skills }) ? "Benchmark ready" : `${missingLevels({ ...profile, skills })} levels missing`}</span>
                      {unsaved ? <span className="unsaved">Unsaved</span> : null}
                    </div>
                  </header>
                  {skills.map((skill) => {
                    const showCode = skill.code.toLowerCase() !== skill.name.toLowerCase();
                    return (
                      <label key={skill.skillId} className="level-row">
                        <span>
                          <strong>{showCode ? <span className="code-chip">{skill.code}</span> : null}{skill.name}</strong>
                          <small>{skill.category || "Taxonomy skill"}{skill.rank === null && skill.suggestedRank ? ` · suggested L${skill.suggestedRank}` : ""}</small>
                        </span>
                        <select aria-label={`Target level for ${skill.name}`} value={skill.rank ?? ""} onChange={(event) => setRank(profile, skill.skillId, event.target.value === "" ? null : Number(event.target.value))}>
                          <option value="">Set</option>
                          {(board?.taxonomy?.proficiencies ?? []).map((item) => <option key={item.rank} value={item.rank}>L{item.rank}</option>)}
                        </select>
                      </label>
                    );
                  })}
                  <footer>
                    <button type="button" className="text-link" onClick={() => { setSkillFor(profile.profileId); setModal("skill"); }}>+ Add benchmark skill</button>
                  </footer>
                </article>
              );
            })}
          </div>
          <div className="save-bar">
            <div className="save-copy">
              <strong>{missing === 0 ? "Selected profiles are complete" : `${missing} benchmark levels missing`}</strong>
              <span>{dirty.length} profile{dirty.length === 1 ? "" : "s"} with unsaved changes</span>
            </div>
            <button type="button" className="fill-btn" disabled={pending || dirty.length === 0} onClick={() => void saveAll()}>Save all changes</button>
            <NavLink className="outline-btn" to="/workspace/approvals">Review approval readiness</NavLink>
          </div>
        </div>
      </div>
      {modal === "profile" ? (
        <ProfileDialog
          standards={standards}
          competencies={board?.competencies ?? []}
          skills={board?.taxonomy?.skills ?? []}
          pending={pending}
          onClose={() => setModal(null)}
          onCreate={(body, skillIds) => {
            setPending(true);
            void createCatalogueProfile(body)
              .then(async (profileId) => {
                await load();
                if (profileId && skillIds.length > 0) {
                  const chosen = (board?.taxonomy?.skills ?? []).filter((skill) => skillIds.includes(skill.skillId));
                  setEdits((current) => ({
                    ...current,
                    [profileId]: chosen.map((skill) => ({ skillId: skill.skillId, code: skill.code, name: skill.name, category: skill.category, rank: null, levelSet: false, suggestedRank: board?.taxonomy?.proficiencies[0]?.rank ?? null })),
                  }));
                  setDirty((current) => current.includes(profileId) ? current : [...current, profileId]);
                  setSelected((current) => current.includes(profileId) || current.length >= 4 ? current : [...current, profileId]);
                }
                setModal(null);
              })
              .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The profile was not created."))
              .finally(() => setPending(false));
          }}
          onTaxonomy={() => setModal("taxonomy")}
        />
      ) : null}
      {modal === "skill" ? (
        <SkillDialog
          skills={(board?.taxonomy?.skills ?? []).filter((skill) => {
            const profile = board?.profiles.find((item) => item.profileId === skillFor);
            return profile ? !skillsOf(profile).some((item) => item.skillId === skill.skillId) : false;
          })}
          levels={board?.taxonomy?.proficiencies ?? []}
          onClose={() => setModal(null)}
          onTaxonomy={() => setModal("taxonomy")}
          onAdd={(skillId, rank) => {
            const profile = board?.profiles.find((item) => item.profileId === skillFor);
            if (profile) addSkill(profile, skillId, rank);
          }}
        />
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
  skills,
  pending,
  onClose,
  onCreate,
  onTaxonomy,
}: {
  standards: Standards | null;
  competencies: CatalogueBoard["competencies"];
  skills: BoardTaxonomy["skills"];
  pending: boolean;
  onClose: () => void;
  onCreate: (body: { profileName: string; frameworkCode: string; levelCode: string; purpose: string; band: string; family: string; competencyCode: string }, skillIds: string[]) => void;
  onTaxonomy: () => void;
}) {
  const [name, setName] = useState("");
  const [frameworkCode, setFrameworkCode] = useState(standards?.frameworks[0]?.code ?? "");
  const framework = standards?.frameworks.find((item) => item.code === frameworkCode) ?? standards?.frameworks[0];
  const [levelCode, setLevelCode] = useState(framework?.levels[0]?.code ?? "");
  const [band, setBand] = useState("");
  const [competencyCode, setCompetencyCode] = useState(competencies[0]?.code ?? "");
  const [picked, setPicked] = useState<string[]>([]);
  return (
    <div className="ld-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section className="ld-sheet" role="dialog" aria-modal="true" aria-labelledby="new-profile-title">
        <header>
          <div>
            <p>L&D-known need</p>
            <h2 id="new-profile-title">Add a profile to benchmark</h2>
          </div>
          <button type="button" className="close-x" aria-label="Close" onClick={onClose}>×</button>
        </header>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            onCreate({ profileName: name.trim(), frameworkCode, levelCode, purpose: "", band, family: "", competencyCode }, picked);
          }}
        >
          <label>Competency<select value={competencyCode} onChange={(event) => setCompetencyCode(event.target.value)}>{competencies.map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label>
          <div className="ld-pair">
            <label>Profile name<input value={name} onChange={(event) => setName(event.target.value)} required placeholder="e.g. AI Product Analyst" /></label>
            <label>Band<input value={band} onChange={(event) => setBand(event.target.value)} required placeholder="e.g. Senior" /></label>
          </div>
          <div className="ld-pair">
            <label>Framework<select value={frameworkCode} onChange={(event) => { setFrameworkCode(event.target.value); const next = standards?.frameworks.find((item) => item.code === event.target.value); setLevelCode(next?.levels[0]?.code ?? ""); }}>{(standards?.frameworks ?? []).map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label>
            <label>Level<select value={levelCode} onChange={(event) => setLevelCode(event.target.value)}>{(framework?.levels ?? []).map((level) => <option key={level.code} value={level.code}>{level.name}</option>)}</select></label>
          </div>
          <fieldset>
            <legend>Select skills from the taxonomy</legend>
            <div className="skill-pick">
              {skills.map((skill) => (
                <label key={skill.skillId}>
                  <input
                    type="checkbox"
                    checked={picked.includes(skill.skillId)}
                    onChange={(event) => setPicked((current) => event.target.checked ? [...current, skill.skillId] : current.filter((id) => id !== skill.skillId))}
                  />
                  <span><strong>{skill.name}</strong><small>{skill.category || skill.competencyName}</small></span>
                </label>
              ))}
              {skills.length === 0 ? <p className="card-note">No taxonomy skill is stored yet.</p> : null}
            </div>
            <button type="button" className="text-link" onClick={onTaxonomy}>+ Skill not in the taxonomy?</button>
          </fieldset>
          <div className="dialog-actions">
            <button type="button" className="outline-btn" onClick={onClose}>Cancel</button>
            <button type="submit" className="fill-btn" disabled={pending || name.trim().length === 0}>Create benchmark draft</button>
          </div>
        </form>
      </section>
    </div>
  );
}

function SkillDialog({
  skills,
  levels,
  onClose,
  onTaxonomy,
  onAdd,
}: {
  skills: BoardTaxonomy["skills"];
  levels: BoardTaxonomy["proficiencies"];
  onClose: () => void;
  onTaxonomy: () => void;
  onAdd: (skillId: string, rank: number | null) => void;
}) {
  const [skillId, setSkillId] = useState("");
  const [rank, setRank] = useState("");
  return (
    <div className="ld-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section className="ld-sheet" role="dialog" aria-modal="true" aria-labelledby="add-skill-title">
        <header>
          <div>
            <p>Benchmark skill</p>
            <h2 id="add-skill-title">Add benchmark skill</h2>
          </div>
          <button type="button" className="close-x" aria-label="Close" onClick={onClose}>×</button>
        </header>
        <form onSubmit={(event) => { event.preventDefault(); onAdd(skillId, rank === "" ? null : Number(rank)); }}>
          <label>Taxonomy skill
            <select value={skillId} required onChange={(event) => setSkillId(event.target.value)}>
              <option value="">Choose a shared skill</option>
              {skills.map((skill) => <option key={skill.skillId} value={skill.skillId}>{skill.name} · {skill.category || skill.competencyName}</option>)}
            </select>
          </label>
          <label>Target level
            <select value={rank} onChange={(event) => setRank(event.target.value)}>
              <option value="">Set later</option>
              {levels.map((level) => <option key={level.rank} value={level.rank}>L{level.rank}</option>)}
            </select>
          </label>
          <p className="card-note">The skill name and proficiency meaning come from the taxonomy. This profile sets only the target level.</p>
          <button type="button" className="text-link" onClick={onTaxonomy}>+ Skill not in the taxonomy?</button>
          <div className="dialog-actions">
            <button type="button" className="outline-btn" onClick={onClose}>Cancel</button>
            <button type="submit" className="fill-btn" disabled={skills.length === 0}>Add skill</button>
          </div>
        </form>
      </section>
    </div>
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
    <div className="ld-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
      <section className="ld-sheet" role="dialog" aria-modal="true" aria-labelledby="new-taxonomy-skill-title">
        <header>
          <div>
            <p>Shared taxonomy</p>
            <h2 id="new-taxonomy-skill-title">Create a taxonomy skill</h2>
          </div>
          <button type="button" className="close-x" aria-label="Close" onClick={onClose}>×</button>
        </header>
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
          <p className="card-note">Create the shared definition first. Profiles will reference it and set only the target level.</p>
          <label>Skill name<input value={name} onChange={(event) => { setName(event.target.value); setCode(event.target.value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "")); }} required placeholder="e.g. Semantic layer design" /></label>
          <label>Skill code<input value={code} onChange={(event) => setCode(event.target.value)} required /></label>
          <p className="ld-note"><strong>Proficiency model:</strong> the new skill joins the shared scale already stored for this taxonomy. A profile chooses the target level when it maps this skill.</p>
          <div className="dialog-actions">
            <button type="button" className="outline-btn" onClick={onClose}>Cancel</button>
            <button type="submit" className="fill-btn" disabled={pending || name.trim().length === 0 || code.trim().length === 0}>Create taxonomy skill</button>
          </div>
        </form>
      </section>
    </div>
  );
}

export function TaxonomyPage() {
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [search, setSearch] = useState("");
  const [category, setCategory] = useState("all");
  const [selected, setSelected] = useState("");
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [draftOpen, setDraftOpen] = useState(false);
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
        action={<button type="button" className="fill-btn" onClick={() => setDraftOpen(true)}>+ New taxonomy skill</button>}
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      <div className="stat-row cols-3">
        <div><span className="kicker">Taxonomy skills</span><strong>{skills.length}</strong></div>
        <div><span className="kicker">Skill categories</span><strong>{categories.length}</strong></div>
        <div><span className="kicker">Draft definitions</span><strong>{skills.filter((skill) => skill.status === "draft").length}</strong></div>
      </div>
      <div className="composer">
        <aside>
          <label>
            <span className="sr-only">Search skill taxonomy</span>
            <input value={search} onChange={(event) => setSearch(event.target.value)} placeholder="Search skills, categories, definitions…" />
          </label>
          <div className="filter-row">
            <button type="button" className={category === "all" ? "is-on" : undefined} onClick={() => setCategory("all")}>All</button>
            {categories.map((item) => <button key={item} type="button" className={category === item ? "is-on" : undefined} onClick={() => setCategory(item)}>{item}</button>)}
          </div>
          <p className="card-note">{filtered.length} skills</p>
          <div className="skill-list">
            {filtered.length === 0 ? <p className="empty-note">No taxonomy skills match this filter.</p> : null}
            {filtered.map((skill) => {
              const count = (board?.profiles ?? []).filter((profile) => profile.skills.some((item) => item.code === skill.code)).length;
              return (
                <button key={skill.skillId} type="button" className={current?.skillId === skill.skillId ? "tax-skill is-on" : "tax-skill"} onClick={() => setSelected(skill.skillId)}>
                  <span className="tax-skill-top">
                    <strong>{skill.code.toLowerCase() === skill.name.toLowerCase() ? skill.name : <><span className="code-chip">{skill.code}</span>{skill.name}</>}</strong>
                    <em className={skill.status === "active" ? "pill mint" : "pill amber"}>{skill.status}</em>
                  </span>
                  <span className="tax-skill-meta">
                    <span>{skill.category || "Uncategorised"}</span>
                    <span>{count} profile{count === 1 ? "" : "s"}</span>
                  </span>
                </button>
              );
            })}
          </div>
        </aside>
        <div className="taxonomy-detail">
          {current ? (
            <>
              <div className="skill-detail-head">
                <div>
                  <div className="pill-row">
                    <span className="pill mint">{current.category || current.competencyName}</span>
                    <span className="pill grey">Shared proficiency model</span>
                  </div>
                  <h2>{current.name}</h2>
                  <p>{current.definition || "No definition is stored."}</p>
                </div>
                <div className="pill-row">
                  <span className={current.status === "active" ? "pill mint" : "pill amber"}>{current.status}</span>
                  {current.status === "draft" ? (
                    <button type="button" className="outline-btn" disabled={pending} onClick={() => {
                      setPending(true);
                      void activateTaxonomySkill(current.code).then(load).catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The skill was not marked active.")).finally(() => setPending(false));
                    }}>Mark active</button>
                  ) : null}
                </div>
              </div>
              <p className="section-kicker">Proficiency definitions</p>
              <h3>One meaning wherever the skill is used</h3>
              <div className="level-list">
                {(board?.taxonomy?.proficiencies ?? []).map((level) => (
                  <div className="level-meaning" key={level.rank}><strong>L{level.rank}</strong><span>{level.name}</span><p>{level.meaning || "No shared meaning is stored."}</p></div>
                ))}
              </div>
              <p className="section-kicker">Used in {usages.length} profile{usages.length === 1 ? "" : "s"}</p>
              <div className="use-row">
                {usages.length === 0 ? <span className="card-note">Not mapped to a profile yet.</span> : usages.map(({ profile, skill }) => (
                  <NavLink key={profile.profileId} className="use-chip" to="/workspace/workspace"><strong>{profile.name}</strong><span>{skill.rank ? `L${skill.rank}` : "Level not set"}</span></NavLink>
                ))}
              </div>
            </>
          ) : <p className="empty-note">No skill selected. Choose a taxonomy skill to inspect its shared definition.</p>}
          {board?.taxonomy ? null : <p className="empty-note">No taxonomy version is in this zone.</p>}
          {draftOpen && board?.taxonomy ? (
            <TaxonomyDialog
              board={board}
              pending={pending}
              onClose={() => setDraftOpen(false)}
              onSave={(body) => {
                setPending(true);
                void saveTaxonomyDraft(body)
                  .then(load)
                  .then(() => setDraftOpen(false))
                  .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That draft was not saved."))
                  .finally(() => setPending(false));
              }}
            />
          ) : null}
        </div>
      </div>
    </section>
  );
}

export function RequestsPage() {
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [standards, setStandards] = useState<Standards | null>(null);
  const [openingId, setOpeningId] = useState("");
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
        action={<NavLink className="fill-btn" to="/workspace/workspace?new=1">+ Create without a request</NavLink>}
      />
      {problem ? <Problem text={problem} onRetry={() => void load()} /> : null}
      {requests.length === 0 ? <p className="empty-note">No profile request is waiting.</p> : null}
      <div className="request-list">
      {requests.map((request) => {
        const profile = board?.profiles.find((item) => item.profileId === request.profileId) ?? null;
        const done = profile ? profile.skills.filter((skill) => skill.levelSet).length : 0;
        const total = profile?.skills.length ?? 0;
        const needsContext = !profile && request.requestStatus === "needs-context";
        const label = profile ? (readyProfile(profile) ? "Benchmark ready" : "Benchmark in progress") : needsContext ? "Needs context" : request.requestStatus === "waiting" ? "Waiting on com-lead" : "New request";
        const action = profile ? (readyProfile(profile) ? "View benchmark" : "Continue benchmark") : needsContext ? "Request context" : "Start benchmark";
        return (
          <article key={request.requestId} className="request-card">
            <div>
              <span className={label === "Benchmark ready" ? "pill mint" : label === "New request" || label === "Benchmark in progress" ? "pill amber" : "pill grey"}>{label}</span>
              <h2>{request.title || request.displayName}</h2>
              <p>{request.competencyName || "Competency"} · {formatAsOf(request.recordedAt)}</p>
            </div>
            <div className="request-note">
              <p>{request.note}</p>
              {request.evidence ? <p className="evidence">{request.evidence}</p> : null}
              <p>Requested by {request.managerEmployeeId}</p>
            </div>
            <div className="request-action">
              {profile ? <p>{done} of {total} skill levels set</p> : null}
              {profile ? <NavLink className="fill-btn" to="/workspace/workspace">{action}</NavLink> : (
                <button type="button" className={needsContext ? "outline-btn" : "fill-btn"} onClick={() => { setOpeningId(request.requestId); setProfileName(request.title || request.displayName); }}>{action}</button>
              )}
            </div>
          </article>
        );
      })}
      </div>
      {openingId ? (
        <div className="ld-backdrop" onMouseDown={(event) => { if (event.target === event.currentTarget) setOpeningId(""); }}>
          <section className="ld-sheet" role="dialog" aria-modal="true" aria-labelledby="open-draft-title">
            <header>
              <div>
                <p>Requests from competencies</p>
                <h2 id="open-draft-title">Open as a draft profile</h2>
              </div>
              <button type="button" className="close-x" aria-label="Close" onClick={() => setOpeningId("")}>×</button>
            </header>
            <form onSubmit={(event) => {
              event.preventDefault();
              setPending(true);
              void openProfileRequest(openingId, { profileName, frameworkCode, levelCode })
                .then(() => { setProfileName(""); setOpeningId(""); return load(); })
                .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That request was not opened."))
                .finally(() => setPending(false));
            }}>
              <label>Profile name<input value={profileName} onChange={(event) => setProfileName(event.target.value)} required /></label>
              <div className="ld-pair">
                <label>Framework<select value={frameworkCode} onChange={(event) => { setFrameworkCode(event.target.value); const next = standards?.frameworks.find((item) => item.code === event.target.value); setLevelCode(next?.levels[0]?.code || ""); }}>{(standards?.frameworks ?? []).map((item) => <option key={item.code} value={item.code}>{item.name}</option>)}</select></label>
                <label>Level<select value={levelCode} onChange={(event) => setLevelCode(event.target.value)}>{(framework?.levels ?? []).map((level) => <option key={level.code} value={level.code}>{level.name}</option>)}</select></label>
              </div>
              <div className="dialog-actions">
                <button type="button" className="outline-btn" onClick={() => setOpeningId("")}>Cancel</button>
                <button type="submit" className="fill-btn" disabled={pending || profileName.trim().length === 0}>Open draft</button>
              </div>
            </form>
          </section>
        </div>
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
      <div className="pack-table">
        <div className="pack-head"><span>Competency</span><span>Benchmark readiness</span><span>Status</span><span>Action</span></div>
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
            <div className="pack-row" key={competency.code}>
              <div><strong>{competency.name}</strong><span className="sub">{competency.comLead || "No lead stored"} · com-lead</span></div>
              <div>
                <div className="meter-label"><span>{ready} / {total} profiles</span><b>{percent}%</b></div>
                <div className="meter"><i style={{ width: `${percent}%` }} data-full={percent === 100 ? "true" : "false"} /></div>
              </div>
              <div><span className={approved || withLead || readyToSend ? "pill mint" : "pill amber"}>{label}</span>{sent?.sentAt ? <span className="sub">{formatAsOf(sent.sentAt)}</span> : null}</div>
              <div>
                {approved ? <NavLink className="fill-btn" to="/workspace/activation">Activate baseline</NavLink> : null}
                {changes ? <NavLink className="fill-btn" to="/workspace/workspace">Resolve changes</NavLink> : null}
                {!approved && !changes && !withLead && !readyToSend ? <NavLink className="outline-btn" to="/workspace/workspace">Finish profiles</NavLink> : null}
                {withLead && !approved ? <NavLink className="outline-btn" to="/workspace/workspace">View review</NavLink> : null}
                {readyToSend ? (
                  <button type="button" className="fill-btn" disabled={pending} onClick={() => {
                    setPending(true);
                    void Promise.all(unsent.map((profile) => sendDraft(profile.benchmarkVersionId as string)))
                      .then(load)
                      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "That draft was not sent."))
                      .finally(() => setPending(false));
                  }}>Send to com-lead</button>
                ) : null}
              </div>
            </div>
          );
        })}
      </div>
      {(board?.competencies.length ?? 0) === 0 ? <p className="empty-note">No competency pack is stored.</p> : null}
      <aside className="handoff">
        <div>
          <p className="kicker">Simple handoff rule</p>
          <h2>Complete profiles move together. Incomplete profiles stay with L&D.</h2>
          <p>Com-leads receive the profile purpose, benchmark skills, target levels, and change rationale—not the authoring workflow.</p>
        </div>
        <span className="rule-pill">L&D prepares · com-lead approves</span>
      </aside>
    </section>
  );
}

export function ActivationPage() {
  useTitle("Activate the approved benchmark.");
  const [board, setBoard] = useState<CatalogueBoard | null>(null);
  const [centre, setCentre] = useState<Awaited<ReturnType<typeof getActivationCentre>> | null>(null);
  const [versions, setVersions] = useState<{ id: string; name: string }[]>([]);
  const [versionId, setVersionId] = useState("");
  const [subjects, setSubjects] = useState("");
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
        action={<div className="approved-chip"><span>{published ? "Published baseline" : "Approved competency"}</span><strong>{chipName}</strong></div>}
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      <ol className="step-track">
        <li data-state={published ? "done" : "current"}><span>01</span><strong>Publish baseline</strong><small>{published ? "Complete" : "Your next step"}</small></li>
        <li data-state={mapped ? "done" : published ? "current" : "wait"}><span>02</span><strong>Map population</strong><small>{mapped ? "Complete" : published ? "Your next step" : "Waiting"}</small></li>
        <li data-state={launched ? "done" : mapped ? "current" : "wait"}><span>03</span><strong>Launch assessment</strong><small>{launched ? "Complete" : mapped ? "Your next step" : "Waiting"}</small></li>
        <li data-state={snapshot ? "done" : launched ? "current" : "wait"}><span>04</span><strong>Readiness snapshot</strong><small>{snapshot ? "Complete" : launched ? "Your next step" : "Waiting"}</small></li>
      </ol>
      <article className="act-card act-split">
        <div>
          <div className="act-kicker"><span className={published ? "pill mint" : "pill amber"}>{published ? "Published" : "Approved · not live"}</span><span>Step 01</span></div>
          <h2>Publish the approved baseline</h2>
          <p>{board?.baselines.length ?? 0} published profile versions are effective for assessment.</p>
          <div className="act-meta">
            {(board?.baselines ?? []).slice(0, 3).map((baseline) => (
              <span key={`${baseline.profileName}-${baseline.versionNumber}`}><strong>{baseline.profileName}</strong> version {baseline.versionNumber}{baseline.publishedAt ? ` · ${formatAsOf(baseline.publishedAt)}` : ""}</span>
            ))}
          </div>
        </div>
        <div className="act-side">
          {published ? <><strong>Baseline is immutable</strong><span>A newer draft is published from the benchmark workspace.</span></> : <span>No published benchmark is stored.</span>}
        </div>
      </article>
      <article className="act-card">
        <header>
          <div>
            <div className="act-kicker"><span className={mapped ? "pill mint" : "pill amber"}>{admitted} / {population || admitted} mapped</span><span>Step 02</span></div>
            <h2>Map people to approved profiles</h2>
            <p>Confirm who should be assessed against each approved job profile.</p>
          </div>
        </header>
        {waves.length === 0 ? <p className="empty-note">No wave is open in this zone.</p> : (
          <div className="people-list">
            {waves.flatMap((wave) => wave.subjectIds.map((subjectId) => (
              <div className="people-row" key={`${wave.waveId}-${subjectId}`}>
                <strong>{subjectId}</strong>
                <span><small>Mapped profile</small>{wave.profileName}</span>
              </div>
            )))}
          </div>
        )}
        {mapped ? <p className="empty-note">All {population} people are mapped.</p> : null}
      </article>
      <article className="act-card act-split">
        <div>
          <div className="act-kicker"><span className={launched ? "pill mint" : "pill grey"}>{launched ? "Assessment active" : "Configured"}</span><span>Step 03</span></div>
          <h2>Launch evidence-based assessment</h2>
          <p>Employees provide self-assessment and evidence. People managers validate ratings before calibration.</p>
          <div className="source-tiles">
            <label><input type="checkbox" checked={board?.cycle?.selfAssessment ?? false} disabled readOnly /><span><strong>Self-assessment</strong><small>Employee rating</small></span></label>
            <label><input type="checkbox" checked={board?.cycle?.managerValidation ?? false} disabled readOnly /><span><strong>Manager validation</strong><small>Rating confirmation</small></span></label>
            <label><input type="checkbox" checked={board?.cycle?.workEvidence ?? false} disabled readOnly /><span><strong>Work evidence</strong><small>Observable proof</small></span></label>
          </div>
          <div className="act-meta">
            <span><strong>Cycle:</strong> {board?.cycle?.cycleName ?? "No cycle name is stored."}</span>
            {board?.cycle?.dueOn ? <span><strong>Due:</strong> {board.cycle.dueOn}</span> : null}
          </div>
        </div>
        <aside className="act-dark">
          {snapshot ? (
            <>
              <span>Assessment progress</span>
              <strong>{snapshot.measuredCount}<small> / {snapshot.memberCount}</small></strong>
              <p>{snapshot.gapCount} below the bar. {snapshot.cohortCount} cohorts.</p>
            </>
          ) : <><span>Ready to launch</span><p>Name the people and the published benchmark. The wave opens for those people.</p></>}
          <form onSubmit={(event) => {
            event.preventDefault();
            const subjectIds = subjects.split(/\s+/).map((item) => item.trim()).filter((item) => item.length > 0);
            if (!versionId || subjectIds.length === 0) {
              setProblem("Choose a published benchmark and name the people to launch.");
              return;
            }
            run(() => admitWave(versionId, subjectIds), "The wave is open.");
          }}>
            <label>Published benchmark<select value={versionId} onChange={(event) => setVersionId(event.target.value)}>{versions.map((version) => <option key={version.id} value={version.id}>{version.name}</option>)}</select></label>
            <label>People<textarea value={subjects} onChange={(event) => setSubjects(event.target.value)} placeholder="One identifier per line" /></label>
            <button type="submit" className="fill-btn" disabled={pending || !versionId}>Launch assessment cycle</button>
          </form>
        </aside>
      </article>
      <section className="act-step">
        <div className="aside-head">
          <div>
            <p className="section-kicker">Step 04</p>
            <h2>Readiness snapshot</h2>
          </div>
          <span className={snapshot ? "pill mint" : "pill grey"}>{snapshot ? "Latest calibrated evidence" : "Available after launch"}</span>
        </div>
        {snapshot ? (
          <div className="stat-row">
            <div><span className="kicker">Measured</span><strong>{snapshot.measuredCount}</strong><span className="hint">of {snapshot.memberCount} members</span></div>
            <div><span className="kicker">Not yet measured</span><strong>{Math.max(snapshot.memberCount - snapshot.measuredCount, 0)}</strong></div>
            <div><span className="kicker">Material gaps</span><strong>{snapshot.gapCount}</strong></div>
            <div><span className="kicker">Cohorts</span><strong>{snapshot.cohortCount}</strong></div>
          </div>
        ) : <p className="empty-note">Launch the assessment to calculate readiness.</p>}
      </section>
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
