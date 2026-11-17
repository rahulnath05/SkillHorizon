import { useCallback, useEffect, useState, type FormEvent } from "react";
import { NavLink, useParams } from "react-router-dom";
import { PageHead, PersonaFrame, type RailItem } from "./frame";
import {
  getCatalogueBoards,
  getCatalogueMarket,
  publishMarket,
  saveMarketDraft,
  useShell,
} from "./api";
import { ActivationPage, ApprovalsPage, OverviewPage, RequestsPage, TaxonomyPage, WorkspacePage } from "./ld";
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
  { id: "engine", icon: "≡", label: "Adaptive engine" },
  { id: "marking", icon: "◇", label: "Rubric & marking" },
  { id: "panel", icon: "✓", label: "SME panel" },
  { id: "bank", icon: "→", label: "Item bank" },
  { id: "scorecard", icon: "▦", label: "Programme scorecard" },
  { id: "operations", icon: "◔", label: "Assessment operations" },
  { id: "declared", icon: "◎", label: "Declared vs assessed" },
  { id: "pilot", icon: "✔", label: "Pilot report" },
  { id: "rolling", icon: "↻", label: "Rolling intake" },
];

const studioIds = new Set(studioNav.map((item) => item.id).concat("assessment"));

export function CatalogueHome() {
  const { screen } = useParams();
  const known = primaryNav.some((item) => item.id === screen) || studioIds.has(screen ?? "") || screen === "market";
  const raw = known ? screen! : "overview";
  const studioCurrent = raw === "assessment" ? "studio" : studioIds.has(raw) ? raw : null;
  const inStudio = studioCurrent !== null;
  const { shell } = useShell();
  const [counts, setCounts] = useState<{ requests?: number; approvals?: number; activation?: number }>({});

  useEffect(() => {
    let live = true;
    void getCatalogueBoards()
      .then((board) => {
        if (!live) return;
        setCounts(board.counts);
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
  const page = inStudio ? `Assessment & programme · ${studioLabel}` : raw === "market" ? "Market demand" : primaryNav.find((item) => item.id === raw)?.label ?? "Overview";

  return (
    <PersonaFrame
      label="Learning & Development"
      kicker={shell?.displayName?.trim() || "Learning and development"}
      mark="LD"
      crumb="L&D Workspace"
      navLabel="Your workspace"
      railAction={<NavLink className="rail-new" to="/workspace/workspace">New profile</NavLink>}
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
