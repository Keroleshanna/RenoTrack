import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { AngebotDetailDto, AngebotStatusDto } from '../../core/api/contracts';
import { Auth, Role } from '../../core/auth/auth';
import { authInterceptor } from '../../core/auth/auth-interceptor';
import { DE } from '../../core/i18n/de';
import { Notifier } from '../../shared/ui/notifier';
import { AngebotDetailPage } from './angebot-detail-page';

/**
 * Admin editing and the freshness contract on the Angebot screen (**D115**), driven through the real
 * component and real HTTP calls against a testing backend.
 *
 * What each case protects:
 * - every write carries the version of the document as this screen last read it — the whole point of
 *   the cross-request contract, and invisible unless asserted on the request itself;
 * - a 409 closes the dialog, shows the one generic message and re-reads the document, so the user is
 *   looking at the current version before trying again;
 * - the Admin's confirmations name whose quote it is (approved requirement), while an Inspector's
 *   screen neither offers "return for correction" nor asks for the staff list.
 */
describe('Angebot detail — Admin editing and expectedVersion (D115)', () => {
  const AUTHOR_ID = 5;
  const VERSION = 7;

  let fixture: ComponentFixture<AngebotDetailPage>;
  let http: HttpTestingController;
  let notifier: jasmine.SpyObj<Notifier>;

  function detail(status: AngebotStatusDto, version = VERSION): AngebotDetailDto {
    return {
      id: 1,
      leadId: 3,
      inspectionId: null,
      angebotNumber: 'ANG-2026-00042',
      status,
      createdByInspectorId: AUTHOR_ID,
      reviewedByAdminId: null,
      sentAt: null,
      decisionAt: null,
      decisionReason: null,
      createdAt: '2026-10-13T08:00:00Z',
      netTotal: 100,
      grossTotal: 119,
      vatBreakdown: [{ rate: 'Standard', netAmount: 100, vatAmount: 19 }],
      sections: [
        {
          id: 11,
          title: 'Pos. 1',
          sortOrder: 1,
          subtotal: 100,
          items: [
            {
              id: 21,
              catalogItemId: null,
              description: 'Fliesen verlegen',
              specification: null,
              quantity: 2,
              unit: 'm2',
              unitPrice: 50,
              vatRate: 'Standard',
              lineTotal: 100,
              savedToCatalog: false,
            },
          ],
        },
      ],
      version,
    };
  }

  function flushDocument(status: AngebotStatusDto, version = VERSION): void {
    http.expectOne('/api/v1/angebote/1').flush(detail(status, version));
    http.expectOne('/api/v1/angebote/1/review-comments').flush([]);
  }

  function render(role: Role, status: AngebotStatusDto): void {
    notifier = jasmine.createSpyObj<Notifier>('Notifier', ['success', 'error']);

    TestBed.configureTestingModule({
      imports: [AngebotDetailPage],
      providers: [
        // The real interceptor: it is what turns every HTTP failure into an `ApiError` in production,
        // and the 409 handling under test reads that error's kind.
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '1' }) } } },
        {
          provide: Auth,
          useValue: { role: signal<Role | null>(role), token: null, hasStoredRefreshToken: false },
        },
        { provide: Notifier, useValue: notifier },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AngebotDetailPage);
    fixture.detectChanges();

    flushDocument(status);
    http.expectOne('/api/v1/leads/3').flush({ id: 3, name: 'Erika Musterfrau' });
    if (role === 'admin') {
      http
        .expectOne((request) => request.url === '/api/v1/users')
        .flush([{ id: AUTHOR_ID, name: 'Ingrid Inspektor', role: 'Inspector', isActive: true }]);
    }
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function buttonLabelled(label: string): HTMLButtonElement | undefined {
    return Array.from(element().querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
  }

  function click(label: string): void {
    const button = buttonLabelled(label);
    if (!button) {
      throw new Error(`No button labelled "${label}".`);
    }
    button.click();
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('offers an Admin the builder controls and Submit on an Inspector’s draft', () => {
    render('admin', 'Draft');

    expect(buttonLabelled(DE.angebotDetail.submitForReview)).toBeDefined();
    expect(element().textContent).toContain(DE.angebotDetail.addSection);
  });

  it('names the author in the Admin’s submit confirmation and sends the read version', () => {
    render('admin', 'Draft');

    click(DE.angebotDetail.submitForReview);
    expect(element().textContent).toContain('Ingrid Inspektor');

    click(DE.actions.confirm);
    const request = http.expectOne('/api/v1/angebote/1/submit-for-review');
    expect(request.request.body).toEqual({ expectedVersion: VERSION });
    request.flush({});

    flushDocument('InReview', VERSION + 1);
  });

  it('offers "return for correction" to an Admin in review, names the author, and sends the version', () => {
    render('admin', 'InReview');

    click(DE.angebotDetail.returnForCorrection);
    expect(element().textContent).toContain('Ingrid Inspektor');

    click(DE.actions.confirm);
    const request = http.expectOne('/api/v1/angebote/1/return-for-correction');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ expectedVersion: VERSION });
    request.flush({});

    expect(notifier.success).toHaveBeenCalledWith(DE.angebotDetail.returnedForCorrection);
    flushDocument('Draft', VERSION + 1);
  });

  it('sends the read version with an approval', () => {
    render('admin', 'InReview');

    click(DE.angebotDetail.approve);
    click(DE.actions.confirm);

    const request = http.expectOne('/api/v1/angebote/1/approve');
    expect(request.request.body).toEqual({ expectedVersion: VERSION });
    request.flush({});
    flushDocument('ApprovedInternally', VERSION + 1);
  });

  /**
   * The cross-request contract's user-facing half: a stale approval is refused, the dialog closes,
   * one generic message explains it and the document is read again — never the server's own detail.
   */
  it('on a 409 closes the dialog, shows the generic message and re-reads the document', () => {
    render('admin', 'InReview');

    click(DE.angebotDetail.approve);
    click(DE.actions.confirm);

    http
      .expectOne('/api/v1/angebote/1/approve')
      .flush({ detail: 'internal wording' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(notifier.error).toHaveBeenCalledOnceWith(DE.angebotDetail.conflictReloaded);
    expect(fixture.componentInstance['confirming']()).toBeNull();

    // The re-read: the screen now shows the version someone else produced.
    flushDocument('Draft', VERSION + 3);
    fixture.detectChanges();
    expect(fixture.componentInstance['detail']()?.version).toBe(VERSION + 3);
  });

  it('removes a line with the read version as a query parameter', () => {
    render('admin', 'Draft');

    fixture.componentInstance['removeItem'](detail('Draft').sections[0].items[0]);

    const request = http.expectOne((r) => r.url === '/api/v1/angebote/1/items/21');
    expect(request.request.method).toBe('DELETE');
    expect(request.request.params.get('expectedVersion')).toBe(String(VERSION));
    request.flush({});
    flushDocument('Draft', VERSION + 1);
  });

  it('offers an Inspector no "return for correction" and never asks for the staff list', () => {
    render('inspector', 'InReview');

    expect(buttonLabelled(DE.angebotDetail.returnForCorrection)).toBeUndefined();
    http.expectNone((request) => request.url === '/api/v1/users');
  });
});
