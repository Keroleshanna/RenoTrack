import { Directive, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Observable } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import { PagedResult } from '../../core/api/contracts';
import { I18n } from '../../core/i18n/i18n';

export type ListState<T> =
  | { readonly kind: 'loading' }
  | { readonly kind: 'ready'; readonly page: PagedResult<T> }
  | { readonly kind: 'error'; readonly message: string };

/**
 * The behaviour every list workspace shares: load a page, keep the status filter in the URL, and
 * turn a failure into a message the user can read.
 *
 * A directive rather than a service, because each workspace *is* one of these with its own fetch —
 * inheriting the loading/paging/error handling is exactly what stops five screens implementing it
 * five slightly different ways.
 *
 * **The filter lives in the query string, not in component state.** That is what makes a Cockpit
 * tile able to link to `/angebote?status=InReview` and land on a filtered list — the number on the
 * tile and the rows behind it can never disagree, because they are the same query.
 */
@Directive()
export abstract class WorkspacePage<T> {
  protected readonly i18n = inject(I18n);
  protected readonly route = inject(ActivatedRoute);
  protected readonly router = inject(Router);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;

  protected readonly state = signal<ListState<T>>({ kind: 'loading' });
  protected readonly page = signal(1);

  protected readonly rows = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.page.items : [];
  });

  protected readonly total = computed(() => {
    const state = this.state();
    return state.kind === 'ready' ? state.page.totalCount : 0;
  });

  protected readonly errorMessage = computed(() => {
    const state = this.state();
    return state.kind === 'error' ? state.message : null;
  });

  /** `null` means "all"; anything else is the status filter from the URL. */
  protected readonly statusFilter = signal<string | null>(null);

  protected readonly pageSize = 25;

  protected readonly showingLabel = computed(() => {
    const total = this.total();
    if (total === 0) {
      return '';
    }
    const first = (this.page() - 1) * this.pageSize + 1;
    const last = Math.min(this.page() * this.pageSize, total);
    return this.i18n.format(this.t().paging.showing, first, last, total);
  });

  protected readonly hasPrevious = computed(() => this.page() > 1);
  protected readonly hasNext = computed(() => this.page() * this.pageSize < this.total());

  /** Each workspace supplies its own fetch; everything else here is shared. */
  protected abstract fetch(status: string | null, page: number): Observable<PagedResult<T>>;

  protected initialise(): void {
    const status = this.route.snapshot.queryParamMap.get('status');
    this.statusFilter.set(status);
    this.load();
  }

  protected load(): void {
    this.state.set({ kind: 'loading' });

    this.fetch(this.statusFilter(), this.page()).subscribe({
      next: (page) => this.state.set({ kind: 'ready', page }),
      // The server's own message is never rendered — it is English and phrased for an API caller.
      error: (error: unknown) => {
        const kind = error instanceof ApiError ? error.kind : 'server';
        this.state.set({ kind: 'error', message: this.t().errors[kind] });
      },
    });
  }

  protected applyStatus(status: string | null): void {
    this.statusFilter.set(status);
    this.page.set(1);

    // Written back to the URL so the view is shareable and survives a reload.
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { status: status ?? null },
      queryParamsHandling: 'merge',
    });

    this.load();
  }

  protected goToPage(delta: number): void {
    this.page.update((current) => Math.max(1, current + delta));
    this.load();
  }
}
