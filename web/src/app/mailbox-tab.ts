import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Api, Mail, Phase } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

/**
 * Mails prepared by the app. Demo: a mailbox showing what would be sent. With a mail server set up the app sends them and
 * shows the status of each; otherwise organisers copy each text into their own mail program.
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
      @for (m of mails(); track m.id) {
        <article class="card mail">
          <div class="row between">
            <div>
              <div class="small muted">{{ i18n.t('mail.to') }} {{ m.to }} · {{ i18n.date(m.createdAt) }}</div>
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
        <p class="muted">{{ i18n.t('mail.empty') }}</p>
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

  ngOnInit(): void { this.load(); }

  private async load(): Promise<void> {
    try { this.mails.set(await this.api.mails(this.slotId())); } catch (e) { this.errors.set(this.i18n.errors(e)); }
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
