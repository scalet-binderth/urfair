import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AdminKeyPrompt } from './admin-key';
import { Api, ApiFailure, SlotSummary } from './api';
import { I18n } from './i18n';

/** Organisation start page: all FairSlots and a form to create a new one. */
@Component({
  selector: 'app-admin-list',
  imports: [FormsModule, RouterLink, AdminKeyPrompt],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="head">
      <h1>{{ i18n.t('admin.title') }}</h1>
      <p>{{ i18n.t('admin.lead') }}</p>
    </div>
    @if (needsKey()) { <app-admin-key (entered)="load()" /> }
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
        <label class="check"><input type="checkbox" [(ngModel)]="withSample" />{{ i18n.t('admin.withSample') }}</label>
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      </div>

      <div class="card">
        @if (slots(); as list) {
          @if (list.length) {
            <div class="tbl-wrap"><table>
              <thead><tr>
                <th>{{ i18n.t('admin.col.name') }}</th><th>{{ i18n.t('admin.col.phase') }}</th><th>{{ i18n.t('admin.col.deadline') }}</th>
                <th class="num">{{ i18n.t('admin.col.activities') }}</th><th class="num">{{ i18n.t('admin.col.ballots') }}</th>
              </tr></thead>
              <tbody>
                @for (s of list; track s.id) {
                  <tr>
                    <td><a [routerLink]="['/admin/s', s.id]">{{ s.name }}</a></td>
                    <td><span class="phase" [attr.data-phase]="s.phase">{{ i18n.t('phase.' + s.phase) }}</span></td>
                    <td>{{ i18n.date(s.registrationDeadline) }}</td>
                    <td class="num">{{ s.activityCount }}</td>
                    <td class="num">{{ s.ballotCount }}</td>
                  </tr>
                }
              </tbody>
            </table></div>
          } @else { <p class="muted">{{ i18n.t('admin.none') }}</p> }
        } @else if (!needsKey()) { <p class="muted">{{ i18n.t('common.loading') }}</p> }
      </div>
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

  protected slots = signal<SlotSummary[] | null>(null);
  protected needsKey = signal(false);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected name = '';
  protected withSample = false;

  constructor() { this.load(); }

  protected async load(): Promise<void> {
    try {
      this.slots.set(await this.api.adminSlots());
      this.needsKey.set(false);
    } catch (e) {
      this.needsKey.set(e instanceof ApiFailure && e.errors[0]?.code === 'unauthorized');
      if (!this.needsKey()) this.errors.set(this.i18n.errors(e));
    }
  }

  protected async create(): Promise<void> {
    if (!this.name.trim()) return;
    this.busy.set(true);
    this.errors.set([]);
    try {
      const { id } = await this.api.createSlot(this.name.trim(), this.withSample);
      await this.router.navigate(['/admin/s', id]);
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
}
