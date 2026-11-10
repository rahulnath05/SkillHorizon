import { useCallback, useEffect, useState } from "react";
import { getStandards, publishBenchmark, saveBenchmarkDraft, type BenchmarkVersion, type Standards } from "./api";
import { useTitle } from "./ui";

export function StandardsAuthoring() {
  useTitle("Benchmarks");
  return <StandardsPanel mode="authoring" primary />;
}

export function PublishedStandards() {
  return <StandardsPanel mode="published" primary={false} />;
}

function StandardsPanel({ mode, primary }: { mode: "authoring" | "published"; primary: boolean }) {
  const [standards, setStandards] = useState<Standards | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const load = useCallback(async () => {
    try {
      setStandards(await getStandards());
      setProblem(null);
    } catch (error) {
      setProblem(error instanceof Error ? error.message : "The standards could not be loaded.");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const authoring = mode === "authoring" && standards?.source === "authoring";

  return (
    <section className="standards" aria-labelledby="standards-title" data-screen={authoring ? "benchmark-authoring" : "benchmark-published"}>
      <p className="eyebrow">{authoring ? "Authoring zone" : "Zone reference"}</p>
      {primary ? (
        <h1 id="standards-title">{authoring ? "Benchmarks" : "Published benchmark"}</h1>
      ) : (
        <h2 id="standards-title">Published benchmark</h2>
      )}
      <p>
        {authoring
          ? "Drafts are edited in this workspace. A published version is copied into the zone. Employees and people managers see that copy."
          : "This is the published version copied into the zone. Drafts stay in the authoring zone."}
      </p>
      {problem ? (
        <p className="gate-alert" role="alert">
          {problem}
          <button type="button" className="team-retry" onClick={() => void load()}>Try again</button>
        </p>
      ) : null}
      {standards ? <FrameworkList frameworks={standards.frameworks} /> : null}
      {authoring && standards ? standards.drafts.map((bundle) => (
        <DraftEditor
          key={bundle.draft.benchmarkVersionId}
          version={bundle.draft}
          proficiencies={bundle.proficiencies}
          pending={pending}
          onSave={async (targets) => {
            setPending(true);
            try {
              await saveBenchmarkDraft({
                jobProfileId: bundle.draft.jobProfileId,
                taxonomyVersionId: bundle.draft.taxonomyVersionId,
                targets,
              });
              await load();
            } catch (error) {
              setProblem(error instanceof Error ? error.message : "The draft could not be saved.");
            } finally {
              setPending(false);
            }
          }}
          onPublish={async () => {
            setPending(true);
            try {
              await publishBenchmark(bundle.draft.benchmarkVersionId);
              await load();
            } catch (error) {
              setProblem(error instanceof Error ? error.message : "The benchmark could not be published.");
            } finally {
              setPending(false);
            }
          }}
        />
      )) : null}
      {standards ? <History versions={standards.history} authoring={authoring} /> : null}
    </section>
  );
}

function FrameworkList({ frameworks }: { frameworks: Standards["frameworks"] }) {
  if (frameworks.length === 0) return null;
  return (
    <div className="team-scroll">
      <table className="team-table">
        <caption className="sr">Frameworks and levels</caption>
        <thead>
          <tr>
            <th scope="col">Framework</th>
            <th scope="col">Level</th>
            <th scope="col">Rank</th>
          </tr>
        </thead>
        <tbody>
          {frameworks.flatMap((framework) => framework.levels.map((level) => (
            <tr key={level.levelId}>
              <td>{framework.name}</td>
              <td>{level.name}</td>
              <td>{level.rank}</td>
            </tr>
          )))}
        </tbody>
      </table>
    </div>
  );
}

function DraftEditor({
  version,
  proficiencies,
  pending,
  onSave,
  onPublish,
}: {
  version: BenchmarkVersion;
  proficiencies: Standards["drafts"][number]["proficiencies"];
  pending: boolean;
  onSave: (targets: { skillId: string; proficiencyRank: number }[]) => Promise<void>;
  onPublish: () => Promise<void>;
}) {
  const [ranks, setRanks] = useState<Record<string, number>>(() =>
    Object.fromEntries(version.targets.map((target) => [target.skillId, target.proficiencyRank])));

  useEffect(() => {
    setRanks(Object.fromEntries(version.targets.map((target) => [target.skillId, target.proficiencyRank])));
  }, [version]);

  return (
    <form
      className="draft"
      data-draft={version.benchmarkVersionId}
      onSubmit={(event) => {
        event.preventDefault();
        void onSave(version.targets.map((target) => ({ skillId: target.skillId, proficiencyRank: ranks[target.skillId] ?? target.proficiencyRank })));
      }}
    >
      <h2>Draft for {version.profileName}</h2>
      <p>Version {version.versionNumber}. This draft is not in the zone reference store.</p>
      <div className="team-scroll">
        <table className="team-table">
          <caption className="sr">Draft skill targets</caption>
          <thead>
            <tr>
              <th scope="col">Skill</th>
              <th scope="col">Target</th>
            </tr>
          </thead>
          <tbody>
            {version.targets.map((target) => (
              <tr key={target.skillId} data-skill={target.skillName}>
                <td>
                  <strong>{target.skillName}</strong>
                  <span className="team-id">{target.practiceName} · {target.competencyName}</span>
                </td>
                <td>
                  <label className="sr" htmlFor={`rank-${target.skillId}`}>Target for {target.skillName}</label>
                  <select
                    id={`rank-${target.skillId}`}
                    value={ranks[target.skillId] ?? target.proficiencyRank}
                    onChange={(event) => setRanks((current) => ({ ...current, [target.skillId]: Number(event.target.value) }))}
                  >
                    {proficiencies.map((item) => (
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
        <button type="button" className="draft-publish" disabled={pending} onClick={() => void onPublish()}>Publish version</button>
      </p>
    </form>
  );
}

function History({ versions, authoring }: { versions: BenchmarkVersion[]; authoring: boolean }) {
  return (
    <div className="history">
      <h2>{authoring ? "Published history" : "Versions in this zone"}</h2>
      {versions.length === 0 ? <p>No published version has been copied into this zone yet.</p> : (
        <div className="team-scroll">
          <table className="team-table">
            <caption className="sr">Published benchmark versions</caption>
            <thead>
              <tr>
                <th scope="col">Version</th>
                <th scope="col">Profile</th>
                <th scope="col">Status</th>
                <th scope="col">Targets</th>
              </tr>
            </thead>
            <tbody>
              {versions.map((version) => (
                <tr key={version.benchmarkVersionId} data-version={version.versionNumber} data-status={version.status}>
                  <td>{version.versionNumber}</td>
                  <td>
                    <strong>{version.profileName}</strong>
                    <span className="team-id">{version.levelName} · rank {version.levelRank}</span>
                  </td>
                  <td>{version.status}</td>
                  <td>{version.targets.map((target) => `${target.skillName} · ${target.proficiencyName}`).join(", ")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
