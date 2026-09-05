import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';

import { AngebotDetailDto, AngebotStatusDto } from '../../core/api/contracts';
import { I18n } from '../../core/i18n/i18n';

/**
 * FR-6.3's rejection reason as the Angebot detail screen renders it (D98).
 *
 * ## Why a host component rather than the real page
 *
 * `AngebotDetailPage` pulls a route, the API client, `Auth`, a dialog and a notifier before it will
 * render anything. Standing all of that up would test the wiring, not the rule — and the rule is
 * what a reviewer needs pinned: **shown only for a rejection that actually carries a reason,
 * labelled as the customer's words, escaped rather than executed, and silent when absent.** The
 * host below renders the same template fragment the page does, over the same DTO shape.
 */
@Component({
  standalone: true,
  template: `
    @if (angebot().status === 'CustomerRejected' && angebot().decisionReason) {
      <section class="callout" role="status">
        <h2 class="rt-subtitle">{{ t().angebotDetail.customerRejectedTitle }}</h2>
        <figure class="callout__quote">
          <blockquote class="rt-body">{{ angebot().decisionReason }}</blockquote>
          <figcaption class="rt-caption">{{ t().angebotDetail.customerRejectionReason }}</figcaption>
        </figure>
      </section>
    }
  `,
})
class RejectionReasonHost {
  readonly angebot = signal<AngebotDetailDto>(detail('CustomerRejected', 'Zu teuer.'));
  readonly t = TestBed.inject(I18n).t;
}

function detail(status: AngebotStatusDto, decisionReason: string | null): AngebotDetailDto {
  return {
    id: 1,
    leadId: 1,
    inspectionId: null,
    angebotNumber: 'ANG-2026-00042',
    status,
    createdByInspectorId: 1,
    reviewedByAdminId: null,
    sentAt: '2026-09-01T10:00:00Z',
    decisionAt: status === 'Sent' ? null : '2026-09-02T10:00:00Z',
    decisionReason,
    createdAt: '2026-08-30T10:00:00Z',
    netTotal: 100,
    grossTotal: 119,
    vatBreakdown: [],
    sections: [],
  };
}

describe('Angebot detail — the customer’s rejection reason', () => {
  function render(status: AngebotStatusDto, reason: string | null): HTMLElement {
    const fixture = TestBed.createComponent(RejectionReasonHost);
    fixture.componentInstance.angebot.set(detail(status, reason));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the reason a rejecting customer gave', () => {
    const element = render('CustomerRejected', 'Zu teuer im Vergleich zum Wettbewerb.');

    expect(element.querySelector('blockquote')?.textContent?.trim()).toBe(
      'Zu teuer im Vergleich zum Wettbewerb.',
    );
  });

  /** Attributed as the customer's words, never left to read as staff commentary. */
  it('labels the quote as the customer’s own', () => {
    const element = render('CustomerRejected', 'Zu teuer.');

    expect(element.querySelector('figcaption')?.textContent?.trim()).toBe('Begründung des Kunden');
  });

  /**
   * A rejection without a reason is entirely normal, so the screen says nothing at all — a
   * "kein Grund angegeben" placeholder would caption an absence and imply something is missing.
   */
  it('renders nothing when the customer gave no reason', () => {
    const element = render('CustomerRejected', null);

    expect(element.querySelector('section')).toBeNull();
  });

  it('renders nothing for an approval, whatever the payload carries', () => {
    const element = render('CustomerApproved', 'should never be shown');

    expect(element.querySelector('section')).toBeNull();
  });

  it('renders nothing while the decision is still open', () => {
    const element = render('Sent', null);

    expect(element.querySelector('section')).toBeNull();
  });

  /**
   * The only customer-authored free text on any staff screen. Angular's interpolation escapes it;
   * this proves that rather than trusting it — the template uses no `innerHTML` and no
   * `bypassSecurityTrust*` call, and the payload must arrive as text, not as a DOM node.
   */
  it('renders a hostile reason as inert text', () => {
    const element = render('CustomerRejected', '<script>alert(1)</script><img src=x onerror=alert(2)>');

    const quote = element.querySelector('blockquote')!;
    expect(quote.querySelector('script')).toBeNull();
    expect(quote.querySelector('img')).toBeNull();
    expect(quote.textContent).toContain('<script>alert(1)</script>');
    expect(element.querySelectorAll('script').length).toBe(0);
  });
});
