import { inspectionCapabilitiesFor } from './inspection-capabilities';

/**
 * BR-10 in one table: a completed Inspection accepts nothing, and Admin never acts on one at all.
 */
describe('Inspection capabilities', () => {
  const COMPLETED = '2026-08-15T09:30:00';

  it('gives the Inspector every on-site control while the visit is open', () => {
    const capabilities = inspectionCapabilitiesFor('inspector', null);

    expect(capabilities.canEditNotes).toBeTrue();
    expect(capabilities.canUploadPhoto).toBeTrue();
    expect(capabilities.canComplete).toBeTrue();
  });

  it('takes every control away once the visit is complete (BR-10)', () => {
    const capabilities = inspectionCapabilitiesFor('inspector', COMPLETED);

    expect(capabilities.canEditNotes).toBeFalse();
    expect(capabilities.canUploadPhoto).toBeFalse();
    expect(capabilities.canComplete).toBeFalse();
  });

  /**
   * §2 marks photos, notes and completion Inspector `S` / Admin `—`, deliberately: the evidence
   * chain-of-custody should point at whoever was actually on site. Admin's `F` on *viewing* is
   * unaffected, and the screen still renders for them.
   */
  it('gives Verwaltung no on-site control, open or complete', () => {
    for (const completedAt of [null, COMPLETED]) {
      const capabilities = inspectionCapabilitiesFor('admin', completedAt);

      expect(capabilities.canEditNotes).toBeFalse();
      expect(capabilities.canUploadPhoto).toBeFalse();
      expect(capabilities.canComplete).toBeFalse();
    }
  });

  it('grants nothing without a role', () => {
    const capabilities = inspectionCapabilitiesFor(null, null);

    expect(capabilities.canEditNotes).toBeFalse();
    expect(capabilities.canUploadPhoto).toBeFalse();
    expect(capabilities.canComplete).toBeFalse();
    expect(capabilities.canReassign).toBeFalse();
  });

  // ---- Reassignment (§2 — Admin F, Inspector —) ----

  it('lets Verwaltung reassign a visit that has not happened yet', () => {
    expect(inspectionCapabilitiesFor('admin', null).canReassign).toBeTrue();
  });

  it('refuses reassignment once the visit is complete (BR-10)', () => {
    // A finished visit is immutable evidence of who attended it, so this is refused for the same
    // reason a late photo is — the server answers 409 either way.
    expect(inspectionCapabilitiesFor('admin', COMPLETED).canReassign).toBeFalse();
  });

  it('never lets Bauleitung reassign, even their own open visit', () => {
    // Owning the work confers no say in who owns it — the mirror of the Lead assignment rule.
    expect(inspectionCapabilitiesFor('inspector', null).canReassign).toBeFalse();
    expect(inspectionCapabilitiesFor('inspector', COMPLETED).canReassign).toBeFalse();
  });
});
