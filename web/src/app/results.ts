import { Component, computed, inject, input, signal, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Activity, Allocation, Api, ApiFailure, LogEntry, Metrics, Participant, Phase, Seat } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

interface Card {
  label: string;
  value: string;
  fcfs: string;
  delta: string;
  good: boolean;
}

@Component({
  selector: 'app-results',
  imports: [FormsModule],
  template: `
    @if (data(); as d) {
      <p class="muted lead">{{ i18n.t('results.lead', { n: d.pointBased.count, k: d.perParticipant }) }}</p>
      @if (!d.participants.length) { <p class="muted">{{ i18n.t('results.empty') }}</p> }
    }
    @if (error()) {
      <ul class="errors">
        <li>{{ error() }}</li>
      </ul>
    }
    @if (data(); as d) {
      <div class="stack">
        <div class="card stack state" [class.frozen]="d.frozenAt">
          @if (d.frozenAt) {
            <p><b>{{ i18n.t('results.frozen', { date: i18n.date(d.frozenAt), n: lockedCount() }) }}</b></p>
            <p class="muted small">{{ i18n.t(phase() === 'Allocation' ? 'results.frozenInternal' : phase() === 'Ended' ? 'results.frozenEnded' : 'results.frozenPublished') }}</p>
            @if (canEdit() && phase() === 'Allocation') {
              <p class="muted small">{{ i18n.t('results.recalculateHint') }}</p>
              <div><button class="btn" type="button" [disabled]="busy()" (click)="recalculate()">{{ i18n.t('results.recalculate') }}</button></div>
            } @else if (phase() === 'Published') {
              <p class="muted small">{{ i18n.t('results.movingUpHint') }}</p>
            }
          } @else {
            <p><b>{{ i18n.t('results.live') }}</b></p>
            <p class="muted small">{{ i18n.t('results.freezeHint') }}</p>
          }
        </div>

        <div class="cmp">
          @for (c of cards(); track c.label) {
            <div class="card">
              <div class="muted small">{{ c.label }}</div>
              <div class="big">{{ c.value }}</div>
              <div class="vs small muted">
                {{ i18n.t('results.fcfs') }} <b>{{ c.fcfs }}</b>
                @if (c.delta) {
                  <span class="delta" [class.neg]="!c.good">{{ c.delta }}</span>
                }
              </div>
            </div>
          }
        </div>

        <div class="card stack">
          <h2>{{ i18n.t('results.how') }}</h2>
          <ol class="how">
            <li>{{ i18n.t('results.how1', { k: d.perParticipant }) }}</li>
            <li>{{ i18n.t('results.how2') }}</li>
            <li>{{ i18n.t('results.how3') }}</li>
            <li>{{ i18n.t('results.how4') }}</li>
          </ol>
        </div>

        <div class="card stack">
          <h2>{{ i18n.t('results.load') }}</h2>
          <p class="muted small">{{ i18n.t('results.loadHint') }}</p>
          <div class="fill">
            @for (a of activities(); track a.id) {
              <div class="act">
                <div class="small">{{ a.title }}</div>
                <div class="slots">
                  @for (s of a.sessions; track s.id) {
                    @let names = namesBySession()[s.id] ?? [];
                    <details class="slot small muted" [class.over]="s.capacity && (d.sessionLoad[s.id] ?? 0) > s.capacity">
                      <summary [title]="names.map((n) => n.name).join(', ')">
                        {{ s.flexible ? i18n.t('results.flexible') : shortDate(s.start) }} ·
                        <b
                          >{{ d.sessionLoad[s.id] ?? 0 }}{{ s.capacity ? ' / ' + s.capacity : '' }}</b
                        >
                        <span class="meter">
                          <i
                            [style.width.%]="
                              s.capacity ? ((d.sessionLoad[s.id] ?? 0) / s.capacity) * 100 : 0
                            "
                          ></i>
                        </span>
                      </summary>
                      @if (names.length) {
                        <ul class="names">
                          @for (n of names; track $index) {
                            <li>@if (n.locked) { <span class="lock" [attr.aria-label]="i18n.t('results.locked')">🔒</span> }{{ n.name }}</li>
                          }
                        </ul>
                      } @else {
                        <p class="names">{{ i18n.t('results.nobody') }}</p>
                      }
                    </details>
                  }
                </div>
              </div>
            }
          </div>
        </div>

        <div class="card stack">
          <div class="row" style="justify-content:space-between">
            <h2>{{ i18n.t('results.people') }}</h2>
            <div class="row">
              <div class="field">
                <label for="q">{{ i18n.t('results.search') }}</label
                ><input
                  id="q"
                  [ngModel]="query()"
                  (ngModelChange)="query.set($event)"
                  style="max-width:200px"
                />
              </div>
              <button class="btn ghost" type="button" (click)="copy()">
                {{ i18n.t('results.copy') }}
              </button>
            </div>
          </div>
          @if (csv()) {
            <textarea class="csv" readonly [value]="csv()" aria-label="CSV"></textarea>
          }
          <div class="tbl-wrap">
            <table>
              <thead>
                <tr>
                  <th>{{ i18n.t('results.col.name') }}</th>
                  <th>{{ i18n.t('results.col.assigned') }}</th>
                  <th>{{ i18n.t('results.col.satisfaction') }}</th>
                  @if (editable()) { <th><span class="sr">{{ i18n.t('common.edit') }}</span></th> }
                </tr>
              </thead>
              <tbody>
                @for (p of visible(); track p.ballotId) {
                  <tr>
                    <td>
                      {{ p.name }}
                      @if (!p.isSample) {
                        <span class="tag">{{ i18n.t('results.real') }}</span>
                      }
                    </td>
                    <td>
                      @if (editingId() === p.ballotId) {
                        <div class="seats">
                          <p class="muted small">{{ pointsText(p) }}</p>
                          @for (seat of seats; track $index; let i = $index) {
                            <div class="row seat">
                              <select [attr.aria-label]="i18n.t('results.seat')" [ngModel]="seat.sessionId" (ngModelChange)="seat.sessionId = $event; seat.locked = true">
                                @for (o of editOptions; track o.id) { <option [value]="o.id">{{ o.label }}</option> }
                              </select>
                              <label class="check small"><input type="checkbox" [(ngModel)]="seat.locked" />{{ i18n.t('results.lock') }}</label>
                              <button class="btn ghost small" type="button" [attr.aria-label]="i18n.t('results.removeSeat')" (click)="seats.splice(i, 1)">✕</button>
                            </div>
                          }
                          @if (p.original.length) {
                            <p class="muted small">{{ i18n.t('results.original', { seats: seatText(p.original) }) }}</p>
                          }
                          <p class="muted small">{{ i18n.t('results.editHint') }}</p>
                          <div class="row">
                            <button class="btn ghost small" type="button" (click)="addSeat()">{{ i18n.t('results.addSeat') }}</button>
                            @if (p.original.length) {
                              <button class="btn ghost small" type="button" (click)="restore(p)">{{ i18n.t('results.restore') }}</button>
                            }
                            <button class="btn small" type="button" [disabled]="busy()" (click)="saveSeats(p.ballotId)">{{ i18n.t('common.save') }}</button>
                            <button class="btn ghost small" type="button" (click)="editingId.set(null)">{{ i18n.t('common.cancel') }}</button>
                          </div>
                        </div>
                      } @else {
                      <div class="chips">
                        @for (x of p.assignments; track x.sessionId) {
                          <span class="chip" [class.fb]="x.isFallback" [title]="i18n.when(x)"
                            >@if (x.locked) { <span class="lock" [title]="i18n.t('results.locked')" [attr.aria-label]="i18n.t('results.locked')">🔒</span> }{{ x.title
                            }}<b>{{
                              x.isFallback ? i18n.t('results.fallbackChip') : x.points
                            }}</b></span
                          >
                        }
                      </div>
                      @if (changed(p)) {
                        <p class="muted small orig">{{ i18n.t('results.original', { seats: seatText(p.original) }) }}</p>
                      }
                      @if (p.waitlist.length || p.declined.length) {
                        <p class="muted small orig">{{ waitText(p) }}</p>
                      }
                      }
                    </td>
                    <td>
                      <div class="sat">
                        <div class="meter"><i [style.width.%]="p.satisfaction * 100"></i></div>
                        <span class="small">{{ i18n.percent(p.satisfaction) }}</span>
                      </div>
                    </td>
                    @if (editable()) {
                      <td>@if (editingId() !== p.ballotId) {
                        <button class="btn ghost small" type="button" (click)="edit(p)">{{ i18n.t('common.edit') }}</button>
                      }</td>
                    }
                  </tr>
                }
              </tbody>
            </table>
          </div>
          @if (hiddenCount() > 0) {
            <button class="btn ghost" type="button" (click)="showAll.set(true)">
              {{ i18n.t('results.showAll', { n: filtered().length }) }}
            </button>
          }
        </div>

        @if (log().length) {
          <div class="card stack">
            <h2>{{ i18n.t('log.title') }}</h2>
            <ul class="log">
              @for (l of log(); track $index) {
                <li>
                  <span class="muted small">{{ i18n.date(l.at) }} · {{ l.actor }}</span><br />
                  {{ logText(l) }}
                </li>
              }
            </ul>
          </div>
        }
      </div>
    } @else if (!error()) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .state {
      border-left: 4px solid var(--warn);
    }
    .state.frozen {
      border-left-color: var(--good);
    }
    .state p {
      margin: 0;
      max-width: 75ch;
    }
    .btn.small {
      padding: 6px 12px;
      font-size: 14px;
    }
    .btn.danger {
      background: var(--warn);
      border-color: var(--warn);
      color: var(--surface);
    }
    .slot.over b {
      color: var(--warn);
    }
    .slot .meter {
      overflow: hidden;
    }
    .slot.over .meter i {
      background: var(--warn);
    }
    .seats {
      display: grid;
      gap: 6px;
    }
    .seats p,
    p.orig {
      margin: 0;
    }
    p.orig {
      margin-top: 4px;
    }
    .log {
      margin: 0;
      padding-left: 18px;
      max-height: 360px;
      overflow: auto;
      display: grid;
      gap: 6px;
    }
    .seat {
      align-items: center;
      flex-wrap: nowrap;
    }
    .seat select {
      min-width: 0;
      max-width: 420px;
      flex: 1;
      padding: 6px 8px;
      border: 1px solid var(--line);
      border-radius: 8px;
      background: var(--bg);
      color: var(--ink);
    }
    .lock {
      margin-right: 4px;
      font-size: 11px;
    }
    .sr {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0 0 0 0);
    }
    .lead {
      margin-bottom: 16px;
      max-width: 75ch;
    }
    .cmp {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
      gap: 12px;
    }
    .cmp .card {
      display: grid;
      gap: 6px;
    }
    .vs {
      border-top: 1px solid var(--line);
      padding-top: 6px;
    }
    .vs b {
      color: var(--ink);
    }
    .delta {
      font-size: 12px;
      font-weight: 600;
      padding: 1px 6px;
      border-radius: 999px;
      background: var(--good-soft);
      color: var(--good);
    }
    .delta.neg {
      background: var(--warn-soft);
      color: var(--warn);
    }
    .how {
      margin: 0;
      padding-left: 20px;
      display: grid;
      gap: 6px;
      max-width: 75ch;
    }
    .how li::marker {
      font-weight: 700;
      color: var(--accent);
    }
    .fill {
      display: grid;
      gap: 10px;
    }
    .act {
      display: grid;
      grid-template-columns: minmax(0, 240px) minmax(0, 1fr);
      gap: 6px 14px;
      align-items: center;
    }
    @media (max-width: 640px) {
      .act {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .slots {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }
    .slot {
      flex: 1 1 110px;
      min-width: 0;
    }
    .slot b {
      color: var(--ink);
    }
    .slot .meter {
      display: block;
      height: 8px;
      margin-top: 3px;
    }
    .slot summary {
      cursor: pointer;
      list-style: none;
    }
    .slot summary::-webkit-details-marker {
      display: none;
    }
    .slot[open] summary b {
      text-decoration: underline;
    }
    .names {
      margin: 6px 0 0;
      padding-left: 18px;
      color: var(--ink);
    }
    p.names {
      padding-left: 0;
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 4px;
    }
    .chip {
      font-size: 13px;
      padding: 2px 8px;
      border-radius: 6px;
      background: var(--bar);
      max-width: 100%;
      overflow-wrap: anywhere;
    }
    .chip b {
      font-weight: 600;
      color: var(--accent);
      margin-left: 6px;
    }
    .chip.fb {
      background: var(--warn-soft);
      color: var(--warn);
    }
    .chip.fb b {
      color: var(--warn);
    }
    tr.me {
      background: var(--accent-soft);
    }
    .sat {
      display: flex;
      align-items: center;
      gap: 8px;
      min-width: 120px;
    }
    .sat .meter {
      flex: 1;
      height: 6px;
    }
    .sat .meter i {
      background: var(--good);
    }
    .csv {
      width: 100%;
      height: 160px;
      font-family: ui-monospace, Consolas, monospace;
      font-size: 12px;
      background: var(--bg);
      color: var(--ink);
      border: 1px solid var(--line);
      border-radius: 8px;
      padding: 8px;
    }
  `,
})
export class ResultsPage implements OnInit {
  readonly slotId = input.required<string>();
  /** Phase of the vote: from the allocation phase on the allocation is frozen. */
  readonly phase = input<Phase>('Allocation');
  readonly canEdit = input(true);
  protected i18n = inject(I18n);
  protected api = inject(Api);
  private toast = inject(Toast);

  protected data = signal<Allocation | null>(null);
  protected activities = signal<Activity[]>([]);
  protected error = signal<string | null>(null);
  protected query = signal('');
  protected showAll = signal(false);
  protected csv = signal('');
  protected busy = signal(false);
  protected editingId = signal<string | null>(null);
  protected seats: Seat[] = [];
  /** Session choices for the person being edited, with their points and original seats. */
  protected editOptions: { id: string; activityId: string; label: string }[] = [];
  protected log = signal<LogEntry[]>([]);

  protected editable = computed(() => !!this.data()?.frozenAt && this.canEdit());
  protected lockedCount = computed(() =>
    (this.data()?.participants ?? []).reduce((n, p) => n + p.assignments.filter((x) => x.locked).length, 0),
  );
  /** Who is assigned to each session, sorted by name (shown when a session in the utilisation card is opened). */
  protected namesBySession = computed(() => {
    const map: Record<string, { name: string; locked: boolean }[]> = {};
    for (const p of this.data()?.participants ?? [])
      for (const x of p.assignments) (map[x.sessionId] ??= []).push({ name: p.name, locked: x.locked ?? false });
    for (const list of Object.values(map)) list.sort((a, b) => a.name.localeCompare(b.name, this.i18n.locale()));
    return map;
  });
  /** Every session of the vote with its current load, for moving people by hand. */
  protected sessionOptions = computed(() => {
    const load = this.data()?.sessionLoad ?? {};
    return this.activities().flatMap((a) =>
      a.sessions.map((s) => ({
        id: s.id,
        activityId: a.id,
        label: `${a.title} · ${this.i18n.when(s)} · ${load[s.id] ?? 0}${s.capacity ? ' / ' + s.capacity : ''}`,
      })),
    );
  });

  protected cards = computed<Card[]>(() => {
    const d = this.data();
    if (!d) return [];
    const p = d.pointBased,
      f = d.firstComeFirstServed,
      pct = (x: number) => this.i18n.percent(x);
    const share = (label: string, get: (m: Metrics) => number): Card => {
      const diff = Math.round((get(p) - get(f)) * 100);
      return {
        label,
        value: pct(get(p)),
        fcfs: pct(get(f)),
        delta: diff ? `${diff > 0 ? '+' : ''}${diff} pp` : '',
        good: diff >= 0,
      };
    };
    const diffFb = p.withFallback - f.withFallback;
    return [
      share(this.i18n.t('results.topChoice'), (m) => m.gotTopChoice),
      share(this.i18n.t('results.avg'), (m) => m.averageSatisfaction),
      share(this.i18n.t('results.worst'), (m) => m.worstTenPercentSatisfaction),
      {
        label: this.i18n.t('results.fallback'),
        value: String(p.withFallback),
        fcfs: String(f.withFallback),
        delta: diffFb ? `${diffFb > 0 ? '+' : ''}${diffFb}` : '',
        good: diffFb <= 0,
      },
    ];
  });

  protected filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    const list = this.data()?.participants ?? [];
    if (q) return list.filter((p) => p.name.toLowerCase().includes(q));
    // Real ballots first, then the least satisfied.
    return [...list].sort(
      (a, b) =>
        Number(a.isSample) - Number(b.isSample) ||
        a.satisfaction - b.satisfaction,
    );
  });
  protected visible = computed(() =>
    this.showAll() || this.query() ? this.filtered() : this.filtered().slice(0, 40),
  );
  protected hiddenCount = computed(() => this.filtered().length - this.visible().length);

  async ngOnInit(): Promise<void> {
    try {
      const [data, activities] = await Promise.all([this.api.allocation(this.slotId()), this.api.adminActivities(this.slotId())]);
      this.data.set(data);
      this.activities.set(activities);
      await this.loadLog();
    } catch (e) {
      this.error.set(
        e instanceof ApiFailure ? this.i18n.error(e.errors[0]) : this.i18n.t('error.generic'),
      );
    }
  }

  protected shortDate(iso: string): string {
    return new Intl.DateTimeFormat(this.i18n.locale(), {
      weekday: 'short',
      day: 'numeric',
      month: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    }).format(new Date(iso));
  }

  protected recalculate(): Promise<void> {
    return this.act(() => this.api.recalculateAllocation(this.slotId()), 'results.recalculated');
  }

  /** Phase changes name the phases in the current language. */
  protected logText(l: LogEntry): string {
    const name = (x: string | null | undefined) => (l.kind === 'phase' && x ? this.i18n.t('phase.' + x) : x || '–');
    return this.i18n.t('log.' + l.kind, { person: l.person ?? '', before: name(l.before), after: name(l.after) });
  }

  protected edit(p: Participant): void {
    this.seats = p.assignments.map((x) => ({ sessionId: x.sessionId, locked: !!x.locked }));
    const original = new Set(p.original);
    this.editOptions = this.sessionOptions().map((o) => {
      const pts = p.points[o.activityId] ?? 0;
      const extra = [pts ? this.i18n.t('results.pts', { n: pts }) : '', original.has(o.id) ? this.i18n.t('results.originalMark') : '']
        .filter((x) => x).join(' · ');
      return { ...o, label: extra ? `${o.label} · ${extra}` : o.label };
    });
    this.editingId.set(p.ballotId);
  }

  /** Puts the seats back as calculated when freezing (not fixed); saving makes it count. */
  protected restore(p: Participant): void {
    this.seats = p.original.map((sessionId) => ({ sessionId, locked: false }));
  }

  /** True if the seats differ from the original (only once frozen). */
  protected changed(p: Participant): boolean {
    if (!this.data()?.frozenAt) return false;
    const now = p.assignments.map((x) => x.sessionId);
    return now.length !== p.original.length || now.some((id) => !p.original.includes(id));
  }

  /** "Title (date)" for session ids, the date only where an activity has several sessions. */
  protected seatText(sessionIds: string[]): string {
    if (!sessionIds.length) return '–';
    return this.activities()
      .flatMap((a) => a.sessions.filter((s) => sessionIds.includes(s.id)).map((s) => ({ a, s })))
      .sort((x, y) => x.s.start.localeCompare(y.s.start))
      .map(({ a, s }) => (a.sessions.length > 1 ? `${a.title} (${this.i18n.when(s)})` : a.title))
      .join(', ');
  }

  /** The person's points, highest first. */
  protected pointsText(p: Participant): string {
    const list = this.activities()
      .map((a) => ({ title: a.title, n: p.points[a.id] ?? 0 }))
      .filter((x) => x.n > 0)
      .sort((x, y) => y.n - x.n)
      .map((x) => `${x.title} ${x.n}`);
    return list.length ? this.i18n.t('results.pointsOf', { list: list.join(' · ') }) : this.i18n.t('results.noPoints');
  }

  /** Waiting-list ticks and seats given back, by title. */
  protected waitText(p: Participant): string {
    const titles = (ids: string[]) => this.activities().filter((a) => ids.includes(a.id)).map((a) => a.title).join(', ');
    return [
      p.waitlist.length ? this.i18n.t('results.waitlist', { list: titles(p.waitlist) }) : '',
      p.declined.length ? this.i18n.t('results.declined', { list: titles(p.declined) }) : '',
    ].filter((x) => x).join(' · ');
  }

  private async loadLog(): Promise<void> {
    try {
      this.log.set(await this.api.allocationLog(this.slotId()));
    } catch {
      this.log.set([]);
    }
  }

  /** Adds a seat in the first activity the person does not have yet. */
  protected addSeat(): void {
    const options = this.editOptions;
    const activityOf = new Map(options.map((o) => [o.id, o.activityId]));
    const used = new Set(this.seats.map((s) => activityOf.get(s.sessionId)));
    const next = options.find((o) => !used.has(o.activityId)) ?? options[0];
    if (next) this.seats.push({ sessionId: next.id, locked: true });
  }

  protected saveSeats(ballotId: string): Promise<void> {
    return this.act(() => this.api.setSeats(this.slotId(), ballotId, this.seats), 'common.saved');
  }

  private async act(call: () => Promise<Allocation>, message: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      this.data.set(await call());
      this.editingId.set(null);
      this.toast.show(this.i18n.t(message));
      await this.loadLog();
    } catch (e) {
      this.error.set(this.i18n.errors(e).join(' '));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(): Promise<void> {
    const rows = ['Name;Activity;Date;Location;Points;Fallback'];
    for (const p of this.data()?.participants ?? []) {
      for (const x of p.assignments) {
        rows.push(
          [p.name, x.title, this.i18n.when(x), x.location, x.points, x.isFallback ? 'yes' : 'no']
            .map((v) => String(v).replace(/;/g, ','))
            .join(';'),
        );
      }
    }
    const text = rows.join('\n');
    try {
      await navigator.clipboard.writeText(text);
      this.toast.show(this.i18n.t('results.copied'));
    } catch {
      this.csv.set(text);
      this.toast.show(this.i18n.t('results.copyFallback'));
    }
  }
}
