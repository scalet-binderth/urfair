import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

export type Phase = 'Draft' | 'Open' | 'Closed' | 'Published';
export const PHASES: Phase[] = ['Draft', 'Open', 'Closed', 'Published'];

export interface Group { id: string; name: string; perParticipant: number; minActivities: number; color: string; }
export const GROUP_COLORS = ['green', 'blue', 'orange', 'purple', 'red', 'grey'];
export interface Rules { pointBudget: number; minActivities: number; maxPerActivity: number; perParticipant: number; step: number; groups: Group[] | null; }
export interface SlotSummary { id: string; name: string; phase: Phase; registrationDeadline: string | null; activityCount: number; ballotCount: number; parish?: string | null; visibility?: Visibility; canEdit?: boolean; }
export interface Slot { id: string; name: string; phase: Phase; registrationDeadline: string | null; rules: Rules; ballotCount: number; sampleCount: number; adminProtected: boolean; openToAll: boolean; parish: string | null; organiser: string | null; visibility: Visibility; canEdit?: boolean | null; }
export interface Session { id: string; start: string; end: string | null; location: string; capacity: number | null; flexible: boolean; }
export interface Activity { id: string; title: string; restrictedTo: string | null; seats: number | null; sessions: Session[]; groupId: string | null; description: string | null; parish: string | null; leader: string | null; }
export interface SessionInput { id: string | null; start: string; end: string | null; location: string; capacity: number | null; flexible: boolean; }
export interface ActivityInput { title: string; restrictedTo: string | null; sessions: SessionInput[]; groupId: string | null; description: string | null; parish: string | null; leader: string | null; }
export interface SlotUpdate { name: string; phase: Phase; registrationDeadline: string | null; openToAll: boolean; parish: string | null; organiser: string | null; visibility: Visibility; rules: Rules; }
export type Visibility = 'JuniaShared' | 'ParishOnly' | 'JuniaReadOnly';
export const VISIBILITIES: Visibility[] = ['ParishOnly', 'JuniaShared', 'JuniaReadOnly'];
export interface Account { id: string; email: string; name: string; isJuniaAdmin: boolean; parishes: string[]; isDemo: boolean; }
export interface AccountInput { email: string; name: string; isJuniaAdmin: boolean; parishes: string[]; }
export interface ListedAccount extends AccountInput { id: string; hasPassword: boolean; setupToken: string | null; }
export interface ParticipantInput { name: string; email: string | null; birthDate: string | null; parish: string | null; }
export interface ListedParticipant extends ParticipantInput { id: string; token: string; invitedAt: string | null; hasVoted: boolean; }
export interface Mail { id: string; to: string; subject: string; body: string; createdAt: string; }
export interface ApiError { code: string; args?: Record<string, string | number> | null; }
export interface Demand { activityId: string; title: string; seats: number | null; interested: number; totalPoints: number; averagePoints: number; }
export interface Metrics { count: number; gotTopChoice: number; averageSatisfaction: number; worstTenPercentSatisfaction: number; withFallback: number; incomplete: number; }
export interface Assignment { activityId: string; title: string; groupId: string | null; sessionId: string; start: string; end: string | null; flexible: boolean; location: string; points: number; isFallback: boolean; }
export interface Participant { ballotId: string; name: string; isSample: boolean; satisfaction: number; assignments: Assignment[]; }
export interface MyActivityStatus { activityId: string; title: string; groupId: string | null; points: number; status: 'fixed' | 'waitlist' | 'free'; position: number | null; waitlisted: boolean; start: string | null; end: string | null; flexible: boolean; location: string | null; }
export interface MyStatus { voted: boolean; activities: MyActivityStatus[]; full: string[]; }
export interface Allocation { seed: number; perParticipant: number; pointBased: Metrics; firstComeFirstServed: Metrics; sessionLoad: Record<string, number | undefined>; participants: Participant[]; }

/** Thrown for 400/401 answers; carries the error codes for translation. */
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
  newSetupLink(id: string) { return this.call(this.http.post<{ setupToken: string }>(`/api/admin/accounts/${id}/setup-link`, {})); }
  deleteAccount(id: string) { return this.call(this.http.delete(`/api/admin/accounts/${id}`)); }

  // ---------- general ----------
  info() { return this.call(this.http.get<{ adminProtected: boolean; demo: boolean; parishes: string[] }>('/api/info')); }

  // ---------- participants ----------
  /** Personal link of the signed-in participant (token and confirmed date of birth), sent with participant requests. */
  setParticipantLink(link: { token: string; check: string } | null): void {
    this.participantLink = link;
    try { if (link) sessionStorage.setItem('participantLink', JSON.stringify(link)); else sessionStorage.removeItem('participantLink'); } catch { /* ignore */ }
  }
  private participantLink: { token: string; check: string } | null = Api.storedLink();
  private static storedLink(): { token: string; check: string } | null {
    try { return JSON.parse(sessionStorage.getItem('participantLink') ?? 'null'); } catch { return null; }
  }
  private me() {
    const l = this.participantLink;
    return { headers: new HttpHeaders(l ? { 'X-Participant': l.token, 'X-Participant-Check': l.check } : {}) };
  }
  linkInfo(token: string) { return this.call(this.http.get<{ slotId: string; slotName: string; needsCheck: boolean }>(`/api/p/${token}`)); }
  linkCheck(token: string, birthDate: string | null) {
    return this.call(this.http.post<{ name: string; slotId: string }>(`/api/p/${token}/check`, { birthDate }));
  }
  /** Previously submitted ballot of this name, or null. */
  async lookupBallot(id: string, name: string): Promise<{ points: Record<string, number>; unavailableSessions: string[] } | null> {
    try {
      return await firstValueFrom(this.http.post<{ points: Record<string, number>; unavailableSessions: string[] }>(`/api/slots/${id}/ballots/lookup`, { name }, this.me()));
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
  setWaitlist(id: string, name: string, activityIds: string[]) {
    return this.call(this.http.post<MyStatus>(`/api/slots/${id}/waitlist`, { name, activityIds }, this.me()));
  }
  slot(id: string) { return this.call(this.http.get<Slot>(`/api/slots/${id}`)); }
  activities(id: string) { return this.call(this.http.get<Activity[]>(`/api/slots/${id}/activities`)); }
  submitBallot(id: string, body: { name: string; points: Record<string, number>; unavailableSessions: string[] }) {
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

  /** Organiser requests rely on the sign-in cookie; kept as a hook for request options. */
  private admin() { return {}; }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (e) {
      if (e instanceof HttpErrorResponse) {
        if (e.status === 401) throw new ApiFailure([{ code: 'unauthorized' }]);
        if (e.status === 403) throw new ApiFailure([{ code: 'forbidden' }]);
        if (e.status === 400 && e.error?.errors) throw new ApiFailure(e.error.errors);
      }
      throw new ApiFailure([{ code: 'generic' }]);
    }
  }
}

/** "2027-05-10T17:00:00" → "2027-05-10T17:00" for datetime-local inputs. */
export const toLocalInput = (iso: string | null): string => (iso ? iso.slice(0, 16) : '');
export const fromLocalInput = (value: string): string | null => (value ? value : null);
