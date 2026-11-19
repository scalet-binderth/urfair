# Punktewahl Firmung

Fair registration for the activities of a parish confirmation preparation.

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
| `src/PointAllocation.Core` | Allocation algorithm, validation, sample data (no dependencies) |
| `src/PointAllocation.Api` | ASP.NET Core minimal API + EF Core; also serves the built web app |
| `tests/PointAllocation.Core.Tests` | xUnit tests for the allocation rules |
| `web` | Angular app (vote, organisation, allocation; German/English) |
| `index.html` | First static prototype (published via GitHub Pages) |

## Run locally

Requirements: .NET SDK 10, Node.js 24. Docker is not needed locally.

```
cd web && npm ci && npx ng build        # builds the UI into src/PointAllocation.Api/wwwroot
cd ../src/PointAllocation.Api && dotnet run
```

Open http://localhost:5080. A local SQLite file is created and filled with sample data.

For UI work with live reload: run the API as above and `npx ng serve` in `web` (http://localhost:4200, `/api` is proxied).

Tests: `dotnet test PointAllocation.slnx`

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
