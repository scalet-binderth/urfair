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
- **Phase**: Draft → Open (registration: participants give points) → Allocation (internal) → Published (allocation
  visible to participants) → Closed (lists final) → Ended (read only). At the registration deadline an open vote
  switches to Allocation by itself; after the last date it becomes Ended (checked every minute, Vienna time).
  Participants see an interim status during registration only if the vote has "show interim status" switched on.
- **Required staffing and more than required** (rules of a vote): with "all posts must be filled" every date with a
  seat count shows participants how many people are still needed, even without the interim status. "More than
  required" lets participants offer up to N extra seats (maximum set by the organisers, `Ballots.Extra`); extra seats
  come after the basic round, only in activities they gave points to, and never cost anyone a seat.
- **Frozen allocation**: during draft and registration the allocation is recalculated on every request (live preview).
  From Allocation on it is frozen; organisers move people by hand, lock seats and recalculate the rest. Going back to
  registration is possible only from the internal allocation and unfreezes it. Freezing
  keeps the original allocation (organisers can go back to it) and a log records every change, including phase changes.
- **Undo with save points** (`SavePoints.cs`): after every change to seats (frozen, recalculated, by hand, moving up,
  giving back, cancelling, restoring) the seats, fixed marks and ticks of all ballots are stored as a save point. In the
  log, "Zu diesem Stand zurück" shows a preview (who gains or loses which seat) and sets the allocation back after a
  confirmation. Seats a participant gave back stay given back; while visible or closed, affected people get "Deine
  Plätze"; restoring is logged and can itself be undone. Only states since the last freeze can be restored.
- **Moving up** (phase Published): no recalculation any more. Participants tick activities to move up into, give back
  a seat or take a free one; free seats fill by themselves within the activity group, need before wish, more points
  first (`Allocator.FillFreeSeats`, `MovingUp.cs`). Whoever moves up gives up their weakest seat of the group unless
  they chose another one next to the tick (`Ballots.GiveUp`, `Allocator.GiveUpOptions`). Every step is logged.
- **Filling a seat** (phase Closed, `SeatOffers.cs`): nothing moves by itself. Participants can still cancel; for a free
  seat organisers start "Platz nachbesetzen", which offers it to one person at a time in the order of moving up (e-mail
  with the personal link, 48 hours, at most until a day before the date; not for dates less than two days ahead).
  Declined or expired: the next person; accepted: the seat given up is offered next; nobody left: e-mail to the
  organisers. People who still need a replacement get one-click suggestions in the allocation (free seats, or
  overbooking their best full wish).
- **Automatic deletion** (`Retention.cs`): a vote that has ended (by itself or by hand) is deleted with everything in
  it (participants, points, seats, mails, log) after `Retention__Days` days (default 90). Organisers and participants
  see the date; going back from Ended stops the countdown. Votes that never end are not deleted automatically.
- **Legal notice and privacy policy** (`/impressum`, `/datenschutz`, links in the footer): texts from `Legal__Imprint`
  and `Legal__Privacy`; without them the pages show a marked German template with placeholders.
- **Lists for leaders**: tab "Listen" of a vote, one list per date grouped by leader, to print, download as CSV or
  prepare as one mail per leader (needs the leader's e-mail on the activity, which participants do not see).
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
`Geburtsdatum`, `Taufdatum`, `Pfarrgemeinde`; known names are completed, nobody is deleted). Everyone gets a personal link `/p/{token}`;
if a date of birth and/or baptism is stored it is asked after the click. Invitations with the link are prepared in the vote's
"Einladungen" tab: in demo mode as a demo mailbox; on a real instance the app sends them itself if a mail server is
set up (see `Mail__*` below), otherwise they are texts to copy. Each mail shows its status (waiting, sent, not sent with
the error and "send again"). Replies go to the vote's "E-Mail für Antworten" (settings, optional).
While registration is open the same tab prepares reminders for everyone who has not voted yet; the participant
list can be filtered to them. While the allocation is visible it sends everyone who voted their seats; whoever moves up
gets an e-mail by themselves.
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
Columns: `Titel;Beschreibung;Gruppe;Pfarre;Leitung;Leitung E-Mail;Teilnahme;Beginn;Ende;Ort;Plätze;Nach Vereinbarung` (English names work too;
only `Titel` and `Beginn` are required; `Pfarre` and `Leitung` are left empty when they match the vote).

For UI work with live reload: run the API as above and `npx ng serve` in `web` (http://localhost:4200, `/api` is proxied).

Tests: `dotnet test Urfair.slnx`

## Configuration

| Setting | Purpose |
|---|---|
| `DATABASE_URL` | PostgreSQL URL (`postgres://user:pass@host/db`). If set, PostgreSQL is used instead of SQLite. |
| `ConnectionStrings__Default` | Alternative connection string (SQLite by default). |
| `Admin__Key` | Real instance only: creates the very first Junia admin account (start page, while no account exists); afterwards only for the emergency access ("Notfall-Zugang": new one-time link for an existing Junia admin). Keep it secret. |
| `Demo__Enabled` | `true` (default): demo people, sample data and reset buttons. Set `false` for a real instance. |
| `App__BaseUrl` | Public address used in links in e-mails (e.g. `https://urfair-prod.onrender.com`). Default: the request host (remembered for mails made in the background). Recommended on a real instance. |
| `Mail__Host` | SMTP server. If set (and `Demo__Enabled=false`), the app sends its e-mails itself; in demo mode they always stay in the demo mailbox. |
| `Mail__Port` | SMTP port, default `587` (STARTTLS). Implicit TLS on port 465 is not supported. |
| `Mail__User`, `Mail__Password` | SMTP login (optional). Keep the password secret. |
| `Mail__From`, `Mail__FromName` | Sender address (default `Mail__User`) and name (default `Urfair`). |
| `Mail__Ssl` | `true` (default): STARTTLS. `false` only for a local test server. |
| `Retention__Days` | Days after the end of a vote until it is deleted with all its data, default `90`. |
| `Legal__Imprint`, `Legal__Privacy` | Texts of the legal notice and privacy policy (German, plain text; lines starting with `## ` are headings, blank lines separate paragraphs). Without them the pages show a template to be completed. Required on a real instance. |

E-mails are kept in the database as an outbox and sent in the background every 20 seconds; failed attempts are retried
after 1, 5, 30 and 120 minutes, then they are shown as "not sent" until an organiser clicks "send again".

See [ROADMAP.md](ROADMAP.md) for the plan and [CLAUDE.md](CLAUDE.md) for the working rules of this project.

## Deployment

The `Dockerfile` builds one container (UI + API, port 8080). GitHub Actions runs the tests and builds
the image on every push and pull request.

## Finding errors in operation

- **Version:** the footer, `/api/info` and the first log line (`Urfair version … starting`) name the running commit
  (`RENDER_GIT_COMMIT`, set by Render; `dev` locally). Render can roll back to an earlier deploy.
- **Error number:** an unexpected server error shows "Fehlernummer abc12345" to the user; the server log has the
  same number with the stack trace, path and version (search the Render log for `Error abc12345`).
- **Browser errors** are written to the server log as `Browser error at …` (message and page only, at most 20 per
  address and 10 minutes). Personal links appear as `/p/…`; no names or bodies are logged.
- **Log per vote** (tab "Zuteilung"): from "Anmeldung" on, every change with account and time: phases, rules,
  activities, participant list, seats, giving back, moving up, cancelling. E-mails with their status are in the mailbox.
  A wrong change to seats can be undone from there ("Zu diesem Stand zurück").
- **Fixing a bug:** issue with error number and steps → a test that reproduces it (`tests/Urfair.Core.Tests` for the
  allocation, `tests/Urfair.Api.Tests` for whole workflows on a fresh demo database) → fix → CI green → merge;
  Render deploys `main`.

## Dependencies and licences

Only permissively licensed, free components: .NET / ASP.NET Core / EF Core and the dotnet-ef tool (MIT), Npgsql (PostgreSQL licence),
SQLite (public domain), Angular (MIT). Tests only: xUnit (Apache 2.0), coverlet (MIT), Microsoft.AspNetCore.Mvc.Testing (MIT).
No external fonts, CDNs or trackers.

## Licence

MIT, see [LICENSE](LICENSE). Free to use, without warranty or liability.
© 2026 Thomas Scalet-Binder
