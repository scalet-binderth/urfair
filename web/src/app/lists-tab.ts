import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, LeaderLists, ListActivity } from './api';
import { saveBlob, toCsv } from './csv';
import { I18n } from './i18n';
import { LeaderChanges } from './leader-changes';
import { Toast } from './toast';

/** One leader with their activities (leader null = none named). */
interface LeaderGroup { leader: string | null; email: string | null; activities: ListActivity[]; }

/**
 * Lists for the activity leaders: who comes to which date, grouped by leader, to print (one page per date), as CSV,
 * or as one mail per leader.
 */
@Component({
  selector: 'app-lists',
  imports: [FormsModule, LeaderChanges],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="stack">
      <div class="card stack no-print">
        <p class="muted small">{{ i18n.t('lists.lead') }}</p>
        @if (data()?.frozen === false) { <p class="warn small">{{ i18n.t('lists.notFrozen') }}</p> }
        <div class="row">
          <div class="field"><label for="l-lead">{{ i18n.t('lists.leaderCol') }}</label>
            <select id="l-lead" [ngModel]="leader()" (ngModelChange)="leader.set($event); activityId.set('')">
              <option [ngValue]="null">{{ i18n.t('lists.allLeaders') }}</option>
              @for (g of groups(); track g.leader) { <option [ngValue]="g.leader ?? ''">{{ g.leader ?? i18n.t('lists.noLeader') }}</option> }
            </select></div>
          <div class="field"><label for="l-act">{{ i18n.t('lists.activity') }}</label>
            <select id="l-act" [ngModel]="activityId()" (ngModelChange)="activityId.set($event)">
              <option value="">{{ i18n.t('lists.all') }}</option>
              @for (a of ofLeader(); track a.id) { <option [value]="a.id">{{ a.title }}</option> }
            </select></div>
        </div>
        <div class="row">
          <label class="check"><input type="checkbox" [ngModel]="withEmail()" (ngModelChange)="withEmail.set($event)" />{{ i18n.t('lists.withEmail') }}</label>
          <label class="check"><input type="checkbox" [ngModel]="withBirthDate()" (ngModelChange)="withBirthDate.set($event)" />{{ i18n.t('lists.withBirthDate') }}</label>
          <label class="check"><input type="checkbox" [ngModel]="withBaptismDate()" (ngModelChange)="withBaptismDate.set($event)" />{{ i18n.t('lists.withBaptismDate') }}</label>
        </div>
        <div class="row">
          <button class="btn" type="button" [disabled]="!shown().length" (click)="print()">{{ i18n.t('lists.print') }}</button>
          <button class="btn ghost" type="button" [disabled]="!shown().length" (click)="download()">{{ i18n.t('lists.csv') }}</button>
          <button class="btn ghost" type="button" [disabled]="busy() || !data()?.frozen || !mailable()" (click)="mail()">{{ i18n.t('lists.mail') }}</button>
        </div>
        <p class="muted small">{{ i18n.t('lists.mailHint') }}</p>
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>

      @if (data()?.frozen) { <app-leader-changes [slotId]="slotId()" [canEdit]="canEdit()" /> }

      @for (g of shownGroups(); track g.leader) {
        <section class="group">
          <div class="group-head">
            <h2>{{ g.leader ?? i18n.t('lists.noLeader') }}</h2>
            <span class="muted small">{{ g.email ?? i18n.t('lists.noLeaderEmail') }}</span>
          </div>
          @for (a of g.activities; track a.id) {
            @for (s of a.sessions; track s.id) {
              <article class="card sheet">
                <p class="muted small">{{ slotName() }} · {{ i18n.t('lists.asOf', { date: i18n.date(now) }) }}</p>
                <h3>{{ a.title }}</h3>
                <p class="small">
                  {{ i18n.when(s) }}@if (s.location) { · {{ s.location }} }@if (a.leader) { · {{ i18n.t('lists.leader', { n: a.leader }) }} }
                  · {{ s.capacity ? i18n.t('lists.countCap', { n: s.people.length, c: s.capacity }) : i18n.t('lists.count', { n: s.people.length }) }}
                </p>
                @if (s.people.length) {
                  <table>
                    <thead><tr>
                      <th class="num">#</th><th>{{ i18n.t('admin.col.name') }}</th><th>{{ i18n.t('participants.parish') }}</th>
                      @if (withBirthDate()) { <th>{{ i18n.t('participants.birthDate') }}</th> }
                      @if (withBaptismDate()) { <th>{{ i18n.t('participants.baptismDate') }}</th> }
                      @if (withEmail()) { <th>{{ i18n.t('participants.email') }}</th> }
                      <th class="tick">{{ i18n.t('lists.present') }}</th>
                    </tr></thead>
                    <tbody>
                      @for (p of s.people; track $index; let i = $index) {
                        <tr>
                          <td class="num">{{ i + 1 }}</td><td>{{ p.name }}</td><td class="small">{{ p.parish ?? '' }}</td>
                          @if (withBirthDate()) { <td class="small">{{ p.birthDate ? i18n.day(p.birthDate) : '' }}</td> }
                          @if (withBaptismDate()) { <td class="small">{{ p.baptismDate ? i18n.day(p.baptismDate) : '' }}</td> }
                          @if (withEmail()) { <td class="small">{{ p.email ?? '' }}</td> }
                          <td class="tick"><span class="box"></span></td>
                        </tr>
                      }
                    </tbody>
                  </table>
                } @else { <p class="muted">{{ i18n.t('lists.empty') }}</p> }
              </article>
            }
          }
        </section>
      }
      @if (!data() && !errors().length) { <p class="muted">{{ i18n.t('common.loading') }}</p> }
    </div>
  `,
  styles: `
    .group { display: grid; gap: 12px }
    .group-head { display: flex; flex-wrap: wrap; gap: 4px 12px; align-items: baseline; margin-top: 8px }
    .group-head h2 { margin: 0 }
    .sheet { display: grid; gap: 6px }
    .sheet table { margin-top: 6px }
    .tick { width: 90px; text-align: center }
    .box { display: inline-block; width: 14px; height: 14px; border: 1px solid var(--ink); border-radius: 2px }
    @media print {
      .group { display: block; break-after: page }
      .group:last-of-type { break-after: auto }
      .sheet { border: 0; padding: 0; break-after: page }
      .sheet:last-of-type { break-after: auto }
      tr { break-inside: avoid }
    }
  `,
})
export class ListsTab implements OnInit {
  readonly slotId = input.required<string>();
  readonly slotName = input('');
  readonly canEdit = input(true);
  private changes = viewChild(LeaderChanges);

  protected i18n = inject(I18n);
  private api = inject(Api);
  private toast = inject(Toast);

  protected data = signal<LeaderLists | null>(null);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  /** null = all leaders, '' = activities without a leader. */
  protected leader = signal<string | null>(null);
  protected activityId = signal('');
  protected withEmail = signal(false);
  protected withBirthDate = signal(false);
  protected withBaptismDate = signal(false);
  protected readonly now = new Date().toISOString();

  /** Activities by leader, alphabetically; activities without a leader last. */
  protected groups = computed<LeaderGroup[]>(() => {
    const map = new Map<string, LeaderGroup>();
    for (const a of this.data()?.activities ?? []) {
      const key = a.leader ?? '';
      const g = map.get(key) ?? { leader: a.leader, email: null, activities: [] };
      g.email ??= a.leaderEmail;
      g.activities.push(a);
      map.set(key, g);
    }
    return [...map.values()].sort((x, y) =>
      x.leader === null ? 1 : y.leader === null ? -1 : x.leader.localeCompare(y.leader, this.i18n.locale(), { sensitivity: 'base' }));
  });

  protected ofLeader = computed<ListActivity[]>(() =>
    this.groups().filter(g => this.leader() === null || (g.leader ?? '') === this.leader()).flatMap(g => g.activities));

  protected shown = computed<ListActivity[]>(() =>
    this.ofLeader().filter(a => !this.activityId() || a.id === this.activityId()));

  protected shownGroups = computed<LeaderGroup[]>(() => {
    const ids = new Set(this.shown().map(a => a.id));
    return this.groups().map(g => ({ ...g, activities: g.activities.filter(a => ids.has(a.id)) })).filter(g => g.activities.length);
  });

  protected mailable = computed(() => this.shown().some(a => a.leaderEmail));

  async ngOnInit(): Promise<void> {
    try { this.data.set(await this.api.leaderLists(this.slotId())); } catch (e) { this.errors.set(this.i18n.errors(e)); }
  }

  protected print(): void { window.print(); }

  /** One mail per leader of the activities shown, with the columns ticked above. */
  protected async mail(): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const r = await this.api.leaderMails(this.slotId(), {
        activityIds: this.shown().map(a => a.id), withEmail: this.withEmail(), withBirthDate: this.withBirthDate(),
        withBaptismDate: this.withBaptismDate(),
      });
      const parts = [this.i18n.t('lists.mailed', { n: r.created })];
      if (r.withoutEmail) parts.push(this.i18n.t('lists.mailedMissing', { n: r.withoutEmail }));
      this.toast.show(parts.join(' '));
      void this.changes()?.reload();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected download(): void {
    const t = (k: string) => this.i18n.t(k);
    const head = [t('lists.activity'), t('lists.date'), t('lists.location'), t('lists.leaderCol'), t('admin.col.name'), t('participants.parish')];
    if (this.withBirthDate()) head.push(t('participants.birthDate'));
    if (this.withBaptismDate()) head.push(t('participants.baptismDate'));
    if (this.withEmail()) head.push(t('participants.email'));
    const rows = [head];
    for (const g of this.shownGroups())
      for (const a of g.activities)
        for (const s of a.sessions)
          for (const p of s.people) {
            const row = [a.title, this.i18n.when(s), s.location, a.leader ?? '', p.name, p.parish ?? ''];
            if (this.withBirthDate()) row.push(p.birthDate ? this.i18n.day(p.birthDate) : '');
            if (this.withBaptismDate()) row.push(p.baptismDate ? this.i18n.day(p.baptismDate) : '');
            if (this.withEmail()) row.push(p.email ?? '');
            rows.push(row);
          }
    saveBlob(new Blob([toCsv(rows, true)], { type: 'text/csv;charset=utf-8' }), `${this.fileName()}.csv`);
  }

  private fileName(): string {
    const one = this.shown().length === 1 ? this.shown()[0].title
      : this.leader() ? this.leader()! : this.slotName();
    return (this.i18n.t('admin.tab.lists') + ' ' + one).replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '').toLowerCase();
  }
}
