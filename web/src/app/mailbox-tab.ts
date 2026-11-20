import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Api, Mail } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

/**
 * Invitations prepared by the app. Demo: a mailbox showing what would be sent. Real use (until e-mail sending exists):
 * organisers copy each text into their own mail program.
 */
@Component({
  selector: 'app-mailbox',
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="stack">
      <div class="card stack">
        <p class="muted small">{{ i18n.t(demo() ? 'mail.leadDemo' : 'mail.lead') }}</p>
        <div><button class="btn" type="button" [disabled]="busy()" (click)="create()">{{ i18n.t('mail.create') }}</button></div>
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>
      @for (m of mails(); track m.id) {
        <article class="card mail">
          <div class="row between">
            <div>
              <div class="small muted">{{ i18n.t('mail.to') }} {{ m.to }} · {{ i18n.date(m.createdAt) }}</div>
              <h3>{{ m.subject }}</h3>
            </div>
            <button class="btn ghost small" type="button" (click)="copy(m)">{{ i18n.t('mail.copy') }}</button>
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
  `,
})
export class MailboxTab implements OnInit {
  readonly slotId = input.required<string>();
  readonly demo = input(false);

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

  protected async create(): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const { created } = await this.api.createInvitations(this.slotId());
      this.toast.show(this.i18n.t('mail.created', { n: created }));
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
