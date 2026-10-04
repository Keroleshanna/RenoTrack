import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { CustomerDto, ProjectDetailDto } from '../../core/api/contracts';
import { Auth, Role } from '../../core/auth/auth';
import { DE } from '../../core/i18n/de';
import { Notifier } from '../../shared/ui/notifier';
import { ProjectDetailPage } from './project-detail-page';

/**
 * The Project detail's "Rechnungsanschrift" panel (Phase 14 Slice 2b, D112), driven through the real
 * component and real HTTP calls against a testing backend.
 *
 * What each case protects, and why it is a test rather than a comment:
 * - a failed read must say so, not load forever (CLAUDE.md §23 — a caught error must not leave a
 *   feature looking alive or dead for the wrong reason);
 * - a refused write closes its dialog (CLAUDE.md §23), while the notifier still reports why;
 * - an Inspector's screen never asks for the address at all (D72, `PermissionMatrix.md` §5).
 */
describe('Project detail — billing address panel', () => {
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
    alreadyInvoiced: 0,
    remaining: 1297,
    invoices: [],
  };

  const customer: CustomerDto = { id: 7, leadId: 3, name: 'Erika Musterfrau', address: null };

  let fixture: ComponentFixture<ProjectDetailPage>;
  let http: HttpTestingController;
  let notifier: jasmine.SpyObj<Notifier>;

  function render(role: Role): void {
    notifier = jasmine.createSpyObj<Notifier>('Notifier', ['success', 'error']);

    TestBed.configureTestingModule({
      imports: [ProjectDetailPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '1' }) } } },
        { provide: Auth, useValue: { role: signal<Role | null>(role) } },
        { provide: Notifier, useValue: notifier },
      ],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProjectDetailPage);
    fixture.detectChanges();

    http.expectOne('/api/v1/projects/1').flush(project);
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function buttonLabelled(label: string): HTMLButtonElement {
    const button = Array.from(element().querySelectorAll('button')).find(
      (candidate) => candidate.textContent?.trim() === label,
    );
    if (!button) {
      throw new Error(`No button labelled "${label}".`);
    }
    return button;
  }

  afterEach(() => http.verify());

  it('shows an error in the panel, not an endless loading state, when the address cannot be read', () => {
    render('admin');

    http.expectOne('/api/v1/customers/7').flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const panel = element().querySelector('.billing-address');
    expect(panel?.textContent).toContain(DE.projectDetail.billingAddressLoadFailed);
    expect(panel?.querySelector('app-skeleton')).toBeNull();
    // Unreadable is not absent: the panel must not claim there is no address on file.
    expect(panel?.textContent).not.toContain(DE.projectDetail.noBillingAddress);
    expect(notifier.error).toHaveBeenCalledTimes(1);
  });

  it('closes the address dialog when the server refuses the correction, and still reports why', () => {
    render('admin');
    http.expectOne('/api/v1/customers/7').flush(customer);
    fixture.detectChanges();

    buttonLabelled(DE.projectDetail.addBillingAddress).click();
    fixture.detectChanges();

    const textarea = element().querySelector<HTMLTextAreaElement>('#billing-address')!;
    textarea.value = 'Musterstraße 1\n12345 Musterstadt';
    textarea.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const dialog = textarea.closest('dialog')!;
    expect(dialog.open).toBeTrue();

    buttonLabelled(DE.actions.save).click();
    const request = http.expectOne('/api/v1/customers/7/address');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ address: 'Musterstraße 1\n12345 Musterstadt' });

    request.flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    expect(dialog.open).toBeFalse();
    expect(notifier.error).toHaveBeenCalledTimes(1);
    expect(notifier.success).not.toHaveBeenCalled();
  });

  it('never requests the address for an Inspector, and renders no panel', () => {
    render('inspector');

    http.expectNone('/api/v1/customers/7');
    expect(element().querySelector('.billing-address')).toBeNull();
  });
});
