import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';

import { I18n } from '../../core/i18n/i18n';
import {
  AngebotStatusDto,
  InvoiceStatusDto,
  LeadStatusDto,
  NotificationDeliveryStatusDto,
  ProjectStatusDto,
} from '../../core/api/contracts';

/** The visual tones a chip can take. Deliberately fewer tones than there are statuses. */
type Tone = 'new' | 'scheduled' | 'done' | 'progress' | 'sent' | 'won' | 'lost';

/**
 * A status, rendered as a tinted chip.
 *
 * **Colour carries meaning here, so it is never the only carrier**: the translated label is always
 * present, which is what keeps the chip readable for a colour-blind user without a separate
 * accessibility mode.
 *
 * One component for all four status enums rather than four near-identical ones. The mapping tables
 * below are total over each enum, so a status added to a contract without a tone is a compile error
 * rather than an unstyled chip.
 */
@Component({
  selector: 'app-status-chip',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span class="chip" [class]="'chip--' + tone()">{{ label() }}</span>`,
  styles: `
    .chip {
      display: inline-flex;
      align-items: center;
      padding: 3px var(--rt-space-3);
      border: 1px solid transparent;
      border-radius: var(--rt-radius-pill);
      font-size: 11.5px;
      font-weight: 600;
      line-height: 1.4;
      white-space: nowrap;
    }
    .chip--new {
      background: var(--rt-status-new-bg);
      color: var(--rt-status-new-fg);
      border-color: var(--rt-status-new-border);
    }
    .chip--scheduled {
      background: var(--rt-status-scheduled-bg);
      color: var(--rt-status-scheduled-fg);
      border-color: var(--rt-status-scheduled-border);
    }
    .chip--done {
      background: var(--rt-status-done-bg);
      color: var(--rt-status-done-fg);
      border-color: var(--rt-status-done-border);
    }
    .chip--progress {
      background: var(--rt-status-progress-bg);
      color: var(--rt-status-progress-fg);
      border-color: var(--rt-status-progress-border);
    }
    .chip--sent {
      background: var(--rt-status-sent-bg);
      color: var(--rt-status-sent-fg);
      border-color: var(--rt-status-sent-border);
    }
    .chip--won {
      background: var(--rt-status-won-bg);
      color: var(--rt-status-won-fg);
      border-color: var(--rt-status-won-border);
    }
    .chip--lost {
      background: var(--rt-status-lost-bg);
      color: var(--rt-status-lost-fg);
      border-color: var(--rt-status-lost-border);
    }
  `,
})
export class StatusChip {
  readonly kind = input.required<'lead' | 'angebot' | 'invoice' | 'project' | 'notification'>();
  readonly value = input.required<string>();

  private readonly t = inject(I18n).t;

  protected readonly label = computed(() => {
    const t = this.t();
    switch (this.kind()) {
      case 'lead':
        return t.leadStatus[this.value() as LeadStatusDto] ?? this.value();
      case 'angebot':
        return t.angebote.status[this.value() as AngebotStatusDto] ?? this.value();
      case 'invoice':
        return t.invoices.status[this.value() as InvoiceStatusDto] ?? this.value();
      case 'project':
        return t.projects.status[this.value() as ProjectStatusDto] ?? this.value();
      case 'notification':
        return t.notifications.status[this.value() as NotificationDeliveryStatusDto] ?? this.value();
    }
  });

  protected readonly tone = computed<Tone>(() => {
    switch (this.kind()) {
      case 'lead':
        return LEAD_TONES[this.value() as LeadStatusDto] ?? 'new';
      case 'angebot':
        return ANGEBOT_TONES[this.value() as AngebotStatusDto] ?? 'new';
      case 'invoice':
        return INVOICE_TONES[this.value() as InvoiceStatusDto] ?? 'new';
      case 'project':
        return PROJECT_TONES[this.value() as ProjectStatusDto] ?? 'new';
      case 'notification':
        return NOTIFICATION_TONES[this.value() as NotificationDeliveryStatusDto] ?? 'new';
    }
  });
}

const LEAD_TONES: Record<LeadStatusDto, Tone> = {
  New: 'new',
  InspectionScheduled: 'scheduled',
  InspectionDone: 'done',
  AngebotInProgress: 'progress',
  AngebotSent: 'sent',
  Won: 'won',
  Lost: 'lost',
};

/**
 * An Angebot's internal review states share the "in progress" tone: from the office's point of view
 * a draft and a returned draft are the same thing — work that has not reached them yet.
 */
const ANGEBOT_TONES: Record<AngebotStatusDto, Tone> = {
  Draft: 'new',
  InReview: 'progress',
  ChangesRequested: 'progress',
  ApprovedInternally: 'done',
  Sent: 'sent',
  CustomerApproved: 'won',
  CustomerRejected: 'lost',
};

/** `Void` is grey, not red: cancelling an invoice is a deliberate act, not a failure. */
const INVOICE_TONES: Record<InvoiceStatusDto, Tone> = {
  Draft: 'new',
  Sent: 'sent',
  Paid: 'won',
  Overdue: 'lost',
  Void: 'new',
};

const PROJECT_TONES: Record<ProjectStatusDto, Tone> = {
  Active: 'scheduled',
  OnHold: 'progress',
  Completed: 'won',
};

/**
 * `Failed` is the only red one. `Sending` shares `Pending`'s tone because from an Admin's side they
 * mean the same thing — nothing has arrived yet — and a row stranded in `Sending` by a crashed
 * attempt is not a failure, it is unfinished work that only a retry can complete.
 */
const NOTIFICATION_TONES: Record<NotificationDeliveryStatusDto, Tone> = {
  Pending: 'new',
  Sending: 'progress',
  Sent: 'won',
  Failed: 'lost',
};
