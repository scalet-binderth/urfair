import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ActivityEditor } from './activity-editor';
import { AdminKeyPrompt } from './admin-key';
import { Activity, Api, ApiFailure, Demand, PHASES, Phase, Rules, Slot, fromLocalInput, toLocalInput } from './api';
import { I18n } from './i18n';
import { ResultsPage } from './results';
import { Toast } from './toast';

type Tab = 'settings' | 'activities' | 'demand' | 'results' | 'demo';

/** Organisation view of one FairSlot. */
@Component({
  selector: 'app-admin-slot',
  imports: [FormsModule, RouterLink, ActivityEditor, AdminKeyPrompt, ResultsPage],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <p><a routerLink="/admin">{{ i18n.t('admin.back') }}</a></p>
    @if (needsKey()) { <app-admin-key (entered)="load()" /> }
    @if (slot(); as s) {
      <div class="head">
        <h1>{{ s.name }} <span class="phase" [attr.data-phase]="s.phase">{{ i18n.t('phase.' + s.phase) }}</span></h1>
        <div class="row link">
          <span class="muted small">{{ i18n.t('admin.participantLink') }}:</span>
          <a [routerLink]="['/s', s.id]" class="small">{{ participantUrl() }}</a>
          <button class="btn ghost small" type="button" (click)="copyLink()">{{ i18n.t('admin.copyLink') }}</button>
        </div>
      </div>

      <nav class="tabs" role="tablist">
        @for (t of tabs; track t) {
          <button type="button" role="tab" [attr.aria-selected]="tab() === t" [class.on]="tab() === t" (click)="show(t)">
            {{ i18n.t('admin.tab.' + t) }}@if (t === 'activities') { ({{ activities().length }}) }
          </button>
        }
      </nav>
      @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }

      @switch (tab()) {
        @case ('settings') {
          <div class="stack">
            <div class="card stack">
              <div class="row">
                <div class="field grow"><label for="s-name">{{ i18n.t('admin.name') }}</label><input id="s-name" [(ngModel)]="form.name" /></div>
                <div class="field"><label for="s-phase">{{ i18n.t('admin.phase') }}</label>
                  <select id="s-phase" [(ngModel)]="form.phase">
                    @for (p of phases; track p) { <option [value]="p">{{ i18n.t('phase.' + p) }}</option> }
                  </select></div>
                <div class="field"><label for="s-deadline">{{ i18n.t('admin.deadline') }}</label>
                  <input id="s-deadline" type="datetime-local" [(ngModel)]="form.deadline" /></div>
              </div>
              <p class="muted small">{{ i18n.t('admin.phaseHint') }}</p>
              <h2>{{ i18n.t('admin.rules') }}</h2>
              <div class="rules">
                <div class="field"><label for="r-budget">{{ i18n.t('admin.budget') }}</label><input id="r-budget" type="number" step="5" [(ngModel)]="form.rules.pointBudget" /></div>
                <div class="field"><label for="r-min">{{ i18n.t('admin.min') }}</label><input id="r-min" type="number" step="1" [(ngModel)]="form.rules.minActivities" /></div>
                <div class="field"><label for="r-max">{{ i18n.t('admin.max') }}</label><input id="r-max" type="number" step="5" [(ngModel)]="form.rules.maxPerActivity" /></div>
                <div class="field"><label for="r-k">{{ i18n.t('admin.k') }}</label><input id="r-k" type="number" step="1" [(ngModel)]="form.rules.perParticipant" /></div>
              </div>
              <p class="muted small">{{ i18n.t('admin.rulesNote') }}</p>
              <div><button class="btn" type="button" [disabled]="busy()" (click)="saveSettings()">{{ i18n.t('common.save') }}</button></div>
            </div>
            <div class="card stack">
              <h2>{{ i18n.t('admin.deleteSlot') }}</h2>
              <p class="muted small">{{ i18n.t('admin.deleteSlotWarn') }}</p>
              @if (confirmDeleteSlot()) {
                <div class="row">
                  <span>{{ i18n.t('common.reallyDelete') }}</span>
                  <button class="btn danger" type="button" (click)="deleteSlot()">{{ i18n.t('common.delete') }}</button>
                  <button class="btn ghost" type="button" (click)="confirmDeleteSlot.set(false)">{{ i18n.t('common.cancel') }}</button>
                </div>
              } @else {
                <div><button class="btn ghost" type="button" (click)="confirmDeleteSlot.set(true)">{{ i18n.t('admin.deleteSlot') }}</button></div>
              }
            </div>
          </div>
        }

        @case ('activities') {
          <div class="stack">
            @if (editing() === 'new') {
              <app-activity-editor [slotId]="s.id" (saved)="afterSave()" (cancelled)="editing.set(null)" />
            } @else {
              <div><button class="btn" type="button" (click)="editing.set('new')">{{ i18n.t('admin.addActivity') }}</button></div>
            }
            @for (a of activities(); track a.id) {
              @if (editing() === a.id) {
                <app-activity-editor [slotId]="s.id" [activity]="a" (saved)="afterSave()" (cancelled)="editing.set(null)" />
              } @else {
                <div class="card act">
                  <div class="info">
                    <h3>{{ a.title }}
                      @if (a.restrictedTo) { <span class="tag">{{ i18n.t('common.only.' + a.restrictedTo) }}</span> }
                      <span class="tag">{{ a.seats ?? i18n.t('common.unlimited') }} {{ a.seats ? i18n.t('admin.col.seats') : '' }}</span>
                    </h3>
                    <ul>@for (x of a.sessions; track x.id) { <li>{{ i18n.when(x) }} · {{ x.location }}@if (x.capacity) { · {{ i18n.t('common.seats', { n: x.capacity }) }} }</li> }</ul>
                  </div>
                  <div class="row actions">
                    @if (confirmDelete() === a.id) {
                      <span class="small">{{ i18n.t('common.reallyDelete') }}</span>
                      <button class="btn danger small" type="button" (click)="deleteActivity(a.id)">{{ i18n.t('common.delete') }}</button>
                      <button class="btn ghost small" type="button" (click)="confirmDelete.set(null)">{{ i18n.t('common.cancel') }}</button>
                    } @else {
                      <button class="btn ghost small" type="button" (click)="editing.set(a.id)">{{ i18n.t('common.edit') }}</button>
                      <button class="btn ghost small" type="button" (click)="confirmDelete.set(a.id)">{{ i18n.t('common.delete') }}</button>
                    }
                  </div>
                </div>
              }
            } @empty {
              @if (editing() !== 'new') { <p class="muted">{{ i18n.t('admin.noActivities') }}</p> }
            }
          </div>
        }

        @case ('demand') {
          <div class="card stack">
            <p class="muted small">{{ i18n.t('admin.demandNote') }}</p>
            <div class="tbl-wrap"><table>
              <thead><tr>
                <th>{{ i18n.t('admin.col.activity') }}</th><th class="num">{{ i18n.t('admin.col.seats') }}</th>
                <th class="num">{{ i18n.t('admin.col.interested') }}</th><th class="num">{{ i18n.t('admin.col.points') }}</th>
                <th class="num">{{ i18n.t('admin.col.avg') }}</th><th>{{ i18n.t('admin.col.demand') }}</th>
              </tr></thead>
              <tbody>
                @for (d of demand(); track d.activityId) {
                  <tr>
                    <td>{{ d.title }}</td>
                    <td class="num">{{ d.seats ?? i18n.t('common.unlimited') }}</td>
                    <td class="num">{{ d.interested }}</td>
                    <td class="num">{{ d.totalPoints }}</td>
                    <td class="num">{{ d.averagePoints.toFixed(1) }}</td>
                    <td>@if (d.seats) {
                      <div class="ratio"><div class="meter"><i [class.hot]="d.interested > d.seats" [style.width.%]="ratioWidth(d)"></i></div><span class="small">{{ (d.interested / d.seats).toFixed(1) }}×</span></div>
                    } @else { <span class="muted small">–</span> }</td>
                  </tr>
                }
              </tbody>
            </table></div>
          </div>
        }

        @case ('results') { <app-results [slotId]="s.id" /> }

        @case ('demo') {
          <div class="card stack">
            <p>{{ i18n.t('admin.ballotCount', { n: s.ballotCount, s: s.sampleCount }) }}</p>
            <div class="row">
              <div class="field"><label for="n-sample">{{ i18n.t('admin.sampleCount') }}</label>
                <input id="n-sample" type="number" min="0" max="1000" step="10" [(ngModel)]="sampleCount" style="max-width:120px" /></div>
              <label class="check"><input type="checkbox" [(ngModel)]="removeReal" />{{ i18n.t('admin.removeReal') }}</label>
              <button class="btn" type="button" [disabled]="busy()" (click)="resetDemo()">{{ i18n.t('admin.reset') }}</button>
            </div>
          </div>
        }
      }
    } @else if (!needsKey() && !errors().length) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    h1 .phase { font-size: 13px; vertical-align: middle; margin-left: 8px }
    .link { align-items: center; gap: 8px }
    .link a { overflow-wrap: anywhere }
    .tabs { display: flex; flex-wrap: wrap; gap: 4px; border-bottom: 1px solid var(--line); margin-bottom: 16px }
    .tabs button { padding: 9px 14px; border: 0; border-bottom: 2px solid transparent; background: none; cursor: pointer; color: var(--muted); font-weight: 500 }
    .tabs button.on { color: var(--ink); border-bottom-color: var(--accent) }
    .grow { flex: 1 1 260px }
    select { padding: 9px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--bg); color: var(--ink); font-size: 15px }
    .rules { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 14px }
    .rules input { max-width: 120px }
    .act { display: flex; flex-wrap: wrap; justify-content: space-between; gap: 10px }
    .act .info { min-width: 0; flex: 1 1 300px }
    .act ul { list-style: none; margin: 4px 0 0; padding: 0; font-size: 13px; color: var(--muted); display: grid; gap: 2px }
    .actions { align-items: center }
    .btn.small { padding: 6px 12px; font-size: 14px }
    .btn.danger { background: var(--warn); border-color: var(--warn); color: var(--surface) }
    .ratio { display: flex; align-items: center; gap: 8px; min-width: 150px }
    .ratio .meter { flex: 1; height: 8px }
    .ratio .meter i.hot { background: var(--warn) }
  `,
})
export class AdminSlotPage implements OnInit {
  /** Bound from the route parameter :slotId. */
  readonly slotId = input.required<string>();

  protected i18n = inject(I18n);
  private api = inject(Api);
  private router = inject(Router);
  private toast = inject(Toast);

  protected readonly tabs: Tab[] = ['settings', 'activities', 'demand', 'results', 'demo'];
  protected readonly phases = PHASES;
  protected tab = signal<Tab>('activities');
  protected slot = signal<Slot | null>(null);
  protected activities = signal<Activity[]>([]);
  protected demand = signal<Demand[]>([]);
  protected editing = signal<string | null>(null);
  protected confirmDelete = signal<string | null>(null);
  protected confirmDeleteSlot = signal(false);
  protected needsKey = signal(false);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected form: { name: string; phase: Phase; deadline: string; rules: Rules } =
    { name: '', phase: 'Draft', deadline: '', rules: { pointBudget: 100, minActivities: 5, maxPerActivity: 40, perParticipant: 3, step: 5 } };
  protected sampleCount = 0;
  protected removeReal = false;

  ngOnInit(): void { this.load(); }

  protected async load(): Promise<void> {
    this.errors.set([]);
    try {
      const [slot, activities] = await Promise.all([this.api.adminSlot(this.slotId()), this.api.adminActivities(this.slotId())]);
      this.slot.set(slot);
      this.activities.set(activities);
      this.form = { name: slot.name, phase: slot.phase, deadline: toLocalInput(slot.registrationDeadline), rules: { ...slot.rules } };
      this.sampleCount = slot.sampleCount;
      this.needsKey.set(false);
      if (this.tab() === 'demand') this.demand.set(await this.api.demand(this.slotId()));
    } catch (e) {
      this.handle(e);
    }
  }

  protected async show(t: Tab): Promise<void> {
    this.tab.set(t);
    this.editing.set(null);
    if (t === 'demand') {
      try { this.demand.set(await this.api.demand(this.slotId())); } catch (e) { this.handle(e); }
    }
  }

  protected participantUrl(): string { return `${location.origin}/s/${this.slotId()}`; }

  protected async copyLink(): Promise<void> {
    try { await navigator.clipboard.writeText(this.participantUrl()); this.toast.show(this.i18n.t('admin.linkCopied')); } catch { /* link is visible and selectable */ }
  }

  protected ratioWidth(d: Demand): number { return Math.min(100, (d.interested / (d.seats ?? 1) / 3) * 100); }

  protected saveSettings(): Promise<void> {
    return this.run(async () => {
      await this.api.updateSlot(this.slotId(), {
        name: this.form.name, phase: this.form.phase, registrationDeadline: fromLocalInput(this.form.deadline), rules: this.form.rules,
      });
      this.toast.show(this.i18n.t('common.saved'));
      await this.load();
    });
  }

  protected deleteSlot(): Promise<void> {
    return this.run(async () => {
      await this.api.deleteSlot(this.slotId());
      this.toast.show(this.i18n.t('common.deleted'));
      await this.router.navigate(['/admin']);
    });
  }

  protected async afterSave(): Promise<void> {
    this.editing.set(null);
    this.toast.show(this.i18n.t('common.saved'));
    await this.load();
  }

  protected deleteActivity(id: string): Promise<void> {
    return this.run(async () => {
      await this.api.deleteActivity(id);
      this.confirmDelete.set(null);
      this.toast.show(this.i18n.t('common.deleted'));
      await this.load();
    });
  }

  protected resetDemo(): Promise<void> {
    return this.run(async () => {
      await this.api.resetDemo(this.slotId(), { sampleCount: Number(this.sampleCount), newSeed: true, removeRealBallots: this.removeReal });
      this.toast.show(this.i18n.t('admin.resetDone'));
      await this.load();
    });
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try { await action(); } catch (e) { this.handle(e); } finally { this.busy.set(false); }
  }

  private handle(e: unknown): void {
    if (e instanceof ApiFailure && e.errors[0]?.code === 'unauthorized') this.needsKey.set(true);
    else this.errors.set(this.i18n.errors(e));
  }
}
