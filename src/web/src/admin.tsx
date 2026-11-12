import { useCallback, useEffect, useState, type FormEvent } from "react";
import { useParams } from "react-router-dom";
import {
  exportPersonalMetadata,
  getAdminAudit,
  getAdminExports,
  getAdminGrants,
  getAdminIntegrations,
  getAdminModels,
  getAdminSecurity,
  getMalpracticeCases,
  grantWorkspace,
  openMalpracticeCase,
  revokeWorkspace,
  type AdminGrant,
  type AuditRecord,
  type ConnectorStatus,
  type ExportType,
  type MalpracticeCase,
  type ModelRegistration,
  type PersonalMetadata,
  type ZoneResidency,
} from "./api";
import { PageHead, PersonaFrame, type RailItem } from "./frame";
import { formatAsOf, useTitle } from "./ui";
import { workspaceCopy } from "./workspaces";

const screens: RailItem[] = [
  { id: "access", label: "Access and roles", icon: "✓", pill: "DR-10" },
  { id: "security", label: "Security", icon: "◫", pill: "SP-01" },
  { id: "ai", label: "Responsible AI", icon: "◇", pill: "SP-07" },
  { id: "audit", label: "Audit and retention", icon: "≡", pill: "SP-10" },
  { id: "malpractice", label: "Malpractice", icon: "→", pill: "SP-12" },
  { id: "integrations", label: "Integrations", icon: "⇄", pill: "IN-01" },
  { id: "exports", label: "Exports", icon: "↓", pill: "DR-11" },
];

const workspaceCodes = [
  "employee",
  "people_manager",
  "competency_lead",
  "learning_and_development",
  "platform_admin",
];

export function AdminHome() {
  const { screen } = useParams();
  const current = screens.some((item) => item.id === screen) ? screen! : "access";
  const page = screens.find((item) => item.id === current)?.label ?? "Access and roles";
  return (
    <PersonaFrame
      label="Platform admin"
      kicker="Trust and admin"
      mark="PA"
      page={page}
      items={screens}
      currentId={current}
    >
      {current === "security" ? <SecurityPage /> : null}
      {current === "ai" ? <ModelsPage /> : null}
      {current === "audit" ? <AuditPage /> : null}
      {current === "malpractice" ? <MalpracticePage /> : null}
      {current === "integrations" ? <IntegrationsPage /> : null}
      {current === "exports" ? <ExportsPage /> : null}
      {current === "access" ? <AccessPage /> : null}
    </PersonaFrame>
  );
}

function AccessPage() {
  useTitle("Access and roles");
  const [grants, setGrants] = useState<AdminGrant[] | null>(null);
  const [subjectId, setSubjectId] = useState("");
  const [workspace, setWorkspace] = useState("employee");
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    setGrants(await getAdminGrants());
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The grants could not be loaded."));
  }, [load]);

  function grant(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setNotice(null);
    void grantWorkspace(subjectId.trim(), workspace)
      .then(() => load())
      .then(() => {
        setProblem(null);
        setNotice("The grant is recorded, with an audit row.");
        setSubjectId("");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The grant was refused."))
      .finally(() => setPending(false));
  }

  function revoke(row: AdminGrant) {
    setPending(true);
    setNotice(null);
    void revokeWorkspace(row.subjectId, row.workspace)
      .then(() => load())
      .then(() => {
        setProblem(null);
        setNotice("The grant is revoked, with an audit row.");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The revoke was refused."))
      .finally(() => setPending(false));
  }

  const people = grants ? new Set(grants.map((row) => row.subjectId)).size : null;
  const held = grants ? new Set(grants.map((row) => row.workspace)).size : null;

  return (
    <section data-screen="access">
      <PageHead
        eyebrow="DR-10 · role-based access"
        title="Who holds a workspace"
        lede="Workspace grants are explicit. Granting or revoking one writes an audit row in the same change. This command cannot open another person’s sitting."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p className="status-note" role="status">{notice}</p> : null}
      <div className="stat-row">
        <div className="stat-card">
          <span className="kicker">Grants</span>
          <strong>{grants ? grants.length : "—"}</strong>
          <span className="hint">Stored for this zone</span>
        </div>
        <div className="stat-card">
          <span className="kicker">People</span>
          <strong>{people ?? "—"}</strong>
          <span className="hint">Distinct sign-ins with a grant</span>
        </div>
        <div className="stat-card">
          <span className="kicker">Workspaces in use</span>
          <strong>{held ?? "—"}</strong>
          <span className="hint">Of the five this zone can grant</span>
        </div>
        <div className="stat-card ink">
          <span className="kicker">Workspaces defined</span>
          <strong>{workspaceCodes.length}</strong>
          <span className="hint">Roles this zone can grant</span>
        </div>
      </div>
      <article className="surface-card">
        <header>
          <div>
            <h2>Grants</h2>
            <p className="card-note">The people and workspaces actually stored</p>
          </div>
        </header>
        {!grants ? <p className="empty-note">The grants have not been loaded.</p> : null}
        {grants && grants.length === 0 ? <p className="empty-note">No workspace grant is stored.</p> : null}
        {grants && grants.length > 0 ? (
          <div className="team-scroll">
            <table className="team-table">
              <thead>
                <tr>
                  <th>Person</th>
                  <th>Workspace</th>
                  <th>Granted</th>
                  <th>Action</th>
                </tr>
              </thead>
              <tbody>
                {grants.map((row) => (
                  <tr key={row.grantId}>
                    <td>{row.subjectId}</td>
                    <td>{workspaceCopy(row.workspace).label}</td>
                    <td>{formatAsOf(row.grantedAt)}</td>
                    <td>
                      <button type="button" className="draft-save" disabled={pending} onClick={() => revoke(row)}>
                        Revoke {workspaceCopy(row.workspace).label}
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>
      <article className="surface-card">
        <header>
          <div>
            <h2>Workspaces this zone can grant</h2>
            <p className="card-note">The catalogue of roles, not a sample of people</p>
          </div>
        </header>
        <ul className="gap-list">
          {workspaceCodes.map((code) => {
            const copy = workspaceCopy(code);
            return (
              <li key={code}>
                <b>{copy.label}</b>
                <span>{copy.detail}</span>
              </li>
            );
          })}
        </ul>
      </article>
      <form className="task-close" onSubmit={grant}>
        <h2>Grant a workspace</h2>
        <label>
          Person
          <input value={subjectId} onChange={(event) => setSubjectId(event.target.value)} required autoComplete="off" />
        </label>
        <label>
          Workspace
          <select value={workspace} onChange={(event) => setWorkspace(event.target.value)}>
            {workspaceCodes.map((code) => (
              <option key={code} value={code}>{workspaceCopy(code).label}</option>
            ))}
          </select>
        </label>
        <button type="submit" className="draft-publish" disabled={pending}>Grant workspace</button>
      </form>
    </section>
  );
}

function SecurityPage() {
  useTitle("Security");
  const [residency, setResidency] = useState<ZoneResidency | null>(null);
  const [connectors, setConnectors] = useState<ConnectorStatus[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    void getAdminSecurity()
      .then((body) => {
        setResidency(body.residency);
        setConnectors(body.connectors);
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "Security status could not be loaded."));
  }, []);

  return (
    <section data-screen="security">
      <PageHead
        eyebrow="SP-01 · security"
        title="Residency for this zone"
        lede="Zone residency and connector status are read from the control registry. This page does not change encryption keys."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>Residency</h2>
            <p className="card-note">{residency ? residency.status : "Not loaded"}</p>
          </div>
        </header>
        {!residency && !problem ? <p className="empty-note">Residency has not been loaded.</p> : null}
        {!residency && problem ? <p className="empty-note">Residency is not available.</p> : null}
        {residency ? (
          <>
            <p>{residency.displayName} · {residency.zoneId} · {residency.status}</p>
            {residency.legalEntities.length === 0 ? <p className="empty-note">No legal entity is mapped to this zone.</p> : (
              <div className="team-scroll">
                <table className="team-table">
                  <thead>
                    <tr>
                      <th>Legal entity</th>
                      <th>Name</th>
                    </tr>
                  </thead>
                  <tbody>
                    {residency.legalEntities.map((entity) => (
                      <tr key={entity.legalEntityId}>
                        <td>{entity.legalEntityId}</td>
                        <td>{entity.displayName}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </>
        ) : null}
      </article>
      <article className="surface-card">
        <header>
          <div>
            <h2>Connectors</h2>
            <p className="card-note">Registered for this zone</p>
          </div>
        </header>
        <ConnectorTable connectors={connectors} />
      </article>
      <article className="surface-card">
        <header>
          <div>
            <h2>Certificates</h2>
            <p className="card-note">Nothing is stored on this read</p>
          </div>
        </header>
        <p className="empty-note">No certificate or penetration-test record is stored for this zone.</p>
      </article>
      <p className="callout"><i aria-hidden="true">i</i><span><b>Keys stay where they are.</b> This page has no control that rotates, revokes, or replaces an encryption key.</span></p>
    </section>
  );
}

function ModelsPage() {
  useTitle("Responsible AI");
  const [models, setModels] = useState<ModelRegistration[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    void getAdminModels()
      .then(setModels)
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The model register could not be loaded."));
  }, []);

  return (
    <section data-screen="ai">
      <PageHead
        eyebrow="SP-07 · responsible AI"
        title="Models that can touch a score"
        lede="The register lists a model only when one is connected. It stays empty until then. This page does not name a model that is not connected, and it does not change a key."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>Model register</h2>
            <p className="card-note">Name and purpose only</p>
          </div>
        </header>
        {!models && !problem ? <p className="empty-note">The register has not been loaded.</p> : null}
        {models && models.length === 0 ? <p className="empty-note">No model is connected.</p> : null}
        {models && models.length > 0 ? (
          <div className="team-scroll">
            <table className="team-table">
              <thead>
                <tr>
                  <th>Model</th>
                  <th>Purpose</th>
                </tr>
              </thead>
              <tbody>
                {models.map((model) => (
                  <tr key={model.modelId}>
                    <td>{model.modelName}</td>
                    <td>{model.purpose}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>
    </section>
  );
}

function AuditPage() {
  useTitle("Audit and retention");
  const [records, setRecords] = useState<AuditRecord[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    void getAdminAudit()
      .then(setRecords)
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The audit log could not be loaded."));
  }, []);

  return (
    <section data-screen="audit">
      <PageHead
        eyebrow="SP-10 · audit and retention"
        title="What was done, and to which record"
        lede="Opening this page writes an audit row for the read. A record names who did what. It does not contain response text or evidence bytes."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>Audit log</h2>
            <p className="card-note">{records ? `${records.length} records` : "Not loaded"}</p>
          </div>
        </header>
        {!records && !problem ? <p className="empty-note">The audit log has not been loaded.</p> : null}
        {records && records.length === 0 ? <p className="empty-note">No audit record is stored.</p> : null}
        {records && records.length > 0 ? (
          <div className="team-scroll">
            <table className="team-table">
              <thead>
                <tr>
                  <th>When</th>
                  <th>Who</th>
                  <th>Action</th>
                  <th>Record</th>
                </tr>
              </thead>
              <tbody>
                {records.map((record) => (
                  <tr key={record.auditId}>
                    <td>{formatAsOf(record.occurredAt)}</td>
                    <td>{record.subjectId}</td>
                    <td>{record.action}</td>
                    <td>{record.recordType}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>
      <article className="surface-card">
        <header>
          <div>
            <h2>Retention</h2>
            <p className="card-note">Not stored on this read</p>
          </div>
        </header>
        <p className="empty-note">Retention periods are not stored on this read. The log above does not include response text or evidence bytes.</p>
      </article>
    </section>
  );
}

function MalpracticePage() {
  useTitle("Malpractice");
  const [cases, setCases] = useState<MalpracticeCase[] | null>(null);
  const [sittingId, setSittingId] = useState("");
  const [status, setStatus] = useState("open");
  const [problem, setProblem] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    setCases(await getMalpracticeCases());
  }, []);

  useEffect(() => {
    void load().catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The cases could not be loaded."));
  }, [load]);

  function open(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setNotice(null);
    void openMalpracticeCase(sittingId.trim(), status)
      .then(() => load())
      .then(() => {
        setProblem(null);
        setNotice("The case stores the status and the sitting id.");
        setSittingId("");
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The case was refused."))
      .finally(() => setPending(false));
  }

  return (
    <section data-screen="malpractice">
      <PageHead
        eyebrow="SP-12 · malpractice"
        title="Cases on a sitting"
        lede="A case stores a status and a sitting id. It does not copy the response."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p className="status-note" role="status">{notice}</p> : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>Open cases</h2>
            <p className="card-note">{cases ? `${cases.length} stored` : "Not loaded"}</p>
          </div>
        </header>
        {!cases && !problem ? <p className="empty-note">The cases have not been loaded.</p> : null}
        {cases && cases.length === 0 ? <p className="empty-note">No case is open.</p> : null}
        {cases && cases.length > 0 ? (
          <div className="team-scroll">
            <table className="team-table">
              <thead>
                <tr>
                  <th>Sitting</th>
                  <th>Status</th>
                  <th>Opened by</th>
                  <th>When</th>
                </tr>
              </thead>
              <tbody>
                {cases.map((item) => (
                  <tr key={item.caseId}>
                    <td className="mono">{item.sittingId}</td>
                    <td><span className={item.status === "open" ? "chip amber" : "chip quiet"}>{item.status === "set_aside" ? "Set aside" : item.status === "explained" ? "Explained" : "Open"}</span></td>
                    <td>{item.openedBy}</td>
                    <td>{formatAsOf(item.openedAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>
      <form className="task-close" onSubmit={open}>
        <h2>Open a case</h2>
        <label>
          Sitting id
          <input value={sittingId} onChange={(event) => setSittingId(event.target.value)} required autoComplete="off" spellCheck={false} />
        </label>
        <label>
          Status
          <select value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="open">Open</option>
            <option value="explained">Explained</option>
            <option value="set_aside">Set aside</option>
          </select>
        </label>
        <button type="submit" className="draft-publish" disabled={pending}>Store case</button>
      </form>
      <p className="callout"><i aria-hidden="true">i</i><span><b>The response stays in the sitting.</b> This case does not store what the person wrote, and it does not change a score by itself.</span></p>
    </section>
  );
}

function IntegrationsPage() {
  useTitle("Integrations");
  const [connectors, setConnectors] = useState<ConnectorStatus[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    void getAdminIntegrations()
      .then(setConnectors)
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The connector registry could not be loaded."));
  }, []);

  const enabled = connectors?.filter((connector) => connector.enabled).length ?? null;

  return (
    <section data-screen="integrations">
      <PageHead
        eyebrow="IN-01 · integrations"
        title="Connectors registered for this zone"
        lede="Only connectors the registry returns are listed. This page does not add a feed that is not registered."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <div className="stat-row cols-2">
        <div className="stat-card">
          <span className="kicker">Registered</span>
          <strong>{connectors ? connectors.length : "—"}</strong>
          <span className="hint">Connectors on this zone</span>
        </div>
        <div className="stat-card">
          <span className="kicker">Enabled</span>
          <strong>{enabled ?? "—"}</strong>
          <span className="hint">Turned on in the registry</span>
        </div>
      </div>
      <article className="surface-card">
        <header>
          <div>
            <h2>Registry</h2>
            <p className="card-note">Type, status, and endpoint</p>
          </div>
        </header>
        <ConnectorTable connectors={connectors} />
      </article>
    </section>
  );
}

function ExportsPage() {
  useTitle("Exports");
  const [types, setTypes] = useState<ExportType[] | null>(null);
  const [subjectId, setSubjectId] = useState("");
  const [exported, setExported] = useState<PersonalMetadata | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  useEffect(() => {
    void getAdminExports()
      .then(setTypes)
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The export types could not be loaded."));
  }, []);

  function run(event: FormEvent) {
    event.preventDefault();
    setPending(true);
    setExported(null);
    void exportPersonalMetadata(subjectId.trim())
      .then((metadata) => {
        setExported(metadata);
        setProblem(null);
      })
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The export was refused."))
      .finally(() => setPending(false));
  }

  return (
    <section data-screen="exports">
      <PageHead
        eyebrow="DR-11 · export"
        title="Export types this zone offers"
        lede="Response text is not an export type. A personal metadata export writes an audit row in the same change and returns the person’s record, not a sitting."
      />
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <article className="surface-card">
        <header>
          <div>
            <h2>Types</h2>
            <p className="card-note">{types ? `${types.length} listed` : "Not loaded"}</p>
          </div>
        </header>
        {!types && !problem ? <p className="empty-note">The export types have not been loaded.</p> : null}
        {types && types.length === 0 ? <p className="empty-note">No export type is listed.</p> : null}
        {types && types.length > 0 ? (
          <div className="team-scroll">
            <table className="team-table">
              <thead>
                <tr>
                  <th>Type</th>
                  <th>What it contains</th>
                </tr>
              </thead>
              <tbody>
                {types.map((type) => (
                  <tr key={type.code}>
                    <td>{type.code}</td>
                    <td>{type.description}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </article>
      <form className="task-close" onSubmit={run}>
        <h2>Personal metadata</h2>
        <label>
          Person
          <input value={subjectId} onChange={(event) => setSubjectId(event.target.value)} required autoComplete="off" />
        </label>
        <button type="submit" className="draft-publish" disabled={pending}>Export personal metadata</button>
      </form>
      {exported ? (
        <article className="surface-card">
          <header>
            <div>
              <h2>Export</h2>
              <p className="card-note">Personal metadata only</p>
            </div>
          </header>
          <dl className="export-meta">
            <div><dt>Person</dt><dd>{exported.subjectId}</dd></div>
            <div><dt>Name</dt><dd>{exported.displayName}</dd></div>
            <div><dt>Legal entity</dt><dd>{exported.legalEntityId}</dd></div>
            <div><dt>Country</dt><dd>{exported.countryCode}</dd></div>
            <div><dt>Employment</dt><dd>{exported.employmentStatus}</dd></div>
            <div><dt>Workspaces</dt><dd>{exported.workspaces.length === 0 ? "None" : exported.workspaces.map((code) => workspaceCopy(code).label).join(", ")}</dd></div>
          </dl>
        </article>
      ) : null}
    </section>
  );
}

function ConnectorTable({ connectors }: { connectors: ConnectorStatus[] | null }) {
  if (!connectors) return <p className="empty-note">Connectors have not been loaded.</p>;
  if (connectors.length === 0) return <p className="empty-note">No connector is registered for this zone.</p>;
  return (
    <div className="team-scroll">
      <table className="team-table">
        <thead>
          <tr>
            <th>Connector</th>
            <th>Status</th>
            <th>Endpoint</th>
          </tr>
        </thead>
        <tbody>
          {connectors.map((connector) => (
            <tr key={connector.connectorId}>
              <td>{connector.connectorType}</td>
              <td><span className={connector.enabled ? "chip mint" : "chip quiet"}>{connector.enabled ? "Enabled" : "Off"}</span></td>
              <td>{connector.endpoint ?? "Not set"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
