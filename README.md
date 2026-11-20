# Urfair

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
| `src/Urfair.Core` | Allocation algorithm, validation, sample data (no dependencies) |
| `src/Urfair.Api` | ASP.NET Core minimal API + EF Core; also serves the built web app |
| `tests/Urfair.Core.Tests` | xUnit tests for the allocation rules |
| `web` | Angular app (German/English): start page, voting, organisation area |
| `index.html` | Redirect from the former GitHub Pages demo to the hosted app (the first prototype is in the git history) |

## Concepts

- **Vote** (in code: `FairSlot`): one allocation round (e.g. "Confirmation preparation 2026/27") with its own rules, activities, dates, participant list and registrations.
- **Phase**: Draft → Open (participants can vote) → Closed → Published (participants see their own result).
- **Participant list**: only listed people see a vote. Demo: identified by name; later e-mail, CSV import and personal links.
- **Organisation area** (`/admin`): create votes, edit activities and dates, participants, settings and rules, demand, allocation, sample data.
- **Participant area** (`/slots`, `/s/{id}`): own votes, voting, own result.

## Run locally

Requirements: .NET SDK 10, Node.js 24. Docker is not needed locally.

```
cd web && npm ci && npx ng build        # builds the UI into src/Urfair.Api/wwwroot
cd ../src/Urfair.Api && dotnet run
```

Open http://localhost:5080. A local SQLite file is created with the demo votes.

### Database changes (migrations)

The schema is managed with EF Core migrations, applied automatically at start-up. Migrations are
provider-specific, so every model change needs one migration for SQLite (local) and one for PostgreSQL (hosted):

```
dotnet tool restore
dotnet ef migrations add <Name> -p src/Urfair.Api -c SqliteAppDbContext -o Data/Migrations/Sqlite
dotnet ef migrations add <Name> -p src/Urfair.Api -c PostgresAppDbContext -o Data/Migrations/Postgres
```

In demo mode the organisation area has a button to reset the whole demo.

### Participants and personal links

The participant list of a vote is maintained by hand or imported from CSV (`Name` or `Vorname`+`Nachname`, `E-Mail`,
`Geburtsdatum`, `Pfarrgemeinde`; known names are completed, nobody is deleted). Everyone gets a personal link `/p/{token}`;
if a date of birth is stored it is asked after the click. Invitations with the link are prepared in the vote's
"Einladungen" tab: in demo mode as a demo mailbox, otherwise as texts to copy (the app does not send e-mails yet).
Outside demo mode participants can only sign in with their link.

### Organiser accounts and roles

Organisers sign in with e-mail and password (cookie; keys stored in the database so sign-ins survive restarts).
Junia admins manage accounts under "Konten": a new account is assigned to Junia (may do everything) or to one or more
Pfarrgemeinden and gets a one-time link (7 days) to set its password; the link is passed on personally.
Every vote has a visibility: *Pfarrgemeinde intern*, *Junia gemeinsam* or *Junia, nur lesen*. The server checks every
request of the organisation area (`src/Urfair.Api/Access.cs`); Pfarrgemeinde admins only see and edit participants of
their own Pfarrgemeinden in Junia-wide votes. In demo mode, three demo accounts sign in with a button.

### Importing activities

Organisers can download the activities of a vote as CSV (`;`, UTF-8), edit the list in Excel and import it again
(preview first, then add or replace). One row per date; rows with the same title form one activity.
Columns: `Titel;Beschreibung;Gruppe;Pfarre;Leitung;Teilnahme;Beginn;Ende;Ort;Plätze;Nach Vereinbarung` (English names work too;
only `Titel` and `Beginn` are required; `Pfarre` and `Leitung` are left empty when they match the vote).

For UI work with live reload: run the API as above and `npx ng serve` in `web` (http://localhost:4200, `/api` is proxied).

Tests: `dotnet test Urfair.slnx`

## Configuration

| Setting | Purpose |
|---|---|
| `DATABASE_URL` | PostgreSQL URL (`postgres://user:pass@host/db`). If set, PostgreSQL is used instead of SQLite. |
| `ConnectionStrings__Default` | Alternative connection string (SQLite by default). |
| `Admin__Key` | Real instance only: key to create the very first Junia admin account (start page, while no account exists). Not used afterwards. |
| `Demo__Enabled` | `true` (default): demo people, sample data and reset buttons. Set `false` for a real instance. |
| `App__BaseUrl` | Public address used in invitation links (e.g. `https://urfair-prod.onrender.com`). Default: the request host. |

See [ROADMAP.md](ROADMAP.md) for the plan and [CLAUDE.md](CLAUDE.md) for the working rules of this project.

## Deployment

The `Dockerfile` builds one container (UI + API, port 8080). GitHub Actions runs the tests and builds
the image on every push and pull request.

## Dependencies and licences

Only permissively licensed, free components: .NET / ASP.NET Core / EF Core and the dotnet-ef tool (MIT), Npgsql (PostgreSQL licence),
SQLite (public domain), Angular (MIT). No external fonts, CDNs or trackers.

## Licence

MIT, see [LICENSE](LICENSE). Free to use, without warranty or liability.
© 2026 Thomas Scalet-Binder
