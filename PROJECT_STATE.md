# PROJECT_STATE.md — Where RenoTrack Actually Stands

**Last updated:** 2026-07-30 — Phase 2 closeout. `SaveAngebotItemAsCatalogItemCommand` was reviewed and confirmed **not** part of Phase 2's roadmap-defined scope (`ARCHITECTURE_DECISIONS.md` D39); Phase 2's actual scope (`PROJECT_ROADMAP.md`'s nine-command list) is fully complete as of Slice 15. Ready for PR.
**Purpose:** A precise, current snapshot — not a summary of history (see `PHASE2_PROGRESS.md` and `ARCHITECTURE_DECISIONS.md` for that). If a fact here conflicts with something you infer from reading old chat history, **this file and the actual code are authoritative.**

---

## 1. Current Phase

**Phase 2 — Application Layer**, per `PROJECT_ROADMAP.md`. **Roadmap-defined scope complete; ready for PR.**

- Phase 0 (Solution bootstrap) — ✅ merged to `main`.
- Phase 1 (Domain core: Lead, Inspection, Angebot) — ✅ merged to `main`.
- Phase 1b (Domain: CatalogItem) — ✅ merged to `main`.
- **Phase 2 (Application layer) — ✅ scope complete, not yet merged/pushed**, on branch `feature/phase-2-application-layer` (15 vertical slices committed locally; see §5). `CatalogItem`'s Application layer (Slices 11–14) was a justified in-scope insertion, needed by `AddAngebotItemCommand`. `SaveAngebotItemAsCatalogItemCommand` reviewed and confirmed out of scope (`ARCHITECTURE_DECISIONS.md` D39) — not a gap, a deliberate exclusion.
- Phase 3 onward — not started.

## 2. Current Branch State

- Active branch: `feature/phase-2-application-layer`.
- This branch is **not yet pushed** to `origin`. It contains 15 local commits (one per vertical slice) plus two documentation commits (one mid-phase handoff, one closeout), per the established convention of accumulating a phase's slices before opening one PR — see `CLAUDE.md` §19. Commit range: `ef9bc27` (Slice 1) through `0628031` (Phase 2 closeout docs), 17 commits total.
- `main` is up to date locally as of the last `git fetch`/`merge --ff-only` performed after Phase 1b's PR was merged.
- **Next git action when resuming:** open the Phase 2 PR (see §9 below for the recommended title and full closeout review). Do not push until instructed.

## 3. Build & Test Status (verify this yourself before trusting it — it may be stale)

As of the last verified run in this conversation:
- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **297 tests passing, 0 failing.**
  - `RenoTrack.Domain.Tests`: **153 tests.**
  - `RenoTrack.Application.Tests`: **144 tests.**
  - `RenoTrack.Api.Tests`: 0 tests (project exists, empty — Phase 4 not started).
- **Run both commands again yourself at the start of any new session before writing code.** Do not trust this count without re-verifying; it reflects only what existed when this file was written.

## 4. Domain Layer — Complete Inventory

### 4.1 Aggregate Roots

| Aggregate | File | Children | Key methods |
|---|---|---|---|
| `Lead` | `src/RenoTrack.Domain/Entities/Lead.cs` | none (owns nothing; references by id) | `Create`, `MarkInspectionScheduled`, `MarkInspectionDone`, `MarkAngebotInProgress`, `MarkAngebotSent`, `MarkWon`, `MarkLost`, `AssignInspector` (no status guard — see `CLAUDE.md` §2 and `ARCHITECTURE_DECISIONS.md`) |
| `Inspection` | `src/RenoTrack.Domain/Entities/Inspection.cs` | `InspectionPhoto` | `Schedule`, `AddPhoto` (returns the created `InspectionPhoto`), `UpdateNotes`, `Complete` — all except `Schedule` guarded by BR-10 (immutable once `CompletedAt` is set) |
| `Angebot` | `src/RenoTrack.Domain/Entities/Angebot.cs` | `AngebotSection` → `AngebotItem` | `Create`, `AddSection`, `AddItemToSection(AngebotSection section, ...)` (takes the section object, not an id — see `CLAUDE.md` §2), `SubmitForReview`, `Approve`, `RequestChanges`, `Send`, `RecordCustomerApproval`, `RecordCustomerRejection` |
| `CatalogItem` | `src/RenoTrack.Domain/Entities/CatalogItem.cs` | none (independent) | `Create`, `Update`, `Retire` (BR-12 — sets `IsRetired`, no delete method exists) |
| `AngebotReviewComment` | `src/RenoTrack.Domain/Entities/AngebotReviewComment.cs` | none (independent) | `Create` only — append-only, no update/delete (ERD.md: "append-only log") |

### 4.2 Child Entities

| Entity | Parent | Notes |
|---|---|---|
| `InspectionPhoto` | `Inspection` | `internal` constructor; `Id, FileUrl, Caption, UploadedAt` |
| `AngebotSection` | `Angebot` | `internal` constructor + `internal AddItem`; `Subtotal` is a computed property (never stored) |
| `AngebotItem` | `AngebotSection` | `internal` constructor; **no update/remove method** (deliberately left open, not a documented rule — see `CLAUDE.md` §2); `LineTotal` is a computed property; `CatalogItemId` is nullable, passive traceability data only (BR-8) |

### 4.3 Value Objects

| Type | File | Purpose |
|---|---|---|
| `Money` | `src/RenoTrack.Domain/ValueObjects/Money.cs` | Always exact to 2 decimal places (intrinsic invariant). `FromExact(decimal)` wraps an already-exact value; `RoundedPerBR11(decimal)` applies BR-11's rounding policy to a raw calculation result — the only two ways to construct one. `+` and `Sum(...)` never re-round (adding already-rounded values can't create new precision). No `*` operator (deliberately removed — see `ARCHITECTURE_DECISIONS.md`). |
| `ItemUnit` | `src/RenoTrack.Domain/ValueObjects/ItemUnit.cs` | Five standard units (`SquareMeter, Piece, LinearMeter, LumpSum, Meter`) matching SRS FR-4.3's literal codes (`m2, Stk, lfm, pauschal, m`), plus an open `Custom(label)` escape hatch. `Custom` rejects (case-insensitively) any label colliding with a reserved standard code, making stored ambiguity structurally impossible. `Code`/`FromCode` are the single round-trip surface Infrastructure will use in Phase 3 — Domain has zero EF Core awareness. |
| `UnitKind` | `src/RenoTrack.Domain/ValueObjects/UnitKind.cs` | The enum backing `ItemUnit`; not used directly outside it. |
| `VatBreakdownLine` | `src/RenoTrack.Domain/ValueObjects/VatBreakdownLine.cs` | `record(VatRate Rate, Money NetAmount, Money VatAmount)` — one row of `Angebot.VatBreakdown`, always computed, never stored (no ERD column exists for it). |

### 4.4 Enums

| Enum | File | Values |
|---|---|---|
| `LeadStatus` | `Enums/LeadStatus.cs` | `New, InspectionScheduled, InspectionDone, AngebotInProgress, AngebotSent, Won, Lost` |
| `AngebotStatus` | `Enums/AngebotStatus.cs` | `Draft, InReview, ChangesRequested, ApprovedInternally, Sent, CustomerApproved, CustomerRejected` |
| `LeadSource` | `Enums/LeadSource.cs` | `Website, Phone, Email` |
| `VatRate` | `Enums/VatRate.cs` | `Zero=0, Reduced=7, Sixteen=16, Standard=19` (+ `VatRateExtensions.ToPercentage()`) |

### 4.5 Domain Test Coverage (153 tests, `RenoTrack.Domain.Tests`)

One test class per entity/value-object, in `tests/RenoTrack.Domain.Tests/{Entities,ValueObjects}/`: `ItemUnitTests`, `MoneyTests`, `LeadTests`, `InspectionTests`, `InspectionPhotoTests`, `AngebotItemTests`, `AngebotSectionTests`, `AngebotTests`, `CatalogItemTests`, `AngebotReviewCommentTests`. `RenoTrack.Domain.csproj` has `<InternalsVisibleTo Include="RenoTrack.Domain.Tests" />` so tests can exercise `internal` constructors directly.

---

## 5. Application Layer — Complete Inventory

### 5.1 Common Infrastructure (`RenoTrack.Application.Common`)

| Item | Location | Notes |
|---|---|---|
| `ICommandHandler<TCommand, TResult>` | `Common/ICommandHandler.cs` | The write-side dispatch abstraction — no MediatR |
| `IQueryHandler<TQuery, TResult>` | `Common/IQueryHandler.cs` | The read-side counterpart — a deliberate second abstraction, not a reuse of `ICommandHandler` (`ARCHITECTURE_DECISIONS.md` D36). First (and so far only) consumer: `SearchCatalogItemsQuery`. |
| `AuditAction` (enum) | `Common/AuditAction.cs` | Current values: `LeadCreated, InspectionScheduled, InspectionDone, AngebotCreated, AngebotSubmittedForReview, AngebotApproved, AngebotChangesRequested, CatalogItemCreated, CatalogItemUpdated, CatalogItemRetired` |
| `OwnershipValidator` : `IOwnershipValidator` | `Common/OwnershipValidator.cs` | Implemented directly in Application (no external dependency); methods: `EnsureInspectionOwnership`, `EnsureLeadOwnership`, `EnsureAngebotOwnership` |
| `NotFoundException` | `Common/Exceptions/NotFoundException.cs` | → 404 (Phase 4) |
| `ForbiddenException` | `Common/Exceptions/ForbiddenException.cs` | → 403 (Phase 4) |
| `ConflictException` | `Common/Exceptions/ConflictException.cs` | → 409 (Phase 4) |

### 5.2 Repository & Service Interfaces (`Common/Interfaces/`)

| Interface | Methods (all as of this writing) |
|---|---|
| `ILeadRepository` | `AddAsync`, `GetByIdAsync` |
| `IInspectionRepository` | `AddAsync`, `GetByIdAsync` |
| `IAngebotRepository` | `AddAsync`, `GetByIdAsync`, `HasActiveAngebotForLeadAsync` |
| `IAngebotReviewCommentRepository` | `AddAsync` only |
| `IUnitOfWork` | `SaveChangesAsync` |
| `IAuditService` | `LogAsync` |
| `IEmailSender` | `SendNewWebsiteLeadNotificationAsync`, `SendAngebotSubmittedForReviewNotificationAsync`, `SendAngebotChangesRequestedNotificationAsync` |
| `IFileStorage` | `SaveAsync` only (`GetAsync`/`DeleteAsync` not yet built) |
| `INumberGeneratorService` | `NextAngebotNumberAsync` |
| `IOwnershipValidator` | `EnsureInspectionOwnership`, `EnsureLeadOwnership`, `EnsureAngebotOwnership` |
| `ICatalogItemRepository` | `AddAsync`, `GetByIdAsync` |

`ICatalogItemQueries` (`SearchAsync`) lives in `CatalogItems/ICatalogItemQueries.cs`, not this folder — its return type is a feature DTO, so it can't live in `Common.Interfaces` without `Common` depending on a feature folder (same reasoning as D23). CatalogItem's Application layer (repository + queries + all three commands) is now complete.

### 5.3 Notification Models (`Common/Notifications/`)

- `NewWebsiteLeadNotification(int LeadId, string LeadName, string LeadPhone, string LeadEmail)`
- `AngebotSubmittedForReviewNotification(int AngebotId, string AngebotNumber, int LeadId)`
- `AngebotChangesRequestedNotification(int AngebotId, string AngebotNumber, string Comment, int InspectorId)`

### 5.4 Commands & Queries Implemented (15 vertical slices, all with Command/Query + Validator (where applicable) + Handler + tests)

**Leads** (`Application/Leads/`):
- `CreateLeadCommand` → `LeadDto`

**Inspections** (`Application/Inspections/`):
- `ScheduleInspectionCommand` → `InspectionDto`
- `CompleteInspectionCommand` → `InspectionDto`
- `UploadInspectionPhotoCommand` → `PhotoDto`
- `UpdateInspectionNotesCommand` → `InspectionDto`

**Angebote** (`Application/Angebote/`):
- `CreateAngebotCommand` → `AngebotDto`
- `AddAngebotSectionCommand` → `SectionDto`
- `SubmitAngebotForReviewCommand` → `AngebotDto`
- `ApproveAngebotCommand` → `AngebotDto`
- `RequestAngebotChangesCommand` → `AngebotDto`
- `AddAngebotItemCommand` → `AddAngebotItemResult` (`ItemDto` + `AngebotSummaryDto`) — both the custom and Catalog-sourced paths, from the start (BR-8, BR-14)

**CatalogItems** (`Application/CatalogItems/`):
- `CreateCatalogItemCommand` → `CatalogItemDto`
- `UpdateCatalogItemCommand` → `CatalogItemDto`
- `RetireCatalogItemCommand` → `CatalogItemDto`
- `SearchCatalogItemsQuery` → `IReadOnlyList<CatalogItemDto>` — **the first query in the codebase**, using `IQueryHandler<TQuery, TResult>` instead of `ICommandHandler`; always excludes retired items (BR-12); no parameters (see `ARCHITECTURE_DECISIONS.md` D36/D37)

**Not yet implemented (deliberately out of Phase 2's scope, not gaps):** `SaveAngebotItemAsCatalogItemCommand` (SRS FR-4.10 — confirmed **not** part of Phase 2's roadmap-defined scope, `ARCHITECTURE_DECISIONS.md` D39; see §7), `UploadInspectionPhotoCommand`'s eventual `GetAsync` companion.

### 5.5 DTOs

| DTO | Location | Notes |
|---|---|---|
| `LeadDto` | `Leads/Dtos/LeadDto.cs` | |
| `InspectionDto` | `Inspections/Dtos/InspectionDto.cs` | |
| `PhotoDto` | `Inspections/Dtos/PhotoDto.cs` | |
| `AngebotDto` | `Angebote/Dtos/AngebotDto.cs` | Header-level only — **no nested `Sections`.** `NetTotal`/`GrossTotal` exposed as plain `decimal` |
| `SectionDto` | `Angebote/Dtos/SectionDto.cs` | No nested `Items` |
| `CatalogItemDto` | `CatalogItems/Dtos/CatalogItemDto.cs` | Header/scalar only; `DefaultUnit`/`SuggestedUnitPrice` unwrapped from `ItemUnit`/`Money` |
| `ItemDto` | `Angebote/Dtos/ItemDto.cs` | No `SectionId` — `AngebotItem` has no such property to map from |
| `AngebotSummaryDto` | `Angebote/Dtos/AngebotSummaryDto.cs` | Lighter than `AngebotDto` — Id/AngebotNumber/Status/NetTotal/GrossTotal only |

**Not yet created:** a `CatalogItemDto` equivalent for `SaveAngebotItemAsCatalogItemCommand`'s response is unnecessary — it already reuses the existing `CatalogItemDto`.

### 5.6 Application Test Coverage (144 tests, `RenoTrack.Application.Tests`)

- `RenoTrack.Application.Tests.csproj` references `RenoTrack.Domain` explicitly (added when the first handler test needed to assert on Domain state).
- Fakes in `tests/RenoTrack.Application.Tests/Fakes/`: `FakeLeadRepository`, `FakeInspectionRepository`, `FakeAngebotRepository`, `FakeAngebotReviewCommentRepository`, `FakeCatalogItemRepository`, `FakeCatalogItemQueries`, `FakeUnitOfWork`, `FakeAuditService`, `FakeEmailSender`, `FakeFileStorage`, `FakeNumberGeneratorService`. `FakeLeadRepository`/`FakeInspectionRepository`/`FakeAngebotRepository`/`FakeCatalogItemRepository` each expose a `Seed(entity)` helper (reflection-based id assignment — test-only). `FakeCatalogItemQueries` implements the same BR-12 retired-item filtering a real implementation must perform, not a dumb passthrough. `AddAngebotItemCommandHandlerTests` additionally assigns `AngebotSection.Id` via the same reflection pattern, inline in the test class — the first test needing to distinguish between sibling child entities by id.
- One test class per handler, in `tests/RenoTrack.Application.Tests/{Leads,Inspections,Angebote,CatalogItems}/Commands/<CommandName>/`, plus `tests/RenoTrack.Application.Tests/CatalogItems/Queries/SearchCatalogItems/` (the first query test) and `tests/RenoTrack.Application.Tests/Common/OwnershipValidatorTests.cs`.

---

## 6. Documentation State

All eight original spec documents live in the repo root and have been actively maintained (not just written once in Phase 0):

| Document | Modified during Phase 1/2? | What changed |
|---|---|---|
| `SRS.md` | No | Unmodified since Phase 0 |
| `Architecture.md` | **Yes, extensively** | §6.1/§6.2 (Domain design decisions), §7.3 (role vs. ownership — new), §9 (stable external resource identifiers — new), §11 (audit-target principle — new) |
| `ERD.md` | **Yes** | `CatalogItem.IsRetired` column added (BR-12) |
| `Sequence Diagram.md` | **Yes** | §4 corrected (added missing AuditLog step for Angebot creation; fixed stale `CreateDraft` → `Create` reference) |
| `StateMachine.md` | **Yes** | §1.3 `ScheduleInspection` row's side-effects updated for BR-13 |
| `BusinessRules.md` | **Yes, extensively** | BR-10, BR-11, BR-12, BR-13, BR-14 all added, each with a Changelog row |
| `PermissionMatrix.md` | **Yes** | §1 "Assign/reassign Inspector" row clarified for BR-13; §6 "Delete/retire" row clarified for BR-12 and cross-referenced to BR-14 |
| `Wireframes.md` | No | Unmodified since Phase 0 |
| `PROJECT_ROADMAP.md` | No (but see below) | Still reflects the original phase plan; **does not yet reflect** that AngebotReviewComment work happened inside Phase 2 rather than a dedicated earlier phase, or that Phase 2's Angebot-workflow ordering deferred `AddAngebotItemCommand`. Notably, its own Phase 2 command list is what the Slice 15 closeout review used to confirm `SaveAngebotItemAsCatalogItemCommand` was never in scope — this document's original scoping held up under scrutiny. |

**New permanent documentation (this handoff):** `CLAUDE.md`, `ARCHITECTURE_DECISIONS.md`, `PHASE2_PROGRESS.md`, `NEXT_STEPS.md`, this file, and `HANDOFF_PROMPT.md`.

Current `BusinessRules.md` rule count: **BR-1 through BR-14** (BR-1–BR-9 from original SRS extraction; BR-10–BR-14 added during Phase 1/Phase 2).

---

## 7. Deferred / Known-Incomplete Work (do not treat these as bugs — they are intentional, documented deferrals)

1. **`AddAngebotItemCommand` — ✅ complete (Slice 15).** Both the Catalog-sourced and custom-item paths implemented from the start, per the standing decision. See `PHASE2_PROGRESS.md` Slice 15 for the full design-review record, including BR-14 and the `NEXT_STEPS.md` §2 wording correction.
2. **CatalogItem Application layer — ✅ complete.** `CreateCatalogItemCommand`, `UpdateCatalogItemCommand`, `RetireCatalogItemCommand`, `SearchCatalogItemsQuery` all done (Slices 11–14).
3. **`SaveAngebotItemAsCatalogItemCommand` (SRS FR-4.10) — deliberately deferred, confirmed out of Phase 2's scope.** Reviewed explicitly rather than assumed-in-scope: `PROJECT_ROADMAP.md`'s Phase 2 command list never included it, and building it now would force a new, single-purpose Application-layer lookup capability (resolving an `AngebotItem`'s owning `Angebot` from the item's id alone) with no other justification. See `ARCHITECTURE_DECISIONS.md` D39. Revisit when a phase that actually needs it arrives (most naturally Phase 3, once real EF ids exist).
4. **`SearchCatalogItemsQuery` is the only query in the codebase so far.** Every command still returns a DTO built from the same aggregate it just mutated. Other read-side needs (list views, a Lead pipeline query, etc.) have not been started — this is normal for where Phase 2 currently stands, not a gap to rush to fill.
5. **`IFileStorage.GetAsync`/`DeleteAsync`** — not built (§4's repository-growth discipline applies here too).
6. **`Angebot.Send()`, `RecordCustomerApproval()`, `RecordCustomerRejection()`** exist in the Domain (built in Phase 1) but have **no Application-layer commands yet** — deliberately deferred to Phase 6 (Token-link mechanism) per `PROJECT_ROADMAP.md`, since they depend on `ITokenLinkService`, which doesn't exist yet.
7. **`AngebotItem` has no update/remove method** — an open question, not a bug (see `CLAUDE.md` §2). Revisit only if real evidence (a documented endpoint or explicit business decision) appears.
8. **No Infrastructure project code exists at all yet** (`RenoTrack.Infrastructure` is still the Phase 0 empty skeleton). Every interface listed in §5.2 has zero concrete implementation. This is expected — Phase 3 is Infrastructure.
9. **`INumberGeneratorService`'s atomic-transaction requirement (Architecture §8) is unverified** — flagged as the single highest-risk assumption to explicitly test once Phase 3 builds a real implementation.

---

## 8. Immediate Next Step

**Open the Phase 2 PR.** See §9 below for the full closeout review (roadmap-item verification, deferred-item audit, doc consistency, test/build confirmation, recommended PR title and commit range). After the PR, Phase 3 (Infrastructure) begins.

---

## 9. Phase 2 Closeout Review

Performed 2026-07-30, immediately before opening the PR.

**1. Every Phase 2 roadmap item complete?** Yes. `PROJECT_ROADMAP.md`'s Phase 2 command list — `CreateLeadCommand`, `ScheduleInspectionCommand`, `CompleteInspectionCommand`, `CreateAngebotCommand`, `AddAngebotSectionCommand`, `AddAngebotItemCommand`, `SubmitAngebotForReviewCommand`, `RequestAngebotChangesCommand`, `ApproveAngebotCommand` — all nine are implemented, tested, and committed (§5.4). `CatalogItem`'s Application layer, while not itself on that list, was a necessary and justified prerequisite for `AddAngebotItemCommand` and is also complete.

**2. Every deferred item explicitly documented with a reason?** Yes — cross-checked against §7 above and `NEXT_STEPS.md` §2:
- `SaveAngebotItemAsCatalogItemCommand` — out of scope, `ARCHITECTURE_DECISIONS.md` D39 (roadmap-scope check + premature-lookup-capability reasoning).
- `IFileStorage.GetAsync`/`DeleteAsync` — no current command needs them (`CLAUDE.md` §4).
- `Angebot.Send()`/`RecordCustomerApproval()`/`RecordCustomerRejection()` Application commands — deferred to Phase 6, depend on `ITokenLinkService`.
- `AngebotItem` update/remove methods — open question, not a rule, `ARCHITECTURE_DECISIONS.md` D12.
- HTTP status-code mapping for Domain exceptions — deferred to Phase 4.
- `INumberGeneratorService`'s atomic-transaction guarantee — unverified, flagged as highest-risk, to be tested in Phase 3.

None of these are silent gaps; each has a named reason and a named future trigger for revisiting.

**3. Are `PROJECT_STATE.md`, `NEXT_STEPS.md`, and `PHASE2_PROGRESS.md` internally consistent?** Yes, verified by cross-reading all three just now: all three agree Phase 2's roadmap scope (§5.4's fifteen slices) is complete, all three agree `SaveAngebotItemAsCatalogItemCommand` is deferred (not "the next slice"), and all three point to the same `ARCHITECTURE_DECISIONS.md` D39 for the reasoning. `PHASE2_PROGRESS.md` gained its own closing "Phase 2 Scope Correction & Closeout" section recording the same decision in slice-log form.

**4. Final test count?** **297 tests, 0 failing** — 153 `RenoTrack.Domain.Tests` + 144 `RenoTrack.Application.Tests` (`RenoTrack.Api.Tests` still empty, Phase 4 not started).

**5. Build clean?** **Yes — 0 Warnings, 0 Errors** (`TreatWarningsAsErrors` solution-wide, `CLAUDE.md` §14).

**6. Recommended PR title and commit range:**
- **Title:** `Phase 2: Application layer — Lead/Inspection/Angebot commands, queries, and guards` (matches `PROJECT_ROADMAP.md`'s own pre-named PR title for this phase, line 92 — no reason to deviate).
- **Commit range:** `main..feature/phase-2-application-layer` — `ef9bc27` (Slice 1, `CreateLeadCommand`) through `0628031` (Phase 2 closeout docs). **17 commits total:** 15 vertical slices, one mid-phase handoff-documentation commit (`38c4314`), and this closeout commit (`0628031`).
- **PR description should note explicitly:** `CatalogItem`'s Application layer was an in-scope, justified insertion (needed by `AddAngebotItemCommand`); `SaveAngebotItemAsCatalogItemCommand` was reviewed and confirmed out of scope (D39), not overlooked.
