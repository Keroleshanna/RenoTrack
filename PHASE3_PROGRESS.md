# PHASE3_PROGRESS.md — Vertical Slice Log

**Purpose:** a detailed record of every vertical slice completed in Phase 3 (Infrastructure) so far, in the order built. Each entry follows the same format Phase 2 established: Goal, Design Decisions & Architectural Discussion, New Abstractions Introduced, Documentation Updates, Tests Added, Final Outcome.

All work in this log lives on branch `feature/phase-3-infrastructure-efcore`, not yet merged or pushed as of this writing.

**Slice order** (per the dependency map reviewed and approved before any code was written): DbContext + configurations → `InitialCreate` migration → `IUnitOfWork` → `ILeadRepository` → `IInspectionRepository` → `IAngebotRepository` → `IAngebotReviewCommentRepository` → `ICatalogItemRepository` → `ICatalogItemQueries` → `IAuditService` → `INumberGeneratorService` (+ concurrency test) → `IFileStorage` placeholder → `IEmailSender` placeholder → `AddInfrastructure()` + `Program.cs` wiring → Identity storage + role seeding. Identity was deliberately moved to the very end, after DI composition, so repository work stays completely independent of it.

---

## Slice 1 — `RenoTrackDbContext` + Entity Configurations + `RenoTrack.Infrastructure.Tests`

**Goal:** The foundation every later Infrastructure slice builds on — a working `DbContext` with Fluent configurations for every Domain entity that exists today, proven correct against a real database, not just compiling.

**Design decisions & architectural discussion (full design review before any code, per the user's request):**
- **Two documentation contradictions resolved before implementation, both requiring the user's explicit confirmation:**
  - **`ERD.md` corrected to match confirmed Domain state** (`ARCHITECTURE_DECISIONS.md` D41): `AngebotSection.Subtotal`, `AngebotItem.LineTotal` (pure computed properties, no backing field) and `Angebot.DecisionResult` (removed from the Domain entirely per D16) were all still listed in `ERD.md` as physical columns. Verified directly against the live C# source before concluding this was stale documentation, not a schema requirement. All three are now `.Ignore()`d in their entity configurations, and `ERD.md`'s diagram and Physical Schema Notes were corrected in this same commit.
  - **`LocalDiskFileStorage` reassigned to Phase 4** (`ARCHITECTURE_DECISIONS.md` D42): `CLAUDE.md` §13 said "(Phase 3)"; `PROJECT_ROADMAP.md`'s Phase 4 deliverable list explicitly owns it, Phase 3's list doesn't mention it. `PROJECT_ROADMAP.md` treated as authoritative (same reasoning as D39). `CLAUDE.md` §13 corrected; the real disk implementation is deferred to Slice 12's placeholder, then Phase 4 for real.
- **New `RenoTrack.Infrastructure.Tests` project** (`ARCHITECTURE_DECISIONS.md` D40) — a real, user-approved deviation from `Architecture.md`'s originally-documented three-test-project structure, since none of the existing three can exercise real EF Core/repository behavior. Runs against real SQL Server LocalDB, never the EF Core InMemory provider — InMemory doesn't enforce the unique constraints, FKs, or `decimal(18,2)` precision this layer exists to verify. All tests share one LocalDB database via an `ICollectionFixture`, forcing xUnit to run them serially against it.
- **DbContext exposes a `DbSet<T>` per aggregate root only** — no `DbSet<AngebotSection>`/`DbSet<AngebotItem>`/`DbSet<InspectionPhoto>`, extending `CLAUDE.md` §2's "aggregate roots are the only public entry point" rule into how the persistence layer is queried.
- **Only entities that exist in the Domain today get a `DbSet`/configuration** — `Lead`, `Inspection`, `InspectionPhoto`, `Angebot`, `AngebotSection`, `AngebotItem`, `AngebotReviewComment`, `CatalogItem`. No `NumberSequence`/`AuditLog` yet (their own later slices), no Identity tables yet (Slice 15), and deliberately nothing for `Customer`/`Project`/`Invoice`/`InvoiceLine`/`Payment`/`TokenLink` — no Domain or Application code references any of them yet, so creating their schema now would be exactly the kind of speculative, ahead-of-need work `CLAUDE.md` §4 rejects everywhere else.
- **Value converters:** `Money` ↔ `decimal(18,2)` via `Money.FromExact`/`.Amount`; `ItemUnit` ↔ `nvarchar` via the existing `Code`/`FromCode` round-trip surface (`ItemUnit`'s own Domain doc comment already anticipated this exact mapping). `VatRate` uses EF's default enum-to-int mapping — no converter needed, since its underlying values (`0/7/16/19`) already are the percentages.
- **Encapsulated child collections mapped via backing-field navigation** (`ARCHITECTURE_DECISIONS.md` D43) — `Inspection.Photos`, `Angebot.Sections`, `AngebotSection.Items` are `IReadOnlyList<T>` over private `List<T>` fields with no public setter; each navigation is explicitly configured with `UsePropertyAccessMode(PropertyAccessMode.Field)` rather than relying on EF's implicit backing-field discovery convention. Proven with real round-trip integration tests, not assumed to work.
- **User-referencing columns deferred to the Identity slice** (`ARCHITECTURE_DECISIONS.md` D44) — `Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, `AngebotReviewComment.AdminUserId` are plain `int`/`int?` columns with no FK constraint yet, since the `Users` table doesn't exist until Slice 15 (Identity was deliberately moved to the end of the slice order). `AngebotItem.CatalogItemId` and `CatalogItem.CreatedFromAngebotItemId`, by contrast, **do** get real FK constraints now — both `CatalogItems` and `AngebotItems` tables already exist, so nothing blocks enforcing that traceability link at the database level (BR-8's "no live reference" is a Domain-behavior concern, not a reason to skip DB-level referential integrity).
- **No generic `Repository<TEntity>` base class** — reaffirmed for the repositories still to come, matching the project's consistent anti-generic-abstraction stance (D28).

**New abstractions introduced:** `RenoTrackDbContext`, `MoneyConverter`, `ItemUnitConverter`, one `IEntityTypeConfiguration<T>` per entity (`LeadConfiguration`, `InspectionConfiguration`, `InspectionPhotoConfiguration`, `AngebotConfiguration`, `AngebotSectionConfiguration`, `AngebotItemConfiguration`, `CatalogItemConfiguration`, `AngebotReviewCommentConfiguration`), the `RenoTrack.Infrastructure.Tests` project itself.

**Documentation updates:** `ERD.md` corrected (D41); `CLAUDE.md` §13 corrected (D42) and §1/§14 updated to describe the new test project; `Architecture.md` §3's solution structure updated to list `RenoTrack.Infrastructure.Tests` and explain why it's a deliberate addition; `ARCHITECTURE_DECISIONS.md` gained D40–D44.

**Tests added:** 12 (`RenoTrack.Infrastructure.Tests`, all against real LocalDB) — `LeadPersistenceTests` (round-trip + status/inspector-assignment persistence), `InspectionPersistenceTests` (round-trip with a photo through the backing-field navigation, completion timestamp persistence), `AngebotPersistenceTests` (full `Angebot → Section → Item` tree round-trip, computed-property correctness post-reload, `NetTotal`/`GrossTotal` persistence, `AngebotNumber` uniqueness constraint enforcement, `AngebotItem.CatalogItemId` FK enforcement against a non-existent id), `CatalogItemPersistenceTests` (round-trip, retirement persistence), `AngebotReviewCommentPersistenceTests` (round-trip, `AngebotId` FK enforcement).

**Final outcome:** 12 Infrastructure tests, alongside the existing 153 Domain + 144 Application → **309 solution-wide.** Build clean (0 warnings, 0 errors). All 12 integration tests passed against real LocalDB on the first run. Committed.

---

## Slice 2 — `InitialCreate` Migration

**Goal:** Generate the first real EF Core migration from Slice 1's `RenoTrackDbContext`, without applying it to a database yet — but only after a deliberate, explicit re-verification that the Domain model, the EF configurations, and `ERD.md` were all still in agreement.

**Design decisions & architectural discussion:**
- **Pre-migration three-way schema review (per the user's explicit request), which found three real, non-deliberate gaps** (`ARCHITECTURE_DECISIONS.md` D45): `Inspection.LeadId`, `Angebot.LeadId`, and `Angebot.InspectionId` had no FK configured at all — an oversight from Slice 1, not a deferral (unlike the Identity-referencing columns, `Leads`/`Inspections` both exist today). Fixed by adding `HasOne<T>().WithMany().HasForeignKey(...).OnDelete(DeleteBehavior.Restrict)` for all three. Three existing integration tests were also found to be passing only by accident — they used a hardcoded `leadId: 1` without ever inserting a real `Lead` row, and only "worked" because another test class happened to create a real `Lead` with `Id == 1` first (unguaranteed xUnit test-class ordering). Fixed by seeding a real `Lead` in each test that needs one, and added three new FK-rejection tests proving the constraints are genuinely enforced.
- **Migration generated via a new `IDesignTimeDbContextFactory`** (`ARCHITECTURE_DECISIONS.md` D47), not by jumping ahead to Slice 14's DI composition — the standard EF Core pattern for exactly this situation. `Microsoft.EntityFrameworkCore.Design` added to `RenoTrack.Infrastructure` (dev-time only, `PrivateAssets="all"`) and `RenoTrack.Api` (the eventual startup project).
- **Manual review of the generated migration (per the user's explicit instruction) found a second real bug before the migration was ever applied anywhere** (`ARCHITECTURE_DECISIONS.md` D46): the shadow FK columns for every encapsulated child collection (`InspectionPhotos.InspectionId`, `AngebotSections.AngebotId`, `AngebotItems.SectionId`) were generated as `nullable: true` — wrong, since a photo/section/item always belongs to exactly one parent by Domain design. Root cause: `HasMany(...).WithOne()` with no back-navigation defaults EF Core to an *optional* relationship unless `.IsRequired()` is called explicitly. Fixed in all three configurations; the first (incorrect) migration was removed via `dotnet ef migrations remove` — it had never been applied to any database — and regenerated cleanly.
- **Post-fix manual review confirmed:** every operation in the regenerated migration was expected (8 `CreateTable`, 13 `CreateIndex`, 1 deferred `AddForeignKey` for the `AngebotItems`↔`CatalogItems` mutual reference — EF's migration generator resolved the circular dependency automatically, exactly as anticipated in the Phase 3 design review); no accidental cascades (only the three true parent-owns-child relationships cascade; every cross-aggregate FK is `Restrict`); no unnecessary columns (`Subtotal`/`LineTotal`/`DecisionResult` correctly absent); no unexpected tables (exactly the 8 entities that exist in the Domain today); no missing Domain concept.
- **Two new integration tests prove the migration itself, not just the model:** `InitialCreateMigration_AppliesCleanlyToAFreshDatabase` (runs `Database.MigrateAsync()` against a fresh LocalDB database and confirms the migration is recorded as applied) and `InitialCreateMigration_ProducesASchemaThatMatchesTheCurrentModel` (asserts `Database.HasPendingModelChanges()` is `false` — the same check EF's own tooling uses to detect migration/model drift).

**New abstractions introduced:** `RenoTrackDbContextFactory` (`IDesignTimeDbContextFactory<RenoTrackDbContext>`, design-time only), the `InitialCreate` migration itself, `InitialCreateMigrationTests`.

**Documentation updates:** `ARCHITECTURE_DECISIONS.md` gained D45–D47.

**Tests added:** 5 — 3 FK-rejection tests (`InspectionPersistenceTests.LeadIdForeignKey_RejectsANonExistentLead`, `AngebotPersistenceTests.LeadIdForeignKey_RejectsANonExistentLead`, `AngebotPersistenceTests.InspectionIdForeignKey_RejectsANonExistentInspection`) plus 2 migration tests (`InitialCreateMigrationTests`). Three existing tests were also strengthened (real `Lead` seeding instead of a coincidentally-matching hardcoded id) without changing their count.

**Final outcome:** 17 Infrastructure tests, alongside 153 Domain + 144 Application → **314 solution-wide.** Build clean (0 warnings, 0 errors). Migration not yet applied to any persistent/shared database — only exercised by the two migration-specific integration tests, each against its own throwaway LocalDB database, cleaned up via `IAsyncLifetime`. Committed.

---

## Slice 3 — `IUnitOfWork`

**Goal:** Implement the Infrastructure side of `IUnitOfWork`, following a short design review (per the user's explicit request) rather than assuming a thin wrapper was obviously correct.

**Design decisions & architectural discussion (full record: `ARCHITECTURE_DECISIONS.md` D48):**
- Confirmed no logic beyond `SaveChangesAsync()` is needed — every Phase 2 handler calls it exactly once; EF Core's own implicit per-`SaveChanges` transaction already covers everything a handler needs, with `INumberGeneratorService`'s (Slice 11) atomic requirement deliberately handled inside that service itself, not through `IUnitOfWork`'s contract.
- `DbContext` and every repository share one Scoped DI lifetime with `UnitOfWork` — the mechanism that makes "repository adds an entity → `UnitOfWork.SaveChangesAsync()` commits it" work at all.
- `UnitOfWork` does not implement `IDisposable` — it doesn't own the injected `DbContext`; disposal belongs to the DI container's scope.
- Cancellation token passes straight through with no additional handling.
- Interface confirmed intentionally minimal — same growth-on-demand discipline as every other repository/interface in this project (`CLAUDE.md` §4).

**New abstractions introduced:** `UnitOfWork : IUnitOfWork` (`src/RenoTrack.Infrastructure/Persistence/UnitOfWork.cs`) — a one-line wrapper over `RenoTrackDbContext.SaveChangesAsync`.

**Documentation updates:** `ARCHITECTURE_DECISIONS.md` gained D48.

**Tests added:** 3 (`UnitOfWorkTests`) — persists pending changes tracked by the same `DbContext`; no-op with nothing pending doesn't throw; an already-cancelled token throws `OperationCanceledException`. The cancellation test initially failed with no pending change at all — EF Core short-circuits `SaveChangesAsync()` when nothing is tracked, skipping the cancellation check entirely — fixed by giving the test a real pending change first, a small empirical finding about EF's own behavior rather than an assumption.

**Final outcome:** 20 Infrastructure tests, alongside 153 Domain + 144 Application → **317 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 4 — `ILeadRepository`

**Goal:** The first concrete repository implementation — `Lead` is the simplest aggregate in the model (no child entities, no navigation properties), making it the natural first case to establish the repository pattern the remaining six repositories will follow.

**Design decisions & architectural discussion (full design review before any code, per the user's request, including a follow-up review requested on one specific point):**
- **Every method verified against a real caller** before implementation: `AddAsync` → `CreateLeadCommandHandler`; `GetByIdAsync` → `ScheduleInspectionCommandHandler`, `CompleteInspectionCommandHandler`, `CreateAngebotCommandHandler`. No speculative method added.
- **`GetByIdAsync` implemented via `DbSet<Lead>.FindAsync`** — a plain PK lookup, no `Include`/`ThenInclude`, since `Lead` has zero navigation properties (Architecture.md §6: Lead relates to every other aggregate by id only, in the reverse direction — other tables carry `LeadId FK`, not the other way around). Confirmed this isn't just true today but structurally permanent, since the "aggregates relate by id, never navigation" rule (`CLAUDE.md` §2) applies project-wide, not just to Lead's current simplicity.
- **`FindAsync` explicitly re-reviewed on request** for compatibility with the wider repository contract, checking three specific risks before accepting it:
  1. No future Lead navigation properties are expected (confirmed above — a permanent architectural constraint, not a temporary absence).
  2. No global EF Core query filter exists anywhere in the codebase (confirmed via direct search — no `HasQueryFilter` call anywhere), and none is expected for `Lead` specifically, since Leads are never deleted/retired (`CLAUDE.md` §2 — no soft-delete-shaped flag exists to filter on).
  3. No handler depends on LINQ-only query behavior `FindAsync` would bypass — the opposite was found to be true: `FindAsync`'s default **tracked** result is actually load-bearing. `CompleteInspectionCommandHandler` loads a `Lead`, mutates it (`MarkInspectionDone()`), and relies entirely on `IUnitOfWork.SaveChangesAsync()` to persist that change — there is no `UpdateAsync` anywhere in this project (`CLAUDE.md` §4), so change tracking is the *only* mechanism making that pattern work. An untracked (`AsNoTracking`) result would have silently broken every load-then-mutate handler.
- **`AddAsync` confirmed as pure persistence, no validation.** No uniqueness rule exists anywhere in `BusinessRules.md`/`ERD.md` for `Lead` (no unique phone/email constraint), and `Lead.Create(...)` already owns all of its own construction invariants — re-checking anything in the repository would duplicate a guard that already exists one layer down (`CLAUDE.md` §5).
- **`SaveChangesAsync` confirmed as exclusively `IUnitOfWork`'s responsibility** — `AddAsync`'s body only calls `DbSet<Lead>.AddAsync` (staging the entity in the change tracker), never `SaveChangesAsync`. Verified with a dedicated integration test proving nothing persists if `IUnitOfWork.SaveChangesAsync` is never called, not just asserted by code review.
- **Indexing reviewed and confirmed unnecessary for this slice.** The existing `(Status, AssignedInspectorId)` index (added Slice 1 for SRS FR-2.4 pipeline filtering) already covers the only non-PK query pattern documented anywhere, and isn't even exercised by `ILeadRepository`'s two PK-based methods. Adding a new index for a query that doesn't exist yet would be exactly the speculative growth `CLAUDE.md` §4 rejects.
- **No new architectural decision was made** — this slice is a straightforward, first application of conventions already settled in Slices 1–3 (repository growth-on-demand, thin persistence-only classes, no generic base class). Nothing new added to `ARCHITECTURE_DECISIONS.md`.

**New abstractions introduced:** `LeadRepository : ILeadRepository` (`src/RenoTrack.Infrastructure/Persistence/Repositories/LeadRepository.cs`) — the first repository, establishing a new `Persistence/Repositories/` folder that the remaining six repository slices will use. Two methods, `AddAsync`/`GetByIdAsync`, both trivial one-line bodies over `RenoTrackDbContext.Leads`.

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 4 (`LeadRepositoryTests`, real LocalDB, same `"Infrastructure Database"` collection as every other Infrastructure test) — distinct from Slice 1's `LeadPersistenceTests`, which proves raw `DbContext` field round-tripping, not repository-class behavior: `AddAsync_FollowedBySaveChangesAsync_PersistsTheLead`, `AddAsync_WithoutSaveChangesAsync_PersistsNothing` (the concrete proof that `SaveChangesAsync` stays exclusively `IUnitOfWork`'s job), `GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsThePersistedLead` (cross-`DbContext`-instance correctness), `GetByIdAsync_WhenLeadDoesNotExist_ReturnsNull`.

**Final outcome:** 24 Infrastructure tests, alongside 153 Domain + 144 Application → **321 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 5 — `IInspectionRepository`

**Goal:** Second repository. Focused review (per the user's standing instruction from Slice 4's approval) covering only what differs from `LeadRepository`, reusing every already-settled decision otherwise.

**What's different from `LeadRepository` (the only points reviewed):**
- **`Inspection` has one child collection, `Photos`.** `GetByIdAsync` must `.Include(i => i.Photos)` — not a new decision, a direct application of `CLAUDE.md` §4's already-settled "no partial-load contract" rule, which had no visible effect for `Lead` (no children) but does here. `FindAsync` doesn't support `Include`, so `GetByIdAsync` uses `FirstOrDefaultAsync(i => i.Id == id, ...)` instead.
- **Navigation mapping already proven.** `InspectionConfiguration`'s `Photos` backing-field binding (D43) and its `.Include`-based round-trip were both already verified in Slice 1's `InspectionPersistenceTests`. Nothing new to prove about the mapping itself.
- **Tracking behavior confirmed to still hold for a child-collection mutation, not just a scalar one.** `UploadInspectionPhotoCommandHandler` loads an `Inspection` via `GetByIdAsync`, calls `inspection.AddPhoto(...)`, and relies on `IUnitOfWork.SaveChangesAsync()` alone — proven with a dedicated test that a photo added to an already-loaded (tracked) aggregate is persisted with no repository-level "update" step, extending Slice 4's tracking-dependency finding to a collection mutation.
- **No performance concern** — Inspection's photo count is small (no documented volume concern), so a single eager `Include` is correct without any split-query/pagination consideration.
- **Repository contract still sufficient** — both existing interface methods already cover every real caller (`ScheduleInspectionCommandHandler` for `AddAsync`; `CompleteInspectionCommandHandler`/`UpdateInspectionNotesCommandHandler`/`UploadInspectionPhotoCommandHandler` for `GetByIdAsync`). No new method needed.

**No new architectural decision** — mechanical application of already-settled rules (CLAUDE.md §4, D43). Nothing added to `ARCHITECTURE_DECISIONS.md`.

**New abstractions introduced:** `InspectionRepository : IInspectionRepository` (`src/RenoTrack.Infrastructure/Persistence/Repositories/InspectionRepository.cs`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 5 (`InspectionRepositoryTests`) — `AddAsync_FollowedBySaveChangesAsync_PersistsTheInspection`, `AddAsync_WithoutSaveChangesAsync_PersistsNothing`, `GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsThePersistedInspectionWithPhotos` (also proves the `Include` loads `Photos`), `GetByIdAsync_WhenInspectionDoesNotExist_ReturnsNull`, and `AddingAPhotoToAnAggregateLoadedViaGetByIdAsync_IsPersistedBySaveChangesAsyncAlone` (the collection-mutation tracking proof specific to this slice).

**Final outcome:** 29 Infrastructure tests, alongside 153 Domain + 144 Application → **326 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 6 — `IAngebotRepository`

**Goal:** First repository for a complex, two-level aggregate (`Angebot` → `Sections` → `Items`) and the first repository with a third method beyond `AddAsync`/`GetByIdAsync`. Focused review covering only what's new relative to Slices 4–5.

**What's new relative to `LeadRepository`/`InspectionRepository` (the only points reviewed):**
- **Two-level `Include` chain.** `GetByIdAsync` uses `.Include(a => a.Sections).ThenInclude(s => s.Items)`, extending Slice 5's single-level `Include` one level deeper. Same `PropertyAccessMode.Field` backing-field mapping as before (D43), already proven for both `AngebotSection`/`AngebotItem` in Slice 1's `AngebotPersistenceTests`.
- **Confirmed the full-aggregate contract still applies uniformly, not per-caller.** Checked every consumer: `SubmitAngebotForReviewCommandHandler` genuinely *needs* `Sections`/`Items` (`Angebot.SubmitForReview()`'s own guard inspects them directly — an unloaded tree would silently look like "no items" and produce a wrong result, not an error). `ApproveAngebotCommandHandler`/`RequestAngebotChangesCommandHandler` don't use the tree at all, but the repository contract (`IAngebotRepository.GetByIdAsync`'s own doc comment: "there is no partial load of an aggregate root in DDD") doesn't vary per caller — this was already settled, not reopened.
- **`HasActiveAngebotForLeadAsync` — new method shape, existence check rather than aggregate load.** Business rule (StateMachine.md §2.4, already stated in the interface doc comment): non-terminal = every `AngebotStatus` except `CustomerApproved`/`CustomerRejected`. Implemented as a single `AnyAsync` predicate on `LeadId`/`Status` only — no `Include`, doesn't touch `AngebotSections`/`AngebotItems` at all, uses the existing `Status` index (`AngebotConfiguration.cs:39`).
- **N+1 checked and ruled out** — the `Include`/`ThenInclude` chain and the `AnyAsync` call are each a single SQL query; no per-row follow-up querying anywhere in this design.
- **`AsSplitQuery()` reviewed and confirmed unnecessary** — it addresses cartesian-product row inflation from multiple *sibling* collections at the same level; `Sections`→`Items` is a single chain, not siblings, and the documented aggregate size (a handful of sections/items) gives no reason to split. Revisit only if a real, measured performance problem appears.
- **Contract confirmed still complete** — all three methods (`AddAsync`, `GetByIdAsync`, `HasActiveAngebotForLeadAsync`) map to real callers; no new method needed.

**No new architectural decision** — direct application of CLAUDE.md §4 (full-aggregate contract), D43 (backing-field mapping), and the project's no-premature-optimization stance to a two-level tree. Nothing added to `ARCHITECTURE_DECISIONS.md`.

**New abstractions introduced:** `AngebotRepository : IAngebotRepository` (`src/RenoTrack.Infrastructure/Persistence/Repositories/AngebotRepository.cs`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 9 (`AngebotRepositoryTests`) — `AddAsync_FollowedBySaveChangesAsync_PersistsTheAngebot`, `AddAsync_WithoutSaveChangesAsync_PersistsNothing`, `GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsTheFullSectionsAndItemsTree`, `GetByIdAsync_WhenAngebotDoesNotExist_ReturnsNull`, `AddingASectionAndItemToAnAggregateLoadedViaGetByIdAsync_IsPersistedBySaveChangesAsyncAlone` (extends the Slice 4/5 tracking proof to a two-level tree mutation), `HasActiveAngebotForLeadAsync_MatchesStateMachine24sNonTerminalDefinition` (3 theory cases: `Draft` → active, `CustomerApproved`/`CustomerRejected` → not active), `HasActiveAngebotForLeadAsync_WhenLeadHasNoAngebot_ReturnsFalse`.

**Final outcome:** 38 Infrastructure tests, alongside 153 Domain + 144 Application → **335 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 7 — `IAngebotReviewCommentRepository`

**Goal:** Simplest repository yet — `AddAsync` only (no `GetByIdAsync`, no other method), independent aggregate with no children. Optimized review per the user's standing instruction: no new architectural question, so no detailed design review — a strict subset of already-approved `AddAsync` patterns from Slices 4–6.

**New abstractions introduced:** `AngebotReviewCommentRepository : IAngebotReviewCommentRepository` (`src/RenoTrack.Infrastructure/Persistence/Repositories/AngebotReviewCommentRepository.cs`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 3 (`AngebotReviewCommentRepositoryTests`) — `AddAsync_FollowedBySaveChangesAsync_PersistsTheComment`, `AddAsync_WithoutSaveChangesAsync_PersistsNothing`, `AddAsync_PersistedViaOneContextInstance_IsVisibleFromAnother`.

**Final outcome:** 41 Infrastructure tests, alongside 153 Domain + 144 Application → **338 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 8 — `ICatalogItemRepository`

**Goal:** Independent aggregate, no children, no navigation — same `AddAsync`/`GetByIdAsync` shape as `LeadRepository`. Optimized review: no new architectural question. One already-settled rule reused (not reopened): BR-14/D38 — `GetByIdAsync` deliberately does not filter by `IsRetired`; that belongs to the not-yet-built `ICatalogItemQueries.SearchAsync`, not this repository.

**New abstractions introduced:** `CatalogItemRepository : ICatalogItemRepository` (`src/RenoTrack.Infrastructure/Persistence/Repositories/CatalogItemRepository.cs`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 5 (`CatalogItemRepositoryTests`) — `AddAsync_FollowedBySaveChangesAsync_PersistsTheCatalogItem`, `AddAsync_WithoutSaveChangesAsync_PersistsNothing`, `GetByIdAsync_AfterAddingViaADifferentContextInstance_ReturnsThePersistedCatalogItem`, `GetByIdAsync_WhenCatalogItemDoesNotExist_ReturnsNull`, `GetByIdAsync_ForARetiredCatalogItem_StillReturnsIt` (BR-14/D38 confirmation).

**Final outcome:** 46 Infrastructure tests, alongside 153 Domain + 144 Application → **343 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 9 — `ICatalogItemQueries`

**Goal:** First query implementation (not a repository) — `SearchAsync()` returns `CatalogItemDto` directly, bypassing aggregate hydration entirely. Focused review on the seven points the user specified, since this is a genuinely new shape.

**Review outcome — no new architectural decision, applying already-settled rules to a new shape:**
- **Why separate from `ICatalogItemRepository`:** already settled (D36) — CQRS-lite's read/write split is real, not nominal (CLAUDE.md §3).
- **Why DTOs, not entities:** a picker list has no reason to instantiate a full aggregate to read six scalar fields.
- **Executes entirely in SQL, no pre-projection materialization:** implemented by projecting field-by-field directly inside `Select(c => new CatalogItemDto(...))` rather than calling `CatalogItemMappingExtensions.ToDto()` inside the query — the latter would force full-entity materialization before the method call. Verified for real, not assumed: the projection (including `Money`/`ItemUnit` converter access via `.Amount`/`.Code`) is confirmed genuinely translatable by EF Core, proven by the integration tests actually running against LocalDB rather than throwing a client-evaluation error.
- **Retired-item filtering confirmed as the only place it happens:** `.Where(c => !c.IsRetired)` here; `CatalogItemRepository.GetByIdAsync` (Slice 8) and `AddAngebotItemCommandHandler` both deliberately don't filter (BR-14/D38).
- **`AsNoTracking()` used** — pure read, no follow-up mutation anywhere in the call path, so change tracking adds nothing.
- **No `IUnitOfWork` dependency** — nothing here is ever committed, consistent with the query side of CQRS-lite.
- **No paging/sorting/search parameter added** — `ICatalogItemQueries.SearchAsync(CancellationToken)`'s zero-parameter shape was already settled in Phase 2 (D37); nothing in SRS/Wireframes documents server-side pagination for the Catalog. One minor, non-contract implementation choice: `.OrderBy(c => c.Title)` for deterministic result order, since SQL has none by default.

**New abstractions introduced:** `CatalogItemQueries : ICatalogItemQueries` (`src/RenoTrack.Infrastructure/Persistence/Queries/CatalogItemQueries.cs` — new `Persistence/Queries/` folder, parallel to `Persistence/Repositories/`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 3 (`CatalogItemQueriesTests`) — `SearchAsync_ReturnsAllFieldsCorrectlyProjected` (also the concrete proof the projection expression is translatable), `SearchAsync_ExcludesRetiredItems`, `SearchAsync_AfterAddingANewCatalogItem_IncludesItInTheResultCount`.

**Final outcome:** 49 Infrastructure tests, alongside 153 Domain + 144 Application → **346 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 10 — `IAuditService`

**Goal:** First slice to add a new table since the `InitialCreate` migration itself. Full design review returned to (per the user's request), covering all 8 points: `AuditLog`'s responsibilities, Domain-vs-Infrastructure placement, what data to persist, FK strategy, transaction participation, failure behavior, migration review, and test strategy.

**Two new architectural decisions recorded (`ARCHITECTURE_DECISIONS.md` D49–D50), both by explicit user instruction:**
- **D49 — `AuditLog` is an Infrastructure persistence model, not a Domain entity.** No `BusinessRules.md` rule references it; it protects no invariant beyond having its fields set; it represents technical instrumentation, not business behavior. The first EF-mapped type in this project with no Domain-entity counterpart, living in a new `Persistence/Entities/` folder.
- **D50 — Best-Effort Audit strategy.** Every handler already calls `IUnitOfWork.SaveChangesAsync()` (the business commit) before `auditService.LogAsync(...)`, with no `SaveChangesAsync` call afterward — meaning `LogAsync` must commit its own write independently. Formalized: business consistency never depends on audit persistence; the business transaction always commits first; audit logging executes afterward as a separate, best-effort write; audit failures are logged as warnings (`ILogger<AuditService>`), never rethrown; an audit failure can never invalidate an already-committed business operation. Requires no change to `IAuditService`'s existing signature — entirely internal to the Infrastructure implementation.

**FK strategy (not a new decision, applying Architecture.md §11's already-documented rule):** no FK to any business entity — `AuditLog` has "no cross-entity linkage," only `EntityType`/`EntityId`, since one table logs against `Lead`/`Inspection`/`Angebot`/`CatalogItem` interchangeably. `PerformedByUserId` gets the same no-FK-until-Identity-slice treatment as every other user-reference column (D44).

**What data is persisted:** exactly `IAuditService.LogAsync`'s own parameters (`EntityType`, `EntityId`, `Action`, `PerformedByUserId`, `Details`) plus `CreatedAt` — nothing speculative (no IP address, user agent, request id; none documented anywhere).

**Migration review:** three-way comparison performed (no Domain entity per D49; `AuditLogConfiguration` ↔ `ERD.md`'s already-documented `AuditLogs` row/index at `ERD.md:240`/`:254`) before generating. Generated migration (`AddAuditLog`) manually reviewed: exactly one `CreateTable` (`AuditLogs`, 7 columns matching the configuration exactly) and one `CreateIndex` on `(EntityType, EntityId)`, no FK constraints (correct — no cross-entity linkage), no cascade behavior, clean `Down()`. Nothing unexpected.

**New abstractions introduced:** `AuditLog` (`src/RenoTrack.Infrastructure/Persistence/Entities/AuditLog.cs`), `AuditLogConfiguration`, `AuditService : IAuditService` (`src/RenoTrack.Infrastructure/Persistence/AuditService.cs`), `AddAuditLog` migration.

**Documentation updates:** `ARCHITECTURE_DECISIONS.md` (D49, D50, plus two new rejected-alternatives entries); this entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b. `ERD.md` needed no correction — the already-documented `AuditLogs` row/index matched the implementation exactly.

**Tests added:** 4 (`AuditServiceTests`) — `LogAsync_PersistsAllFieldsCorrectly`, `LogAsync_WithNoPerformingUserAndNoDetails_PersistsBothAsNull`, `LogAsync_CommitsIndependently_WithNoUnitOfWorkInvolved` (proves `LogAsync` persists without any `IUnitOfWork` call, confirming the D50 consequence), `LogAsync_WhenTheUnderlyingWriteFails_DoesNotThrow` (a disposed `DbContext` deterministically fails the write; proves the Best-Effort Audit strategy's swallow-and-log behavior for real, not just by code review).

**Final outcome:** 53 Infrastructure tests, alongside 153 Domain + 144 Application → **350 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 11 — `INumberGeneratorService`

**Goal:** The single highest-risk unverified assumption carried since Phase 2 (D34) — Angebot number generation must be atomic and collision-free under real concurrent load. Full design review returned to, focused on correctness (concurrency, locking, transaction boundaries), not implementation detail — with an explicit instruction not to assume correctness, but prove it.

**A real documentation/reality mismatch was found, not assumed away:** `Architecture.md` §8 and `ERD.md` both stated the sequence increment happens "inside the same DB transaction as the Angebot creation." Re-checking the actual, already-built `CreateAngebotCommandHandler` (`CreateAngebotCommandHandler.cs:43-50`) showed `NextAngebotNumberAsync` is awaited and returns a plain `string` **before** the `Angebot` entity even exists in memory — true same-transaction participation is not achievable without restructuring an already-approved Phase 2 handler (not permitted without a genuine bug). Both documents corrected in this commit to describe the actual, provably-safe design.

**Two new architectural decisions recorded (`ARCHITECTURE_DECISIONS.md` D51–D52):**
- **D51 — `NumberSequence` is an Infrastructure persistence model, not a Domain entity.** Same reasoning as `AuditLog` (D49) — a technical counter with no business invariant referenced anywhere.
- **D52 — Atomic single-statement increment, decoupled from the Angebot's own transaction; raw SQL deliberately, narrowly introduced.** The requirement is atomic increment-and-return of a single counter row. EF Core's read/track/write model cannot express this as one database round trip — even load-then-increment-then-`SaveChangesAsync` is two round trips with an in-memory gap between them, a real concurrent-duplicate race (this was explicitly analyzed and rejected, not assumed safe). A single `UPDATE ... OUTPUT INSERTED.LastValue` raw SQL statement, executed via `DbContext.Database.SqlQueryRaw<int>(...)` with no ambient/explicit EF transaction, runs as one SQL Server auto-commit unit — a row-level exclusive lock held only for that one statement (sub-millisecond), not across the rest of the handler. This is a **deliberate, narrowly-scoped exception** to the project's EF-Core-only Infrastructure convention, confined entirely to `NumberGeneratorService` — nowhere else in Infrastructure uses raw SQL. A first-of-year fallback (`INSERT ... OUTPUT`) handles a new `(SequenceType, Year)`; a losing race on that `INSERT` (caught via the unique-constraint violation, SQL error 2601/2627) triggers exactly one bounded retry of the `UPDATE`, guaranteed to succeed. Gaps in Angebot numbering confirmed acceptable — `BusinessRules.md`/`SRS.md` searched directly: BR-9's "never skip or reuse" requirement is Invoice-specific (a §14 UStG legal requirement), with no equivalent rule for Angebot numbers.

**Documentation corrections (`CLAUDE.md` §15's documentation-first discipline):** `Architecture.md` §8 and `ERD.md`'s `NumberSequences` row both corrected to describe the actual atomic-statement design, not literal same-transaction participation.

**Concurrency proven, not assumed:** the integration test suite includes a 50-way parallel-request test (`Task.WhenAll`, each caller with its own `DbContext` — a `DbContext` is not thread-safe, so this genuinely exercises SQL Server's own row locking) against the same year, asserting all 50 returned numbers are distinct and form the exact expected `00001`–`00050` sequence. This is real proof against actual LocalDB, not a code-review claim.

**New abstractions introduced:** `NumberSequence` (`src/RenoTrack.Infrastructure/Persistence/Entities/NumberSequence.cs`), `NumberSequenceConfiguration`, `NumberGeneratorService : INumberGeneratorService` (`src/RenoTrack.Infrastructure/Persistence/NumberGeneratorService.cs`), `AddNumberSequence` migration.

**Migration review:** exactly one `CreateTable` (`NumberSequences`, 4 columns matching the configuration), one unique `CreateIndex` on `(SequenceType, Year)`, no FK constraints, clean `Down()`. Matches the reviewed design exactly.

**Documentation updates:** `ARCHITECTURE_DECISIONS.md` (D51, D52, plus two new rejected-alternatives entries); `Architecture.md` §8; `ERD.md`'s `NumberSequences` row; this entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 4 (`NumberGeneratorServiceTests`) — `NextAngebotNumberAsync_ForANewYear_ReturnsSequenceOne`, `NextAngebotNumberAsync_CalledTwiceSequentially_Increments`, `NextAngebotNumberAsync_DifferentYears_EachStartsItsOwnSequenceAtOne`, `NextAngebotNumberAsync_ManyConcurrentCallsForTheSameYear_NeverReturnsADuplicate` (the concurrency proof — 50 parallel callers).

**Final outcome:** 57 Infrastructure tests, alongside 153 Domain + 144 Application → **354 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 12 — `IFileStorage` Placeholder

**Goal:** Register a minimal placeholder implementation only — the real `LocalDiskFileStorage` is Phase 4's deliverable, already settled (D42). No design review needed beyond confirming the placeholder satisfies the interface, can't be silently used in production, and clearly communicates it isn't the real implementation.

**Implementation choice:** `PlaceholderFileStorage.SaveAsync` always throws `NotImplementedException`, rather than a silent no-op — a no-op would be far worse (uploaded photos would appear to succeed while actually being dropped, with no error anywhere). Throwing loudly guarantees it cannot be accidentally relied upon before Phase 4 lands. Lives in a new `RenoTrack.Infrastructure/FileStorage/` folder (not `Persistence/`, since it has no EF Core/`DbContext` involvement at all — named `FileStorage`, not `Storage`, to avoid colliding with `.gitignore`'s pre-existing `storage/` rule for local runtime file storage, Architecture.md §9).

**New abstractions introduced:** `PlaceholderFileStorage : IFileStorage` (`src/RenoTrack.Infrastructure/FileStorage/PlaceholderFileStorage.cs`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 1 (`PlaceholderFileStorageTests`) — `SaveAsync_AlwaysThrowsNotImplementedException`. No database involved, so no `[Collection("Infrastructure Database")]` — lives under `tests/RenoTrack.Infrastructure.Tests/FileStorage/`, a new folder parallel to `Persistence/`.

**Final outcome:** 58 Infrastructure tests, alongside 153 Domain + 144 Application → **355 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 13 — `IEmailSender` Placeholder

**Goal:** Register a minimal placeholder implementation only — the real SMTP-backed implementation is Phase 9's deliverable (`IEmailSender`'s own doc comment; `CLAUDE.md` §11 — SRS OQ-3, the provider choice, must be resolved first). No design review beyond confirming the placeholder satisfies the interface, can't silently appear to send email, and clearly communicates the correct phase.

**Two corrections made before implementing, not assumed:** the real implementation belongs to **Phase 9**, not Phase 4 (unlike `IFileStorage`, Slice 12) — confirmed against `IEmailSender.cs`'s own doc comment and `CLAUDE.md` §11. And unlike `PlaceholderFileStorage` (which throws), `IEmailSender`'s own interface doc comment **explicitly sanctions a no-op/logging implementation** here, specifically so Phase 2's already-built handlers (`CreateLeadCommand`, `SubmitAngebotForReviewCommand`, `RequestAngebotChangesCommand`) can run end-to-end through Phases 3–8 without a real mail provider. A throw-based placeholder here would have broken that already-documented intent, not matched it — the two placeholders are deliberately different shapes for a real, checked reason, not an inconsistency.

**Implementation choice:** `LoggingNoOpEmailSender` never throws, but every call is logged at `Warning` level with the notification's key details, so it is always visible (in logs, in tests) that no real email was ever sent, satisfying "cannot silently appear to send emails" without contradicting the interface's documented no-op allowance. Verified for real: each test asserts against a capturing `ILogger` fake that a Warning entry was actually emitted, not just assumed from reading the code.

**New abstractions introduced:** `LoggingNoOpEmailSender : IEmailSender` (`src/RenoTrack.Infrastructure/Email/LoggingNoOpEmailSender.cs`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 3 (`LoggingNoOpEmailSenderTests`, one per interface method) — each asserts the call doesn't throw and that a `Warning`-level log entry containing "No email was sent" (plus the notification's key identifier) is actually captured. No database involved.

**Final outcome:** 61 Infrastructure tests, alongside 153 Domain + 144 Application → **358 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 14 — `AddInfrastructure()` DI Extension + `Program.cs` Wiring

**Goal:** First slice touching `RenoTrack.Api`. Focused design review on Infrastructure composition only (lifetimes, `DbContext` registration, service registrations, configuration handling, `Program.cs` shape, dependency graph, build-time DI validation) — no Application-layer wiring (command handlers, FluentValidation validators, `IOwnershipValidator`) is in scope here.

**Review outcome — no new architectural decision, direct application of already-settled rules:**
- **Every registration is Scoped**, matching `RenoTrackDbContext`'s own Scoped lifetime (`AddDbContext`'s default) — the mechanism D48 already identified as what makes "repository adds an entity → `UnitOfWork.SaveChangesAsync()` commits it" work. This includes `PlaceholderFileStorage`/`LoggingNoOpEmailSender`, which have no dependencies today but are kept on the same uniform Scoped rule rather than Singleton, to avoid a future captive-dependency bug the moment their real Phase 4/Phase 9 implementations gain a Scoped dependency.
- **`IOwnershipValidator` deliberately not registered here** — CLAUDE.md §9 is explicit its concrete implementation lives in `RenoTrack.Application`, not `Infrastructure`; that registration (and FluentValidation validators', and command handlers') belongs to a not-yet-built `AddApplication()` extension, out of scope for this slice.
- **Configuration read only at registration time** — `AddInfrastructure(IServiceCollection, IConfiguration)` extracts one connection string once, captured as a plain `string`; no service takes `IConfiguration`/`IServiceProvider` as a dependency (no service-locator pattern). Connection string lives in `appsettings.Development.json` only (LocalDB, no real secret — matches `RenoTrackDbContextFactory`'s/the test fixture's already-committed pattern), per Architecture.md §13; `appsettings.json` stays without one, so a missing Production connection string throws a clear `InvalidOperationException` at startup rather than failing cryptically later.
- **`Program.cs` stays minimal** — one added line, `builder.Services.AddInfrastructure(builder.Configuration);`.
- **No circular dependencies** — every repository/query/service depends only on `RenoTrackDbContext` (+ `ILogger<T>` for two of them), never on each other.
- **Build-time DI validation proven, not assumed** — a test builds the real container with `ValidateOnBuild = true, ValidateScopes = true` (the exact check that would catch a Singleton capturing a Scoped `DbContext`), resolves all 11 registered interfaces inside a real scope, and confirms two resolutions of `RenoTrackDbContext` within one scope return the same instance (the concrete proof behind D48's reasoning).

**New abstractions introduced:** `DependencyInjection.AddInfrastructure(this IServiceCollection, IConfiguration)` (`src/RenoTrack.Infrastructure/DependencyInjection.cs`).

**Other changes:** `Program.cs` calls `AddInfrastructure(builder.Configuration)`. `RenoTrack.Infrastructure.Tests.csproj` gains a `Microsoft.Extensions.Configuration` package reference (needed for `ConfigurationBuilder`/`AddInMemoryCollection` in the new DI test).

**Local-only connection string, not committed:** a `ConnectionStrings:RenoTrackDb` entry was added locally to `appsettings.Development.json` — but `.gitignore` deliberately excludes that file under "Secrets / local config (never commit real values)," the same category as `.env` (a pre-existing, intentional rule, not a coincidental collision like `FileStorage/`'s naming clash with `storage/` was in Slice 12). This file is never committed to this repo. Consequence for a fresh clone: `RenoTrack.Api` will throw the clear `InvalidOperationException` from `AddInfrastructure()` until a developer creates their own local `appsettings.Development.json` with a `ConnectionStrings:RenoTrackDb` entry (LocalDB, matching `RenoTrackDbContextFactory`'s hardcoded connection string) — expected, matches Architecture.md §13's policy, and doesn't block any current test (`RenoTrack.Infrastructure.Tests` uses its own hardcoded fixture connection string, never `appsettings.json`).

**Documentation updates:** This entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 4 (`DependencyInjectionTests`) — `AddInfrastructure_MissingConnectionString_ThrowsAtRegistrationTime`, `BuildingTheContainer_WithValidateOnBuildAndValidateScopes_Succeeds`, `EveryRegisteredInfrastructureService_ResolvesToItsExpectedConcreteType` (all 11 registrations), `RepositoriesResolvedInTheSameScope_ShareTheSameDbContextInstance`.

**Final outcome:** 65 Infrastructure tests, alongside 153 Domain + 144 Application → **362 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

---

## Slice 15 — Identity Storage + Role Seeding

**Goal:** The deliberately-last Phase 3 slice — ASP.NET Core Identity storage, role seeding, and the five deferred user-referencing FKs (D44) finally resolved. Full design review, focused on correctness and security per the user's explicit framing.

**Two new architectural decisions recorded (`ARCHITECTURE_DECISIONS.md` D53–D54):**
- **D53 — `ApplicationUser`/Identity roles are Infrastructure-only, forced by D1, not a judgment call.** `RenoTrack.Domain` has zero project references; `ApplicationUser` must inherit `IdentityUser<int>` (a framework base class), so it structurally cannot live in Domain — unlike `AuditLog`/`NumberSequence` (D49/D51), where Domain placement was a genuine choice. `IdentityRole<int>` used directly, no custom subclass.
- **D54 — `AddIdentityCore` (not `AddIdentity`); role seeding made safe under concurrent startup.** `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<int>>().AddEntityFrameworkStores<RenoTrackDbContext>()` avoids `AddIdentity`'s unwanted cookie-authentication-scheme defaults for a JWT-bearer-only API. **A real concurrency question raised during review, not assumed safe:** the naive check-then-create role-seeding pattern is a genuine check-then-act race under concurrent application startup — `AspNetRoles`' unique `NormalizedName` index (a framework default) makes the losing instance's `INSERT` fail with an unhandled `DbUpdateException`, since `RoleStore` doesn't convert `SaveChanges` failures into graceful `IdentityResult.Failed`. Mitigated the same way D52 handled its own first-of-year race: catch the failure, re-verify existence, treat "already exists now" as benign. Proven with a 10-concurrent-instance test, not just documented as acceptable.

**Identity schema:** `ApplicationUser : IdentityUser<int>` adds only `Name`, `IsActive` (deactivation, resolving SRS OQ-1 per PermissionMatrix.md), `CreatedAt`. `RenoTrackDbContext` now inherits `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>` (`base.OnModelCreating` called first). Table names stay the framework defaults (`AspNetUsers`, `AspNetRoles`, etc.) — no reason to rename away from what every Identity tool/guide expects. `ERD.md`'s simplified single-table `USER` sketch corrected to describe the real multi-table schema (same precedent as D41).

**Password hashing:** entirely delegated to Identity's own default `IPasswordHasher<TUser>` — no custom hashing code anywhere. Verified with a test confirming a created user's `PasswordHash` is populated and isn't the plaintext.

**Role seeding:** `IdentityRoleSeeder.SeedRolesAsync` seeds exactly `Admin`/`Inspector` — no user accounts (account creation is PermissionMatrix's own Admin-driven action, not storage setup). Deterministic and idempotent by construction; race-tolerant per D54.

**Deferred FKs (D44) resolved — 5 columns across 4 tables, verified against source, not assumed:**

| Column | Nullability | Delete behavior |
|---|---|---|
| `Lead.AssignedInspectorId` | nullable | Restrict |
| `Inspection.InspectorId` | required | Restrict |
| `Angebot.CreatedByInspectorId` | required | Restrict |
| `Angebot.ReviewedByAdminId` | nullable | Restrict |
| `AngebotReviewComment.AdminUserId` | required | Restrict |

**A real, expected consequence of adding these FKs retroactively:** 19 existing tests across 7 test files failed immediately after the migration, because they used arbitrary hardcoded inspector/admin ids (`5`, `7`, `2`, etc.) with no backing `AspNetUsers` row — harmless before this slice (no FK existed to violate), a real FK violation afterward. Fixed by adding a `SeedApplicationUserAsync` helper (mirroring the already-established `SeedLeadAsync` pattern) to each affected test class and replacing every hardcoded id with a real seeded one. This was anticipated as a real risk of this slice, not a surprise — verified by actually running the full suite after adding the FKs, not assumed clean.

**Authentication readiness:** storage only, confirmed — no `AddAuthentication()`/`AddJwtBearer()`, no `SignInManager`-based login endpoint, no `[Authorize]` attribute, no change to `UseAuthentication()`/`UseAuthorization()` beyond the pre-existing template boilerplate. All deferred to Phase 4.

**DI registration:** Identity registration added inside the existing `AddInfrastructure()` (Slice 14) — no new composition root needed, confirming D54's `AddIdentityCore` choice integrates cleanly.

**Migration review:** three-way comparison performed (no Domain entity per D53; `ApplicationUser`/FK configurations ↔ `ERD.md`, corrected). Generated migration (`AddIdentity`) manually reviewed: 7 `AspNetX` tables (`Users`, `Roles`, `UserRoles`, `UserClaims`, `UserLogins`, `UserTokens`, `RoleClaims`) with expected columns (including `ApplicationUser`'s 3 extra columns, correctly required/nullable), 5 new `AddForeignKey` operations (all `Restrict`, nullability matching the table above exactly — no `ALTER COLUMN` needed since the underlying column nullability was already correct from Slice 1), framework-internal Identity FKs correctly cascade (dormant in practice — Users are never hard-deleted), no unexpected tables, no missing Domain concept.

**New abstractions introduced:** `ApplicationUser` (`src/RenoTrack.Infrastructure/Identity/ApplicationUser.cs`), `ApplicationUserConfiguration`, `IdentityRoleSeeder` (`src/RenoTrack.Infrastructure/Identity/IdentityRoleSeeder.cs`), `AddIdentity` migration. `RenoTrackDbContext` now inherits `IdentityDbContext<...>`. `Program.cs` seeds roles at startup (scope-based, storage-only).

**Documentation updates:** `ARCHITECTURE_DECISIONS.md` (D53, D54, plus two new rejected-alternatives entries); `ERD.md` (`USER`/`ROLE` corrected, 5 FK rows updated, `AspNetUsers`/`AspNetRoles` terminology throughout); this entry (`PHASE3_PROGRESS.md`); `PROJECT_STATE.md` §6.4/§9; `NEXT_STEPS.md` §1b.

**Tests added:** 12 — `IdentityRoleSeederTests` (3: exact-two-roles, idempotent-run-twice, **10-concurrent-instance race proof**), `ApplicationUserTests` (1: password-hasher sanity check), plus 2 new FK-rejection tests (`Lead.AssignedInspectorId`, `Angebot.ReviewedByAdminId`) and 3 more (`Inspection.InspectorId`, `Angebot.CreatedByInspectorId`, `AngebotReviewComment.AdminUserId`) completing FK-rejection coverage for all 5 columns, plus fixes (not new tests, but real changes) to 19 previously-passing tests across 7 files to seed real users instead of arbitrary ids.

**Final outcome:** 74 Infrastructure tests, alongside 153 Domain + 144 Application → **371 solution-wide.** Build clean (0 warnings, 0 errors). Committed.

**Phase 3 is now feature-complete — all 15 slices done.** Per the user's explicit instruction, Phase 4 does not begin next; a full Phase 3 completion review (documentation audit, architecture decision audit, migration audit, DI audit, test summary, merge readiness report) follows separately.
