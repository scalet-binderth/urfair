import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Api } from './api';

export type Role = { kind: 'participant'; name: string } | { kind: 'admin' };

/**
 * Demo sign-in: remembers in this browser tab whether someone entered as participant (with a name)
 * or as organiser. Not a security boundary; personal links and a real login replace it later.
 */
@Injectable({ providedIn: 'root' })
export class SessionService {
  private api = inject(Api);
  readonly role = signal<Role | null>(SessionService.stored());
  readonly participantName = computed(() => { const r = this.role(); return r?.kind === 'participant' ? r.name : null; });
  readonly isAdmin = computed(() => this.role()?.kind === 'admin');

  private static stored(): Role | null {
    try { return JSON.parse(sessionStorage.getItem('role') ?? 'null'); } catch { return null; }
  }

  signIn(role: Role): void {
    this.role.set(role);
    try { sessionStorage.setItem('role', JSON.stringify(role)); } catch { /* keep in memory */ }
  }

  signOut(): void {
    this.role.set(null);
    this.api.setAdminKey('');
    this.api.setParticipantLink(null);
    try { sessionStorage.removeItem('role'); } catch { /* ignore */ }
  }
}

/** Route guard: participants only. */
export const participantGuard: CanActivateFn = () =>
  inject(SessionService).participantName() ? true : inject(Router).parseUrl('/');

/** Route guard: organisers only. */
export const adminGuard: CanActivateFn = () =>
  inject(SessionService).isAdmin() ? true : inject(Router).parseUrl('/');
