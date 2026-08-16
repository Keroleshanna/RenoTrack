import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { Observable } from 'rxjs';

import { ApiError } from '../../core/api/api-error';
import {
  NOTIFICATION_DELIVERY_STATUSES,
  NotificationDeliveryDto,
  NotificationDeliveryStatusDto,
  PagedResult,
} from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Dialog } from '../../shared/ui/dialog';
import { Notifier } from '../../shared/ui/notifier';
import { StatusChip } from '../../shared/ui/status-chip';
import { FilterOption, WorkspaceChrome } from '../workspaces/workspace-chrome';
import { WorkspacePage } from '../workspaces/workspace-page';

/**
 * The operational view of outgoing email (`PermissionMatrix.md` §9, D69). Admin only.
 *
 * **This screen exists because a committed business operation stays successful when its
 * notification fails.** The API never turns a delivery failure into a 500, so without somewhere to
 * see the failure it would be invisible — and two of the six senders are *anonymous* public
 * endpoints (the website contact form and the customer's token-link decision), where no Admin was
 * present to be told anything at the time. That is the whole justification for the screen.
 *
 * **Retry is manual, synchronous, and re-sends only the notification** (D70). It never re-executes
 * the business operation and never mints a token. Duplicates from an ambiguous transport failure
 * are accepted: every notification is content-idempotent, so a duplicate is a second identical
 * email, never a second business effect. That is why the confirmation says what will happen.
 *
 * **A refusal and a failed delivery are different outcomes and are reported differently.** A 409 —
 * already sent, a lost claim race, an expired token, a `Void`/`Paid` invoice, email disabled — is
 * an error toast. A *delivery* failure returns **200** with the row recording `Failed`, so it is
 * reported as an outcome, not as a broken request. Conflating them would tell an Admin their click
 * failed when the system did exactly what it promised.
 */
@Component({
  selector: 'app-notifications-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, Dialog, StatusChip, WorkspaceChrome],
  template: `
    <app-workspace-chrome
      [title]="t().notifications.title"
      [subtitle]="t().notifications.subtitle"
      [headlineLabel]="t().notifications.count"
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
              <th>{{ t().notifications.columns.type }}</th>
              <th>{{ t().notifications.columns.reference }}</th>
              <th>{{ t().notifications.columns.recipient }}</th>
              <th>{{ t().notifications.columns.status }}</th>
              <th class="rt-numeric">{{ t().notifications.columns.attempts }}</th>
              <th class="rt-numeric">{{ t().notifications.columns.lastAttempt }}</th>
              <th><span class="rt-visually-hidden">{{ t().actions.retry }}</span></th>
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>
                  <span class="rt-table__primary">{{ t().notifications.type[row.notificationType] }}</span>
                  @if (row.failureMessage) {
                    <span class="rt-table__secondary">{{ row.failureMessage }}</span>
                  }
                </td>
                <!--
                  entityType/entityId are shown raw and are not linked: the reference is
                  polymorphic with no foreign key, so there is nothing to resolve a title from.
                -->
                <td>{{ row.entityType }} #{{ row.entityId }}</td>
                <td>
                  @if (row.recipient) {
                    {{ row.recipient }}
                  } @else {
                    <span class="rt-muted">{{ t().notifications.noRecipient }}</span>
                  }
                </td>
                <td><app-status-chip kind="notification" [value]="row.status" /></td>
                <td class="rt-numeric">{{ row.attemptCount }}</td>
                <td class="rt-numeric">
                  @if (row.lastAttemptAt) {
                    {{ row.lastAttemptAt | date: t().formats.dateTime : undefined : locale() }}
                  } @else {
                    <span class="rt-muted">—</span>
                  }
                </td>
                <td>
                  <!--
                    Offered on everything except Sent, which is the only genuinely terminal state.
                    Sending is included deliberately: with no lease or sweeper anywhere, a row
                    stranded by a crashed attempt is recoverable only by an Admin clicking again.
                  -->
                  @if (row.status !== 'Sent') {
                    <button
                      type="button"
                      class="rt-button rt-button--small"
                      (click)="askRetry(row)"
                    >
                      {{ t().notifications.retry }}
                    </button>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>

      <p class="rt-caption hint">{{ t().notifications.sendingHint }}</p>
    </app-workspace-chrome>

    <app-dialog
      [open]="retryOpen()"
      [heading]="t().notifications.retryTitle"
      [description]="t().notifications.retryBody"
      (closed)="retryOpen.set(false)"
    >
      @if (retrying(); as row) {
        <p class="rt-body">{{ t().notifications.type[row.notificationType] }}</p>
        <p class="rt-caption">{{ row.entityType }} #{{ row.entityId }}</p>
      }

      <ng-container dialogActions>
        <button type="button" class="rt-button" (click)="retryOpen.set(false)">
          {{ t().actions.cancel }}
        </button>
        <button
          type="button"
          class="rt-button rt-button--primary"
          [disabled]="busy()"
          (click)="retry()"
        >
          {{ t().notifications.retry }}
        </button>
      </ng-container>
    </app-dialog>
  `,
  styles: `
    .hint {
      margin-top: var(--rt-space-4);
    }
  `,
})
export class NotificationsPage extends WorkspacePage<NotificationDeliveryDto> {
  private readonly api = inject(RenoTrackApi);
  private readonly notifier = inject(Notifier);

  protected readonly busy = signal(false);
  protected readonly retryOpen = signal(false);
  protected readonly retrying = signal<NotificationDeliveryDto | null>(null);

  protected readonly filters = computed<readonly FilterOption[]>(() => [
    // "All" includes Sent, deliberately: §9 says "failed/pending", but hiding successes would make
    // "did my retry actually work?" unanswerable.
    { value: null, label: this.t().filters.allStatuses },
    ...NOTIFICATION_DELIVERY_STATUSES.map((status) => ({
      value: status,
      label: this.t().notifications.status[status],
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

  protected fetch(
    status: string | null,
    page: number,
  ): Observable<PagedResult<NotificationDeliveryDto>> {
    return this.api.notificationDeliveries({
      status: (status as NotificationDeliveryStatusDto | null) ?? undefined,
      page,
      pageSize: this.pageSize,
    });
  }

  protected askRetry(row: NotificationDeliveryDto): void {
    this.retrying.set(row);
    this.retryOpen.set(true);
  }

  protected retry(): void {
    const row = this.retrying();
    if (!row) {
      return;
    }

    this.busy.set(true);

    this.api.retryNotificationDelivery(row.id).subscribe({
      // 200 — but the row's own status is the outcome, not the status code. A second failed
      // attempt is a successful request describing a failure, and saying "delivered" here would
      // be a lie the Admin acts on.
      next: (updated) => {
        this.busy.set(false);
        this.retryOpen.set(false);

        if (updated.status === 'Sent') {
          this.notifier.success(this.t().notifications.retrySent);
        } else {
          this.notifier.error(this.t().notifications.retryFailed);
        }

        this.load();
      },
      // A refusal (409) or any other error. Never repaired, never retried automatically.
      error: (error: unknown) => {
        this.busy.set(false);

        // Closed on refusal too, not just on success. A 409 is terminal for this click — already
        // sent, a lost claim race, an expired token, email disabled — so leaving the confirmation
        // open would sit on top of the message explaining why and invite a second, identical
        // refusal. Found by clicking retry against a deployment with email disabled.
        this.retryOpen.set(false);
        this.notifier.error(this.messageFor(error));

        // Re-read regardless: a lost claim race means someone else moved the row, and the screen
        // should show what actually happened rather than the state it assumed.
        this.load();
      },
    });
  }

  private messageFor(error: unknown): string {
    const kind = error instanceof ApiError ? error.kind : 'server';
    return this.t().errors[kind];
  }
}
