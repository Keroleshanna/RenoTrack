import { ANGEBOT_STATUSES, AngebotStatusDto } from '../../core/api/contracts';
import { EDITABLE_STATUSES, capabilitiesFor } from './angebot-capabilities';

/**
 * The permission rules the Angebot screen renders, tested exhaustively over every status.
 *
 * **Exhaustive, not happy-path**, for the same reason `CLAUDE.md` §14 tests a guarded state machine
 * from every state: what matters about a permission table is where it says *no*, and a spot check of
 * the two interesting states proves nothing about the other five.
 *
 * These are presentation rules. The server enforces the real boundary — that is not a reason to test
 * these loosely, it is the reason they must agree with it exactly.
 */
describe('Angebot capabilities', () => {
  const OTHER: readonly AngebotStatusDto[] = ANGEBOT_STATUSES;

  describe('Bauleitung (Inspector)', () => {
    it('may edit exactly the two editable states (StateMachine.md §2.4)', () => {
      const editable = OTHER.filter((status) => capabilitiesFor('inspector', status).canEdit);

      expect(editable).toEqual([...EDITABLE_STATUSES]);
    });

    it('may submit from Draft only — ChangesRequested reopens by editing, not by a button', () => {
      const submittable = OTHER.filter(
        (status) => capabilitiesFor('inspector', status).canSubmitForReview,
      );

      expect(submittable).toEqual(['Draft']);
    });

    it('flags ChangesRequested as awaiting rework, so the screen can explain the next step', () => {
      expect(capabilitiesFor('inspector', 'ChangesRequested').awaitingRework).toBeTrue();
      expect(capabilitiesFor('inspector', 'Draft').awaitingRework).toBeFalse();
    });

    it('never reviews, sends or converts — every one of those is Admin "F" (§4, §5)', () => {
      for (const status of OTHER) {
        const capabilities = capabilitiesFor('inspector', status);

        expect(capabilities.canReview).toBeFalse();
        expect(capabilities.canSend).toBeFalse();
        expect(capabilities.canConvertToProject).toBeFalse();
      }
    });

    it('may always contribute a custom line to the Catalog — §6 grants it flatly', () => {
      for (const status of OTHER) {
        expect(capabilitiesFor('inspector', status).canSaveCustomItemToCatalog).toBeTrue();
      }
    });

    it('may duplicate from any status — §3 names past drafts and decided quotes alike', () => {
      // Not state-scoped on purpose: a finished quote is the most useful template there is. The
      // rules that do apply (ownership of source and target, one active quote per Lead) need data
      // this function does not have, so they stay server-side.
      for (const status of OTHER) {
        expect(capabilitiesFor('inspector', status).canDuplicate).toBeTrue();
      }
    });
  });

  describe('Verwaltung (Admin)', () => {
    it('never edits a draft — §3 marks that "R", so a change goes through Request Changes', () => {
      for (const status of OTHER) {
        expect(capabilitiesFor('admin', status).canEdit).toBeFalse();
      }
    });

    it('reviews only what is actually in review', () => {
      const reviewable = OTHER.filter((status) => capabilitiesFor('admin', status).canReview);

      expect(reviewable).toEqual(['InReview']);
    });

    it('sends only after internal approval (BR-1 is the gate)', () => {
      const sendable = OTHER.filter((status) => capabilitiesFor('admin', status).canSend);

      expect(sendable).toEqual(['ApprovedInternally']);
    });

    it('converts only a customer-approved quote (BR-2)', () => {
      const convertible = OTHER.filter(
        (status) => capabilitiesFor('admin', status).canConvertToProject,
      );

      expect(convertible).toEqual(['CustomerApproved']);
    });

    it('never submits for review and never saves a Catalog item — neither is an Admin action', () => {
      for (const status of OTHER) {
        const capabilities = capabilitiesFor('admin', status);

        expect(capabilities.canSubmitForReview).toBeFalse();
        expect(capabilities.canSaveCustomItemToCatalog).toBeFalse();

        // §3 marks duplication Inspector S / Admin — : an Admin authors nothing.
        expect(capabilities.canDuplicate).toBeFalse();
      }
    });
  });

  /**
   * A session with no resolved role must reach nothing.
   *
   * `Auth.roleFrom` already fails secure by mapping anything unrecognised to the *narrower* role, so
   * `null` here only occurs before a session exists — and "before a session exists" must not be a
   * state in which the screen offers a commercial action.
   */
  it('grants nothing without a role', () => {
    for (const status of OTHER) {
      const capabilities = capabilitiesFor(null, status);

      expect(capabilities.canEdit).toBeFalse();
      expect(capabilities.canSubmitForReview).toBeFalse();
      expect(capabilities.canReview).toBeFalse();
      expect(capabilities.canSend).toBeFalse();
      expect(capabilities.canConvertToProject).toBeFalse();
      expect(capabilities.canSaveCustomItemToCatalog).toBeFalse();
      expect(capabilities.canDuplicate).toBeFalse();
      expect(capabilities.awaitingRework).toBeFalse();
    }
  });
});
