import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api, Assignment, Slot } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';

/** A participant's own allocation after the organisers published it. */
@Component({
  selector: 'app-my-result',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <p><a routerLink="/slots">{{ i18n.t('vote.back') }}</a></p>
    @if (slot(); as s) {
      <div class="head">
        <p class="muted small">{{ s.name }}</p>
        <h1>{{ i18n.t('result.title') }}</h1>
      </div>
      @if (result(); as r) {
        @if (!r.voted) {
          <p class="card">{{ i18n.t('result.noVote') }}</p>
        } @else {
          <p>{{ i18n.t('result.lead', { n: r.assignments.length }) }}</p>
          <div class="list">
            @for (a of r.assignments; track a.sessionId) {
              <div class="card item">
                <h3>{{ a.title }} @if (a.isFallback) { <span class="tag">{{ i18n.t('results.fallbackChip') }}</span> }</h3>
                <p class="muted">{{ i18n.when(a) }} · {{ a.location }}</p>
              </div>
            }
          </div>
        }
      }
    } @else if (error()) {
      <p class="muted">{{ i18n.t('common.loadError') }}</p>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    .list { display: grid; gap: 10px; margin-top: 12px; max-width: 640px }
    .item { display: grid; gap: 4px; border-left: 4px solid var(--good) }
  `,
})
export class MyResultPage implements OnInit {
  readonly slotId = input.required<string>();
  protected i18n = inject(I18n);
  private api = inject(Api);
  private session = inject(SessionService);
  protected slot = signal<Slot | null>(null);
  protected result = signal<{ voted: boolean; assignments: Assignment[] } | null>(null);
  protected error = signal(false);

  async ngOnInit(): Promise<void> {
    try {
      const name = this.session.participantName() ?? '';
      const [slot, result] = await Promise.all([this.api.slot(this.slotId()), this.api.myResult(this.slotId(), name)]);
      this.slot.set(slot);
      this.result.set(result);
    } catch {
      this.error.set(true);
    }
  }
}
