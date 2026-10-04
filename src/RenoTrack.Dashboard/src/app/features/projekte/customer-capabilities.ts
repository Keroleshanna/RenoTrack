import { Role } from '../../core/auth/auth';

/**
 * Whether the signed-in user may see and correct a Customer's address (Phase 14 Slice 2b, D112).
 *
 * `PermissionMatrix.md` §5 marks both "View customer address" and "Correct customer address" Admin
 * `F` / Inspector `—`. `CustomersController` is `[Authorize(Roles = Admin)]` at class level, so this
 * is presentation over an already-closed door (CLAUDE.md §23) — and because an Inspector never sees
 * the panel, the Dashboard never asks for an address it would be refused (D72).
 *
 * **Fails secure**, like `Auth`'s role mapping: only an explicitly established Admin gets `true`.
 * There is no state to consult — a Customer has no status, and the address may be corrected at any
 * point in a Project's life.
 */
export interface CustomerCapabilities {
  readonly canCorrectAddress: boolean;
}

export function customerCapabilitiesFor(role: Role | null): CustomerCapabilities {
  return { canCorrectAddress: role === 'admin' };
}
