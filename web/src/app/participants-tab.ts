import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, ListedParticipant, ParticipantInput, SlotSummary } from './api';
import { decodeCsv, saveBlob } from './csv';
import { I18n } from './i18n';
import { AppInfo } from './info';
import { Toast } from './toast';

/** Participant list of one vote: add and edit by hand, import from Excel/CSV, personal links. */
@Component({
  selector: 'app-participants',
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="stack">
      <div class="card stack">
        <p class="muted small">{{ i18n.t('participants.lead') }}</p>
        <form class="row form" (submit)="$event.preventDefault(); save()">
          <div class="field grow"><label for="p-name">{{ i18n.t('admin.participantName') }}</label>
            <input id="p-name" name="name" maxlength="120" [(ngModel)]="form.name" [placeholder]="i18n.t('vote.namePlaceholder')" /></div>
          <div class="field grow"><label for="p-mail">{{ i18n.t('participants.email') }}</label>
            <input id="p-mail" name="email" type="email" maxlength="200" [(ngModel)]="form.email" /></div>
          <div class="field"><label for="p-birth">{{ i18n.t('participants.birthDate') }}</label>
            <input id="p-birth" name="birth" type="date" [(ngModel)]="form.birthDate" /></div>
          <div class="field"><label for="p-baptism">{{ i18n.t('participants.baptismDate') }}</label>
            <input id="p-baptism" name="baptism" type="date" [(ngModel)]="form.baptismDate" /></div>
          <div class="field"><label for="p-parish">{{ i18n.t('participants.parish') }}</label>
            <select id="p-parish" name="parish" [(ngModel)]="form.parish">
              <option value="">–</option>
              @for (p of info.parishes(); track p) { <option [value]="p">{{ p }}</option> }
            </select></div>
          <button class="btn" type="submit" [disabled]="!form.name.trim() || busy()">{{ i18n.t(editing() ? 'common.save' : 'admin.addParticipant') }}</button>
          @if (editing()) { <button class="btn ghost" type="button" (click)="reset()">{{ i18n.t('common.cancel') }}</button> }
        </form>
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>

      <div class="card stack">
        <div class="row between">
          <h2>{{ i18n.t('participants.count', { n: inParish().length, v: votedCount() }) }}</h2>
          <div class="row">
            @if (parishesInList().length > 1) {
              <select [ngModel]="filter()" (ngModelChange)="filter.set($event)" [attr.aria-label]="i18n.t('participants.filter')">
                <option value="">{{ i18n.t('participants.allParishes') }}</option>
                @for (p of parishesInList(); track p) { <option [value]="p">{{ p }}</option> }
              </select>
            }
            <button class="btn ghost small" type="button" (click)="toggleCopy()">{{ i18n.t('participants.copyOpen') }}</button>
            <button class="btn ghost small" type="button" (click)="showImport.set(!showImport())">{{ i18n.t('import.open') }}</button>
          </div>
        </div>
        <div class="row center">
          <label class="check"><input type="checkbox" [ngModel]="notVotedOnly()" (ngModelChange)="notVotedOnly.set($event)" />{{ i18n.t('participants.notVotedOnly', { n: notVotedCount() }) }}</label>
          <button class="btn ghost small" type="button" (click)="copyMails()">{{ i18n.t('participants.copyMails') }}</button>
        </div>

        @if (showCopy()) {
          <div class="import stack">
            <p class="muted small">{{ i18n.t('participants.copyLead') }}</p>
            <select [(ngModel)]="copyFrom" (ngModelChange)="sendCopy(false)" [attr.aria-label]="i18n.t('participants.copyOpen')">
              <option [ngValue]="null">{{ i18n.t('participants.copyChoose') }}</option>
              @for (s of otherSlots(); track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
            </select>
            @if (copyErrors().length) { <ul class="errors">@for (e of copyErrors(); track $index) { <li>{{ e }}</li> }</ul> }
            @if (copyCounts(); as c) {
              <p class="small">{{ i18n.t('participants.copyFound', { n: c.new, k: c.known }) }}</p>
              @if (c.new) {
                <div class="row">
                  <button class="btn" type="button" [disabled]="busy()" (click)="sendCopy(true)">{{ i18n.t('participants.commit') }}</button>
                  <button class="btn ghost" type="button" (click)="closeCopy()">{{ i18n.t('common.cancel') }}</button>
                </div>
              }
            }
          </div>
        }

        @if (showImport()) {
          <div class="import stack">
            <p class="muted small">{{ i18n.t('participants.importLead') }}</p>
            <div class="row">
              <button class="btn ghost" type="button" (click)="download()">{{ i18n.t('import.template') }}</button>
              <label class="btn ghost file">{{ i18n.t('import.choose') }}<input type="file" accept=".csv,text/csv" (change)="choose($event)" /></label>
            </div>
            @if (importErrors().length) { <ul class="errors">@for (e of importErrors(); track $index) { <li>{{ e }}</li> }</ul> }
            @if (preview(); as pv) {
              <p class="small">{{ i18n.t('participants.found', { n: pv.new, u: pv.updated }) }}</p>
              @if (!importErrors().length) {
                <div class="row">
                  <button class="btn" type="button" [disabled]="busy()" (click)="commit()">{{ i18n.t('participants.commit') }}</button>
                  <button class="btn ghost" type="button" (click)="preview.set(null)">{{ i18n.t('common.cancel') }}</button>
                </div>
              }
            }
          </div>
        }

        @if (filtered().length) {
          <div class="tbl-wrap"><table>
            <thead><tr>
              <th>{{ i18n.t('admin.col.name') }}</th><th>{{ i18n.t('participants.parish') }}</th><th>{{ i18n.t('participants.email') }}</th>
              <th>{{ i18n.t('participants.birthDate') }}</th><th>{{ i18n.t('participants.baptismDate') }}</th><th>{{ i18n.t('admin.col.voted') }}</th><th>{{ i18n.t('participants.link') }}</th><th></th>
            </tr></thead>
            <tbody>
              @for (p of filtered(); track p.id) {
                <tr>
                  <td>{{ p.name }}</td>
                  <td class="small">{{ p.parish ?? '–' }}</td>
                  <td class="small">{{ p.email ?? '–' }}@if (p.invitedAt) { <span class="tag">{{ i18n.t('participants.invited') }}</span> }</td>
                  <td class="small">{{ i18n.day(p.birthDate) }}</td>
                  <td class="small">{{ i18n.day(p.baptismDate) }}</td>
                  <td>@if (p.hasVoted) { <span class="phase" data-phase="Open">{{ i18n.t('admin.voted') }}</span> } @else { <span class="muted small">{{ i18n.t('admin.notVoted') }}</span> }</td>
                  <td class="nowrap">
                    <button class="btn ghost small" type="button" (click)="copyLink(p)">{{ i18n.t('admin.copyLink') }}</button>
                    @if (demo()) { <a class="small" [routerLink]="['/p', p.token]">{{ i18n.t('participants.open') }}</a> }
                  </td>
                  <td class="num nowrap">
                    @if (confirmDelete() === p.id) {
                      <button class="btn danger small" type="button" [disabled]="busy()" (click)="remove(p.id)">{{ i18n.t('common.delete') }}</button>
                      <button class="btn ghost small" type="button" (click)="confirmDelete.set(null)">{{ i18n.t('common.cancel') }}</button>
                    } @else {
                      <button class="btn ghost small" type="button" (click)="edit(p)">{{ i18n.t('common.edit') }}</button>
                      <button class="btn ghost small" type="button" (click)="confirmDelete.set(p.id)">{{ i18n.t('common.delete') }}</button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table></div>
        } @else if (!inParish().length) { <p class="muted">{{ i18n.t('admin.noParticipants') }}</p> }
      </div>
    </div>
  `,
  styles: `
    .grow { flex: 1 1 200px }
    .form { align-items: end }
    .between { justify-content: space-between; align-items: center }
    .center { align-items: center }
    .file { position: relative; overflow: hidden }
    .file input { position: absolute; inset: 0; opacity: 0; cursor: pointer }
    .import { border: 1px dashed var(--line); border-radius: var(--r); padding: 12px }
    .nowrap { white-space: nowrap }
  `,
})
export class ParticipantsTab implements OnInit {
  readonly slotId = input.required<string>();
  /** Demo mode: personal links can be opened directly. */
  readonly demo = input(false);

  protected i18n = inject(I18n);
  protected info = inject(AppInfo);
  private api = inject(Api);
  private toast = inject(Toast);

  protected list = signal<ListedParticipant[]>([]);
  protected filter = signal('');
  protected notVotedOnly = signal(false);
  protected inParish = computed(() => this.list().filter(p => !this.filter() || p.parish === this.filter()));
  protected filtered = computed(() => this.inParish().filter(p => !this.notVotedOnly() || !p.hasVoted));
  protected votedCount = computed(() => this.inParish().filter(p => p.hasVoted).length);
  protected notVotedCount = computed(() => this.inParish().length - this.votedCount());
  protected parishesInList = computed(() => [...new Set(this.list().map(p => p.parish).filter((p): p is string => !!p))].sort());
  protected form = { name: '', email: '', birthDate: '', baptismDate: '', parish: '' };
  protected editing = signal<string | null>(null);
  protected confirmDelete = signal<string | null>(null);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected showImport = signal(false);
  protected preview = signal<{ new: number; updated: number } | null>(null);
  protected importErrors = signal<string[]>([]);
  private csv = '';
  protected showCopy = signal(false);
  protected otherSlots = signal<SlotSummary[]>([]);
  protected copyFrom: string | null = null;
  protected copyCounts = signal<{ new: number; known: number } | null>(null);
  protected copyErrors = signal<string[]>([]);

  ngOnInit(): void { this.load(); }

  private async load(): Promise<void> {
    try { this.list.set(await this.api.participants(this.slotId())); } catch (e) { this.errors.set(this.i18n.errors(e)); }
  }

  protected edit(p: ListedParticipant): void {
    this.editing.set(p.id);
    this.form = { name: p.name, email: p.email ?? '', birthDate: p.birthDate ?? '', baptismDate: p.baptismDate ?? '', parish: p.parish ?? '' };
  }

  protected reset(): void {
    this.editing.set(null);
    this.form = { name: '', email: '', birthDate: '', baptismDate: '', parish: '' };
    this.errors.set([]);
  }

  protected async save(): Promise<void> {
    const body: ParticipantInput = {
      name: this.form.name.trim(), email: this.form.email.trim() || null, birthDate: this.form.birthDate || null,
      baptismDate: this.form.baptismDate || null, parish: this.form.parish || null,
    };
    await this.run(async () => {
      const id = this.editing();
      if (id) await this.api.updateParticipant(id, body); else await this.api.addParticipant(this.slotId(), body);
      this.toast.show(this.i18n.t('common.saved'));
      this.reset();
      await this.load();
    });
  }

  protected async remove(id: string): Promise<void> {
    await this.run(async () => {
      await this.api.removeParticipant(id);
      this.confirmDelete.set(null);
      await this.load();
    });
  }

  /** E-mail addresses of the people shown, e.g. to remind them via BCC in one's own mail program. */
  protected async copyMails(): Promise<void> {
    const mails = [...new Set(this.filtered().map(p => p.email).filter((m): m is string => !!m))];
    if (!mails.length) { this.toast.show(this.i18n.t('participants.noMails')); return; }
    await this.toast.copy(mails.join('; '), this.i18n.t('participants.mailsCopied', { n: mails.length }));
  }

  protected copyLink(p: ListedParticipant): Promise<void> {
    return this.toast.copy(`${location.origin}/p/${p.token}`, this.i18n.t('admin.linkCopied'));
  }

  protected async download(): Promise<void> {
    try {
      saveBlob(await this.api.exportParticipants(this.slotId()), this.i18n.t('participants.fileName'));
    } catch (e) { this.importErrors.set(this.i18n.errors(e)); }
  }

  protected async choose(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;
    this.csv = decodeCsv(await file.arrayBuffer());
    await this.send(false);
  }

  protected async commit(): Promise<void> { await this.send(true); }

  private async send(commit: boolean): Promise<void> {
    this.busy.set(true);
    this.importErrors.set([]);
    try {
      const res = await this.api.importParticipants(this.slotId(), { csv: this.csv, commit });
      this.importErrors.set(res.errors.map(e => this.i18n.error(e)));
      this.preview.set(res.people.length || res.errors.length ? { new: res.new, updated: res.updated } : null);
      if (!res.people.length && !res.errors.length) this.importErrors.set([this.i18n.t('error.csvEmpty')]);
      if (res.saved) {
        this.preview.set(null);
        this.showImport.set(false);
        this.toast.show(this.i18n.t('participants.imported', { n: res.new, u: res.updated }));
        await this.load();
      }
    } catch (e) {
      this.importErrors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }

  /** Other votes as the source, e.g. last year's (loaded when the panel opens). */
  protected async toggleCopy(): Promise<void> {
    if (this.showCopy()) { this.closeCopy(); return; }
    this.showCopy.set(true);
    try { this.otherSlots.set((await this.api.adminSlots()).filter(s => s.id !== this.slotId())); }
    catch (e) { this.copyErrors.set(this.i18n.errors(e)); }
  }

  protected closeCopy(): void {
    this.showCopy.set(false);
    this.copyFrom = null;
    this.copyCounts.set(null);
    this.copyErrors.set([]);
  }

  protected async sendCopy(commit: boolean): Promise<void> {
    this.copyCounts.set(null);
    this.copyErrors.set([]);
    if (!this.copyFrom) return;
    this.busy.set(true);
    try {
      const res = await this.api.copyParticipants(this.slotId(), { from: this.copyFrom, commit });
      if (res.saved) {
        this.closeCopy();
        this.toast.show(this.i18n.t('participants.copied', { n: res.new }));
        await this.load();
      } else this.copyCounts.set({ new: res.new, known: res.known });
    } catch (e) {
      this.copyErrors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try { await action(); } catch (e) { this.errors.set(this.i18n.errors(e)); } finally { this.busy.set(false); }
  }
}
