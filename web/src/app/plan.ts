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

/** Roadmap from the planning document (as of 20.11.2026), shown in the app for presentations. */
const PLAN: Record<'de' | 'en', PlanText> = {
  de: {
    title: 'Umsetzungsplan',
    lead: 'Die Demo ist vorzeigbar. Bis zum echten Einsatz im Pfarrverbund Urfahr St. Junia fehlen vor allem Dinge, die man in der Vorführung nicht sieht.',
    asOf: 'Stand: 20. November 2026',
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
      'Eigenes Ergebnis nach Veröffentlichung, Deutsch und Englisch',
    ],
    phasesTitle: 'Reihenfolge',
    phases: [
      { label: 'Schritt 1 · erledigt', kind: 'done', title: 'Sichere Grundlage', items: ['Datenbank-Migrationen', 'Demo-Schalter (Demo und echte Instanz aus demselben Code)', 'Beschreibung pro Veranstaltung', 'Veranstaltungen aus Excel/CSV importieren'] },
      { label: 'Schritt 2', title: 'Teilnehmende', items: ['Teilnehmerliste pro Wahl per CSV oder von Hand, mit E-Mail und Pfarrgemeinde', 'Persönliche Links mit Prüfung (Geburts- oder Taufdatum)', 'Demo-Postfach statt echter E-Mails'] },
      { label: 'Schritt 3', title: 'Login und Rollen', items: ['Junia- und Pfarrgemeinde-Admins', 'Sichtbarkeit pro Wahl', 'Import und Pflege der Teilnehmenden je nach Rolle', 'Demo-Konten per Knopf'] },
      { label: 'Schritt 4', title: 'Zuteilung im Griff', items: ['Zuteilung festschreiben und anpassen', 'Listen für Leiter:innen', '„Noch nicht abgestimmt“', 'Anmeldeschluss automatisch', 'danach: Probelauf mit 20 bis 30 Firmlingen'] },
      { label: 'Schritt 5 und 6', title: 'Nach dem Probelauf', items: ['Abmelden und Nachrücken von der Warteliste', 'Pflichtbesetzung', '„Mehr als gefordert“', 'Zwischenstand ein- und ausschaltbar'] },
      { label: 'Parallel', kind: 'parallel', title: 'Mit der Diözese', items: ['Hosting: Diözesanserver oder EU-Anbieter', 'Datenschutz mit dem Datenschutzbeauftragten', 'Impressum und Datenschutzerklärung'] },
    ],
    mustTitle: 'Muss vor echten Anmeldungen passieren',
    mustLead: 'Ohne diese Punkte dürfen keine echten Daten von Firmlingen in die App.',
    mustHead: ['Thema', 'Was fehlt', 'Warum'],
    must: [
      { cells: ['Identifikation', 'E-Mail, CSV-Import, Hinzufügen von Hand, persönlicher Link, Prüfung nach dem Klick (z. B. Geburts- oder Taufdatum)', 'Heute kann sich jede:r unter jedem Namen anmelden'] },
      { cells: ['Login und Rollen', 'Echte Konten für mehrere Personen, Rollen pro Pfarre', 'Heute gibt es nur einen offenen Demo-Zugang'] },
      { cells: ['Zuteilung festschreiben', 'Speichern, einfrieren, händisch verschieben, fixieren, neu rechnen', 'Heute wird bei jedem Aufruf neu gerechnet'] },
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
      { cells: ['Zwischenstand und Warteliste', 'In der Demo umgesetzt: nach der Abgabe sieht man, was man fix hätte, wo man auf der Warteliste steht (mit Platz) und was ausgebucht ist; pro Projekt „auf Warteliste bleiben“. Achtung: ein sichtbarer Zwischenstand verleitet zum Taktieren, daher pro Wahl ein- und ausschaltbar machen.', 'Demo erledigt, Schalter klein'] },
      { cells: ['Abmelden und Nachrücken nach Schluss', 'Organisation oder Teilnehmende melden ab. Der frei gewordene Platz wird der ersten Person auf der Warteliste (mit Häkchen) angeboten, mit Frist zum Annehmen, sonst der nächsten. Setzt festgeschriebene Zuteilung und E-Mails voraus.', 'mittel bis groß'] },
      { cells: ['Pflichtbesetzung (Posten müssen besetzt sein)', 'Beim Anlegen wählbar: „freie Wahl“ oder „alle Posten müssen besetzt sein“ (z. B. Pfarrfest). Dann füllt der Algorithmus zuerst die Mindestbesetzung jedes Termins auf, bevorzugt mit Personen, die dort Punkte gegeben haben. In der Abstimmung steht „hier fehlen noch X Personen“, damit sich Leute gezielt melden.', 'mittel'] },
      { cells: ['Veranstaltungen importieren', 'Statt PDF über Excel/CSV, weil ein PDF keine Felder enthält. Umgesetzt in Schritt 1: Liste herunterladen, in Excel bearbeiten, hochladen, Vorschau prüfen, ergänzen oder ersetzen.', 'erledigt'] },
      { cells: ['Teilnehmende importieren', 'Pro Wahl per CSV (Vorname, Nachname, E-Mail, Geburtsdatum, Pfarrgemeinde), keine zentrale Personenkartei: weniger Daten, wird mit der Wahl gelöscht. Die Pfarrgemeinde-Spalte erlaubt bei Junia-Wahlen Auswertungen pro Pfarrgemeinde. Ab Schritt 3 an die Rolle gekoppelt: Pfarrgemeinde-Admins importieren und pflegen nur Zeilen der eigenen Pfarrgemeinde, fremde Zeilen werden mit Zeilennummer abgelehnt.', 'mittel (Schritt 2 und 3)'] },
    ],
    shouldTitle: 'Sollte für den echten Einsatz dabei sein',
    should: ['Beschreibung pro Veranstaltung (was, mitbringen, Leitung)', 'Teilnehmerlisten für Leiter:innen zum Exportieren oder Drucken', 'Anmeldeschluss automatisch', 'Überblick „noch nicht abgestimmt“ für Erinnerungen'],
    laterTitle: 'Später, wenn es sich bewährt',
    later: ['E-Mails aus der App', '„Mit Freund:in zusammen“', 'Vorjahr als Vorlage kopieren', 'Teilnehmende aus einer anderen Wahl übernehmen', 'Wahlen „für alle offen“ mit Ausnahmeliste (z. B. alle außer den Teilnehmenden vom Vorjahr)'],
    questionsTitle: 'Offene Fragen',
    questions: ['Wer ist Ansprechperson für Hosting und Datenschutz in Pfarre und Diözese?', 'Darf eine Person Admin mehrerer Pfarrgemeinden sein?', 'Dürfen Pfarrgemeinde-Admins bei „Junia, nur lesen“ die Teilnehmenden ihrer Pfarrgemeinde pflegen? (Vorschlag: ja, nur die eigenen Zeilen)', 'Probelauf mit 20 bis 30 Firmlingen oder gleich dem ganzen Jahrgang?'],
    back: '← Zur Startseite',
  },
  en: {
    title: 'Roadmap',
    lead: 'The demo is ready to show. Before real use across the parish network Urfahr St. Junia, mostly invisible things are missing.',
    asOf: 'As of 20 November 2026',
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
      'Own result after publishing, German and English',
    ],
    phasesTitle: 'Order',
    phases: [
      { label: 'Step 1 · done', kind: 'done', title: 'Safe foundation', items: ['Database migrations', 'Demo switch (demo and real instance from the same code)', 'Description per activity', 'Import activities from Excel/CSV'] },
      { label: 'Step 2', title: 'Participants', items: ['Participant list per vote via CSV or by hand, with e-mail and Pfarrgemeinde', 'Personal links with a check (date of birth or baptism)', 'Demo mailbox instead of real e-mails'] },
      { label: 'Step 3', title: 'Login and roles', items: ['Junia and parish community admins', 'Visibility per vote', 'Importing and editing participants by role', 'Demo accounts via buttons'] },
      { label: 'Step 4', title: 'Allocation under control', items: ['Freeze and adjust the allocation', 'Lists for activity leaders', '“Not voted yet”', 'Automatic registration deadline', 'then: pilot with 20 to 30 confirmands'] },
      { label: 'Steps 5 and 6', title: 'After the pilot', items: ['Cancellations and moving up from the waiting list', 'Required staffing', '“More than required”', 'Interim status switchable'] },
      { label: 'In parallel', kind: 'parallel', title: 'With the diocese', items: ['Hosting: diocese server or EU provider', 'Data protection with the data protection officer', 'Legal notice and privacy policy'] },
    ],
    mustTitle: 'Needed before real registrations',
    mustLead: 'Without these points no real data of confirmands may go into the app.',
    mustHead: ['Topic', 'Missing', 'Why'],
    must: [
      { cells: ['Identification', 'E-mail, CSV import, manual entry, personal link, check after the click (e.g. date of birth or baptism)', 'Today anyone can sign in under any name'] },
      { cells: ['Login and roles', 'Real accounts for several people, roles per parish', 'Today there is only an open demo access'] },
      { cells: ['Freeze the allocation', 'Store, freeze, move by hand, lock, recalculate', 'Today it is recalculated on every request'] },
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
      { cells: ['Interim status and waiting list', 'Done in the demo: after voting you see what you would get, where you are on the waiting list (with position) and what is fully booked; per activity “stay on the waiting list”. Note: a visible interim status invites tactics, so make it switchable per vote.', 'demo done, switch small'] },
      { cells: ['Cancellations and moving up after the deadline', 'Organisers or participants cancel. The free seat is offered to the first person on the waiting list (with the tick), with a deadline to accept, otherwise to the next. Needs a frozen allocation and e-mails.', 'medium to large'] },
      { cells: ['Required staffing (all posts must be filled)', 'Chosen when creating a vote: “free choice” or “all posts must be filled” (e.g. parish festival). Then the algorithm first fills each date’s minimum staffing, preferring people who gave it points. While voting, “X people still missing here” is shown so people sign up where needed.', 'medium'] },
      { cells: ['Import activities', 'Via Excel/CSV instead of PDF, because a PDF has no fields. Done in step 1: download the list, edit it in Excel, upload, check the preview, add or replace.', 'done'] },
      { cells: ['Import participants', 'Per vote via CSV (first name, last name, e-mail, date of birth, Pfarrgemeinde); no central register of people: less data, deleted with the vote. The Pfarrgemeinde column allows evaluations per parish community in Junia votes. From step 3 tied to the role: parish community admins import and edit only rows of their own parish community; other rows are rejected with their row number.', 'medium (steps 2 and 3)'] },
    ],
    shouldTitle: 'Should be there for real use',
    should: ['Description per activity (what, what to bring, who leads)', 'Participant lists for leaders to export or print', 'Automatic registration deadline', 'Overview “not voted yet” for reminders'],
    laterTitle: 'Later, once it has proven itself',
    later: ['E-mails from the app', '“Together with a friend”', 'Copy last year as a template', 'Take over participants from another vote', 'Votes “open to everyone” with an exclusion list (e.g. everyone except last year’s participants)'],
    questionsTitle: 'Open questions',
    questions: ['Who is the contact for hosting and data protection in the parish and diocese?', 'May one person be admin of several parish communities?', 'May parish community admins maintain their own participants in “Junia, read only” votes? (proposal: yes, only their own rows)', 'Pilot with 20 to 30 confirmands or the whole year group?'],
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
