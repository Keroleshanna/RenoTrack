import { FormControl } from '@angular/forms';

import { CUSTOMER_ADDRESS_MAX } from '../../core/api/contracts';
import { customerAddressValidator, toCorrectCustomerAddressRequest } from './customer-address-form';

/** Phase 14 Slice 2b (D112): the dialog's rule mirrors the server's, and sends the address alone. */
describe('Customer address form', () => {
  describe('CUSTOMER_ADDRESS_MAX', () => {
    it('mirrors Customer.MaxAddressLength on the server', () => {
      expect(CUSTOMER_ADDRESS_MAX).toBe(500);
    });
  });

  describe('customerAddressValidator', () => {
    const validate = (value: string) => customerAddressValidator(new FormControl(value, { nonNullable: true }));

    it('accepts a multi-line address', () => {
      expect(validate('Musterstraße 1\n12345 Musterstadt')).toBeNull();
    });

    it('refuses an empty address — correction never clears', () => {
      expect(validate('')).toEqual({ required: true });
    });

    it('refuses an address of only whitespace and line breaks', () => {
      expect(validate('  \n\t ')).toEqual({ required: true });
    });

    it('accepts exactly the maximum after trimming', () => {
      expect(validate(`  ${'a'.repeat(CUSTOMER_ADDRESS_MAX)}  `)).toBeNull();
    });

    it('refuses one character over the maximum', () => {
      expect(validate('a'.repeat(CUSTOMER_ADDRESS_MAX + 1))).toEqual({
        maxlength: { requiredLength: CUSTOMER_ADDRESS_MAX, actualLength: CUSTOMER_ADDRESS_MAX + 1 },
      });
    });
  });

  describe('toCorrectCustomerAddressRequest', () => {
    it('sends the trimmed address and nothing else', () => {
      const request = toCorrectCustomerAddressRequest('  Musterstraße 1\n12345 Musterstadt  ');

      expect(request).toEqual({ address: 'Musterstraße 1\n12345 Musterstadt' });
      expect(Object.keys(request)).toEqual(['address']);
    });
  });
});
