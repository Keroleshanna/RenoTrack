import { Role } from '../../core/auth/auth';

/**
 * What the signed-in user may do with one Inspection.
 *
 * ## BR-10 is the whole rule
 *
 * A completed Inspection is **immutable**: no photo, no note, no second completion. The aggregate
 * enforces that itself (`Inspection.AddPhoto`/`UpdateNotes`/`Complete` all guard on `CompletedAt`),
 * and this mirrors it so the screen does not offer controls that would be refused with a 409.
 *
 * ## Admin may look, never touch
 *
 * `PermissionMatrix.md` §2 marks photo upload, notes and completion Inspector `S` and Admin `—`,
 * explicitly so the evidence chain-of-custody points at whoever was actually on site. Admin `F` on
 * *viewing* an Inspection is unaffected.
 *
 * **Ownership is not decided here.** "The assigned Inspector" is a fact about the record, not about
 * the role, and it is enforced by `IOwnershipValidator` in each handler — an Inspector who opens
 * someone else's site visit is refused by the server, and the screen reports that refusal.
 */
export interface InspectionCapabilities {
  readonly canEditNotes: boolean;
  readonly canUploadPhoto: boolean;
  readonly canComplete: boolean;

  /**
   * Reassigning the visit to a different colleague (§2 "Reassign an Inspection to a different
   * Inspector — Admin F, Inspector —").
   *
   * **The one capability that belongs to Admin rather than the Inspector**, which is why the
   * `open` flag above cannot be reused for it: deciding who goes is an office decision, while
   * recording what was found on site is not. BR-10 still applies to both — a completed visit is
   * immutable, so rewriting who attended it is refused with a 409 exactly as a late photo is.
   */
  readonly canReassign: boolean;

  /**
   * Reopening a completed visit so its record can be corrected (BR-10's own named remedy).
   *
   * **The exact inverse of the on-site controls**: available only once the visit is complete, and
   * only to the Inspector, since it is what re-enables *their* edits. BR-10's immutability is not
   * weakened — photos, notes and reassignment still refuse while `completedAt` is set — this is
   * the deliberate, audited way to say otherwise.
   */
  readonly canReopen: boolean;
}

export function inspectionCapabilitiesFor(
  role: Role | null,
  completedAt: string | null,
): InspectionCapabilities {
  const stillOpen = completedAt === null;
  const open = role === 'inspector' && stillOpen;

  return {
    canEditNotes: open,
    canUploadPhoto: open,
    canComplete: open,
    canReassign: role === 'admin' && stillOpen,
    canReopen: role === 'inspector' && !stillOpen,
  };
}
