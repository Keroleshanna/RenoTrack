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
 * - **Editing** (`PermissionMatrix.md` §3): Inspector `S`, Admin `R`. An Admin may *read* a draft
 *   and never edit it — if they want a change they use Request Changes, which keeps authorship and
 *   accountability with the Inspector who wrote the quote.
 * - **Editable states** (`StateMachine.md` §2.4): `Draft` and `ChangesRequested` only.
 * - **Submit** (FR-5.1): from `Draft` only. A `ChangesRequested` quote reaches `Draft` again the
 *   moment its owner edits it — `Angebot.EnsureEditable()` performs that transition itself, and
 *   there is deliberately **no reopen endpoint** to model instead.
 * - **Approve / request changes** (§4, both `F`): any Admin, on any `InReview` quote. `F` means no
 *   ownership rule exists, so none is applied here either.
 * - **Send** (FR-6.1, `F`): Admin, once `ApprovedInternally`.
 * - **Convert to Project** (FR-7.1, BR-2, §5 `F`): Admin, once the customer has approved.
 * - **Save as Catalog item** (FR-4.10, §6 Inspector `F`): any Inspector, on a line that did not come
 *   from the Catalog. Not state-scoped — §6 grants it flatly, and the endpoint enforces no status.
 */
export interface AngebotCapabilities {
  readonly canEdit: boolean;
  readonly canSubmitForReview: boolean;
  readonly canReview: boolean;
  readonly canSend: boolean;
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
   * 403/409.
   */
  readonly canDuplicate: boolean;
  /**
   * True when the quote is waiting on its owner but cannot be submitted yet, because editing is what
   * reopens it. The screen explains this rather than offering a disabled Submit button with no
   * account of why.
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

  return {
    canEdit: isInspector && EDITABLE_STATUSES.includes(status),

    // Both editable states, not just Draft. A returned quote could previously only be resubmitted
    // after editing something — so an Inspector who read the comment and concluded nothing needed
    // changing had no way forward, and the workaround was an edit made purely to satisfy a guard.
    // The aggregate now accepts either state; this mirrors it.
    canSubmitForReview: isInspector && EDITABLE_STATUSES.includes(status),
    canReview: isAdmin && status === 'InReview',
    canSend: isAdmin && status === 'ApprovedInternally',
    canConvertToProject: isAdmin && status === 'CustomerApproved',
    canSaveCustomItemToCatalog: isInspector,
    canDuplicate: isInspector,
    awaitingRework: isInspector && status === 'ChangesRequested',
  };
}
