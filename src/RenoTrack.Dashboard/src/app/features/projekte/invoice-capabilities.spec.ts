import { INVOICE_STATUSES, InvoiceStatusDto } from '../../core/api/contracts';
import { invoiceCapabilitiesFor } from './invoice-capabilities';

/**
 * The Invoice action rules, over every status — the same exhaustive discipline the Angebot
 * capabilities get, and for the same reason: what matters is where the table says *no*.
 */
describe('Invoice capabilities', () => {
  const ALL: readonly InvoiceStatusDto[] = INVOICE_STATUSES;

  describe('Verwaltung (Admin)', () => {
    it('sends a Draft and nothing else', () => {
      const sendable = ALL.filter((status) => invoiceCapabilitiesFor('admin', status).canSend);

      expect(sendable).toEqual(['Draft']);
    });

    it('records payment on Sent and Overdue only — Paid is terminal (§3.2)', () => {
      const payable = ALL.filter((status) => invoiceCapabilitiesFor('admin', status).canMarkPaid);

      expect(payable).toEqual(['Sent', 'Overdue']);
    });

    it('voids anything not already settled, and never a settled Invoice', () => {
      const voidable = ALL.filter((status) => invoiceCapabilitiesFor('admin', status).canVoid);

      expect(voidable).toEqual(['Draft', 'Sent', 'Overdue']);
    });
  });

  /**
   * `PermissionMatrix.md` §5 gives Bauleitung `—` on every Invoice action while granting `R` on the
   * Project's Invoice list. Both halves matter, so both are asserted here: reading the list must
   * never imply acting on it.
   */
  it('gives Bauleitung no Invoice action in any state', () => {
    for (const status of ALL) {
      const capabilities = invoiceCapabilitiesFor('inspector', status);

      expect(capabilities.canSend).toBeFalse();
      expect(capabilities.canMarkPaid).toBeFalse();
      expect(capabilities.canVoid).toBeFalse();
    }
  });

  it('gives an unresolved session no Invoice action at all', () => {
    for (const status of ALL) {
      const capabilities = invoiceCapabilitiesFor(null, status);

      expect(capabilities.canSend).toBeFalse();
      expect(capabilities.canMarkPaid).toBeFalse();
      expect(capabilities.canVoid).toBeFalse();
    }
  });
});
