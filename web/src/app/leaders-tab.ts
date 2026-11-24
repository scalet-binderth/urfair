import { ChangeDetectionStrategy, Component, OnInit, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Leader } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

/**
 * All leaders of a vote in one place: name, address and the activities they lead. The organiser comes first and leads
 * every activity without a leader of its own; changing that row changes the settings too. One address per leader.
 */
@Component({
  selector: 'app-leaders',
  imports: [FormsModule],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    <div class="card stack">
      <p class="muted small">{{ i18n.t('leaders.lead') }}</p>
      @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
      <div class="tbl-wrap"><table>
        <thead><tr>
          <th>{{ i18n.t('editor.leader') }}</th><th>{{ i18n.t('leaders.email') }}</th><th>{{ i18n.t('leaders.activities') }}</th><th></th>
        </tr></thead>
        <tbody>
          @for (l of leaders(); track key(l)) {
            @if (editing() === key(l)) {
              <tr class="edit">
                <td><input maxlength="120" [attr.aria-label]="i18n.t('editor.leader')" [placeholder]="l.isOrganiser ? i18n.t('leaders.organiserName') : ''" [(ngModel)]="name" /></td>
                <td><input type="email" maxlength="200" required [attr.aria-label]="i18n.t('leaders.email')" [(ngModel)]="email" /></td>
                <td class="small">{{ titles(l) }}@if (l.isOrganiser) { <div class="muted">{{ i18n.t('leaders.organiserSync') }}</div> }</td>
                <td class="actions">
                  <button class="btn small" type="button" [disabled]="busy()" (click)="save(l)">{{ i18n.t('common.save') }}</button>
                  <button class="btn ghost small" type="button" (click)="editing.set(null)">{{ i18n.t('common.cancel') }}</button>
                </td>
              </tr>
            } @else {
              <tr>
                <td>{{ l.name ?? i18n.t('leaders.organiser') }}@if (l.isOrganiser) { <span class="tag">{{ i18n.t('leaders.organiserTag') }}</span> }</td>
                <td>@if (l.email) { {{ l.email }} } @else { <span class="warn">{{ i18n.t('leaders.emailMissing') }}</span> }
                  @if (l.emails.length) { <div class="warn small">{{ i18n.t('leaders.emailsDiffer', { list: l.emails.join(', ') }) }}</div> }</td>
                <td class="small">@if (l.activities.length) { {{ titles(l) }} } @else { <span class="muted">{{ i18n.t('leaders.none') }}</span> }</td>
                <td class="actions"><button class="btn ghost small" type="button" [hidden]="!canEdit()" (click)="edit(l)">{{ i18n.t('common.edit') }}</button></td>
              </tr>
            }
          }
        </tbody>
      </table></div>
    </div>
  `,
  styles: `
    .warn { color: var(--warn) }
    td { vertical-align: top }
    td input { width: 100%; min-width: 160px }
    .actions { white-space: nowrap; text-align: right }
    .btn.small { padding: 6px 12px; font-size: 14px }
  `,
})
export class LeadersTab implements OnInit {
  readonly slotId = input.required<string>();
  readonly canEdit = input(true);
  /** A leader or the organiser was changed (activities and settings show it). */
  readonly changed = output<void>();

  protected i18n = inject(I18n);
  private api = inject(Api);
  private toast = inject(Toast);
  protected leaders = signal<Leader[]>([]);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);
  protected editing = signal<string | null>(null);
  protected name = '';
  protected email = '';

  async ngOnInit(): Promise<void> {
    try { this.leaders.set(await this.api.leaders(this.slotId())); } catch (e) { this.errors.set(this.i18n.errors(e)); }
  }

  protected key(l: Leader): string { return l.isOrganiser ? '' : (l.name ?? '').toLowerCase(); }
  protected titles(l: Leader): string { return l.activities.map(a => a.title).join(', '); }

  protected edit(l: Leader): void {
    this.name = l.name ?? '';
    this.email = l.email ?? l.emails[0] ?? '';
    this.errors.set([]);
    this.editing.set(this.key(l));
  }

  protected async save(l: Leader): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      this.leaders.set(await this.api.updateLeader(this.slotId(), { oldName: l.name, isOrganiser: l.isOrganiser, name: this.name.trim() || null, email: this.email.trim() || null }));
      this.editing.set(null);
      this.toast.show(this.i18n.t('common.saved'));
      this.changed.emit();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
}
