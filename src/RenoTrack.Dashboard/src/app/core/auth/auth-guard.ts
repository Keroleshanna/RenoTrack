import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { Auth, Role } from './auth';

/**
 * Route guards mirroring `PermissionMatrix.md`.
 *
 * **A guard is a courtesy to the user, never a security boundary** (CLAUDE.md §23,
 * `Architecture.md` §7.1/§7.3). Every endpoint behind these screens carries its own
 * `[Authorize(Roles = ...)]` and, where the matrix says "S", its own scoping. If this file were
 * deleted the system would still be secure — it would merely be ruder, showing a screen that then
 * failed with a 403.
 */

/**
 * Requires a session, restoring one from the stored refresh token first.
 *
 * The restore matters: on a page reload the access token is gone (it lives in memory by D73) while
 * the refresh token survives. Without this, refreshing the browser would bounce a valid session to
 * the login screen.
 */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(Auth);
  const router = inject(Router);

  return auth.restore().pipe(
    map((restored) =>
      restored ? true : router.createUrlTree(['/anmelden'], { queryParams: { weiter: state.url } }),
    ),
  );
};

/**
 * Requires one of the given roles, on top of the session check.
 *
 * Sends a signed-in user who lacks the role to the Cockpit rather than to the login screen — they
 * are authenticated, so asking them to sign in again would be nonsense.
 */
export function roleGuard(...roles: readonly Role[]): CanActivateFn {
  return (_route, state) => {
    const auth = inject(Auth);
    const router = inject(Router);

    return auth.restore().pipe(
      map((restored) => {
        if (!restored) {
          return router.createUrlTree(['/anmelden'], { queryParams: { weiter: state.url } });
        }
        const role = auth.role();
        return role && roles.includes(role) ? true : router.createUrlTree(['/cockpit']);
      }),
    );
  };
}
