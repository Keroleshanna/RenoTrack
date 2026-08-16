import { InvoiceStatusDto } from '../../core/api/contracts';
import { Role } from '../../core/auth/auth';

/**
 * Which Invoice action a given Invoice can accept, for a given role.
 *
 * ## Every action is Admin-only, without exception
 *
 * `PermissionMatrix.md` §5 marks create, send, mark-paid and void as Admin `F` / Inspector `—`.
 * An Inspector may *read* a Project's Invoices (that row is `R`, clarified in Phase 8 Slice 6) and
 * gains no action from doing so. `InvoicesController` is `[Authorize(Roles = Admin)]` at class
 * level, so this is presentation over an already-closed door (CLAUDE.md §23).
 *
 * ## The state rules are `StateMachine.md` §3.3's, not new ones
 *
 * - **Send** — from `Draft` only; sending is what issues the customer's token link.
 * - **Mark paid** — from `Sent` or `Overdue`. `Paid` is terminal, which is what makes a duplicate
 *   confirmation impossible rather than merely discouraged.
 * - **Void** — from anything that is not already settled. `Paid` and `Void` are both terminal, and
 *   BR-9 makes voiding a status change that keeps the row and its number: this is never a delete.
 *
 * `Overdue` is included as a source state because the Domain defines the transition, even though
 * nothing in this system ever writes that status — there is no scheduler, by decision, and the
 * Dashboard derives "overdue" at read time instead. Excluding it here would mean an Invoice that
 * *did* carry the status could never be settled.
 */
export interface InvoiceCapabilities {
  readonly canSend: boolean;
  readonly canMarkPaid: boolean;
  readonly canVoid: boolean;
}

export function invoiceCapabilitiesFor(
  role: Role | null,
  status: InvoiceStatusDto,
): InvoiceCapabilities {
  const isAdmin = role === 'admin';

  return {
    canSend: isAdmin && status === 'Draft',
    canMarkPaid: isAdmin && (status === 'Sent' || status === 'Overdue'),
    canVoid: isAdmin && status !== 'Paid' && status !== 'Void',
  };
}
