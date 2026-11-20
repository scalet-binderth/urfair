import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api } from './api';
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
        <p class="muted">{{ i18n.t('start.participantText') }}</p>
        <div class="field">
          <label for="p-name">{{ i18n.t('vote.name') }}</label>
          <input id="p-name" autocomplete="name" [placeholder]="i18n.t('vote.namePlaceholder')" [(ngModel)]="name" name="name" />
        </div>
        <button class="btn" type="submit" [disabled]="!name.trim()">{{ i18n.t('start.participantGo') }}</button>
        @if (info.demo()) {
        <div class="demo-people">
          <p class="small muted">{{ i18n.t('start.demoPeople') }}</p>
          @for (p of people; track p.key) {
            <button type="button" class="person" (click)="name = p.name; joinAsParticipant()">
              <b>{{ p.key }}</b> {{ p.name }}<span class="muted small">{{ i18n.t('start.demo.' + p.key) }}</span>
            </button>
          }
        </div>
        }
      </form>

      <form class="card tile stack" (submit)="$event.preventDefault(); joinAsAdmin()">
        <h2>{{ i18n.t('start.admin') }}</h2>
        <p class="muted">{{ i18n.t('start.adminText') }}</p>
        @if (info.adminProtected()) {
          <div class="field">
            <label for="a-key">{{ i18n.t('admin.key') }}</label>
            <input id="a-key" type="password" autocomplete="current-password" [(ngModel)]="key" name="key" />
          </div>
        } @else {
          <p class="small muted">{{ i18n.t('start.adminDemo') }}</p>
        }
        @if (error()) { <ul class="errors"><li>{{ error() }}</li></ul> }
        <button class="btn" type="submit" [disabled]="busy()">{{ i18n.t('start.adminGo') }}</button>
      </form>

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
    .person b { grid-row: span 2; color: var(--accent) }
  `,
})
export class StartPage {
  protected i18n = inject(I18n);
  protected info = inject(AppInfo);
  private api = inject(Api);
  private session = inject(SessionService);
  private router = inject(Router);

  protected name = this.session.participantName() ?? '';
  /** Demo people, matching the seeded participant lists on the server (DemoData.cs). */
  protected readonly people = [
    { key: 'A', name: 'Anna Ammer' }, { key: 'B', name: 'Ben Berger' },
    { key: 'C', name: 'Clara Czerny' }, { key: 'D', name: 'David Doppler' },
  ];
  protected key = '';
  protected error = signal<string | null>(null);
  protected busy = signal(false);

  protected joinAsParticipant(): void {
    if (!this.name.trim()) return;
    this.session.signIn({ kind: 'participant', name: this.name.trim() });
    this.router.navigate(['/slots']);
  }

  protected async joinAsAdmin(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.api.setAdminKey(this.key);
    try {
      await this.api.adminSlots(); // fails with 401 if the key is wrong
      this.session.signIn({ kind: 'admin' });
      await this.router.navigate(['/admin']);
    } catch {
      this.api.setAdminKey('');
      this.error.set(this.i18n.t('error.unauthorized'));
    } finally {
      this.busy.set(false);
    }
  }
}
