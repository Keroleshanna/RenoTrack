import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, finalize, map, of, shareReplay, tap, throwError } from 'rxjs';

import { AuthResponseDto } from '../api/contracts';

export type Role = 'admin' | 'inspector';

export interface Session {
  readonly userId: number;
  readonly name: string;
  readonly email: string;
  readonly role: Role;
}

/**
 * `sessionStorage`, never `localStorage` (**D73**).
 *
 * `localStorage` would leave a credential valid for `RefreshTokenDays` (default 7) readable by any
 * injected script *and* surviving browser restarts — which would also make the client-side-only
 * logout D60 leaves us essentially meaningless. `sessionStorage` dies with the tab.
 */
const REFRESH_TOKEN_KEY = 'renotrack.refresh';

/**
 * Authentication (**D73**).
 *
 * - **The access token lives in memory only**, so no script arriving later can read it back and no
 *   restart resurrects it.
 * - **The refresh token lives in `sessionStorage`**, which is what lets a page reload keep the
 *   session without putting a week-long credential on disk.
 * - **Cookies were rejected for Phase 10**: they would change `AuthController`, add CSRF surface,
 *   and contradict D60's body-returned token. No backend change was made for any of this.
 *
 * ## Exactly one refresh may be in flight
 *
 * Concurrent 401s subscribe to the *same* refresh and replay on success. This is not about
 * protecting the server — `TokenService.RotateAsync` already treats a lost race as a race rather
 * than as reuse — it exists so the losing 401 is not read as "session over".
 *
 * **The refresh request is itself never retried.** Presenting an already-revoked token *is* reuse,
 * and reuse revokes the entire chain.
 */
@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);

  private readonly accessToken = signal<string | null>(null);
  private readonly session = signal<Session | null>(null);

  /** The shared in-flight refresh, or null when none is running. */
  private refreshInFlight: Observable<string> | null = null;

  readonly user = this.session.asReadonly();
  readonly role = computed<Role | null>(() => this.session()?.role ?? null);
  readonly isAuthenticated = computed(() => this.accessToken() !== null);

  get token(): string | null {
    return this.accessToken();
  }

  get hasStoredRefreshToken(): boolean {
    return this.readRefreshToken() !== null;
  }

  login(email: string, password: string): Observable<Session> {
    return this.http
      .post<AuthResponseDto>('/api/v1/auth/login', { email, password })
      .pipe(map((response) => this.accept(response)));
  }

  /**
   * Restores a session after a page reload, from the stored refresh token.
   *
   * Returns `false` rather than throwing when there is nothing to restore: the caller is a route
   * guard, and "no session" is an ordinary answer there, not a fault.
   */
  restore(): Observable<boolean> {
    if (this.accessToken()) {
      return of(true);
    }
    if (!this.readRefreshToken()) {
      return of(false);
    }

    return this.refresh().pipe(
      map(() => true),
      // Any failure means the token was unknown, expired, revoked, or belongs to an inactive user.
      // All four mean the session is genuinely over.
      tap({ error: () => this.clear() }),
      map(() => true),
    );
  }

  /**
   * Rotates the refresh token, sharing one request across every concurrent caller.
   *
   * `shareReplay({ refCount: false })` so a late subscriber — a second 401 that arrived while the
   * request was in flight — receives the token rather than triggering a second rotation with a
   * token that has just been spent.
   */
  refresh(): Observable<string> {
    if (this.refreshInFlight) {
      return this.refreshInFlight;
    }

    const refreshToken = this.readRefreshToken();
    if (!refreshToken) {
      return throwError(() => new Error('No refresh token available.'));
    }

    this.refreshInFlight = this.http
      .post<AuthResponseDto>('/api/v1/auth/refresh', { refreshToken })
      .pipe(
        map((response) => {
          this.accept(response);
          return response.accessToken;
        }),
        tap({ error: () => this.clear() }),
        finalize(() => {
          this.refreshInFlight = null;
        }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );

    return this.refreshInFlight;
  }

  /**
   * Ends the session client-side.
   *
   * **There is deliberately no logout endpoint** — no requirement documents one, and D60's stance is
   * that authentication gains a command only when it gains a business rule. Clearing the refresh
   * token is what makes this meaningful at all, which is exactly why D73 refused `localStorage`.
   */
  logout(): void {
    this.clear();
  }

  private accept(response: AuthResponseDto): Session {
    const session: Session = {
      userId: response.userId,
      name: response.name,
      email: response.email,
      role: roleFrom(response.role),
    };

    this.accessToken.set(response.accessToken);
    this.session.set(session);
    sessionStorage.setItem(REFRESH_TOKEN_KEY, response.refreshToken);

    return session;
  }

  private clear(): void {
    this.accessToken.set(null);
    this.session.set(null);
    this.refreshInFlight = null;
    sessionStorage.removeItem(REFRESH_TOKEN_KEY);
  }

  private readRefreshToken(): string | null {
    return sessionStorage.getItem(REFRESH_TOKEN_KEY);
  }
}

/**
 * Maps the Identity role name onto the Dashboard's own union.
 *
 * **Fails secure**, mirroring `LeadsController.RequestingInspectorId()`: `Admin` is the wider role,
 * so it is only ever returned by matching it explicitly; anything unrecognised becomes `inspector`,
 * the narrower one. A typo or a future third role therefore under-privileges the UI rather than
 * over-privileging it — and the server enforces the real boundary regardless (CLAUDE.md §23).
 */
function roleFrom(role: string): Role {
  return role === 'Admin' ? 'admin' : 'inspector';
}
