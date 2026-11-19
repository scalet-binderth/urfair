# UrFairSlot

Fair seat allocation for limited activities: workshops, project weeks, holiday programmes,
confirmation preparation.

The name combines **Urfahr**, the district of Linz where the tool was born, with Austrian
"ur fair" ("really fair"). It was first built for the confirmation preparation of the
Pfarre Urfahr St. Junia, after all seats were gone minutes after registration opened.

Instead of "whoever clicks first gets the seat", every participant spreads a point budget
(e.g. 100 points) over the activities that matter to them. After registration closes the
system assigns the seats:

1. Seats are handed out in rounds; in each round every participant gets one activity.
2. Within a round, more points win. Ties are decided by lot, never by time.
3. Full activities and clashing dates are skipped; if nothing with points is left, a fallback activity with free seats is assigned.
4. Finally, participants swap whenever neither ends up worse off.

## Structure

| Path | Content |
|---|---|
| `src/UrFairSlot.Core` | Allocation algorithm, validation, sample data (no dependencies) |
| `src/UrFairSlot.Api` | ASP.NET Core minimal API + EF Core; also serves the built web app |
| `tests/UrFairSlot.Core.Tests` | xUnit tests for the allocation rules |
| `web` | Angular app (German/English): start page, voting per FairSlot, organisation area |

## Concepts

- **FairSlot**: one allocation round (e.g. "Confirmation preparation 2026/27") with its own rules, activities, dates and registrations.
- **Phase**: Draft → Open (participants can vote) → Closed → Published.
- **Organisation area** (`/admin`): create FairSlots, edit activities and dates, settings and rules, demand, allocation, sample data.
- **Voting page** (`/s/{id}`): the link participants receive.
| `index.html` | Redirect from the former GitHub Pages demo to the hosted app (the first prototype is in the git history) |

## Run locally

Requirements: .NET SDK 10, Node.js 24. Docker is not needed locally.

```
cd web && npm ci && npx ng build        # builds the UI into src/UrFairSlot.Api/wwwroot
cd ../src/UrFairSlot.Api && dotnet run
```

Open http://localhost:5080. A local SQLite file is created with a demo FairSlot.

While only demo data exists, a change of the database schema (`SchemaVersion` in `Database.cs`) recreates the
database instead of migrating it. Switch to EF Core migrations before real registrations are stored.

For UI work with live reload: run the API as above and `npx ng serve` in `web` (http://localhost:4200, `/api` is proxied).

Tests: `dotnet test UrFairSlot.slnx`

## Configuration

| Setting | Purpose |
|---|---|
| `DATABASE_URL` | PostgreSQL URL (`postgres://user:pass@host/db`). If set, PostgreSQL is used instead of SQLite. |
| `ConnectionStrings__Default` | Alternative connection string (SQLite by default). |
| `Admin__Key` | If set, the organisation area requires this key. Leave empty for open demos. |

## Deployment

The `Dockerfile` builds one container (UI + API, port 8080). GitHub Actions runs the tests and builds
the image on every push and pull request.

## Dependencies and licences

Only permissively licensed, free components: .NET / ASP.NET Core / EF Core (MIT), Npgsql (PostgreSQL licence),
SQLite (public domain), Angular (MIT). No external fonts, CDNs or trackers.

## Licence

MIT, see [LICENSE](LICENSE). Free to use, without warranty or liability.
© 2026 Thomas Scalet-Binder
