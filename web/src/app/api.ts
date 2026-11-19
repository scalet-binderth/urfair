import { HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Rules { pointBudget: number; minActivities: number; maxPerActivity: number; perParticipant: number; step: number; }
export interface Config { rules: Rules; registrationDeadline: string | null; ballotCount: number; sampleCount: number; adminProtected: boolean; }
export interface Session { id: string; start: string; end: string | null; location: string; capacity: number | null; flexible: boolean; }
export interface Activity { id: string; title: string; restrictedTo: string | null; seats: number | null; sessions: Session[]; }
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
  /** Id of the ballot submitted from this browser, to highlight it in the results. */
  readonly myBallotId = signal<string | null>(null);

  private static storedKey(): string {
    try { return sessionStorage.getItem('adminKey') ?? ''; } catch { return ''; }
  }

  setAdminKey(key: string): void {
    this.adminKey.set(key);
    try { sessionStorage.setItem('adminKey', key); } catch { /* ignore */ }
  }

  config() { return this.call(this.http.get<Config>('/api/config')); }
  activities() { return this.call(this.http.get<Activity[]>('/api/activities')); }
  submitBallot(body: { name: string; points: Record<string, number>; unavailableSessions: string[] }) {
    return this.call(this.http.post<{ id: string; replaced: boolean }>('/api/ballots', body));
  }
  demand() { return this.call(this.http.get<Demand[]>('/api/admin/demand', this.admin())); }
  saveRules(rules: Rules) { return this.call(this.http.put<Rules>('/api/admin/rules', rules, this.admin())); }
  resetDemo(body: { sampleCount: number; newSeed: boolean; removeRealBallots: boolean }) {
    return this.call(this.http.post('/api/admin/reset-demo', body, this.admin()));
  }
  allocation() { return this.call(this.http.get<Allocation>('/api/admin/allocation', this.admin())); }

  private admin() { return { headers: new HttpHeaders(this.adminKey() ? { 'X-Admin-Key': this.adminKey() } : {}) }; }

  private async call<T>(request: import('rxjs').Observable<T>): Promise<T> {
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
