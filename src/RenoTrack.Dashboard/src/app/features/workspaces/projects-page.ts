import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { CurrencyPipe, DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { Observable } from 'rxjs';

import { RenoTrackApi } from '../../core/api/renotrack-api';
import {
  PROJECT_STATUSES,
  PagedResult,
  ProjectListItemDto,
  ProjectStatusDto,
} from '../../core/api/contracts';
import { StatusChip } from '../../shared/ui/status-chip';
import { FilterOption, WorkspaceChrome } from './workspace-chrome';
import { WorkspacePage } from './workspace-page';

/**
 * The Projekte workspace — the jobs actually being built.
 *
 * **Readable by both roles and unscoped**, which is `PermissionMatrix.md` §5's own rule rather than
 * an oversight: an Inspector may see the outcome of a Lead they worked. That read confers no Invoice
 * permission — every money action stays Admin-only, which is why this screen shows the order value
 * and not the receivable.
 *
 * **There is no project title.** `ERD.md` gives `Project` no title column, though Wireframe E1
 * renders one. That gap pre-dates Phase 10 and a title is not invented here — the customer plus the
 * originating Angebot number is what the schema can actually identify a Project by.
 */
@Component({
  selector: 'app-projects-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CurrencyPipe, DatePipe, RouterLink, StatusChip, WorkspaceChrome],
  template: `
    <app-workspace-chrome
      [title]="t().projects.title"
      [subtitle]="t().projects.subtitle"
      [headlineLabel]="t().projects.count"
      [headlineValue]="total().toString()"
      [filters]="filters()"
      [filterLegend]="t().filters.status"
      [active]="statusFilter()"
      [state]="chromeState()"
      [errorMessage]="errorMessage() ?? ''"
      [showing]="showingLabel()"
      [hasPrevious]="hasPrevious()"
      [hasNext]="hasNext()"
      [previousLabel]="t().paging.previous"
      [nextLabel]="t().paging.next"
      (statusChange)="applyStatus($event)"
      (pageChange)="goToPage($event)"
      (reload)="load()"
    >
      <div class="rt-table-wrap">
        <table class="rt-table">
          <thead>
            <tr>
              <th>{{ t().projects.columns.customer }}</th>
              <th>{{ t().projects.columns.angebot }}</th>
              <th>{{ t().projects.columns.status }}</th>
              <th class="rt-numeric">{{ t().projects.columns.agreed }}</th>
              <th class="rt-numeric">{{ t().projects.columns.created }}</th>
            </tr>
          </thead>
          <tbody>
            @for (project of rows(); track project.id) {
              <tr>
                <td>
                  <a class="row-link" [routerLink]="['/projekte', project.id]">{{
                    project.customerName
                  }}</a>
                </td>
                <td class="rt-numeric">
                  <a class="row-link" [routerLink]="['/angebote', project.angebotId]">{{
                    project.angebotNumber
                  }}</a>
                </td>
                <td><app-status-chip kind="project" [value]="project.status" /></td>
                <td class="rt-numeric strong">
                  {{ project.agreedTotal | currency: 'EUR' : 'symbol' : '1.2-2' : locale() }}
                </td>
                <td class="rt-numeric">
                  {{ project.createdAt | date: t().formats.date : undefined : locale() }}
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </app-workspace-chrome>
  `,
  styles: `
    .strong {
      font-weight: 650;
    }
    .row-link {
      color: var(--rt-brand);
      font-weight: 600;
      text-decoration: none;
    }
    .row-link:hover {
      text-decoration: underline;
    }
  `,
})
export class ProjectsPage extends WorkspacePage<ProjectListItemDto> {
  private readonly api = inject(RenoTrackApi);

  protected readonly filters = computed<readonly FilterOption[]>(() => [
    { value: null, label: this.t().filters.allStatuses },
    ...PROJECT_STATUSES.map((status) => ({
      value: status,
      label: this.t().projects.status[status],
    })),
  ]);

  protected readonly chromeState = computed(() => {
    const state = this.state();
    if (state.kind !== 'ready') {
      return state.kind;
    }
    return state.page.items.length ? ('ready' as const) : ('empty' as const);
  });

  constructor() {
    super();
    this.initialise();
  }

  protected fetch(status: string | null, page: number): Observable<PagedResult<ProjectListItemDto>> {
    return this.api.projects({
      status: (status as ProjectStatusDto | null) ?? undefined,
      page,
      pageSize: this.pageSize,
    });
  }
}
