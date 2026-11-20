import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Activity, Api, Group, MyStatus, Slot } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';
import { Toast } from './toast';

/** Voting page of one FairSlot. */
@Component({
  selector: 'app-vote',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    @if (slot(); as c) {
      <div class="head">
        <p class="muted small">{{ c.name }}</p>
        <h1>{{ i18n.t('vote.title') }}</h1>
        <p>{{ i18n.t('vote.lead', { budget: c.rules.pointBudget, min: c.rules.minActivities, max: c.rules.maxPerActivity, k: c.rules.perParticipant }) }}</p>
        @if (groups().length) { <p>{{ i18n.t('vote.leadGroups') }}</p> }
      </div>
      @if (c.phase !== 'Open') { <p class="card">{{ i18n.t('vote.closed') }}</p> }
      <div class="mini">
        <span class="small">{{ left() }} {{ i18n.t('vote.of', { n: c.rules.pointBudget }) }} · {{ chosenCount() }}</span>
        <div class="meter"><i [style.width.%]="spentPercent()"></i></div>
      </div>
      @if (status()?.voted) {
        <section class="card stack status">
          <h2>{{ i18n.t('status.title') }}</h2>
          <p class="muted small">{{ i18n.t('status.lead') }}</p>
          <ul class="st">
            @for (x of status()!.activities; track x.activityId) {
              <li>
                <span class="t">@if (groupFor(x.groupId); as g) { <span class="grp" [attr.data-color]="g.color"></span> }{{ x.title }}
                  <span class="muted small">· {{ i18n.t('status.points', { n: x.points }) }}</span></span>
                <span class="chip" [attr.data-status]="x.status">{{ i18n.t('status.' + x.status, { n: x.position ?? 0 }) }}</span>
                @if (x.status === 'fixed') {
                  <span class="muted small w">{{ i18n.when({ start: x.start!, end: x.end, flexible: x.flexible }) }} · {{ x.location }}</span>
                } @else {
                  <label class="w small"><input type="checkbox" [checked]="x.waitlisted" [disabled]="busy()" (change)="toggleWaitlist(x.activityId)" />{{ i18n.t('status.keep') }}</label>
                }
              </li>
            }
          </ul>
          <p class="muted small">{{ i18n.t('status.note') }}</p>
        </section>
      }
      <div class="layout">
        <div class="list">
          @for (sec of sections(); track sec.key) {
          @if (sec.group; as g) {
            <h2 class="gh" [attr.data-color]="g.color"><span class="grp"></span>{{ g.name }}
              <span class="muted small">{{ i18n.t('vote.groupRule', { min: g.minActivities, k: g.perParticipant }) }}</span></h2>
          } @else if (sections().length > 1) { <h2 class="gh">{{ i18n.t('vote.ungrouped') }}</h2> }
          @for (a of sec.activities; track a.id) {
            <div class="act" [class.on]="pointsFor(a.id) > 0">
              <div class="info">
                <h3>
                  {{ a.title }}
                  @if (a.restrictedTo) { <span class="tag">{{ i18n.t('common.only.' + a.restrictedTo) }}</span> }
                  @if (a.seats === null) { <span class="tag">{{ i18n.t('common.unlimited') }}</span> }
                  @if (status()?.full?.includes(a.id)) { <span class="tag full">{{ i18n.t('status.full') }}</span> }
                </h3>
                @if (a.description) { <p class="desc">{{ a.description }}</p> }
                @if (a.sessions.length > 1) { <div class="muted small">{{ i18n.t('vote.pickSessions') }}</div> }
                <ul>
                  @for (s of a.sessions; track s.id) {
                    <li>
                      @if (a.sessions.length > 1) {
                        <label [class.off]="unavailable().has(s.id)">
                          <input type="checkbox" [checked]="!unavailable().has(s.id)" (change)="toggleSession(s.id)" />
                          <span>{{ sessionText(s) }}</span>
                        </label>
                      } @else { {{ sessionText(s) }} }
                    </li>
                  }
                </ul>
              </div>
              <div class="stepper">
                <button type="button" [disabled]="pointsFor(a.id) <= 0" (click)="change(a.id, -1)" [attr.aria-label]="i18n.t('vote.less', { title: a.title })">−</button>
                <output>{{ pointsFor(a.id) }}</output>
                <button type="button" [disabled]="pointsFor(a.id) >= c.rules.maxPerActivity || left() <= 0" (click)="change(a.id, 1)" [attr.aria-label]="i18n.t('vote.more', { title: a.title })">+</button>
              </div>
            </div>
          }
          }
        </div>
        <aside class="side">
          <div class="card stack">
            <div>
              <div class="muted small">{{ i18n.t('vote.remaining') }}</div>
              <div class="big">{{ left() }} <span class="muted of">{{ i18n.t('vote.of', { n: c.rules.pointBudget }) }}</span></div>
            </div>
            <div class="meter"><i [style.width.%]="spentPercent()"></i></div>
            <ul class="checks">
              <li [class.ok]="left() === 0">{{ i18n.t('vote.check.budget', { n: c.rules.pointBudget }) }}</li>
              <li [class.ok]="chosenCount() >= c.rules.minActivities">{{ i18n.t('vote.check.min', { n: c.rules.minActivities, count: chosenCount() }) }}</li>
              @for (g of groups(); track g.id) {
                <li [class.ok]="countIn(g.id) >= g.minActivities">{{ i18n.t('vote.check.group', { name: g.name, n: g.minActivities, count: countIn(g.id) }) }}</li>
              }
              <li [class.ok]="!noDateFits()">{{ i18n.t('vote.check.sessions') }}</li>
            </ul>
          </div>
          <div class="card stack">
            <h3>{{ i18n.t('vote.ranking') }}</h3>
            @if (ranking().length) {
              <ol class="rank">@for (r of ranking(); track r.id) { <li>{{ r.title }} <span class="muted">· {{ r.points }}</span></li> }</ol>
            } @else { <p class="muted small">{{ i18n.t('vote.rankingEmpty') }}</p> }
          </div>
          <div class="card stack">
            <p class="small">{{ i18n.t('vote.as', { name: name() }) }}</p>
            @if (loadedPrevious()) { <p class="small muted">{{ i18n.t('vote.loaded') }}</p> }
            @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
            <button class="btn" type="button" [disabled]="!canSubmit() || busy() || c.phase !== 'Open'" (click)="submit()">{{ i18n.t('vote.submit') }}</button>
            <button class="btn ghost" type="button" (click)="fillExample()">{{ i18n.t('vote.example') }}</button>
            <p class="muted small">{{ i18n.t('vote.note') }}</p>
            <a routerLink="/slots" class="small">{{ i18n.t('vote.back') }}</a>
          </div>
        </aside>
      </div>
    } @else if (loadError()) {
      <p class="muted">{{ i18n.t('common.loadError') }}</p>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 24px; align-items: start }
    @media (max-width: 860px) { .layout { grid-template-columns: minmax(0, 1fr) } }
    .list { display: grid; gap: 8px }
    .status { margin-bottom: 18px }
    .st { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; color: var(--ink); font-size: 15px }
    .st li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 2px 10px; padding-bottom: 8px; border-bottom: 1px dashed var(--line) }
    .st .w { grid-column: 1 / -1 }
    .chip { font-size: 12px; font-weight: 600; padding: 2px 8px; border-radius: 999px; background: var(--bar); color: var(--muted); white-space: nowrap; align-self: start }
    .chip[data-status="fixed"] { background: var(--good-soft); color: var(--good) }
    .chip[data-status="waitlist"] { background: var(--warn-soft); color: var(--warn) }
    .tag.full { background: var(--warn-soft); color: var(--warn) }
    .gh { font-size: 18px; margin: 10px 0 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px }
    .gh:first-child { margin-top: 0 }
    .gh .grp { width: 14px; height: 14px; margin: 0; align-self: center }
    .act { background: var(--surface); border: 1px solid var(--line); border-radius: var(--r); padding: 12px 14px; display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 6px 14px; align-items: center }
    .act.on { border-color: var(--accent) }
    .info { min-width: 0 }
    .desc { margin: 2px 0 6px; font-size: 14px; white-space: pre-line }
    ul { list-style: none; margin: 4px 0 0; padding: 0; display: grid; gap: 2px; font-size: 13px; color: var(--muted) }
    label { display: flex; gap: 8px; align-items: flex-start; cursor: pointer }
    label input { margin-top: 3px; accent-color: var(--accent) }
    label.off span { text-decoration: line-through }
    .stepper { display: flex; align-items: center; gap: 4px }
    .stepper button { width: 34px; height: 34px; border-radius: 8px; border: 1px solid var(--line); background: var(--bg); cursor: pointer; font-size: 18px; line-height: 1 }
    .stepper button:hover:not(:disabled) { border-color: var(--accent) }
    .stepper button:disabled { opacity: 0.35; cursor: default }
    .stepper output { min-width: 44px; text-align: center; font-weight: 700; font-size: 18px }
    .act.on .stepper output { color: var(--accent) }
    .side { position: sticky; top: 16px; display: grid; gap: 14px }
    @media (max-width: 860px) { .side { position: static } }
    .of { font-size: 16px; font-weight: 400 }
    .checks { list-style: none; padding: 0; margin: 0; display: grid; gap: 4px; font-size: 14px; color: var(--ink) }
    .checks li::before { content: '○'; display: inline-block; width: 20px; color: var(--muted) }
    .checks li.ok::before { content: '✓'; color: var(--good); font-weight: 700 }
    .rank { margin: 0; padding-left: 22px; display: grid; gap: 2px; font-size: 14px }
    .mini { position: sticky; top: env(safe-area-inset-top, 0px); z-index: 5; display: none; background: var(--surface); border: 1px solid var(--line); border-radius: var(--r); padding: 10px 12px; margin-bottom: 10px; gap: 10px; align-items: center }
    .mini .meter { flex: 1 }
    @media (max-width: 860px) { .mini { display: flex } }
  `,
})
export class VotePage implements OnInit {
  /** Bound from the route parameter :slotId. */
  readonly slotId = input.required<string>();

  protected i18n = inject(I18n);
  private api = inject(Api);
  private toast = inject(Toast);

  protected slot = signal<Slot | null>(null);
  protected activities = signal<Activity[]>([]);
  protected loadError = signal(false);
  protected points = signal<Record<string, number>>({});
  protected unavailable = signal(new Set<string>());
  private session = inject(SessionService);
  protected name = computed(() => this.session.participantName() ?? '');
  protected loadedPrevious = signal(false);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);

  private rules = computed(() => this.slot()!.rules);
  protected spent = computed(() => Object.values(this.points()).reduce((a, b) => a + b, 0));
  protected left = computed(() => (this.slot() ? this.rules().pointBudget - this.spent() : 0));
  protected spentPercent = computed(() => (this.slot() ? Math.min(100, (this.spent() / this.rules().pointBudget) * 100) : 0));
  protected chosenCount = computed(() => Object.values(this.points()).filter(p => p > 0).length);
  protected ranking = computed(() =>
    this.activities().filter(a => this.pointsFor(a.id) > 0)
      .map(a => ({ id: a.id, title: a.title, points: this.pointsFor(a.id) }))
      .sort((x, y) => y.points - x.points));
  protected noDateFits = computed(() =>
    this.activities().some(a => this.pointsFor(a.id) > 0 && a.sessions.every(s => this.unavailable().has(s.id))));
  protected status = signal<MyStatus | null>(null);
  protected groups = computed(() => this.slot()?.rules.groups ?? []);
  protected groupFor(id: string | null): Group | undefined { return this.groups().find(g => g.id === id); }

  private async loadStatus(): Promise<void> {
    try { this.status.set(await this.api.myStatus(this.slotId(), this.name())); } catch { this.status.set(null); }
  }

  protected async toggleWaitlist(activityId: string): Promise<void> {
    const current = this.status()?.activities.filter(x => x.waitlisted).map(x => x.activityId) ?? [];
    const next = current.includes(activityId) ? current.filter(x => x !== activityId) : [...current, activityId];
    this.busy.set(true);
    try {
      this.status.set(await this.api.setWaitlist(this.slotId(), this.name(), next));
      this.toast.show(this.i18n.t('status.saved'));
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
  /** Activities per group (in group order), ungrouped ones last. */
  protected sections = computed(() => {
    const list = this.activities();
    const known = new Set(this.groups().map(g => g.id));
    const result = this.groups().map(g => ({ key: g.id, group: g as Group | null, activities: list.filter(a => a.groupId === g.id) }));
    const rest = list.filter(a => !a.groupId || !known.has(a.groupId));
    if (rest.length || !result.length) result.push({ key: '', group: null, activities: rest });
    return result.filter(s => s.activities.length);
  });
  protected countIn(groupId: string): number { return this.activities().filter(a => a.groupId === groupId && this.pointsFor(a.id) > 0).length; }
  protected canSubmit = computed(() =>
    !!this.slot() && this.left() === 0 && this.chosenCount() >= this.rules().minActivities && !this.noDateFits() && this.name().trim().length > 0 &&
    this.groups().every(g => this.countIn(g.id) >= g.minActivities));

  async ngOnInit(): Promise<void> {
    try {
      const [slot, activities] = await Promise.all([this.api.slot(this.slotId()), this.api.activities(this.slotId())]);
      this.slot.set(slot);
      this.activities.set(activities);
      // Demo: show the earlier ballot of this name so it can be changed.
      const previous = await this.api.lookupBallot(this.slotId(), this.name());
      if (previous) {
        this.points.set(previous.points);
        this.unavailable.set(new Set(previous.unavailableSessions));
        this.loadedPrevious.set(true);
      }
      await this.loadStatus();
    } catch {
      this.loadError.set(true);
    }
  }

  protected pointsFor(id: string): number { return this.points()[id] ?? 0; }

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
    if (next > 0) copy[id] = next; else delete copy[id];
    this.points.set(copy);
  }

  protected toggleSession(id: string): void {
    const copy = new Set(this.unavailable());
    if (copy.has(id)) copy.delete(id); else copy.add(id);
    this.unavailable.set(copy);
  }

  /** Demo helper: a valid distribution over the first activities, more points on the earlier ones. */
  protected fillExample(): void {
    const r = this.rules();
    // Each group's minimum first, then the remaining activities in list order.
    const first = this.groups().flatMap(g => this.activities().filter(a => a.groupId === g.id).slice(0, g.minActivities));
    const list = [...first, ...this.activities().filter(a => !first.includes(a))];
    const units = r.pointBudget / r.step, maxUnits = r.maxPerActivity / r.step;
    const n = Math.min(list.length, Math.max(r.minActivities, first.length, Math.ceil(units / maxUnits)));
    if (n === 0) return;
    const u: number[] = Array(n).fill(1);
    let rest = units - n;
    // Fill from the top, each activity at most maxUnits.
    for (let i = 0; rest > 0 && i < n; i++) {
      const add = Math.min(rest, maxUnits - u[i]);
      u[i] += add;
      rest -= add;
    }
    const points: Record<string, number> = {};
    list.slice(0, n).forEach((a, i) => (points[a.id] = u[i] * r.step));
    this.points.set(points);
    this.unavailable.set(new Set());
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const res = await this.api.submitBallot(this.slotId(), { name: this.name().trim(), points: this.points(), unavailableSessions: [...this.unavailable()] });
      this.toast.show(this.i18n.t(res.replaced ? 'vote.replaced' : 'vote.saved'));
      await this.loadStatus();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
}
