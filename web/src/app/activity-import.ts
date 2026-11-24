import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Activity, Api, Group, Leader } from './api';
import { I18n } from './i18n';

/** Import activities from an Excel/CSV list: download the current list as template, upload, preview, take over. */
@Component({
  selector: 'app-activity-import',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="card stack">
      <h2>{{ i18n.t('import.title') }}</h2>
      <p class="muted small">{{ i18n.t('import.lead') }}</p>
      <div class="row">
        <button class="btn ghost" type="button" (click)="download()">{{ i18n.t('import.template') }}</button>
        <label class="btn ghost file">{{ i18n.t('import.choose') }}
          <input type="file" accept=".csv,text/csv" (change)="choose($event)" /></label>
        @if (fileName()) { <span class="small muted">{{ fileName() }}</span> }
      </div>

      @if (errors().length) {
        <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul>
      }
      @if (preview().length) {
        <p class="small">{{ i18n.t('import.found', { n: preview().length, s: sessionCount() }) }}</p>
        <div class="tbl-wrap"><table>
          <thead><tr>
            <th>{{ i18n.t('admin.col.activity') }}</th><th>{{ i18n.t('editor.group') }}</th><th>{{ i18n.t('editor.leader') }}</th>
            <th class="num">{{ i18n.t('editor.sessions') }}</th><th class="num">{{ i18n.t('admin.col.seats') }}</th>
          </tr></thead>
          <tbody>
            @for (a of preview(); track a.id) {
              <tr>
                <td>{{ a.title }}@if (a.restrictedTo) { <span class="tag">{{ i18n.t('common.only.' + a.restrictedTo) }}</span> }</td>
                <td>@if (groupOf(a); as g) { <span class="grp" [attr.data-color]="g.color"></span>{{ g.name }} }</td>
                <td class="small">{{ a.leader ?? '' }}</td>
                <td class="num">{{ a.sessions.length }}</td>
                <td class="num">{{ a.seats ?? i18n.t('common.unlimited') }}</td>
              </tr>
            }
          </tbody>
        </table></div>
        @if (!errors().length && leaders().length) {
          <div class="stack leaders">
            <h3>{{ i18n.t('import.leaders') }}</h3>
            @if (missing()) { <p class="small warn">{{ i18n.t(missing() === 1 ? 'import.leadersMissingOne' : 'import.leadersMissing', { n: missing() }) }}</p> }
            @else { <p class="muted small">{{ i18n.t('import.leadersLead') }}</p> }
            @for (l of leaders(); track key(l)) {
              <div class="row leader">
                <div class="who"><strong>{{ l.name ?? i18n.t('leaders.organiser') }}</strong>@if (l.isOrganiser) { <span class="tag">{{ i18n.t('leaders.organiserTag') }}</span> }
                  <div class="muted small">{{ titles(l) }}</div></div>
                <input type="email" maxlength="200" [attr.aria-label]="i18n.t('leaders.email')" [class.need]="!emails[key(l)]?.trim()"
                  [placeholder]="i18n.t('leaders.emailRequired')" [(ngModel)]="emails[key(l)]" />
              </div>
            }
          </div>
        }
        @if (!errors().length) {
          <label class="check"><input type="checkbox" [(ngModel)]="replace" />{{ i18n.t('import.replace') }}</label>
          @if (replace) { <p class="small warn">{{ i18n.t('import.replaceWarn') }}</p> }
          <div class="row">
            <button class="btn" type="button" [disabled]="busy() || missing() > 0" (click)="commit()">{{ i18n.t('import.commit', { n: preview().length }) }}</button>
            <button class="btn ghost" type="button" (click)="reset()">{{ i18n.t('common.cancel') }}</button>
          </div>
        }
      }
    </div>
  `,
  styles: `
    .file { position: relative; overflow: hidden }
    .file input { position: absolute; inset: 0; opacity: 0; cursor: pointer }
    .warn { color: var(--warn) }
    .leaders h3 { margin: 0 }
    .leader { align-items: center; justify-content: space-between; border-top: 1px solid var(--line); padding-top: 8px }
    .leader .who { flex: 1 1 240px; min-width: 0 }
    .leader input { flex: 1 1 240px; max-width: 340px }
    .leader input.need { border-color: var(--warn) }
  `,
})
export class ActivityImport {
  readonly slotId = input.required<string>();
  readonly groups = input<Group[]>([]);
  readonly imported = output<void>();

  protected i18n = inject(I18n);
  private api = inject(Api);
  protected fileName = signal('');
  protected preview = signal<Activity[]>([]);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected replace = false;
  /** Who leads the imported activities; the organiser (no leader) has the key ''. */
  protected leaders = signal<Leader[]>([]);
  /** Addresses by leader key, prefilled from the CSV or the vote and editable. */
  protected emails: Record<string, string> = {};
  private csv = '';

  protected key(l: Leader): string { return l.isOrganiser ? '' : (l.name ?? '').toLowerCase(); }
  protected titles(l: Leader): string { return l.activities.map(a => a.title).join(', '); }
  protected missing(): number { return this.leaders().filter(l => !this.emails[this.key(l)]?.trim()).length; }

  protected sessionCount(): number { return this.preview().reduce((n, a) => n + a.sessions.length, 0); }
  protected groupOf(a: Activity): Group | undefined { return this.groups().find(g => g.id === a.groupId); }

  protected async download(): Promise<void> {
    try {
      const blob = await this.api.exportActivities(this.slotId());
      const link = document.createElement('a');
      link.href = URL.createObjectURL(blob);
      link.download = 'veranstaltungen.csv';
      link.click();
      URL.revokeObjectURL(link.href);
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    }
  }

  protected async choose(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = ''; // allow choosing the same file again after editing it
    if (!file) return;
    this.fileName.set(file.name);
    this.csv = decode(await file.arrayBuffer());
    await this.send(false);
  }

  protected async commit(): Promise<void> { await this.send(true); }

  protected reset(): void {
    this.preview.set([]);
    this.errors.set([]);
    this.fileName.set('');
    this.replace = false;
    this.leaders.set([]);
    this.emails = {};
    this.csv = '';
  }

  private async send(commit: boolean): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const leaderEmails: Record<string, string> = {};
      for (const l of this.leaders()) if (!l.isOrganiser && l.name && this.emails[this.key(l)]?.trim()) leaderEmails[l.name] = this.emails[this.key(l)].trim();
      const organiserEmail = this.emails['']?.trim() || null;
      const res = await this.api.importActivities(this.slotId(), { csv: this.csv, replace: this.replace, commit, leaderEmails, organiserEmail });
      this.preview.set(res.activities);
      this.leaders.set(res.leaders ?? []);
      for (const l of res.leaders ?? []) if (!this.emails[this.key(l)]?.trim()) this.emails[this.key(l)] = l.email ?? '';
      this.errors.set(res.errors.map(e => this.i18n.error(e)));
      if (!res.activities.length && !res.errors.length) this.errors.set([this.i18n.t('error.csvEmpty')]);
      if (res.saved) {
        this.reset();
        this.imported.emit();
      }
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Excel saves "CSV UTF-8" or, as plain "CSV", Windows-1252; try UTF-8 first. */
function decode(buffer: ArrayBuffer): string {
  try { return new TextDecoder('utf-8', { fatal: true }).decode(buffer); } catch { return new TextDecoder('windows-1252').decode(buffer); }
}
