import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Account, Api } from './api';

export type Role = { kind: 'participant'; name: string } | { kind: 'admin'; account: Account };

/**
 * Who uses this browser tab: a participant (personal link, or name in demo mode) or an organiser account.
 * The organiser's real sign-in is the server cookie; this only mirrors it for the UI.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private api = inject(Api);
  readonly role = signal<Role | null>(SessionService.stored());
  readonly participantName = computed(() => { const r = this.role(); return r?.kind === 'participant' ? r.name : null; });
  readonly account = computed(() => { const r = this.role(); return r?.kind === 'admin' ? r.account : null; });
  readonly isAdmin = computed(() => this.account() !== null);

  private static stored(): Role | null {
    try { return JSON.parse(sessionStorage.getItem('role') ?? 'null'); } catch { return null; }
  }

  signIn(role: Role): void {
    this.role.set(role);
    try { sessionStorage.setItem('role', JSON.stringify(role)); } catch { /* keep in memory */ }
  }

  signOut(): void {
    if (this.isAdmin()) this.api.logout().catch(() => { /* cookie expires anyway */ });
    this.role.set(null);
    this.api.setParticipantLink(null);
    try { sessionStorage.removeItem('role'); } catch { /* ignore */ }
  }

  /** Syncs the organiser role with the server cookie (e.g. after it expired or roles changed). */
  async refresh(): Promise<void> {
    try {
      const { account } = await this.api.currentAccount();
      if (account) { if (this.role()?.kind !== 'participant') this.signIn({ kind: 'admin', account }); }
      else if (this.isAdmin()) { this.role.set(null); try { sessionStorage.removeItem('role'); } catch { /* ignore */ } }
    } catch { /* offline: keep what we have */ }
  }
}

/** Route guard: participants only. */
export const participantGuard: CanActivateFn = () =>
  inject(SessionService).participantName() ? true : inject(Router).parseUrl('/');

/** Route guard: organisers only. */
export const adminGuard: CanActivateFn = () =>
  inject(SessionService).isAdmin() ? true : inject(Router).parseUrl('/');
