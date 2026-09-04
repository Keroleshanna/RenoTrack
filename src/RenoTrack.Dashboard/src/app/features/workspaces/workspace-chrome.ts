import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

import { EmptyState, ErrorState, Skeleton } from '../../shared/ui/state-panels';

/** One option in a workspace's status filter. */
export interface FilterOption {
  readonly value: string | null;
  readonly label: string;
}

/**
 * The frame every workspace shares: title, status filter, result count, states and paging.
 *
 * Extracted because five screens rendering the same frame five ways is exactly how a product starts
 * to feel like five products. The table itself stays in each workspace, because that is the part
 * that genuinely differs.
 */
@Component({
  selector: 'app-workspace-chrome',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyState, ErrorState, Skeleton],
  template: `
    <header class="head">
      <div>
        <h1 class="rt-display">{{ title() }}</h1>
        <p class="rt-body rt-muted head__subtitle">{{ subtitle() }}</p>
      </div>
      <div class="head__end">
        <!--
          The workspace's own primary action, e.g. "New lead". Projected rather than an input, so a
          screen keeps ownership of what the control does and who may see it — the chrome must not
          grow a role opinion.
        -->
        <ng-content select="[chromeActions]" />

        @if (headlineLabel()) {
          <div class="rt-panel__aside">
            <span class="rt-label">{{ headlineLabel() }}</span>
            <span class="rt-panel__aside-value">{{ headlineValue() }}</span>
          </div>
        }
      </div>
    </header>

    <section class="rt-panel">
      @if (filters().length) {
        <div class="filters" role="group" [attr.aria-label]="filterLegend()">
          @for (option of filters(); track option.label) {
            <button
              type="button"
              class="filters__chip"
              [class.filters__chip--active]="option.value === active()"
              [attr.aria-pressed]="option.value === active()"
              (click)="statusChange.emit(option.value)"
            >
              {{ option.label }}
            </button>
          }
        </div>
      }

      @switch (state()) {
        @case ('loading') {
          <app-skeleton [lineCount]="8" />
        }
        @case ('error') {
          <app-error-state [body]="errorMessage()" (retry)="reload.emit()" />
        }
        @case ('empty') {
          <app-empty-state [title]="emptyTitle()" [body]="emptyBody()" />
        }
        @default {
          <ng-content />

          <footer class="paging">
            <span class="rt-caption">{{ showing() }}</span>
            <div class="paging__buttons">
              <button type="button" [disabled]="!hasPrevious()" (click)="pageChange.emit(-1)">
                {{ previousLabel() }}
              </button>
              <button type="button" [disabled]="!hasNext()" (click)="pageChange.emit(1)">
                {{ nextLabel() }}
              </button>
            </div>
          </footer>
        }
      }
    </section>
  `,
  styles: `
    :host {
      display: block;
      max-width: var(--rt-content-max);
      margin: 0 auto;
      padding-bottom: var(--rt-space-16);
    }
    .head {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      flex-wrap: wrap;
      gap: var(--rt-space-4);
      margin-bottom: var(--rt-space-6);
    }
    .head__subtitle {
      margin-top: var(--rt-space-2);
    }
    .head__end {
      display: flex;
      align-items: center;
      gap: var(--rt-space-5);
    }
    .filters {
      display: flex;
      flex-wrap: wrap;
      gap: var(--rt-space-2);
      margin-bottom: var(--rt-space-5);
      padding-bottom: var(--rt-space-5);
      border-bottom: 1px solid var(--rt-border);
    }
    .filters__chip {
      padding: var(--rt-space-2) var(--rt-space-4);
      background: var(--rt-surface-raised);
      border: 1px solid var(--rt-border-strong);
      border-radius: var(--rt-radius-pill);
      font: inherit;
      font-size: 12.5px;
      font-weight: 600;
      color: var(--rt-text-muted);
      cursor: pointer;
      transition: border-color var(--rt-motion-fast) var(--rt-ease),
        color var(--rt-motion-fast) var(--rt-ease);
    }
    .filters__chip:hover {
      border-color: var(--rt-brand);
      color: var(--rt-brand);
    }
    .filters__chip--active {
      background: var(--rt-brand);
      border-color: var(--rt-brand);
      color: #fff;
    }
    .paging {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--rt-space-4);
      margin-top: var(--rt-space-5);
      padding-top: var(--rt-space-4);
      border-top: 1px solid var(--rt-border);
    }
    .paging__buttons {
      display: flex;
      gap: var(--rt-space-2);
    }
    .paging__buttons button {
      padding: var(--rt-space-2) var(--rt-space-4);
      background: var(--rt-surface-raised);
      border: 1px solid var(--rt-border-strong);
      border-radius: var(--rt-radius-md);
      font: inherit;
      font-size: 12.5px;
      font-weight: 600;
      cursor: pointer;
    }
    .paging__buttons button:disabled {
      opacity: 0.45;
      cursor: default;
    }
    .paging__buttons button:not(:disabled):hover {
      border-color: var(--rt-brand);
      color: var(--rt-brand);
    }
    @media (max-width: 599px) {
      .paging {
        flex-direction: column;
        align-items: stretch;
      }
    }
  `,
})
export class WorkspaceChrome {
  readonly title = input.required<string>();
  readonly subtitle = input('');
  readonly headlineLabel = input('');
  readonly headlineValue = input('');

  readonly filters = input<readonly FilterOption[]>([]);
  readonly filterLegend = input('');
  readonly active = input<string | null>(null);

  readonly state = input.required<'loading' | 'error' | 'empty' | 'ready'>();
  readonly errorMessage = input('');
  readonly emptyTitle = input('');
  readonly emptyBody = input('');

  readonly showing = input('');
  readonly hasPrevious = input(false);
  readonly hasNext = input(false);
  readonly previousLabel = input('');
  readonly nextLabel = input('');

  readonly statusChange = output<string | null>();
  readonly pageChange = output<number>();
  readonly reload = output<void>();
}
