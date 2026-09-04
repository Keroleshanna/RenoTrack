import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, firstValueFrom, of } from 'rxjs';
import { catchError } from 'rxjs/operators';

import { ApiError } from '../../core/api/api-error';
import { InspectionDetailDto, UserSummaryDto } from '../../core/api/contracts';
import { RenoTrackApi } from '../../core/api/renotrack-api';
import { Auth } from '../../core/auth/auth';
import { I18n } from '../../core/i18n/i18n';
import { ContactActions } from '../../shared/ui/contact-actions';
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
  imports: [DatePipe, ReactiveFormsModule, RouterLink, ContactActions, Dialog, ErrorState, Skeleton],
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

  protected readonly reopenOpen = signal(false);

  /** Files chosen for upload, held until the user confirms — never uploaded on selection. */
  protected readonly pendingFiles = signal<readonly File[]>([]);

  /** Progress while a batch is in flight, e.g. "3 of 5". Null when nothing is uploading. */
  protected readonly uploadProgress = signal<string | null>(null);

  protected readonly pendingLabel = computed(() =>
    this.i18n.format(this.t().inspectionDetail.photosSelected, this.pendingFiles().length),
  );

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

  protected chooseFiles(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.pendingFiles.set([...(input.files ?? [])]);

    // Cleared so choosing the same file twice in a row still fires `change` — a camera capture
    // often produces the identical name each time.
    input.value = '';
  }

  /**
   * Uploads every selected photo.
   *
   * **Sequential, not parallel.** The endpoint writes a file and commits a row per call; firing ten
   * at once from a phone on site invites timeouts and interleaves failures unhelpfully. One at a
   * time also makes the progress count honest.
   *
   * **Partial failure is reported as partial**, not as total success or total failure: whatever
   * uploaded stays uploaded — each call is its own transaction — so the user is told how many
   * arrived and the rest are kept in the pending list to retry. Silently discarding them, or
   * claiming success for a batch that half-failed, are both worse than an accurate count.
   */
  protected async uploadPhotos(): Promise<void> {
    const files = this.pendingFiles();
    if (files.length === 0) {
      return;
    }

    this.busy.set(true);

    const failed: File[] = [];
    let uploaded = 0;

    for (const [index, file] of files.entries()) {
      this.uploadProgress.set(
        this.i18n.format(this.t().inspectionDetail.uploading, index + 1, files.length),
      );

      try {
        await firstValueFrom(this.api.uploadInspectionPhoto(this.id, file, null));
        uploaded++;
      } catch {
        failed.push(file);
      }
    }

    this.busy.set(false);
    this.uploadProgress.set(null);
    this.pendingFiles.set(failed);

    if (uploaded > 0) {
      this.notifier.success(
        this.i18n.format(this.t().inspectionDetail.photosUploaded, uploaded),
      );
    }

    if (failed.length > 0) {
      this.notifier.error(
        this.i18n.format(this.t().inspectionDetail.photosFailed, failed.length),
      );
    }

    // Always, even when everything failed: the count on screen must match the server's.
    this.reload();
  }

  protected reopen(): void {
    this.perform(this.api.reopenInspection(this.id), this.t().inspectionDetail.reopened, () =>
      this.reopenOpen.set(false),
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
