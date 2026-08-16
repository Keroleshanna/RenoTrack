import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { ApiError } from '../../core/api/api-error';
import { InspectionDetailDto, UserSummaryDto } from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { Dialog } from '../../shared/ui/dialog';
import { Notifier } from '../../shared/ui/notifier';
import { ErrorState, Skeleton } from '../../shared/ui/state-panels';
import { InspectionCapabilities, inspectionCapabilitiesFor } from './inspection-capabilities';

/**
 * Wireframe **C3** — the on-site screen, and the one screen in this application that is genuinely
 * mobile-first (SRS §2.4 calls it out for this screen and no other).
 *
 * ## Why this screen had to exist for the rest to work
 *
 * Completing a site visit is what moves a Lead to `InspectionDone`, and `InspectionDone` is FR-4.1's
 * precondition for writing a quote. Without it the Angebot builder is unreachable through the
 * Dashboard: an Inspector could see their appointments and never turn one into work.
 *
 * ## Everything here is irreversible in one direction
 *
 * BR-10 makes a completed Inspection immutable — no later photo, no corrected note. So completion is
 * confirmed before it runs, and once it has run every control disappears rather than being offered
 * and refused.
 *
 * ## Photos are uploaded, never displayed
 *
 * `IFileStorage` has `SaveAsync` and `DeleteAsync` and **no `GetAsync`**, and no authenticated
 * endpoint serves a stored photo (CLAUDE.md §13 records this as a known gap). The screen therefore
 * reports the count the API returns and does not render thumbnails — a broken `<img>` for every
 * photo would misrepresent a documented gap as a bug.
 */
@Component({
  selector: 'app-inspection-detail-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, ReactiveFormsModule, RouterLink, Dialog, ErrorState, Skeleton],
  templateUrl: './inspection-detail-page.html',
  styleUrl: './inspection-detail-page.scss',
})
export class InspectionDetailPage {
  private readonly api = inject(RenoTrackApi);
  private readonly auth = inject(Auth);
  private readonly i18n = inject(I18n);
  private readonly notifier = inject(Notifier);
  private readonly route = inject(ActivatedRoute);

  protected readonly t = this.i18n.t;
  protected readonly locale = this.i18n.locale;

  protected readonly id = Number(this.route.snapshot.paramMap.get('id'));

  protected readonly inspection = signal<InspectionDetailDto | null>(null);
  protected readonly loadState = signal<'loading' | 'ready' | 'error'>('loading');
  protected readonly loadError = signal('');
  protected readonly busy = signal(false);

  protected readonly confirmingComplete = signal(false);
  protected readonly reassignOpen = signal(false);

  /** Candidates for reassignment. Only fetched for an Admin, who is the only role that may. */
  protected readonly inspectors = signal<readonly UserSummaryDto[]>([]);

  protected readonly reassignForm = new FormGroup({
    inspectorId: new FormControl<number | null>(null, { validators: [Validators.required] }),
  });

  /** The file chosen for upload, held until the user confirms — never uploaded on selection. */
  protected readonly pendingFile = signal<File | null>(null);

  protected readonly notesForm = new FormGroup({
    notes: new FormControl('', { nonNullable: true }),
  });

  protected readonly capabilities = computed<InspectionCapabilities>(() =>
    inspectionCapabilitiesFor(this.auth.role(), this.inspection()?.completedAt ?? null),
  );

  constructor() {
    this.load();
  }

  protected load(): void {
    this.loadState.set('loading');

    this.api.inspection(this.id).subscribe({
      next: (inspection) => {
        this.inspection.set(inspection);
        this.notesForm.setValue({ notes: inspection.notes ?? '' });
        this.loadState.set('ready');
      },
      error: (error: unknown) => {
        this.loadError.set(this.messageFor(error));
        this.loadState.set('error');
      },
    });
  }

  private reload(): void {
    this.api.inspection(this.id).subscribe({
      next: (inspection) => {
        this.inspection.set(inspection);
        this.notesForm.setValue({ notes: inspection.notes ?? '' });
      },
      error: (error: unknown) => this.notifier.error(this.messageFor(error)),
    });
  }

  protected saveNotes(): void {
    const notes = this.notesForm.getRawValue().notes.trim();

    this.perform(
      this.api.updateInspectionNotes(this.id, notes.length ? notes : null),
      this.t().inspectionDetail.notesSaved,
    );
  }

  protected chooseFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.pendingFile.set(input.files?.[0] ?? null);
  }

  protected uploadPhoto(): void {
    const file = this.pendingFile();
    if (!file) {
      return;
    }

    this.perform(
      this.api.uploadInspectionPhoto(this.id, file, null),
      this.t().inspectionDetail.photoUploaded,
      () => this.pendingFile.set(null),
    );
  }

  protected complete(): void {
    this.perform(this.api.completeInspection(this.id), this.t().inspectionDetail.completed, () =>
      this.confirmingComplete.set(false),
    );
  }

  protected openReassign(): void {
    // Pre-selected with whoever currently holds it, so the list starts from the truth.
    this.reassignForm.reset({ inspectorId: this.inspection()?.inspectorId ?? null });

    if (!this.inspectors().length) {
      this.api
        .staff('Inspector')
        .pipe(catchError(() => of([])))
        .subscribe((staff) => this.inspectors.set(staff.filter((user) => user.isActive)));
    }

    this.reassignOpen.set(true);
  }

  protected reassign(): void {
    if (this.reassignForm.invalid) {
      this.reassignForm.markAllAsTouched();
      return;
    }

    this.perform(
      this.api.reassignInspection(this.id, this.reassignForm.getRawValue().inspectorId!),
      this.t().inspectionDetail.reassigned,
      () => this.reassignOpen.set(false),
    );
  }

  private perform(
    request: Observable<unknown>,
    successMessage: string,
    onSuccess?: () => void,
  ): void {
    this.busy.set(true);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        onSuccess?.();
        this.notifier.success(successMessage);
        this.reload();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.notifier.error(this.messageFor(error));
      },
    });
  }

  private messageFor(error: unknown): string {
    const kind = error instanceof ApiError ? error.kind : 'server';
    return this.t().errors[kind];
  }
}
