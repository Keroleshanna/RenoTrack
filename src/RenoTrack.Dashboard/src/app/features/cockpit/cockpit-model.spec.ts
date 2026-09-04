import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideHttpClient } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';

import { Auth, Role } from '../../core/auth/auth';
import { Cockpit, CockpitModel } from './cockpit-model';
import { LEAD_STATUSES } from '../../core/api/contracts';

/**
 * The Cockpit's composition.
 *
 * The behaviour worth pinning is **what the role changes**, because it is a permission question
 * rather than a styling one: `PermissionMatrix.md` §5 gives Bauleitung no Invoice permission, so
 * their Cockpit must not even *request* money. A regression there would show up as a 403 in the
 * console and an empty panel — or worse, as a screen that quietly renders someone else's figures.
 */
describe('CockpitModel', () => {
  let model: CockpitModel;
  let http: HttpTestingController;
  let auth: Auth;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    model = TestBed.inject(CockpitModel);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(Auth);
  });

  afterEach(() => http.verify());

  /** Answers every request the Cockpit makes, and reports which URLs were asked for. */
  function respond(options: { leadCount?: number; overdueCount?: number } = {}): string[] {
    const urls: string[] = [];

    for (const request of http.match(() => true)) {
      urls.push(request.request.urlWithParams);
      const url = request.request.url;

      if (url === '/api/v1/leads') {
        request.flush({ items: [], page: 1, pageSize: 1, totalCount: options.leadCount ?? 3 });
      } else if (url === '/api/v1/angebote') {
        request.flush({ items: [], page: 1, pageSize: 25, totalCount: 2 });
      } else if (url === '/api/v1/projects') {
        request.flush({ items: [], page: 1, pageSize: 25, totalCount: 4 });
      } else if (url === '/api/v1/inspections') {
        request.flush([]);
      } else if (url === '/api/v1/invoices/receivables') {
        request.flush({
          invoicedGross: 100_000,
          paidGross: 70_000,
          openGross: 30_000,
          overdueGross: 12_000,
          voidedGross: 0,
          invoiceCount: 10,
          openCount: 3,
          overdueCount: options.overdueCount ?? 2,
        });
      } else if (url === '/api/v1/invoices') {
        request.flush({ items: [], page: 1, pageSize: 25, totalCount: 2 });
      } else {
        request.flush(null);
      }
    }

    return urls;
  }

  function load(role: Role): { cockpit?: Cockpit; urls: string[] } {
    spyOn(auth, 'role').and.returnValue(role);

    let cockpit: Cockpit | undefined;
    model.load(new Date('2026-08-15T09:00:00')).subscribe((result) => (cockpit = result));

    const urls = respond();
    return { cockpit, urls };
  }

  // -----------------------------------------------------------------------------------------------
  // Role
  // -----------------------------------------------------------------------------------------------

  /**
   * The security-relevant assertion. Not "the panel is hidden" — the request is never made, so a
   * 403 is avoided by not asking rather than handled after the fact.
   */
  it('never requests invoice data for site management', () => {
    const { urls } = load('inspector');

    expect(urls.some((url) => url.includes('/api/v1/invoices'))).toBeFalse();
  });

  it('requests invoice data for the office', () => {
    const { urls } = load('admin');

    expect(urls.some((url) => url.includes('/api/v1/invoices/receivables'))).toBeTrue();
  });

  it('gives the office money on its KPI row and site management none', () => {
    const admin = load('admin').cockpit;
    expect(admin?.kpis.some((kpi) => kpi.id === 'overdue')).toBeTrue();
    expect(admin?.money).not.toBeNull();

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    model = TestBed.inject(CockpitModel);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(Auth);

    const inspector = load('inspector').cockpit;
    expect(inspector?.kpis.some((kpi) => kpi.id === 'overdue')).toBeFalse();
    expect(inspector?.money).toBeNull();
  });

  // -----------------------------------------------------------------------------------------------
  // Counting and composition
  // -----------------------------------------------------------------------------------------------

  /**
   * Counts must come from `totalCount`, not from the number of rows returned — a `pageSize=1`
   * request returns one row while describing a table of any size.
   */
  it('asks for one row per status count rather than paging the table', () => {
    const { urls } = load('admin');

    const countRequests = urls.filter(
      (url) => url.startsWith('/api/v1/leads?') && url.endsWith('pageSize=1'),
    );

    expect(countRequests.length).toBe(LEAD_STATUSES.length);
  });

  /** An empty queue is the absence of work, not a zero to render. */
  it('omits decisions with nothing in them', () => {
    const { cockpit } = load('admin');

    expect(cockpit?.decisions.every((entry) => entry.count > 0)).toBeTrue();
  });

  /**
   * The three money segments must sum to the invoiced total. Overdue is a *subset* of open, so
   * showing both at face value would double-count it and the bar would over-run its own total.
   */
  it('shows open money net of overdue so the segments sum to the invoiced total', () => {
    const { cockpit } = load('admin');
    const segments = cockpit?.money ?? [];

    const sum = segments.reduce((total, segment) => total + segment.value, 0);

    expect(sum).toBe(100_000);
    expect(segments.find((s) => s.id === 'open')?.value).toBe(18_000);
    expect(segments.find((s) => s.id === 'overdue')?.value).toBe(12_000);
  });

  /** `Lost` is absent from the funnel: it shows progression, and a lost enquiry left it. */
  it('builds a funnel that ends at Won and never includes Lost', () => {
    const { cockpit } = load('admin');

    const ids = cockpit?.funnel.map((row) => row.id) ?? [];

    expect(ids).toEqual([
      'New',
      'InspectionScheduled',
      'InspectionDone',
      'AngebotInProgress',
      'AngebotSent',
      'Won',
    ]);
    expect(ids).not.toContain('Lost');
  });

  it('gives every decision a route, so the cockpit always leads to real work', () => {
    const { cockpit } = load('admin');

    expect(cockpit?.decisions.every((entry) => entry.route.length > 0)).toBeTrue();
  });
});
