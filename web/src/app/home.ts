import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, SlotSummary } from './api';
import { I18n } from './i18n';

/** Start page: lists the FairSlots that currently accept votes. */
@Component({
  selector: 'app-home',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="head">
      <h1>{{ i18n.t('home.title') }}</h1>
      <p>{{ i18n.t('home.lead') }}</p>
    </div>
    @if (slots(); as list) {
      @if (list.length) {
        <div class="slots">
          @for (s of list; track s.id) {
            <a class="card slot" [routerLink]="['/s', s.id]">
              <h2>{{ s.name }}</h2>
              <p class="muted small">
                {{ i18n.t('home.activities', { n: s.activityCount }) }}
                @if (s.registrationDeadline) { · {{ i18n.t('common.deadline', { date: i18n.date(s.registrationDeadline) }) }} }
              </p>
              <span class="btn">{{ i18n.t('home.go') }}</span>
            </a>
          }
        </div>
      } @else {
        <p class="muted">{{ i18n.t('home.empty') }}</p>
      }
    } @else if (error()) {
      <p class="muted">{{ i18n.t('common.loadError') }}</p>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    .slots { display: grid; grid-template-columns: repeat(auto-fill, minmax(min(100%, 320px), 1fr)); gap: 12px }
    .slot { display: grid; gap: 10px; text-decoration: none; color: var(--ink) }
    .slot:hover { border-color: var(--accent) }
    .slot .btn { justify-self: start }
  `,
})
export class HomePage {
  protected i18n = inject(I18n);
  private api = inject(Api);
  protected slots = signal<SlotSummary[] | null>(null);
  protected error = signal(false);

  constructor() {
    this.api.openSlots().then(s => this.slots.set(s), () => this.error.set(true));
  }
}
