import { useCallback, useEffect, useState } from "react";
import { getTeam, type DirectReport } from "./api";
import { useTitle } from "./ui";

export function TeamDirectory() {
  const [people, setPeople] = useState<DirectReport[] | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  useTitle("Your team");

  const load = useCallback(async () => {
    try {
      setPeople(await getTeam());
      setProblem(null);
    } catch (error) {
      setPeople(null);
      setProblem(error instanceof Error ? error.message : "The team list could not be loaded.");
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <section className="team" aria-labelledby="team-title" data-screen="team">
      <p className="eyebrow">Direct reports</p>
      <h1 id="team-title">Your team</h1>
      <p>
        See where each person is in the cycle without reducing development to a single score or ranking people against one another.
      </p>
      {problem ? (
        <p className="gate-alert" role="alert">
          {problem}
          <button type="button" className="team-retry" onClick={() => void load()}>Try again</button>
        </p>
      ) : null}
      {people === null && !problem ? <p role="status">Loading the team…</p> : null}
      {people && people.length === 0 ? <p>No one reports to you.</p> : null}
      {people && people.length > 0 ? (
        <div className="team-scroll">
          <table className="team-table">
            <caption className="sr">People who report to you</caption>
            <thead>
              <tr>
                <th scope="col">Person</th>
                <th scope="col">Country</th>
                <th scope="col">Legal entity</th>
              </tr>
            </thead>
            <tbody>
              {people.map((person) => (
                <tr key={person.employeeId} data-report={person.employeeId}>
                  <td>
                    <strong>{person.displayName}</strong>
                    <span className="team-id">{person.employeeId}</span>
                  </td>
                  <td>{person.countryCode}</td>
                  <td>{person.legalEntityId}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </section>
  );
}
