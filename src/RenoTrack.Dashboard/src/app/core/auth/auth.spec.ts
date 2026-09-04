import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { Auth } from './auth';
import { AuthResponseDto } from '../api/contracts';

/**
 * Authentication — the security-sensitive parts of D73.
 *
 * Three properties are worth a test rather than a comment, because each would fail silently:
 *
 * - the access token **never** reaches storage;
 * - concurrent refreshes share **one** request, since a second rotation would present a token the
 *   first one already spent, and reuse revokes the whole chain;
 * - logout clears the refresh token, which is the only thing that makes a client-side-only logout
 *   mean anything at all.
 */
describe('Auth', () => {
  const response: AuthResponseDto = {
    accessToken: 'access-1',
    accessTokenExpiresAt: '2026-08-15T12:15:00Z',
    refreshToken: 'refresh-1',
    refreshTokenExpiresAt: '2026-08-22T12:00:00Z',
    userId: 7,
    name: 'A. Weber',
    email: 'a.weber@renotrack.test',
    role: 'Admin',
  };

  let auth: Auth;
  let http: HttpTestingController;

  beforeEach(() => {
    sessionStorage.clear();

    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    auth = TestBed.inject(Auth);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    sessionStorage.clear();
  });

  function signIn(): void {
    auth.login('a.weber@renotrack.test', 'pw').subscribe();
    http.expectOne('/api/v1/auth/login').flush(response);
  }

  it('signs in and exposes the session', () => {
    signIn();

    expect(auth.isAuthenticated()).toBeTrue();
    expect(auth.user()?.name).toBe('A. Weber');
    expect(auth.role()).toBe('admin');
  });

  /**
   * D73's central rule. `localStorage` would leave a week-long credential readable by any injected
   * script and surviving restarts; the access token is not written anywhere at all.
   */
  it('keeps the access token out of both storages and the refresh token in sessionStorage only', () => {
    signIn();

    expect(sessionStorage.getItem('renotrack.refresh')).toBe('refresh-1');
    expect(localStorage.getItem('renotrack.refresh')).toBeNull();
    expect(JSON.stringify(sessionStorage)).not.toContain('access-1');
    expect(JSON.stringify(localStorage)).not.toContain('access-1');
  });

  /**
   * Concurrent 401s must share one rotation. A second request would present a token the first had
   * already spent — which the server correctly reads as reuse, revoking the entire chain.
   */
  it('shares a single refresh between concurrent callers', () => {
    signIn();

    const first = jasmine.createSpy('first');
    const second = jasmine.createSpy('second');
    auth.refresh().subscribe(first);
    auth.refresh().subscribe(second);

    // One request, not two.
    http.expectOne('/api/v1/auth/refresh').flush({ ...response, accessToken: 'access-2' });

    expect(first).toHaveBeenCalledWith('access-2');
    expect(second).toHaveBeenCalledWith('access-2');
  });

  it('ends the session when the refresh token is rejected', () => {
    signIn();

    auth.refresh().subscribe({ error: () => undefined });
    http.expectOne('/api/v1/auth/refresh').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(auth.isAuthenticated()).toBeFalse();
    expect(sessionStorage.getItem('renotrack.refresh')).toBeNull();
  });

  /** Clearing the stored token is the only thing that makes a client-side-only logout meaningful. */
  it('clears the stored refresh token on logout', () => {
    signIn();

    auth.logout();

    expect(auth.isAuthenticated()).toBeFalse();
    expect(auth.user()).toBeNull();
    expect(sessionStorage.getItem('renotrack.refresh')).toBeNull();
  });

  it('restores a session from a stored refresh token after a reload', () => {
    sessionStorage.setItem('renotrack.refresh', 'refresh-1');

    let restored: boolean | undefined;
    auth.restore().subscribe((value) => (restored = value));
    http.expectOne('/api/v1/auth/refresh').flush(response);

    expect(restored).toBeTrue();
    expect(auth.role()).toBe('admin');
  });

  it('reports no session when nothing is stored, without calling the server', () => {
    let restored: boolean | undefined;
    auth.restore().subscribe((value) => (restored = value));

    expect(restored).toBeFalse();
  });

  /**
   * Fails secure, mirroring `LeadsController.RequestingInspectorId()`: `admin` is the wider role, so
   * anything unrecognised must land on the narrower one rather than being granted the wider.
   */
  it('treats an unrecognised role as the narrower one', () => {
    auth.login('x@y.z', 'pw').subscribe();
    http
      .expectOne('/api/v1/auth/login')
      .flush({ ...response, role: 'Gärtner' as unknown as 'Admin' });

    expect(auth.role()).toBe('inspector');
  });
});
