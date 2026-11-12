import type { ReactNode } from "react";
import { Link, NavLink } from "react-router-dom";

export type RailItem = {
  id: string;
  label: string;
  icon: string;
  count?: number;
  pill?: string;
};

export type StudioItem = {
  id: string;
  label: string;
  icon: string;
  pill?: string;
};

export function PersonaFrame({
  label,
  kicker,
  mark,
  section,
  page,
  items,
  currentId,
  highlightId,
  studio,
  children,
}: {
  label: string;
  kicker: string;
  mark: string;
  section?: string;
  page: string;
  items: RailItem[];
  currentId: string;
  highlightId?: string;
  studio?: {
    parentId: string;
    open: boolean;
    currentId: string;
    items: StudioItem[];
  } | null;
  children: ReactNode;
}) {
  const today = new Date().toLocaleDateString("en-GB", {
    timeZone: "Asia/Kolkata",
    weekday: "long",
    day: "numeric",
    month: "long",
  });
  const highlighted = highlightId ?? currentId;

  return (
    <div className="persona-app">
      <aside className="persona-rail" aria-label={`${label} screens`}>
        <div className="persona-who">
          <span className="persona-av" aria-hidden="true">{mark}</span>
          <div>
            <b>{label}</b>
            <span>{kicker}</span>
          </div>
        </div>
        <p className="persona-navlab">Screens</p>
        <nav className="persona-nav">
          {items.map((item) => {
            const open = Boolean(studio && studio.parentId === item.id && studio.open);
            const marked = item.id === currentId || item.id === highlighted || open;
            return (
              <div key={item.id} className={studio?.parentId === item.id ? "persona-branch" : undefined}>
                <Link
                  to={`/workspace/${item.id}`}
                  className={marked ? "is-current" : undefined}
                  aria-current={!open && item.id === currentId ? "page" : undefined}
                  aria-expanded={studio?.parentId === item.id ? open : undefined}
                >
                  <i aria-hidden="true">{item.icon}</i>
                  <span>{item.label}</span>
                  {item.pill ? <small>{item.pill}</small> : null}
                  {item.count && item.count > 0 ? <em>{item.count}</em> : null}
                </Link>
                {open && studio ? (
                  <div className="persona-sub" role="group" aria-label="Assessment and programme">
                    {studio.items.map((sub) => {
                      const current = studio.currentId === sub.id;
                      return (
                        <NavLink
                          key={sub.id}
                          to={`/workspace/${sub.id}`}
                          aria-current={current ? "page" : undefined}
                          className={current ? "is-current" : undefined}
                        >
                          <i aria-hidden="true">{sub.icon}</i>
                          <span>{sub.label}</span>
                          {sub.pill ? <small>{sub.pill}</small> : null}
                        </NavLink>
                      );
                    })}
                  </div>
                ) : null}
              </div>
            );
          })}
        </nav>
        <div className="persona-railfoot">
          <button
            type="button"
            className="mira-rail"
            aria-disabled="true"
            title="Mira is not available yet"
            onClick={(event) => event.preventDefault()}
          >
            <span className="mira-orb" aria-hidden="true" />
            Mira
          </button>
          <p>Mira stays off until that assistant is built.</p>
        </div>
      </aside>
      <div className="persona-main">
        <header className="persona-top">
          <p className="persona-crumb">
            <b>{label}</b>
            {section ? <> / {section}</> : null}
            {" / "}
            {page}
          </p>
          <div className="persona-top-actions">
            <button
              type="button"
              className="mira-top"
              aria-disabled="true"
              title="Mira is not available yet"
              onClick={(event) => event.preventDefault()}
            >
              Work with Mira
            </button>
            <span className="persona-date">{today}</span>
          </div>
        </header>
        <div className="persona-page">{children}</div>
      </div>
    </div>
  );
}

export function PageHead({
  eyebrow,
  title,
  lede,
  action,
}: {
  eyebrow: string;
  title: string;
  lede?: string;
  action?: ReactNode;
}) {
  return (
    <header className="page-head">
      <div>
        <p className="eyebrow">{eyebrow}</p>
        <h1>{title}</h1>
        {lede ? <p className="lede">{lede}</p> : null}
      </div>
      {action ? <div className="page-head-action">{action}</div> : null}
    </header>
  );
}
