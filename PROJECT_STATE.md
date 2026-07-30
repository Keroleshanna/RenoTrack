# PROJECT_STATE.md — Where RenoTrack Actually Stands

**Last updated:** 2026-07-30 — Phase 3, Slice 13 complete (`IEmailSender` → `LoggingNoOpEmailSender`, per `CLAUDE.md` §11). Phase 2 merged to `main` (PR #5, merge commit `dc85de1`).
**Purpose:** A precise, current snapshot — not a summary of history (see `PHASE2_PROGRESS.md` and `ARCHITECTURE_DECISIONS.md` for that). If a fact here conflicts with something you infer from reading old chat history, **this file and the actual code are authoritative.**

---

## 1. Current Phase

**Phase 3 — Infrastructure**, per `PROJECT_ROADMAP.md`. In progress, on branch `feature/phase-3-infrastructure-efcore`, not yet merged.

- Phase 0 (Solution bootstrap) — ✅ merged to `main`.
- Phase 1 (Domain core: Lead, Inspection, Angebot) — ✅ merged to `main`.
- Phase 1b (Domain: CatalogItem) — ✅ merged to `main`.
- **Phase 2 (Application layer) — ✅ merged to `main`** (PR #5, merge commit `dc85de1`; 15 vertical slices + documentation commits, branch `feature/phase-2-application-layer`). `CatalogItem`'s Application layer (Slices 11–14) was a justified in-scope insertion, needed by `AddAngebotItemCommand`. `SaveAngebotItemAsCatalogItemCommand` reviewed and confirmed out of scope (`ARCHITECTURE_DECISIONS.md` D39) — not a gap, a deliberate exclusion, to be revisited in Phase 3+.
- **Phase 3 (Infrastructure) — 🔶 in progress.** Design review + dependency map approved; Slices 1–13 complete (`RenoTrackDbContext` + entity configurations + `RenoTrack.Infrastructure.Tests`; `InitialCreate` migration; `UnitOfWork`; `ILeadRepository`; `IInspectionRepository`; `IAngebotRepository`; `IAngebotReviewCommentRepository`; `ICatalogItemRepository`; `ICatalogItemQueries`; `IAuditService`; `INumberGeneratorService`; `IFileStorage` placeholder; `IEmailSender` placeholder). See `PHASE3_PROGRESS.md`.

## 2. Current Branch State

- Active branch: `feature/phase-3-infrastructure-efcore`, created off `main` (at `dc85de1`) per `CLAUDE.md` §19 — no direct commits to `main` after Phase 0's bootstrap.
- `feature/phase-2-application-layer` was merged via PR #5 and is no longer the active working branch.
- **Next git action when resuming:** continue committing additional Infrastructure slices to this same branch, in the approved dependency-map order (see `PHASE3_PROGRESS.md`). Do not open a PR or push until instructed.

## 3. Build & Test Status (verify this yourself before trusting it — it may be stale)

As of the last verified run in this conversation:
- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **358 tests passing, 0 failing.**
  - `RenoTrack.Domain.Tests`: **153 tests.**
  - `RenoTrack.Application.Tests`: **144 tests.**
  - `RenoTrack.Infrastructure.Tests`: **61 tests** (real SQL Server LocalDB integration tests — new in Phase 3; `PlaceholderFileStorageTests`/`LoggingNoOpEmailSenderTests` are the exceptions, no database involved).
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

## 6. Infrastructure Layer — Complete Inventory (Phase 3, in progress)

### 6.1 `RenoTrackDbContext` (`src/RenoTrack.Infrastructure/Persistence/RenoTrackDbContext.cs`)

One `DbSet<T>` per aggregate root only: `Leads`, `Inspections`, `Angebote`, `CatalogItems`, `AngebotReviewComments`. No `DbSet` for child entities (`AngebotSection`, `AngebotItem`, `InspectionPhoto`) — reachable only through their aggregate root's navigation. `OnModelCreating` calls `ApplyConfigurationsFromAssembly` — no configuration inlined there. No `NumberSequence`/`AuditLog`/Identity `DbSet`s yet — added in their own later slices (§8 below), not speculatively now.

**`InitialCreate` migration** (`Persistence/Migrations/`) — the first real migration, generated via `RenoTrackDbContextFactory` (`IDesignTimeDbContextFactory<RenoTrackDbContext>`, design-time only — DI composition is still Slice 14). Creates exactly 8 tables (matching §6.1's `DbSet`s + child entities); confirmed via `InitialCreateMigrationTests` to apply cleanly to a fresh LocalDB database and to have zero pending model changes (i.e., the migration and the current EF model are in sync). Not yet applied to any shared/persistent database.

### 6.2 Entity Configurations (`src/RenoTrack.Infrastructure/Persistence/Configurations/`)

| Configuration | Notes |
|---|---|
| `LeadConfiguration` | `Source`/`Status` stored as string enums (ERD.md's own stated reason: readability in raw SQL). Index on `(Status, AssignedInspectorId)` for pipeline filtering. `AssignedInspectorId` has no FK yet (Identity slice). |
| `InspectionConfiguration` | `Photos` navigation bound to its backing field (`PropertyAccessMode.Field`) — see D43. `InspectorId` has no FK yet. |
| `InspectionPhotoConfiguration` | `InspectionId` FK is a shadow property. |
| `AngebotConfiguration` | `AngebotNumber` unique index; `Status` index; `Status` stored as string. `NetTotal`/`GrossTotal` via `MoneyConverter`, `decimal(18,2)`. `VatBreakdown` ignored (no ERD column, always computed). `Sections` navigation bound to its backing field. `CreatedByInspectorId`/`ReviewedByAdminId` have no FK yet. |
| `AngebotSectionConfiguration` | `Subtotal` ignored — computed property, no column (D41). `Items` navigation bound to its backing field. |
| `AngebotItemConfiguration` | `LineTotal` ignored (D41). `Unit` via `ItemUnitConverter`; `UnitPrice` via `MoneyConverter`; `VatRate` uses EF's default enum-to-int mapping. `CatalogItemId` **has** a real FK to `CatalogItems` (`DeleteBehavior.Restrict`) — both tables exist today, unlike the Users-referencing columns. |
| `CatalogItemConfiguration` | `DefaultUnit`/`SuggestedUnitPrice` via the same converters. `CreatedFromAngebotItemId` **has** a real FK to `AngebotItems` (`DeleteBehavior.Restrict`). |
| `AngebotReviewCommentConfiguration` | `AngebotId` **has** a real FK to `Angebote` (`DeleteBehavior.Restrict`). `AdminUserId` has no FK yet. |

### 6.3 Value Converters (`src/RenoTrack.Infrastructure/Persistence/ValueConverters/`)

`MoneyConverter` (`Money` ↔ `decimal`, via `.Amount`/`Money.FromExact`), `ItemUnitConverter` (`ItemUnit` ↔ `string`, via `.Code`/`ItemUnit.FromCode` — the exact round-trip surface `ItemUnit`'s own Domain doc comment anticipated).

### 6.4 Repository & Service Interfaces — Implementation Status

- **`IUnitOfWork` → `UnitOfWork` — ✅ done (Slice 3).** One-line wrapper over `RenoTrackDbContext.SaveChangesAsync` — confirmed intentionally thin by explicit design review, `ARCHITECTURE_DECISIONS.md` D48. No `IDisposable` (doesn't own the injected `DbContext`).
- **`ILeadRepository` → `LeadRepository` — ✅ done (Slice 4).** `src/RenoTrack.Infrastructure/Persistence/Repositories/LeadRepository.cs` — the first concrete repository, establishing the `Persistence/Repositories/` folder. `AddAsync`/`GetByIdAsync` only, matching the interface exactly. `GetByIdAsync` uses `DbSet<Lead>.FindAsync` (no `Include` needed — Lead has no navigation properties); its tracked result is relied upon by every handler that loads-then-mutates a Lead, since no `UpdateAsync` exists anywhere in this project. `AddAsync` performs no validation (Domain already guards its own invariants) and never calls `SaveChangesAsync` — that stays exclusively `IUnitOfWork`'s job, verified by a dedicated test.
- **`IInspectionRepository` → `InspectionRepository` — ✅ done (Slice 5).** `src/RenoTrack.Infrastructure/Persistence/Repositories/InspectionRepository.cs` — the first repository with a child collection. `GetByIdAsync` eagerly `.Include(i => i.Photos)`s (CLAUDE.md §4's "full aggregate" rule, `FindAsync` doesn't support `Include` so `FirstOrDefaultAsync` is used instead). A photo added via `Inspection.AddPhoto(...)` on an aggregate loaded through this repository is persisted by `IUnitOfWork.SaveChangesAsync()` alone, verified by a dedicated test.
- **`IAngebotRepository` → `AngebotRepository` — ✅ done (Slice 6).** `src/RenoTrack.Infrastructure/Persistence/Repositories/AngebotRepository.cs` — the first repository for a two-level aggregate (`Sections` → `Items`). `GetByIdAsync` uses `.Include(a => a.Sections).ThenInclude(s => s.Items)` (`AsSplitQuery` deliberately not used — a single chain, not sibling collections, so no cartesian-product concern at this aggregate size). `HasActiveAngebotForLeadAsync` is a plain `AnyAsync` existence check over `LeadId`/`Status` only (StateMachine.md §2.4's non-terminal definition), no `Include`.
- **`IAngebotReviewCommentRepository` → `AngebotReviewCommentRepository` — ✅ done (Slice 7).** `src/RenoTrack.Infrastructure/Persistence/Repositories/AngebotReviewCommentRepository.cs` — `AddAsync` only, matching the interface exactly. No design review beyond confirming it's a strict subset of already-approved `AddAsync` patterns.
- **`ICatalogItemRepository` → `CatalogItemRepository` — ✅ done (Slice 8).** `src/RenoTrack.Infrastructure/Persistence/Repositories/CatalogItemRepository.cs` — same `AddAsync`/`GetByIdAsync` shape as `LeadRepository` (no children, no navigation). `GetByIdAsync` deliberately does not filter by `IsRetired` (BR-14/D38 — a retired item remains a valid direct reference); that filtering belongs to `ICatalogItemQueries.SearchAsync` (Slice 9), not this repository.
- **`ICatalogItemQueries` → `CatalogItemQueries` — ✅ done (Slice 9).** `src/RenoTrack.Infrastructure/Persistence/Queries/CatalogItemQueries.cs` — the first query implementation (new `Persistence/Queries/` folder, parallel to `Persistence/Repositories/`). Projects directly to `CatalogItemDto` inside `Select(...)` (not via `.ToDto()`, which would force full-entity materialization) — verified genuinely SQL-translatable by the integration tests actually running. `AsNoTracking()`, no `IUnitOfWork` dependency, always excludes `IsRetired` (BR-12/D37, the only place this filter is applied), no parameters (zero-parameter shape already settled in Phase 2).
- **`IAuditService` → `AuditService` — ✅ done (Slice 10).** `src/RenoTrack.Infrastructure/Persistence/AuditService.cs`, backed by `AuditLog` (`src/RenoTrack.Infrastructure/Persistence/Entities/AuditLog.cs`) — an Infrastructure-only persistence model, not a Domain entity (D49). Implements the **Best-Effort Audit strategy** (D50): commits its own write independently of `IUnitOfWork` (nothing else would ever persist it, since every handler calls `LogAsync` after its own `SaveChangesAsync`), catches and logs any failure as a warning, never rethrows — an audit-write fault can never invalidate an already-committed business operation.
- **`INumberGeneratorService` → `NumberGeneratorService` — ✅ done (Slice 11).** `src/RenoTrack.Infrastructure/Persistence/NumberGeneratorService.cs`, backed by `NumberSequence` (Infrastructure-only, D51). Implements an atomic single-statement increment (`UPDATE ... OUTPUT`, raw SQL — a deliberate, narrowly-scoped exception to the EF-Core-only convention, D52), decoupled from the Angebot's own `SaveChangesAsync` (not achievable given `CreateAngebotCommandHandler`'s call order — `Architecture.md`/`ERD.md` corrected accordingly). Proven collision-free under real concurrent load by a 50-parallel-caller integration test, resolving the project's highest-risk unverified assumption (D34).
- **`IFileStorage` → `PlaceholderFileStorage` — ✅ done (Slice 12).** `src/RenoTrack.Infrastructure/FileStorage/PlaceholderFileStorage.cs` — always throws `NotImplementedException` rather than a silent no-op (a no-op would drop uploaded photos while appearing to succeed). The real `LocalDiskFileStorage` remains Phase 4's deliverable (D42), unaffected.
- **`IEmailSender` → `LoggingNoOpEmailSender` — ✅ done (Slice 13).** `src/RenoTrack.Infrastructure/Email/LoggingNoOpEmailSender.cs` — never throws (unlike `PlaceholderFileStorage`; the interface's own doc comment explicitly sanctions a no-op/logging placeholder here so Phase 2's handlers run end-to-end without SMTP), but logs a `Warning` on every call so it's never silent. Real SMTP-backed implementation remains Phase 9's deliverable.
- Only `AddInfrastructure()` DI wiring (Slice 14) and Identity (Slice 15) remain in the approved dependency map (`PHASE3_PROGRESS.md`).

### 6.5 Infrastructure Test Coverage (61 tests, `RenoTrack.Infrastructure.Tests`)

Real SQL Server LocalDB integration tests, never the EF Core InMemory provider (`ARCHITECTURE_DECISIONS.md` D40). `RenoTrackDbContextFixture` (`IAsyncLifetime` + `ICollectionFixture<T>`) creates/drops one shared LocalDB database (`RenoTrackInfrastructureTests`) per test run; every test class in the shared `"Infrastructure Database"` collection also seeds a real `Lead` row (via a `SeedLeadAsync` helper) before referencing its id, rather than a hardcoded placeholder — needed once real FKs made a coincidental id-match insufficient. Test classes: `LeadPersistenceTests`, `InspectionPersistenceTests`, `AngebotPersistenceTests`, `CatalogItemPersistenceTests`, `AngebotReviewCommentPersistenceTests` (15 tests total, including the 3 FK-rejection tests added in Slice 2, `EnsureCreated`-based schema), `UnitOfWorkTests` (3 tests), `InitialCreateMigrationTests` (2 tests, its own throwaway database, exercises `Database.MigrateAsync()` and `HasPendingModelChanges()` directly), `LeadRepositoryTests` (4 tests, Slice 4), `InspectionRepositoryTests` (5 tests, Slice 5 — `GetByIdAsync` eagerly loads `Photos`; a photo added post-load persists via `SaveChangesAsync` alone), `AngebotRepositoryTests` (9 tests, Slice 6 — `GetByIdAsync`'s two-level `Include`/`ThenInclude`, a section+item added post-load persisting via `SaveChangesAsync` alone, and `HasActiveAngebotForLeadAsync`'s non-terminal-status semantics driven directly via EF's change tracker since `Angebot.Status`'s only reachable terminal states through Domain methods require a `Sent` precondition), `AngebotReviewCommentRepositoryTests` (3 tests, Slice 7 — `AddAsync`-only contract), `CatalogItemRepositoryTests` (5 tests, Slice 8 — same `AddAsync`/`GetByIdAsync` shape as `LeadRepositoryTests`, plus a BR-14/D38 confirmation that `GetByIdAsync` still returns a retired item), `CatalogItemQueriesTests` (3 tests, Slice 9 — proves the DTO projection is genuinely SQL-translatable and that `IsRetired` is excluded), `AuditServiceTests` (4 tests, Slice 10 — proves `LogAsync` commits independently of `IUnitOfWork`, and that a real underlying write failure is caught and swallowed per the Best-Effort Audit strategy, D50), `NumberGeneratorServiceTests` (4 tests, Slice 11 — including a 50-parallel-caller concurrency test proving no duplicate numbers under real concurrent load against LocalDB), all under `tests/RenoTrack.Infrastructure.Tests/Persistence/`, `PlaceholderFileStorageTests` (1 test, Slice 12, under `tests/RenoTrack.Infrastructure.Tests/FileStorage/` — no database involved), plus `LoggingNoOpEmailSenderTests` (3 tests, Slice 13, under a new `tests/RenoTrack.Infrastructure.Tests/Email/` folder — no database involved, uses a capturing `ILogger` fake to verify the Warning log is actually emitted).

---

## 7. Documentation State

All eight original spec documents live in the repo root and have been actively maintained (not just written once in Phase 0):

| Document | Modified during Phase 1/2? | What changed |
|---|---|---|
| `SRS.md` | No | Unmodified since Phase 0 |
| `Architecture.md` | **Yes, extensively** | §6.1/§6.2 (Domain design decisions), §7.3 (role vs. ownership — new), §9 (stable external resource identifiers — new), §11 (audit-target principle — new). Phase 3: §3's solution structure updated to add `RenoTrack.Infrastructure.Tests` |
| `ERD.md` | **Yes** | `CatalogItem.IsRetired` column added (BR-12). Phase 3: `Subtotal`/`LineTotal`/`DecisionResult` removed to match confirmed Domain state (D41); notes added on which FKs are deferred until the Identity slice (D44) |
| `Sequence Diagram.md` | **Yes** | §4 corrected (added missing AuditLog step for Angebot creation; fixed stale `CreateDraft` → `Create` reference) |
| `StateMachine.md` | **Yes** | §1.3 `ScheduleInspection` row's side-effects updated for BR-13 |
| `BusinessRules.md` | **Yes, extensively** | BR-10, BR-11, BR-12, BR-13, BR-14 all added, each with a Changelog row |
| `PermissionMatrix.md` | **Yes** | §1 "Assign/reassign Inspector" row clarified for BR-13; §6 "Delete/retire" row clarified for BR-12 and cross-referenced to BR-14 |
| `Wireframes.md` | No | Unmodified since Phase 0 |
| `PROJECT_ROADMAP.md` | No (but see below) | Still reflects the original phase plan; **does not yet reflect** that AngebotReviewComment work happened inside Phase 2 rather than a dedicated earlier phase, or that Phase 2's Angebot-workflow ordering deferred `AddAngebotItemCommand`. Notably, its own Phase 2 command list is what the Slice 15 closeout review used to confirm `SaveAngebotItemAsCatalogItemCommand` was never in scope — this document's original scoping held up under scrutiny. |

**New permanent documentation (this handoff):** `CLAUDE.md`, `ARCHITECTURE_DECISIONS.md`, `PHASE2_PROGRESS.md`, `PHASE3_PROGRESS.md`, `NEXT_STEPS.md`, this file, and `HANDOFF_PROMPT.md`.

Current `BusinessRules.md` rule count: **BR-1 through BR-14** (BR-1–BR-9 from original SRS extraction; BR-10–BR-14 added during Phase 1/Phase 2).

---

## 8. Deferred / Known-Incomplete Work (do not treat these as bugs — they are intentional, documented deferrals)

1. **`AddAngebotItemCommand` — ✅ complete (Slice 15).** Both the Catalog-sourced and custom-item paths implemented from the start, per the standing decision. See `PHASE2_PROGRESS.md` Slice 15 for the full design-review record, including BR-14 and the `NEXT_STEPS.md` §2 wording correction.
2. **CatalogItem Application layer — ✅ complete.** `CreateCatalogItemCommand`, `UpdateCatalogItemCommand`, `RetireCatalogItemCommand`, `SearchCatalogItemsQuery` all done (Slices 11–14).
3. **`SaveAngebotItemAsCatalogItemCommand` (SRS FR-4.10) — deliberately deferred, confirmed out of Phase 2's scope.** Reviewed explicitly rather than assumed-in-scope: `PROJECT_ROADMAP.md`'s Phase 2 command list never included it, and building it now would force a new, single-purpose Application-layer lookup capability (resolving an `AngebotItem`'s owning `Angebot` from the item's id alone) with no other justification. See `ARCHITECTURE_DECISIONS.md` D39. Revisit when a phase that actually needs it arrives (most naturally Phase 3, once real EF ids exist).
4. **`SearchCatalogItemsQuery` is the only query in the codebase so far.** Every command still returns a DTO built from the same aggregate it just mutated. Other read-side needs (list views, a Lead pipeline query, etc.) have not been started — this is normal for where Phase 2 currently stands, not a gap to rush to fill.
5. **`IFileStorage.GetAsync`/`DeleteAsync`** — not built (§4's repository-growth discipline applies here too).
6. **`Angebot.Send()`, `RecordCustomerApproval()`, `RecordCustomerRejection()`** exist in the Domain (built in Phase 1) but have **no Application-layer commands yet** — deliberately deferred to Phase 6 (Token-link mechanism) per `PROJECT_ROADMAP.md`, since they depend on `ITokenLinkService`, which doesn't exist yet.
7. **`AngebotItem` has no update/remove method** — an open question, not a bug (see `CLAUDE.md` §2). Revisit only if real evidence (a documented endpoint or explicit business decision) appears.
8. **Infrastructure project — 🔶 in progress (Slices 1–3 of 15 done).** `RenoTrackDbContext` + entity configurations + `InitialCreate` migration + `UnitOfWork` exist and are tested against real LocalDB; every repository/service interface listed in §5.2 except `IUnitOfWork` still has zero concrete implementation (Slices 4–13 build those; see §6.4 and `PHASE3_PROGRESS.md`).
9. **`INumberGeneratorService`'s atomic-transaction requirement (Architecture §8) is unverified** — flagged as the single highest-risk assumption; due for its concurrency test at Slice 11.
10. **`LocalDiskFileStorage`/real `IEmailSender` — deliberately deferred**, not gaps: `LocalDiskFileStorage` is Phase 4's (confirmed against `PROJECT_ROADMAP.md`, `CLAUDE.md` §13 corrected — D42); `IEmailSender`'s real SMTP-backed implementation is Phase 9's (`CLAUDE.md` §11). Phase 3 registers placeholder-only implementations of both (Slices 12–13) purely so DI composition succeeds.
11. **User-referencing FK constraints** (`Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, `AngebotReviewComment.AdminUserId`) — deliberately deferred until the Identity slice (Slice 15) adds a `Users` table (`ARCHITECTURE_DECISIONS.md` D44), not an oversight.

---

## 9. Immediate Next Step

**Begin Slice 14 (`AddInfrastructure()` DI extension + `Program.cs` wiring)** — Slices 1–13 are all complete, reviewed, tested, documented, and committed. Every repository/query/service built in Slices 4–13 now needs real DI registration: `RenoTrackDbContext` (`AddDbContext`, connection string from configuration), each repository/query as Scoped (matching the `DbContext`'s own Scoped lifetime, per D48's finding that this is what makes "repository adds an entity → `UnitOfWork.SaveChangesAsync()` commits it" work), `UnitOfWork`, `AuditService`, `NumberGeneratorService`, `PlaceholderFileStorage`, `LoggingNoOpEmailSender`. This is the first slice to actually wire `RenoTrack.Api`'s `Program.cs` to Infrastructure — expect a real (if not necessarily lengthy) review of lifetime choices and configuration-source handling. See `PHASE3_PROGRESS.md` for the full slice order and each slice's record. §10 below remains the historical record of Phase 2's closeout.

---

## 10. Phase 2 Closeout Review

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
- **Commit range:** `main..feature/phase-2-application-layer`, starting at `ef9bc27` (Slice 1, `CreateLeadCommand`) — 15 vertical-slice commits plus a small number of documentation-only commits (mid-phase handoff docs, closeout docs, and closeout sanity-review corrections). Let `git log main..feature/phase-2-application-layer` at PR-open time be the source of truth for the exact count — it was already drifting across this very closeout review as fix commits landed, which is itself the reason not to hardcode it in prose.
- **PR description should note explicitly:** `CatalogItem`'s Application layer was an in-scope, justified insertion (needed by `AddAngebotItemCommand`); `SaveAngebotItemAsCatalogItemCommand` was reviewed and confirmed out of scope (D39), not overlooked.
