import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { Api, Mail, Mailbox, MailboxRole, Phase } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

/**
 * Mails prepared by the app. Demo: a mailbox showing what would be sent, as a whole or per person (organisers, leaders,
 * participants) with inbox and sent. With a mail server set up the app sends them and shows the status of each; otherwise
 * organisers copy each text into their own mail program.
 */
@Component({
  selector: 'app-mailbox',
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="stack">
      <div class="card stack">
        <p class="muted small">{{ i18n.t(demo() ? 'mail.leadDemo' : sending() ? 'mail.leadSending' : 'mail.lead') }}</p>
        <div><button class="btn" type="button" [disabled]="busy()" (click)="create()">{{ i18n.t('mail.create') }}</button></div>
        @if (phase() === 'Open') {
          <p class="muted small">{{ i18n.t('mail.remindHint') }}</p>
          <div><button class="btn ghost" type="button" [disabled]="busy()" (click)="remind()">{{ i18n.t('mail.remind') }}</button></div>
        }
        @if (phase() === 'Published') {
          <p class="muted small">{{ i18n.t('mail.resultHint') }}</p>
          <div><button class="btn ghost" type="button" [disabled]="busy()" (click)="results()">{{ i18n.t('mail.result') }}</button></div>
        }
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>
      @if (demo() && boxes().length) {
        <div class="card stack">
          <label class="field">
            <span>{{ i18n.t('mail.box') }}</span>
            <select (change)="choose($any($event.target).value)">
              <option value="" [selected]="!box()">{{ i18n.t('mail.box.all', { n: mails().length }) }}</option>
              @for (g of groups(); track g.role) {
                <optgroup [label]="i18n.t('mail.role.' + g.role)">
                  @for (b of g.boxes; track b.address) { <option [value]="b.address" [selected]="b.address === box()">{{ label(b) }}</option> }
                </optgroup>
              }
            </select>
          </label>
          @if (box()) {
            <div class="row">
              <button class="btn small" type="button" [class.ghost]="folder() !== 'inbox'" (click)="folder.set('inbox')">{{ i18n.t('mail.inbox', { n: inbox().length }) }}</button>
              <button class="btn small" type="button" [class.ghost]="folder() !== 'sent'" (click)="folder.set('sent')">{{ i18n.t('mail.sent', { n: sent().length }) }}</button>
            </div>
          }
          <p class="muted small">{{ i18n.t('mail.boxHint') }}</p>
        </div>
      }
      @for (m of shown(); track m.id) {
        <article class="card mail">
          <div class="row between">
            <div>
              @if (demo()) {
                <div class="small muted">
                  {{ i18n.t('mail.from') }} <span [title]="m.from ?? ''">{{ who(m.from) }}</span> ·
                  {{ i18n.t('mail.to') }} <span [title]="m.to">{{ who(m.to) }}</span> · {{ i18n.date(m.createdAt) }}
                </div>
              } @else {
                <div class="small muted">{{ i18n.t('mail.to') }} {{ m.to }} · {{ i18n.date(m.createdAt) }}</div>
              }
              <h3>{{ m.subject }}</h3>
              @if (m.status !== 'manual') {
                <div class="small" [class.bad]="m.status === 'failed'" [class.good]="m.status === 'sent'">
                  {{ i18n.t('mail.status.' + m.status, { at: m.sentAt ? i18n.date(m.sentAt) : '' }) }}
                  @if (m.error) { <span class="muted">· {{ m.error }}</span> }
                </div>
              }
            </div>
            <div class="row">
              @if (m.status === 'failed' || (m.status === 'pending' && m.error)) {
                <button class="btn ghost small" type="button" [disabled]="busy()" (click)="retry(m)">{{ i18n.t('mail.retry') }}</button>
              }
              <button class="btn ghost small" type="button" (click)="copy(m)">{{ i18n.t('mail.copy') }}</button>
            </div>
          </div>
          <pre>{{ m.body }}</pre>
          @if (demo() && linkOf(m); as link) { <a class="small" [href]="link">{{ i18n.t('mail.openLink') }}</a> }
        </article>
      } @empty {
        <p class="muted">{{ i18n.t(box() ? 'mail.boxEmpty' : 'mail.empty') }}</p>
      }
    </div>
  `,
  styles: `
    .between { justify-content: space-between; align-items: start }
    pre { white-space: pre-wrap; font: inherit; font-size: 14px; margin: 8px 0 0; overflow-wrap: anywhere }
    .btn.small { padding: 6px 12px; font-size: 14px }
    .bad { color: var(--accent) }
    .good { color: var(--good) }
  `,
})
export class MailboxTab implements OnInit {
  readonly slotId = input.required<string>();
  readonly demo = input(false);
  /** The app sends the mails itself. */
  readonly sending = input(false);
  /** Reminders while registration is open, result mails while the allocation is visible. */
  readonly phase = input<Phase>('Draft');

  protected i18n = inject(I18n);
  private api = inject(Api);
  private toast = inject(Toast);
  protected mails = signal<Mail[]>([]);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  /** Demo: everyone mails go to or come from, the chosen one ('' = all mails) and their folder. */
  protected boxes = signal<Mailbox[]>([]);
  protected box = signal('');
  protected folder = signal<'inbox' | 'sent'>('inbox');
  protected inbox = computed(() => this.mails().filter(m => same(m.to, this.box())));
  protected sent = computed(() => this.mails().filter(m => same(m.from, this.box())));
  protected shown = computed(() => !this.box() ? this.mails() : this.folder() === 'inbox' ? this.inbox() : this.sent());
  protected groups = computed(() => (['organiser', 'leader', 'participant', 'other'] as MailboxRole[])
    .map(role => ({ role, boxes: this.boxes().filter(b => b.roles[0] === role) })).filter(g => g.boxes.length));

  ngOnInit(): void { this.load(); }

  private async load(): Promise<void> {
    try {
      this.mails.set(await this.api.mails(this.slotId()));
      if (this.demo()) this.boxes.set(await this.api.mailboxes(this.slotId()));
    } catch (e) { this.errors.set(this.i18n.errors(e)); }
  }

  protected choose(address: string): void {
    this.box.set(address);
    this.folder.set('inbox');
  }

  /** Name and further roles, e.g. „Miriam Koller (Organisation, Kursleitung)“. */
  protected label(b: Mailbox): string {
    return b.roles.length > 1 ? `${b.name} (${b.roles.map(r => this.i18n.t('mail.role.' + r)).join(', ')})` : b.name;
  }

  /** The name behind an address; null is the app itself. */
  protected who(address: string | null): string {
    if (!address) return this.i18n.t('mail.app');
    return this.boxes().find(b => same(b.address, address))?.name ?? address;
  }

  protected create(): Promise<void> {
    return this.prepare(() => this.api.createInvitations(this.slotId()), 'mail.created');
  }

  protected remind(): Promise<void> {
    return this.prepare(() => this.api.createReminders(this.slotId()), 'mail.reminded');
  }

  protected results(): Promise<void> {
    return this.prepare(() => this.api.createResultMails(this.slotId()), 'mail.resulted');
  }

  protected async retry(m: Mail): Promise<void> {
    this.busy.set(true);
    try {
      await this.api.retryMail(this.slotId(), m.id);
      this.toast.show(this.i18n.t('mail.retried'));
      await this.load();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async prepare(call: () => Promise<{ created: number }>, message: string): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const { created } = await call();
      this.toast.show(this.i18n.t(message, { n: created }));
      await this.load();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }

  /** The personal link inside an invitation, opened in this app (demo). */
  protected linkOf(m: Mail): string | null {
    const match = m.body.match(/\/p\/[A-Za-z0-9_-]+/);
    return match ? match[0] : null;
  }

  protected async copy(m: Mail): Promise<void> {
    try {
      await navigator.clipboard.writeText(`${m.subject}\n\n${m.body}`);
      this.toast.show(this.i18n.t('mail.copied'));
    } catch { /* clipboard blocked */ }
  }
}

function same(a: string | null, b: string): boolean {
  return !!a && a.toLowerCase() === b.toLowerCase();
}
