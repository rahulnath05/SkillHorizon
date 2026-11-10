export type WorkspaceCopy = {
  label: string;
  detail: string;
  initials: string;
};

const workspaces: Record<string, WorkspaceCopy> = {
  employee: {
    label: "Employee",
    detail: "Your own skills, path, and evidence.",
    initials: "E",
  },
  people_manager: {
    label: "People manager",
    detail: "Coaching and readiness for the people who report to you.",
    initials: "PM",
  },
  competency_lead: {
    label: "Competency lead",
    detail: "Practice packs and the baseline you seal.",
    initials: "CL",
  },
  learning_and_development: {
    label: "L&D",
    detail: "Taxonomy, benchmarks, assessment, and activation.",
    initials: "LD",
  },
  platform_admin: {
    label: "Platform admin",
    detail: "Access, retention, and integration health.",
    initials: "PA",
  },
};

export function initials(value: string): string {
  const parts = value.split(/[\s._-]+/).filter((part) => part.length > 0);
  if (parts.length === 0) return "S";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[1][0]}`.toUpperCase();
}

export function workspaceCopy(code: string): WorkspaceCopy {
  return workspaces[code] ?? {
    label: code,
    detail: "A workspace granted to this sign-in.",
    initials: initials(code),
  };
}
