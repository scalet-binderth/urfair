import { Injectable, signal } from '@angular/core';

export type Lang = 'de' | 'en';
type Args = Record<string, string | number>;

/** Minimal translation service: two dictionaries, {placeholder} substitution, choice stored in localStorage. */
const texts: Record<Lang, Record<string, string>> = {
  de: {
    'app.name': 'Urfair',
    'app.tagline': 'Faire Platzvergabe mit Punkten',
    'app.madeFor': 'Entwickelt für die Pfarre Urfahr St. Junia · Open Source (MIT)',
    'app.demo': 'Testversion. Alle Anmeldungen außer deiner eigenen sind erfundene Beispieldaten.',
    'nav.vote': 'Wählen', 'nav.admin': 'Organisation',
    'lang.switch': 'English',
    'common.loading': 'Wird geladen …',
    'common.loadError': 'Die Daten konnten nicht geladen werden. Bitte später erneut versuchen.',
    'common.unlimited': 'offen', 'common.seats': '{n} Plätze', 'common.only.m': 'nur Burschen', 'common.only.w': 'nur Mädchen',
    'common.flexibleUntil': 'bis {date}, nach Vereinbarung',
    'common.save': 'Speichern', 'common.cancel': 'Abbrechen', 'common.delete': 'Löschen', 'common.edit': 'Bearbeiten',
    'common.saved': 'Gespeichert.', 'common.deleted': 'Gelöscht.', 'common.reallyDelete': 'Wirklich löschen?',
    'common.deadline': 'Anmeldeschluss: {date}',

    'phase.Draft': 'Entwurf', 'phase.Open': 'Anmeldung offen', 'phase.Closed': 'Geschlossen', 'phase.Published': 'Veröffentlicht',

    'home.title': 'Wähle deine Veranstaltungen',
    'home.lead': 'Statt „wer zuerst klickt“ verteilst du Punkte auf das, was dir wichtig ist. Zugeteilt wird erst nach dem Anmeldeschluss, fair für alle.',
    'home.empty': 'Für dich ist derzeit keine Anmeldung freigeschaltet. Wenn du glaubst, dass das ein Fehler ist, wende dich an das Organisationsteam.',
    'home.open': 'Offene Anmeldungen', 'home.done': 'Abgeschlossen',
    'home.closed': 'Die Anmeldung ist beendet. Die Zuteilung wird gerade vorbereitet.',
    'home.published': 'Die Zuteilung ist fertig.', 'home.seeResult': 'Mein Ergebnis ansehen',
    'result.title': 'Deine Zuteilung', 'result.lead': 'Du bist für {n} Veranstaltung(en) eingeteilt:',
    'result.noVote': 'Du hast bei dieser Wahl nicht abgestimmt.',
    'start.demoPeople': 'Oder als Demo-Person anmelden:',
    'start.demo.A': 'für keine Wahl freigeschaltet', 'start.demo.B': 'eine offene Wahl',
    'start.demo.C': 'eine offene, eine abgeschlossene (mit Ergebnis)', 'start.demo.D': 'eine offene, zwei abgeschlossene',
    'home.activities': '{n} Veranstaltungen', 'home.go': 'Zur Wahl',
    'home.hello': 'Hallo {name}, wofür möchtest du abstimmen?',

    'start.title': 'Willkommen bei Urfair',
    'start.lead': 'Plätze fair vergeben: Statt „wer zuerst klickt“ verteilt jede:r Punkte auf das, was wichtig ist. Zugeteilt wird erst nach dem Anmeldeschluss.',
    'start.participant': 'Ich nehme teil',
    'start.participantText': 'Gib deinen Namen ein, wähle deine Veranstaltungen und verteile deine Punkte.',
    'start.participantGo': 'Weiter',
    'start.admin': 'Ich organisiere',
    'start.adminText': 'Wahlen anlegen, Veranstaltungen und Termine pflegen, Nachfrage ansehen und zuteilen.',
    'start.adminDemo': 'In der Testversion ist kein Zugangsschlüssel nötig.',
    'start.adminGo': 'Zur Organisation',
    'session.as': 'Angemeldet als {name}', 'session.signOut': 'Abmelden',
    'vote.as': 'Du stimmst ab als {name}.', 'vote.loaded': 'Deine bisherige Wahl wurde geladen. Du kannst sie ändern und erneut abgeben.',
    'vote.back': '← Zurück zur Übersicht',

    'vote.title': 'Wähle deine Veranstaltungen',
    'vote.lead': 'Du hast {budget} Punkte. Verteile sie auf mindestens {min} Veranstaltungen, höchstens {max} auf eine. Je mehr Punkte, desto wichtiger ist sie dir. Du bekommst {k} Veranstaltungen zugeteilt, erst nach dem Anmeldeschluss.',
    'vote.closed': 'Für diese Wahl ist derzeit keine Anmeldung möglich.',
    'vote.pickSessions': 'Hake ab, an welchen Terminen du Zeit hast:',
    'vote.remaining': 'Noch zu vergeben', 'vote.of': 'von {n}',
    'vote.check.budget': 'Alle {n} Punkte verteilt', 'vote.check.min': 'Mindestens {n} Veranstaltungen ({count})',
    'vote.check.sessions': 'Bei jeder gewählten Veranstaltung passt ein Termin',
    'vote.ranking': 'Deine Reihung', 'vote.rankingEmpty': 'Noch keine Punkte vergeben.',
    'vote.name': 'Dein Name', 'vote.namePlaceholder': 'Vorname Nachname',
    'vote.submit': 'Wahl abgeben', 'vote.example': 'Beispiel ausfüllen',
    'vote.note': 'Du kannst deine Wahl bis zum Anmeldeschluss ändern. Wann du abgibst, macht keinen Unterschied.',
    'vote.saved': 'Deine Wahl ist gespeichert.', 'vote.replaced': 'Deine Wahl wurde aktualisiert.',
    'vote.less': '5 Punkte weniger für {title}', 'vote.more': '5 Punkte mehr für {title}',

    'admin.title': 'Organisation',
    'admin.lead': 'Hier legt ihr Wahlen an, pflegt Veranstaltungen und Termine und startet die Zuteilung.',
    'admin.key': 'Zugangsschlüssel für die Organisation', 'admin.keyNeeded': 'Für diesen Bereich ist ein Zugangsschlüssel nötig.',
    'admin.new': 'Neue Wahl', 'admin.newName': 'Name, z. B. „Ferienprogramm Sommer 2027“',
    'admin.withSample': 'Mit Beispieldaten (Firmvorbereitung Urfahr) füllen', 'admin.create': 'Anlegen',
    'admin.col.name': 'Name', 'admin.col.phase': 'Phase', 'admin.col.deadline': 'Anmeldeschluss',
    'admin.col.activities': 'Veranstaltungen', 'admin.col.ballots': 'Anmeldungen', 'admin.none': 'Noch keine Wahlen angelegt.',
    'admin.back': '← Alle Wahlen',
    'admin.tab.settings': 'Einstellungen', 'admin.tab.activities': 'Veranstaltungen', 'admin.tab.demand': 'Nachfrage',
    'admin.tab.results': 'Zuteilung', 'admin.tab.demo': 'Beispieldaten', 'admin.tab.participants': 'Teilnehmende',
    'admin.participantsNote': 'Nur Personen auf dieser Liste sehen die Wahl und können abstimmen. In der Testversion zählt der Name; später kommen E-Mail, CSV-Import und persönliche Links dazu.',
    'admin.participantName': 'Name', 'admin.addParticipant': 'Hinzufügen', 'admin.col.voted': 'Abgestimmt',
    'admin.voted': 'abgestimmt', 'admin.notVoted': 'noch nicht', 'admin.noParticipants': 'Noch niemand auf der Liste.',
    'admin.participantLink': 'Link für Teilnehmende', 'admin.copyLink': 'Link kopieren', 'admin.linkCopied': 'Link kopiert.',
    'admin.name': 'Name', 'admin.phase': 'Phase', 'admin.deadline': 'Anmeldeschluss',
    'admin.phaseHint': 'Nur in der Phase „Anmeldung offen“ können Teilnehmende abstimmen.',
    'admin.rules': 'Regeln', 'admin.budget': 'Punkte pro Person', 'admin.min': 'Mindestens bepunktete Veranstaltungen',
    'admin.max': 'Höchstens Punkte auf eine Veranstaltung', 'admin.k': 'Zugeteilte Veranstaltungen pro Person',
    'admin.rulesNote': 'Beim Ändern der Regeln werden die Beispieldaten neu erzeugt.',
    'admin.deleteSlot': 'Wahl löschen', 'admin.deleteSlotWarn': 'Alle Veranstaltungen und Anmeldungen dieser Wahl gehen verloren.',
    'admin.addActivity': 'Veranstaltung hinzufügen', 'admin.noActivities': 'Noch keine Veranstaltungen. Leg die erste an.',
    'admin.sessions': '{n} Termine',
    'admin.ballotCount': '{n} Anmeldungen, davon {s} Beispiel-Teilnehmende.',
    'admin.sampleCount': 'Anzahl Beispiel-Teilnehmende', 'admin.removeReal': 'Echte Anmeldungen ebenfalls löschen',
    'admin.reset': 'Beispieldaten neu erzeugen', 'admin.resetDone': 'Beispieldaten neu erzeugt.',
    'admin.demandNote': 'Der Balken zeigt Interessierte im Verhältnis zu den Plätzen. Orange heißt überbucht.',
    'admin.col.activity': 'Veranstaltung', 'admin.col.seats': 'Plätze', 'admin.col.interested': 'Interessierte',
    'admin.col.points': 'Punkte gesamt', 'admin.col.avg': 'Ø Punkte', 'admin.col.demand': 'Nachfrage',

    'editor.title': 'Titel', 'editor.restriction': 'Teilnahme', 'editor.all': 'Alle',
    'editor.sessions': 'Termine', 'editor.start': 'Beginn', 'editor.end': 'Ende', 'editor.location': 'Ort',
    'editor.capacity': 'Plätze', 'editor.capacityHint': 'leer = unbegrenzt', 'editor.flexible': 'Termin nach Vereinbarung',
    'editor.addSession': 'Termin hinzufügen', 'editor.removeSession': 'Termin entfernen',

    'results.title': 'Ergebnis der Zuteilung',
    'results.lead': '{n} Teilnehmende, je {k} Veranstaltungen. Zum Vergleich: dieselben Wünsche nach „Wer zuerst kommt“ (alle klicken zur Öffnung, Reihenfolge zufällig).',
    'results.topChoice': 'bekamen ihre Nr.-1-Veranstaltung', 'results.avg': 'Ø Zufriedenheit (erreichte Punkte)',
    'results.worst': 'Zufriedenheit der schlechtesten 10 %', 'results.fallback': 'Teilnehmende mit Ersatzveranstaltung',
    'results.fcfs': 'Wer zuerst kommt:',
    'results.how': 'So wird zugeteilt',
    'results.how1': 'Es wird in {k} Runden verteilt. Jede Runde bekommt jede Person eine Veranstaltung.',
    'results.how2': 'Innerhalb einer Runde hat Vorrang, wer mehr Punkte gesetzt hat. Bei Gleichstand entscheidet das Los, nie die Uhrzeit.',
    'results.how3': 'Ist die Wunschveranstaltung voll oder kollidiert der Termin, kommt die nächste der eigenen Reihung dran. Sonst gibt es eine Ersatzveranstaltung mit freien Plätzen.',
    'results.how4': 'Zum Schluss wird getauscht, wenn zwei Personen dadurch beide nicht schlechter dastehen.',
    'results.load': 'Auslastung', 'results.people': 'Teilnehmende', 'results.search': 'Suchen',
    'results.copy': 'Liste kopieren (CSV)', 'results.copied': 'Liste in die Zwischenablage kopiert.',
    'results.copyFallback': 'Text markiert. Mit Strg+C kopieren.',
    'results.col.name': 'Name', 'results.col.assigned': 'Zugeteilt (Punkte)', 'results.col.satisfaction': 'Zufriedenheit',
    'results.real': 'echt', 'results.fallbackChip': 'Ersatz', 'results.showAll': 'Alle {n} anzeigen', 'results.flexible': 'flexibel',
    'results.empty': 'Noch keine Anmeldungen oder Veranstaltungen.',

    'error.nameRequired': 'Bitte gib einen Namen an.',
    'error.unknownActivity': 'Unbekannte Veranstaltung.',
    'error.negativePoints': 'Punkte dürfen nicht negativ sein.',
    'error.pointStep': 'Punkte bitte in {step}er-Schritten vergeben.',
    'error.maxPerActivity': 'Höchstens {max} Punkte für „{activity}“.',
    'error.budgetNotSpent': 'Es müssen genau {budget} Punkte verteilt werden (derzeit {sum}).',
    'error.tooFewActivities': 'Bitte mindestens {min} Veranstaltungen bepunkten (derzeit {count}).',
    'error.noSessionAvailable': 'Bei „{activity}“ hast du an keinem Termin Zeit.',
    'error.maxAboveBudget': 'Die Höchstpunkte pro Veranstaltung dürfen nicht über dem Punktekonto liegen.',
    'error.tooManyActivitiesForBudget': 'Mit diesem Punktekonto sind höchstens {max} Veranstaltungen möglich.',
    'error.perParticipantAboveMin': 'Es sollten mindestens so viele Veranstaltungen bepunktet werden, wie zugeteilt werden.',
    'error.perParticipantTooLow': 'Pro Person muss mindestens 1 Veranstaltung zugeteilt werden.',
    'error.registrationClosed': 'Die Anmeldung ist derzeit geschlossen.',
    'error.notListed': 'Du stehst nicht auf der Teilnehmerliste dieser Wahl.',
    'error.alreadyListed': 'Diese Person steht schon auf der Liste.',
    'error.notPublished': 'Die Zuteilung ist noch nicht veröffentlicht.',
    'error.titleRequired': 'Bitte gib einen Titel an.',
    'error.restrictionInvalid': 'Ungültige Teilnahme-Einschränkung.',
    'error.sessionRequired': 'Bitte mindestens einen Termin anlegen.',
    'error.capacityInvalid': 'Plätze müssen mindestens 1 sein (oder leer für unbegrenzt).',
    'error.endRequired': 'Bitte bei jedem Termin ein Ende angeben (oder „nach Vereinbarung“ wählen).',
    'error.endBeforeStart': 'Das Ende eines Termins muss nach dem Beginn liegen.',
    'error.unauthorized': 'Der Zugangsschlüssel ist falsch.',
    'error.generic': 'Das hat nicht geklappt. Bitte erneut versuchen.',
  },
  en: {
    'app.name': 'Urfair',
    'app.tagline': 'Fair seat allocation with points',
    'app.madeFor': 'Made for the parish Pfarre Urfahr St. Junia · open source (MIT)',
    'app.demo': 'Test version. All registrations except your own are invented sample data.',
    'nav.vote': 'Vote', 'nav.admin': 'Organisation',
    'lang.switch': 'Deutsch',
    'common.loading': 'Loading …',
    'common.loadError': 'The data could not be loaded. Please try again later.',
    'common.unlimited': 'open', 'common.seats': '{n} seats', 'common.only.m': 'boys only', 'common.only.w': 'girls only',
    'common.flexibleUntil': 'until {date}, by arrangement',
    'common.save': 'Save', 'common.cancel': 'Cancel', 'common.delete': 'Delete', 'common.edit': 'Edit',
    'common.saved': 'Saved.', 'common.deleted': 'Deleted.', 'common.reallyDelete': 'Really delete?',
    'common.deadline': 'Registration closes: {date}',

    'phase.Draft': 'Draft', 'phase.Open': 'Registration open', 'phase.Closed': 'Closed', 'phase.Published': 'Published',

    'home.title': 'Choose your activities',
    'home.lead': 'Instead of “whoever clicks first”, you spread points over what matters to you. Seats are assigned after registration closes, fairly for everyone.',
    'home.empty': 'No registration is open for you at the moment. If you think this is a mistake, please contact the organisers.',
    'home.open': 'Open registrations', 'home.done': 'Finished',
    'home.closed': 'Registration has closed. The allocation is being prepared.',
    'home.published': 'The allocation is ready.', 'home.seeResult': 'See my result',
    'result.title': 'Your allocation', 'result.lead': 'You have been assigned to {n} activity(ies):',
    'result.noVote': 'You did not vote in this round.',
    'start.demoPeople': 'Or sign in as a demo person:',
    'start.demo.A': 'not listed anywhere', 'start.demo.B': 'one open vote',
    'start.demo.C': 'one open, one finished (with result)', 'start.demo.D': 'one open, two finished',
    'home.activities': '{n} activities', 'home.go': 'Go to vote',
    'home.hello': 'Hello {name}, what would you like to vote on?',

    'start.title': 'Welcome to Urfair',
    'start.lead': 'Fair seat allocation: instead of “whoever clicks first”, everyone spreads points over what matters to them. Seats are assigned after registration closes.',
    'start.participant': 'I am taking part',
    'start.participantText': 'Enter your name, pick your activities and spread your points.',
    'start.participantGo': 'Continue',
    'start.admin': 'I am organising',
    'start.adminText': 'Create votes, maintain activities and dates, check demand and run the allocation.',
    'start.adminDemo': 'No access key is needed in the test version.',
    'start.adminGo': 'Go to organisation',
    'session.as': 'Signed in as {name}', 'session.signOut': 'Sign out',
    'vote.as': 'You are voting as {name}.', 'vote.loaded': 'Your previous vote has been loaded. You can change it and submit again.',
    'vote.back': '← Back to overview',

    'vote.title': 'Choose your activities',
    'vote.lead': 'You have {budget} points. Spread them over at least {min} activities, at most {max} on one. More points mean it matters more to you. You will be assigned {k} activities after registration closes.',
    'vote.closed': 'Registration for this vote is not open at the moment.',
    'vote.pickSessions': 'Tick the dates you can make:',
    'vote.remaining': 'Points left', 'vote.of': 'of {n}',
    'vote.check.budget': 'All {n} points spent', 'vote.check.min': 'At least {n} activities ({count})',
    'vote.check.sessions': 'Every chosen activity has a date that works',
    'vote.ranking': 'Your ranking', 'vote.rankingEmpty': 'No points given yet.',
    'vote.name': 'Your name', 'vote.namePlaceholder': 'First name Last name',
    'vote.submit': 'Submit vote', 'vote.example': 'Fill in an example',
    'vote.note': 'You can change your vote until registration closes. When you submit makes no difference.',
    'vote.saved': 'Your vote has been saved.', 'vote.replaced': 'Your vote has been updated.',
    'vote.less': '5 points less for {title}', 'vote.more': '5 points more for {title}',

    'admin.title': 'Organisation',
    'admin.lead': 'Create votes, maintain activities and dates, and run the allocation.',
    'admin.key': 'Organisation access key', 'admin.keyNeeded': 'An access key is required for this area.',
    'admin.new': 'New vote', 'admin.newName': 'Name, e.g. “Summer holiday programme 2027”',
    'admin.withSample': 'Fill with sample data (confirmation preparation Urfahr)', 'admin.create': 'Create',
    'admin.col.name': 'Name', 'admin.col.phase': 'Phase', 'admin.col.deadline': 'Registration closes',
    'admin.col.activities': 'Activities', 'admin.col.ballots': 'Registrations', 'admin.none': 'No votes yet.',
    'admin.back': '← All votes',
    'admin.tab.settings': 'Settings', 'admin.tab.activities': 'Activities', 'admin.tab.demand': 'Demand',
    'admin.tab.results': 'Allocation', 'admin.tab.demo': 'Sample data', 'admin.tab.participants': 'Participants',
    'admin.participantsNote': 'Only people on this list see the vote and can take part. In the test version the name counts; e-mail, CSV import and personal links follow.',
    'admin.participantName': 'Name', 'admin.addParticipant': 'Add', 'admin.col.voted': 'Voted',
    'admin.voted': 'voted', 'admin.notVoted': 'not yet', 'admin.noParticipants': 'Nobody on the list yet.',
    'admin.participantLink': 'Link for participants', 'admin.copyLink': 'Copy link', 'admin.linkCopied': 'Link copied.',
    'admin.name': 'Name', 'admin.phase': 'Phase', 'admin.deadline': 'Registration closes',
    'admin.phaseHint': 'Participants can only vote while the phase is “Registration open”.',
    'admin.rules': 'Rules', 'admin.budget': 'Points per person', 'admin.min': 'Minimum activities with points',
    'admin.max': 'Maximum points on one activity', 'admin.k': 'Activities assigned per person',
    'admin.rulesNote': 'Changing the rules regenerates the sample data.',
    'admin.deleteSlot': 'Delete vote', 'admin.deleteSlotWarn': 'All activities and registrations of this vote will be lost.',
    'admin.addActivity': 'Add activity', 'admin.noActivities': 'No activities yet. Create the first one.',
    'admin.sessions': '{n} dates',
    'admin.ballotCount': '{n} registrations, {s} of them sample participants.',
    'admin.sampleCount': 'Number of sample participants', 'admin.removeReal': 'Also delete real registrations',
    'admin.reset': 'Regenerate sample data', 'admin.resetDone': 'Sample data regenerated.',
    'admin.demandNote': 'The bar shows interested participants relative to seats. Orange means overbooked.',
    'admin.col.activity': 'Activity', 'admin.col.seats': 'Seats', 'admin.col.interested': 'Interested',
    'admin.col.points': 'Total points', 'admin.col.avg': 'Avg points', 'admin.col.demand': 'Demand',

    'editor.title': 'Title', 'editor.restriction': 'Who can take part', 'editor.all': 'Everyone',
    'editor.sessions': 'Dates', 'editor.start': 'Start', 'editor.end': 'End', 'editor.location': 'Location',
    'editor.capacity': 'Seats', 'editor.capacityHint': 'empty = unlimited', 'editor.flexible': 'Date by arrangement',
    'editor.addSession': 'Add date', 'editor.removeSession': 'Remove date',

    'results.title': 'Allocation result',
    'results.lead': '{n} participants, {k} activities each. For comparison: the same wishes under “first come, first served” (everyone clicks at opening, random order).',
    'results.topChoice': 'got their no. 1 activity', 'results.avg': 'Average satisfaction (points achieved)',
    'results.worst': 'Satisfaction of the worst 10 %', 'results.fallback': 'Participants with a fallback activity',
    'results.fcfs': 'First come, first served:',
    'results.how': 'How the allocation works',
    'results.how1': 'Seats are handed out in {k} rounds. In each round every participant gets one activity.',
    'results.how2': 'Within a round, whoever gave more points wins. Ties are decided by lot, never by time.',
    'results.how3': 'If the wish is full or the date clashes, the next one in the own ranking is tried. Otherwise a fallback activity with free seats is assigned.',
    'results.how4': 'Finally, two participants swap whenever neither ends up worse off.',
    'results.load': 'Utilisation', 'results.people': 'Participants', 'results.search': 'Search',
    'results.copy': 'Copy list (CSV)', 'results.copied': 'List copied to the clipboard.',
    'results.copyFallback': 'Text selected. Press Ctrl+C to copy.',
    'results.col.name': 'Name', 'results.col.assigned': 'Assigned (points)', 'results.col.satisfaction': 'Satisfaction',
    'results.real': 'real', 'results.fallbackChip': 'fallback', 'results.showAll': 'Show all {n}', 'results.flexible': 'flexible',
    'results.empty': 'No registrations or activities yet.',

    'error.nameRequired': 'Please enter a name.',
    'error.unknownActivity': 'Unknown activity.',
    'error.negativePoints': 'Points cannot be negative.',
    'error.pointStep': 'Please give points in steps of {step}.',
    'error.maxPerActivity': 'At most {max} points for “{activity}”.',
    'error.budgetNotSpent': 'Exactly {budget} points must be spent (currently {sum}).',
    'error.tooFewActivities': 'Please give points to at least {min} activities (currently {count}).',
    'error.noSessionAvailable': 'You have no available date for “{activity}”.',
    'error.maxAboveBudget': 'The maximum per activity cannot exceed the point budget.',
    'error.tooManyActivitiesForBudget': 'This budget allows at most {max} activities.',
    'error.perParticipantAboveMin': 'At least as many activities should get points as are assigned.',
    'error.perParticipantTooLow': 'At least 1 activity must be assigned per participant.',
    'error.registrationClosed': 'Registration is currently closed.',
    'error.notListed': 'You are not on the participant list of this vote.',
    'error.alreadyListed': 'This person is already on the list.',
    'error.notPublished': 'The allocation has not been published yet.',
    'error.titleRequired': 'Please enter a title.',
    'error.restrictionInvalid': 'Invalid participation restriction.',
    'error.sessionRequired': 'Please add at least one date.',
    'error.capacityInvalid': 'Seats must be at least 1 (or empty for unlimited).',
    'error.endRequired': 'Please give every date an end (or choose “by arrangement”).',
    'error.endBeforeStart': 'A date must end after it starts.',
    'error.unauthorized': 'The access key is wrong.',
    'error.generic': 'Something went wrong. Please try again.',
  },
};

@Injectable({ providedIn: 'root' })
export class I18n {
  readonly lang = signal<Lang>(I18n.initial());

  private static initial(): Lang {
    try { return localStorage.getItem('lang') === 'en' ? 'en' : 'de'; } catch { return 'de'; }
  }

  toggle(): void {
    const next: Lang = this.lang() === 'de' ? 'en' : 'de';
    this.lang.set(next);
    document.documentElement.lang = next;
    try { localStorage.setItem('lang', next); } catch { /* storage unavailable: keep in memory */ }
  }

  t(key: string, args?: Args): string {
    const text = texts[this.lang()][key] ?? texts.de[key] ?? key;
    return args ? text.replace(/\{(\w+)\}/g, (_, k) => String(args[k] ?? `{${k}}`)) : text;
  }

  /** Translates validation errors returned by the API. */
  error(e: { code: string; args?: Args | null }): string {
    return this.t('error.' + e.code, e.args ?? undefined);
  }

  /** Translates a list of API errors, or a generic message for anything else. */
  errors(e: unknown): string[] {
    const list = (e as { errors?: { code: string; args?: Args | null }[] })?.errors;
    return list?.length ? list.map(x => this.error(x)) : [this.t('error.generic')];
  }

  locale(): string { return this.lang() === 'de' ? 'de-AT' : 'en-GB'; }

  /** Formats a session date range, e.g. "Sa., 21.11. 14:00–16:00". */
  when(s: { start: string; end: string | null; flexible: boolean }): string {
    const start = new Date(s.start);
    const loc = this.locale();
    const day = new Intl.DateTimeFormat(loc, { weekday: 'short', day: 'numeric', month: 'numeric' });
    const time = new Intl.DateTimeFormat(loc, { hour: '2-digit', minute: '2-digit' });
    if (s.flexible || !s.end) {
      return this.t('common.flexibleUntil', { date: new Intl.DateTimeFormat(loc, { dateStyle: 'medium' }).format(start) });
    }
    const end = new Date(s.end);
    const sameDay = start.toDateString() === end.toDateString();
    return `${day.format(start)} ${time.format(start)}–${sameDay ? '' : day.format(end) + ' '}${time.format(end)}`;
  }

  date(iso: string | null): string {
    return iso ? new Intl.DateTimeFormat(this.locale(), { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(iso)) : '–';
  }

  percent(x: number): string { return Math.round(x * 100) + ' %'; }
}
