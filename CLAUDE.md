# Working on Urfair

Read `README.md` (setup, structure, configuration) and `ROADMAP.md` (plan and next steps) first.

## Context

- Volunteer, free, open-source project (MIT, © Thomas Scalet-Binder) for the Pfarre Urfahr St. Junia, Linz.
  Nobody pays for it and there is no budget for licences or penalties.
- Live demo: https://urfair.onrender.com (Render free tier, deploys `main`; database: Neon PostgreSQL, Frankfurt).
  Render and Neon settings belong to the owner; never handle their passwords or connection strings.

## Rules

- **Language:** code, comments, identifiers, branch names, commit messages and repo docs in English.
  UI texts in German with an English switch (`web/src/app/i18n.ts`, both dictionaries). Talk to the owner in German.
- **Minimal and free:** the smallest working solution, standard libraries first. New dependencies only with a
  permissive free licence (MIT, Apache, BSD, PostgreSQL); name the licence when adding one. No paid services,
  no external fonts/CDNs/trackers (GDPR).
- **Always a running demo:** `main` must stay deployable. Small feature branches (`feature/...`), pull request,
  CI green (`.github/workflows/ci.yml`: `dotnet test`, `ng build`, `docker build`), then merge.
- **Ask first:** commit, push and merge only after the owner says so.
- **Database changes:** add a migration for both providers (commands in README). Demo data is seeded in
  `src/Urfair.Api/Data/Database.cs`; real instances run with `Demo__Enabled=false`.
- **Roadmap:** when a work package is done or added, update `ROADMAP.md` and the roadmap page `web/src/app/plan.ts`.

## Handy

- Build UI: `cd web && npx ng build` (output goes to `src/Urfair.Api/wwwroot`). API: `dotnet run --project src/Urfair.Api --urls http://localhost:5080`.
- Stop a running API before rebuilding (Windows locks the DLLs).
- Corporate laptop: winget community source is blocked by policy; do not work around it.
