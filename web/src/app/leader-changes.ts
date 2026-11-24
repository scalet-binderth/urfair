import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { Api, LeaderChange } from './api';
import { I18n } from './i18n';
import { Toast } from './toast';

/**
 * Leaders who got their list by mail but do not know about later changes (cancelling, moving up, changes by hand).
 * Nothing goes out on its own: the organisers see what changed and decide whom to mail.
 */
@Component({
  selector: 'app-leader-changes',
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    @if (changes().length) {
      <div class="card stack pending no-print">
        <h3>{{ i18n.t(changes().length === 1 ? 'leaderChanges.titleOne' : 'leaderChanges.title', { n: changes().length }) }}</h3>
        <p class="muted small">{{ i18n.t('leaderChanges.lead') }}</p>
        @for (c of changes(); track c.email) {
          <div class="leader">
            <div class="who"><strong>{{ c.name ?? c.email }}</strong> <span class="muted small">{{ c.email }}</span></div>
            <ul class="small">
              @for (s of c.sessions; track s.sessionId) {
                <li><strong>{{ s.title }}</strong>, {{ i18n.when(s) }}:
                  @if (s.added.length) { <span class="add">+ {{ s.added.join(', ') }}</span> }
                  @if (s.removed.length) { <span class="rem">− {{ s.removed.join(', ') }}</span> }
                  <span class="muted">({{ s.capacity ? i18n.t('lists.countCap', { n: s.count, c: s.capacity }) : i18n.t('lists.count', { n: s.count }) }})</span></li>
              }
            </ul>
            <button class="btn ghost small" type="button" [disabled]="busy() || !canEdit()" (click)="send([c.email])">{{ i18n.t('leaderChanges.mailOne') }}</button>
          </div>
        }
        @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
        @if (changes().length > 1) {
          <div><button class="btn small" type="button" [disabled]="busy() || !canEdit()" (click)="send(null)">{{ i18n.t('leaderChanges.mailAll', { n: changes().length }) }}</button></div>
        }
      </div>
    }
  `,
  styles: `
    .pending { border-color: var(--warn) }
    .pending h3 { margin: 0; color: var(--warn) }
    .leader { display: grid; gap: 4px; justify-items: start; border-top: 1px solid var(--line); padding-top: 8px }
    .leader ul { margin: 0; padding-left: 18px; display: grid; gap: 2px }
    .add { color: var(--good) }
    .rem { color: var(--warn) }
    .btn.small { padding: 6px 12px; font-size: 14px }
  `,
})
export class LeaderChanges implements OnInit {
  readonly slotId = input.required<string>();
  readonly canEdit = input(true);

  protected i18n = inject(I18n);
  private api = inject(Api);
  private toast = inject(Toast);
  protected changes = signal<LeaderChange[]>([]);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);

  ngOnInit(): void { void this.reload(); }

  /** Called again by the parent after it changed the allocation. */
  async reload(): Promise<void> {
    try { this.changes.set(await this.api.leaderChanges(this.slotId())); } catch (e) { this.errors.set(this.i18n.errors(e)); }
  }

  protected async send(emails: string[] | null): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const r = await this.api.sendLeaderChanges(this.slotId(), emails);
      this.toast.show(this.i18n.t('lists.mailed', { n: r.created }));
      await this.reload();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
}
