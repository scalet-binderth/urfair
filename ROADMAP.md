# Roadmap

Goal: Urfair as a free, shared tool for the Pfarre Urfahr-St. Junia and its 8 Pfarrgemeinden (parish communities;
official names in `src/Urfair.Core/Parishes.cs`).
Principle: **`main` is always the running demo** (https://urfair.onrender.com). Every step lands as small pull
requests; CI must be green; Render deploys `main` automatically. The same code later runs as a real instance
with `Demo__Enabled=false`. Anything "outgoing" (e-mails, offers) shows up in an in-app demo mailbox in demo mode.

Status as of 21 November 2026. Parish fields are a selection from the official list. The roadmap page in the app (`web/src/app/plan.ts`) shows the same plan in German
and English; keep both in sync.

## Done

- v0.1 demo: votes with phases, activities and dates, participant list, point voting, fair allocation compared
  with first come first served, own result, German/English, demo people A–D, playground "Pfarrfest"
- Activity groups with quota per group (Firmung: 2 green, 1 blue)
- Interim status after voting (fixed / waiting list with position / not needed) and a waiting-list flag (stored only)
- Demo people B–D start with ballots
- **Step 1:** EF Core migrations (SQLite + PostgreSQL), demo switch `Demo__Enabled`, reset of the whole
  demo, description per activity, Excel/CSV import and export of activities (instead of a PDF importer)
- Optional parish and person responsible per vote, parish and leader per activity (only where they differ);
  shown to participants and included in the CSV columns `Pfarre` and `Leitung`; parish chosen from the official list
  (short forms such as "St. Markus" are recognised in the import)
- **Step 2:** participant list per vote with e-mail, date of birth and Pfarrgemeinde (by hand or CSV import/export,
  filter by Pfarrgemeinde); personal link `/p/{token}` with a date-of-birth check (10 wrong tries lock it for 15 minutes),
  afterwards the browser sends token and date with every participant request; outside demo mode only links work;
  invitations with the link (demo mailbox, otherwise texts to copy until e-mail sending exists)
- **Step 3:** organiser accounts (e-mail and password, PBKDF2 hashes, cookie with keys in the database, 10 wrong
  logins lock for 15 minutes); Junia admins create accounts for Junia or one or more Pfarrgemeinden with a one-time
  link to set the password; the first Junia admin of a real instance is created with `Admin__Key`; visibility per vote
  (Pfarrgemeinde only / Junia shared / Junia read-only) checked on the server for every organiser request;
  participants, import, invitations limited to the own Pfarrgemeinden in Junia-wide votes; demo accounts via buttons
- Emergency access: with `Admin__Key` a new one-time link for an existing Junia admin (e.g. the only one forgot the password)
- **Step 4, part 1:** freeze the allocation (from phase "closed"; publishing freezes automatically), move people by
  hand (overbooking allowed, shown in orange), lock seats, recalculate everything that is not locked, unfreeze
  (not while published); while frozen the vote cannot go back to draft/open, rules cannot change and activities
  cannot be replaced by an import; participants and the interim status see the stored allocation

## Next steps (each ends with a demo that can be shown)

| Step | Work packages | Visible in the demo |
|---|---|---|
| 2 ✓ | done, see above | |
| 3 ✓ | done, see above | |
| 4 | ~~Freeze the allocation, move by hand, lock, recalculate~~ ✓; lists for leaders (CSV/print); "not voted yet"; automatic deadline | finished lists for activity leaders |
| → | **Pilot with 20–30 confirmands** on a real instance | |
| 5 | Cancellations after the deadline (organiser and participant); moving up from the waiting list with a deadline to accept | offer in the demo mailbox |
| 6 | Required staffing mode ("all posts must be filled", hint "X people still missing"); "more than required" with an upper limit; interim status switchable per vote | Pfarrfest with missing people shown |

In parallel, start early: hosting (diocese server or EU provider), data protection with the data protection
officer, legal notice and privacy policy, automatic deletion after an event.

## Later

- E-mails from the app (parish mail account)
- "Together with a friend"
- Copy last year's vote as a template
- Take over participants from another vote
- "Open to everyone" with an exclusion list (e.g. last year's participants)

## Requirement notes

- **More than required:** without an explicit choice the algorithm limits to the required number. New: participants
  state "I would also take up to N" (organiser sets the maximum); extra seats only after the basic round.
- **Interim status:** invites tactics, so make it switchable per vote.
- **Cancellations and moving up:** needs a frozen allocation and e-mails; offer the seat to the first person on the
  waiting list (with the flag), with a deadline, then the next.
- **Required staffing:** first fill every date's minimum, preferring people who gave it points; show missing
  people while voting.
- **Import:** Excel/CSV instead of PDF (a PDF has no fields; reliable only for one fixed layout).
- **Participant import:** the list belongs to one vote, never to a Pfarrgemeinde; each person carries a
  Pfarrgemeinde column (short forms recognised, as for activities), so Junia-wide votes such as the Firmung can be
  filtered and evaluated per Pfarrgemeinde. No central register of people across Junia: less data, deleted with the
  vote (minors). Rows with errors are reported by row number. Permissions from step 3:

  | Vote visibility | Junia admin | Pfarrgemeinde admin |
  |---|---|---|
  | Pfarrgemeinde only | everything | import and edit if it is their own Pfarrgemeinde |
  | Junia shared | everything | import and edit, only rows of their own Pfarrgemeinde |
  | Junia, read only | everything | proposal: only rows of their own Pfarrgemeinde; activities and rules not |

  Rows for another Pfarrgemeinde are rejected with their row number. Step 2 builds the import with the Pfarrgemeinde
  column but without permission checks; step 3 adds them without changing the import.

## Open questions

- Who is the contact for hosting and data protection in the parish and diocese?
- ~~May one person be admin of several parishes?~~ Yes (an account can have several Pfarrgemeinden).
- May Pfarrgemeinde admins maintain their own participants in "Junia, read only" votes? (proposal: yes, only rows of their own Pfarrgemeinde)
- Pilot with 20–30 confirmands or the whole year group right away?
