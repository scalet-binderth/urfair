import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, SlotSummary } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';

/** Participant overview: the FairSlots this person is listed in, open ones first. */
@Component({
  selector: 'app-home',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h1>{{ i18n.t('home.hello', { name: session.participantName() ?? '' }) }}</h1>
      <p>{{ i18n.t('home.lead') }}</p>
    </div>
    @if (slots(); as list) {
      @if (!list.length) {
        <p class="card">{{ i18n.t('home.empty') }}</p>
      }
      @if (open().length) {
        <h2 class="section">{{ i18n.t('home.open') }}</h2>
        <div class="slots">
          @for (s of open(); track s.id) {
            <a class="card slot" [routerLink]="['/s', s.id]">
              <h3>{{ s.name }}</h3>
              <p class="muted small">
                {{ i18n.t('home.activities', { n: s.activityCount }) }}
                @if (s.registrationDeadline) { · {{ i18n.t('common.deadline', { date: i18n.date(s.registrationDeadline) }) }} }
              </p>
              <span class="btn">{{ i18n.t('home.go') }}</span>
            </a>
          }
        </div>
      }
      @if (done().length) {
        <h2 class="section">{{ i18n.t('home.done') }}</h2>
        <div class="slots">
          @for (s of done(); track s.id) {
            <div class="card slot">
              <h3>{{ s.name }} <span class="phase" [attr.data-phase]="s.phase">{{ i18n.t('home.phase.' + s.phase) }}</span></h3>
              @if (s.phase === 'Allocation') {
                <p class="muted small">{{ i18n.t('home.closed') }}</p>
              } @else {
                <p class="muted small">{{ i18n.t(s.phase === 'Ended' ? 'home.ended' : 'home.published') }}</p>
                @if (s.phase === 'Published' || s.phase === 'Closed') {
                  <a class="btn ghost" [routerLink]="['/s', s.id]">{{ i18n.t(s.phase === 'Closed' ? 'home.seeStatusClosed' : 'home.seeStatus') }}</a>
                } @else {
                  <a class="btn ghost" [routerLink]="['/s', s.id, 'result']">{{ i18n.t('home.seeResult') }}</a>
                }
              }
            </div>
          }
        </div>
      }
    } @else if (error()) {
      <p class="muted">{{ i18n.t('common.loadError') }}</p>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    .section { margin: 18px 0 10px }
    .slots { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 320px), 1fr)); gap: 12px }
    .slot { display: grid; gap: 10px; align-content: start; text-decoration: none; color: var(--ink) }
    a.slot:hover { border-color: var(--accent) }
    .slot .btn { justify-self: start }
    .slot .phase { margin-left: 6px; vertical-align: middle }
  `,
})
export class HomePage {
  protected i18n = inject(I18n);
  protected session = inject(SessionService);
  private api = inject(Api);
  protected slots = signal<SlotSummary[] | null>(null);
  protected error = signal(false);
  protected open = computed(() => (this.slots() ?? []).filter(s => s.phase === 'Open'));
  protected done = computed(() => (this.slots() ?? []).filter(s => s.phase !== 'Open'));

  constructor() {
    this.api.mySlots(this.session.participantName() ?? '').then(s => this.slots.set(s), () => this.error.set(true));
  }
}
