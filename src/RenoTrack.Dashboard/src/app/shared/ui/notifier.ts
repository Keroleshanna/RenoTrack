import { ChangeDetectionStrategy, Component, Injectable, inject, signal } from '@angular/core';

import { I18n } from '../../core/i18n/i18n';

export type NoticeTone = 'success' | 'error';

export interface Notice {
  readonly id: number;
  readonly tone: NoticeTone;
  readonly message: string;
}

/**
 * The confirmation a completed write action leaves behind.
 *
 * **Every mutation in this application has a consequence somewhere else** — an Angebot leaves the
 * Inspector's hands, an email goes to a customer, a number is burned. A screen that simply re-renders
 * with different data does not tell the user *that their action happened*, only that something is now
 * different. This is what says it.
 *
 * ## This is for transient feedback only
 *
 * A toast reports **the outcome of something the user just did**: saved, sent, updated, refused.
 * Every one of them expires, and every one can be closed by hand.
 *
 * **Work that is waiting on someone is not a toast and never appears here.** "The office requested
 * changes" and "a quote needs review" are *states of the business*, not events in a session: they
 * belong on the Cockpit's decision list and on the document itself, where they persist until the
 * underlying state changes and survive a reload, a re-login and a different device. Putting them
 * in a corner overlay would mean a dismissed toast silently loses the task — which is exactly why
 * nothing here is allowed to be the only place a task is shown.
 *
 * Messages are always dictionary strings resolved by the caller, never a server `detail` (CLAUDE.md
 * §23) — the backend speaks English to an API caller, not German to a Handwerksbetrieb.
 */
@Injectable({ providedIn: 'root' })
export class Notifier {
  /**
   * How long each tone stays on screen.
   *
   * A failure gets longer because it usually carries something to read and sometimes something to
   * act on — but it still expires. Leaving failures up indefinitely was a real complaint in QA:
   * they accumulated in the corner across a session and became a permanent smear over the UI that
   * said nothing about the current state of anything.
   */
  private static readonly LIFETIME_MS: Readonly<Record<NoticeTone, number>> = {
    success: 5000,
    error: 12000,
  };

  private readonly items = signal<readonly Notice[]>([]);
  private nextId = 1;

  readonly notices = this.items.asReadonly();

  success(message: string): void {
    this.push('success', message);
  }

  error(message: string): void {
    this.push('error', message);
  }

  dismiss(id: number): void {
    this.items.update((current) => current.filter((notice) => notice.id !== id));
  }

  private push(tone: NoticeTone, message: string): void {
    const id = this.nextId++;
    this.items.update((current) => [...current, { id, tone, message }]);

    setTimeout(() => this.dismiss(id), Notifier.LIFETIME_MS[tone]);
  }
}

/**
 * Where notices are rendered — once, in the app shell, so no screen has to host its own.
 *
 * `role="status"` rather than `role="alert"`: these report the result of something the user just
 * did, and an assertive interruption for "saved" is noise for a screen-reader user.
 */
@Component({
  selector: 'app-notices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="notices" role="status" aria-live="polite">
      @for (notice of notifier.notices(); track notice.id) {
        <div class="notice" [class.notice--error]="notice.tone === 'error'">
          <span class="notice__text">{{ notice.message }}</span>
          <button
            type="button"
            class="notice__close"
            [attr.aria-label]="t().actions.close"
            (click)="notifier.dismiss(notice.id)"
          >
            ×
          </button>
        </div>
      }
    </div>
  `,
  styles: `
    .notices {
      position: fixed;
      right: var(--rt-space-5);
      bottom: var(--rt-space-5);
      z-index: 20;
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-2);
      max-width: min(420px, calc(100vw - 2 * var(--rt-space-4)));
    }
    .notice {
      display: flex;
      align-items: flex-start;
      gap: var(--rt-space-3);
      padding: var(--rt-space-3) var(--rt-space-4);
      background: var(--rt-surface-inverse);
      color: var(--rt-text-inverse);
      border-radius: var(--rt-radius-md);
      box-shadow: var(--rt-shadow-3);
      font-size: 13.5px;
      /* The tone is carried by a bar, not by the whole surface — a wall of red is alarming out of
         proportion to a failed request that can simply be retried. */
      border-left: 3px solid var(--rt-success);
    }
    .notice--error {
      border-left-color: var(--rt-danger);
    }
    .notice__text {
      flex: 1;
    }
    .notice__close {
      flex: none;
      background: transparent;
      border: 0;
      color: inherit;
      font: inherit;
      font-size: 18px;
      line-height: 1;
      cursor: pointer;
      opacity: 0.7;
    }
    .notice__close:hover {
      opacity: 1;
    }
    @media (max-width: 599px) {
      .notices {
        right: var(--rt-space-4);
        left: var(--rt-space-4);
        bottom: var(--rt-space-4);
        max-width: none;
      }
    }
  `,
})
export class Notices {
  protected readonly notifier = inject(Notifier);
  protected readonly t = inject(I18n).t;
}
