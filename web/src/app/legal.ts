import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from './api';
import { I18n } from './i18n';

interface Block { heading: boolean; text: string }

/**
 * Templates shown while the operator has not set Legal:Imprint / Legal:Privacy. They describe what Urfair really stores;
 * the parts in [brackets] and the legal basis have to be filled in with the data protection officer.
 */
const IMPRINT_TEMPLATE = `## Impressum und Offenlegung
Medieninhaber und Betreiber: [Name der Pfarre]
[Anschrift]
E-Mail: [Kontaktadresse]

## Zweck
Urfair verteilt Plätze bei Veranstaltungen der Pfarre fair nach Punkten (z. B. Projekte der Firmvorbereitung, Dienste beim Pfarrfest).

## Software
Urfair ist freie Software (Open Source, MIT-Lizenz). Quellcode: https://github.com/scalet-binderth/urfair`;

const PRIVACY_TEMPLATE = `## Verantwortlich
[Name der Pfarre]
[Anschrift]
E-Mail: [Kontaktadresse]
Datenschutz-Ansprechperson: [Name, Kontakt]

## Wofür Urfair Daten verarbeitet
Nur um Plätze bei Veranstaltungen der Pfarre fair zu verteilen: Abstimmung, Zuteilung, Warteliste, Nachrücken, Listen für die Leiter:innen der Termine und Nachrichten dazu. Es gibt keine Werbung und keine Auswertung für andere Zwecke.

## Welche Daten
Teilnehmende, von der Organisation eingetragen: Vor- und Nachname, E-Mail-Adresse, Geburtsdatum, auf Wunsch Taufdatum, Pfarrgemeinde.
Bei der Abstimmung: die vergebenen Punkte, Termine, an denen man nicht kann, Wartelisten-Wünsche, zurückgegebene und abgesagte Plätze und die zugeteilten Plätze.
Organisator:innen: Name, E-Mail-Adresse, Pfarrgemeinde und ein Passwort (nur verschlüsselt gespeichert).
Jede Änderung an einer Wahl wird mit Zeit und Name protokolliert, damit Fehler nachvollziehbar bleiben.

## Rechtsgrundlage
[mit der Datenschutz-Ansprechperson festzulegen]

## Wer die Daten sieht
Die Organisator:innen der Wahl, je nach Pfarrgemeinde. Die Leiter:innen einer Veranstaltung bekommen die Liste ihres Termins mit Namen, Geburts- und Taufdatum. Teilnehmende sehen nur ihre eigenen Angaben, nie die Namen anderer.
Die App läuft bei [Hosting-Anbieter, Standort in der EU]; E-Mails gehen über [Mailserver].

## Cookies und fremde Dienste
Nur was für die Anmeldung technisch nötig ist: ein Anmelde-Cookie für Organisator:innen (12 Stunden gültig) und der persönliche Link der Teilnehmenden, der bis zum Schließen des Browserfensters gemerkt wird. Keine Tracker, keine fremden Schriften oder Dienste.

## Wie lange
Eine Wahl wird {days} Tage nach ihrem Ende automatisch mit allen Daten gelöscht: Teilnehmende, Punkte, Plätze, E-Mails und Protokoll. Die Organisator:innen können sie auch früher löschen.

## Deine Rechte
Du kannst Auskunft über deine Daten verlangen, sie berichtigen oder löschen lassen, ihre Verarbeitung einschränken lassen und ihr widersprechen. Wende dich dafür an [Kontaktadresse]. Du kannst dich auch bei der zuständigen Aufsichtsbehörde beschweren: [Aufsichtsbehörde].`;

/** Legal notice and privacy policy (route data kind): the operator's text from the configuration, otherwise a marked template. */
@Component({
  selector: 'app-legal',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/" class="muted small">← {{ i18n.t('legal.back') }}</a>
    <h1>{{ i18n.t(kind() === 'imprint' ? 'legal.imprint' : 'legal.privacy') }}</h1>
    @if (loaded()) {
      @if (i18n.lang() === 'en') { <p class="muted small">{{ i18n.t('legal.germanOnly') }}</p> }
      @if (template()) { <p class="card warn small">{{ i18n.t('legal.template') }}</p> }
      <article class="card stack">
        @for (b of blocks(); track $index) {
          @if (b.heading) { <h2>{{ b.text }}</h2> } @else { <p>{{ b.text }}</p> }
        }
      </article>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    :host { display: grid; gap: 12px }
    h1 { margin: 0 }
    h2 { font-size: 17px; margin: 8px 0 0 }
    p { margin: 0; white-space: pre-line }
  `,
})
export class LegalPage {
  protected i18n = inject(I18n);
  private api = inject(Api);
  readonly kind = input<'imprint' | 'privacy'>('imprint');
  private legal = signal<{ imprint: string | null; privacy: string | null; retentionDays: number } | null>(null);
  protected loaded = computed(() => this.legal() !== null);
  private configured = computed(() => (this.kind() === 'imprint' ? this.legal()?.imprint : this.legal()?.privacy) ?? null);
  protected template = computed(() => this.loaded() && !this.configured());
  /** Blocks are separated by empty lines; a block starting with "## " is a heading. */
  protected blocks = computed<Block[]>(() => {
    const l = this.legal();
    if (!l) return [];
    const text = this.configured() ?? (this.kind() === 'imprint' ? IMPRINT_TEMPLATE : PRIVACY_TEMPLATE).replace('{days}', String(l.retentionDays));
    return text.replace(/\r/g, '').split('\n').reduce<Block[]>((all, line) => {
      const last = all[all.length - 1];
      if (line.startsWith('## ')) all.push({ heading: true, text: line.slice(3).trim() });
      else if (!line.trim()) { if (last && !last.heading && last.text) all.push({ heading: false, text: '' }); }
      else if (last && !last.heading) last.text = last.text ? `${last.text}\n${line}` : line;
      else all.push({ heading: false, text: line });
      return all;
    }, []).filter(b => b.text);
  });

  constructor() {
    this.api.legal().then(l => this.legal.set(l), () => this.legal.set({ imprint: null, privacy: null, retentionDays: 90 }));
  }
}
