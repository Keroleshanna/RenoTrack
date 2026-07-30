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
