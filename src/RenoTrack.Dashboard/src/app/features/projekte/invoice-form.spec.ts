import { FormControl, FormGroup } from '@angular/forms';

import { INVOICE_DESCRIPTION_MAX } from '../../core/api/contracts';
import {
  invoiceDescriptionValidator,
  servicePeriodValidator,
  toCreateInvoiceRequest,
} from './invoice-form';

/** Phase 14 Slice 2 (D111): the invoice form agrees with the server's shape rules exactly. */
describe('invoice form', () => {
  describe('INVOICE_DESCRIPTION_MAX', () => {
    // Mirrored from Invoice.MaxDescriptionLength. If the server's limit changes, this fails rather
    // than the form silently accepting text the server will refuse.
    it('mirrors the server limit of 500', () => {
      expect(INVOICE_DESCRIPTION_MAX).toBe(500);
    });
  });

  describe('invoiceDescriptionValidator', () => {
    const validate = (value: string) => invoiceDescriptionValidator(new FormControl(value));

    it('requires a description', () => {
      expect(validate('')).toEqual({ required: true });
    });

    it('treats a description of only spaces as missing', () => {
      expect(validate('   ')).toEqual({ required: true });
    });

    it('accepts exactly the maximum', () => {
      expect(validate('a'.repeat(INVOICE_DESCRIPTION_MAX))).toBeNull();
    });

    it('refuses one character over the maximum', () => {
      expect(validate('a'.repeat(INVOICE_DESCRIPTION_MAX + 1))).not.toBeNull();
    });

    it('measures after trimming, as the server does', () => {
      expect(validate('  ' + 'a'.repeat(INVOICE_DESCRIPTION_MAX) + '  ')).toBeNull();
    });
  });

  describe('servicePeriodValidator', () => {
    const validate = (start: string, end: string) =>
      servicePeriodValidator(
        new FormGroup({
          servicePeriodStart: new FormControl(start),
          servicePeriodEnd: new FormControl(end),
        }),
      );

    it('accepts no service period at all', () => {
      expect(validate('', '')).toBeNull();
    });

    it('accepts a single service date', () => {
      expect(validate('2026-09-15', '')).toBeNull();
    });

    it('accepts a period, including one of a single day', () => {
      expect(validate('2026-09-01', '2026-09-30')).toBeNull();
      expect(validate('2026-09-15', '2026-09-15')).toBeNull();
    });

    it('refuses an end without a start', () => {
      expect(validate('', '2026-09-30')).toEqual({ servicePeriodEndWithoutStart: true });
    });

    it('refuses an end before the start', () => {
      expect(validate('2026-09-30', '2026-09-01')).toEqual({ servicePeriodEndBeforeStart: true });
    });
  });

  describe('toCreateInvoiceRequest', () => {
    it('trims the description and sends absent dates as null', () => {
      expect(
        toCreateInvoiceRequest({
          grossAmount: 297.5,
          dueDate: '2026-10-15',
          description: '  Malerarbeiten  ',
          servicePeriodStart: '',
          servicePeriodEnd: '',
        }),
      ).toEqual({
        grossAmount: 297.5,
        dueDate: '2026-10-15',
        description: 'Malerarbeiten',
        servicePeriodStart: null,
        servicePeriodEnd: null,
      });
    });

    // D111: the server alone splits the gross by rate. The request has no field a VAT figure could
    // travel in — pinned by its exact key set, so one cannot be added quietly.
    it('carries no VAT rate, VAT amount, net amount or line', () => {
      const request = toCreateInvoiceRequest({
        grossAmount: 100,
        dueDate: '2026-10-15',
        description: 'Abschlag 1',
        servicePeriodStart: '2026-09-01',
        servicePeriodEnd: '2026-09-30',
      });

      expect(Object.keys(request).sort()).toEqual([
        'description',
        'dueDate',
        'grossAmount',
        'servicePeriodEnd',
        'servicePeriodStart',
      ]);
    });
  });
});
