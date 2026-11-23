import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Account, Api } from './api';
import { AppInfo } from './info';
import { I18n } from './i18n';
import { SessionService } from './session';

/** Demo start page: enter as participant (with a name) or as organiser. */
@Component({
  selector: 'app-start',
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="head">
      <h1>{{ i18n.t('start.title') }}</h1>
      <p>{{ i18n.t('start.lead') }}</p>
    </div>
    <div class="tiles">
      <form class="card tile stack" (submit)="$event.preventDefault(); joinAsParticipant()">
        <h2>{{ i18n.t('start.participant') }}</h2>
        <p class="muted">{{ i18n.t(info.demo() ? 'start.participantText' : 'start.linkOnly') }}</p>
        @if (info.demo()) {
        <div class="field">
          <label for="p-name">{{ i18n.t('vote.name') }}</label>
          <input id="p-name" autocomplete="name" [placeholder]="i18n.t('vote.namePlaceholder')" [(ngModel)]="name" name="name" />
        </div>
        <button class="btn" type="submit" [disabled]="!name.trim()">{{ i18n.t('start.participantGo') }}</button>
        }
        @if (info.demo()) {
        <div class="demo-people">
          <p class="small muted">{{ i18n.t('start.demoPeople') }}</p>
          @for (p of people; track p.key) {
            <button type="button" class="person" (click)="name = p.name; joinAsParticipant()">
              <b>{{ p.key }}</b> {{ p.name }}<span class="muted small">{{ i18n.t('start.demo.' + p.key) }}</span>
            </button>
            <details class="try small">
              <summary>{{ i18n.t('start.tryTitle') }}</summary>
              <ul>
                @for (i of p.tries; track i) { <li>{{ i18n.t('start.try.' + p.key + i) }}</li> }
              </ul>
            </details>
          }
          <p class="small muted">{{ i18n.t('start.tryMail') }}</p>
        </div>
        }
      </form>

      <div class="card tile stack">
        <h2>{{ i18n.t('start.admin') }}</h2>
        <p class="muted">{{ i18n.t('start.adminText') }}</p>
        @if (info.demo()) {
          <div class="demo-people">
            <p class="small muted">{{ i18n.t('start.demoAccounts') }}</p>
            @for (d of demoAccounts; track d.key) {
              <button type="button" class="person" [disabled]="busy()" (click)="demoLogin(d.key)">
                <b>{{ d.badge }}</b> {{ i18n.t('start.demoAccount.' + d.key) }}<span class="muted small">{{ i18n.t('start.demoAccountText.' + d.key) }}</span>
              </button>
            }
          </div>
        } @else if (setupNeeded()) {
          <form class="stack" (submit)="$event.preventDefault(); setup()">
            <p class="small">{{ i18n.t('start.setupText') }}</p>
            <div class="field"><label for="s-key">{{ i18n.t('admin.key') }}</label><input id="s-key" name="key" type="password" [(ngModel)]="key" /></div>
            <div class="field"><label for="s-name">{{ i18n.t('accounts.name') }}</label><input id="s-name" name="name" autocomplete="name" [(ngModel)]="accountName" /></div>
            <div class="field"><label for="s-mail">{{ i18n.t('participants.email') }}</label><input id="s-mail" name="email" type="email" autocomplete="username" [(ngModel)]="email" /></div>
            <div class="field"><label for="s-pw">{{ i18n.t('accounts.newPassword') }}</label><input id="s-pw" name="password" type="password" autocomplete="new-password" [(ngModel)]="password" /></div>
            <button class="btn" type="submit" [disabled]="busy()">{{ i18n.t('start.setupGo') }}</button>
          </form>
        } @else {
          <form class="stack" (submit)="$event.preventDefault(); login()">
            <div class="field"><label for="a-mail">{{ i18n.t('participants.email') }}</label><input id="a-mail" name="email" type="email" autocomplete="username" [(ngModel)]="email" /></div>
            <div class="field"><label for="a-pw">{{ i18n.t('accounts.password') }}</label><input id="a-pw" name="password" type="password" autocomplete="current-password" [(ngModel)]="password" /></div>
            <button class="btn" type="submit" [disabled]="busy() || !email || !password">{{ i18n.t('start.adminGo') }}</button>
            <p class="small muted">{{ i18n.t('start.forgot') }}</p>
          </form>
          @if (showRecover()) {
            <form class="stack recover" (submit)="$event.preventDefault(); recover()">
              <p class="small">{{ i18n.t('start.recoverText') }}</p>
              <div class="field"><label for="r-key">{{ i18n.t('admin.key') }}</label><input id="r-key" name="rkey" type="password" autocomplete="off" [(ngModel)]="key" /></div>
              <div class="field"><label for="r-mail">{{ i18n.t('start.recoverEmail') }}</label><input id="r-mail" name="rmail" type="email" [(ngModel)]="recoverEmail" /></div>
              <button class="btn ghost" type="submit" [disabled]="busy() || !key || !recoverEmail">{{ i18n.t('start.recoverGo') }}</button>
            </form>
          } @else {
            <button class="linkish small" type="button" (click)="showRecover.set(true)">{{ i18n.t('start.recoverOpen') }}</button>
          }
        }
        @if (error()) { <ul class="errors"><li>{{ error() }}</li></ul> }
      </div>

      <div class="card tile stack">
        <h2>{{ i18n.t('start.plan') }}</h2>
        <p class="muted">{{ i18n.t('start.planText') }}</p>
        <a class="btn ghost" routerLink="/plan">{{ i18n.t('start.planGo') }}</a>
      </div>
    </div>
  `,
  styles: `
    .tiles { display: grid; grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 1fr)); gap: 16px }
    .tile { align-content: start }
    .tile .btn { justify-self: start }
    .demo-people { display: grid; gap: 6px; border-top: 1px solid var(--line); padding-top: 12px }
    .person { display: grid; grid-template-columns: 22px 1fr; gap: 0 8px; text-align: left; padding: 8px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--bg); cursor: pointer }
    .person:hover { border-color: var(--accent) }
    .linkish { justify-self: start; background: none; border: 0; padding: 0; color: var(--accent); cursor: pointer; text-decoration: underline }
    .recover { border-top: 1px solid var(--line); padding-top: 12px }
    .person b { grid-row: span 2; color: var(--accent) }
    .try { margin: -2px 0 4px 30px }
    .try summary { cursor: pointer; color: var(--accent) }
    .try ul { margin: 6px 0 0; padding-left: 18px; display: grid; gap: 4px }
    .demo-people > p { margin: 0 }
  `,
})
export class StartPage {
  protected i18n = inject(I18n);
  protected info = inject(AppInfo);
  private api = inject(Api);
  private session = inject(SessionService);
  private router = inject(Router);

  protected name = this.session.participantName() ?? '';
  /** Demo people, matching the seeded participant lists on the server (DemoData.cs); tries = numbered texts start.try.{key}{n}. */
  protected readonly people = [
    { key: 'A', name: 'Anna Ammer', tries: [1, 2] }, { key: 'B', name: 'Ben Berger', tries: [1, 2, 3, 4] },
    { key: 'C', name: 'Clara Czerny', tries: [1, 2, 3, 4, 5, 6] }, { key: 'D', name: 'David Doppler', tries: [1, 2, 3, 4, 5] },
  ];
  protected key = '';
  protected email = '';
  protected password = '';
  protected accountName = '';
  protected setupNeeded = signal(false);
  protected showRecover = signal(false);
  protected recoverEmail = '';
  protected readonly demoAccounts = [{ key: 'junia', badge: 'J' }, { key: 'markus', badge: 'M' }, { key: 'geist', badge: 'G' }];

  constructor() { this.api.currentAccount().then(r => this.setupNeeded.set(r.setupNeeded), () => { /* ignore */ }); }
  protected error = signal<string | null>(null);
  protected busy = signal(false);

  protected joinAsParticipant(): void {
    if (!this.name.trim()) return;
    this.api.setParticipantLink(null); // demo: sign in by name
    this.session.signIn({ kind: 'participant', name: this.name.trim() });
    this.router.navigate(['/slots']);
  }

  /** Emergency access with Admin__Key: opens a fresh one-time link for an existing Junia admin. */
  protected async recover(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const { setupToken } = await this.api.recover(this.key, this.recoverEmail.trim());
      await this.router.navigate(['/account', setupToken]);
    } catch (e) {
      this.error.set(this.i18n.errors(e)[0]);
    } finally {
      this.busy.set(false);
      this.key = '';
    }
  }

  protected async demoLogin(key: string): Promise<void> { await this.signInWith(() => this.api.demoLogin(key)); }
  protected async login(): Promise<void> { await this.signInWith(() => this.api.login(this.email.trim(), this.password)); }
  protected async setup(): Promise<void> {
    await this.signInWith(() => this.api.setup({ key: this.key, email: this.email.trim(), name: this.accountName.trim(), password: this.password }));
  }

  private async signInWith(action: () => Promise<Account>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const account = await action();
      this.session.signOut();
      this.session.signIn({ kind: 'admin', account });
      await this.router.navigate(['/admin']);
    } catch (e) {
      this.error.set(this.i18n.errors(e)[0]);
    } finally {
      this.busy.set(false);
      this.password = '';
    }
  }
}
