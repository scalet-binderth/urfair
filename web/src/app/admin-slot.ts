import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ActivityEditor } from './activity-editor';
import { ActivityImport } from './activity-import';
import { ListsTab } from './lists-tab';
import { MailboxTab } from './mailbox-tab';
import { ParticipantsTab } from './participants-tab';
import { Activity, Api, ApiFailure, Demand, GROUP_COLORS, Group, PHASES, Phase, Rules, Slot, VISIBILITIES, Visibility, fromLocalInput, toLocalInput } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';
import { AppInfo } from './info';
import { ResultsPage } from './results';
import { Toast } from './toast';

type Tab = 'settings' | 'activities' | 'participants' | 'mail' | 'demand' | 'results' | 'lists' | 'demo';

/** Organisation view of one FairSlot. */
@Component({
  selector: 'app-admin-slot',
  imports: [FormsModule, RouterLink, ActivityEditor, ActivityImport, ListsTab, MailboxTab, ParticipantsTab, ResultsPage],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <p class="no-print"><a routerLink="/admin">{{ i18n.t('admin.back') }}</a></p>
    @if (slot(); as s) {
      <div class="head no-print">
        <h1>{{ s.name }} <span class="phase" [attr.data-phase]="s.phase">{{ i18n.t('phase.' + s.phase) }}</span></h1>
        <div class="row link">
          <span class="muted small">{{ i18n.t('admin.participantLink') }}:</span>
          <a [routerLink]="['/s', s.id]" class="small">{{ participantUrl() }}</a>
          <button class="btn ghost small" type="button" (click)="copyLink()">{{ i18n.t('admin.copyLink') }}</button>
        </div>
      </div>

      @if (!canEdit()) { <p class="card small readonly no-print">{{ i18n.t('admin.readOnly') }}</p> }
      @else if (s.phase === 'Ended') { <p class="card small readonly no-print">{{ i18n.t('admin.ended') }}@if (s.deleteOn) { {{ i18n.t('admin.deleteOn', { date: i18n.day(s.deleteOn) }) }} }</p> }
      <nav class="tabs no-print" role="tablist">
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
              @if (s.phase === 'Allocation' && (form.phase === 'Open' || form.phase === 'Draft')) { <p class="warn small">{{ i18n.t('admin.reopenWarn') }}</p> }
              @if (form.phase === 'Ended') { <p class="muted small">{{ i18n.t('admin.endedHint') }}</p> }
              <label class="check"><input type="checkbox" [(ngModel)]="form.showInterim" />{{ i18n.t('admin.showInterim') }}</label>
              <p class="muted small">{{ i18n.t('admin.showInterimHint') }}</p>
              <div class="row">
                <div class="field grow"><label for="s-parish">{{ i18n.t('admin.parish') }}</label>
                  <select id="s-parish" [(ngModel)]="form.parish">
                    <option value="">{{ i18n.t('admin.parishNone') }}</option>
                    @for (p of info.parishes(); track p) { <option [value]="p">{{ p }}</option> }
                    @if (form.parish && !info.parishes().includes(form.parish)) { <option [value]="form.parish">{{ form.parish }}</option> }
                  </select></div>
                <div class="field grow"><label for="s-organiser">{{ i18n.t('admin.organiser') }}</label><input id="s-organiser" maxlength="120" [(ngModel)]="form.organiser" /></div>
                <div class="field grow"><label for="s-organiser-email">{{ i18n.t('admin.organiserEmail') }}</label><input id="s-organiser-email" type="email" maxlength="200" [(ngModel)]="form.organiserEmail" /></div>
              </div>
              <p class="muted small">{{ i18n.t('admin.parishHint') }} {{ i18n.t('admin.organiserEmailHint') }}</p>
              <div class="field"><label for="s-visibility">{{ i18n.t('admin.visibility') }}</label>
                <select id="s-visibility" [(ngModel)]="form.visibility">
                  @for (v of visibilities(); track v) { <option [value]="v">{{ i18n.t('visibility.' + v) }}</option> }
                </select></div>
              <p class="muted small">{{ i18n.t('visibility.hint.' + form.visibility) }}</p>
              <label class="check"><input type="checkbox" [(ngModel)]="form.openToAll" (ngModelChange)="$event && loadOtherSlots()" />{{ i18n.t('admin.openToAll') }}</label>
              <p class="muted small">{{ i18n.t('admin.openToAllHint') }}</p>
              @if (form.openToAll) {
                <div class="field"><label for="s-excluded">{{ i18n.t('admin.excluded') }}</label><textarea id="s-excluded" rows="4" [(ngModel)]="form.excluded"></textarea></div>
                <select [(ngModel)]="excludeSource" (ngModelChange)="excludeFrom($event)" [attr.aria-label]="i18n.t('admin.excludeFrom')">
                  <option [ngValue]="null">{{ i18n.t('admin.excludeFrom') }}</option>
                  @for (o of otherSlots(); track o.id) { <option [ngValue]="o.id">{{ o.name }}</option> }
                </select>
                <p class="muted small">{{ i18n.t('admin.excludedHint') }}</p>
              }
              <h2>{{ i18n.t('admin.rules') }}</h2>
              <div class="rules">
                <div class="field"><label for="r-budget">{{ i18n.t('admin.budget') }}</label><input id="r-budget" type="number" step="5" [(ngModel)]="form.rules.pointBudget" /></div>
                <div class="field"><label for="r-min">{{ i18n.t('admin.min') }}</label><input id="r-min" type="number" step="1" [(ngModel)]="form.rules.minActivities" /></div>
                <div class="field"><label for="r-max">{{ i18n.t('admin.max') }}</label><input id="r-max" type="number" step="5" [(ngModel)]="form.rules.maxPerActivity" /></div>
                <div class="field"><label for="r-k">{{ i18n.t('admin.k') }}</label>
                  @if (formGroups.length) { <input id="r-k" type="number" [value]="groupSum()" disabled /> }
                  @else { <input id="r-k" type="number" step="1" [(ngModel)]="form.rules.perParticipant" /> }</div>
              </div>
              <h3>{{ i18n.t('admin.groups') }}</h3>
              <p class="muted small">{{ i18n.t('admin.groupsHint') }}</p>
              @for (g of formGroups; track $index; let i = $index) {
                <div class="row group-row">
                  <span class="grp" [attr.data-color]="g.color"></span>
                  <div class="field grow"><label [for]="'g-name-' + i">{{ i18n.t('admin.groupName') }}</label><input [id]="'g-name-' + i" [(ngModel)]="g.name" /></div>
                  <div class="field"><label [for]="'g-k-' + i">{{ i18n.t('admin.groupK') }}</label><input [id]="'g-k-' + i" type="number" min="1" [(ngModel)]="g.perParticipant" /></div>
                  <div class="field"><label [for]="'g-min-' + i">{{ i18n.t('admin.groupMin') }}</label><input [id]="'g-min-' + i" type="number" min="1" [(ngModel)]="g.minActivities" /></div>
                  <div class="field"><label [for]="'g-color-' + i">{{ i18n.t('admin.groupColor') }}</label>
                    <select [id]="'g-color-' + i" [(ngModel)]="g.color">@for (c of colors; track c) { <option [value]="c">{{ i18n.t('color.' + c) }}</option> }</select></div>
                  <button class="btn ghost small" type="button" [attr.aria-label]="i18n.t('admin.removeGroup')" (click)="formGroups.splice(i, 1)">✕</button>
                </div>
              }
              <div><button class="btn ghost" type="button" (click)="addGroup()">{{ i18n.t('admin.addGroup') }}</button></div>
              <h3>{{ i18n.t('admin.staffing') }}</h3>
              <label class="check"><input type="checkbox" [(ngModel)]="form.rules.requiredStaffing" />{{ i18n.t('admin.requiredStaffing') }}</label>
              <p class="muted small">{{ i18n.t('admin.requiredStaffingHint') }}</p>
              <div class="field"><label for="r-extra">{{ i18n.t('admin.maxExtra') }}</label><input id="r-extra" type="number" min="0" max="10" step="1" [(ngModel)]="form.rules.maxExtra" /></div>
              <p class="muted small">{{ i18n.t('admin.maxExtraHint') }}</p>
              <div class="field"><label for="r-friends">{{ i18n.t('admin.friends') }}</label>
                <select id="r-friends" [(ngModel)]="form.rules.friends">
                  @for (n of friendSizes; track n) { <option [ngValue]="n">{{ n === 0 ? i18n.t('admin.friendsOff') : n === 2 ? i18n.t('admin.friendsPairs') : i18n.t('admin.friendsUpTo', { n }) }}</option> }
                </select>
              </div>
              <p class="muted small">{{ i18n.t('admin.friendsHint') }}</p>
              <p class="muted small">{{ i18n.t('admin.rulesNote') }}</p>
              @if (canEdit()) { <div><button class="btn" type="button" [disabled]="busy()" (click)="saveSettings()">{{ i18n.t('common.save') }}</button></div> }
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
              <app-activity-editor [slotId]="s.id" [groups]="s.rules.groups ?? []" (saved)="afterSave()" (cancelled)="editing.set(null)" />
            } @else {
              <div class="row">
                <button class="btn" type="button" [hidden]="!changeable()" (click)="editing.set('new')">{{ i18n.t('admin.addActivity') }}</button>
                <button class="btn ghost" type="button" [hidden]="!changeable()" (click)="showImport.set(!showImport())">{{ i18n.t('import.open') }}</button>
              </div>
            }
            @if (showImport()) { <app-activity-import [slotId]="s.id" [groups]="s.rules.groups ?? []" (imported)="afterImport()" /> }
            @for (a of activities(); track a.id) {
              @if (editing() === a.id) {
                <app-activity-editor [slotId]="s.id" [groups]="s.rules.groups ?? []" [activity]="a" (saved)="afterSave()" (cancelled)="editing.set(null)" />
              } @else {
                <div class="card act">
                  <div class="info">
                    <h3>@if (groupOf(a); as g) { <span class="grp" [attr.data-color]="g.color" [title]="g.name"></span> }{{ a.title }}
                      @if (groupOf(a); as g) { <span class="tag">{{ g.name }}</span> }
                      @if (a.restrictedTo) { <span class="tag">{{ i18n.t('common.only.' + a.restrictedTo) }}</span> }
                      <span class="tag">{{ a.seats ?? i18n.t('common.unlimited') }} {{ a.seats ? i18n.t('admin.col.seats') : '' }}</span>
                    </h3>
                    @if (a.parish || a.leader) { <div class="muted small">{{ i18n.byLine(a.parish, a.leader, true) }}</div> }
                    @if (a.description) { <p class="muted small desc">{{ a.description }}</p> }
                    <ul>@for (x of a.sessions; track x.id) { <li>{{ i18n.when(x) }}@if (x.location) { · {{ x.location }} }@if (x.capacity) { · {{ i18n.t('common.seats', { n: x.capacity }) }} }</li> }</ul>
                  </div>
                  <div class="row actions">
                    @if (confirmDelete() === a.id) {
                      <span class="small">{{ i18n.t('common.reallyDelete') }}</span>
                      <button class="btn danger small" type="button" (click)="deleteActivity(a.id)">{{ i18n.t('common.delete') }}</button>
                      <button class="btn ghost small" type="button" (click)="confirmDelete.set(null)">{{ i18n.t('common.cancel') }}</button>
                    } @else {
                      <button class="btn ghost small" type="button" [hidden]="!changeable()" (click)="editing.set(a.id)">{{ i18n.t('common.edit') }}</button>
                      <button class="btn ghost small" type="button" [hidden]="!changeable()" (click)="confirmDelete.set(a.id)">{{ i18n.t('common.delete') }}</button>
                    }
                  </div>
                </div>
              }
            } @empty {
              @if (editing() !== 'new') { <p class="muted">{{ i18n.t('admin.noActivities') }}</p> }
            }
          </div>
        }

        @case ('participants') { <app-participants [slotId]="s.id" [demo]="info.demo()" /> }
        @case ('mail') { <app-mailbox [slotId]="s.id" [demo]="info.demo()" [sending]="info.mailSending()" [phase]="s.phase" /> }

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

        @case ('results') { <app-results [slotId]="s.id" [phase]="s.phase" [canEdit]="changeable()" [requiredStaffing]="!!s.rules.requiredStaffing" /> }
        @case ('lists') { <app-lists [slotId]="s.id" [slotName]="s.name" /> }

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
          <div class="card stack">
            <h2>{{ i18n.t('admin.clearAll') }}</h2>
            <p class="muted small">{{ i18n.t('admin.clearAllHint') }}</p>
            @if (confirmClear()) {
              <div class="row">
                <span>{{ i18n.t('common.reallyDelete') }}</span>
                <button class="btn danger" type="button" [disabled]="busy()" (click)="clearAll()">{{ i18n.t('admin.clearAll') }}</button>
                <button class="btn ghost" type="button" (click)="confirmClear.set(false)">{{ i18n.t('common.cancel') }}</button>
              </div>
            } @else {
              <div><button class="btn ghost" type="button" (click)="confirmClear.set(true)">{{ i18n.t('admin.clearAll') }}</button></div>
            }
          </div>
        }
      }
    } @else if (!errors().length) {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    h1 .phase { font-size: 13px; vertical-align: middle; margin-left: 8px }
    .link { align-items: center; gap: 8px }
    .link a { overflow-wrap: anywhere }
    .readonly { border-color: var(--warn); color: var(--warn) }
    .warn { color: var(--warn) }
    .tabs { display: flex; flex-wrap: wrap; gap: 4px; border-bottom: 1px solid var(--line); margin-bottom: 16px }
    .tabs button { padding: 9px 14px; border: 0; border-bottom: 2px solid transparent; background: none; cursor: pointer; color: var(--muted); font-weight: 500 }
    .tabs button.on { color: var(--ink); border-bottom-color: var(--accent) }
    .grow { flex: 1 1 260px }
    select { padding: 9px 10px; border: 1px solid var(--line); border-radius: 8px; background: var(--bg); color: var(--ink); font-size: 15px }
    .rules { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 14px }
    .rules input { max-width: 120px }
    .group-row { align-items: end }
    .group-row .grp { margin-bottom: 14px }
    .group-row input[type=number] { max-width: 90px }
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
  protected session = inject(SessionService);
  private toast = inject(Toast);

  protected info = inject(AppInfo);
  protected get tabs(): Tab[] { return (['settings', 'activities', 'participants', 'mail', 'demand', 'results', 'lists', 'demo'] as Tab[]).filter(t => t !== 'demo' || (this.info.demo() && this.canEdit())); }
  protected canEdit = computed(() => this.slot()?.canEdit !== false);
  /** Ended votes are read only; only the settings stay (e.g. to go back while the last date has not passed). */
  protected changeable = computed(() => this.canEdit() && this.slot()?.phase !== 'Ended');
  /** Pfarrgemeinde admins cannot make a vote read-only for themselves. */
  protected visibilities = computed(() => VISIBILITIES.filter(v => v !== 'JuniaReadOnly' || this.session.account()?.isJuniaAdmin || this.slot()?.visibility === v));
  protected readonly phases = PHASES;
  protected tab = signal<Tab>('activities');
  protected slot = signal<Slot | null>(null);
  protected activities = signal<Activity[]>([]);
  protected demand = signal<Demand[]>([]);
  protected editing = signal<string | null>(null);
  protected confirmDelete = signal<string | null>(null);
  protected confirmDeleteSlot = signal(false);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected confirmClear = signal(false);
  protected showImport = signal(false);
  protected form: { name: string; phase: Phase; deadline: string; openToAll: boolean; excluded: string; showInterim: boolean; parish: string; organiser: string; organiserEmail: string; visibility: Visibility; rules: Rules } =
    { name: '', phase: 'Draft', deadline: '', openToAll: false, excluded: '', showInterim: false, parish: '', organiser: '', organiserEmail: '', visibility: 'JuniaShared', rules: { pointBudget: 100, minActivities: 5, maxPerActivity: 40, perParticipant: 3, step: 5, groups: null, maxExtra: 0, requiredStaffing: false, friends: 0 } };
  protected readonly friendSizes = [0, 2, 3, 4, 5];
  protected formGroups: Group[] = [];
  /** Votes whose participants can be excluded (loaded when "open to everyone" is on). */
  protected otherSlots = signal<{ id: string; name: string }[]>([]);
  protected excludeSource: string | null = null;
  protected readonly colors = GROUP_COLORS;
  protected sampleCount = 0;
  protected removeReal = false;

  ngOnInit(): void { this.load(); }

  protected async load(): Promise<void> {
    this.errors.set([]);
    try {
      const [slot, activities] = await Promise.all([this.api.adminSlot(this.slotId()), this.api.adminActivities(this.slotId())]);
      this.slot.set(slot);
      this.activities.set(activities);
      this.form = { name: slot.name, phase: slot.phase, deadline: toLocalInput(slot.registrationDeadline), openToAll: slot.openToAll, excluded: (slot.excluded ?? []).join('\n'), showInterim: slot.showInterim,
        parish: slot.parish ?? '', organiser: slot.organiser ?? '', organiserEmail: slot.organiserEmail ?? '', visibility: slot.visibility, rules: { ...slot.rules } };
      this.formGroups = (slot.rules.groups ?? []).map(g => ({ ...g }));
      this.sampleCount = slot.sampleCount;
      if (slot.openToAll) await this.loadOtherSlots();
      if (this.tab() === 'demand') this.demand.set(await this.api.demand(this.slotId()));
    } catch (e) {
      this.handle(e);
    }
  }

  protected async show(t: Tab): Promise<void> {
    this.tab.set(t);
    this.editing.set(null);
    try {
      if (t === 'demand') this.demand.set(await this.api.demand(this.slotId()));
    } catch (e) { this.handle(e); }
  }


  protected participantUrl(): string { return `${location.origin}/s/${this.slotId()}`; }

  protected async copyLink(): Promise<void> {
    try { await navigator.clipboard.writeText(this.participantUrl()); this.toast.show(this.i18n.t('admin.linkCopied')); } catch { /* link is visible and selectable */ }
  }

  protected groupSum(): number { return this.formGroups.reduce((sum, g) => sum + Number(g.perParticipant || 0), 0); }
  protected groupOf(a: Activity): Group | undefined { return this.slot()?.rules.groups?.find(g => g.id === a.groupId); }
  protected addGroup(): void {
    this.formGroups.push({ id: '', name: '', perParticipant: 1, minActivities: 2, color: GROUP_COLORS[this.formGroups.length % GROUP_COLORS.length] });
  }

  protected ratioWidth(d: Demand): number { return Math.min(100, (d.interested / (d.seats ?? 1) / 3) * 100); }

  protected async loadOtherSlots(): Promise<void> {
    if (this.otherSlots().length) return;
    try { this.otherSlots.set((await this.api.adminSlots()).filter(s => s.id !== this.slotId())); } catch (e) { this.handle(e); }
  }

  /** Adds the names of another vote's participants (e.g. last year's) to the exclusion list; saving applies it. */
  protected async excludeFrom(id: string | null): Promise<void> {
    if (!id) return;
    try {
      const names = this.form.excluded.split('\n').map(n => n.trim()).filter(n => n);
      const known = new Set(names.map(n => n.toLowerCase()));
      const added = (await this.api.participants(id)).map(p => p.name).filter(n => !known.has(n.toLowerCase()) && known.add(n.toLowerCase()));
      this.form.excluded = [...names, ...added].join('\n');
      this.toast.show(this.i18n.t('admin.excludeAdded', { n: added.length }));
    } catch (e) { this.handle(e); }
    finally { this.excludeSource = null; }
  }

  protected saveSettings(): Promise<void> {
    return this.run(async () => {
      await this.api.updateSlot(this.slotId(), {
        name: this.form.name, phase: this.form.phase, registrationDeadline: fromLocalInput(this.form.deadline), openToAll: this.form.openToAll, showInterim: this.form.showInterim,
        excluded: this.form.excluded.split('\n').map(n => n.trim()).filter(n => n),
        parish: this.form.parish.trim() || null, organiser: this.form.organiser.trim() || null, organiserEmail: this.form.organiserEmail.trim() || null, visibility: this.form.visibility,
        rules: { ...this.form.rules, maxExtra: Number(this.form.rules.maxExtra || 0), requiredStaffing: !!this.form.rules.requiredStaffing, friends: Number(this.form.rules.friends || 0),
          perParticipant: this.formGroups.length ? this.groupSum() : this.form.rules.perParticipant, groups: this.formGroups.map(g => ({ ...g, perParticipant: Number(g.perParticipant), minActivities: Number(g.minActivities) })) },
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

  protected async afterImport(): Promise<void> {
    this.showImport.set(false);
    this.toast.show(this.i18n.t('import.done'));
    await this.load();
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

  /** Removes every ballot (sample and real), e.g. to start a live demo from scratch. */
  protected clearAll(): Promise<void> {
    return this.run(async () => {
      await this.api.resetDemo(this.slotId(), { sampleCount: 0, newSeed: false, removeRealBallots: true });
      this.confirmClear.set(false);
      this.toast.show(this.i18n.t('admin.cleared'));
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
    if (e instanceof ApiFailure && e.errors[0]?.code === 'unauthorized') { this.session.refresh().then(() => this.router.navigate(['/'])); return; }
    else this.errors.set(this.i18n.errors(e));
  }
}
