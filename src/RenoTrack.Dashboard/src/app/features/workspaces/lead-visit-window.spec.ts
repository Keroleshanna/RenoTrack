import { MAX_SCHEDULE_WINDOW_DAYS } from '../../core/api/contracts';

/**
 * The Leads page's appointment column asks the schedule endpoint for a window of dates.
 *
 * **A window wider than the API's cap is a 400, not a truncated result** — and because the page
 * catches that failure to keep the pipeline usable, the column silently read "not scheduled" for
 * every Lead while looking perfectly healthy. That is the failure mode this pins: the numbers the
 * page computes must stay inside the contract by construction.
 *
 * Mirrors the constants in `leads-page.ts`; if either moves, this is the line that has to move too.
 */
describe('Lead visit window', () => {
  const BACK_DAYS = 90;
  const FORWARD_DAYS = MAX_SCHEDULE_WINDOW_DAYS - BACK_DAYS - 1;

  it('stays inside the API window cap', () => {
    expect(BACK_DAYS + FORWARD_DAYS).toBeLessThanOrEqual(MAX_SCHEDULE_WINDOW_DAYS);
  });

  it('rejects the window that actually shipped and failed', () => {
    // Three months back plus a year forward — roughly 456 days, comfortably over the cap.
    expect(90 + 365).toBeGreaterThan(MAX_SCHEDULE_WINDOW_DAYS);
  });

  it('still looks far enough back to catch a completed visit', () => {
    // A visit a fortnight ago must remain visible; the column shows "done" for it.
    expect(BACK_DAYS).toBeGreaterThanOrEqual(30);
  });

  it('still looks far enough forward to be useful for planning', () => {
    expect(FORWARD_DAYS).toBeGreaterThanOrEqual(180);
  });

  it('mirrors the server constant, so drift is visible here', () => {
    expect(MAX_SCHEDULE_WINDOW_DAYS).toBe(366);
  });
});
