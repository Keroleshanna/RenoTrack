import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

import { CreateInvoiceRequestDto, INVOICE_DESCRIPTION_MAX } from '../../core/api/contracts';

/**
 * The invoice form's rules and its mapping to the request (Phase 14 Slice 2, D111), kept out of the
 * component so they are unit-tested rather than trusted.
 *
 * **These mirror the server's shape rules and nothing more.** They exist so an Admin is told at the
 * field, not by a 400 (CLAUDE.md §23). The server re-checks every one of them, and it alone decides
 * the VAT split — this module has no notion of a rate or a net amount at all.
 */

/** What the dialog's controls hold. Dates are `yyyy-MM-dd` from `<input type="date">`, or `''`. */
export interface InvoiceFormValue {
  readonly grossAmount: number;
  readonly dueDate: string;
  readonly description: string;
  readonly servicePeriodStart: string;
  readonly servicePeriodEnd: string;
}

/**
 * Required and within {@link INVOICE_DESCRIPTION_MAX} **after trimming** — the server measures the
 * trimmed text, because that is what it stores, so a description padded with spaces is judged the
 * same way here. `Validators.required` alone would accept a description of only spaces.
 */
export const invoiceDescriptionValidator: ValidatorFn = (
  control: AbstractControl<string>,
): ValidationErrors | null => {
  const trimmed = (control.value ?? '').trim();

  if (trimmed.length === 0) {
    return { required: true };
  }

  return trimmed.length > INVOICE_DESCRIPTION_MAX
    ? { maxlength: { requiredLength: INVOICE_DESCRIPTION_MAX, actualLength: trimmed.length } }
    : null;
};

/**
 * A form-group rule over the two service-period dates: both may be absent, a start may stand alone
 * (a single service date), but an end needs a start and cannot precede it. ISO dates compare
 * correctly as strings.
 */
export const servicePeriodValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const start = (group.get('servicePeriodStart')?.value as string | null) ?? '';
  const end = (group.get('servicePeriodEnd')?.value as string | null) ?? '';

  if (end === '') {
    return null;
  }

  if (start === '') {
    return { servicePeriodEndWithoutStart: true };
  }

  return end < start ? { servicePeriodEndBeforeStart: true } : null;
};

/**
 * The request the dialog sends: the description trimmed, and an empty date sent as `null` so the
 * server records "not given" rather than receiving a value it would have to interpret.
 */
export function toCreateInvoiceRequest(value: InvoiceFormValue): CreateInvoiceRequestDto {
  return {
    grossAmount: value.grossAmount,
    dueDate: value.dueDate,
    description: value.description.trim(),
    servicePeriodStart: value.servicePeriodStart === '' ? null : value.servicePeriodStart,
    servicePeriodEnd: value.servicePeriodEnd === '' ? null : value.servicePeriodEnd,
  };
}
