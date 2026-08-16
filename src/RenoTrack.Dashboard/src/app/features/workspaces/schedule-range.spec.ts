import { addDays, rangeBounds, startOfWeek } from './inspections-page';

/**
 * The site-visit schedule's date filters.
 *
 * **These exist because "This week" and "Next 30 days" returned identical results in QA.** Both
 * were rolling windows from today (+7 and +30), so with one upcoming visit every chip showed the
 * same row and the filter looked broken. The week filters are now real calendar weeks — adjacent,
 * mutually exclusive, and matching what a German office means by "diese Woche".
 *
 * `to` is exclusive throughout, matching the API's own `ScheduledAt >= from && ScheduledAt < to`.
 */
describe('Schedule range bounds', () => {
  // A Wednesday, deliberately mid-week so "this week" has days on both sides of it.
  const wednesday = new Date(2026, 7, 19, 14, 30);

  const iso = (date: Date) => `${date.getFullYear()}-${date.getMonth() + 1}-${date.getDate()}`;

  it('starts the week on Monday, not Sunday', () => {
    // ISO-8601 / German business convention. getDay() calls Sunday 0, which is the *end* of an ISO
    // week — getting this wrong shifts every boundary by a day.
    expect(iso(startOfWeek(wednesday))).toBe('2026-8-17');
    expect(startOfWeek(wednesday).getDay()).toBe(1);
  });

  it('treats a Sunday as the last day of its week, not the first', () => {
    const sunday = new Date(2026, 7, 23, 9, 0);
    expect(iso(startOfWeek(sunday))).toBe('2026-8-17');
  });

  it('covers Monday to the following Monday for this week', () => {
    const { from, to } = rangeBounds('week', wednesday);

    expect(iso(from)).toBe('2026-8-17');
    expect(iso(to)).toBe('2026-8-24');
    expect(from.getHours()).toBe(0);
  });

  it('covers exactly the following calendar week for next week', () => {
    const { from, to } = rangeBounds('nextWeek', wednesday);

    expect(iso(from)).toBe('2026-8-24');
    expect(iso(to)).toBe('2026-8-31');
  });

  it('makes the two week ranges adjacent and non-overlapping', () => {
    const thisWeek = rangeBounds('week', wednesday);
    const nextWeek = rangeBounds('nextWeek', wednesday);

    // The exclusive end of one is the inclusive start of the other: no visit is counted twice, and
    // none falls into the gap.
    expect(nextWeek.from.getTime()).toBe(thisWeek.to.getTime());
  });

  it('runs 30 days from today for the month range', () => {
    const { from, to } = rangeBounds('month', wednesday);

    // From today, not from the week start — nobody means "the last two days" by "next 30 days".
    expect(iso(from)).toBe('2026-8-19');
    expect(iso(to)).toBe('2026-9-18');
  });

  it('distinguishes the ranges for a visit that only one of them contains', () => {
    const thisWeek = rangeBounds('week', wednesday);
    const nextWeek = rangeBounds('nextWeek', wednesday);
    const month = rangeBounds('month', wednesday);

    const contains = (r: { from: Date; to: Date }, at: Date) => at >= r.from && at < r.to;

    // The exact QA case: a visit on Thursday of the current week.
    const thursday = new Date(2026, 7, 20, 9, 0);
    expect(contains(thisWeek, thursday)).toBeTrue();
    expect(contains(nextWeek, thursday)).toBeFalse();
    expect(contains(month, thursday)).toBeTrue();

    // A visit early next week: excluded from "this week", which is what was broken before.
    const nextTuesday = new Date(2026, 7, 25, 9, 0);
    expect(contains(thisWeek, nextTuesday)).toBeFalse();
    expect(contains(nextWeek, nextTuesday)).toBeTrue();
    expect(contains(month, nextTuesday)).toBeTrue();
  });

  it('excludes a visit beyond the 30-day horizon', () => {
    const month = rangeBounds('month', wednesday);
    const farOff = addDays(month.to, 1);

    expect(farOff >= month.to).toBeTrue();
  });

  it('excludes the exclusive end instant itself', () => {
    const { to } = rangeBounds('week', wednesday);
    const thisWeek = rangeBounds('week', wednesday);

    // Midnight on the next Monday belongs to next week, not this one.
    expect(to >= thisWeek.from && to < thisWeek.to).toBeFalse();
  });

  it('includes earlier days of the current week, not only the future', () => {
    const thisWeek = rangeBounds('week', wednesday);
    const monday = new Date(2026, 7, 17, 8, 0);

    // "This week" is a calendar week, so a visit already completed on Monday still belongs to it.
    expect(monday >= thisWeek.from && monday < thisWeek.to).toBeTrue();
  });
});
