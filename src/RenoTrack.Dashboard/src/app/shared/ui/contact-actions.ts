import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { I18n } from '../../core/i18n/i18n';

/**
 * Makes a customer's contact details actionable, wherever they appear.
 *
 * ## Why this is one component rather than a link in each template
 *
 * An address, a phone number and an email address show up on the Lead list, the Lead detail, the
 * on-site screen, the schedule and the Project detail. Every one of them is a thing the user wants
 * to *act on* — navigate to, ring, message, write to — and copying that markup into five templates
 * is how four of them end up subtly different and the fifth gets forgotten.
 *
 * ## Why these targets
 *
 * - **Address → a maps URL.** `google.com/maps/search/?api=1&query=…` is the documented,
 *   platform-neutral form: on a phone the OS hands it to the installed maps app, on a desktop it
 *   opens in the browser. A `geo:` URI would be the purer choice but desktop browsers do nothing
 *   with it, and this screen is used on both.
 * - **Phone → `tel:` and WhatsApp.** `tel:` is what a phone dials. WhatsApp goes through
 *   `wa.me/<digits>`, which needs the number in international form with no punctuation — see
 *   {@link whatsAppNumber}, which is where the real work is.
 * - **Email → `mailto:`.** The application sends its own transactional mail through the backend
 *   (FR-9); this is a person writing to a person, which belongs in their own mail client where the
 *   reply will land.
 *
 * Every link opens in a new tab (`rel="noopener"`), because losing an in-progress quote to a
 * navigation is a far worse outcome than an extra tab.
 */
@Component({
  selector: 'app-contact-actions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="contact">
      @if (phone(); as phoneNumber) {
        <a class="contact__action" [href]="'tel:' + phoneNumber" [attr.aria-label]="t().contact.call">
          <span aria-hidden="true">☎</span> {{ t().contact.call }}
        </a>

        @if (whatsAppHref(); as href) {
          <a
            class="contact__action"
            [href]="href"
            target="_blank"
            rel="noopener"
            [attr.aria-label]="t().contact.whatsApp"
          >
            <span aria-hidden="true">✆</span> {{ t().contact.whatsApp }}
          </a>
        }
      }

      @if (email(); as address) {
        <a
          class="contact__action"
          [href]="'mailto:' + address"
          [attr.aria-label]="t().contact.email"
        >
          <span aria-hidden="true">✉</span> {{ t().contact.email }}
        </a>
      }

      @if (mapsHref(); as href) {
        <a
          class="contact__action"
          [href]="href"
          target="_blank"
          rel="noopener"
          [attr.aria-label]="t().contact.route"
        >
          <span aria-hidden="true">◎</span> {{ t().contact.route }}
        </a>
      }
    </div>
  `,
  styles: `
    .contact {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rt-space-2);
    }
    .contact__action {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 4px var(--rt-space-3);
      border: 1px solid var(--rt-border);
      border-radius: var(--rt-radius-pill);
      background: var(--rt-surface-raised);
      color: var(--rt-brand);
      font-size: 12px;
      font-weight: 600;
      text-decoration: none;
      white-space: nowrap;
    }
    .contact__action:hover {
      background: var(--rt-surface-sunken);
      text-decoration: none;
    }
    /* Comfortably tappable on the one screen that is genuinely used on site (Wireframe C3). */
    @media (max-width: 599px) {
      .contact__action {
        padding: 8px var(--rt-space-4);
        font-size: 13px;
      }
    }
  `,
})
export class ContactActions {
  readonly phone = input<string | null>(null);
  readonly email = input<string | null>(null);
  readonly address = input<string | null>(null);

  protected readonly t = inject(I18n).t;

  protected readonly mapsHref = computed(() => {
    const address = this.address()?.trim();
    return address
      ? `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`
      : null;
  });

  protected readonly whatsAppHref = computed(() => {
    const number = whatsAppNumber(this.phone());
    return number ? `https://wa.me/${number}` : null;
  });
}

/**
 * A phone number in the digits-only international form `wa.me` requires, or `null` if it cannot be
 * derived confidently.
 *
 * **The guessing is deliberately limited.** `wa.me` takes a country code and no punctuation, and
 * the Leads in this system are German — so `0176 …` (a national trunk prefix) becomes `49176 …`,
 * and `+49 …` simply loses its punctuation. Anything else is returned as-is when it is already
 * plausibly international, and refused otherwise.
 *
 * Refusing is the important part: a wrong number here does not fail visibly, it opens a chat with
 * *somebody else*. Returning `null` hides the WhatsApp action for that contact and leaves the plain
 * call link, which is the honest outcome — the office can still ring them.
 */
export function whatsAppNumber(phone: string | null | undefined): string | null {
  if (!phone) {
    return null;
  }

  const trimmed = phone.trim();
  const digits = trimmed.replace(/\D/g, '');

  if (digits.length < 6) {
    return null;
  }

  // Already international, written with a +.
  if (trimmed.startsWith('+')) {
    return digits;
  }

  // German national form: a single leading 0 is the trunk prefix and is replaced by the country
  // code. `00` is the international prefix, so those digits are already a full number.
  if (digits.startsWith('00')) {
    return digits.slice(2);
  }

  if (digits.startsWith('0')) {
    return `49${digits.slice(1)}`;
  }

  // No prefix at all: guessing a country would be inventing one.
  return null;
}
