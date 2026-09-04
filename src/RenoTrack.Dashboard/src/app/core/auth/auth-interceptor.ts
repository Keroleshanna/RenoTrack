import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, switchMap, throwError } from 'rxjs';

import { Auth } from './auth';
import { toApiError } from '../api/api-error';

/** The two anonymous auth routes. Attaching a bearer token to either is meaningless. */
const ANONYMOUS_PATHS = ['/api/v1/auth/login', '/api/v1/auth/refresh'];

/**
 * Attaches the bearer token, and turns one 401 into a refresh-and-replay.
 *
 * **The retry is bounded to exactly one attempt.** A 401 on the *replayed* request is not retried:
 * the token was fresh, so a second 401 means the server refuses this caller for a reason a new token
 * cannot fix. Looping there would spend refresh tokens until the chain was revoked.
 *
 * The refresh call itself is excluded entirely — re-presenting a spent refresh token *is* reuse, and
 * reuse revokes every outstanding token for the user (CLAUDE.md §22).
 *
 * Every error leaves here as an `ApiError`, so no screen ever parses `HttpErrorResponse`.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(Auth);
  const router = inject(Router);

  const isAnonymous = ANONYMOUS_PATHS.some((path) => request.url.startsWith(path));

  return next(withToken(request, isAnonymous ? null : auth.token)).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse)) {
        return throwError(() => error);
      }

      const canRefresh = error.status === 401 && !isAnonymous && auth.hasStoredRefreshToken;

      if (!canRefresh) {
        if (error.status === 401 && !isAnonymous) {
          // Nothing left to try: send them to sign in rather than leaving a screen that silently
          // renders nothing.
          auth.logout();
          void router.navigate(['/anmelden']);
        }
        return throwError(() => toApiError(error));
      }

      return auth.refresh().pipe(
        switchMap((token) => next(withToken(request, token))),
        catchError((refreshError: unknown) => {
          auth.logout();
          void router.navigate(['/anmelden']);
          return throwError(() =>
            refreshError instanceof HttpErrorResponse ? toApiError(refreshError) : toApiError(error),
          );
        }),
      );
    }),
  );
};

function withToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
}
