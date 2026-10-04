import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

import { CorrectCustomerAddressRequestDto, CUSTOMER_ADDRESS_MAX } from '../../core/api/contracts';

/**
 * The customer-address dialog's rule and its mapping to the request (Phase 14 Slice 2b, D112), kept
 * out of the component so they are unit-tested rather than trusted.
 *
 * **These mirror the server's shape rule and nothing more** (CLAUDE.md §23): no postcode, country or
 * format check exists on the server, so none exists here. The address is one free-text value whose
 * own line breaks the invoice document prints as written.
 */

/**
 * Required and within {@link CUSTOMER_ADDRESS_MAX} **after trimming**. Trimming is what the server
 * stores, and the request sends the trimmed text, so the length judged here is the length judged
 * there. `Validators.required` alone would accept an address of only spaces — which the server
 * refuses, because correction never clears.
 */
export const customerAddressValidator: ValidatorFn = (
  control: AbstractControl<string>,
): ValidationErrors | null => {
  const trimmed = (control.value ?? '').trim();

  if (trimmed.length === 0) {
    return { required: true };
  }

  return trimmed.length > CUSTOMER_ADDRESS_MAX
    ? { maxlength: { requiredLength: CUSTOMER_ADDRESS_MAX, actualLength: trimmed.length } }
    : null;
};

/** The request the dialog sends: the address, trimmed, and nothing else (D61, D112). */
export function toCorrectCustomerAddressRequest(address: string): CorrectCustomerAddressRequestDto {
  return { address: address.trim() };
}
