# PHASE2_PROGRESS.md — Vertical Slice Log

**Purpose:** a detailed record of every vertical slice completed in Phase 2 (Application Layer) so far, in the order built. Each entry follows the same format: Goal, Design Decisions & Architectural Discussion, New Abstractions Introduced, Documentation Updates, Tests Added, Final Outcome. Cumulative test counts are given at each step so progress can be cross-checked against `PROJECT_STATE.md`.

**Process convention followed for every slice (see also `CLAUDE.md`):** analyze → (for slices touching new architectural territory) design review with the user before any code → implement → present code for review → write tests → verify full-solution build/test → commit. Not every slice triggered a full design-review round — simpler, precedent-following slices moved faster once the pattern was established.

All work in this log lives on branch `feature/phase-2-application-layer`, not yet merged or pushed as of this writing.

---

## Slice 1 — `CreateLeadCommand`

**Goal:** First vertical slice of Phase 2, establishing every convention the rest of the phase would follow. Covers Sequence Diagram §1 (website) and §2 (manual creation).

**Design decisions & architectural discussion:**
- Established `ICommandHandler<TCommand, TResult>` as the only dispatch abstraction (no MediatR — see `ARCHITECTURE_DECISIONS.md` D22).
- `ILeadRepository` started with `AddAsync` only (no `GetByIdAsync` yet — nothing in this slice needs to load an existing Lead).
- `CreateLeadCommand` carries `CreatedByUserId` as `int?` — null for the anonymous website path, populated for the Admin manual-entry path — mirroring the same optionality `Lead.Create`'s own `Address`/`Notes` parameters already use.
- Handler dispatches `IEmailSender.SendNewWebsiteLeadNotificationAsync` **only** when `Source == LeadSource.Website` (SRS FR-9.2) — the one conditional in the handler, classified as orchestration ("which side effect to trigger"), not business logic.
- **Mid-slice correction (caught during design review, before tests were written):** `IEmailSender`'s first draft took a `LeadDto` parameter directly, which made `Application.Common` depend on the `Leads` feature folder — backwards. Fixed by introducing `Common.Notifications.NewWebsiteLeadNotification`, a dedicated model narrower than the full `LeadDto`. See `ARCHITECTURE_DECISIONS.md` D23 for the full reasoning.
- **Second correction in the same slice:** `IAuditService.LogAsync`'s `action` parameter started as a raw `string` ("Created"); the user asked for a centralized `AuditAction` enum from the very first use, not after a second inconsistent string appeared. See D24.

**New abstractions introduced:** `ICommandHandler<TCommand, TResult>`, `ILeadRepository` (`AddAsync` only), `IUnitOfWork`, `IAuditService`, `IEmailSender` (+ `NewWebsiteLeadNotification`), `AuditAction` enum (single value: `LeadCreated`), `LeadDto`.

**Documentation updates:** None required — this slice didn't reveal any documentation gap.

**Tests added:** 8 (`CreateLeadCommandHandlerTests`) — happy path (DTO shape, repository add, audit log, save-changes count), conditional notification dispatch (sent for `Website`, not for `Phone`/`Email`), validation failure with no side effects.

**Final outcome:** 8 Application tests, alongside 146 pre-existing Domain tests → **154 total.** Solution-wide build clean (0 warnings, 0 errors). Committed.

---

## Slice 2 — `ScheduleInspectionCommand`

**Goal:** Sequence Diagram §3 Step A. First command to load an *existing* aggregate (`Lead`), and the first to discover a genuine business-rule gap.

**Design decisions & architectural discussion:**
- `ILeadRepository` gained `GetByIdAsync` — first real need for it.
- New `IInspectionRepository` (`AddAsync` only).
- `Lead.MarkInspectionScheduled()`'s own self-guard (`Status == New`) was confirmed to already cover StateMachine §1.3's guard for this transition — the handler does **not** re-check status itself.
- **Genuine gap discovered:** does scheduling an Inspection for a specific Inspector also assign that Inspector to the Lead (`Lead.AssignedInspectorId`)? No document explicitly said so, but PermissionMatrix's Inspector-pipeline scoping *depends* on that field being set. Verified this was a real gap (not something to guess past), then formalized as **BR-13** before writing the handler. See `ARCHITECTURE_DECISIONS.md` D25.
- **Audit-target question raised for the first time:** should the audit entry target `Lead` or the newly-created `Inspection`? Investigated `ERD.md`'s `AuditLog` schema (no cross-entity linkage column) and Wireframe C1 (per-Lead Activity Timeline is the only documented audit UI) — concluded the entry must target `Lead`, since an `Inspection`-typed entry could never surface on that screen. This became the general audit-target principle later written into `Architecture.md` §11 (see D26).

**New abstractions introduced:** `IInspectionRepository`, `NotFoundException` (first use — first command that can fail to find its target), `InspectionDto`, `AuditAction.InspectionScheduled`.

**Documentation updates:** **BR-13** added to `BusinessRules.md` (with Changelog row). `StateMachine.md` §1.3's `ScheduleInspection` row updated to note the `AssignedInspectorId` side effect. `PermissionMatrix.md`'s "Assign/reassign Inspector" row updated to clarify it can also happen implicitly via BR-13.

**Tests added:** 11 (`ScheduleInspectionCommandHandlerTests`) — happy path (BR-13 assignment proven directly, Lead status transition, audit target is `Lead` not `Inspection`), not-found, Domain guard-failure propagation (Lead not in `New` status), validation failures.

**Final outcome:** 19 Application tests total → **165 solution-wide** (146 Domain + 19 Application). `FakeLeadRepository` gained a reflection-based `Seed(lead)` test helper (simulating a database-assigned id, since `Lead.Id` has no public setter). Committed.

---

## Slice 3 — `CompleteInspectionCommand`

**Goal:** Sequence Diagram §3 Step B (end). Business-critical: applies BR-10 at the Application level and drives another Lead transition.

**Design decisions & architectural discussion:**
- `IInspectionRepository` gained `GetByIdAsync`.
- Confirmed StateMachine §1.3's "Inspection belongs to this Lead" guard is satisfied **structurally** by this command's shape (loading the Lead via `inspection.LeadId`, the Inspection's own field) — no explicit runtime check needed, since there is no other Lead reachable from a given Inspection.
- **First real ownership-check discussion:** PermissionMatrix §2 marks "Mark Inspection complete" as Inspector-`S` (assigned Inspector only) — different from Admin's role in the prior slice. The user distinguished "authentication/role authorization" (API-layer concern) from "business ownership rules" (Application-layer concern, since they depend on the *loaded* resource) and explicitly asked for this distinction to be documented as a general principle before implementing. This became `Architecture.md` §7.3.
- Ownership check for this slice was still written **inline** (`if (inspection.InspectorId != command.CompletedByInspectorId) throw new ForbiddenException(...)`) — not yet extracted, since this was only its first occurrence. The user explicitly flagged this would likely recur and should be watched for a third occurrence before extracting anything (see Slice 5).
- Defensive Lead-loading discussed explicitly: loading `Lead` via `inspection.LeadId` even though it "should never" be missing if data is consistent — kept, since the extra query costs nothing (already required to call `MarkInspectionDone()`), and failing loudly on a hypothetical data-integrity gap is strictly better than a silent `NullReferenceException`.

**New abstractions introduced:** `ForbiddenException` (first use), `AuditAction.InspectionDone`.

**Documentation updates:** `Architecture.md` §7.3 added (role-based authorization vs. resource ownership — general principle, not slice-specific).

**Tests added:** 10 (`CompleteInspectionCommandHandlerTests`) — happy path, not-found (Inspection and, separately, the defensively-loaded Lead), ownership failure, Domain guard-failure propagation (already completed), validation failures.

**Final outcome:** 29 Application tests → **175 solution-wide.** Committed.

---

## Slice 4 — `UploadInspectionPhotoCommand`

**Goal:** Sequence Diagram §3 Step B (photo loop). Introduces `IFileStorage` for the first time, and the second occurrence of the ownership check.

**Design decisions & architectural discussion:**
- `IFileStorage` introduced with **only** `SaveAsync` — `GetAsync`/`DeleteAsync` (both named in Architecture §9's original prose) deliberately not built, since no current use case needs them.
- **Domain inconsistency discovered and fixed (`ARCHITECTURE_DECISIONS.md` D33):** `Inspection.AddPhoto` originally returned `void`, unlike `AngebotSection.AddItem` (built later in Phase 1, returns the created child). This meant the handler had no way to get the created `InspectionPhoto` back to build a `PhotoDto` except `inspection.Photos.Last()` — fragile, order-dependent. Fixed by changing `Inspection.AddPhoto` to return the created `InspectionPhoto`, matching the established pattern. Confirmed with the user this was a genuine inconsistency (not a new feature) before touching already-merged Phase 1 Domain code; existing Domain tests updated, one new test added asserting the return value.
- **A genuine ordering bug was found and fixed before this slice was committed** — see `ARCHITECTURE_DECISIONS.md` D29 for the full story. In short: the first draft uploaded the file to storage *before* calling `Inspection.AddPhoto` (which enforces BR-10), so a rejected upload (already-completed Inspection) still wasted a real file write, leaving an orphaned file. The user explicitly rejected the natural-seeming fix (`Inspection.IsEditable` property) and asked for the *workflow* to be restructured instead. The actual fix: the handler computes the `FileUrl` itself (a GUID-based key) and calls `Inspection.AddPhoto(fileUrl, ...)` — BR-10's *existing* guard — before calling `IFileStorage.SaveAsync` at all. `IFileStorage.SaveAsync`'s signature changed accordingly (caller supplies the key, rather than the method inventing and returning one).
- This became a general principle, recorded in `Architecture.md` §9: "the Application layer is responsible for generating stable external resource identifiers before invoking external infrastructure, when doing so improves workflow consistency" — expected to recur for invoice PDFs or other generated documents later.
- No `AuditLog` entry for this command — classified as operational activity (attaching evidence), not a business milestone; Sequence Diagram §3 itself omits an audit step here, reinforcing the classification.
- Second occurrence of the `inspection.InspectorId != callerId` ownership check (still inline, not yet extracted).

**New abstractions introduced:** `IFileStorage` (`SaveAsync` only), `PhotoDto`.

**Documentation updates:** `Architecture.md` §9 gained the "stable external resource identifiers" principle. `Inspection.AddPhoto`'s Domain-code change was treated as a bug fix, not a Phase 1 reopening, and documented as such in the commit message.

**Tests added:** 10 (`UploadInspectionPhotoCommandHandlerTests`) — happy path (including a test asserting the same `FileUrl` was used for both the `InspectionPhoto` and the `IFileStorage.SaveAsync` call), not-found, ownership failure, **and an explicit proof that the ordering fix works**: uploading to an already-completed Inspection throws with **zero** entries in the fake file-storage's `SavedFiles` list — i.e. no orphaned file is ever written. Plus 1 new Domain test (`AddPhoto_ReturnsTheCreatedPhoto`).

**Final outcome:** 39 Application tests, 147 Domain tests → **186 solution-wide.** Committed.

---

## Slice 5 — `UpdateInspectionNotesCommand` + `IOwnershipValidator` Extraction

**Goal:** Sequence Diagram §3 Step B (notes). The simplest remaining Inspection command, and — by design — the trigger for the ownership-check extraction decision, since this was explicitly planned as its third occurrence.

**Design decisions & architectural discussion:**
- First command that never touches `Lead` at all — no status transition results from editing notes.
- No `AuditLog` entry (same "operational, not milestone" reasoning as photo upload).
- **Third occurrence of the ownership check** → extraction decision made explicitly, per the user's own earlier instruction to wait for it. The user's requested shape: **not** a generic `EnsureOwnedBy(int, int, string, int)` helper (rejected — "loses business intent... I want the code to explain *why* the comparison exists, not just *what* it does"), but a named-business-intent interface: `IOwnershipValidator.EnsureInspectionOwnership(Inspection, int)`, anticipating a future `EnsureAngebotOwnership(Angebot, int)` with the same underlying comparison shape but a distinct, self-explanatory name. See `ARCHITECTURE_DECISIONS.md` D28 for the full reasoning and the user's exact framing.
- `OwnershipValidator` (the concrete implementation) placed directly in `RenoTrack.Application` (not `Infrastructure`), since it has zero external dependency — the first service interface where this placement made sense.
- `CompleteInspectionCommandHandler` and `UploadInspectionPhotoCommandHandler` (Slices 3 and 4) were both retroactively updated in this slice to call the new `IOwnershipValidator.EnsureInspectionOwnership(...)` instead of their original inline checks — their existing tests needed only a constructor-parameter update (a real `OwnershipValidator` instance, no fake needed, since it has no dependencies of its own), not new test logic.

**New abstractions introduced:** `IOwnershipValidator` (+ `OwnershipValidator` implementation), `UpdateInspectionNotesCommand`/`Validator`/`Handler`.

**Documentation updates:** None beyond what Slice 3 already added to `Architecture.md` §7.3 (this slice confirmed and exercised that principle rather than introducing a new one).

**Tests added:** 8 (`UpdateInspectionNotesCommandHandlerTests`) + 2 (`OwnershipValidatorTests`, a small dedicated test class for the extracted service itself) = 10, plus the two retrofitted handler tests' constructor updates (no new test count from those, just updated wiring).

**Final outcome:** 49 Application tests, 147 Domain tests → **196 solution-wide.** This slice closed out the entire `Inspection` workflow (Schedule → UploadPhoto → UpdateNotes → Complete). Committed.

---

## Slice 6 — `CreateAngebotCommand`

**Goal:** Sequence Diagram §4 opening ("Create the draft"). First `Angebot`-workflow command — opens Phase 2's second major aggregate.

**Design decisions & architectural discussion:**
- New `IAngebotRepository` — not just `AddAsync`: also `HasActiveAngebotForLeadAsync(int leadId)` from the start, since StateMachine §2.4's "only one non-terminal Angebot per Lead" invariant is a genuine repository-backed cross-aggregate check needed by this very first command (`Angebot` cannot see its own siblings; `Lead` doesn't know `Angebot` exists as a type).
- `Lead.MarkAngebotInProgress()`'s own self-guard (`Status == InspectionDone`) confirmed to already cover the "Lead status allows this" half of the guard — not re-checked in the handler.
- New `IOwnershipValidator.EnsureLeadOwnership(Lead, int)` — a *third* distinct ownership relationship (after Inspection-ownership and, implicitly, the not-yet-built Angebot-ownership), extending the existing service rather than introducing a new abstraction, exactly matching the pattern anticipated in Slice 5.
- New `INumberGeneratorService` (first use — Architecture §8's Angebot-numbering service; full design reasoning in `ARCHITECTURE_DECISIONS.md` D34), deliberately minimal (`NextAngebotNumberAsync(int year, CancellationToken ct) → string`), with an explicit note-to-self that Phase 3's real implementation **must** increment atomically inside the same DB transaction as the Angebot creation, to prevent duplicate numbers under concurrency — flagged as the single highest-risk unverified assumption in the project until Phase 3 actually builds and tests it.
- New `ConflictException` (first use; see `ARCHITECTURE_DECISIONS.md` D35) for "Lead already has an active Angebot" — distinct from `NotFoundException`/`ForbiddenException`, mapped eventually to HTTP 409.
- **Audit-target inconsistency discovered in the documentation itself:** Sequence Diagram §4's "Create the draft" block had **no** `AUD` call at all, unlike every prior Lead-transition-driving command. Given creating an Angebot draft *does* drive `Lead.MarkAngebotInProgress()` — the exact same class of Lead-level milestone as `InspectionScheduled`/`InspectionDone` — this looked like an oversight in the diagram, not a deliberate omission. Corrected: added `AuditAction.AngebotCreated`, logged against `Lead`, **and** updated `Sequence Diagram.md` §4 itself to add the missing `AUD` step (plus fixed a stale `Angebot.CreateDraft(...)` reference to the renamed `Angebot.Create(...)`).
- `AngebotDto` introduced deliberately **header-only** — no nested `Sections` — matching the incremental-DTO discipline; `SectionDto`/`ItemDto`/`AngebotSummaryDto` all deferred to later slices/commands that actually return them.

**New abstractions introduced:** `IAngebotRepository` (`AddAsync` + `HasActiveAngebotForLeadAsync`), `INumberGeneratorService`, `ConflictException`, `IOwnershipValidator.EnsureLeadOwnership`, `AngebotDto`, `AuditAction.AngebotCreated`.

**Documentation updates:** `Sequence Diagram.md` §4 corrected (added missing `AUD` step; fixed stale `CreateDraft` reference).

**Tests added:** 13 (`CreateAngebotCommandHandlerTests`) — happy path (DTO shape, generated-number usage, year-requested assertion, Lead transition, audit entry), not-found, ownership failure, conflict (active Angebot already exists), Domain guard-failure propagation (Lead not `InspectionDone`), validation failures.

**Final outcome:** 62 Application tests, 147 Domain tests → **209 solution-wide.** Committed.

---

## Slice 7 — `AddAngebotSectionCommand`

**Goal:** Sequence Diagram §4 "Add a Section." First command exercising Angebot's internal aggregate composition (Angebot → AngebotSection).

**Design decisions & architectural discussion:**
- `IAngebotRepository` gained `GetByIdAsync` — explicitly discussed and confirmed to be **the only** read method needed; Sequence Diagram §4's literal naming (`GetByIdWithDetailsAsync`) was identified as EF-flavored jargon from the diagram's authors, not a second contract Application needs — in DDD there is no legitimate partial load of an aggregate root, so one `GetByIdAsync` returning the full tree is correct.
- New `IOwnershipValidator.EnsureAngebotOwnership(Angebot, int)` — the anticipated third ownership relationship, extending the same service again.
- Explicitly verified (and the user specifically checked this in review) that the handler contains **zero** knowledge of: which states are editable, the implicit `ChangesRequested → Draft` auto-transition, or totals recalculation — all three live entirely inside `Angebot.AddSection(...)`, built in Phase 1. This slice produced the thinnest handler in the project to that point (five lines of real logic).
- **Audit decision:** no `AuditLog` entry, even though this command *can* trigger the internal `ChangesRequested → Draft` transition. Reasoning: that transition is "making the draft editable again" from the user's perspective (an implementation detail of resuming editing), not a new business milestone — audit stays focused on explicit workflow events, consistent with the classification already used for photo-upload/notes-update.

**New abstractions introduced:** `IAngebotRepository.GetByIdAsync`, `IOwnershipValidator.EnsureAngebotOwnership`, `SectionDto`.

**Documentation updates:** None — no new gap or inconsistency found in this slice.

**Tests added:** 10 (`AddAngebotSectionCommandHandlerTests`) — happy path, **explicit proof of the `ChangesRequested → Draft` auto-transition**, not-found, ownership failure, Domain guard-failure propagation (Angebot in `InReview`, locked from editing), validation failures.

**Final outcome:** 72 Application tests, 147 Domain tests → **219 solution-wide.** Committed.

---

## Slice 8 — `SubmitAngebotForReviewCommand`

**Goal:** Sequence Diagram §5 (submission branch). The first Angebot command that is a genuine business milestone rather than an editing operation, and the first Angebot-workflow command whose full pattern set (repository, ownership, audit, notification) matched the documentation with zero inconsistency to resolve.

**Design decisions & architectural discussion:**
- No new repository method — existing `GetByIdAsync` sufficient.
- `Angebot.SubmitForReview()`'s own self-guards (`Status == Draft` **and** "at least one section has at least one item," both built in Phase 1) confirmed to fully cover StateMachine §2.3 — neither duplicated in the handler.
- **Audit target resolved cleanly, no ambiguity this time:** StateMachine §1.3 explicitly states Angebot's internal review transitions cause "no Lead-level change" — so, unlike `CreateAngebotCommand`, the audit entry unambiguously targets `Angebot`, not `Lead`. This is presented as the *same* general principle (§D26) correctly producing the opposite answer in a different situation, not a special case.
- **First real use of the second notification model:** SRS FR-9.2 explicitly lists "an Inspector submits an Angebot for review" as one of its three named Admin-notification triggers. `AngebotSubmittedForReviewNotification(AngebotId, AngebotNumber, LeadId)` — `LeadId` included since it's already on the loaded `Angebot` at zero extra query cost, useful for a future dashboard link.
- Execution order explicitly reaffirmed: Domain transition → `SaveChangesAsync` → audit → notification (never notify before persistence succeeds).

**New abstractions introduced:** `AuditAction.AngebotSubmittedForReview`, `AngebotSubmittedForReviewNotification`, `IEmailSender.SendAngebotSubmittedForReviewNotificationAsync`.

**Documentation updates:** None — first slice where the documentation already fully anticipated everything needed.

**Tests added:** 10 (`SubmitAngebotForReviewCommandHandlerTests`) — happy path (including audit-target-is-Angebot and notification-content assertions), not-found, ownership failure, two distinct Domain guard-failure paths (no items yet; already `InReview`), explicit proof that a failed Domain guard produces **no** audit entry and **no** notification, validation failures.

**Final outcome:** 82 Application tests, 147 Domain tests → **229 solution-wide.** Committed.

---

## Slice 9 — `ApproveAngebotCommand`

**Goal:** Sequence Diagram §5 (Admin-approves branch). First command performed by an Admin rather than an Inspector — the first genuinely different authorization model in the whole project.

**Design decisions & architectural discussion:**
- **Central question of this slice:** does `IOwnershipValidator` apply here? Investigated `PermissionMatrix.md` §4: "Approve Angebot — Admin **F**" (full access), not `S` (scoped) — a fundamentally different marking than every prior ownership-checked action. Concluded: **no**, `IOwnershipValidator` is not called at all in this handler — "any authenticated Admin may approve any Angebot" is a pure role-based rule with no ownership concept whatsoever, resolved entirely at the not-yet-built API layer (`[Authorize(Roles="Admin")]`).
- This formalized the general role-vs-ownership split as `Architecture.md` §7.3's full, explicit rule (building on the narrower observation first made in Slice 3): consult PermissionMatrix's letter (`F` vs. `S`) mechanically to decide whether `IOwnershipValidator` participates in any future command.
- `Angebot.Approve(reviewedByAdminId)`'s own self-guard (`Status == InReview`) and its own recording of `ReviewedByAdminId` (approval metadata) confirmed to be entirely Domain's responsibility — the handler passes the value through and does nothing else with it.
- No notification — internal approval is not customer-facing; both SRS FR-9.2 and Sequence Diagram §5 omit an email step here, since the actual customer-facing email belongs to the later "Send Angebot" workflow (Phase 6, not yet built).
- Audit target: `Angebot` (same StateMachine §1.3 "no Lead-level change" reasoning as Slice 8).

**New abstractions introduced:** `AuditAction.AngebotApproved`. **No new repository, ownership, or notification abstractions** — this slice is notable for *not* needing anything new beyond one enum value, precisely because its authorization model, not its data shape, is what's different.

**Documentation updates:** None — `PermissionMatrix.md`'s `F` marking for this action was already correct and unambiguous; no correction needed.

**Tests added:** 8 (`ApproveAngebotCommandHandlerTests`) — happy path (Status transition, `ReviewedByAdminId` recorded, audit entry), not-found, Domain guard-failure propagation (still `Draft`), validation failures. **Deliberately no ownership-failure test** — there is no ownership concept to test failing.

**Final outcome:** 90 Application tests, 147 Domain tests → **237 solution-wide.** Committed.

---

## Slice 10 — `RequestAngebotChangesCommand` + `AngebotReviewComment`

**Goal:** Sequence Diagram §5 (changes-requested branch) — the final command in the originally-planned Angebot workflow list. Notable as the first slice in Phase 2 that extends the **Domain** model itself, not just the Application layer.

**Design decisions & architectural discussion:**
- **Central question, raised and investigated carefully before any code:** this command includes an Admin-entered comment. Does that comment belong inside the `Angebot` aggregate (reopening the Phase 1 decision that kept `AngebotReviewComment` out), live only as transient notification content, or represent a genuinely separate, persisted Domain concept that Phase 1 simply never built?
- **Full evidence gathered before deciding (documentation-first, not code-first):** `ERD.md` models it as its own table, explicitly called an "append-only log" (SRS FR-5.4); Wireframe D3 displays "threaded" comment **history** (not one-shot content); `PermissionMatrix.md` §4 grants **both** roles read access to that history; `Architecture.md` §6's aggregate diagram, re-checked, still does not list it as an Angebot child.
- **The user required one additional specific verification before approving the new aggregate:** does the documented workflow actually support multiple review cycles (Draft → InReview → ChangesRequested → Draft → ... repeated), or just one? This was explicitly checked, not assumed — SRS FR-5.3 ("this loop may repeat as many times as needed") and Sequence Diagram §5 ("Loop repeats until Admin approves") both confirmed it explicitly. Only after this was confirmed was the new aggregate approved.
- **Final decision:** `AngebotReviewComment` is a genuine Domain gap, filled now — a new, independent aggregate root (`Create` only, no update/delete, matching ERD's "append-only" description), not a feature addition and not a reversal of the Phase 1 boundary decision. See `ARCHITECTURE_DECISIONS.md` D32.
- `Angebot.RequestChanges(reviewedByAdminId)` (built in Phase 1) was explicitly preserved as taking **no** comment parameter — it performs only the workflow transition; the `AngebotReviewComment` is created **independently** in the Application layer. Neither aggregate's type references the other at all — verified with reflection-based structural tests in `Domain.Tests` (in addition to the Application-level test proving the handler is what composes them).
- No `IOwnershipValidator` call — same Admin-`F` reasoning as Slice 9 (`PermissionMatrix.md` §4 also marks "Request changes" `F`).
- Audit target: `Angebot` (same reasoning as Slices 8/9) — the comment itself is supporting business data, never the audit target.
- **Notification gap noted, not a contradiction:** SRS FR-9.2's own enumeration is Admin-notifications only and doesn't mention notifying the *Inspector*, but Sequence Diagram §5 explicitly shows "Notify Inspector with comment." Followed the diagram (the behavior is obviously sensible — the Inspector must know changes were requested and why) and noted this as a minor SRS completeness gap in the decision log, not a real contradiction.

**New abstractions introduced (Domain):** `AngebotReviewComment` entity, with 4 Domain tests (`Create` field-setting, comment trimming, empty-comment rejection) plus 2 new structural/independence tests confirming neither `AngebotReviewComment` nor `Angebot` references the other's type (checked including generic type arguments, to catch a hidden `List<T>`).

**New abstractions introduced (Application):** `IAngebotReviewCommentRepository` (`AddAsync` only), `AngebotChangesRequestedNotification`, `IEmailSender.SendAngebotChangesRequestedNotificationAsync`, `AuditAction.AngebotChangesRequested`.

**Documentation updates:** None required beyond the analysis itself — all supporting evidence (ERD, Wireframes, PermissionMatrix, SRS, Sequence Diagram) was already present and consistent; this slice's job was to *notice* the gap between documentation and implementation, not to fix a documentation inconsistency.

**Tests added:** 6 Domain tests (`AngebotReviewCommentTests`, including the 2 structural independence tests) + 12 Application tests (`RequestAngebotChangesCommandHandlerTests`) — happy path (Angebot transition, `ReviewedByAdminId` recorded, comment fields, audit target, notification content), **an explicit "composes independently" test** (comment's `AngebotId` is the only link; the Angebot repository's `AddedAngebote` stays empty since the Angebot was loaded/mutated, never re-added), not-found, Domain guard-failure propagation (comment creation and everything downstream skipped when the guard fires), validation failures.

**Final outcome:** 102 Application tests, 153 Domain tests → **255 solution-wide.** This slice closed out the entire originally-planned Angebot workflow (`Create`, `AddSection`, `SubmitForReview`, `Approve`, `RequestChanges`) except `AddAngebotItemCommand`, which was **deliberately postponed** (see below). Committed.

---

## Slice 11 — `CreateCatalogItemCommand`

**Goal:** First slice of the CatalogItem Application layer (see `NEXT_STEPS.md` §1 for the full recommended order — Create → Update → Retire → Search). Closest precedent: `CreateLeadCommand` — a straightforward create-and-persist, no cross-aggregate concerns, since `CatalogItem` is an independent aggregate.

**Design decisions & architectural discussion (design-reviewed and approved before implementation, covering the whole feature, not just this slice):**
- Confirmed via `PermissionMatrix.md` §6: every CatalogItem action (Create, Update, Retire, Search) is Admin/Inspector-**F** (full access), never `S` (scoped) — the first entire feature in the project with **zero** `IOwnershipValidator` calls anywhere in it, same treatment as `ApproveAngebotCommand`/`RequestAngebotChangesCommand`.
- Audit: all three mutating commands (Create/Update/Retire) will log against `CatalogItem` itself — no cross-aggregate audit-target ambiguity like Lead/Angebot had, since the entity mutated *is* the entity the business cares about.
- Notification: none — SRS FR-9.2 names no Catalog-related trigger.
- `SaveAngebotItemAsCatalogItemCommand` explicitly deferred to the `AddAngebotItemCommand` slice (it operates on `AngebotItem`, not `CatalogItem` — its natural home is the Angebot workflow feature, not this one).
- `SearchCatalogItemsQuery` (last in the order, not part of this slice) will use a new `IQueryHandler<TQuery, TResult>` interface rather than reusing `ICommandHandler` — see `ARCHITECTURE_DECISIONS.md` D36 — and will take **no** `includeRetired` parameter, always excluding retired items, since no documented use case needs to see them (D37).
- This specific slice, `CreateCatalogItemCommand`, introduced no new architectural territory of its own beyond the feature-level decisions above — its shape mirrors `CreateLeadCommand` exactly (validate → construct via `CatalogItem.Create` → persist → audit → return DTO), with no notification branch.

**New abstractions introduced:** `ICatalogItemRepository` (`AddAsync` only — the absolute minimum this command needs), `CatalogItemDto` (header/scalar fields only — `DefaultUnit`/`SuggestedUnitPrice` unwrapped from `ItemUnit`/`Money` per `CLAUDE.md` §7), `AuditAction.CatalogItemCreated`.

**Documentation updates:** `ARCHITECTURE_DECISIONS.md` gained D36 (`IQueryHandler` as a deliberate second dispatch abstraction) and D37 (`SearchCatalogItemsQuery` starts with no `includeRetired` parameter) — both decided during this slice's design review for the feature as a whole, ahead of the commands/query they'll actually apply to.

**Tests added:** 6 (`CreateCatalogItemCommandHandlerTests`) — happy path (DTO shape including unwrapped `DefaultUnit`/`SuggestedUnitPrice`), repository add, save-changes count, audit entry (entity type `CatalogItem`, correct action/performer), validation failure with no side effects, negative-price validation failure.

**Final outcome:** 108 Application tests, 153 Domain tests → **261 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 12 — `UpdateCatalogItemCommand`

**Goal:** Second slice of the CatalogItem Application layer. The first genuine `Update`-shaped command in the whole Application layer — every prior command has been Create or a workflow transition only.

**Design decisions & architectural discussion:**
- `ICatalogItemRepository` gained `GetByIdAsync` — first use case needing to load an existing `CatalogItem`, following the same on-demand growth as every other repository.
- `CatalogItem.Update(title, defaultUnit, suggestedUnitPrice, defaultSpecification)`'s own self-guards (`Title` non-empty, price non-negative) confirmed sufficient — the handler does not duplicate them beyond FluentValidation's shape-only check.
- Explicitly verified `Update` never touches `CreatedFromAngebotItemId`, `CreatedAt`, or `IsRetired` (Domain-level guarantee, `CatalogItem.cs`'s own doc comment) — added a test proving this rather than just trusting the comment.
- No `IOwnershipValidator` call — same Admin-`F` reasoning as Slice 11, confirmed unchanged.
- Audit: `AuditAction.CatalogItemUpdated`, target `CatalogItem` — same reasoning as Create.
- No notification — same as Create.

**New abstractions introduced:** `ICatalogItemRepository.GetByIdAsync`, `AuditAction.CatalogItemUpdated`. No new DTO — reuses `CatalogItemDto`.

**Documentation updates:** None beyond this log entry — no new gap or inconsistency found; this slice exercised decisions already made during Slice 11's design review.

**Tests added:** 7 (`UpdateCatalogItemCommandHandlerTests`) — happy path (DTO shape with updated values), explicit proof that `CreatedFromAngebotItemId`/`CreatedAt`/`IsRetired` are unchanged, save-changes count, audit entry, not-found, validation failure with no side effects (including proof the seeded entity itself is unchanged), negative-price validation failure.

**Final outcome:** 115 Application tests, 153 Domain tests → **268 solution-wide.** Committed.

---

## Slice 13 — `RetireCatalogItemCommand`

**Goal:** Third slice of the CatalogItem Application layer. Before implementing, the user asked for a short, explicit Domain-behavior verification rather than assuming the existing `Retire()` design was sufficient.

**Design decisions & architectural discussion:**
- **Explicit pre-implementation verification (requested by the user), all four confirming the existing design with no changes needed:**
  1. `CatalogItem.Retire()` is intentionally idempotent — no guard, `IsRetired = true` unconditionally; confirmed by its own doc comment and `CatalogItemTests.Retire_IsIdempotent`.
  2. No path exists for a retired item to become active again — `IsRetired` has a `private set` written only by `Retire()`, one-directional.
  3. No current or planned use case needs "Unretire" — checked `SRS.md`, `Wireframes.md`, `PermissionMatrix.md`, `BusinessRules.md`, `ERD.md`; zero mentions anywhere.
  4. No business rule should block retirement based on historical `AngebotItem` references — BR-8's copy-on-create semantics mean past `AngebotItem`s are structurally indifferent to a `CatalogItem`'s later edits or retirement, proven by `CatalogItemTests.UpdatingACatalogItem_DoesNotAffectAnAngebotItemAlreadyCreatedFromIt_BR8`. A retirement guard here would contradict BR-8's own point.
- Handler is the simplest of the three CatalogItem commands: load → `Retire()` (no parameters, no guard failure possible) → persist → audit. No Domain guard-failure test exists for the same reason `ApproveAngebotCommandHandler` has none — there's no invalid "from" state.
- No `IOwnershipValidator`, no notification — same reasoning as Create/Update, re-confirmed unchanged.

**New abstractions introduced:** `AuditAction.CatalogItemRetired` only. No new repository method (`GetByIdAsync` already existed from Slice 12), no new DTO.

**Documentation updates:** None beyond this log entry — the pre-implementation verification confirmed the existing Domain design rather than surfacing a gap.

**Tests added:** 7 (`RetireCatalogItemCommandHandlerTests`) — happy path (`IsRetired` true in the returned DTO), save-changes count, audit entry, **explicit idempotency proof** (retiring an already-retired item succeeds without error), not-found, validation failures (two invalid-id/user-id cases).

**Final outcome:** 122 Application tests, 153 Domain tests → **275 solution-wide.** Committed.

---

## Why `AddAngebotItemCommand` Was Intentionally Postponed

`AddAngebotItemCommand` is next in Sequence Diagram §4's literal flow, immediately after `AddAngebotSectionCommand`. It was explicitly **not** built yet, by deliberate user decision, for the following reason:

`AddAngebotItemCommand` represents **one** business use case with **two** supported paths (BR-8, SRS FR-4.9): adding an item copied from an existing `CatalogItem`, or adding a fully custom item. `CatalogItem`'s own Application layer (`Create`/`Update`/`Retire`/`Search`) had not been built at the point this command was reached in the implementation order. Implementing only the custom-item path now, then returning later to add the Catalog-sourced path once `CatalogItem` existed, would have produced a temporary, structurally-incomplete vertical slice that would immediately need to be reopened and modified — the opposite of the "finish a slice completely before moving on" discipline this whole phase has followed.

**Decision:** finish the rest of the originally-planned Angebot workflow first (Slices 6–10 above), then build `CatalogItem`'s Application layer as its own dedicated feature, then return to `AddAngebotItemCommand` and implement both paths together, from the start, in one complete slice. See `NEXT_STEPS.md` for the exact recommended order once CatalogItem is done, and `ARCHITECTURE_DECISIONS.md` D30 for the full decision record.

**As of this document, CatalogItem's Application layer has not yet been started — it is the immediate next task.**
