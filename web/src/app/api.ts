import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

export type Phase = 'Draft' | 'Open' | 'Closed' | 'Published';
export const PHASES: Phase[] = ['Draft', 'Open', 'Closed', 'Published'];

export interface Rules { pointBudget: number; minActivities: number; maxPerActivity: number; perParticipant: number; step: number; }
export interface SlotSummary { id: string; name: string; phase: Phase; registrationDeadline: string | null; activityCount: number; ballotCount: number; }
export interface Slot { id: string; name: string; phase: Phase; registrationDeadline: string | null; rules: Rules; ballotCount: number; sampleCount: number; adminProtected: boolean; }
export interface Session { id: string; start: string; end: string | null; location: string; capacity: number | null; flexible: boolean; }
export interface Activity { id: string; title: string; restrictedTo: string | null; seats: number | null; sessions: Session[]; }
export interface SessionInput { id: string | null; start: string; end: string | null; location: string; capacity: number | null; flexible: boolean; }
export interface ActivityInput { title: string; restrictedTo: string | null; sessions: SessionInput[]; }
export interface SlotUpdate { name: string; phase: Phase; registrationDeadline: string | null; rules: Rules; }
export interface ListedParticipant { id: string; name: string; hasVoted: boolean; }
export interface ApiError { code: string; args?: Record<string, string | number> | null; }
export interface Demand { activityId: string; title: string; seats: number | null; interested: number; totalPoints: number; averagePoints: number; }
export interface Metrics { count: number; gotTopChoice: number; averageSatisfaction: number; worstTenPercentSatisfaction: number; withFallback: number; incomplete: number; }
export interface Assignment { activityId: string; title: string; sessionId: string; start: string; end: string | null; flexible: boolean; location: string; points: number; isFallback: boolean; }
export interface Participant { ballotId: string; name: string; isSample: boolean; satisfaction: number; assignments: Assignment[]; }
export interface Allocation { seed: number; perParticipant: number; pointBased: Metrics; firstComeFirstServed: Metrics; sessionLoad: Record<string, number | undefined>; participants: Participant[]; }

/** Thrown for 400/401 answers; carries the error codes for translation. */
export class ApiFailure extends Error {
  constructor(readonly errors: ApiError[]) { super(errors.map(e => e.code).join(', ')); }
}

@Injectable({ providedIn: 'root' })
export class Api {
  private http = inject(HttpClient);
  /** Admin key kept for this browser tab only. */
  readonly adminKey = signal(Api.storedKey());

  private static storedKey(): string {
    try { return sessionStorage.getItem('adminKey') ?? ''; } catch { return ''; }
  }

  setAdminKey(key: string): void {
    this.adminKey.set(key);
    try { sessionStorage.setItem('adminKey', key); } catch { /* ignore */ }
  }

  // ---------- general ----------
  info() { return this.call(this.http.get<{ adminProtected: boolean }>('/api/info')); }

  // ---------- participants ----------
  /** Previously submitted ballot of this name, or null. */
  async lookupBallot(id: string, name: string): Promise<{ points: Record<string, number>; unavailableSessions: string[] } | null> {
    try {
      return await firstValueFrom(this.http.post<{ points: Record<string, number>; unavailableSessions: string[] }>(`/api/slots/${id}/ballots/lookup`, { name }));
    } catch (e) {
      if (e instanceof HttpErrorResponse && e.status === 404) return null;
      throw new ApiFailure([{ code: 'generic' }]);
    }
  }
  mySlots(name: string) { return this.call(this.http.post<SlotSummary[]>('/api/my-slots', { name })); }
  myResult(id: string, name: string) {
    return this.call(this.http.post<{ voted: boolean; assignments: Assignment[] }>(`/api/slots/${id}/my-result`, { name }));
  }
  slot(id: string) { return this.call(this.http.get<Slot>(`/api/slots/${id}`)); }
  activities(id: string) { return this.call(this.http.get<Activity[]>(`/api/slots/${id}/activities`)); }
  submitBallot(id: string, body: { name: string; points: Record<string, number>; unavailableSessions: string[] }) {
    return this.call(this.http.post<{ id: string; replaced: boolean }>(`/api/slots/${id}/ballots`, body));
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
  addParticipant(id: string, name: string) { return this.call(this.http.post(`/api/admin/slots/${id}/participants`, { name }, this.admin())); }
  removeParticipant(participantId: string) { return this.call(this.http.delete(`/api/admin/participants/${participantId}`, this.admin())); }
  allocation(id: string) { return this.call(this.http.get<Allocation>(`/api/admin/slots/${id}/allocation`, this.admin())); }

  private admin() { return { headers: new HttpHeaders(this.adminKey() ? { 'X-Admin-Key': this.adminKey() } : {}) }; }

  private async call<T>(request: Observable<T>): Promise<T> {
    try {
      return await firstValueFrom(request);
    } catch (e) {
      if (e instanceof HttpErrorResponse) {
        if (e.status === 401) throw new ApiFailure([{ code: 'unauthorized' }]);
        if (e.status === 400 && e.error?.errors) throw new ApiFailure(e.error.errors);
      }
      throw new ApiFailure([{ code: 'generic' }]);
    }
  }
}

/** "2027-05-10T17:00:00" → "2027-05-10T17:00" for datetime-local inputs. */
export const toLocalInput = (iso: string | null): string => (iso ? iso.slice(0, 16) : '');
export const fromLocalInput = (value: string): string | null => (value ? value : null);
