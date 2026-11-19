import { Injectable, signal } from '@angular/core';

export type Lang = 'de' | 'en';
type Args = Record<string, string | number>;

/** Minimal translation service: two dictionaries, {placeholder} substitution, choice stored in localStorage. */
const texts: Record<Lang, Record<string, string>> = {
  de: {
    'app.name': 'UrFairSlot',
    'app.madeFor': 'Entwickelt für die Pfarre Urfahr St. Junia · Open Source (MIT)',
    'app.parish': 'Firmvorbereitung Urfahr 2026/27',
    'app.demo': 'Testversion. Alle Anmeldungen außer deiner eigenen sind erfundene Beispieldaten.',
    'nav.vote': 'Wählen', 'nav.admin': 'Organisation', 'nav.results': 'Zuteilung',
    'lang.switch': 'English',
    'common.loading': 'Wird geladen …',
    'common.loadError': 'Die Daten konnten nicht geladen werden. Bitte später erneut versuchen.',
    'common.unlimited': 'offen', 'common.seats': '{n} Plätze', 'common.only.m': 'nur Burschen', 'common.only.w': 'nur Mädchen',
    'common.flexibleUntil': 'bis {date}, nach Vereinbarung',

    'vote.title': 'Wähle deine Veranstaltungen',
    'vote.lead': 'Du hast {budget} Punkte. Verteile sie auf mindestens {min} Veranstaltungen, höchstens {max} auf eine. Je mehr Punkte, desto wichtiger ist sie dir. Du bekommst {k} Veranstaltungen zugeteilt, erst nach dem Anmeldeschluss.',
    'vote.pickSessions': 'Hake ab, an welchen Terminen du Zeit hast:',
    'vote.remaining': 'Noch zu vergeben', 'vote.of': 'von {n}',
    'vote.check.budget': 'Alle {n} Punkte verteilt', 'vote.check.min': 'Mindestens {n} Veranstaltungen ({count})',
    'vote.check.sessions': 'Bei jeder gewählten Veranstaltung passt ein Termin',
    'vote.ranking': 'Deine Reihung', 'vote.rankingEmpty': 'Noch keine Punkte vergeben.',
    'vote.name': 'Dein Name', 'vote.namePlaceholder': 'Vorname Nachname',
    'vote.submit': 'Wahl abgeben', 'vote.example': 'Beispiel ausfüllen',
    'vote.note': 'Du kannst deine Wahl bis zum Anmeldeschluss ändern. Wann du abgibst, macht keinen Unterschied.',
    'vote.saved': 'Deine Wahl ist gespeichert.', 'vote.replaced': 'Deine Wahl wurde aktualisiert.',
    'vote.seeResult': 'Zuteilung ansehen', 'vote.less': '5 Punkte weniger für {title}', 'vote.more': '5 Punkte mehr für {title}',

    'admin.title': 'Regeln und Nachfrage',
    'admin.lead': 'Hier legt das Firmteam die Spielregeln fest und sieht, wie gefragt jede Veranstaltung ist.',
    'admin.key': 'Zugangsschlüssel für die Organisation', 'admin.keyNeeded': 'Für diesen Bereich ist ein Zugangsschlüssel nötig.',
    'admin.rules': 'Regeln', 'admin.budget': 'Punkte pro Firmling', 'admin.min': 'Mindestens bepunktete Veranstaltungen',
    'admin.max': 'Höchstens Punkte auf eine Veranstaltung', 'admin.k': 'Zugeteilte Veranstaltungen pro Firmling',
    'admin.saveRules': 'Regeln speichern', 'admin.rulesSaved': 'Regeln gespeichert, Beispieldaten neu erzeugt.',
    'admin.ballots': 'Anmeldungen', 'admin.ballotCount': '{n} Anmeldungen, davon {s} Beispiel-Firmlinge.',
    'admin.sampleCount': 'Anzahl Beispiel-Firmlinge', 'admin.removeReal': 'Echte Anmeldungen ebenfalls löschen',
    'admin.reset': 'Demo zurücksetzen', 'admin.resetDone': 'Demo zurückgesetzt.',
    'admin.demand': 'Nachfrage je Veranstaltung',
    'admin.demandNote': 'Der Balken zeigt Interessierte im Verhältnis zu den Plätzen. Orange heißt überbucht.',
    'admin.col.activity': 'Veranstaltung', 'admin.col.seats': 'Plätze', 'admin.col.interested': 'Interessierte',
    'admin.col.points': 'Punkte gesamt', 'admin.col.avg': 'Ø Punkte', 'admin.col.demand': 'Nachfrage',
    'admin.run': 'Zuteilung berechnen',

    'results.title': 'Ergebnis der Zuteilung',
    'results.lead': '{n} Firmlinge, je {k} Veranstaltungen. Zum Vergleich: dieselben Wünsche nach „Wer zuerst kommt“ (alle klicken zur Öffnung, Reihenfolge zufällig).',
    'results.topChoice': 'bekamen ihre Nr.-1-Veranstaltung', 'results.avg': 'Ø Zufriedenheit (erreichte Punkte)',
    'results.worst': 'Zufriedenheit der schlechtesten 10 %', 'results.fallback': 'Firmlinge mit Ersatzveranstaltung',
    'results.fcfs': 'Wer zuerst kommt:',
    'results.how': 'So wird zugeteilt',
    'results.how1': 'Es wird in {k} Runden verteilt. Jede Runde bekommt jeder Firmling eine Veranstaltung.',
    'results.how2': 'Innerhalb einer Runde hat Vorrang, wer mehr Punkte gesetzt hat. Bei Gleichstand entscheidet das Los, nie die Uhrzeit.',
    'results.how3': 'Ist die Wunschveranstaltung voll oder kollidiert der Termin, kommt die nächste der eigenen Reihung dran. Sonst gibt es eine Ersatzveranstaltung mit freien Plätzen.',
    'results.how4': 'Zum Schluss wird getauscht, wenn zwei Firmlinge dadurch beide nicht schlechter dastehen.',
    'results.load': 'Auslastung', 'results.people': 'Firmlinge', 'results.search': 'Suchen',
    'results.copy': 'Liste kopieren (CSV)', 'results.copied': 'Liste in die Zwischenablage kopiert.',
    'results.copyFallback': 'Text markiert. Mit Strg+C kopieren.',
    'results.col.name': 'Name', 'results.col.assigned': 'Zugeteilt (Punkte)', 'results.col.satisfaction': 'Zufriedenheit',
    'results.real': 'echt', 'results.fallbackChip': 'Ersatz', 'results.showAll': 'Alle {n} anzeigen', 'results.flexible': 'flexibel',

    'error.nameRequired': 'Bitte gib deinen Namen an.',
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
    'error.perParticipantTooLow': 'Pro Firmling muss mindestens 1 Veranstaltung zugeteilt werden.',
    'error.unauthorized': 'Der Zugangsschlüssel ist falsch.',
    'error.generic': 'Das hat nicht geklappt. Bitte erneut versuchen.',
  },
  en: {
    'app.name': 'UrFairSlot',
    'app.madeFor': 'Made for the parish Pfarre Urfahr St. Junia · open source (MIT)',
    'app.parish': 'Confirmation preparation Urfahr 2026/27',
    'app.demo': 'Test version. All registrations except your own are invented sample data.',
    'nav.vote': 'Vote', 'nav.admin': 'Organisation', 'nav.results': 'Allocation',
    'lang.switch': 'Deutsch',
    'common.loading': 'Loading …',
    'common.loadError': 'The data could not be loaded. Please try again later.',
    'common.unlimited': 'open', 'common.seats': '{n} seats', 'common.only.m': 'boys only', 'common.only.w': 'girls only',
    'common.flexibleUntil': 'until {date}, by arrangement',

    'vote.title': 'Choose your activities',
    'vote.lead': 'You have {budget} points. Spread them over at least {min} activities, at most {max} on one. More points mean it matters more to you. You will be assigned {k} activities after registration closes.',
    'vote.pickSessions': 'Tick the dates you can make:',
    'vote.remaining': 'Points left', 'vote.of': 'of {n}',
    'vote.check.budget': 'All {n} points spent', 'vote.check.min': 'At least {n} activities ({count})',
    'vote.check.sessions': 'Every chosen activity has a date that works',
    'vote.ranking': 'Your ranking', 'vote.rankingEmpty': 'No points given yet.',
    'vote.name': 'Your name', 'vote.namePlaceholder': 'First name Last name',
    'vote.submit': 'Submit vote', 'vote.example': 'Fill in an example',
    'vote.note': 'You can change your vote until registration closes. When you submit makes no difference.',
    'vote.saved': 'Your vote has been saved.', 'vote.replaced': 'Your vote has been updated.',
    'vote.seeResult': 'See allocation', 'vote.less': '5 points less for {title}', 'vote.more': '5 points more for {title}',

    'admin.title': 'Rules and demand',
    'admin.lead': 'The confirmation team sets the rules here and sees how popular each activity is.',
    'admin.key': 'Organisation access key', 'admin.keyNeeded': 'An access key is required for this area.',
    'admin.rules': 'Rules', 'admin.budget': 'Points per participant', 'admin.min': 'Minimum activities with points',
    'admin.max': 'Maximum points on one activity', 'admin.k': 'Activities assigned per participant',
    'admin.saveRules': 'Save rules', 'admin.rulesSaved': 'Rules saved, sample data regenerated.',
    'admin.ballots': 'Registrations', 'admin.ballotCount': '{n} registrations, {s} of them sample participants.',
    'admin.sampleCount': 'Number of sample participants', 'admin.removeReal': 'Also delete real registrations',
    'admin.reset': 'Reset demo', 'admin.resetDone': 'Demo reset.',
    'admin.demand': 'Demand per activity',
    'admin.demandNote': 'The bar shows interested participants relative to seats. Orange means overbooked.',
    'admin.col.activity': 'Activity', 'admin.col.seats': 'Seats', 'admin.col.interested': 'Interested',
    'admin.col.points': 'Total points', 'admin.col.avg': 'Avg points', 'admin.col.demand': 'Demand',
    'admin.run': 'Run allocation',

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

    'error.nameRequired': 'Please enter your name.',
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

  percent(x: number): string { return Math.round(x * 100) + ' %'; }
}
