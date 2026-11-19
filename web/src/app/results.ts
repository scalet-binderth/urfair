import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Activity, Allocation, Api, ApiFailure, Metrics } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

interface Card { label: string; value: string; fcfs: string; delta: string; good: boolean; }

@Component({
  selector: 'app-results',
  imports: [FormsModule],
  template: `
    <div class="head">
      <h1>{{ i18n.t('results.title') }}</h1>
      @if (data(); as d) { <p>{{ i18n.t('results.lead', { n: d.pointBased.count, k: d.perParticipant }) }}</p> }
    </div>
    @if (error()) { <ul class="errors"><li>{{ error() }}</li></ul> }
    @if (data(); as d) {
      <div class="stack">
        <div class="cmp">
          @for (c of cards(); track c.label) {
            <div class="card">
              <div class="muted small">{{ c.label }}</div>
              <div class="big">{{ c.value }}</div>
              <div class="vs small muted">{{ i18n.t('results.fcfs') }} <b>{{ c.fcfs }}</b>
                @if (c.delta) { <span class="delta" [class.neg]="!c.good">{{ c.delta }}</span> }</div>
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
          <div class="fill">
            @for (a of activities(); track a.id) {
              <div class="act"><div class="small">{{ a.title }}</div>
                <div class="slots">
                  @for (s of a.sessions; track s.id) {
                    <div class="slot small muted">{{ s.flexible ? i18n.t('results.flexible') : shortDate(s.start) }} ·
                      <b>{{ d.sessionLoad[s.id] ?? 0 }}{{ s.capacity ? ' / ' + s.capacity : '' }}</b>
                      <div class="meter"><i [style.width.%]="s.capacity ? ((d.sessionLoad[s.id] ?? 0) / s.capacity) * 100 : 0"></i></div>
                    </div>
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
              <div class="field"><label for="q">{{ i18n.t('results.search') }}</label><input id="q" [ngModel]="query()" (ngModelChange)="query.set($event)" style="max-width:200px"></div>
              <button class="btn ghost" type="button" (click)="copy()">{{ i18n.t('results.copy') }}</button>
            </div>
          </div>
          @if (csv()) { <textarea class="csv" readonly [value]="csv()" aria-label="CSV"></textarea> }
          <div class="tbl-wrap"><table>
            <thead><tr><th>{{ i18n.t('results.col.name') }}</th><th>{{ i18n.t('results.col.assigned') }}</th><th>{{ i18n.t('results.col.satisfaction') }}</th></tr></thead>
            <tbody>
              @for (p of visible(); track p.ballotId) {
                <tr [class.me]="p.ballotId === api.myBallotId()">
                  <td>{{ p.name }} @if (!p.isSample) { <span class="tag">{{ i18n.t('results.real') }}</span> }</td>
                  <td><div class="chips">
                    @for (x of p.assignments; track x.sessionId) {
                      <span class="chip" [class.fb]="x.isFallback" [title]="i18n.when(x)">{{ x.title }}<b>{{ x.isFallback ? i18n.t('results.fallbackChip') : x.points }}</b></span>
                    }
                  </div></td>
                  <td><div class="sat"><div class="meter"><i [style.width.%]="p.satisfaction * 100"></i></div><span class="small">{{ i18n.percent(p.satisfaction) }}</span></div></td>
                </tr>
              }
            </tbody>
          </table></div>
          @if (hiddenCount() > 0) { <button class="btn ghost" type="button" (click)="showAll.set(true)">{{ i18n.t('results.showAll', { n: filtered().length }) }}</button> }
        </div>
      </div>
    } @else if (!error()) { <p class="muted">{{ i18n.t('common.loading') }}</p> }
  `,
  styles: `
    .cmp { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 12px }
    .cmp .card { display: grid; gap: 6px }
    .vs { border-top: 1px solid var(--line); padding-top: 6px }
    .vs b { color: var(--ink) }
    .delta { font-size: 12px; font-weight: 600; padding: 1px 6px; border-radius: 999px; background: var(--good-soft); color: var(--good) }
    .delta.neg { background: var(--warn-soft); color: var(--warn) }
    .how { margin: 0; padding-left: 20px; display: grid; gap: 6px; max-width: 75ch }
    .how li::marker { font-weight: 700; color: var(--accent) }
    .fill { display: grid; gap: 10px }
    .act { display: grid; grid-template-columns: minmax(0, 240px) minmax(0, 1fr); gap: 6px 14px; align-items: center }
    @media (max-width: 640px) { .act { grid-template-columns: minmax(0, 1fr) } }
    .slots { display: flex; flex-wrap: wrap; gap: 6px }
    .slot { flex: 1 1 110px; min-width: 0 }
    .slot b { color: var(--ink) }
    .slot .meter { height: 8px; margin-top: 3px }
    .chips { display: flex; flex-wrap: wrap; gap: 4px }
    .chip { font-size: 13px; padding: 2px 8px; border-radius: 6px; background: var(--bar); max-width: 100%; overflow-wrap: anywhere }
    .chip b { font-weight: 600; color: var(--accent); margin-left: 6px }
    .chip.fb { background: var(--warn-soft); color: var(--warn) }
    .chip.fb b { color: var(--warn) }
    tr.me { background: var(--accent-soft) }
    .sat { display: flex; align-items: center; gap: 8px; min-width: 120px }
    .sat .meter { flex: 1; height: 6px }
    .sat .meter i { background: var(--good) }
    .csv { width: 100%; height: 160px; font-family: ui-monospace, Consolas, monospace; font-size: 12px; background: var(--bg); color: var(--ink); border: 1px solid var(--line); border-radius: 8px; padding: 8px }
  `,
})
export class ResultsPage {
  protected i18n = inject(I18n);
  protected api = inject(Api);
  private toast = inject(Toast);

  protected data = signal<Allocation | null>(null);
  protected activities = signal<Activity[]>([]);
  protected error = signal<string | null>(null);
  protected query = signal('');
  protected showAll = signal(false);
  protected csv = signal('');

  protected cards = computed<Card[]>(() => {
    const d = this.data();
    if (!d) return [];
    const p = d.pointBased, f = d.firstComeFirstServed, pct = (x: number) => this.i18n.percent(x);
    const share = (label: string, get: (m: Metrics) => number): Card => {
      const diff = Math.round((get(p) - get(f)) * 100);
      return { label, value: pct(get(p)), fcfs: pct(get(f)), delta: diff ? `${diff > 0 ? '+' : ''}${diff} pp` : '', good: diff >= 0 };
    };
    const diffFb = p.withFallback - f.withFallback;
    return [
      share(this.i18n.t('results.topChoice'), m => m.gotTopChoice),
      share(this.i18n.t('results.avg'), m => m.averageSatisfaction),
      share(this.i18n.t('results.worst'), m => m.worstTenPercentSatisfaction),
      { label: this.i18n.t('results.fallback'), value: String(p.withFallback), fcfs: String(f.withFallback), delta: diffFb ? `${diffFb > 0 ? '+' : ''}${diffFb}` : '', good: diffFb <= 0 },
    ];
  });

  protected filtered = computed(() => {
    const q = this.query().trim().toLowerCase();
    const list = this.data()?.participants ?? [];
    if (q) return list.filter(p => p.name.toLowerCase().includes(q));
    const mine = this.api.myBallotId();
    // Own and real ballots first, then the least satisfied.
    return [...list].sort((a, b) => Number(b.ballotId === mine) - Number(a.ballotId === mine)
      || Number(a.isSample) - Number(b.isSample) || a.satisfaction - b.satisfaction);
  });
  protected visible = computed(() => (this.showAll() || this.query() ? this.filtered() : this.filtered().slice(0, 40)));
  protected hiddenCount = computed(() => this.filtered().length - this.visible().length);

  constructor() { this.load(); }

  private async load(): Promise<void> {
    try {
      const [data, activities] = await Promise.all([this.api.allocation(), this.api.activities()]);
      this.data.set(data);
      this.activities.set(activities);
    } catch (e) {
      this.error.set(e instanceof ApiFailure ? this.i18n.error(e.errors[0]) : this.i18n.t('error.generic'));
    }
  }

  protected shortDate(iso: string): string {
    return new Intl.DateTimeFormat(this.i18n.locale(), { weekday: 'short', day: 'numeric', month: 'numeric', hour: '2-digit', minute: '2-digit' }).format(new Date(iso));
  }

  protected async copy(): Promise<void> {
    const rows = ['Name;Activity;Date;Location;Points;Fallback'];
    for (const p of this.data()?.participants ?? []) {
      for (const x of p.assignments) {
        rows.push([p.name, x.title, this.i18n.when(x), x.location, x.points, x.isFallback ? 'yes' : 'no'].map(v => String(v).replace(/;/g, ',')).join(';'));
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
