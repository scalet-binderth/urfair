import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AccountInput, Api, ListedAccount, Mail } from './api';
import { I18n } from './i18n';
import { AppInfo } from './info';
import { SessionService } from './session';
import { Toast } from './toast';

/**
 * Junia admins manage organiser accounts. New accounts get a one-time link by mail to set their password; after
 * "forgot password" the Junia admins are told by mail and send a new link from here.
 */
@Component({
  selector: 'app-accounts',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="head">
      <h1>{{ i18n.t('accounts.title') }}</h1>
      <p>{{ i18n.t('accounts.lead') }}</p>
    </div>
    <div class="stack">
      <form class="card stack" (submit)="$event.preventDefault(); save()">
        <h2>{{ i18n.t(editing() ? 'accounts.edit' : 'accounts.new') }}</h2>
        <div class="row">
          <div class="field grow"><label for="a-name">{{ i18n.t('accounts.name') }}</label><input id="a-name" name="name" maxlength="120" [(ngModel)]="form.name" /></div>
          <div class="field grow"><label for="a-mail">{{ i18n.t('participants.email') }}</label>
            <input id="a-mail" name="email" type="email" maxlength="200" [(ngModel)]="form.email" [disabled]="!!editing()" /></div>
        </div>
        <label class="check"><input type="checkbox" name="junia" [(ngModel)]="form.isJuniaAdmin" />{{ i18n.t('accounts.junia') }}</label>
        @if (!form.isJuniaAdmin) {
          <fieldset class="parishes">
            <legend class="small">{{ i18n.t('accounts.parishes') }}</legend>
            <!-- The first entry is the Pfarre Urfahr-St. Junia itself; that is the Junia admin role. -->
            @for (p of info.parishes().slice(1); track p) {
              <label class="check"><input type="checkbox" [checked]="form.parishes.includes(p)" (change)="toggle(p)" />{{ p }}</label>
            }
          </fieldset>
        }
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
        <div class="row">
          <button class="btn" type="submit" [disabled]="busy()">{{ i18n.t(editing() ? 'common.save' : 'accounts.create') }}</button>
          @if (editing()) { <button class="btn ghost" type="button" (click)="reset()">{{ i18n.t('common.cancel') }}</button> }
        </div>
      </form>

      <div class="card stack">
        @if (accounts().length) {
          <div class="tbl-wrap"><table>
            <thead><tr><th>{{ i18n.t('accounts.name') }}</th><th>{{ i18n.t('participants.email') }}</th><th>{{ i18n.t('accounts.role') }}</th><th>{{ i18n.t('accounts.status') }}</th><th></th></tr></thead>
            <tbody>
              @for (a of accounts(); track a.id) {
                <tr [class.asks]="!!a.resetRequestedAt">
                  <td>{{ a.name }}</td>
                  <td class="small">{{ a.email }}</td>
                  <td class="small">{{ a.isJuniaAdmin ? i18n.t('accounts.juniaShort') : a.parishes.join(', ') }}</td>
                  <td class="small">
                    @if (a.resetRequestedAt) { <div class="warn">{{ i18n.t('accounts.resetRequested', { at: i18n.date(a.resetRequestedAt) }) }}</div> }
                    @if (a.setupToken) { <button class="btn ghost small" type="button" (click)="copy(a.setupToken)">{{ i18n.t('accounts.copySetup') }}</button> }
                    @else if (!a.hasPassword) { <span class="muted">{{ i18n.t('accounts.noPassword') }}</span> }
                    @else if (!a.resetRequestedAt) { <span class="phase" data-phase="Open">{{ i18n.t('accounts.active') }}</span> }
                  </td>
                  <td class="num nowrap">
                    @if (confirmDelete() === a.id) {
                      <button class="btn danger small" type="button" [disabled]="busy()" (click)="remove(a.id)">{{ i18n.t('common.delete') }}</button>
                      <button class="btn ghost small" type="button" (click)="confirmDelete.set(null)">{{ i18n.t('common.cancel') }}</button>
                    } @else {
                      <button class="btn ghost small" type="button" (click)="edit(a)">{{ i18n.t('common.edit') }}</button>
                      <button class="btn small" [class.ghost]="!a.resetRequestedAt" type="button" [disabled]="busy()" (click)="newLink(a.id)">{{ i18n.t('accounts.newLink') }}</button>
                      @if (a.id !== session.account()?.id) {
                        <button class="btn ghost small" type="button" (click)="confirmDelete.set(a.id)">{{ i18n.t('common.delete') }}</button>
                      }
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table></div>
        } @else { <p class="muted">{{ i18n.t(info.demo() ? 'accounts.noneDemo' : 'accounts.none') }}</p> }
      </div>

      <div class="card stack">
        <h2>{{ i18n.t('accounts.mails') }}</h2>
        <p class="muted small">{{ i18n.t(info.mailSending() ? 'accounts.mailsSent' : 'accounts.mailsManual') }}</p>
        @for (m of mails(); track m.id) {
          <details class="mail">
            <summary>
              <span class="small muted">{{ i18n.date(m.createdAt) }} · {{ i18n.t('mail.to') }} {{ m.to }}</span> <b>{{ m.subject }}</b>
              @if (m.status !== 'manual') {
                <span class="small" [class.bad]="m.status === 'failed'" [class.good]="m.status === 'sent'"> · {{ i18n.t('mail.status.' + m.status, { at: m.sentAt ? i18n.date(m.sentAt) : '' }) }}</span>
              }
            </summary>
            <pre>{{ m.body }}</pre>
            @if (m.error) { <p class="small muted">{{ m.error }}</p> }
            <div class="row">
              @if (m.status === 'failed' || (m.status === 'pending' && m.error)) {
                <button class="btn ghost small" type="button" [disabled]="busy()" (click)="retry(m)">{{ i18n.t('mail.retry') }}</button>
              }
              <button class="btn ghost small" type="button" (click)="copyMail(m)">{{ i18n.t('mail.copy') }}</button>
            </div>
          </details>
        } @empty { <p class="muted small">{{ i18n.t('accounts.noMails') }}</p> }
      </div>
    </div>
  `,
  styles: `
    .grow { flex: 1 1 240px }
    .parishes { border: 1px solid var(--line); border-radius: var(--r); padding: 8px 12px; display: grid; gap: 4px; margin: 0 }
    .nowrap { white-space: nowrap }
    .warn { font-weight: 500 }
    tr.asks td { background: color-mix(in srgb, var(--warn) 8%, transparent) }
    .mail { border-top: 1px solid var(--line); padding-top: 8px }
    .mail summary { cursor: pointer }
    .mail pre { white-space: pre-wrap; font: inherit; font-size: 14px; margin: 8px 0; overflow-wrap: anywhere }
  `,
})
export class AccountsPage implements OnInit {
  protected i18n = inject(I18n);
  protected info = inject(AppInfo);
  protected session = inject(SessionService);
  private api = inject(Api);
  private toast = inject(Toast);

  protected accounts = signal<ListedAccount[]>([]);
  protected mails = signal<Mail[]>([]);
  protected editing = signal<string | null>(null);
  protected confirmDelete = signal<string | null>(null);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected form: AccountInput = { email: '', name: '', isJuniaAdmin: false, parishes: [] };

  ngOnInit(): void { this.load(); }

  private async load(): Promise<void> {
    try {
      const [accounts, mails] = await Promise.all([this.api.accounts(), this.api.accountMails()]);
      this.accounts.set(accounts);
      this.mails.set(mails);
    } catch (e) { this.errors.set(this.i18n.errors(e)); }
  }

  protected toggle(p: string): void {
    this.form.parishes = this.form.parishes.includes(p) ? this.form.parishes.filter(x => x !== p) : [...this.form.parishes, p];
  }

  protected edit(a: ListedAccount): void {
    this.editing.set(a.id);
    this.form = { email: a.email, name: a.name, isJuniaAdmin: a.isJuniaAdmin, parishes: [...a.parishes] };
  }

  protected reset(): void {
    this.editing.set(null);
    this.form = { email: '', name: '', isJuniaAdmin: false, parishes: [] };
    this.errors.set([]);
  }

  protected async save(): Promise<void> {
    await this.run(async () => {
      const id = this.editing();
      if (id) {
        await this.api.updateAccount(id, this.form);
        this.toast.show(this.i18n.t('common.saved'));
      } else {
        const res = await this.api.createAccount(this.form);
        await this.handOver(res.setupToken);
      }
      this.reset();
      await this.load();
    });
  }

  protected async newLink(id: string): Promise<void> {
    await this.run(async () => {
      const res = await this.api.newSetupLink(id);
      await this.handOver(res.setupToken);
      await this.load();
    });
  }

  protected async retry(m: Mail): Promise<void> {
    await this.run(async () => {
      await this.api.retryAccountMail(m.id);
      this.toast.show(this.i18n.t('mail.retried'));
      await this.load();
    });
  }

  protected copyMail(m: Mail): Promise<void> {
    return this.toast.copy(`${m.subject}\n\n${m.body}`, this.i18n.t('mail.copied'));
  }

  /** The link went into a mail: the app sends it, otherwise it is copied so the admin can pass it on. */
  private async handOver(token: string): Promise<void> {
    if (this.info.mailSending()) this.toast.show(this.i18n.t('accounts.linkMailed'));
    else await this.copy(token);
  }

  protected async remove(id: string): Promise<void> {
    await this.run(async () => {
      await this.api.deleteAccount(id);
      this.confirmDelete.set(null);
      await this.load();
    });
  }

  /** Copies the one-time link to set the password; the organiser passes it on personally. */
  protected copy(token: string): Promise<void> {
    return this.toast.copy(`${location.origin}/account/${token}`, this.i18n.t('accounts.linkCopied'));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try { await action(); } catch (e) { this.errors.set(this.i18n.errors(e)); } finally { this.busy.set(false); }
  }
}
