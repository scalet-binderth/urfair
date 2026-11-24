import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';

/** Opened from a one-time link /account/{token}: the organiser sets their password and is signed in. */
@Component({
  selector: 'app-set-password',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    @if (info(); as i) {
      <form class="card stack narrow" (submit)="$event.preventDefault(); save()">
        <h1>{{ i18n.t('accounts.setTitle') }}</h1>
        <p>{{ i18n.t('accounts.setLead', { name: i.name, email: i.email }) }}</p>
        <input type="email" name="username" autocomplete="username" [value]="i.email" hidden />
        <div class="field"><label for="pw1">{{ i18n.t('accounts.newPassword') }}</label>
          <input id="pw1" name="pw1" type="password" autocomplete="new-password" [(ngModel)]="password" /></div>
        <div class="field"><label for="pw2">{{ i18n.t('accounts.repeatPassword') }}</label>
          <input id="pw2" name="pw2" type="password" autocomplete="new-password" [(ngModel)]="repeat" /></div>
        @if (error()) { <p class="errors">{{ error() }}</p> }
        <div><button class="btn" type="submit" [disabled]="busy() || !password">{{ i18n.t('accounts.setGo') }}</button></div>
      </form>
    } @else if (notFound()) {
      <div class="card stack narrow"><h1>{{ i18n.t('link.invalid') }}</h1><p>{{ i18n.t('accounts.linkInvalid') }}</p></div>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
})
export class SetPasswordPage implements OnInit {
  /** Bound from the route parameter :token. */
  readonly token = input.required<string>();

  protected i18n = inject(I18n);
  private api = inject(Api);
  private session = inject(SessionService);
  private router = inject(Router);
  protected info = signal<{ name: string; email: string } | null>(null);
  protected notFound = signal(false);
  protected error = signal<string | null>(null);
  protected busy = signal(false);
  protected password = '';
  protected repeat = '';

  async ngOnInit(): Promise<void> {
    try { this.info.set(await this.api.passwordLink(this.token())); } catch { this.notFound.set(true); }
  }

  protected async save(): Promise<void> {
    if (this.password !== this.repeat) { this.error.set(this.i18n.t('accounts.mismatch')); return; }
    this.busy.set(true);
    this.error.set(null);
    try {
      const account = await this.api.setPassword(this.token(), this.password);
      this.session.signOut();
      this.session.signIn({ kind: 'admin', account });
      await this.router.navigate(['/admin']);
    } catch (e) {
      this.error.set(this.i18n.errors(e)[0]);
    } finally {
      this.busy.set(false);
    }
  }
}
