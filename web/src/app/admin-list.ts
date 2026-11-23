import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Api, ApiFailure, SlotSummary } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';
import { AppInfo } from './info';
import { Toast } from './toast';

/** Organisation start page: all FairSlots and a form to create a new one. */
@Component({
  selector: 'app-admin-list',
  imports: [FormsModule, RouterLink],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="head">
      <h1>{{ i18n.t('admin.title') }}</h1>
      <p>{{ i18n.t('admin.lead') }}</p>
    </div>
    <div class="stack">
      <div class="card stack">
        <h2>{{ i18n.t('admin.new') }}</h2>
        <div class="row">
          <div class="field grow">
            <label for="new-name">{{ i18n.t('admin.name') }}</label>
            <input id="new-name" [placeholder]="i18n.t('admin.newName')" [(ngModel)]="name" (keyup.enter)="create()" />
          </div>
          <button class="btn" type="button" [disabled]="busy() || !name.trim()" (click)="create()">{{ i18n.t('admin.create') }}</button>
        </div>
        @if (slots()?.length) {
          <div class="row">
            <div class="field grow">
              <label for="new-template">{{ i18n.t('admin.template') }}</label>
              <select id="new-template" [(ngModel)]="copyFrom" (ngModelChange)="templateChosen($event)">
                <option [ngValue]="null">{{ i18n.t('admin.templateNone') }}</option>
                @for (s of slots(); track s.id) { <option [ngValue]="s.id">{{ s.name }}</option> }
              </select>
            </div>
            @if (copyFrom) {
              <div class="field">
                <label for="new-shift">{{ i18n.t('admin.shiftWeeks') }}</label>
                <input id="new-shift" type="number" min="0" max="104" [(ngModel)]="shiftWeeks" />
              </div>
            }
          </div>
          @if (copyFrom) { <p class="muted small">{{ i18n.t('admin.templateHint') }}</p> }
        }
        @if (info.demo() && !copyFrom) { <label class="check"><input type="checkbox" [(ngModel)]="withSample" />{{ i18n.t('admin.withSample') }}</label> }
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>

      <div class="card">
        @if (slots(); as list) {
          @if (list.length) {
            <div class="tbl-wrap"><table>
              <thead><tr>
                <th>{{ i18n.t('admin.col.name') }}</th><th>{{ i18n.t('admin.col.owner') }}</th><th>{{ i18n.t('admin.col.phase') }}</th><th>{{ i18n.t('admin.col.deadline') }}</th>
                <th class="num">{{ i18n.t('admin.col.activities') }}</th><th class="num">{{ i18n.t('admin.col.ballots') }}</th>
              </tr></thead>
              <tbody>
                @for (s of list; track s.id) {
                  <tr>
                    <td><a [routerLink]="['/admin/s', s.id]">{{ s.name }}</a></td>
                    <td class="small">{{ s.parish ?? '–' }}<br /><span class="muted">{{ i18n.t('visibility.' + s.visibility) }}</span>@if (!s.canEdit) { <span class="tag">{{ i18n.t('admin.readOnlyShort') }}</span> }</td>
                    <td><span class="phase" [attr.data-phase]="s.phase">{{ i18n.t('phase.' + s.phase) }}</span></td>
                    <td>{{ i18n.date(s.registrationDeadline) }}</td>
                    <td class="num">{{ s.activityCount }}</td>
                    <td class="num">{{ s.ballotCount }}</td>
                  </tr>
                }
              </tbody>
            </table></div>
          } @else { <p class="muted">{{ i18n.t('admin.none') }}</p> }
        } @else { <p class="muted">{{ i18n.t('common.loading') }}</p> }
      </div>
      @if (info.demo() && session.account()?.isJuniaAdmin) {
        <div class="card stack">
          <h2>{{ i18n.t('admin.resetAll') }}</h2>
          <p class="muted small">{{ i18n.t('admin.resetAllHint') }}</p>
          @if (confirmResetAll()) {
            <div class="row">
              <span>{{ i18n.t('common.reallyDelete') }}</span>
              <button class="btn" type="button" [disabled]="busy()" (click)="resetAll()">{{ i18n.t('admin.resetAll') }}</button>
              <button class="btn ghost" type="button" (click)="confirmResetAll.set(false)">{{ i18n.t('common.cancel') }}</button>
            </div>
          } @else {
            <div><button class="btn ghost" type="button" (click)="confirmResetAll.set(true)">{{ i18n.t('admin.resetAll') }}</button></div>
          }
        </div>
      }
    </div>
  `,
  styles: `
    .grow { flex: 1 1 260px }
  `,
})
export class AdminListPage {
  protected i18n = inject(I18n);
  private api = inject(Api);
  private router = inject(Router);
  protected session = inject(SessionService);
  protected info = inject(AppInfo);
  private toast = inject(Toast);
  protected confirmResetAll = signal(false);

  protected slots = signal<SlotSummary[] | null>(null);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected name = '';
  protected withSample = false;
  protected copyFrom: string | null = null;
  /** 52 weeks: the same weekday a year later. */
  protected shiftWeeks = 52;

  constructor() { this.load(); }

  protected async load(): Promise<void> {
    try {
      this.slots.set(await this.api.adminSlots());
    } catch (e) {
      if (e instanceof ApiFailure && e.errors[0]?.code === 'unauthorized') { await this.session.refresh(); await this.router.navigate(['/']); }
      else this.errors.set(this.i18n.errors(e));
    }
  }

  /** Demo only: deletes every vote and recreates the demo votes. */
  protected async resetAll(): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      await this.api.resetAll();
      this.confirmResetAll.set(false);
      this.toast.show(this.i18n.t('admin.resetDone'));
      await this.load();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async create(): Promise<void> {
    if (!this.name.trim()) return;
    this.busy.set(true);
    this.errors.set([]);
    try {
      const { id } = await this.api.createSlot(this.name.trim(), this.withSample && !this.copyFrom, this.copyFrom, this.copyFrom ? this.shiftWeeks ?? 0 : 0);
      await this.router.navigate(['/admin/s', id]);
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }

  /** Suggests the template's name, so only the year needs changing. */
  protected templateChosen(id: string | null): void {
    const source = this.slots()?.find(s => s.id === id);
    if (source && !this.name.trim()) this.name = source.name;
  }
}
