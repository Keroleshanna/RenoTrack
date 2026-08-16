import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { I18n } from '../../core/i18n/i18n';

/**
 * The three states every data surface must be able to show: loading, empty, error.
 *
 * They live in one file because they are one design decision, not three — a screen picks exactly one
 * of them, and they must look like siblings. Splitting them across files is how they drift apart.
 *
 * **Copy is resolved, not defaulted.** An `input()` default is evaluated once at construction, so a
 * default of `DE.states.emptyTitle` would freeze the panel in whatever language was active when it
 * was created and ignore the switcher (D79). Each input is optional and a `computed` falls back to
 * the *current* dictionary.
 */
@Component({
  selector: 'app-empty-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="panel">
      <p class="rt-subtitle">{{ resolvedTitle() }}</p>
      <p class="rt-body rt-muted panel__body">{{ resolvedBody() }}</p>
    </div>
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--rt-space-2);
      padding: var(--rt-space-10) var(--rt-space-6);
      text-align: center;
    }
    .panel__body {
      max-width: 44ch;
    }
  `,
})
export class EmptyState {
  readonly title = input<string>();
  readonly body = input<string>();

  private readonly t = inject(I18n).t;

  protected readonly resolvedTitle = computed(() => this.title() ?? this.t().states.emptyTitle);
  protected readonly resolvedBody = computed(() => this.body() ?? this.t().states.emptyBody);
}

@Component({
  selector: 'app-error-state',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="panel" role="alert">
      <p class="rt-subtitle">{{ resolvedTitle() }}</p>
      <p class="rt-body rt-muted panel__body">{{ resolvedBody() }}</p>
      <button type="button" class="panel__action" (click)="retry.emit()">
        {{ t().actions.retry }}
      </button>
    </div>
  `,
  styles: `
    .panel {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: var(--rt-space-2);
      padding: var(--rt-space-10) var(--rt-space-6);
      text-align: center;
    }
    .panel__body {
      max-width: 46ch;
    }
    .panel__action {
      margin-top: var(--rt-space-3);
      padding: var(--rt-space-2) var(--rt-space-5);
      background: var(--rt-surface-raised);
      border: 1px solid var(--rt-border-strong);
      border-radius: var(--rt-radius-md);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
    }
    .panel__action:hover {
      border-color: var(--rt-brand);
      color: var(--rt-brand);
    }
  `,
})
export class ErrorState {
  readonly title = input<string>();
  readonly body = input<string>();

  /** The surface owning this panel decides what retrying means; the panel only asks. */
  readonly retry = output<void>();

  protected readonly t = inject(I18n).t;

  protected readonly resolvedTitle = computed(() => this.title() ?? this.t().states.errorTitle);
  protected readonly resolvedBody = computed(() => this.body() ?? this.t().states.errorBody);
}

/**
 * A content-shaped placeholder, not a spinner.
 *
 * A spinner says "something is happening"; a skeleton also says *what shape* is coming, which stops
 * the page reflowing under the reader when it arrives. `aria-busy` plus a visually hidden live
 * message is what makes that legible to a screen reader, which sees no shape at all.
 */
@Component({
  selector: 'app-skeleton',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="skeleton" [attr.aria-busy]="true" aria-live="polite">
      <span class="rt-visually-hidden">{{ t().states.loading }}</span>
      @for (line of lines(); track $index) {
        <span class="skeleton__line" [style.width.%]="$last ? 60 : 100"></span>
      }
    </div>
  `,
  styles: `
    .skeleton {
      display: flex;
      flex-direction: column;
      gap: var(--rt-space-3);
      width: 100%;
    }
    .skeleton__line {
      height: 12px;
      border-radius: var(--rt-radius-sm);
      background: linear-gradient(
        90deg,
        var(--rt-surface-sunken) 25%,
        #e4e7e9 37%,
        var(--rt-surface-sunken) 63%
      );
      background-size: 400% 100%;
      animation: rt-shimmer 1.4s ease infinite;
    }
    @keyframes rt-shimmer {
      0% { background-position: 100% 50%; }
      100% { background-position: 0 50%; }
    }
    /* The shimmer is decoration; a user who asked for less motion gets a static block. */
    @media (prefers-reduced-motion: reduce) {
      .skeleton__line { animation: none; }
    }
  `,
})
export class Skeleton {
  readonly lineCount = input(3);

  protected readonly t = inject(I18n).t;

  protected readonly lines = computed(() => Array.from({ length: this.lineCount() }, (_, i) => i));
}
