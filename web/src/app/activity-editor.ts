import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Activity, ActivityInput, Api, Group, fromLocalInput, toLocalInput } from './api';
import { I18n } from './i18n';
import { AppInfo } from './info';

interface SessionForm { id: string | null; start: string; end: string; location: string; capacity: number | null; flexible: boolean; }

/** Form to create or edit one activity with its sessions. */
@Component({
  selector: 'app-activity-editor',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="editor stack">
      <div class="row">
        <div class="field grow"><label [for]="uid + '-title'">{{ i18n.t('editor.title') }}</label>
          <input [id]="uid + '-title'" [(ngModel)]="title" /></div>
        <div class="field"><label [for]="uid + '-restriction'">{{ i18n.t('editor.restriction') }}</label>
          <select [id]="uid + '-restriction'" [(ngModel)]="restrictedTo">
            <option value="">{{ i18n.t('editor.all') }}</option>
            <option value="w">{{ i18n.t('common.only.w') }}</option>
            <option value="m">{{ i18n.t('common.only.m') }}</option>
          </select></div>
        @if (groups().length) {
          <div class="field"><label [for]="uid + '-group'">{{ i18n.t('editor.group') }}</label>
            <select [id]="uid + '-group'" [(ngModel)]="groupId">
              <option value="">{{ i18n.t('editor.noGroup') }}</option>
              @for (g of groups(); track g.id) { <option [value]="g.id">{{ g.name }}</option> }
            </select></div>
        }
      </div>
      <div class="field"><label [for]="uid + '-desc'">{{ i18n.t('editor.description') }}</label>
        <textarea [id]="uid + '-desc'" rows="3" maxlength="2000" [placeholder]="i18n.t('editor.descriptionHint')" [(ngModel)]="description"></textarea></div>
      <div class="row">
        <div class="field grow"><label [for]="uid + '-parish'">{{ i18n.t('editor.parish') }}</label>
          <select [id]="uid + '-parish'" [(ngModel)]="parish">
            <option value="">{{ i18n.t('editor.inheritHint') }}</option>
            @for (p of info.parishes(); track p) { <option [value]="p">{{ p }}</option> }
            @if (parish && !info.parishes().includes(parish)) { <option [value]="parish">{{ parish }}</option> }
          </select></div>
        <div class="field grow"><label [for]="uid + '-leader'">{{ i18n.t('editor.leader') }}</label>
          <input [id]="uid + '-leader'" maxlength="120" [attr.list]="uid + '-leaders'" [placeholder]="i18n.t('editor.inheritHint')" [(ngModel)]="leader" (ngModelChange)="leaderChanged()" />
          <datalist [id]="uid + '-leaders'">@for (l of knownLeaders(); track l.name) { <option [value]="l.name"></option> }</datalist></div>
        <div class="field grow"><label [for]="uid + '-leader-mail'">{{ i18n.t('editor.leaderEmail') }}@if (leader.trim()) { * }</label>
          <input [id]="uid + '-leader-mail'" type="email" maxlength="200" [required]="!!leader.trim()" [placeholder]="i18n.t('editor.leaderEmailHint')" [(ngModel)]="leaderEmail" /></div>
      </div>
      <p class="muted small">{{ i18n.t('editor.leaderNote') }}</p>

      <h3>{{ i18n.t('editor.sessions') }}</h3>
      @for (s of sessions; track $index; let i = $index) {
        <div class="session">
          <div class="field"><label [for]="uid + '-start-' + i">{{ i18n.t('editor.start') }}</label>
            <input [id]="uid + '-start-' + i" type="datetime-local" [(ngModel)]="s.start" /></div>
          <div class="field"><label [for]="uid + '-end-' + i">{{ i18n.t('editor.end') }}</label>
            <input [id]="uid + '-end-' + i" type="datetime-local" [(ngModel)]="s.end" [disabled]="s.flexible" /></div>
          <div class="field loc"><label [for]="uid + '-loc-' + i">{{ i18n.t('editor.location') }}</label>
            <input [id]="uid + '-loc-' + i" [(ngModel)]="s.location" /></div>
          <div class="field cap"><label [for]="uid + '-cap-' + i">{{ i18n.t('editor.capacity') }}</label>
            <input [id]="uid + '-cap-' + i" type="number" min="1" [placeholder]="i18n.t('editor.capacityHint')" [(ngModel)]="s.capacity" /></div>
          <label class="check"><input type="checkbox" [(ngModel)]="s.flexible" />{{ i18n.t('editor.flexible') }}</label>
          <button class="btn ghost small" type="button" [attr.aria-label]="i18n.t('editor.removeSession')" (click)="sessions.splice(i, 1)">✕</button>
        </div>
      }
      <div><button class="btn ghost" type="button" (click)="addSession()">{{ i18n.t('editor.addSession') }}</button></div>

      @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      <div class="row">
        <button class="btn" type="button" [disabled]="busy()" (click)="save()">{{ i18n.t('common.save') }}</button>
        <button class="btn ghost" type="button" (click)="cancelled.emit()">{{ i18n.t('common.cancel') }}</button>
      </div>
    </div>
  `,
  styles: `
    .editor { border: 1px solid var(--accent); border-radius: var(--r); padding: 14px; background: var(--surface) }
    .grow { flex: 1 1 260px }
    textarea { resize: vertical }
    .session { display: flex; flex-wrap: wrap; gap: 8px 10px; align-items: end; padding-bottom: 10px; border-bottom: 1px dashed var(--line) }
    .session .loc { flex: 1 1 180px }
    .session .cap { width: 130px }
    .btn.small { padding: 8px 12px; font-size: inherit }
  `,
})
export class ActivityEditor implements OnInit {
  readonly slotId = input.required<string>();
  /** Activity to edit; null creates a new one. */
  readonly activity = input<Activity | null>(null);
  /** Groups of the FairSlot; empty hides the group field. */
  readonly groups = input<Group[]>([]);
  /** The vote's activities, for the leaders already known (name and address). */
  readonly known = input<Activity[]>([]);
  readonly saved = output<void>();
  readonly cancelled = output<void>();

  protected i18n = inject(I18n);
  protected info = inject(AppInfo);
  private api = inject(Api);
  protected uid = 'ae' + Math.random().toString(36).slice(2, 8);
  protected title = '';
  protected restrictedTo = '';
  protected groupId = '';
  protected description = '';
  protected parish = '';
  protected leader = '';
  protected leaderEmail = '';
  protected sessions: SessionForm[] = [];
  protected errors = signal<string[]>([]);
  protected busy = signal(false);

  ngOnInit(): void {
    const a = this.activity();
    if (a) {
      this.title = a.title;
      this.restrictedTo = a.restrictedTo ?? '';
      this.groupId = a.groupId ?? '';
      this.description = a.description ?? '';
      this.parish = a.parish ?? '';
      this.leader = a.leader ?? '';
      this.leaderEmail = a.leaderEmail ?? '';
      this.sessions = a.sessions.map(s => ({ id: s.id, start: toLocalInput(s.start), end: toLocalInput(s.end), location: s.location, capacity: s.capacity, flexible: s.flexible }));
    } else {
      this.addSession();
    }
  }

  protected knownLeaders = computed(() => {
    const map = new Map<string, { name: string; email: string | null }>();
    for (const a of this.known()) {
      if (!a.leader) continue;
      const key = a.leader.trim().toLowerCase();
      const l = map.get(key) ?? { name: a.leader.trim(), email: null };
      l.email ??= a.leaderEmail ?? null;
      map.set(key, l);
    }
    return [...map.values()].sort((x, y) => x.name.localeCompare(y.name, this.i18n.locale(), { sensitivity: 'base' }));
  });

  /** A known leader brings their address along (one address per leader). */
  protected leaderChanged(): void {
    const known = this.knownLeaders().find(l => l.name.toLowerCase() === this.leader.trim().toLowerCase());
    if (known?.email && !this.leaderEmail.trim()) this.leaderEmail = known.email;
  }

  protected addSession(): void {
    const last = this.sessions.at(-1);
    this.sessions.push({ id: null, start: last?.start ?? '', end: last?.end ?? '', location: last?.location ?? '', capacity: last?.capacity ?? null, flexible: false });
  }

  protected async save(): Promise<void> {
    const body: ActivityInput = {
      title: this.title.trim(),
      restrictedTo: this.restrictedTo || null,
      groupId: this.groupId || null,
      description: this.description.trim() || null,
      parish: this.parish.trim() || null,
      leader: this.leader.trim() || null,
      leaderEmail: this.leaderEmail.trim() || null,
      sessions: this.sessions.map(s => ({
        id: s.id, start: s.start, end: s.flexible ? null : fromLocalInput(s.end), location: s.location.trim(),
        capacity: s.capacity === null || (s.capacity as unknown) === '' ? null : Number(s.capacity), flexible: s.flexible,
      })),
    };
    if (body.sessions.some(s => !s.start)) { this.errors.set([this.i18n.t('error.sessionRequired')]); return; }
    this.busy.set(true);
    this.errors.set([]);
    try {
      const a = this.activity();
      if (a) await this.api.updateActivity(a.id, body); else await this.api.addActivity(this.slotId(), body);
      this.saved.emit();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
}
