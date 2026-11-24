# Roadmap

Goal: Urfair as a free, shared tool for the Pfarre Urfahr-St. Junia and its 8 Pfarrgemeinden (parish communities;
official names in `src/Urfair.Core/Parishes.cs`).
Principle: **`main` is always the running demo** (https://urfair.onrender.com). Every step lands as small pull
requests; CI must be green; Render deploys `main` automatically. The same code later runs as a real instance
with `Demo__Enabled=false`. Anything "outgoing" (e-mails, offers) shows up in an in-app demo mailbox in demo mode.

Status as of 23 November 2026. Parish fields are a selection from the official list. The roadmap page in the app (`web/src/app/plan.ts`) shows the same plan in German
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
  invitations with the link (demo mailbox, otherwise texts to copy; sent by the app since step 6)
- **Step 3:** organiser accounts (e-mail and password, PBKDF2 hashes, cookie with keys in the database, 10 wrong
  logins lock for 15 minutes); Junia admins create accounts for Junia or one or more Pfarrgemeinden with a one-time
  link to set the password; the first Junia admin of a real instance is created with `Admin__Key`; visibility per vote
  (Pfarrgemeinde only / Junia shared / Junia read-only) checked on the server for every organiser request;
  participants, import, invitations limited to the own Pfarrgemeinden in Junia-wide votes; demo accounts via buttons
- Emergency access: with `Admin__Key` a new one-time link for an existing Junia admin (e.g. the only one forgot the password)
- **Step 4, part 1:** freeze the allocation (from phase "closed"; publishing freezes automatically; since step 5 the
  phase decides), move people by
  hand (overbooking allowed, shown in orange), lock seats, recalculate everything that is not locked, unfreeze
  (not while published); while frozen the vote cannot go back to draft/open, rules cannot change and activities
  cannot be replaced by an import; participants and the interim status see the stored allocation
- Utilisation shows who is assigned to each date (click to expand, tooltip on hover)
- Keep the original: freezing stores the calculated allocation (kept when recalculating, filled in at start-up for
  allocations frozen earlier); when moving people the organisers see the person's points and original seats, the
  table shows "Original: ..." where seats differ, "back to the original"; a log records freezing, recalculating,
  unfreezing and every seat change with who and when
- **Step 4 finished:** lists for activity leaders (new tab "Lists": one list per date with leader, location, seats
  and a column to tick off; print one page per date or download CSV; e-mail addresses only on request and only of
  the own Pfarrgemeinden); "not voted yet" (filter and count in the participant list, copy e-mail addresses for BCC,
  reminder e-mails with the personal link while registration is open)
- Lists per leader: grouped and filtered by leader, optional dates of birth and baptism, e-mail address of the leader per activity
  (internal, also in the CSV), "mail to the leaders" prepares one mail per leader with their lists (once frozen, only
  for accounts that see all participants)
- Optional date of baptism per participant (by hand and CSV column `Taufdatum`); if stored, the personal link asks for
  it as well (same lock after wrong tries)
- **Step 5, part 1:** phases Draft → Open (registration) → Allocation (internal) → Published (visible) → Closed →
  Ended. At the deadline Urfair switches to Allocation and freezes by itself; after the last date it ends by itself
  (background check every minute, Vienna time). Freezing follows the phase (no separate buttons); back to
  registration only from the internal allocation (unfreezes, deadline must be in the future). Interim status during
  registration only if switched on per vote; points locked after the deadline; waiting-list ticks only while visible
  (or during registration with interim status). Ended is read only (back only while the last date has not passed).
  Phase changes appear in the log. Demo dates are relative to the reset day, one vote per phase
- **Step 5, part 2:** moving up while the allocation is visible. Participants tick activities to move up into (only
  where it can help; the tick names the seat they would give up), give back a seat (with or without wanting a
  replacement; an activity given back is never allocated to them again) and take a free seat where they lack one.
  Free seats (given back, removed, more capacity) fill by themselves within the group: first people who need a seat
  and gave the activity points, then ticks; more points win, ties by lottery; whoever moves up gives up their weakest
  unlocked seat of that group, which fills again (chain). Triggered at once and checked every minute; each step is
  in the log with "Urfair" as actor. Recalculating is only possible in the internal allocation. Organisers see ticks
  and seats given back per person. Demo: the visible "Ministrantenausflug" has full activities and ticked sample people;
  demo person C did not get Kletterwald and can tick it, D sits there and can give it back, so C moves up (with mail)
- **Step 6, part 1:** e-mails from the app. All mails (invitations, reminders, leader lists, new: allocation and moved
  up) are kept as an outbox; with a mail server (`Mail__Host`, built-in .NET SMTP client, STARTTLS) and outside demo
  mode a background job sends them every 20 seconds with retries (1, 5, 30, 120 minutes, then "not sent" with the
  error and "send again"). Replies go to the optional "E-Mail für Antworten" of the vote. While the allocation is
  visible, "send the allocation" mails everyone who voted their seats and personal link; whoever moves up gets an
  e-mail with the new seats. Without a mail server everything stays to copy; the demo always shows the mailbox
- **Step 6, part 2:** cancelling when closed. Participants can cancel a seat until its date starts ("do you need a
  replacement?"); nothing moves automatically, the log says "cancelled" and the organisers get an e-mail (to the
  "E-Mail für Antworten", reply goes to the participant) with the seats left and a link to the allocation. The
  allocation marks and lists first everyone who still needs a replacement; organisers seat them by hand (also
  overbooked) or leave the seat free. Demo: D can cancel in the closed "Ferienprogramm"
- **Giving back with care:** if giving back leaves a person short of seats, the app warns and offers "just swap"
  instead (the waiting-list tick that gives up exactly this seat only when moving up). If they are still short after
  moving up, the organisers get an e-mail ("Platz fehlt", replies go to the person, e.g. to suggest another activity).
  Once participants see the allocation, every change by hand is mailed to the person with their new seats.
  Demo: B sits in his weakest wish in the full "Ministrantenausflug"; the start page explains per demo person what
  can be tried
- **Operations and troubleshooting:** the running version (commit) in the footer, `/api/info` and the log; unexpected
  server errors show an error number that is also in the log; browser errors go to the server log (without personal
  links); the log per vote also records rules, activities and participant list changes with the account; API tests
  (`tests/Urfair.Api.Tests`) run the demo workflows on a fresh database in CI. Bug-fix routine in the README
- **Seen stays seen:** once participants have seen an allocation (visible phase or their own changes in the log), it
  can still go back to "internal" for quiet corrections by hand, but it is no longer recalculated and does not go back
  to registration
- **Step 8:** undo with save points. After every change to seats (frozen, recalculated, by hand, moving up, giving
  back, cancelling, restoring) a save point stores seats, fixed marks, ticks and seats given back of all ballots (new
  table `AllocationSnapshots`). In the log, the last entry of each change offers "back to this state" with a preview
  (who gains or loses which seat, how many get an e-mail) and a confirmation. Restoring deletes nothing: it is logged
  per person and as a whole and can itself be undone. Seats a participant gave back stay given back; ticks used up by
  moving up come back; while visible or closed everyone affected gets "Deine Plätze"; while visible, seats that are
  free afterwards move up at once (switch to internal first to avoid that). Only states since the last freeze. Demo:
  every vote with an allocation starts with a save point "frozen"
- **Step 6, part 3:** filling a seat when closed. The allocation lists free seats per date with "fill seat": the
  seat is offered to one real person at a time in the order of moving up (missing a seat, then ticks, more points,
  lottery), each person once per date and only one open offer per person (new table `SeatOffers`). The person gets
  an e-mail with their personal link and sees the offer in their allocation (accept/decline, naming the seat they
  would give up); they have 48 hours, at most until a day before the date, and dates less than two days ahead are
  not offered. Declined or expired (e-mail to the person): the next one is asked; accepted: the seat given up is
  offered next (chain); nobody left: the organisers get an e-mail "Platz frei". Leaving "closed" withdraws open
  offers. People who need a replacement get up to three one-click suggestions (another date of what they gave back,
  free seats by points) and one to overbook their best full wish. Every step is in the log; accepting is a save
  point. Demo: D cancels "Kochen mit Oma" in the closed "Ferienprogramm", C (who ticked it) gets the offer and gives
  up Fußballturnier, which is then free for D's suggestion
- **Step 5, rest:** choosing the seat to give up. If more than one seat of the group could go for a ticked activity
  (fewer points, not fixed, no clash), participants choose it next to the tick; the one with the fewest points is
  pre-selected. The choice is binding: if that seat can no longer go, they do not move up there instead of losing
  another one; a seat no longer held falls back to the weakest. It counts for moving up and for offers when closed,
  goes with the tick and is part of the save points (new column `Ballots.GiveUp`). Demo: set the "Ferienprogramm"
  to visible, C can give up Parkbad instead of Fußballturnier for "Kochen mit Oma"
- **Step 7: required staffing and more than required.** Two new rules per vote (blocked while the allocation is
  frozen, like all rules). "Required staffing": every date with a seat count must be filled; participants see
  "noch gesucht: X" per date while voting (even without the interim status) and organisers see it in the load view.
  The allocation stays as it is: the basic round and the fallback already fill the emptiest dates. "More than
  required": organisers allow up to N extra seats (0 = off, at most 10), participants choose "up to N" next to their
  points. Extra seats come after the basic round and the swaps, only in activities the person gave points to, more
  points first, group quotas do not limit them and nobody loses a seat for them. Lowering the maximum cuts offers
  down (new columns `FairSlots.MaxExtra`, `FairSlots.RequiredStaffing`, `Ballots.Extra`). Demo: the "Pfarrfest"
  shows the people still missing per shift; D offers one seat more and gets a third shift
- **Automatic deletion and legal pages.** When a vote becomes Ended (after the last date or by hand) its deletion
  period starts (`FairSlots.EndedAt`); after `Retention__Days` days (default 90) the background clock deletes it with
  all its data. Organisers and participants see the deletion date; going back from Ended stops it. Votes that never
  end (e.g. without dates) are not covered yet. Legal notice and privacy policy in the footer (`/impressum`,
  `/datenschutz`), texts from `Legal__Imprint` and `Legal__Privacy`; without them a marked German template is shown.
  Still open: the real texts (legal basis, supervisory authority) with the parish and the data protection officer.
  Demo: set the "Ferienprogramm" to Ended to see the deletion date
- **Copy a vote as a template.** "Neue Wahl" can start from any vote the account sees, also an ended one (before it
  is deleted): settings, rules, groups, activities and dates are copied, the dates moved by whole weeks (default 52 =
  the same weekday a year later, at most 104). Participants, points, seats, mails and the log are not copied; the
  copy starts as a draft. Pfarrgemeinde admins copy into their own Pfarrgemeinde, never read-only for themselves.
  Demo: create "Ferienprogramm 2028" from the "Ferienprogramm"
- **Take over participants from another vote.** Tab "Teilnehmende", "Aus anderer Wahl übernehmen": choose a vote
  the account sees (also an ended one), see how many are new and how many already listed, then take them over with
  e-mail, dates of birth and baptism and Pfarrgemeinde. New personal links, not invited yet; names already listed
  stay unchanged; logged after the draft. Pfarrgemeinde admins take over only people of their own Pfarrgemeinden.
  Demo: take over the people of the "Ferienprogramm" into the "Jungschar-Sommerlager"
- **"Open to everyone" with an exclusion list.** Settings: below "Für alle offen" a list of names (one per line,
  case-insensitive) who do not see the vote and cannot vote; "exclude the participants of another vote" adds the names
  of e.g. last year's vote (only people the account may see). People on the participant list always take part; a
  vote already cast stays. The list is copied with a template (new column `FairSlots.Excluded`). Limit: there is no
  self-registration yet, so on a real instance "open to everyone" only reaches people with a personal link.
  Demo: exclude "David Doppler" in the "Pfarrfest", then he no longer sees it
- **Together with friends.** Settings: off / pairs / groups up to 3, 4 or 5 (new columns `FairSlots.Friends`,
  `Ballots.Friends`). Each member enters the same code in the ballot ("Neuen Code erzeugen" makes a random one) and
  sees who else entered it; a full group takes nobody more. After the calculated allocation (before extra seats)
  each group, in lottery order, is moved into a shared date of an activity all members gave points to: by a free
  seat or by swapping with an outsider who does not lose points; members may give up a weaker seat. Limits: only in
  the calculated allocation; moving up may separate a group again; lowering the group size dissolves groups that are
  too large. Demo: "Firmvorbereitung" allows groups up to 3, B and C share the code `FIRM42`. The code only counts once
  the vote is submitted: a changed code and other changes not submitted yet are marked, leaving the page asks first
- **Undo for activities, the participant list and rules.** Every logged change to them (from registration on) keeps
  how the things it touched were before (new column `AllocationLog.Undo`, JSON); the log offers "Rückgängig machen"
  with a confirmation. Undoing sets exactly these back and overwrites later changes to them: a deleted activity comes
  back with its dates, the points ballots gave it (as far as each budget allows) and, once frozen, the seats people
  lost with it where they still lack a seat there; a removed person comes back with the same personal link; a rule
  change comes back with the extra seats, friend codes and activity groups it cut down. An undo never takes a seat
  away (it is refused instead), is logged and can itself be undone. Rules stay locked while the allocation is frozen.
  Limits: draft changes are not logged and cannot be undone; offers and the original seats on deleted dates do not
  come back; details of removed people stay in the log until the vote is deleted. Demo: delete "Tiergarten Walding" in
  the "Ministrantenausflug" and undo it, Clara gets her seat back
- **Filling seats automatically when closed.** New setting "Bei Absage automatisch nachbesetzen" (column
  `FairSlots.AutoFill`, on by default, also for existing votes, copied with a vote): a cancellation in "closed" offers
  the seat at once, as "fill seat" would; the organisers' e-mail says whether it was offered, nobody waits for it or
  the date is too close. The time to answer depends on the date: 48 hours, 24 if it is less than a week ahead, 12 if
  less than three days, at most until a day before; dates less than 36 hours ahead are not offered (before: 2 days).
  For every free seat not yet offered the allocation shows who would be asked next (up to three, with the seat they
  would give up). Participants can remove waiting-list ticks when closed (not add any), so they are no longer asked.
  Demo: participants see the e-mails of the vote addressed to them ("Deine Mails (Demo)" on their page); D cancels
  "Kochen mit Oma" in the "Ferienprogramm" and C finds the offer there at once
- **Leaders hear about changes, demo mail traffic.** Once a leader got their lists by mail, the list they got is kept
  per date (column `Sessions.LeaderList`) and every change against it is mailed to them ("Änderung Teilnahmeliste":
  who is new, who is no longer there, the whole list), only while the allocation is visible or closed (`LeaderNotices.cs`).
  Changes by participants (cancelling, giving back, moving up, accepting an offer) go out at once, others (e.g. by hand)
  once the vote has been quiet for 10 minutes, so a series of edits makes one mail. Accepting an offered seat is
  confirmed by mail ("Platz bestätigt"). Demo: the mails sent so far are seeded with earlier dates (invitations,
  results, lists for the leaders); the "Ferienprogramm" has two leaders with e-mail addresses and B ticked "Kochen mit
  Oma" after C, so a declined offer goes on to him. The "Einladungen" tab shows the whole mail traffic or one mailbox
  (organisers, leaders, participants) with inbox and sent, and who each mail is from
- **Access links by mail, forgot password.** A new account gets its one-time link (7 days) by mail; "Passwort
  vergessen?" on the start page tells all Junia admins by mail (same answer whether the address exists or not, one
  mail per hour and account) and marks the account under "Konten", where "Link schicken" mails a new one-time link;
  the old password stays valid until a new one is set. Account mails have no vote (`Mails.FairSlotId` may be empty,
  column `Accounts.ResetRequestedAt`), are listed under "Konten" (to copy without a mail server) and are deleted after
  30 days or with the account
- **Leaders in one place, mails after a decision.** New tab "Leitungen": every leader with e-mail address and the
  activities they lead; a change there counts for all their activities (one address per leader) and is logged. The
  organisers come first and lead everything without a leader of its own; their name and address are the same as in
  the settings, both ways. Addresses are required (organisers' address defaults to the creator's; a leader needs one);
  the activity form suggests known leaders with their address. The CSV import lists the leaders, takes their address
  from the CSV or the vote and asks for missing ones before taking over. Changes to a leader's list are no longer mailed
  on their own: "Zuteilung" and "Listen" show who does not know yet and what changed (new, no longer there), with
  "Mail the changed list" per leader or for all. Explanations of satisfaction (results) and demand (green: a seat for
  everyone interested, orange: overbooked, red: from 2×)

## Next steps (each ends with a demo that can be shown)

| Step | Work packages | Visible in the demo |
|---|---|---|
| 2 ✓ | done, see above | |
| 3 ✓ | done, see above | |
| 4 ✓ | done, see above | |
| 5 ✓ | done, see above | |
| → | **Pilot with 20–30 confirmands** on a real instance | |
| 6 ✓ | done, see above | |
| 7 ✓ | done, see above | |
| 8 ✓ | done, see above (also for activities, participant list and rules) | |

In parallel, start early: hosting (diocese server or EU provider), data protection with the data protection
officer, the real texts of the legal notice and privacy policy (pages and automatic deletion are done).

## Process model (decided 22 November 2026)

1. **Draft:** organisers set up the vote.
2. **Registration:** participants spread points. No interim status for participants (it invites tactics;
   switchable per vote; in required-staffing mode only "X people still missing"). Organisers see a live preview.
3. **Allocation:** starts at the deadline (automatically) with a calculated, frozen allocation.
   - *Internal:* only organisers see it; they move, overbook, delete, change capacities and may recalculate everything.
   - *Visible:* participants see their seats and waiting-list positions. Points are locked; participants can only set
     waiting-list ticks and give back a seat they no longer want. No complete recalculation any more, only local
     changes: a free seat (given back, deleted, more capacity) moves up automatically (rules below).
   - More capacity never sends the vote back to registration; the new seats fill from the waiting list.
4. **Closed:** the lists are final. Participants can still cancel (e.g. ill) and remove waiting-list ticks. A
   cancelled seat is offered at once (offers with a deadline, see below; switchable per vote). Otherwise, and for
   other free seats, the organisers choose per seat: "fill seat", overbook on purpose or leave it free.
   Going back to allocation stays possible for bigger changes (it reopens automatic moving up for everyone).
5. **Ended:** after the last date (automatically) or earlier by hand; read only, starts the deletion period.

Rules for free seats:

- **Within the group:** a green seat is only exchanged for a green one. Whoever moves up gives up a seat of the same
  group (pre-selected: the one with the fewest points; they can choose another); if they lack a seat in that group,
  nothing is given up.
- **Need before wish:** people missing a required seat (e.g. after cancelling) come before the waiting list.
- **Chain:** the seat given up is free again and the next round starts, until nobody is left. In allocation this is
  automatic; when closed every step is an offer with a deadline to accept (e-mail to the next person and also when
  it expires). Each step improves someone by their own points and a seat given up is never offered back to the same
  person, so the chain ends. The closer the date, the shorter the deadline (48, 24 or 12 hours, at most until a day
  before); no offers for a date less than 36 hours ahead; then only by hand.
- **Replacement for someone who cancels:** they are asked "do you need a replacement?" (pre-selected if a required
  seat is now missing). Order: another date of the same activity; free seats in the same group they gave points to
  (by points); other free seats in the same group (they choose); if nothing is free, the organisers overbook one date
  on purpose (suggestion: where the person gave the most points) or decide on no replacement. Capacity is not
  raised, because that would start new chains.

Messages: Urfair is the hub. E-mails only notify and always link straight to the page (participants via their
personal link, organisers to the vote). Actions happen in the app, never by replying to a mail. Freezing keeps the
original allocation so organisers can see and restore it; a log records every change.

## Later

- Nothing at the moment.

## Requirement notes

- **More than required:** done in step 7 (participants offer "up to N" more, the organisers set the maximum; extra
  seats only after the basic round).
- **Interim status:** invites tactics; decided: off while registering, shown during allocation (see "Process model").
- **Cancellations and moving up:** decided, see "Process model"; e-mails come in step 6.
- **Required staffing:** done in step 7 as "every date with a seat count is a post to fill"; missing people are
  shown while voting. A separate minimum below the seat count per date can follow if a real vote needs it.
- **Together with friends (decided 22 November 2026, done):** switchable per vote (off / pairs / groups up to 5).
  A group forms by a code that every member enters (mutual consent, nobody can be added by others). It is only a
  wish: after the normal allocation, seats are swapped so that the group shares at least one activity, and only an
  activity every member gave points to; a swap is only made if nobody outside the group loses. Groups are never
  allocated as a block, so friends cannot crowd out others.
- **Import:** Excel/CSV instead of PDF (a PDF has no fields; reliable only for one fixed layout).
- **Participant import:** the list belongs to one vote, never to a Pfarrgemeinde; each person carries a
  Pfarrgemeinde column (short forms recognised, as for activities), so Junia-wide votes such as the Firmung can be
  filtered and evaluated per Pfarrgemeinde. No central register of people across Junia: less data, deleted with the
  vote (minors). Rows with errors are reported by row number. Permissions from step 3:

  | Vote visibility | Junia admin | Pfarrgemeinde admin |
  |---|---|---|
  | Pfarrgemeinde only | everything | import and edit if it is their own Pfarrgemeinde |
  | Junia shared | everything | import and edit, only rows of their own Pfarrgemeinde |
  | Junia, read only | everything | only rows of their own Pfarrgemeinde (decided 24 November 2026); activities and rules not |

  Rows for another Pfarrgemeinde are rejected with their row number. Step 2 builds the import with the Pfarrgemeinde
  column but without permission checks; step 3 adds them without changing the import.

## Open questions

- Who is the contact for hosting and data protection in the parish and diocese?
- ~~May one person be admin of several parishes?~~ Yes (an account can have several Pfarrgemeinden).
- ~~May Pfarrgemeinde admins maintain their own participants in "Junia, read only" votes?~~ Yes, only rows of their own Pfarrgemeinde.
- Pilot with 20–30 confirmands or the whole year group right away?
- ~~"Together with friends": pairs or small groups, how many, how strongly may it influence the allocation?~~ Decided, see requirement notes.
