import { HttpErrorResponse, HttpHeaders, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { toApiError } from '../../core/api/api-error';
import { ProjectDetailDto, ProjectInvoiceDto } from '../../core/api/contracts';
import { Auth, Role } from '../../core/auth/auth';
import { DE } from '../../core/i18n/de';
import { DocumentDownload } from '../../shared/ui/document-download';
import { Notifier } from '../../shared/ui/notifier';
import { ProjectDetailPage } from './project-detail-page';

/**
 * The issued invoice's PDF on the Project detail (Phase 14 Slice 3a, D113), through the real
 * component and real HTTP calls against a testing backend.
 *
 * What each case protects:
 * - the archived document is fetched and saved under the server's name, for any status it exists in;
 * - nothing is offered where nothing was archived, and nothing at all to an Inspector (D112);
 * - a refused send closes its confirmation and says what to check (CLAUDE.md §23).
 *
 * The production interceptor's error mapping is reproduced by a test interceptor, so the component
 * receives the same `ApiError` it does in the running application.
 */
describe('Project detail — invoice document', () => {
  const issued: ProjectInvoiceDto = {
    id: 11,
    invoiceNumber: 'RE-2026-00011',
    grossAmount: 500,
    status: 'Void',
    dueDate: '2026-10-20',
    description: 'Abschlag 1',
    hasDocument: true,
  };

  const draft: ProjectInvoiceDto = { ...issued, id: 12, invoiceNumber: 'RE-2026-00012', status: 'Draft', hasDocument: false };

  const project: ProjectDetailDto = {
    id: 1,
    status: 'Active',
    agreedTotal: 1297,
    createdAt: '2026-10-03T08:00:00Z',
    completedAt: null,
    customerId: 7,
    customerName: 'Erika Musterfrau',
    leadId: 3,
    inspectionId: null,
    angebotId: 5,
    angebotNumber: 'ANG-2026-00001',
    alreadyInvoiced: 1000,
    remaining: 297,
    invoices: [issued, draft],
  };

  let fixture: ComponentFixture<ProjectDetailPage>;
  let http: HttpTestingController;
  let notifier: jasmine.SpyObj<Notifier>;
  let download: jasmine.SpyObj<DocumentDownload>;

  function render(role: Role): void {
    notifier = jasmine.createSpyObj<Notifier>('Notifier', ['success', 'error']);
    download = jasmine.createSpyObj<DocumentDownload>('DocumentDownload', ['save']);

    TestBed.configureTestingModule({
      imports: [ProjectDetailPage],
      providers: [
        provideHttpClient(
          withInterceptors([
            (request, next) =>
              next(request).pipe(
                catchError((error: unknown) =>
                  throwError(() => (error instanceof HttpErrorResponse ? toApiError(error) : error)),
                ),
              ),
          ]),
        ),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '1' }) } } },
        { provide: Auth, useValue: { role: signal<Role | null>(role) } },
        { provide: Notifier, useValue: notifier },
        { provide: DocumentDownload, useValue: download },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProjectDetailPage);
    fixture.detectChanges();

    http.expectOne('/api/v1/projects/1').flush(project);
    if (role === 'admin') {
      http.expectOne('/api/v1/customers/7').flush({ id: 7, leadId: 3, name: 'Erika Musterfrau', address: 'Weg 1' });
    }
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function pdfButtons(): HTMLButtonElement[] {
    return Array.from(element().querySelectorAll<HTMLButtonElement>('button')).filter(
      (button) => button.textContent?.trim() === DE.projectDetail.downloadPdf,
    );
  }

  afterEach(() => http.verify());

  it('downloads the archived PDF of a Void invoice under the name the server gives it', () => {
    render('admin');

    const [button] = pdfButtons();
    expect(button.getAttribute('aria-label')).toBe(`${DE.projectDetail.downloadPdfLabel} RE-2026-00011`);
    button.click();
    fixture.detectChanges();

    const request = http.expectOne('/api/v1/invoices/11/document');
    expect(request.request.method).toBe('GET');
    expect(request.request.responseType).toBe('blob');
    expect(button.disabled).toBeTrue();

    const pdf = new Blob(['%PDF-'], { type: 'application/pdf' });
    request.flush(pdf, {
      headers: new HttpHeaders({
        'Content-Disposition': "attachment; filename=Rechnung-RE-2026-00011.pdf; filename*=UTF-8''Rechnung-RE-2026-00011.pdf",
      }),
    });
    fixture.detectChanges();

    expect(download.save).toHaveBeenCalledOnceWith(pdf, 'Rechnung-RE-2026-00011.pdf');
    expect(button.disabled).toBeFalse();
    expect(notifier.error).not.toHaveBeenCalled();
  });

  it('offers no PDF for an invoice with no archived document', () => {
    render('admin');

    expect(pdfButtons().length).toBe(1);
  });

  it('never offers an Inspector the document, and never fetches it on their behalf', () => {
    render('inspector');

    expect(pdfButtons().length).toBe(0);
    http.expectNone('/api/v1/invoices/11/document');
  });

  it('reports a failed download instead of saving anything', () => {
    render('admin');

    pdfButtons()[0].click();
    http
      .expectOne('/api/v1/invoices/11/document')
      .flush(new Blob(), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(download.save).not.toHaveBeenCalled();
    expect(notifier.error).toHaveBeenCalledOnceWith(DE.projectDetail.documentDownloadFailed);
    expect(pdfButtons()[0].disabled).toBeFalse();
  });

  it('closes the send confirmation when the server refuses, and says what to check', () => {
    render('admin');

    Array.from(element().querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.trim() === DE.projectDetail.send)!
      .click();
    fixture.detectChanges();

    const dialog = Array.from(element().querySelectorAll('dialog')).find((candidate) =>
      candidate.textContent?.includes(DE.projectDetail.sendInvoiceTitle),
    )!;
    expect(dialog.open).toBeTrue();
    expect(dialog.textContent).toContain(DE.projectDetail.sendInvoiceBody);

    Array.from(dialog.querySelectorAll<HTMLButtonElement>('button'))
      .find((button) => button.textContent?.trim() === DE.actions.confirm)!
      .click();

    const request = http.expectOne('/api/v1/invoices/12/send');
    request.flush({ status: 409, title: 'Conflict' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    // The screen re-reads after a refusal, so the row shows the server's truth (D81).
    http.expectOne('/api/v1/projects/1').flush(project);
    fixture.detectChanges();

    expect(dialog.open).toBeFalse();
    expect(notifier.error).toHaveBeenCalledOnceWith(DE.projectDetail.sendRefused);
    expect(notifier.success).not.toHaveBeenCalled();
  });
});
