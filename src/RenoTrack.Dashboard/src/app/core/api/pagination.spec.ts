import { clampPageSize } from './renotrack-api';
import { DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE } from './contracts';

/**
 * The frontend must not be able to request a page size the API rejects.
 *
 * `Application.Common.Pagination` caps the page at 100 and every list validator enforces it with a
 * `ValidationException`. A picker once asked for 200 rows; the user saw an empty list and no
 * explanation, because a 400 is not something a list screen can render usefully. These tests pin
 * the guard that makes that unrepresentable rather than merely fixed at the one call site.
 */
describe('Page size contract', () => {
  it('passes an ordinary page size through untouched', () => {
    expect(clampPageSize(25)).toBe(25);
    expect(clampPageSize(1)).toBe(1);
    expect(clampPageSize(MAX_PAGE_SIZE)).toBe(MAX_PAGE_SIZE);
  });

  it('clamps anything above the API maximum', () => {
    // The exact value that produced the 400 in QA.
    expect(clampPageSize(200)).toBe(MAX_PAGE_SIZE);
    expect(clampPageSize(101)).toBe(MAX_PAGE_SIZE);
    expect(clampPageSize(Number.MAX_SAFE_INTEGER)).toBe(MAX_PAGE_SIZE);
  });

  it('clamps zero and negatives up to one', () => {
    // GreaterThan(0) server-side, so 0 is as invalid as 200 — just in the other direction.
    expect(clampPageSize(0)).toBe(1);
    expect(clampPageSize(-10)).toBe(1);
  });

  it('truncates a fractional page size rather than sending a decimal', () => {
    expect(clampPageSize(25.9)).toBe(25);
  });

  it('falls back to the default when the value is not a number', () => {
    expect(clampPageSize('viele')).toBe(DEFAULT_PAGE_SIZE);
    expect(clampPageSize(Number.NaN)).toBe(DEFAULT_PAGE_SIZE);
    expect(clampPageSize(Number.POSITIVE_INFINITY)).toBe(DEFAULT_PAGE_SIZE);
  });

  it('mirrors the server constant, so drift is visible here', () => {
    // If Pagination.MaxPageSize ever changes, this is the line that has to change with it.
    expect(MAX_PAGE_SIZE).toBe(100);
    expect(DEFAULT_PAGE_SIZE).toBe(25);
  });
});
