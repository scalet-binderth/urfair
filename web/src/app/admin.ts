import { Component, inject, signal, ChangeDetectionStrategy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api, ApiFailure, Config, Demand, Rules } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

@Component({
  selector: 'app-admin',
  imports: [FormsModule],
  template: `
    <div class="head">
      <h1>{{ i18n.t('admin.title') }}</h1>
      <p>{{ i18n.t('admin.lead') }}</p>
    </div>
    @if (config(); as c) {
      <div class="stack">
        @if (c.adminProtected) {
          <div class="card row">
            <div class="field">
              <label for="key">{{ i18n.t('admin.key') }}</label>
              <input
                id="key"
                type="password"
                [ngModel]="api.adminKey()"
                (ngModelChange)="api.setAdminKey($event)"
                style="max-width:260px"
              />
            </div>
            <button class="btn ghost" type="button" (click)="loadDemand()">OK</button>
          </div>
        }
        @if (errors().length) {
          <ul class="errors">
            @for (e of errors(); track $index) {
              <li>{{ e }}</li>
            }
          </ul>
        }

        <div class="card stack">
          <h2>{{ i18n.t('admin.rules') }}</h2>
          <div class="rules">
            <div class="field">
              <label for="r-budget">{{ i18n.t('admin.budget') }}</label
              ><input id="r-budget" type="number" step="5" [(ngModel)]="rules.pointBudget" />
            </div>
            <div class="field">
              <label for="r-min">{{ i18n.t('admin.min') }}</label
              ><input id="r-min" type="number" step="1" [(ngModel)]="rules.minActivities" />
            </div>
            <div class="field">
              <label for="r-max">{{ i18n.t('admin.max') }}</label
              ><input id="r-max" type="number" step="5" [(ngModel)]="rules.maxPerActivity" />
            </div>
            <div class="field">
              <label for="r-k">{{ i18n.t('admin.k') }}</label
              ><input id="r-k" type="number" step="1" [(ngModel)]="rules.perParticipant" />
            </div>
          </div>
          <div>
            <button class="btn ghost" type="button" [disabled]="busy()" (click)="saveRules()">
              {{ i18n.t('admin.saveRules') }}
            </button>
          </div>
        </div>

        <div class="card stack">
          <h2>{{ i18n.t('admin.ballots') }}</h2>
          <p>{{ i18n.t('admin.ballotCount', { n: c.ballotCount, s: c.sampleCount }) }}</p>
          <div class="row">
            <div class="field">
              <label for="n-sample">{{ i18n.t('admin.sampleCount') }}</label>
              <input
                id="n-sample"
                type="number"
                min="0"
                max="1000"
                step="10"
                [(ngModel)]="sampleCount"
                style="max-width:120px"
              />
            </div>
            <label class="check"
              ><input type="checkbox" [(ngModel)]="removeReal" />{{
                i18n.t('admin.removeReal')
              }}</label
            >
            <button class="btn ghost" type="button" [disabled]="busy()" (click)="reset()">
              {{ i18n.t('admin.reset') }}
            </button>
            <button class="btn" type="button" (click)="router.navigate(['/results'])">
              {{ i18n.t('admin.run') }}
            </button>
          </div>
        </div>

        <div class="card stack">
          <h2>{{ i18n.t('admin.demand') }}</h2>
          <p class="muted small">{{ i18n.t('admin.demandNote') }}</p>
          <div class="tbl-wrap">
            <table>
              <thead>
                <tr>
                  <th>{{ i18n.t('admin.col.activity') }}</th>
                  <th class="num">{{ i18n.t('admin.col.seats') }}</th>
                  <th class="num">{{ i18n.t('admin.col.interested') }}</th>
                  <th class="num">{{ i18n.t('admin.col.points') }}</th>
                  <th class="num">{{ i18n.t('admin.col.avg') }}</th>
                  <th>{{ i18n.t('admin.col.demand') }}</th>
                </tr>
              </thead>
              <tbody>
                @for (d of demand(); track d.activityId) {
                  <tr>
                    <td>{{ d.title }}</td>
                    <td class="num">{{ d.seats ?? i18n.t('common.unlimited') }}</td>
                    <td class="num">{{ d.interested }}</td>
                    <td class="num">{{ d.totalPoints }}</td>
                    <td class="num">{{ d.averagePoints.toFixed(1) }}</td>
                    <td>
                      @if (d.seats) {
                        <div class="ratio">
                          <div class="meter">
                            <i
                              [class.hot]="d.interested > d.seats"
                              [style.width.%]="ratioWidth(d)"
                            ></i>
                          </div>
                          <span class="small">{{ (d.interested / d.seats).toFixed(1) }}×</span>
                        </div>
                      } @else {
                        <span class="muted small">–</span>
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </div>
      </div>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .rules {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));
      gap: 14px;
    }
    .rules input {
      max-width: 120px;
    }
    .ratio {
      display: flex;
      align-items: center;
      gap: 8px;
      min-width: 150px;
    }
    .ratio .meter {
      flex: 1;
      height: 8px;
    }
    .ratio .meter i.hot {
      background: var(--warn);
    }
  `,
})
export class AdminPage {
  protected i18n = inject(I18n);
  protected api = inject(Api);
  protected router = inject(Router);
  private toast = inject(Toast);

  protected config = signal<Config | null>(null);
  protected demand = signal<Demand[]>([]);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected rules: Rules = {
    pointBudget: 100,
    minActivities: 5,
    maxPerActivity: 40,
    perParticipant: 3,
    step: 5,
  };
  protected sampleCount = 140;
  protected removeReal = false;

  constructor() {
    this.load();
  }

  private async load(): Promise<void> {
    const c = await this.api.config();
    this.config.set(c);
    this.rules = { ...c.rules };
    this.sampleCount = c.sampleCount;
    await this.loadDemand();
  }

  protected async loadDemand(): Promise<void> {
    await this.run(async () => this.demand.set(await this.api.demand()));
  }

  protected ratioWidth(d: Demand): number {
    return Math.min(100, (d.interested / (d.seats ?? 1) / 3) * 100);
  }

  protected async saveRules(): Promise<void> {
    await this.run(async () => {
      await this.api.saveRules(this.rules);
      this.toast.show(this.i18n.t('admin.rulesSaved'));
      await this.load();
    });
  }

  protected async reset(): Promise<void> {
    await this.run(async () => {
      await this.api.resetDemo({
        sampleCount: this.sampleCount,
        newSeed: true,
        removeRealBallots: this.removeReal,
      });
      this.toast.show(this.i18n.t('admin.resetDone'));
      await this.load();
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      await action();
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
