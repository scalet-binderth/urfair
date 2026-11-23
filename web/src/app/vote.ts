import { ChangeDetectionStrategy, Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Activity, Api, Group, MyActivityStatus, MyStatus, Slot } from './api';
import { I18n } from './i18n';
import { SessionService } from './session';
import { Toast } from './toast';

/** Voting page of one FairSlot. */
@Component({
  selector: 'app-vote',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.Eager,
  template: `
    @if (slot(); as c) {
      <div class="head">
        <p class="muted small">{{ c.name }}</p>
        @if (c.parish || c.organiser) { <p class="muted small">{{ i18n.byLine(c.parish, c.organiser) }}</p> }
        <h1>{{ i18n.t('vote.title') }}</h1>
        <p>{{ i18n.t('vote.lead', { budget: c.rules.pointBudget, min: c.rules.minActivities, max: c.rules.maxPerActivity, k: c.rules.perParticipant }) }}</p>
        @if (groups().length) { <p>{{ i18n.t('vote.leadGroups') }}</p> }
      </div>
      @if (!canVote()) { <p class="card">{{ i18n.t('vote.closed') }}</p> }
      <div class="mini">
        <span class="small">{{ left() }} {{ i18n.t('vote.of', { n: c.rules.pointBudget }) }} · {{ chosenCount() }}</span>
        <div class="meter"><i [style.width.%]="spentPercent()"></i></div>
      </div>
      @if (status()?.activities?.length) {
        <section class="card stack status">
          <h2>{{ i18n.t(interim() ? 'status.title' : 'status.titleFinal') }}</h2>
          <p class="muted small">{{ i18n.t(interim() ? 'status.lead' : closed() ? 'status.leadClosed' : 'status.leadFinal') }}</p>
          @if (closed() && status()!.offer; as o) {
            <div class="offer stack">
              <p class="small"><b>{{ i18n.t('status.offerTitle', { title: o.title }) }}</b></p>
              <p class="small">{{ i18n.when(o) }}@if (o.location) { · {{ o.location }} }</p>
              <p class="small">{{ o.givesUp ? i18n.t('status.offerSwap', { seat: o.givesUp }) : i18n.t('status.offerMissing') }}</p>
              <p class="muted small">{{ i18n.t('status.offerUntil', { date: i18n.date(o.expiresAt) }) }}</p>
              <div class="row">
                <button class="btn small" type="button" [disabled]="busy()" (click)="answerOffer(o.id, 'accept')">{{ i18n.t('status.offerAccept') }}</button>
                <button class="btn ghost small" type="button" [disabled]="busy()" (click)="answerOffer(o.id, 'decline')">{{ i18n.t('status.offerDecline') }}</button>
              </div>
            </div>
          }
          <ul class="st">
            @for (x of status()!.activities; track x.activityId) {
              <li>
                <span class="t">@if (groupFor(x.groupId); as g) { <span class="grp" [attr.data-color]="g.color"></span> }{{ x.title }}
                  <span class="muted small">· {{ i18n.t('status.points', { n: x.points }) }}</span></span>
                <span class="chip" [attr.data-status]="x.status">{{ i18n.t(x.status === 'fixed' && !interim() ? 'status.fixedFinal' : x.status === 'givenBack' && closed() ? 'status.cancelled' : 'status.' + x.status, { n: x.position ?? 0 }) }}</span>
                @if (x.status === 'fixed') {
                  <span class="muted small w">{{ i18n.when({ start: x.start!, end: x.end, flexible: x.flexible }) }}@if (x.location) { · {{ x.location }} }</span>
                  @if (x.canGiveBack) {
                    @if (givingBack() === x.activityId) {
                      <div class="w back">
                        <p class="small">{{ i18n.t(closed() ? 'status.cancelConfirm' : 'status.giveBackConfirm', { title: x.title }) }}</p>
                        @if (published() && leavesShort(x)) {
                          <p class="small short">{{ i18n.t('status.giveBackShort') }}</p>
                          @for (y of swapsFor(x); track y.activityId) {
                            @if (y.waitlisted) {
                              <p class="small">{{ i18n.t('status.swapSet', { title: y.title, mine: x.title }) }}</p>
                            } @else {
                              <p class="small">{{ i18n.t('status.swapOffer', { title: y.title, mine: x.title }) }}@if (y.position) { {{ i18n.t('status.swapPosition', { n: y.position }) }} }</p>
                              <div class="row"><button class="btn small" type="button" [disabled]="busy()" (click)="swapOnly(y.activityId)">{{ i18n.t('status.swapOnly', { title: y.title }) }}</button></div>
                            }
                          }
                        }
                        <label class="small"><input type="checkbox" [checked]="replacement()" (change)="replacement.set(!replacement())" />{{ i18n.t(closed() ? 'status.replacementClosed' : 'status.replacement') }}</label>
                        <div class="row">
                          <button class="btn small" type="button" [class.ghost]="published() && swapsFor(x).length > 0" [disabled]="busy()" (click)="giveBack(x.activityId)">{{
                            i18n.t(closed() ? 'status.cancel' : published() && leavesShort(x) ? 'status.giveBackAnyway' : 'status.giveBack') }}</button>
                          <button class="btn ghost small" type="button" (click)="givingBack.set(null)">{{ i18n.t('common.cancel') }}</button>
                        </div>
                      </div>
                    } @else {
                      <div class="w"><button class="btn ghost small" type="button" [disabled]="busy()" (click)="givingBack.set(x.activityId); replacement.set(true)">{{ i18n.t(closed() ? 'status.cancel' : 'status.giveBack') }}</button></div>
                    }
                  }
                } @else if (x.status !== 'givenBack') {
                  @if (interim()) {
                    <label class="w small"><input type="checkbox" [checked]="x.waitlisted" [disabled]="busy() || !canWaitlist()" (change)="toggleWaitlist(x.activityId)" />{{ i18n.t('status.keep') }}</label>
                  } @else if (published() && (x.canMoveUp || choosable(x))) {
                    <label class="w small"><input type="checkbox" [checked]="x.waitlisted" [disabled]="busy()" (change)="toggleWaitlist(x.activityId)" />{{
                      choosable(x) ? i18n.t('status.keepFinal') : x.givesUp ? i18n.t('status.keepSwap', { title: x.givesUp }) : i18n.t('status.keepFinal') }}</label>
                    @if (choosable(x)) {
                      <label class="w small">{{ i18n.t('status.giveUpChoose') }}
                        <select [disabled]="busy()" (change)="chooseGiveUp(x, $any($event.target).value)">
                          @for (o of x.giveUpOptions!; track o.activityId) {
                            <option [value]="o.activityId" [selected]="o.activityId === giveUpFor(x)">{{ o.title }} ({{ i18n.t('status.points', { n: o.points }) }})</option>
                          }
                        </select>
                      </label>
                    }
                  }
                }
              </li>
            }
          </ul>
          @for (m of status()!.missing ?? []; track m.groupId) {
            <div class="missing stack">
              <p class="small"><b>{{ i18n.t('status.missing', { n: m.count, group: groupFor(m.groupId) ? ' (' + groupFor(m.groupId)!.name + ')' : '' }) }}</b></p>
              @if (m.open.length) {
                <ul class="open">
                  @for (o of m.open; track o.sessionId) {
                    <li>
                      <span>{{ o.title }} <span class="muted small">· {{ i18n.when(o) }}@if (o.location) { · {{ o.location }} }@if (o.points) { · {{ i18n.t('status.points', { n: o.points }) }} }</span></span>
                      <button class="btn ghost small" type="button" [disabled]="busy()" (click)="takeSeat(o.sessionId)">{{ i18n.t('status.take') }}</button>
                    </li>
                  }
                </ul>
              } @else { <p class="muted small">{{ i18n.t('status.nothingOpen') }}</p> }
            </div>
          }
          <p class="muted small">{{ i18n.t(published() ? 'status.noteFinal' : closed() ? 'status.noteClosed' : 'status.note') }}</p>
        </section>
      } @else if (status()?.voted && c.phase === 'Open') {
        <p class="card small">{{ i18n.t('status.hidden') }}</p>
      }
      <div class="layout">
        <div class="list">
          @for (sec of sections(); track sec.key) {
          @if (sec.group; as g) {
            <h2 class="gh" [attr.data-color]="g.color"><span class="grp"></span>{{ g.name }}
              <span class="muted small">{{ i18n.t('vote.groupRule', { min: g.minActivities, k: g.perParticipant }) }}</span></h2>
          } @else if (sections().length > 1) { <h2 class="gh">{{ i18n.t('vote.ungrouped') }}</h2> }
          @for (a of sec.activities; track a.id) {
            <div class="act" [class.on]="pointsFor(a.id) > 0">
              <div class="info">
                <h3>
                  {{ a.title }}
                  @if (a.restrictedTo) { <span class="tag">{{ i18n.t('common.only.' + a.restrictedTo) }}</span> }
                  @if (a.seats === null) { <span class="tag">{{ i18n.t('common.unlimited') }}</span> }
                  @if (status()?.full?.includes(a.id)) { <span class="tag full">{{ i18n.t('status.full') }}</span> }
                </h3>
                @if (a.parish || a.leader) { <div class="muted small">{{ i18n.byLine(a.parish, a.leader, true) }}</div> }
                @if (a.description) { <p class="desc">{{ a.description }}</p> }
                @if (a.sessions.length > 1) { <div class="muted small">{{ i18n.t('vote.pickSessions') }}</div> }
                <ul>
                  @for (s of a.sessions; track s.id) {
                    <li>
                      @if (a.sessions.length > 1) {
                        <label [class.off]="unavailable().has(s.id)">
                          <input type="checkbox" [checked]="!unavailable().has(s.id)" [disabled]="!canVote()" (change)="toggleSession(s.id)" />
                          <span>{{ sessionText(s) }}</span>
                        </label>
                      } @else { {{ sessionText(s) }} }
                    </li>
                  }
                </ul>
              </div>
              <div class="stepper">
                <button type="button" [disabled]="!canVote() || pointsFor(a.id) <= 0" (click)="change(a.id, -1)" [attr.aria-label]="i18n.t('vote.less', { title: a.title })">−</button>
                <output>{{ pointsFor(a.id) }}</output>
                <button type="button" [disabled]="!canVote() || pointsFor(a.id) >= c.rules.maxPerActivity || left() <= 0" (click)="change(a.id, 1)" [attr.aria-label]="i18n.t('vote.more', { title: a.title })">+</button>
              </div>
            </div>
          }
          }
        </div>
        <aside class="side">
          <div class="card stack">
            <div>
              <div class="muted small">{{ i18n.t('vote.remaining') }}</div>
              <div class="big">{{ left() }} <span class="muted of">{{ i18n.t('vote.of', { n: c.rules.pointBudget }) }}</span></div>
            </div>
            <div class="meter"><i [style.width.%]="spentPercent()"></i></div>
            <ul class="checks">
              <li [class.ok]="left() === 0">{{ i18n.t('vote.check.budget', { n: c.rules.pointBudget }) }}</li>
              <li [class.ok]="chosenCount() >= c.rules.minActivities">{{ i18n.t('vote.check.min', { n: c.rules.minActivities, count: chosenCount() }) }}</li>
              @for (g of groups(); track g.id) {
                <li [class.ok]="countIn(g.id) >= g.minActivities">{{ i18n.t('vote.check.group', { name: g.name, n: g.minActivities, count: countIn(g.id) }) }}</li>
              }
              <li [class.ok]="!noDateFits()">{{ i18n.t('vote.check.sessions') }}</li>
            </ul>
          </div>
          <div class="card stack">
            <h3>{{ i18n.t('vote.ranking') }}</h3>
            @if (ranking().length) {
              <ol class="rank">@for (r of ranking(); track r.id) { <li>{{ r.title }} <span class="muted">· {{ r.points }}</span></li> }</ol>
            } @else { <p class="muted small">{{ i18n.t('vote.rankingEmpty') }}</p> }
          </div>
          <div class="card stack">
            <p class="small">{{ i18n.t('vote.as', { name: name() }) }}</p>
            @if (loadedPrevious()) { <p class="small muted">{{ i18n.t('vote.loaded') }}</p> }
            @if (errors().length) { <ul class="errors">@for (e of errors(); track $index) { <li>{{ e }}</li> }</ul> }
            <button class="btn" type="button" [disabled]="!canSubmit() || busy() || !canVote()" (click)="submit()">{{ i18n.t('vote.submit') }}</button>
            @if (canVote()) { <button class="btn ghost" type="button" (click)="fillExample()">{{ i18n.t('vote.example') }}</button> }
            <p class="muted small">{{ i18n.t('vote.note') }}</p>
            <a routerLink="/slots" class="small">{{ i18n.t('vote.back') }}</a>
          </div>
        </aside>
      </div>
    } @else if (loadError()) {
      <p class="muted">{{ i18n.t('common.loadError') }}</p>
    } @else {
      <p class="muted">{{ i18n.t('common.loading') }}</p>
    }
  `,
  styles: `
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) 320px; gap: 24px; align-items: start }
    @media (max-width: 860px) { .layout { grid-template-columns: minmax(0, 1fr) } }
    .list { display: grid; gap: 8px }
    .status { margin-bottom: 18px }
    .st { list-style: none; margin: 0; padding: 0; display: grid; gap: 8px; color: var(--ink); font-size: 15px }
    .st li { display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 2px 10px; padding-bottom: 8px; border-bottom: 1px dashed var(--line) }
    .st .w { grid-column: 1 / -1 }
    .chip { font-size: 12px; font-weight: 600; padding: 2px 8px; border-radius: 999px; background: var(--bar); color: var(--muted); white-space: nowrap; align-self: start }
    .chip[data-status="fixed"] { background: var(--good-soft); color: var(--good) }
    .chip[data-status="waitlist"] { background: var(--warn-soft); color: var(--warn) }
    .chip[data-status="givenBack"] { text-decoration: line-through }
    .back { display: grid; gap: 6px; padding: 8px 10px; border: 1px solid var(--line); border-radius: 8px }
    .back p { margin: 0 }
    .back .short { color: var(--warn) }
    .btn.small { padding: 5px 10px; font-size: 13px }
    .missing { gap: 6px; padding: 10px 12px; border-radius: 8px; background: var(--warn-soft) }
    .missing p { margin: 0 }
    .offer { gap: 6px; padding: 10px 12px; border-radius: 8px; background: var(--accent-soft); border-left: 4px solid var(--accent) }
    .offer p { margin: 0 }
    .open { color: var(--ink); font-size: 14px; gap: 6px }
    .open li { display: flex; justify-content: space-between; align-items: center; gap: 10px }
    .tag.full { background: var(--warn-soft); color: var(--warn) }
    .gh { font-size: 18px; margin: 10px 0 0; display: flex; flex-wrap: wrap; align-items: baseline; gap: 4px 10px }
    .gh:first-child { margin-top: 0 }
    .gh .grp { width: 14px; height: 14px; margin: 0; align-self: center }
    .act { background: var(--surface); border: 1px solid var(--line); border-radius: var(--r); padding: 12px 14px; display: grid; grid-template-columns: minmax(0, 1fr) auto; gap: 6px 14px; align-items: center }
    .act.on { border-color: var(--accent) }
    .info { min-width: 0 }
    .desc { margin: 2px 0 6px; font-size: 14px; white-space: pre-line }
    ul { list-style: none; margin: 4px 0 0; padding: 0; display: grid; gap: 2px; font-size: 13px; color: var(--muted) }
    label { display: flex; gap: 8px; align-items: flex-start; cursor: pointer }
    label input { margin-top: 3px; accent-color: var(--accent) }
    label.off span { text-decoration: line-through }
    .stepper { display: flex; align-items: center; gap: 4px }
    .stepper button { width: 34px; height: 34px; border-radius: 8px; border: 1px solid var(--line); background: var(--bg); cursor: pointer; font-size: 18px; line-height: 1 }
    .stepper button:hover:not(:disabled) { border-color: var(--accent) }
    .stepper button:disabled { opacity: 0.35; cursor: default }
    .stepper output { min-width: 44px; text-align: center; font-weight: 700; font-size: 18px }
    .act.on .stepper output { color: var(--accent) }
    .side { position: sticky; top: 16px; display: grid; gap: 14px }
    @media (max-width: 860px) { .side { position: static } }
    .of { font-size: 16px; font-weight: 400 }
    .checks { list-style: none; padding: 0; margin: 0; display: grid; gap: 4px; font-size: 14px; color: var(--ink) }
    .checks li::before { content: '○'; display: inline-block; width: 20px; color: var(--muted) }
    .checks li.ok::before { content: '✓'; color: var(--good); font-weight: 700 }
    .rank { margin: 0; padding-left: 22px; display: grid; gap: 2px; font-size: 14px }
    .mini { position: sticky; top: env(safe-area-inset-top, 0px); z-index: 5; display: none; background: var(--surface); border: 1px solid var(--line); border-radius: var(--r); padding: 10px 12px; margin-bottom: 10px; gap: 10px; align-items: center }
    .mini .meter { flex: 1 }
    @media (max-width: 860px) { .mini { display: flex } }
  `,
})
export class VotePage implements OnInit {
  /** Bound from the route parameter :slotId. */
  readonly slotId = input.required<string>();

  protected i18n = inject(I18n);
  private api = inject(Api);
  private toast = inject(Toast);

  protected slot = signal<Slot | null>(null);
  protected activities = signal<Activity[]>([]);
  protected loadError = signal(false);
  protected points = signal<Record<string, number>>({});
  protected unavailable = signal(new Set<string>());
  private session = inject(SessionService);
  protected name = computed(() => this.session.participantName() ?? '');
  protected loadedPrevious = signal(false);
  protected errors = signal<string[]>([]);
  protected busy = signal(false);

  private rules = computed(() => this.slot()!.rules);
  protected spent = computed(() => Object.values(this.points()).reduce((a, b) => a + b, 0));
  protected left = computed(() => (this.slot() ? this.rules().pointBudget - this.spent() : 0));
  protected spentPercent = computed(() => (this.slot() ? Math.min(100, (this.spent() / this.rules().pointBudget) * 100) : 0));
  protected chosenCount = computed(() => Object.values(this.points()).filter(p => p > 0).length);
  protected ranking = computed(() =>
    this.activities().filter(a => this.pointsFor(a.id) > 0)
      .map(a => ({ id: a.id, title: a.title, points: this.pointsFor(a.id) }))
      .sort((x, y) => y.points - x.points));
  protected noDateFits = computed(() =>
    this.activities().some(a => this.pointsFor(a.id) > 0 && a.sessions.every(s => this.unavailable().has(s.id))));
  protected status = signal<MyStatus | null>(null);
  /** Points can only be given during registration, until the deadline. */
  protected canVote = computed(() => {
    const s = this.slot();
    return s?.phase === 'Open' && (!s.registrationDeadline || new Date(s.registrationDeadline) > new Date());
  });
  /** During registration the status is an interim one; afterwards it is the allocation. */
  protected interim = computed(() => this.slot()?.phase === 'Open');
  protected canWaitlist = computed(() => this.slot()?.phase === 'Published' || (this.interim() && !!this.slot()?.showInterim));
  /** Visible allocation: seats can be given back, ticks move up at once and free seats can be taken. */
  protected published = computed(() => this.slot()?.phase === 'Published');
  /** Lists are final: participants can only cancel a seat; the organisers decide what happens with it. */
  protected closed = computed(() => this.slot()?.phase === 'Closed');
  protected givingBack = signal<string | null>(null);
  protected replacement = signal(true);
  protected groups = computed(() => this.slot()?.rules.groups ?? []);
  protected groupFor(id: string | null): Group | undefined { return this.groups().find(g => g.id === id); }

  /** Seats required in the group of an activity (without groups: all seats; ungrouped ones: the rest). */
  private required(groupId: string | null): number {
    const rules = this.slot()!.rules;
    const group = this.groupFor(groupId);
    if (group) return group.perParticipant;
    const groups = this.groups();
    return groups.length ? Math.max(0, rules.perParticipant - groups.reduce((n, g) => n + g.perParticipant, 0)) : rules.perParticipant;
  }

  /** Giving back this seat leaves the person with fewer seats in its group than required. */
  protected leavesShort(x: MyActivityStatus): boolean {
    const sameGroup = (this.status()?.activities ?? []).filter(y => y.status === 'fixed' && (this.groupFor(y.groupId) ?? null) === (this.groupFor(x.groupId) ?? null));
    return sameGroup.length - 1 < this.required(x.groupId);
  }

  /** Ticks that would swap exactly this seat: moving up there gives it up, so the person is never left without one. */
  protected swapsFor(x: MyActivityStatus): MyActivityStatus[] {
    return (this.status()?.activities ?? []).filter(y => y.canMoveUp && y.givesUp === x.title);
  }

  protected async swapOnly(activityId: string): Promise<void> {
    await this.toggleWaitlist(activityId);
    this.givingBack.set(null);
  }

  private async loadStatus(): Promise<void> {
    try { this.status.set(await this.api.myStatus(this.slotId(), this.name())); } catch { this.status.set(null); }
  }

  protected async toggleWaitlist(activityId: string): Promise<void> {
    const current = this.status()?.activities.filter(x => x.waitlisted).map(x => x.activityId) ?? [];
    const next = current.includes(activityId) ? current.filter(x => x !== activityId) : [...current, activityId];
    await this.update(() => this.api.setWaitlist(this.slotId(), this.name(), next, this.giveUpChoices(next)), 'status.saved');
  }

  /** Seats chosen to give up that are not saved yet (the activity is not ticked). */
  private giveUpDraft = signal<Record<string, string>>({});

  /** More than one seat could go for this activity, so the person may choose (pre-selected: the weakest). */
  protected choosable(x: MyActivityStatus): boolean { return (x.giveUpOptions?.length ?? 0) > 1; }

  protected giveUpFor(x: MyActivityStatus): string {
    return this.giveUpDraft()[x.activityId] ?? x.giveUpChoice ?? x.giveUpOptions?.find(o => o.title === x.givesUp)?.activityId ?? x.giveUpOptions?.[0]?.activityId ?? '';
  }

  /** Explicit choices for the ticked activities; without one the weakest seat goes. */
  private giveUpChoices(ticked: string[]): Record<string, string> {
    const result: Record<string, string> = {};
    for (const x of this.status()?.activities ?? []) {
      const chosen = this.giveUpDraft()[x.activityId] ?? x.giveUpChoice;
      if (ticked.includes(x.activityId) && chosen) result[x.activityId] = chosen;
    }
    return result;
  }

  protected async chooseGiveUp(x: MyActivityStatus, activityId: string): Promise<void> {
    this.giveUpDraft.update(d => ({ ...d, [x.activityId]: activityId }));
    if (!x.waitlisted) return;
    const ticked = this.status()!.activities.filter(y => y.waitlisted).map(y => y.activityId);
    await this.update(() => this.api.setWaitlist(this.slotId(), this.name(), ticked, this.giveUpChoices(ticked)), 'status.saved');
  }

  protected async giveBack(activityId: string): Promise<void> {
    await this.update(() => this.api.giveBack(this.slotId(), this.name(), activityId, this.replacement()), this.closed() ? 'status.cancelledSaved' : 'status.givenBackSaved');
    this.givingBack.set(null);
  }

  protected takeSeat(sessionId: string): Promise<void> {
    return this.update(() => this.api.takeSeat(this.slotId(), this.name(), sessionId), 'status.taken');
  }

  protected answerOffer(offerId: string, answer: 'accept' | 'decline'): Promise<void> {
    return this.update(() => this.api.answerOffer(this.slotId(), this.name(), offerId, answer), answer === 'accept' ? 'status.offerAccepted' : 'status.offerDeclined');
  }

  /** Saves a change of the allocation status; free seats may move up at once, so the full status comes back. */
  private async update(call: () => Promise<MyStatus>, done: string): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      this.status.set(await call());
      this.toast.show(this.i18n.t(done));
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
      await this.loadStatus();
    } finally {
      this.busy.set(false);
    }
  }
  /** Activities per group (in group order), ungrouped ones last. */
  protected sections = computed(() => {
    const list = this.activities();
    const known = new Set(this.groups().map(g => g.id));
    const result = this.groups().map(g => ({ key: g.id, group: g as Group | null, activities: list.filter(a => a.groupId === g.id) }));
    const rest = list.filter(a => !a.groupId || !known.has(a.groupId));
    if (rest.length || !result.length) result.push({ key: '', group: null, activities: rest });
    return result.filter(s => s.activities.length);
  });
  protected countIn(groupId: string): number { return this.activities().filter(a => a.groupId === groupId && this.pointsFor(a.id) > 0).length; }
  protected canSubmit = computed(() =>
    !!this.slot() && this.left() === 0 && this.chosenCount() >= this.rules().minActivities && !this.noDateFits() && this.name().trim().length > 0 &&
    this.groups().every(g => this.countIn(g.id) >= g.minActivities));

  async ngOnInit(): Promise<void> {
    try {
      const [slot, activities] = await Promise.all([this.api.slot(this.slotId()), this.api.activities(this.slotId())]);
      this.slot.set(slot);
      this.activities.set(activities);
      // Demo: show the earlier ballot of this name so it can be changed.
      const previous = await this.api.lookupBallot(this.slotId(), this.name());
      if (previous) {
        this.points.set(previous.points);
        this.unavailable.set(new Set(previous.unavailableSessions));
        this.loadedPrevious.set(true);
      }
      await this.loadStatus();
    } catch {
      this.loadError.set(true);
    }
  }

  protected pointsFor(id: string): number { return this.points()[id] ?? 0; }

  protected sessionText(s: Activity['sessions'][number]): string {
    const seats = s.capacity ? ' · ' + this.i18n.t('common.seats', { n: s.capacity }) : '';
    return `${this.i18n.when(s)}${s.location ? " · " + s.location : ""}${seats}`;
  }

  protected change(id: string, direction: 1 | -1): void {
    const r = this.rules();
    const current = this.pointsFor(id);
    const next = Math.max(0, Math.min(r.maxPerActivity, current + direction * r.step));
    if (next - current > this.left()) return;
    const copy = { ...this.points() };
    if (next > 0) copy[id] = next; else delete copy[id];
    this.points.set(copy);
  }

  protected toggleSession(id: string): void {
    const copy = new Set(this.unavailable());
    if (copy.has(id)) copy.delete(id); else copy.add(id);
    this.unavailable.set(copy);
  }

  /** Demo helper: a valid distribution over the first activities, more points on the earlier ones. */
  protected fillExample(): void {
    const r = this.rules();
    // Each group's minimum first, then the remaining activities in list order.
    const first = this.groups().flatMap(g => this.activities().filter(a => a.groupId === g.id).slice(0, g.minActivities));
    const list = [...first, ...this.activities().filter(a => !first.includes(a))];
    const units = r.pointBudget / r.step, maxUnits = r.maxPerActivity / r.step;
    const n = Math.min(list.length, Math.max(r.minActivities, first.length, Math.ceil(units / maxUnits)));
    if (n === 0) return;
    const u: number[] = Array(n).fill(1);
    let rest = units - n;
    // Fill from the top, each activity at most maxUnits.
    for (let i = 0; rest > 0 && i < n; i++) {
      const add = Math.min(rest, maxUnits - u[i]);
      u[i] += add;
      rest -= add;
    }
    const points: Record<string, number> = {};
    list.slice(0, n).forEach((a, i) => (points[a.id] = u[i] * r.step));
    this.points.set(points);
    this.unavailable.set(new Set());
  }

  protected async submit(): Promise<void> {
    this.busy.set(true);
    this.errors.set([]);
    try {
      const res = await this.api.submitBallot(this.slotId(), { name: this.name().trim(), points: this.points(), unavailableSessions: [...this.unavailable()] });
      this.toast.show(this.i18n.t(res.replaced ? 'vote.replaced' : 'vote.saved'));
      await this.loadStatus();
    } catch (e) {
      this.errors.set(this.i18n.errors(e));
    } finally {
      this.busy.set(false);
    }
  }
}
