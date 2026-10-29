import { AngebotStatusDto } from '../../core/api/contracts';
import { Role } from '../../core/auth/auth';

/**
 * What the signed-in user may do with one Angebot, given their role and its state.
 *
 * ## This is presentation, and only presentation
 *
 * Every flag here has a server-side counterpart that is the real boundary: `[Authorize(Roles = …)]`
 * on the controller, `IOwnershipValidator` in the handler, and the aggregate's own state guards
 * (CLAUDE.md §16, §23). Deleting this file would make the screen offer actions the API then refuses
 * with 403/409 — ruder, not less secure. It exists so a user is not offered work they cannot do.
 *
 * ## Where each rule comes from
 *
 * - **Editing** (`PermissionMatrix.md` §3, **D115**): Inspector `S`, Admin `F`. Since D115 an Admin
 *   has the Inspector's full operational capability on an editable Angebot — sections, lines, the
 *   Catalog picker. Authorship stays with the Inspector who created the quote; every Admin edit is
 *   audited server-side. Ownership cannot be judged here, and need not be: an Inspector is only ever
 *   shown their own Angebote, because the reads are scoped server-side.
 * - **Editable states** (`StateMachine.md` §2.4): `Draft` and `ChangesRequested` only — for both
 *   roles. D115 did not make `InReview` editable for anyone.
 * - **Submit** (FR-5.1, D94, D115): from `Draft` or `ChangesRequested`, by the owning Inspector or
 *   any Admin.
 * - **Approve / request changes** (§4, both `F`): any Admin, on any `InReview` quote.
 * - **Return for correction** (FR-5.5, §4 Admin `F`, **D115**): any Admin, on an `InReview` quote —
 *   "Zur Korrektur zurückholen", the explicit `InReview → Draft` transition that lets a reviewer fix
 *   a small mistake without the Request Changes round trip.
 * - **Send** (FR-6.1, `F`): Admin, once `ApprovedInternally`.
 * - **Resend the link** (FR-6.1a, `F`, D99): Admin, while `Sent` — the only window in which a link
 *   exists and no decision has been recorded.
 * - **Convert to Project** (FR-7.1, BR-2, §5 `F`): Admin, once the customer has approved.
 * - **Save as Catalog item** (FR-4.10, §6 Inspector `F`): any Inspector, on a line that did not come
 *   from the Catalog. Not state-scoped — §6 grants it flatly, and the endpoint enforces no status.
 *   The Admin curates the Catalog directly instead (§6), so this stays Inspector-only.
 */
export interface AngebotCapabilities {
  readonly canEdit: boolean;
  readonly canSubmitForReview: boolean;
  readonly canReview: boolean;

  /**
   * "Zur Korrektur zurückholen" (FR-5.5, §4 Admin `F`, **D115**).
   *
   * **Only while `InReview`, and only for an Admin.** The server refuses anything else — an
   * Inspector with an empty 403, another state with a 409. It is offered alongside approve and
   * request changes because it is the third thing a reviewer can do with a submitted quote.
   */
  readonly canReturnForCorrection: boolean;
  readonly canSend: boolean;

  /**
   * Re-issuing the customer's token link (FR-6.1a, §4 Admin `F`, **D99**).
   *
   * **Only while `Sent`**, which is exactly the window in which a link exists and no decision has
   * been recorded: before that there is nothing to replace, and afterwards BR-4 makes the link
   * terminal. The server refuses anything else with a 409 — this flag exists so an Admin is not
   * offered work that would be refused, not as the enforcement.
   */
  readonly canResend: boolean;
  readonly canConvertToProject: boolean;
  readonly canSaveCustomItemToCatalog: boolean;

  /**
   * Reusing this quote as the starting point for another enquiry (FR-4.11, §3 Inspector `S`).
   *
   * **Not state-scoped**, deliberately: §3's own wording covers "their own past drafts, or any
   * `Sent`/decided Angebot they're shown for reference", which spans every status — a finished
   * quote is in fact the most useful template. The two rules that *do* apply are ownership of the
   * source and of the target, and StateMachine §2.4's one-active-quote rule on the target; all
   * three need data this function does not have, so all three stay server-side and surface as
   * 403/409. **Inspector-only** — an Admin may not duplicate (D115 non-goal).
   */
  readonly canDuplicate: boolean;
  /**
   * True when the quote is waiting on its owner but cannot be submitted yet, because editing is what
   * reopens it. The screen explains this rather than offering a disabled Submit button with no
   * account of why. Inspector-only: the explanation is addressed to the author.
   */
  readonly awaitingRework: boolean;
}

/** The two states in which an Angebot accepts structural edits (`StateMachine.md` §2.4). */
export const EDITABLE_STATUSES: readonly AngebotStatusDto[] = ['Draft', 'ChangesRequested'];

export function capabilitiesFor(
  role: Role | null,
  status: AngebotStatusDto,
): AngebotCapabilities {
  const isInspector = role === 'inspector';
  const isAdmin = role === 'admin';
  const editable = EDITABLE_STATUSES.includes(status);

  return {
    // Both roles since D115. Fails secure: `role` must positively be one of the two.
    canEdit: (isInspector || isAdmin) && editable,

    // Both editable states, not just Draft (D94) — and both roles (D115). A returned quote could
    // previously only be resubmitted after editing something, so an Inspector who read the comment
    // and concluded nothing needed changing had no way forward.
    canSubmitForReview: (isInspector || isAdmin) && editable,
    canReview: isAdmin && status === 'InReview',
    canReturnForCorrection: isAdmin && status === 'InReview',
    canSend: isAdmin && status === 'ApprovedInternally',
    canResend: isAdmin && status === 'Sent',
    canConvertToProject: isAdmin && status === 'CustomerApproved',
    canSaveCustomItemToCatalog: isInspector,
    canDuplicate: isInspector,
    awaitingRework: isInspector && status === 'ChangesRequested',
  };
}
