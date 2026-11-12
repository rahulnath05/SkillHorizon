import { useCallback, useEffect, useState, type FormEvent } from "react";
import { NavLink, useParams } from "react-router-dom";
import {
  exportPersonalMetadata,
  getAdminAudit,
  getAdminExports,
  getAdminGrants,
  getAdminIntegrations,
  getAdminModels,
  getMalpracticeCases,
  getAdminSecurity,
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
import { formatAsOf, useTitle } from "./ui";
import { workspaceCopy } from "./workspaces";

const screens = [
  { id: "access", label: "Access and roles" },
  { id: "security", label: "Security" },
  { id: "ai", label: "Responsible AI" },
  { id: "audit", label: "Audit and retention" },
  { id: "malpractice", label: "Malpractice" },
  { id: "integrations", label: "Integrations" },
  { id: "exports", label: "Exports" },
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
  return (
    <div className="home admin-home">
      <nav className="home-nav" aria-label="Platform admin">
        {screens.map((item) => (
          <NavLink key={item.id} to={`/workspace/${item.id}`} aria-current={current === item.id ? "page" : undefined}>
            {item.label}
          </NavLink>
        ))}
      </nav>
      <div className="home-main">
        {current === "security" ? <SecurityPage /> : null}
        {current === "ai" ? <ModelsPage /> : null}
        {current === "audit" ? <AuditPage /> : null}
        {current === "malpractice" ? <MalpracticePage /> : null}
        {current === "integrations" ? <IntegrationsPage /> : null}
        {current === "exports" ? <ExportsPage /> : null}
        {current === "access" ? <AccessPage /> : null}
      </div>
    </div>
  );
}

function AccessPage() {
  useTitle("Access and roles");
  const [grants, setGrants] = useState<AdminGrant[]>([]);
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

  return (
    <section data-screen="access">
      <p className="eyebrow">Trust and admin</p>
      <h1>Access and roles</h1>
      <p>Workspace grants are explicit. Granting or revoking one writes an audit row in the same change. This command cannot open another person’s sitting.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {grants.length === 0 ? <p>No workspace grant is stored.</p> : (
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
                    <button
                      type="button"
                      className="draft-save"
                      disabled={pending}
                      onClick={() => revoke(row)}
                    >
                      Revoke {workspaceCopy(row.workspace).label}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
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
  const [connectors, setConnectors] = useState<ConnectorStatus[]>([]);
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
      <p className="eyebrow">Trust and admin</p>
      <h1>Security</h1>
      <p>Zone residency and connector status are read from the control registry. This page does not change encryption keys.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {residency ? (
        <>
          <h2>Residency</h2>
          <p>{residency.displayName} · {residency.zoneId} · {residency.status}</p>
          {residency.legalEntities.length === 0 ? <p>No legal entity is mapped to this zone.</p> : (
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
      <ConnectorTable connectors={connectors} />
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
      <p className="eyebrow">Trust and admin</p>
      <h1>Responsible AI</h1>
      <p>Models allowed to touch a score. The register is empty while no model is connected.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {models && models.length === 0 ? <p>No model is connected.</p> : null}
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
    </section>
  );
}

function AuditPage() {
  useTitle("Audit and retention");
  const [records, setRecords] = useState<AuditRecord[]>([]);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    void getAdminAudit()
      .then(setRecords)
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The audit log could not be loaded."));
  }, []);

  return (
    <section data-screen="audit">
      <p className="eyebrow">Trust and admin</p>
      <h1>Audit and retention</h1>
      <p>Opening this page writes an audit row for the read. A record names who did what. It does not contain response text or evidence bytes.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {records.length === 0 ? <p>No audit record is stored.</p> : (
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
      )}
    </section>
  );
}

function MalpracticePage() {
  useTitle("Malpractice");
  const [cases, setCases] = useState<MalpracticeCase[]>([]);
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
      <p className="eyebrow">Trust and admin</p>
      <h1>Malpractice</h1>
      <p>A case stores a status and a sitting id. It does not copy the response.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {notice ? <p role="status">{notice}</p> : null}
      {cases.length === 0 ? <p>No case is open.</p> : (
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
                  <td>{item.status === "set_aside" ? "Set aside" : item.status === "explained" ? "Explained" : "Open"}</td>
                  <td>{item.openedBy}</td>
                  <td>{formatAsOf(item.openedAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
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
    </section>
  );
}

function IntegrationsPage() {
  useTitle("Integrations");
  const [connectors, setConnectors] = useState<ConnectorStatus[]>([]);
  const [problem, setProblem] = useState<string | null>(null);

  useEffect(() => {
    void getAdminIntegrations()
      .then(setConnectors)
      .catch((error: unknown) => setProblem(error instanceof Error ? error.message : "The connector registry could not be loaded."));
  }, []);

  return (
    <section data-screen="integrations">
      <p className="eyebrow">Trust and admin</p>
      <h1>Integrations</h1>
      <p>Connectors registered for this zone.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      <ConnectorTable connectors={connectors} />
    </section>
  );
}

function ExportsPage() {
  useTitle("Exports");
  const [types, setTypes] = useState<ExportType[]>([]);
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
      <p className="eyebrow">Trust and admin</p>
      <h1>Exports</h1>
      <p>Response text is not an export type. A personal metadata export writes an audit row in the same change.</p>
      {problem ? <p className="gate-alert" role="alert">{problem}</p> : null}
      {types.length === 0 ? <p>No export type is listed.</p> : (
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
      )}
      <form className="task-close" onSubmit={run}>
        <h2>Personal metadata</h2>
        <label>
          Person
          <input value={subjectId} onChange={(event) => setSubjectId(event.target.value)} required autoComplete="off" />
        </label>
        <button type="submit" className="draft-publish" disabled={pending}>Export personal metadata</button>
      </form>
      {exported ? (
        <dl className="export-meta">
          <div><dt>Person</dt><dd>{exported.subjectId}</dd></div>
          <div><dt>Name</dt><dd>{exported.displayName}</dd></div>
          <div><dt>Legal entity</dt><dd>{exported.legalEntityId}</dd></div>
          <div><dt>Country</dt><dd>{exported.countryCode}</dd></div>
          <div><dt>Employment</dt><dd>{exported.employmentStatus}</dd></div>
          <div><dt>Workspaces</dt><dd>{exported.workspaces.length === 0 ? "None" : exported.workspaces.map((code) => workspaceCopy(code).label).join(", ")}</dd></div>
        </dl>
      ) : null}
    </section>
  );
}

function ConnectorTable({ connectors }: { connectors: ConnectorStatus[] }) {
  if (connectors.length === 0) return <p>No connector is registered for this zone.</p>;
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
              <td>{connector.enabled ? "Enabled" : "Off"}</td>
              <td>{connector.endpoint ?? "Not set"}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
