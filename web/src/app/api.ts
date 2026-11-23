import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

export type Phase = 'Draft' | 'Open' | 'Allocation' | 'Published' | 'Closed' | 'Ended';
export const PHASES: Phase[] = ['Draft', 'Open', 'Allocation', 'Published', 'Closed', 'Ended'];

export interface Group { id: string; name: string; perParticipant: number; minActivities: number; color: string; }
export const GROUP_COLORS = ['green', 'blue', 'orange', 'purple', 'red', 'grey'];
/** maxExtra: "more than required", participants may offer up to this many seats more (0 = off). requiredStaffing: every date must be filled. */
export interface Rules { pointBudget: number; minActivities: number; maxPerActivity: number; perParticipant: number; step: number; groups: Group[] | null; maxExtra?: number; requiredStaffing?: boolean; }
export interface SlotSummary { id: string; name: string; phase: Phase; registrationDeadline: string | null; activityCount: number; ballotCount: number; parish?: string | null; visibility?: Visibility; canEdit?: boolean; }
export interface Slot { id: string; name: string; phase: Phase; registrationDeadline: string | null; rules: Rules; ballotCount: number; sampleCount: number; adminProtected: boolean; openToAll: boolean; parish: string | null; organiser: string | null; visibility: Visibility; showInterim: boolean; canEdit?: boolean | null; organiserEmail?: string | null; }
/** missing: required staffing, people still missing on this date (participant view). */
export interface Session { id: string; start: string; end: string | null; location: string; capacity: number | null; flexible: boolean; missing?: number | null; }
export interface Activity { id: string; title: string; restrictedTo: string | null; seats: number | null; sessions: Session[]; groupId: string | null; description: string | null; parish: string | null; leader: string | null; leaderEmail?: string | null; }
export interface SessionInput { id: string | null; start: string; end: string | null; location: string; capacity: number | null; flexible: boolean; }
export interface ActivityInput { title: string; restrictedTo: string | null; sessions: SessionInput[]; groupId: string | null; description: string | null; parish: string | null; leader: string | null; leaderEmail: string | null; }
export interface SlotUpdate { name: string; phase: Phase; registrationDeadline: string | null; openToAll: boolean; parish: string | null; organiser: string | null; organiserEmail: string | null; visibility: Visibility; showInterim: boolean; rules: Rules; }
export type Visibility = 'JuniaShared' | 'ParishOnly' | 'JuniaReadOnly';
export const VISIBILITIES: Visibility[] = ['ParishOnly', 'JuniaShared', 'JuniaReadOnly'];
export interface Account { id: string; email: string; name: string; isJuniaAdmin: boolean; parishes: string[]; isDemo: boolean; }
export interface AccountInput { email: string; name: string; isJuniaAdmin: boolean; parishes: string[]; }
export interface ListedAccount extends AccountInput { id: string; hasPassword: boolean; setupToken: string | null; }
export interface ParticipantInput { name: string; email: string | null; birthDate: string | null; baptismDate: string | null; parish: string | null; }
export interface ListedParticipant extends ParticipantInput { id: string; token: string; invitedAt: string | null; hasVoted: boolean; }
/** manual: copied by hand; pending: waiting to be sent (also after an error); sent; failed: gave up. */
export type MailStatus = 'manual' | 'pending' | 'sent' | 'failed';
export interface Mail { id: string; to: string; subject: string; body: string; createdAt: string; status: MailStatus; sentAt: string | null; error: string | null; replyTo: string | null; }
/** check = confirmed date of birth, baptism = confirmed date of baptism ('' if none is asked). */
export interface ParticipantLink { token: string; check: string; baptism?: string; }
export interface LinkInfo { slotId: string; slotName: string; needsCheck: boolean; needsBaptism: boolean; }
export interface ApiError { code: string; args?: Record<string, string | number> | null; }
export interface Demand { activityId: string; title: string; seats: number | null; interested: number; totalPoints: number; averagePoints: number; }
export interface Metrics { count: number; gotTopChoice: number; averageSatisfaction: number; worstTenPercentSatisfaction: number; withFallback: number; incomplete: number; }
export interface Assignment { activityId: string; title: string; groupId: string | null; sessionId: string; start: string; end: string | null; flexible: boolean; location: string; points: number; isFallback: boolean; locked?: boolean; }
export interface Participant {
  ballotId: string; name: string; isSample: boolean; satisfaction: number; assignments: Assignment[];
  /** Points per activity id from the ballot. */
  points: Record<string, number | undefined>;
  /** Session ids as calculated when the allocation was frozen (empty while live). */
  original: string[];
  /** Activity ids ticked on the waiting list and given back. */
  waitlist: string[]; declined: string[];
  /** Gave back or cancelled a seat, wants a replacement and still lacks a seat. */
  needsReplacement: boolean;
  /** For needsReplacement: seats organisers could give with one click. */
  suggestions?: Suggestion[] | null;
}
export interface Suggestion { sessionId: string; activityId: string; title: string; start: string; flexible: boolean; points: number; kind: 'otherDate' | 'pointed' | 'free' | 'overbook'; }
/** A free seat offered to one person when closed. */
export interface Offer { id: string; sessionId: string; person: string; status: 'open' | 'accepted' | 'declined' | 'expired' | 'withdrawn'; givesUp: string | null; expiresAt: string; createdAt: string; answeredAt: string | null; }
export interface MyOffer { id: string; activityId: string; title: string; start: string; end: string | null; flexible: boolean; location: string; givesUp: string | null; expiresAt: string; }
export interface MyActivityStatus {
  activityId: string; title: string; groupId: string | null; points: number; status: 'fixed' | 'waitlist' | 'free' | 'givenBack'; position: number | null; waitlisted: boolean;
  start: string | null; end: string | null; flexible: boolean; location: string | null;
  /** While visible: a tick can move the person up, giving up the seat titled givesUp (null if a seat is missing). */
  canMoveUp: boolean; givesUp: string | null; canGiveBack: boolean;
  /** While visible: seats that could go for this activity (weakest first) and the one the person chose, if any. */
  giveUpOptions?: GiveUpOption[] | null; giveUpChoice?: string | null;
}
export interface GiveUpOption { activityId: string; title: string; points: number; }
export interface OpenSeat { sessionId: string; activityId: string; title: string; start: string; end: string | null; flexible: boolean; location: string; points: number; }
export interface Missing { groupId: string | null; count: number; open: OpenSeat[]; }
export interface MyStatus { voted: boolean; activities: MyActivityStatus[]; full: string[]; missing: Missing[] | null; offer?: MyOffer | null; }
export interface Allocation { seed: number; perParticipant: number; pointBased: Metrics; firstComeFirstServed: Metrics; sessionLoad: Record<string, number | undefined>; participants: Participant[]; frozenAt: string | null; wasVisible?: boolean; }
export interface Seat { sessionId: string; locked: boolean; }
export interface ListPerson { name: string; parish: string | null; email: string | null; birthDate: string | null; baptismDate: string | null; }
export interface ListSession { id: string; start: string; end: string | null; flexible: boolean; location: string; capacity: number | null; people: ListPerson[]; }
export interface ListActivity { id: string; title: string; leader: string | null; leaderEmail: string | null; groupId: string | null; sessions: ListSession[]; }
export interface LeaderLists { frozen: boolean; activities: ListActivity[]; }
export interface LogEntry {
  id: string; at: string; actor: string; person: string | null; before: string | null; after: string | null;
  /** A save point exists: the allocation can go back to the state after this entry. */
  canRestore: boolean;
  kind: 'frozen' | 'recalculated' | 'unfrozen' | 'seats' | 'phase' | 'movedUp' | 'givenBack' | 'cancelled' | 'tookSeat' | 'settings' | 'activity'
    | 'activityAdded' | 'activityRemoved' | 'activitiesImported' | 'activitiesReplaced' | 'listed' | 'unlisted' | 'participantChanged' | 'renamed'
    | 'participantsImported' | 'restored' | 'offered' | 'offerAccepted' | 'offerDeclined' | 'offerExpired' | 'offerWithdrawn' | 'offerNone';
}
export interface RestoreChange { name: string; isSample: boolean; before: string; after: string; keptGivenBack: string | null; }
export interface RestorePreview { at: string; changes: RestoreChange[]; mails: number; movesUp: boolean; }

/** Thrown for 400/401 answers and server errors (with error number); carries the error codes for translation. */
export class ApiFailure extends Error {
  constructor(readonly errors: ApiError[]) { super(errors.map(e => e.code).join(', ')); }
}

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);

  // ---------- organiser accounts (sign-in cookie) ----------
  currentAccount() { return this.call(this.http.get<{ account: Account | null; setupNeeded: boolean }>('/api/auth/me')); }
  login(email: string, password: string) { return this.call(this.http.post<Account>('/api/auth/login', { email, password })); }
  logout() { return this.call(this.http.post('/api/auth/logout', {})); }
  demoLogin(key: string) { return this.call(this.http.post<Account>('/api/auth/demo', { key })); }
  setup(body: { key: string; email: string; name: string; password: string }) { return this.call(this.http.post<Account>('/api/auth/setup', body)); }
  passwordLink(token: string) { return this.call(this.http.get<{ name: string; email: string }>(`/api/auth/password/${token}`)); }
  setPassword(token: string, password: string) { return this.call(this.http.post<Account>(`/api/auth/password/${token}`, { password })); }
  accounts() { return this.call(this.http.get<ListedAccount[]>('/api/admin/accounts')); }
  createAccount(body: AccountInput) { return this.call(this.http.post<{ id: string; setupToken: string }>('/api/admin/accounts', body)); }
  updateAccount(id: string, body: AccountInput) { return this.call(this.http.put(`/api/admin/accounts/${id}`, body)); }
  recover(key: string, email: string) { return this.call(this.http.post<{ setupToken: string }>('/api/auth/recover', { key, email })); }
  newSetupLink(id: string) { return this.call(this.http.post<{ setupToken: string }>(`/api/admin/accounts/${id}/setup-link`, {})); }
  deleteAccount(id: string) { return this.call(this.http.delete(`/api/admin/accounts/${id}`)); }

  // ---------- general ----------
  info() { return this.call(this.http.get<{ adminProtected: boolean; demo: boolean; mailSending: boolean; parishes: string[]; version?: string }>('/api/info')); }

  // ---------- participants ----------
  /** Personal link of the signed-in participant (token and confirmed dates of birth and baptism), sent with participant requests. */
  setParticipantLink(link: ParticipantLink | null): void {
    this.participantLink = link;
    try { if (link) sessionStorage.setItem('participantLink', JSON.stringify(link)); else sessionStorage.removeItem('participantLink'); } catch { /* ignore */ }
  }
  private participantLink: ParticipantLink | null = Api.storedLink();
  private static storedLink(): ParticipantLink | null {
    try { return JSON.parse(sessionStorage.getItem('participantLink') ?? 'null'); } catch { return null; }
  }
  private me() {
    const l = this.participantLink;
    return { headers: new HttpHeaders(l ? { 'X-Participant': l.token, 'X-Participant-Check': l.check, 'X-Participant-Baptism': l.baptism ?? '' } : {}) };
  }
  linkInfo(token: string) { return this.call(this.http.get<LinkInfo>(`/api/p/${token}`)); }
  linkCheck(token: string, birthDate: string | null, baptismDate: string | null) {
    return this.call(this.http.post<{ name: string; slotId: string }>(`/api/p/${token}/check`, { birthDate, baptismDate }));
  }
  /** Previously submitted ballot of this name, or null. */
  async lookupBallot(id: string, name: string): Promise<{ points: Record<string, number>; unavailableSessions: string[]; extra?: number } | null> {
    try {
      return await firstValueFrom(this.http.post<{ points: Record<string, number>; unavailableSessions: string[]; extra?: number }>(`/api/slots/${id}/ballots/lookup`, { name }, this.me()));
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 404) return null;
      throw new ApiFailure([{ code: 'generic' }]);
    }
  }
  mySlots(name: string) { return this.call(this.http.post<SlotSummary[]>('/api/my-slots', { name }, this.me())); }
  myResult(id: string, name: string) {
    return this.call(this.http.post<{ voted: boolean; assignments: Assignment[] }>(`/api/slots/${id}/my-result`, { name }, this.me()));
  }
  myStatus(id: string, name: string) { return this.call(this.http.post<MyStatus>(`/api/slots/${id}/my-status`, { name }, this.me())); }
  /** giveUp: per ticked activity the activity whose seat goes for it; left out, earlier choices stay. */
  setWaitlist(id: string, name: string, activityIds: string[], giveUp?: Record<string, string>) {
    return this.call(this.http.post<MyStatus>(`/api/slots/${id}/waitlist`, { name, activityIds, giveUp }, this.me()));
  }
  giveBack(id: string, name: string, activityId: string, replacement: boolean) {
    return this.call(this.http.post<MyStatus>(`/api/slots/${id}/give-back`, { name, activityId, replacement }, this.me()));
  }
  takeSeat(id: string, name: string, sessionId: string) {
    return this.call(this.http.post<MyStatus>(`/api/slots/${id}/take-seat`, { name, sessionId }, this.me()));
  }
  answerOffer(id: string, name: string, offerId: string, answer: 'accept' | 'decline') {
    return this.call(this.http.post<MyStatus>(`/api/slots/${id}/offers/${offerId}/${answer}`, { name }, this.me()));
  }
  slot(id: string) { return this.call(this.http.get<Slot>(`/api/slots/${id}`)); }
  activities(id: string) { return this.call(this.http.get<Activity[]>(`/api/slots/${id}/activities`)); }
  submitBallot(id: string, body: { name: string; points: Record<string, number>; unavailableSessions: string[]; extra?: number }) {
    return this.call(this.http.post<{ id: string; replaced: boolean }>(`/api/slots/${id}/ballots`, body, this.me()));
  }

  // ---------- organisation ----------
  adminSlots() { return this.call(this.http.get<SlotSummary[]>('/api/admin/slots', this.admin())); }
  createSlot(name: string, withSampleData: boolean) {
    return this.call(this.http.post<{ id: string }>('/api/admin/slots', { name, withSampleData }, this.admin()));
  }
  adminSlot(id: string) { return this.call(this.http.get<Slot>(`/api/admin/slots/${id}`, this.admin())); }
  updateSlot(id: string, body: SlotUpdate) { return this.call(this.http.put(`/api/admin/slots/${id}`, body, this.admin())); }
  deleteSlot(id: string) { return this.call(this.http.delete(`/api/admin/slots/${id}`, this.admin())); }
  adminActivities(id: string) { return this.call(this.http.get<Activity[]>(`/api/admin/slots/${id}/activities`, this.admin())); }
  addActivity(slotId: string, body: ActivityInput) {
    return this.call(this.http.post<{ id: string }>(`/api/admin/slots/${slotId}/activities`, body, this.admin()));
  }
  updateActivity(id: string, body: ActivityInput) { return this.call(this.http.put(`/api/admin/activities/${id}`, body, this.admin())); }
  deleteActivity(id: string) { return this.call(this.http.delete(`/api/admin/activities/${id}`, this.admin())); }
  demand(id: string) { return this.call(this.http.get<Demand[]>(`/api/admin/slots/${id}/demand`, this.admin())); }
  resetDemo(id: string, body: { sampleCount: number; newSeed: boolean; removeRealBallots: boolean }) {
    return this.call(this.http.post(`/api/admin/slots/${id}/reset-demo`, body, this.admin()));
  }
  participants(id: string) { return this.call(this.http.get<ListedParticipant[]>(`/api/admin/slots/${id}/participants`, this.admin())); }
  addParticipant(id: string, body: ParticipantInput) { return this.call(this.http.post(`/api/admin/slots/${id}/participants`, body, this.admin())); }
  updateParticipant(participantId: string, body: ParticipantInput) { return this.call(this.http.put(`/api/admin/participants/${participantId}`, body, this.admin())); }
  exportParticipants(id: string) { return this.call(this.http.get(`/api/admin/slots/${id}/participants.csv`, { ...this.admin(), responseType: 'blob' })); }
  importParticipants(id: string, body: { csv: string; commit: boolean }) {
    return this.call(this.http.post<{ people: ParticipantInput[]; errors: ApiError[]; new: number; updated: number; saved: boolean }>(`/api/admin/slots/${id}/participants/import`, body, this.admin()));
  }
  createInvitations(id: string) { return this.call(this.http.post<{ created: number }>(`/api/admin/slots/${id}/invitations`, {}, this.admin())); }
  mails(id: string) { return this.call(this.http.get<Mail[]>(`/api/admin/slots/${id}/mails`, this.admin())); }
  removeParticipant(participantId: string) { return this.call(this.http.delete(`/api/admin/participants/${participantId}`, this.admin())); }
  exportActivities(id: string) { return this.call(this.http.get(`/api/admin/slots/${id}/activities.csv`, { ...this.admin(), responseType: 'blob' })); }
  importActivities(id: string, body: { csv: string; replace: boolean; commit: boolean }) {
    return this.call(this.http.post<{ activities: Activity[]; errors: ApiError[]; saved: boolean }>(`/api/admin/slots/${id}/import`, body, this.admin()));
  }
  resetAll() { return this.call(this.http.post('/api/admin/reset-all', {}, this.admin())); }
  allocation(id: string) { return this.call(this.http.get<Allocation>(`/api/admin/slots/${id}/allocation`, this.admin())); }
  recalculateAllocation(id: string) { return this.call(this.http.post<Allocation>(`/api/admin/slots/${id}/allocation/recalculate`, {}, this.admin())); }
  setSeats(id: string, ballotId: string, seats: Seat[]) {
    return this.call(this.http.put<Allocation>(`/api/admin/slots/${id}/allocation/${ballotId}`, { seats }, this.admin()));
  }
  allocationLog(id: string) { return this.call(this.http.get<LogEntry[]>(`/api/admin/slots/${id}/allocation/log`, this.admin())); }
  restorePreview(id: string, logId: string) {
    return this.call(this.http.get<RestorePreview>(`/api/admin/slots/${id}/allocation/restore/${logId}`, this.admin()));
  }
  restore(id: string, logId: string) { return this.call(this.http.post<Allocation>(`/api/admin/slots/${id}/allocation/restore/${logId}`, {}, this.admin())); }
  offers(id: string) { return this.call(this.http.get<Offer[]>(`/api/admin/slots/${id}/offers`, this.admin())); }
  fillSeat(id: string, sessionId: string) { return this.call(this.http.post(`/api/admin/slots/${id}/offers`, { sessionId }, this.admin())); }
  leaderLists(id: string) { return this.call(this.http.get<LeaderLists>(`/api/admin/slots/${id}/allocation/lists`, this.admin())); }
  leaderMails(id: string, body: { activityIds: string[]; withEmail: boolean; withBirthDate: boolean; withBaptismDate: boolean }) {
    return this.call(this.http.post<{ created: number; withoutEmail: number }>(`/api/admin/slots/${id}/allocation/leader-mails`, body, this.admin()));
  }
  createReminders(id: string) { return this.call(this.http.post<{ created: number }>(`/api/admin/slots/${id}/reminders`, {}, this.admin())); }
  createResultMails(id: string) { return this.call(this.http.post<{ created: number }>(`/api/admin/slots/${id}/result-mails`, {}, this.admin())); }
  retryMail(id: string, mailId: string) { return this.call(this.http.post<void>(`/api/admin/slots/${id}/mails/${mailId}/retry`, {}, this.admin())); }

  /** Organiser requests rely on the sign-in cookie; kept as a hook for request options. */
  private admin() { return {}; }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (e) {
      if (e instanceof HttpErrorResponse) {
        if (e.status === 401) throw new ApiFailure([{ code: 'unauthorized' }]);
        if (e.status === 403) throw new ApiFailure([{ code: 'forbidden' }]);
        if ((e.status === 400 || e.status === 500) && e.error?.errors) throw new ApiFailure(e.error.errors);
      }
      throw new ApiFailure([{ code: 'generic' }]);
    }
  }
}

/** "2027-05-10T17:00:00" → "2027-05-10T17:00" for datetime-local inputs. */
export const toLocalInput = (iso: string | null): string => (iso ? iso.slice(0, 16) : '');
export const fromLocalInput = (value: string): string | null => (value ? value : null);
