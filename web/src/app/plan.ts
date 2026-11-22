import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { I18n } from './i18n';

interface Row { cells: string[] }
interface Phase { label: string; kind?: 'done' | 'parallel'; title: string; items: string[] }
interface PlanText {
  title: string; lead: string; asOf: string;
  nowTitle: string; now: string[];
  phasesTitle: string; phases: Phase[];
  mustTitle: string; mustLead: string; mustHead: string[]; must: Row[];
  rolesTitle: string; rolesLead: string; roles: string[]; visibilityLead: string; visibilityHead: string[]; visibility: Row[]; visibilityNote: string;
  flowTitle: string; flowLead: string; flowHead: string[]; flow: Row[];
  shouldTitle: string; should: string[];
  laterTitle: string; later: string[];
  questionsTitle: string; questions: string[];
  back: string;
}

/** Roadmap from the planning document (as of 22.11.2026), shown in the app for presentations. */
const PLAN: Record<'de' | 'en', PlanText> = {
  de: {
    title: 'Umsetzungsplan',
    lead: 'Die Demo ist vorzeigbar. Bis zum echten Einsatz in der Pfarre Urfahr-St. Junia fehlen vor allem Dinge, die man in der Vorführung nicht sieht.',
    asOf: 'Stand: 22. November 2026',
    nowTitle: 'Was die Demo schon kann',
    now: [
      'Anmeldung als Teilnehmer:in oder Organisation, Demo-Personen A bis D',
      'Mehrere Wahlen mit Phasen: Entwurf, offen, geschlossen, veröffentlicht',
      'Veranstaltungen, Termine und Teilnehmerliste pflegen',
      'Punkte verteilen, Wahl ändern, faire Zuteilung mit Vergleich zu „Wer zuerst kommt“',
      'Spielwiese „Pfarrfest“: für alle offen, Anmeldungen per Knopf löschen',
      'Projektgruppen wie bei der Firmung: 2 aus Grün, 1 aus Blau, frei definierbar',
      'Zwischenstand nach der Abgabe (fix, Warteliste mit Platz, ausgebucht) und Wartelisten-Häkchen',
      'Demo-Personen B bis D haben schon abgestimmt und können ändern',
      'Veranstaltungen mit Beschreibung, Pfarre und Leitung, Import und Export über Excel/CSV',
      'Datenbank mit Migrationen: Daten bleiben bei Updates erhalten; Demo per Knopf zurücksetzen',
      'Teilnehmerliste mit E-Mail, Geburtsdatum und Pfarrgemeinde, von Hand oder per CSV; persönliche Links und Einladungen',
      'Konten mit Rollen (Junia- und Pfarrgemeinde-Admins), Sichtbarkeit pro Wahl, Demo-Konten per Knopf',
      'Zuteilung festschreiben, Personen von Hand verschieben, Plätze fixieren und den Rest neu rechnen',
      'Auslastung mit den Namen der Zugeteilten pro Termin',
      'Original merken: Punkte und Original-Plätze beim Verschieben, zurück zum Original, Protokoll der Änderungen',
      'Listen für Leiter:innen pro Termin zum Drucken oder als CSV',
      '„Noch nicht abgestimmt“: Filter, E-Mail-Adressen kopieren, Erinnerungen mit persönlichem Link',
      'Eigenes Ergebnis nach Veröffentlichung, Deutsch und Englisch',
    ],
    phasesTitle: 'Reihenfolge',
    phases: [
      { label: 'Schritt 1 · erledigt', kind: 'done', title: 'Sichere Grundlage', items: ['Datenbank-Migrationen', 'Demo-Schalter (Demo und echte Instanz aus demselben Code)', 'Beschreibung pro Veranstaltung', 'Veranstaltungen aus Excel/CSV importieren'] },
      { label: 'Schritt 2 · erledigt', kind: 'done', title: 'Teilnehmende', items: ['Teilnehmerliste pro Wahl per CSV oder von Hand, mit E-Mail und Pfarrgemeinde', 'Persönliche Links mit Prüfung des Geburtsdatums', 'Einladungen: Demo-Postfach, sonst zum Kopieren'] },
      { label: 'Schritt 3 · erledigt', kind: 'done', title: 'Login und Rollen', items: ['Junia- und Pfarrgemeinde-Admins', 'Sichtbarkeit pro Wahl', 'Import und Pflege der Teilnehmenden je nach Rolle', 'Demo-Konten per Knopf'] },
      { label: 'Schritt 4 · erledigt', kind: 'done', title: 'Zuteilung im Griff', items: ['Zuteilung festschreiben und anpassen', 'Namen pro Termin in der Auslastung', 'Original merken und Protokoll', 'Listen für Leiter:innen', '„Noch nicht abgestimmt“ mit Erinnerungen'] },
      { label: 'Schritt 5', title: 'Neuer Ablauf', items: ['Phasen: Entwurf → Anmeldung → Zuteilung (erst intern, dann sichtbar) → Abgeschlossen → Beendet', 'Anmeldeschluss automatisch: Zuteilung wird gerechnet und festgeschrieben', 'Während der Anmeldung kein Zwischenstand (pro Wahl einschaltbar)', 'In der Zuteilung: Punkte gesperrt, nur Warteliste und „Platz zurückgeben“', 'Freie Plätze rücken automatisch nach, innerhalb der Gruppe, Bedarf vor Wunsch', 'Beendet nach dem letzten Termin oder von Hand (startet die Löschfrist)', 'danach: Probelauf mit 20 bis 30 Firmlingen'] },
      { label: 'Schritt 6 und 7', title: 'Nachrichten und Sonderfälle', items: ['E-Mails aus der App, immer mit direktem Link', 'Nachricht an die Organisation', 'Absagen kurz vor dem Termin: „Platz nachbesetzen“ mit Angeboten und Frist, Mail auch bei Ablauf', 'Ersatz für die absagende Person, gezielt überbuchen', 'Pflichtbesetzung', '„Mehr als gefordert“'] },
      { label: 'Parallel', kind: 'parallel', title: 'Mit der Diözese', items: ['Hosting: Diözesanserver oder EU-Anbieter', 'Datenschutz mit dem Datenschutzbeauftragten', 'Impressum und Datenschutzerklärung'] },
    ],
    mustTitle: 'Muss vor echten Anmeldungen passieren',
    mustLead: 'Ohne diese Punkte dürfen keine echten Daten von Firmlingen in die App.',
    mustHead: ['Thema', 'Was fehlt', 'Warum'],
    must: [
      { cells: ['Identifikation', 'Erledigt (Schritt 2): Teilnehmerliste, persönlicher Link, Prüfung des Geburtsdatums', 'Sonst könnte sich jede:r unter jedem Namen anmelden'] },
      { cells: ['Login und Rollen', 'Erledigt (Schritt 3): Konten für mehrere Personen, Rollen pro Pfarrgemeinde', 'Sonst gäbe es nur einen offenen Zugang'] },
      { cells: ['Zuteilung festschreiben', 'Erledigt (Schritt 4): speichern, händisch verschieben, fixieren, neu rechnen; Veröffentlichen schreibt automatisch fest', 'Sonst würde bei jedem Aufruf neu gerechnet'] },
      { cells: ['Recht und Datenschutz', 'Impressum, Datenschutzerklärung, automatisches Löschen, Hosting in der EU', 'Daten von Minderjährigen'] },
    ],
    rolesTitle: 'Rollen und Rechte',
    rolesLead: 'Urfair läuft als ein gemeinsames Werkzeug für die Pfarre Urfahr-St. Junia mit ihren 8 Pfarrgemeinden. Jede Wahl gehört einer Pfarrgemeinde oder ganz Junia.',
    roles: ['Junia-Admin: sieht und bearbeitet alle Wahlen, verwaltet Pfarrgemeinden und Konten', 'Pfarrgemeinde-Admin: legt Wahlen für die eigene Pfarrgemeinde an'],
    visibilityLead: 'Beim Anlegen einer Wahl wählt man die Sichtbarkeit (Junia-Admins dürfen immer alles):',
    visibilityHead: ['Sichtbarkeit', 'Sehen', 'Bearbeiten'],
    visibility: [
      { cells: ['Pfarrgemeinde intern', 'eigene Pfarrgemeinde', 'eigene Pfarrgemeinde'] },
      { cells: ['Junia gemeinsam', 'alle Pfarrgemeinden', 'alle Pfarrgemeinden'] },
      { cells: ['Junia, nur lesen', 'alle Pfarrgemeinden', 'nur Junia-Admins'] },
    ],
    visibilityNote: '„Sehen“ meint den Organisationsbereich. Welche Teilnehmenden eine Wahl sehen, regelt die Teilnehmerliste.',
    flowTitle: 'Neue Anforderungen zum Wahl-Ablauf',
    flowLead: 'Aus dem Feedback vom 20. November 2026, mit Einschätzung.',
    flowHead: ['Anforderung', 'Einschätzung', 'Aufwand'],
    flow: [
      { cells: ['Mehr als gefordert wählen', 'Ohne Angabe begrenzt der Algorithmus automatisch auf die geforderte Zahl (z. B. 3). Neu: Teilnehmende geben bewusst „ich nehme auch bis zu N“ an, die Obergrenze legt die Organisation fest. Zusatzplätze werden erst nach der Grundrunde vergeben, damit niemand wegen Vielwählern leer ausgeht.', 'klein bis mittel'] },
      { cells: ['Zwischenstand und Warteliste', 'Entschieden: Während der Anmeldung sehen Teilnehmende keinen Zwischenstand (gegen Taktieren; pro Wahl einschaltbar). In der Zuteilung sehen sie ihre Plätze und ihren Wartelistenplatz; Punkte sind dann gesperrt, nur Wartelisten-Häkchen und „Platz zurückgeben“ sind möglich.', 'Demo erledigt, Umbau Schritt 5'] },
      { cells: ['Abmelden und Nachrücken', 'Entschieden: Ein freier Platz geht innerhalb derselben Gruppe weiter (grün nur gegen grün). Zuerst an Personen, denen ein Pflichtplatz fehlt, dann an die Warteliste. Wer nachrückt, wählt, welchen Platz der Gruppe er abgibt (vorgewählt: wenigste Punkte); der wird wieder frei, bis niemand mehr wartet. In der Zuteilung läuft das automatisch; im Zustand „Abgeschlossen“ erst nach „Platz nachbesetzen“ als Angebot mit Frist (Mail auch bei Ablauf), ab 2 Tagen vor dem Termin nur noch von Hand. Wer absagt, bekommt auf Wunsch Ersatz: anderer Termin derselben Veranstaltung, dann freie Plätze der Gruppe; ist nichts frei, überbucht die Organisation gezielt.', 'mittel bis groß'] },
      { cells: ['Original merken', 'Beim Festschreiben wird die ursprüngliche Zuteilung gespeichert. Beim Verschieben sieht die Organisation die Punkte der Person und ihre Original-Plätze und kann zum Original zurück; ein Protokoll zeigt, wer wann was geändert hat.', 'erledigt'] },
      { cells: ['Pflichtbesetzung (Posten müssen besetzt sein)', 'Beim Anlegen wählbar: „freie Wahl“ oder „alle Posten müssen besetzt sein“ (z. B. Pfarrfest). Dann füllt der Algorithmus zuerst die Mindestbesetzung jedes Termins auf, bevorzugt mit Personen, die dort Punkte gegeben haben. In der Abstimmung steht „hier fehlen noch X Personen“, damit sich Leute gezielt melden.', 'mittel'] },
      { cells: ['Veranstaltungen importieren', 'Statt PDF über Excel/CSV, weil ein PDF keine Felder enthält. Umgesetzt in Schritt 1: Liste herunterladen, in Excel bearbeiten, hochladen, Vorschau prüfen, ergänzen oder ersetzen.', 'erledigt'] },
      { cells: ['Teilnehmende importieren', 'Pro Wahl per CSV (Vorname, Nachname, E-Mail, Geburtsdatum, Pfarrgemeinde), keine zentrale Personenkartei: weniger Daten, wird mit der Wahl gelöscht. Die Pfarrgemeinde-Spalte erlaubt bei Junia-Wahlen Auswertungen pro Pfarrgemeinde. Ab Schritt 3 an die Rolle gekoppelt: Pfarrgemeinde-Admins importieren und pflegen nur Zeilen der eigenen Pfarrgemeinde, fremde Zeilen werden mit Zeilennummer abgelehnt.', 'mittel (Schritt 2 und 3)'] },
    ],
    shouldTitle: 'Sollte für den echten Einsatz dabei sein',
    should: ['✓ Teilnehmerlisten für Leiter:innen zum Exportieren oder Drucken', 'Anmeldeschluss automatisch (Schritt 5)', '✓ Überblick „noch nicht abgestimmt“ für Erinnerungen'],
    laterTitle: 'Später, wenn es sich bewährt',
    later: ['„Mit Freund:innen zusammen“: pro Wahl einschaltbar (aus, Paare, Gruppen bis 3); Gruppe per gemeinsamem Code, nur mit Zustimmung aller; nur ein Wunsch: nach der Zuteilung wird getauscht, damit die Gruppe mindestens eine Veranstaltung teilt, die alle gewählt haben, und nur wenn dabei niemand außerhalb der Gruppe verliert', 'Vorjahr als Vorlage kopieren', 'Teilnehmende aus einer anderen Wahl übernehmen', 'Wahlen „für alle offen“ mit Ausnahmeliste (z. B. alle außer den Teilnehmenden vom Vorjahr)'],
    questionsTitle: 'Offene Fragen',
    questions: ['Wer ist Ansprechperson für Hosting und Datenschutz in Pfarre und Diözese?', 'Dürfen Pfarrgemeinde-Admins bei „Junia, nur lesen“ die Teilnehmenden ihrer Pfarrgemeinde pflegen? (Vorschlag: ja, nur die eigenen Zeilen)', 'Probelauf mit 20 bis 30 Firmlingen oder gleich dem ganzen Jahrgang?'],
    back: '← Zur Startseite',
  },
  en: {
    title: 'Roadmap',
    lead: 'The demo is ready to show. Before real use in the Pfarre Urfahr-St. Junia, mostly invisible things are missing.',
    asOf: 'As of 22 November 2026',
    nowTitle: 'What the demo can do',
    now: [
      'Sign in as participant or organiser, demo people A to D',
      'Several votes with phases: draft, open, closed, published',
      'Maintain activities, dates and participant lists',
      'Spread points, change a vote, fair allocation compared with “first come, first served”',
      'Playground “Pfarrfest”: open to everyone, registrations cleared with one button',
      'Activity groups as in the confirmation preparation: 2 from green, 1 from blue, freely definable',
      'Interim status after voting (fixed, waiting list with position, fully booked) and a waiting-list tick box',
      'Demo people B to D have already voted and can change their vote',
      'Activities with description, parish and leader, import and export via Excel/CSV',
      'Database with migrations: data survives updates; reset the demo with one button',
      'Participant list with e-mail, date of birth and parish community, by hand or via CSV; personal links and invitations',
      'Accounts with roles (Junia and parish community admins), visibility per vote, demo accounts via buttons',
      'Freeze the allocation, move people by hand, lock seats and recalculate the rest',
      'Utilisation with the names of the people assigned to each date',
      'Keep the original: points and original seats when moving people, back to the original, log of changes',
      'Lists for activity leaders per date to print or as CSV',
      '“Not voted yet”: filter, copy e-mail addresses, reminders with the personal link',
      'Own result after publishing, German and English',
    ],
    phasesTitle: 'Order',
    phases: [
      { label: 'Step 1 · done', kind: 'done', title: 'Safe foundation', items: ['Database migrations', 'Demo switch (demo and real instance from the same code)', 'Description per activity', 'Import activities from Excel/CSV'] },
      { label: 'Step 2 · done', kind: 'done', title: 'Participants', items: ['Participant list per vote via CSV or by hand, with e-mail and Pfarrgemeinde', 'Personal links with a date-of-birth check', 'Invitations: demo mailbox, otherwise to copy'] },
      { label: 'Step 3 · done', kind: 'done', title: 'Login and roles', items: ['Junia and parish community admins', 'Visibility per vote', 'Importing and editing participants by role', 'Demo accounts via buttons'] },
      { label: 'Step 4 · done', kind: 'done', title: 'Allocation under control', items: ['Freeze and adjust the allocation', 'Names per date in the utilisation', 'Keep the original and log', 'Lists for activity leaders', '“Not voted yet” with reminders'] },
      { label: 'Step 5', title: 'New flow', items: ['Phases: draft → registration → allocation (internal first, then visible) → closed → ended', 'Automatic registration deadline: the allocation is calculated and frozen', 'No interim status during registration (switchable per vote)', 'During allocation: points locked, only waiting list and “give back a seat”', 'Free seats move up automatically, within the group, need before wish', 'Ended after the last date or by hand (starts the deletion period)', 'then: pilot with 20 to 30 confirmands'] },
      { label: 'Steps 6 and 7', title: 'Messages and special cases', items: ['E-mails from the app, always with a direct link', 'Message to the organisers', 'Cancellations shortly before the date: “fill seat” with offers and a deadline, e-mail also when it expires', 'Replacement for the person cancelling, targeted overbooking', 'Required staffing', '“More than required”'] },
      { label: 'In parallel', kind: 'parallel', title: 'With the diocese', items: ['Hosting: diocese server or EU provider', 'Data protection with the data protection officer', 'Legal notice and privacy policy'] },
    ],
    mustTitle: 'Needed before real registrations',
    mustLead: 'Without these points no real data of confirmands may go into the app.',
    mustHead: ['Topic', 'Missing', 'Why'],
    must: [
      { cells: ['Identification', 'Done (step 2): participant list, personal link, date-of-birth check', 'Otherwise anyone could sign in under any name'] },
      { cells: ['Login and roles', 'Done (step 3): accounts for several people, roles per parish community', 'Otherwise there would only be an open access'] },
      { cells: ['Freeze the allocation', 'Done (step 4): store, move by hand, lock, recalculate; publishing freezes automatically', 'Otherwise it would be recalculated on every request'] },
      { cells: ['Legal and privacy', 'Legal notice, privacy policy, automatic deletion, hosting in the EU', 'Data of minors'] },
    ],
    rolesTitle: 'Roles and permissions',
    rolesLead: 'Urfair runs as one shared tool for the Pfarre Urfahr-St. Junia and its 8 parish communities. Every vote belongs to a parish community or to all of Junia.',
    roles: ['Junia admin: sees and edits all votes, manages parish communities and accounts', 'Parish admin: creates votes for their own parish community'],
    visibilityLead: 'When creating a vote, its visibility is chosen (Junia admins may always do everything):',
    visibilityHead: ['Visibility', 'See', 'Edit'],
    visibility: [
      { cells: ['Parish community only', 'own parish community', 'own parish community'] },
      { cells: ['Junia shared', 'all parish communities', 'all parish communities'] },
      { cells: ['Junia, read only', 'all parish communities', 'Junia admins only'] },
    ],
    visibilityNote: '“See” refers to the organisation area. Which participants see a vote is set by the participant list.',
    flowTitle: 'New requirements for the voting flow',
    flowLead: 'From the feedback of 20 November 2026, with an assessment.',
    flowHead: ['Requirement', 'Assessment', 'Effort'],
    flow: [
      { cells: ['Choose more than required', 'Without it, the algorithm limits automatically to the required number (e.g. 3). New: participants deliberately state “I would also take up to N”; the organiser sets the upper limit. Extra seats are only given after the basic round, so nobody ends up empty because of people choosing many.', 'small to medium'] },
      { cells: ['Interim status and waiting list', 'Decided: during registration participants see no interim status (against tactics; switchable per vote). During allocation they see their seats and waiting-list position; points are locked then, only waiting-list ticks and “give back a seat” are possible.', 'demo done, rework in step 5'] },
      { cells: ['Cancellations and moving up', 'Decided: a free seat stays within the same group (green only for green). First to people missing a required seat, then to the waiting list. Whoever moves up chooses which seat of the group to give up (pre-selected: fewest points); it becomes free again until nobody is waiting. During allocation this is automatic; when closed only after “fill seat”, as an offer with a deadline (e-mail also when it expires), and only by hand from 2 days before the date. Whoever cancels gets a replacement on request: another date of the same activity, then free seats of the group; if nothing is free, the organisers overbook on purpose.', 'medium to large'] },
      { cells: ['Keep the original', 'Freezing stores the original allocation. When moving people, organisers see the person’s points and original seats and can go back to the original; a log shows who changed what and when.', 'done'] },
      { cells: ['Required staffing (all posts must be filled)', 'Chosen when creating a vote: “free choice” or “all posts must be filled” (e.g. parish festival). Then the algorithm first fills each date’s minimum staffing, preferring people who gave it points. While voting, “X people still missing here” is shown so people sign up where needed.', 'medium'] },
      { cells: ['Import activities', 'Via Excel/CSV instead of PDF, because a PDF has no fields. Done in step 1: download the list, edit it in Excel, upload, check the preview, add or replace.', 'done'] },
      { cells: ['Import participants', 'Per vote via CSV (first name, last name, e-mail, date of birth, Pfarrgemeinde); no central register of people: less data, deleted with the vote. The Pfarrgemeinde column allows evaluations per parish community in Junia votes. From step 3 tied to the role: parish community admins import and edit only rows of their own parish community; other rows are rejected with their row number.', 'medium (steps 2 and 3)'] },
    ],
    shouldTitle: 'Should be there for real use',
    should: ['✓ Participant lists for leaders to export or print', 'Automatic registration deadline (step 5)', '✓ Overview “not voted yet” for reminders'],
    laterTitle: 'Later, once it has proven itself',
    later: ['“Together with friends”: switchable per vote (off, pairs, groups up to 3); a group forms by a shared code, only with everyone’s consent; only a wish: after the allocation seats are swapped so the group shares at least one activity all of them chose, and only if nobody outside the group loses', 'Copy last year as a template', 'Take over participants from another vote', 'Votes “open to everyone” with an exclusion list (e.g. everyone except last year’s participants)'],
    questionsTitle: 'Open questions',
    questions: ['Who is the contact for hosting and data protection in the parish and diocese?', 'May parish community admins maintain their own participants in “Junia, read only” votes? (proposal: yes, only their own rows)', 'Pilot with 20 to 30 confirmands or the whole year group?'],
    back: '← Back to the start page',
  },
};

@Component({
  selector: 'app-plan',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @let p = plan();
    <p><a routerLink="/">{{ p.back }}</a></p>
    <div class="head">
      <p class="muted small">{{ p.asOf }}</p>
      <h1>{{ p.title }}</h1>
      <p>{{ p.lead }}</p>
    </div>

    <section class="stack">
      <h2>{{ p.phasesTitle }}</h2>
      <ol class="phases">
        @for (ph of p.phases; track ph.label) {
          <li class="card phase-card" [attr.data-kind]="ph.kind">
            <span class="step">{{ ph.label }}</span>
            <h3>{{ ph.title }}</h3>
            <ul>@for (i of ph.items; track i) { <li>{{ i }}</li> }</ul>
          </li>
        }
      </ol>
    </section>

    <section class="card stack">
      <h2>{{ p.mustTitle }}</h2>
      <p class="muted">{{ p.mustLead }}</p>
      <div class="tbl-wrap"><table>
        <thead><tr>@for (h of p.mustHead; track h) { <th>{{ h }}</th> }</tr></thead>
        <tbody>@for (r of p.must; track r.cells[0]) { <tr>@for (c of r.cells; track $index) { <td>{{ c }}</td> }</tr> }</tbody>
      </table></div>
    </section>

    <section class="card stack">
      <h2>{{ p.rolesTitle }}</h2>
      <p>{{ p.rolesLead }}</p>
      <ul>@for (r of p.roles; track r) { <li>{{ r }}</li> }</ul>
      <p>{{ p.visibilityLead }}</p>
      <div class="tbl-wrap"><table>
        <thead><tr>@for (h of p.visibilityHead; track h) { <th>{{ h }}</th> }</tr></thead>
        <tbody>@for (r of p.visibility; track r.cells[0]) { <tr>@for (c of r.cells; track $index) { <td>{{ c }}</td> }</tr> }</tbody>
      </table></div>
      <p class="muted small">{{ p.visibilityNote }}</p>
    </section>

    <section class="card stack">
      <h2>{{ p.flowTitle }}</h2>
      <p class="muted">{{ p.flowLead }}</p>
      <div class="tbl-wrap"><table>
        <thead><tr>@for (h of p.flowHead; track h) { <th>{{ h }}</th> }</tr></thead>
        <tbody>@for (r of p.flow; track r.cells[0]) { <tr>@for (c of r.cells; track $index) { <td>{{ c }}</td> }</tr> }</tbody>
      </table></div>
    </section>

    <div class="two">
      <section class="card stack"><h2>{{ p.shouldTitle }}</h2><ul>@for (s of p.should; track s) { <li>{{ s }}</li> }</ul></section>
      <section class="card stack"><h2>{{ p.laterTitle }}</h2><ul>@for (s of p.later; track s) { <li>{{ s }}</li> }</ul></section>
    </div>

    <section class="card stack">
      <h2>{{ p.questionsTitle }}</h2>
      <ul>@for (q of p.questions; track q) { <li>{{ q }}</li> }</ul>
    </section>

    <section class="card stack">
      <h2>{{ p.nowTitle }}</h2>
      <ul>@for (n of p.now; track n) { <li>{{ n }}</li> }</ul>
    </section>
  `,
  styles: `
    :host { display: grid; gap: 16px }
    .head { margin-bottom: 0 }
    ul { margin: 0; padding-left: 20px; display: grid; gap: 4px }
    .phases { list-style: none; margin: 0; padding: 0; display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr)); gap: 12px; counter-reset: none }
    .phase-card { display: grid; gap: 8px; align-content: start; border-top: 4px solid var(--accent) }
    .phase-card[data-kind="done"] { border-top-color: var(--good) }
    .phase-card[data-kind="parallel"] { border-top-color: var(--warn) }
    .step { font-size: 12px; font-weight: 700; letter-spacing: .04em; text-transform: uppercase; color: var(--muted) }
    .phase-card ul { font-size: 14px }
    .two { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 320px), 1fr)); gap: 16px }
  `,
})
export class PlanPage {
  private i18n = inject(I18n);
  protected plan = computed(() => PLAN[this.i18n.lang()]);
}
