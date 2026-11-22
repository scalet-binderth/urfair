import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, LeaderLists, ListActivity } from './api';
import { I18n } from './i18n';

/** Lists for the activity leaders: who comes to which date, to print (one page per date) or as CSV. */
@Component({
  selector: 'app-lists',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="stack">
      <div class="card stack no-print">
        <p class="muted small">{{ i18n.t('lists.lead') }}</p>
        @if (data()?.frozen === false) { <p class="warn small">{{ i18n.t('lists.notFrozen') }}</p> }
        <div class="row">
          <div class="field"><label for="l-act">{{ i18n.t('lists.activity') }}</label>
            <select id="l-act" [ngModel]="activityId()" (ngModelChange)="activityId.set($event)">
              <option value="">{{ i18n.t('lists.all') }}</option>
              @for (a of data()?.activities ?? []; track a.id) { <option [value]="a.id">{{ a.title }}</option> }
            </select></div>
          <label class="check"><input type="checkbox" [ngModel]="withEmail()" (ngModelChange)="withEmail.set($event)" />{{ i18n.t('lists.withEmail') }}</label>
          <button class="btn" type="button" [disabled]="!shown().length" (click)="print()">{{ i18n.t('lists.print') }}</button>
          <button class="btn ghost" type="button" [disabled]="!shown().length" (click)="download()">{{ i18n.t('lists.csv') }}</button>
        </div>
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>

      @for (a of shown(); track a.id) {
        @for (s of a.sessions; track s.id) {
          <section class="card sheet">
            <p class="muted small">{{ slotName() }} · {{ i18n.t('lists.asOf', { date: i18n.date(now) }) }}</p>
            <h2>{{ a.title }}</h2>
            <p class="small">
              {{ i18n.when(s) }}@if (s.location) { · {{ s.location }} }@if (a.leader) { · {{ i18n.t('lists.leader', { n: a.leader }) }} }
              · {{ s.capacity ? i18n.t('lists.countCap', { n: s.people.length, c: s.capacity }) : i18n.t('lists.count', { n: s.people.length }) }}
            </p>
            @if (s.people.length) {
              <table>
                <thead><tr>
                  <th class="num">#</th><th>{{ i18n.t('admin.col.name') }}</th><th>{{ i18n.t('participants.parish') }}</th>
                  @if (withEmail()) { <th>{{ i18n.t('participants.email') }}</th> }
                  <th class="tick">{{ i18n.t('lists.present') }}</th>
                </tr></thead>
                <tbody>
                  @for (p of s.people; track $index; let i = $index) {
                    <tr>
                      <td class="num">{{ i + 1 }}</td><td>{{ p.name }}</td><td class="small">{{ p.parish ?? '' }}</td>
                      @if (withEmail()) { <td class="small">{{ p.email ?? '' }}</td> }
                      <td class="tick"><span class="box"></span></td>
                    </tr>
                  }
                </tbody>
              </table>
            } @else { <p class="muted">{{ i18n.t('lists.empty') }}</p> }
          </section>
        }
      }
      @if (!data() && !errors().length) { <p class="muted">{{ i18n.t('common.loading') }}</p> }
    </div>
  `,
  styles: `
    select { padding: 9px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--bg); color: var(--ink); font-size: 15px; max-width: 100% }
    .warn { color: var(--warn) }
    .sheet { display: grid; gap: 6px }
    .sheet table { margin-top: 6px }
    .tick { width: 90px; text-align: center }
    .box { display: inline-block; width: 14px; height: 14px; border: 1px solid var(--ink); border-radius: 2px }
    @media print {
      .sheet { border: 0; padding: 0; break-after: page }
      .sheet:last-of-type { break-after: auto }
      tr { break-inside: avoid }
    }
  `,
})
export class ListsTab implements OnInit {
  readonly slotId = input.required<string>();
  readonly slotName = input('');

  protected i18n = inject(I18n);
  private api = inject(Api);

  protected data = signal<LeaderLists | null>(null);
  protected errors = signal<string[]>([]);
  protected activityId = signal('');
  protected withEmail = signal(false);
  protected readonly now = new Date().toISOString();

  protected shown = computed<ListActivity[]>(() =>
    (this.data()?.activities ?? []).filter(a => !this.activityId() || a.id === this.activityId()));

  async ngOnInit(): Promise<void> {
    try { this.data.set(await this.api.leaderLists(this.slotId())); } catch (e) { this.errors.set(this.i18n.errors(e)); }
  }

  protected print(): void { window.print(); }

  protected download(): void {
    const t = (k: string) => this.i18n.t(k);
    const head = [t('lists.activity'), t('lists.date'), t('lists.location'), t('lists.leaderCol'), t('admin.col.name'), t('participants.parish')];
    if (this.withEmail()) head.push(t('participants.email'));
    const rows = [head];
    for (const a of this.shown())
      for (const s of a.sessions)
        for (const p of s.people) {
          const row = [a.title, this.i18n.when(s), s.location, a.leader ?? '', p.name, p.parish ?? ''];
          if (this.withEmail()) row.push(p.email ?? '');
          rows.push(row);
        }
    const csv = rows.map(r => r.map(cell).join(';')).join('\r\n');
    const link = document.createElement('a');
    link.href = URL.createObjectURL(new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8' }));
    link.download = `${this.fileName()}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  }

  private fileName(): string {
    const one = this.shown().length === 1 ? this.shown()[0].title : this.slotName();
    return (this.i18n.t('admin.tab.lists') + ' ' + one).replace(/[^\p{L}\p{N}]+/gu, '-').replace(/^-|-$/g, '').toLowerCase();
  }
}

/** A CSV cell for Excel: quoted when needed; text that Excel would run as a formula is prefixed with an apostrophe. */
function cell(v: string): string {
  const safe = /^[=+\-@\t\r]/.test(v) ? `'${v}` : v;
  return /[;"\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}
