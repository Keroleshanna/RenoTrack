import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { A11yModule } from '@angular/cdk/a11y';

import { I18n } from '../../core/i18n/i18n';

/**
 * The modal every write action opens (Wireframes C2, D2, E2, E3).
 *
 * **A native `<dialog>`, not a hand-rolled overlay.** The browser gives the top layer, the backdrop,
 * `Escape`, and inert-ing of the page behind it for free; reimplementing those is exactly how an
 * internal tool ends up with a modal a keyboard user cannot leave. `cdkTrapFocus` handles the one
 * thing `showModal()` does not do on its own — cycling focus within the panel — and comes from
 * `@angular/cdk`, which is already a dependency rather than a new one (D75).
 *
 * The dialog is **presentation only**: it renders projected content and reports `close`. Whether an
 * action is allowed, what it does, and what happens when the server refuses all stay with the screen
 * that owns the workflow.
 */
@Component({
  selector: 'app-dialog',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [A11yModule],
  template: `
    <dialog #panel class="dialog" (close)="closed.emit()" (cancel)="closed.emit()">
      <div class="dialog__inner" cdkTrapFocus [cdkTrapFocusAutoCapture]="true">
        <header class="dialog__head">
          <h2 class="rt-title">{{ heading() }}</h2>
          @if (description()) {
            <p class="rt-caption dialog__description">{{ description() }}</p>
          }
        </header>

        <div class="dialog__body">
          <ng-content />
        </div>

        <footer class="dialog__foot">
          <ng-content select="[dialogActions]" />
        </footer>
      </div>
    </dialog>
  `,
  styles: `
    .dialog {
      width: min(560px, calc(100vw - 2 * var(--rt-space-4)));
      padding: 0;
      border: 0;
      border-radius: var(--rt-radius-lg);
      background: var(--rt-surface-raised);
      color: var(--rt-text);
      box-shadow: var(--rt-shadow-3);
    }
    .dialog::backdrop {
      background: rgb(23 26 28 / 45%);
    }
    .dialog__inner {
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-5);
      padding: var(--rt-space-6);
    }
    .dialog__description {
      margin-top: var(--rt-space-2);
      max-width: 52ch;
    }
    .dialog__body {
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-4);
    }
    .dialog__foot {
      display: flex;
      justify-content: flex-end;
      gap: var(--rt-space-2);
      flex-wrap: wrap;
    }
    @media (max-width: 599px) {
      .dialog__foot {
        flex-direction: column-reverse;
      }
      .dialog__foot ::ng-deep button {
        width: 100%;
      }
    }
  `,
})
export class Dialog {
  readonly open = input.required<boolean>();
  readonly heading = input.required<string>();
  readonly description = input('');

  /** Raised by `Escape`, the backdrop, and by `close()` — the owner decides what closing means. */
  readonly closed = output<void>();

  protected readonly t = inject(I18n).t;

  /**
   * A **signal** query, not a decorator: the effect below depends on it, and a decorator query is
   * not reactive — the element resolves after the first render, and the effect would never re-run.
   */
  private readonly panel = viewChild<ElementRef<HTMLDialogElement>>('panel');

  constructor() {
    // `showModal()`/`close()` rather than an `[open]` attribute: the attribute renders the element
    // but skips the top layer, the backdrop and the page-inerting entirely, which is the whole
    // reason for using a native dialog.
    effect(() => {
      const element = this.panel()?.nativeElement;
      if (!element) {
        return;
      }
      if (this.open() && !element.open) {
        element.showModal();
      } else if (!this.open() && element.open) {
        element.close();
      }
    });
  }
}
