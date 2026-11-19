import { Component, computed, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Activity, Api, ApiFailure, Config } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

@Component({
  selector: 'app-vote',
  imports: [FormsModule, RouterLink],
  template: `
    @if (config(); as c) {
      <div class="head">
        <h1>{{ i18n.t('vote.title') }}</h1>
        <p>
          {{
            i18n.t('vote.lead', {
              budget: c.rules.pointBudget,
              min: c.rules.minActivities,
              max: c.rules.maxPerActivity,
              k: c.rules.perParticipant,
            })
          }}
        </p>
      </div>
      <div class="mini">
        <span class="small">{{ remaining() }} · {{ chosenCount() }}</span>
        <div class="meter"><i [style.width.%]="spentPercent()"></i></div>
      </div>
      <div class="layout">
        <div class="list">
          @for (a of activities(); track a.id) {
            <div class="act" [class.on]="pointsFor(a.id) > 0">
              <div class="info">
                <h3>
                  {{ a.title }}
                  @if (a.restrictedTo) {
                    <span class="tag">{{ i18n.t('common.only.' + a.restrictedTo) }}</span>
                  }
                  @if (a.seats === null) {
                    <span class="tag">{{ i18n.t('common.unlimited') }}</span>
                  }
                </h3>
                @if (a.sessions.length > 1) {
                  <div class="muted small">{{ i18n.t('vote.pickSessions') }}</div>
                }
                <ul>
                  @for (s of a.sessions; track s.id) {
                    <li>
                      @if (a.sessions.length > 1) {
                        <label [class.off]="unavailable().has(s.id)">
                          <input
                            type="checkbox"
                            [checked]="!unavailable().has(s.id)"
                            (change)="toggleSession(s.id)"
                          />
                          <span>{{ sessionText(s) }}</span>
                        </label>
                      } @else {
                        {{ sessionText(s) }}
                      }
                    </li>
                  }
                </ul>
              </div>
              <div class="stepper">
                <button
                  type="button"
                  [disabled]="pointsFor(a.id) <= 0"
                  (click)="change(a.id, -1)"
                  [attr.aria-label]="i18n.t('vote.less', { title: a.title })"
                >
                  −
                </button>
                <output>{{ pointsFor(a.id) }}</output>
                <button
                  type="button"
                  [disabled]="pointsFor(a.id) >= c.rules.maxPerActivity || left() <= 0"
                  (click)="change(a.id, 1)"
                  [attr.aria-label]="i18n.t('vote.more', { title: a.title })"
                >
                  +
                </button>
              </div>
            </div>
          }
        </div>
        <aside class="side">
          <div class="card stack">
            <div>
              <div class="muted small">{{ i18n.t('vote.remaining') }}</div>
              <div class="big">
                {{ left() }}
                <span class="muted of">{{ i18n.t('vote.of', { n: c.rules.pointBudget }) }}</span>
              </div>
            </div>
            <div class="meter"><i [style.width.%]="spentPercent()"></i></div>
            <ul class="checks">
              <li [class.ok]="left() === 0">
                {{ i18n.t('vote.check.budget', { n: c.rules.pointBudget }) }}
              </li>
              <li [class.ok]="chosenCount() >= c.rules.minActivities">
                {{ i18n.t('vote.check.min', { n: c.rules.minActivities, count: chosenCount() }) }}
              </li>
              <li [class.ok]="!noDateFits()">{{ i18n.t('vote.check.sessions') }}</li>
            </ul>
          </div>
          <div class="card stack">
            <h3>{{ i18n.t('vote.ranking') }}</h3>
            @if (ranking().length) {
              <ol class="rank">
                @for (r of ranking(); track r.id) {
                  <li>
                    {{ r.title }} <span class="muted">· {{ r.points }}</span>
                  </li>
                }
              </ol>
            } @else {
              <p class="muted small">{{ i18n.t('vote.rankingEmpty') }}</p>
            }
          </div>
          <div class="card stack">
            <div class="field">
              <label for="name">{{ i18n.t('vote.name') }}</label>
              <input
                id="name"
                autocomplete="off"
                [placeholder]="i18n.t('vote.namePlaceholder')"
                [ngModel]="name()"
                (ngModelChange)="name.set($event)"
              />
            </div>
            @if (errors().length) {
              <ul class="errors">
                @for (e of errors(); track $index) {
                  <li>{{ e }}</li>
                }
              </ul>
            }
            <button
              class="btn"
              type="button"
              [disabled]="!canSubmit() || busy()"
              (click)="submit()"
            >
              {{ i18n.t('vote.submit') }}
            </button>
            <button class="btn ghost" type="button" (click)="fillExample()">
              {{ i18n.t('vote.example') }}
            </button>
            <p class="muted small">{{ i18n.t('vote.note') }}</p>
            @if (api.myBallotId()) {
              <a class="btn ghost" routerLink="/results">{{ i18n.t('vote.seeResult') }}</a>
            }
          </div>
        </aside>
      </div>
    } @else if (loadError()) {
      <p class="muted">{{ i18n.t('common.loadError') }}</p>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 320px;
      gap: 24px;
      align-items: start;
    }
    @media (max-width: 860px) {
      .layout {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .list {
      display: grid;
      gap: 8px;
    }
    .act {
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--r);
      padding: 12px 14px;
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 6px 14px;
      align-items: center;
    }
    .act.on {
      border-color: var(--accent);
    }
    .info {
      min-width: 0;
    }
    ul {
      list-style: none;
      margin: 4px 0 0;
      padding: 0;
      display: grid;
      gap: 2px;
      font-size: 13px;
      color: var(--muted);
    }
    label {
      display: flex;
      gap: 8px;
      align-items: flex-start;
      cursor: pointer;
    }
    label input {
      margin-top: 3px;
      accent-color: var(--accent);
    }
    label.off span {
      text-decoration: line-through;
    }
    .stepper {
      display: flex;
      align-items: center;
      gap: 4px;
    }
    .stepper button {
      width: 34px;
      height: 34px;
      border-radius: 8px;
      border: 1px solid var(--line);
      background: var(--bg);
      cursor: pointer;
      font-size: 18px;
      line-height: 1;
    }
    .stepper button:hover:not(:disabled) {
      border-color: var(--accent);
    }
    .stepper button:disabled {
      opacity: 0.35;
      cursor: default;
    }
    .stepper output {
      min-width: 44px;
      text-align: center;
      font-weight: 700;
      font-size: 18px;
    }
    .act.on .stepper output {
      color: var(--accent);
    }
    .side {
      position: sticky;
      top: 16px;
      display: grid;
      gap: 14px;
    }
    @media (max-width: 860px) {
      .side {
        position: static;
      }
    }
    .of {
      font-size: 16px;
      font-weight: 400;
    }
    .checks {
      list-style: none;
      padding: 0;
      margin: 0;
      display: grid;
      gap: 4px;
      font-size: 14px;
      color: var(--ink);
    }
    .checks li::before {
      content: '○';
      display: inline-block;
      width: 20px;
      color: var(--muted);
    }
    .checks li.ok::before {
      content: '✓';
      color: var(--good);
      font-weight: 700;
    }
    .rank {
      margin: 0;
      padding-left: 22px;
      display: grid;
      gap: 2px;
      font-size: 14px;
    }
    .mini {
      position: sticky;
      top: env(safe-area-inset-top, 0px);
      z-index: 5;
      display: none;
      background: var(--surface);
      border: 1px solid var(--line);
      border-radius: var(--r);
      padding: 10px 12px;
      margin-bottom: 10px;
      gap: 10px;
      align-items: center;
    }
    .mini .meter {
      flex: 1;
    }
    @media (max-width: 860px) {
      .mini {
        display: flex;
      }
    }
  `,
})
export class VotePage {
  protected i18n = inject(I18n);
  protected api = inject(Api);
  private toast = inject(Toast);

  protected config = signal<Config | null>(null);
  protected activities = signal<Activity[]>([]);
  protected loadError = signal(false);
  protected points = signal<Record<string, number>>({});
  protected unavailable = signal(new Set<string>());
  protected name = signal('');
  protected errors = signal<string[]>([]);
  protected busy = signal(false);

  private rules = computed(() => this.config()!.rules);
  protected spent = computed(() => Object.values(this.points()).reduce((a, b) => a + b, 0));
  protected left = computed(() => (this.config() ? this.rules().pointBudget - this.spent() : 0));
  protected spentPercent = computed(() =>
    this.config() ? Math.min(100, (this.spent() / this.rules().pointBudget) * 100) : 0,
  );
  protected chosenCount = computed(() => Object.values(this.points()).filter((p) => p > 0).length);
  protected remaining = computed(
    () => `${this.left()} ${this.i18n.t('vote.of', { n: this.config()?.rules.pointBudget ?? 0 })}`,
  );
  protected ranking = computed(() =>
    this.activities()
      .filter((a) => this.pointsFor(a.id) > 0)
      .map((a) => ({ id: a.id, title: a.title, points: this.pointsFor(a.id) }))
      .sort((x, y) => y.points - x.points),
  );
  protected noDateFits = computed(() =>
    this.activities().some(
      (a) => this.pointsFor(a.id) > 0 && a.sessions.every((s) => this.unavailable().has(s.id)),
    ),
  );
  protected canSubmit = computed(
    () =>
      !!this.config() &&
      this.left() === 0 &&
      this.chosenCount() >= this.rules().minActivities &&
      !this.noDateFits() &&
      this.name().trim().length > 0,
  );

  constructor() {
    this.load();
  }

  private async load(): Promise<void> {
    try {
      const [config, activities] = await Promise.all([this.api.config(), this.api.activities()]);
      this.config.set(config);
      this.activities.set(activities);
    } catch {
      this.loadError.set(true);
    }
  }

  protected pointsFor(id: string): number {
    return this.points()[id] ?? 0;
  }

  protected sessionText(s: Activity['sessions'][number]): string {
    const seats = s.capacity ? ' · ' + this.i18n.t('common.seats', { n: s.capacity }) : '';
    return `${this.i18n.when(s)} · ${s.location}${seats}`;
  }

  protected change(id: string, direction: 1 | -1): void {
    const r = this.rules();
    const current = this.pointsFor(id);
    const next = Math.max(0, Math.min(r.maxPerActivity, current + direction * r.step));
    if (next - current > this.left()) return;
    const copy = { ...this.points() };
    if (next > 0) copy[id] = next;
    else delete copy[id];
    this.points.set(copy);
  }

  protected toggleSession(id: string): void {
    const copy = new Set(this.unavailable());
    if (copy.has(id)) copy.delete(id);
    else copy.add(id);
    this.unavailable.set(copy);
  }

  /** Demo helper: a valid example distribution. */
  protected fillExample(): void {
    this.points.set({ nacht: 40, kino: 25, hochseil: 20, kitchen: 10, advent: 5 });
    this.unavailable.set(new Set(['hochseil#0']));
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const res = await this.api.submitBallot({
        name: this.name().trim(),
        points: this.points(),
        unavailableSessions: [...this.unavailable()],
      });
      this.api.myBallotId.set(res.id);
      this.toast.show(this.i18n.t(res.replaced ? 'vote.replaced' : 'vote.saved'));
    } catch (e) {
      this.errors.set(
        e instanceof ApiFailure
          ? e.errors.map((x) => this.i18n.error(x))
          : [this.i18n.t('error.generic')],
      );
    } finally {
      this.busy.set(false);
    }
  }
}
