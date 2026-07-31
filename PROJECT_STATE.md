# PROJECT_STATE.md — Where RenoTrack Actually Stands

**Last updated:** 2026-07-31 — **Phase 3 is complete and merged to `main`** (PR #6, merge commit `85df430`; handoff docs followed in PR #7, `babfff9`). All 15 Phase 3 slices, the post-review Should-Fix fixes, the CI workflow split (D56), and the `IdentityRoleSeeder` redesign (D55) are all on `main`. Phase 2 merged to `main` earlier (PR #5, merge commit `dc85de1`). **Phase 4 is now in progress** on `feature/phase-4-api-auth-leads-inspections` — Slice 1 of 11 done; see `PHASE4_PROGRESS.md`.
**Purpose:** A precise, current snapshot — not a summary of history (see `PHASE2_PROGRESS.md` and `ARCHITECTURE_DECISIONS.md` for that). If a fact here conflicts with something you infer from reading old chat history, **this file and the actual code are authoritative.**

---

## 1. Current Phase

**Phase 4 — API layer**, per `PROJECT_ROADMAP.md`. **In progress** (Slice 1 of 11 done). Phases 0–3 are all complete and merged to `main`.

- Phase 0 (Solution bootstrap) — ✅ merged to `main`.
- Phase 1 (Domain core: Lead, Inspection, Angebot) — ✅ merged to `main`.
- Phase 1b (Domain: CatalogItem) — ✅ merged to `main`.
- **Phase 2 (Application layer) — ✅ merged to `main`** (PR #5, merge commit `dc85de1`; 15 vertical slices + documentation commits, branch `feature/phase-2-application-layer`). `CatalogItem`'s Application layer (Slices 11–14) was a justified in-scope insertion, needed by `AddAngebotItemCommand`. `SaveAngebotItemAsCatalogItemCommand` reviewed and confirmed out of scope (`ARCHITECTURE_DECISIONS.md` D39) — not a gap, a deliberate exclusion, to be revisited in Phase 3+.
- **Phase 3 (Infrastructure) — ✅ complete and merged to `main`** (PR #6, merge commit `85df430`, branch `feature/phase-3-infrastructure-efcore`). All 15 slices done (`RenoTrackDbContext` + entity configurations + `RenoTrack.Infrastructure.Tests`; `InitialCreate`/`AddAuditLog`/`AddNumberSequence`/`AddIdentity` migrations; `UnitOfWork`; `ILeadRepository`; `IInspectionRepository`; `IAngebotRepository`; `IAngebotReviewCommentRepository`; `ICatalogItemRepository`; `ICatalogItemQueries`; `IAuditService`; `INumberGeneratorService`; `IFileStorage` placeholder; `IEmailSender` placeholder; `AddInfrastructure()` + `Program.cs` wiring; Identity storage + role seeding). A pre-merge code review found three Should-Fix issues, all fixed (`c085058`). A real concurrency bug in `IdentityRoleSeeder`, found during final CI verification (not by the original review), was root-caused and fixed with a genuine design change — `IdentityRoleSeeder` became a dedicated DI service (D55) — rather than patched around. CI was split into a Linux job (build + non-Infrastructure tests) and a Windows job (Infrastructure tests against real LocalDB) to fix an environmental CI failure without weakening D40 (D56). See `PHASE3_PROGRESS.md` and §11 below for the full closeout record.

- **Phase 4 (API layer) — 🚧 in progress**, branch `feature/phase-4-api-auth-leads-inspections` (off `babfff9`). Scope confirmed against `PROJECT_ROADMAP.md`'s own Phase 4 entry (narrower than "the whole API layer"): API foundation, JWT authentication, Lead endpoints, Inspection endpoints, global exception handling, `LocalDiskFileStorage`, `AddApplication()` DI. Angebot/Catalog (Phase 5), token links (Phase 6), Projects (Phase 7), Invoices (Phase 8) are explicitly out of scope. **Slice 1 of 11 complete** (API foundation, conventions & docs — D57, D58). Full slice list and log in `PHASE4_PROGRESS.md`.

**Immediate next step: Phase 4 Slice 2 (global exception-handling middleware).** See §9.

## 2. Current Branch State

- **`feature/phase-4-api-auth-leads-inspections` is the current branch**, created off `origin/main` at `babfff9` (PR #7, the Phase 3 handoff-docs merge — note this is *later* than the `85df430` named throughout `HANDOFF_PROMPT.md`, which was written before PR #7 merged).
- `main` is at `babfff9`. `feature/phase-3-infrastructure-efcore` (final commit `f5d3108`) and `feature/phase-2-application-layer` are both merged and no longer active.
- **Next git action when resuming:** continue Phase 4 on the existing branch, one slice per commit, PR opened when the phase reaches a natural milestone — per `CLAUDE.md` §19, no direct commits to `main`, no force-push ever.

## 3. Build & Test Status (verify this yourself before trusting it — it may be stale)

As of the last verified run in this conversation, on `feature/phase-4-api-auth-leads-inspections` after Phase 4 Slice 1:
- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **375 tests passing, 0 failing.**
  - `RenoTrack.Domain.Tests`: **153 tests.**
  - `RenoTrack.Application.Tests`: **144 tests.**
  - `RenoTrack.Infrastructure.Tests`: **74 tests** (real SQL Server LocalDB integration tests — new in Phase 3; `PlaceholderFileStorageTests`/`LoggingNoOpEmailSenderTests`/`DependencyInjectionTests` are exceptions with no database connection actually opened; the Identity tests do use real LocalDB).
  - `RenoTrack.Api.Tests`: **4 tests** (new in Phase 4 Slice 1 — real `WebApplicationFactory<Program>` against real LocalDB, schema via `MigrateAsync`, D58).
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

## 6. Infrastructure Layer — Complete Inventory (Phase 3, complete)

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
- **`AddInfrastructure()` + `Program.cs` wiring — ✅ done (Slice 14).** `src/RenoTrack.Infrastructure/DependencyInjection.cs` registers `RenoTrackDbContext` (Scoped, from `ConnectionStrings:RenoTrackDb`) and all 11 repository/query/service interfaces above, every one Scoped. Deliberately excludes `IOwnershipValidator` (Application-layer implementation, CLAUDE.md §9) and all Application-layer DI (validators, command handlers) — out of scope, belongs to a future `AddApplication()` extension. `Program.cs` calls `AddInfrastructure(builder.Configuration)`.
- **Identity storage + role seeding — ✅ done (Slice 15, the last Phase 3 slice).** `ApplicationUser : IdentityUser<int>` (`src/RenoTrack.Infrastructure/Identity/ApplicationUser.cs`, Infrastructure-only per D53) adds `Name`/`IsActive`/`CreatedAt`. `RenoTrackDbContext` now inherits `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`. `AddIdentityCore` (not `AddIdentity`, D54) registered inside the existing `AddInfrastructure()`. `IdentityRoleSeeder` seeds `Admin`/`Inspector` only, idempotently and safely under concurrent startup (D54's race mitigation, proven by a 10-concurrent-instance test). The five deferred user-referencing FKs from D44 are now real constraints (`Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, `AngebotReviewComment.AdminUserId`), all `Restrict`. No authentication/JWT wiring — storage only, per the standing Phase 3 scope.
- **Every Application interface now has exactly one Infrastructure implementation, and every planned Phase 3 slice is done.**

### 6.5 Infrastructure Test Coverage (74 tests, `RenoTrack.Infrastructure.Tests`)

Real SQL Server LocalDB integration tests, never the EF Core InMemory provider (`ARCHITECTURE_DECISIONS.md` D40). `RenoTrackDbContextFixture` (`IAsyncLifetime` + `ICollectionFixture<T>`) creates/drops one shared LocalDB database (`RenoTrackInfrastructureTests`) per test run; every test class in the shared `"Infrastructure Database"` collection also seeds a real `Lead` row (via a `SeedLeadAsync` helper) before referencing its id, rather than a hardcoded placeholder — needed once real FKs made a coincidental id-match insufficient. Test classes: `LeadPersistenceTests`, `InspectionPersistenceTests`, `AngebotPersistenceTests`, `CatalogItemPersistenceTests`, `AngebotReviewCommentPersistenceTests` (15 tests total, including the 3 FK-rejection tests added in Slice 2, `EnsureCreated`-based schema), `UnitOfWorkTests` (3 tests), `InitialCreateMigrationTests` (2 tests, its own throwaway database, exercises `Database.MigrateAsync()` and `HasPendingModelChanges()` directly), `LeadRepositoryTests` (4 tests, Slice 4), `InspectionRepositoryTests` (5 tests, Slice 5 — `GetByIdAsync` eagerly loads `Photos`; a photo added post-load persists via `SaveChangesAsync` alone), `AngebotRepositoryTests` (9 tests, Slice 6 — `GetByIdAsync`'s two-level `Include`/`ThenInclude`, a section+item added post-load persisting via `SaveChangesAsync` alone, and `HasActiveAngebotForLeadAsync`'s non-terminal-status semantics driven directly via EF's change tracker since `Angebot.Status`'s only reachable terminal states through Domain methods require a `Sent` precondition), `AngebotReviewCommentRepositoryTests` (3 tests, Slice 7 — `AddAsync`-only contract), `CatalogItemRepositoryTests` (5 tests, Slice 8 — same `AddAsync`/`GetByIdAsync` shape as `LeadRepositoryTests`, plus a BR-14/D38 confirmation that `GetByIdAsync` still returns a retired item), `CatalogItemQueriesTests` (3 tests, Slice 9 — proves the DTO projection is genuinely SQL-translatable and that `IsRetired` is excluded), `AuditServiceTests` (4 tests, Slice 10 — proves `LogAsync` commits independently of `IUnitOfWork`, and that a real underlying write failure is caught and swallowed per the Best-Effort Audit strategy, D50), `NumberGeneratorServiceTests` (4 tests, Slice 11 — including a 50-parallel-caller concurrency test proving no duplicate numbers under real concurrent load against LocalDB), all under `tests/RenoTrack.Infrastructure.Tests/Persistence/`, `PlaceholderFileStorageTests` (1 test, Slice 12, under `tests/RenoTrack.Infrastructure.Tests/FileStorage/` — no database involved), `LoggingNoOpEmailSenderTests` (3 tests, Slice 13, under `tests/RenoTrack.Infrastructure.Tests/Email/` — no database involved, uses a capturing `ILogger` fake to verify the Warning log is actually emitted), `DependencyInjectionTests` (4 tests, Slice 14, at the project root — builds the real DI container with `ValidateOnBuild`/`ValidateScopes` and resolves every registered service; no database connection is ever actually opened, only a `DbContext` object constructed), plus (Slice 15, `tests/RenoTrack.Infrastructure.Tests/Identity/`) `IdentityRoleSeederTests` (3 tests, including a 10-concurrent-instance race proof) and `ApplicationUserTests` (1 test, password-hasher sanity check) — both against real LocalDB via a DI-built `UserManager`/`RoleManager`.

---

## 7. Documentation State

All eight original spec documents live in the repo root and have been actively maintained (not just written once in Phase 0):

| Document | Modified during Phase 1/2? | What changed |
|---|---|---|
| `SRS.md` | No | Unmodified since Phase 0 |
| `Architecture.md` | **Yes, extensively** | §6.1/§6.2 (Domain design decisions), §7.3 (role vs. ownership — new), §9 (stable external resource identifiers — new), §11 (audit-target principle — new). Phase 3: §3's solution structure updated to add `RenoTrack.Infrastructure.Tests` |
| `ERD.md` | **Yes** | `CatalogItem.IsRetired` column added (BR-12). Phase 3: `Subtotal`/`LineTotal`/`DecisionResult` removed to match confirmed Domain state (D41); `NumberSequences`' transaction-boundary wording corrected (D52); `USER`/`ROLE` corrected from a simplified single-table sketch to the real `AspNetUsers`/`AspNetRoles`/`AspNetUserRoles` Identity schema, and all five deferred user-referencing FK notes updated from "deferred" to "resolved" (D44/D53) |
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
8. **Infrastructure project — ✅ complete (all 15 slices done).** `RenoTrackDbContext` + entity configurations + `InitialCreate` migration + `UnitOfWork` + all 6 repositories/queries + `IAuditService` + `INumberGeneratorService` + `IFileStorage`/`IEmailSender` placeholders + `AddInfrastructure()` DI wiring + Identity storage all exist and are tested against real LocalDB. Every Application interface listed in §5.2 (except the deliberately-Application-side `IOwnershipValidator`, CLAUDE.md §9) now has exactly one Infrastructure implementation. See §6.4 and `PHASE3_PROGRESS.md`.
9. **`INumberGeneratorService`'s atomic-uniqueness requirement (Architecture §8) is now verified** — the single highest-risk assumption carried since Phase 2 (D34), resolved and proven by a 50-parallel-caller concurrency integration test in Slice 11 (D52).
10. **`LocalDiskFileStorage`/real `IEmailSender` — still deliberately deferred**, not gaps: `LocalDiskFileStorage` is Phase 4's (confirmed against `PROJECT_ROADMAP.md`, `CLAUDE.md` §13 corrected — D42); `IEmailSender`'s real SMTP-backed implementation is Phase 9's (`CLAUDE.md` §11). Phase 3 registered placeholder-only implementations of both (Slices 12–13, `PlaceholderFileStorage` throws loudly, `LoggingNoOpEmailSender` logs-and-continues per each interface's own documented intent) purely so DI composition succeeds until then.
11. **User-referencing FK constraints** (`Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, `AngebotReviewComment.AdminUserId`) — deferred until the Identity slice (`ARCHITECTURE_DECISIONS.md` D44), and now resolved: all five have real `Restrict` FK constraints as of Slice 15 (D53/D54).

---

## 9. Immediate Next Step

**Phase 4 is in progress — Slice 1 of 11 is done.** The immediate next step is **Slice 2: the global exception-handling middleware** (RFC 7807 ProblemDetails, `Architecture.md` §5.3), which also resolves the long-deferred question of how Domain's own `ArgumentException`/`InvalidOperationException` map to HTTP status codes. See `PHASE4_PROGRESS.md` for the full slice list and the Slice 1 record. The remainder of this section is the original Phase 4 framing, kept for context.

**Phase 3 was complete and merged to `main` (PR #6, `85df430`).** The next step after it was **Phase 4 — the API layer** (per `PROJECT_ROADMAP.md`): controllers, the `AddApplication()` DI extension (`IOwnershipValidator`, FluentValidation validators, command handlers — none of these are wired into DI yet, since `RenoTrack.Api` has had no controllers to need them), authentication/JWT issuance (Architecture.md §7.1 — Phase 3 built Identity storage only, no `[Authorize]` attributes or login endpoints exist), HTTP status-code mapping for Domain exceptions (RFC 7807 ProblemDetails, Architecture.md §5.3), and the real `LocalDiskFileStorage` (D42). A design review and explicit user sign-off is expected before any Phase 4 code is written, exactly as every prior phase/slice was handled. §10 and §11 below remain the historical closeout records for Phase 2 and Phase 3 respectively.

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

---

## 11. Phase 3 Closeout Review

Performed 2026-07-30, immediately after Slice 15 (Identity), before opening a PR. Every finding below was checked directly (build/test run, migration CLI, file inspection), not assumed from memory. **§11.7 below records what happened after this review** — the code review's Should-Fix fixes, the CI environmental fix, the `IdentityRoleSeeder` redesign, and the actual merge — since none of that had happened yet when §11.1–§11.6 were written.

### 11.1 Documentation Audit

- **`PROJECT_STATE.md`/`NEXT_STEPS.md`/`PHASE3_PROGRESS.md`** cross-checked for consistency: all three agree all 15 slices are done, all three cite the same decision numbers (D40–D54) for the same facts, none references a "next slice" that's already been built.
- **`ARCHITECTURE_DECISIONS.md`**: D40 through D54 all present, in order, no gaps (`grep` confirms 15 consecutive headers). Each of D49/D51/D53 (Infrastructure-only placement) and D50/D54 (best-effort/race-tolerant behavior) explicitly references its precedent rather than re-deriving the reasoning from scratch.
- **`ERD.md`** corrected three times during Phase 3 (D41 — computed properties; D52 — `NumberSequences`' transaction-boundary wording; D53 — `USER`/`ROLE` real Identity schema) — each correction made in the same commit as the code it describes, per `CLAUDE.md` §15's documentation-first discipline, not batched up for this closeout.
- **`Architecture.md`** §8 corrected (D52) to describe the actual atomic-statement number-generation design rather than literal same-transaction wording that the already-built handler ordering made impossible.
- **`CLAUDE.md`** §13 remains correct from its Slice 1 correction (D42 — `LocalDiskFileStorage` is Phase 4's); no further correction needed during Slices 2–15.
- **No stale cross-references found** — spot-checked every `ARCHITECTURE_DECISIONS.md` reference added in Slices 10–15 against the actual decision numbers; all resolve correctly.

### 11.2 Architecture Decision Audit

15 decisions recorded in Phase 3 (D40–D54), grouped by theme:

| Theme | Decisions |
|---|---|
| Test infrastructure / migration process | D40 (new test project), D45/D46 (schema-review + migration-review catching two real bugs), D47 (design-time factory) |
| Documentation corrections (ERD/CLAUDE.md corrected to match confirmed reality) | D41, D42 |
| EF Core mapping techniques | D43 (backing-field navigation) |
| Deferred-until-Identity FKs | D44 (deferred), resolved in Slice 15 |
| `IUnitOfWork` scope | D48 (confirmed thin) |
| Infrastructure-only persistence models (forced or chosen) | D49 (`AuditLog`, judgment call), D51 (`NumberSequence`, judgment call), D53 (`ApplicationUser`, forced by D1) |
| Best-effort / race-tolerant behavior patterns | D50 (audit), D52 (number generation), D54 (role seeding) — the same "catch, re-verify, treat as benign" shape reused three times for three genuinely different problems, not copy-pasted blindly each time |
| DI/composition choices | D54 (`AddIdentityCore` vs `AddIdentity`) |

Every decision that changed already-written documentation (D41, D42, D52, D53) did so in the same commit as the code change — verified by checking each commit's diff includes both.

### 11.3 Migration Audit

Four migrations, applied in this order (verified via `dotnet ef migrations list`, all currently `(Pending)` — none applied to any shared/persistent database, consistent with every prior status note in this file):

1. `InitialCreate` (Slice 1–2) — the 8 core business tables.
2. `AddAuditLog` (Slice 10) — `AuditLogs`, no FK (deliberately, D50's "no cross-entity linkage").
3. `AddNumberSequence` (Slice 11) — `NumberSequences`, no FK.
4. `AddIdentity` (Slice 15) — 7 `AspNetX` tables, plus 5 retroactive FKs on `Leads`/`Inspections`/`Angebote`(×2)/`AngebotReviewComments`.

**`dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration."** — confirmed directly, not assumed: the current C# model and the migration history are in sync. Every migration was manually reviewed at the time it was generated (Slice 2 caught two real bugs this way — D45/D46 — proving the review step itself has caught real issues, not just theoretical ones).

### 11.4 DI Audit

`AddInfrastructure()` registers all 11 Infrastructure-implemented Application interfaces — `ILeadRepository`, `IInspectionRepository`, `IAngebotRepository`, `IAngebotReviewCommentRepository`, `ICatalogItemRepository`, `ICatalogItemQueries`, `IUnitOfWork`, `IAuditService`, `INumberGeneratorService`, `IFileStorage`, `IEmailSender` — every one Scoped, verified by direct comparison against every `public interface I...` declared in `Common/Interfaces/` and `CatalogItems/ICatalogItemQueries.cs` (12 found; 11 registered; the 12th, `IOwnershipValidator`, correctly excluded per CLAUDE.md §9 — its implementation lives in `RenoTrack.Application`, not here). `RenoTrackDbContext` registered via `AddDbContext` (Scoped default); `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<int>>().AddEntityFrameworkStores<RenoTrackDbContext>()` added alongside with no new composition root. `DependencyInjectionTests` proves this container actually builds with `ValidateOnBuild`/`ValidateScopes` enabled (would fail if any Singleton captured the Scoped `DbContext`) and resolves every registration inside a real scope.

### 11.5 Test Summary

**`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors.** **`dotnet test RenoTrack.slnx` → 371 tests passing, 0 failing:**

| Project | Tests |
|---|---|
| `RenoTrack.Domain.Tests` | 153 |
| `RenoTrack.Application.Tests` | 144 |
| `RenoTrack.Infrastructure.Tests` | 74 |
| `RenoTrack.Api.Tests` | 0 (Phase 4 not started) |
| **Total** | **371** |

`RenoTrack.Infrastructure.Tests`' 74 break down as: 15 Slice-1 persistence tests, 5 migration/schema tests (Slice 2), 3 `UnitOfWork` tests, 4 `LeadRepository`, 5 `InspectionRepository`, 9 `AngebotRepository`, 3 `AngebotReviewCommentRepository`, 5 `CatalogItemRepository`, 3 `CatalogItemQueries`, 4 `AuditService`, 4 `NumberGeneratorService` (including the 50-parallel-caller concurrency proof), 1 `PlaceholderFileStorage`, 3 `LoggingNoOpEmailSender`, 4 `DependencyInjection`, 4 `Identity` (including the 10-concurrent-instance role-seeding race proof) — plus the net effect of Slice 15's real-FK retrofit, which required fixing (not just adding) 19 previously-passing tests across 7 files. Every test class runs against real SQL Server LocalDB except the four classes with genuinely no database concern (`PlaceholderFileStorageTests`, `LoggingNoOpEmailSenderTests`, `DependencyInjectionTests`'s container-build check, and the password-hasher sanity check) — the standing decision (D40) that `RenoTrack.Infrastructure.Tests` never uses the EF Core InMemory provider held for all 15 slices.

### 11.6 Merge Readiness Report

- **Branch:** `feature/phase-3-infrastructure-efcore`, 17 commits ahead of `main`, working tree clean, `origin/main` and local `main` match exactly (`git fetch` confirmed no drift) — safe to open a PR against current `main`.
- **Build/test:** clean, as above.
- **Scope:** every item in Phase 3's approved 15-slice dependency map is done — no partial slices, no "configure this later" left anywhere.
- **Known, deliberate deferrals carried past this phase (not gaps):** real `LocalDiskFileStorage` (Phase 4, D42); real SMTP `IEmailSender` (Phase 9, `CLAUDE.md` §11); `AddApplication()` DI extension for `IOwnershipValidator`/validators/command handlers (Phase 4, since `RenoTrack.Api` has no controllers yet to need them); HTTP status-code mapping for Domain exceptions (Phase 4); authentication/JWT issuance (Phase 4, Architecture.md §7.1) — Slice 15 deliberately stopped at storage.
- **Recommended PR title:** `Phase 3: Infrastructure layer — EF Core persistence, repositories, and Identity storage` (matches the `Phase N: <layer> — <one-line scope>` pattern Phase 2's PR title established).
- **Recommended commit range:** `main..feature/phase-3-infrastructure-efcore`, `1edccae` (Slice 1) through `b6d4d48` (Slice 15) — 15 feature commits, one docs-sync commit (`fca7eb8`, mid-phase).
- **PR description should note explicitly:** the retroactive-FK test breakage in Slice 15 (19 tests, expected and fixed, not a late-discovered bug); the three narrow, explicitly-scoped exceptions to "EF Core only" (raw SQL in `NumberGeneratorService`, D52) and "no custom auth cookies" (`AddIdentityCore`, D54); that `INumberGeneratorService`'s concurrency guarantee (the single highest-risk item flagged since Phase 2, D34) is now proven, not just implemented.
- **Verdict at the time this section was written: ready to open as a PR**, pending the user's own final read-through and explicit go-ahead to push/open. (Superseded — see §11.7: additional work happened between opening the PR and merging it.)

### 11.7 What Happened Between This Review and the Actual Merge

This section records everything that happened *after* §11.1–§11.6 were written — a separate code-review pass, an environmental CI fix, and a real bug found and fixed with a genuine design change — none of which existed yet when the verdict above was recorded.

**Code review (lead-reviewer pass, role-reversed: reviewer only, no implementation until findings were presented):** three Should-Fix findings, no Must-Fix findings:
1. `HANDOFF_PROMPT.md` described the PR as "not yet opened" — stale by the time of review.
2. `tests/RenoTrack.Infrastructure.Tests.csproj` had no explicit `<ProjectReference>` to `RenoTrack.Application` — it compiled only via implicit transitive resolution through `RenoTrack.Infrastructure`, which this project's own layering discipline treats as fragile (explicit references only, `CLAUDE.md` §1).
3. `tests/RenoTrack.Infrastructure.Tests/Identity/IdentityTestServices.cs` duplicated `AddInfrastructure()`'s DI registrations by hand instead of calling the real extension method — a maintenance hazard (two places to keep in sync).

All three fixed in commit `c085058`, followed by a full rebuild/retest/`has-pending-model-changes` re-verification before pushing.

**CI environmental failure, fixed without touching tests or replacing LocalDB (D56):** the first CI run failed because the single Linux job couldn't run `RenoTrack.Infrastructure.Tests` (LocalDB is Windows-only, D40). `.github/workflows/ci.yml` was split into `build-and-test` (`ubuntu-latest`) and `infrastructure-tests` (`windows-latest`, `needs: build-and-test`, starts `sqllocaldb start MSSQLLocalDB`). Verified via the GitHub Actions API: both jobs green.

**A real, previously-undetected concurrency bug found and fixed with a genuine design change (D55), not a patch:** re-running `IdentityRoleSeederTests`'s concurrency test repeatedly during local Release-config verification (not something the original review or a single CI run would have caught) surfaced a ~66% failure rate, caused by `RoleManager`/`DbContext` tracking state from a failed role-seed attempt bleeding into the next role's `SaveChangesAsync()` call. Rather than patch around it (a `DbContext` parameter, or reflection into `RoleManager.Store`), the design was reworked: `IdentityRoleSeeder` became a dedicated `AddScoped` DI service with `IServiceScopeFactory` injected via its constructor, creating one fresh `IServiceScope` per role internally, keeping the public `SeedRolesAsync()` parameterless. Verified empirically: 32 consecutive runs (22 Debug, 10 Release) all passed after the fix, versus the prior ~2-in-3 failure rate. Full alternatives-considered record in `ARCHITECTURE_DECISIONS.md` D55.

**Final push and merge:** the Should-Fix fixes, the CI split, and the `IdentityRoleSeeder` fix were pushed together (`f5d3108`, once everything was consistently green), the PR was marked ready for review, and **PR #6 was merged into `main` via merge commit `85df430`** on 2026-07-31. `feature/phase-3-infrastructure-efcore` remains on the remote (merged, not deleted) but is no longer the active branch.

**Post-merge verification on `main` itself** (not just on the feature branch before merge): `dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors; `dotnet test RenoTrack.slnx` → 371 passing, 0 failing (153 Domain + 144 Application + 74 Infrastructure); `dotnet ef migrations has-pending-model-changes` → no pending changes. All three checked directly against the merged `main`, not carried over from the pre-merge branch state.

**Final verdict: Phase 3 is done. `main` is green, clean, and ready for Phase 4 to begin.**
