# NEXT_STEPS.md — What Happens Next

**Read this after `PROJECT_STATE.md` and before writing any code.** This file is specifically about *what to do next*, not what has already happened (that's `PHASE2_PROGRESS.md`) or why past decisions were made (that's `ARCHITECTURE_DECISIONS.md`).

---

## 1. Phase 2 — Complete and Merged

All of Phase 2's roadmap-defined scope (`PROJECT_ROADMAP.md`'s Phase 2 command list: `CreateLeadCommand`, `ScheduleInspectionCommand`, `CompleteInspectionCommand`, `CreateAngebotCommand`, `AddAngebotSectionCommand`, `AddAngebotItemCommand`, `SubmitAngebotForReviewCommand`, `RequestAngebotChangesCommand`, `ApproveAngebotCommand`) is done — 15 vertical slices, full record in `PHASE2_PROGRESS.md`. `CatalogItem`'s Application layer (`CreateCatalogItemCommand`, `UpdateCatalogItemCommand`, `RetireCatalogItemCommand`, `SearchCatalogItemsQuery` — Slices 11–14) was a deliberate, justified insertion into this branch, needed by `AddAngebotItemCommand`. **Merged to `main` via PR #5 (merge commit `dc85de1`).** `feature/phase-2-application-layer` is no longer the active branch.

## 1b. Phase 3 — In Progress (Slices 1–3 of 15 Done)

Design review + dependency map approved before any code was written (per the standing process). Working branch: `feature/phase-3-infrastructure-efcore`. Slice order (Identity deliberately moved to the end, after DI composition, per explicit user request — repository work stays independent of it):

1. **`RenoTrackDbContext` + entity configurations + `RenoTrack.Infrastructure.Tests` — ✅ done.** See `PHASE3_PROGRESS.md` Slice 1 for the full record, including two documentation contradictions resolved before implementation (`ERD.md`'s stale `Subtotal`/`LineTotal`/`DecisionResult` columns — D41; `LocalDiskFileStorage`'s phase assignment — D42) and the new `RenoTrack.Infrastructure.Tests` project (D40).
2. **`InitialCreate` migration — ✅ done.** A pre-migration three-way schema review (Domain ↔ EF configurations ↔ `ERD.md`) caught three missing FKs (`Inspection.LeadId`, `Angebot.LeadId`, `Angebot.InspectionId` — D45) before generating anything. A manual review of the generated migration then caught a second real bug — owned-child shadow FK columns were nullable instead of `NOT NULL` (D46) — fixed before the migration was finalized. Two new tests prove the migration applies cleanly and has zero model drift. See `PHASE3_PROGRESS.md` Slice 2 for the full record.
3. **`IUnitOfWork` — ✅ done.** Confirmed intentionally thin by explicit design review before implementation (D48) — a one-line wrapper over `SaveChangesAsync`, no transaction API, no `IDisposable`. See `PHASE3_PROGRESS.md` Slice 3 for the full record.
4. **`ILeadRepository` — ✅ done.** `LeadRepository` uses `DbSet<Lead>.FindAsync` for `GetByIdAsync` (no `Include` — Lead has no navigation properties, confirmed structurally permanent, not incidental); `AddAsync` performs no validation and never calls `SaveChangesAsync`, verified by a dedicated test. See `PHASE3_PROGRESS.md` Slice 4 for the full record, including the follow-up review of `FindAsync`'s compatibility with the wider repository contract.
5. **`IInspectionRepository` — ✅ done.** `InspectionRepository`'s `GetByIdAsync` eagerly `.Include(i => i.Photos)`s (`FindAsync` doesn't support `Include`, so `FirstOrDefaultAsync` is used) — the first repository where CLAUDE.md §4's "full aggregate" rule actually changes the query shape. Verified a photo added post-load is persisted by `SaveChangesAsync` alone, extending Slice 4's tracking finding to a collection mutation. See `PHASE3_PROGRESS.md` Slice 5 — reviewed only the deltas from `LeadRepository`, per the standing "don't repeat the full template" instruction.
6. **`IAngebotRepository` — ✅ done.** `AngebotRepository`'s `GetByIdAsync` uses a two-level `.Include(a => a.Sections).ThenInclude(s => s.Items)` (`AsSplitQuery` reviewed and confirmed unnecessary — a single chain, not sibling collections, at a documented small aggregate size). `HasActiveAngebotForLeadAsync` is a plain `AnyAsync` existence check (StateMachine.md §2.4's non-terminal-status definition), no `Include`. See `PHASE3_PROGRESS.md` Slice 6.
7. **`IAngebotReviewCommentRepository` — ✅ done.** `AddAsync` only, no new concerns beyond confirming the strict-subset shape. See `PHASE3_PROGRESS.md` Slice 7.
8. **`ICatalogItemRepository` — ✅ done.** Same `AddAsync`/`GetByIdAsync` shape as `LeadRepository`; `GetByIdAsync` deliberately does not filter `IsRetired` (BR-14/D38 reused, not reopened). See `PHASE3_PROGRESS.md` Slice 8.
9. **`ICatalogItemQueries` — ✅ done.** First query implementation — `SearchAsync()` projects directly to `CatalogItemDto` inside `Select(...)` (not via `.ToDto()`, which would force full-entity materialization), verified genuinely SQL-translatable by the integration tests. `AsNoTracking()`, no `IUnitOfWork`, excludes `IsRetired` (BR-12/D37, the only place this filter applies). See `PHASE3_PROGRESS.md` Slice 9.
10. **`IAuditService` — ✅ done.** New table (`AuditLogs`), full design review. Two new architectural decisions: **D49** — `AuditLog` is Infrastructure-only, not a Domain entity (no business invariant references it). **D50** — the **Best-Effort Audit strategy**: `LogAsync` commits its own write independently of `IUnitOfWork` (since every handler calls it after their own `SaveChangesAsync`), catches and logs any failure as a warning, never rethrows. See `PHASE3_PROGRESS.md` Slice 10.
11. **`INumberGeneratorService` — next up.** The highest-risk unverified assumption in the project (D34, flagged since Phase 2): the increment must be atomic within the same transaction as the entity being numbered, to prevent duplicate Angebot numbers under concurrent requests. Requires a new `NumberSequences` table (schema-affecting, same review discipline as Slice 10) **and** a real concurrency test — not optional, not just a code review.
6. `IAngebotRepository`
7. `IAngebotReviewCommentRepository`
8. `ICatalogItemRepository`
9. `ICatalogItemQueries`
10. `IAuditService`
11. `INumberGeneratorService` (+ the concurrency test flagged since Phase 2, D34)
12. `IFileStorage` placeholder (real `LocalDiskFileStorage` is Phase 4's — D42)
13. `IEmailSender` placeholder (real SMTP-backed implementation is Phase 9's — `CLAUDE.md` §11)
14. `AddInfrastructure()` extension + `Program.cs` wiring
15. Identity storage + role seeding

**Immediate next step:** Slice 11 (`INumberGeneratorService`). `NextAngebotNumberAsync(int year, CancellationToken)` needs a real, atomic implementation — the increment must happen inside the same transaction as the entity being numbered (Architecture.md §8, flagged as the project's highest-risk unverified assumption since Phase 2, D34). Needs a new `NumberSequences` table (schema-affecting — same three-way review and migration-review discipline as Slice 10) and, critically, **a genuine concurrency test proving no duplicate numbers under concurrent requests** — this was explicitly called out in the original handoff as not-optional-diligence. Full design review warranted.

## 2. Deferred Items — Explicitly Recorded, With Reasons

- **`SaveAngebotItemAsCatalogItemCommand` (SRS FR-4.10) — deferred out of Phase 2, not implemented.** Two independent reasons, both verified rather than assumed (see `ARCHITECTURE_DECISIONS.md` D39 for the full record):
  1. It was never actually in Phase 2's roadmap-defined scope — `PROJECT_ROADMAP.md`'s Phase 2 command list doesn't include it or any CatalogItem command; Phase 1b's title names the "save as catalog item" concept but its own deliverable list only required the Domain-level `CatalogItem.Create(..., createdFromAngebotItemId)`, already built.
  2. Implementing it today would force a new Application-layer lookup capability — resolving an `AngebotItem`'s owning `Angebot`/`Section` from the item's id alone (its documented route, `POST /api/v1/angebot-items/{itemId}/save-as-catalog-item`, carries no `AngebotId`) — that no other command needs and that isn't justified by anything else in current scope.
  - **Revisit only when a phase that actually needs it arrives** — most naturally Phase 3, since real EF Core ids would trivially resolve the lookup problem. Do not build a one-off lookup mechanism just to unblock this single command.
- **`IFileStorage.GetAsync`/`DeleteAsync`** — not built; no current command needs them (`CLAUDE.md` §4).
- **`Angebot.Send()`, `RecordCustomerApproval()`, `RecordCustomerRejection()`** — Domain methods exist (Phase 1) but have no Application-layer commands yet; deliberately deferred to Phase 6 (Token-link mechanism), since they depend on `ITokenLinkService`, which doesn't exist.
- **`AngebotItem` update/remove methods** — open question, not a rule (`ARCHITECTURE_DECISIONS.md` D12/`CLAUDE.md` §2). Revisit only with real evidence (a documented endpoint, an explicit business decision).
- **HTTP status-code mapping** for Domain's own `ArgumentException`/`InvalidOperationException` — deferred to Phase 4's API middleware design.

## 3. What Should NOT Be Changed

- **Do not modify `Lead`, `Inspection`, `Angebot`, `CatalogItem`, or `AngebotReviewComment`'s existing public API** without a genuine bug or a new, explicitly-documented business rule. "Treat Phase 1/1b/the-Domain-so-far as a stable baseline" is a standing instruction, repeated explicitly by the user at the close of both Phase 1 and Phase 1b.
- **Do not add `IOwnershipValidator` calls to Admin-`F` commands** (`ApproveAngebotCommand`, `RequestAngebotChangesCommand`, every CatalogItem command). Their absence is correct, not an oversight to "fix" for consistency.
- **Do not introduce MediatR, AutoMapper, or a mocking framework (Moq/NSubstitute)** — all three were explicitly considered and explicitly rejected for this project (see `ARCHITECTURE_DECISIONS.md` D22, `CLAUDE.md` §8, `CLAUDE.md` §14).
- **Do not add repository methods, DTO fields, or notification models speculatively** — every abstraction in this codebase was added because one specific, real command needed it at that exact moment. Continue that discipline; do not "future-proof" ahead of an actual requirement. `SaveAngebotItemAsCatalogItemCommand`'s deferral (§2 above) is this exact principle applied to a whole command, not just a field.
- **Do not re-litigate whether `AngebotReviewComment` should be an Angebot child** — this was verified carefully (multi-cycle review support, ERD/Wireframe/PermissionMatrix evidence) and confirmed correct in `ARCHITECTURE_DECISIONS.md` D32. Reopen only with genuinely new evidence, not a fresh read of the same documents.
- **Do not force-push to `main`, ever** (`CLAUDE.md` §19, `ARCHITECTURE_DECISIONS.md` D5). Always `git fetch origin` before any push and re-verify remote state.
- **Do not assume a command belongs to the current phase just because it appears in the SRS/Sequence Diagrams** — always check the phase's own roadmap-defined scope first (`ARCHITECTURE_DECISIONS.md` D39 is the concrete example: `SaveAngebotItemAsCatalogItemCommand` is real, documented, SRS-backed work, and still doesn't belong in Phase 2).

## 4. Decisions Considered Final (Do Not Reopen Without New Evidence)

- Clean Architecture layering + explicit (not transitive) cross-project references.
- No MediatR; hand-rolled `ICommandHandler<TCommand, TResult>`.
- No AutoMapper; manual `ToDto()` extension methods.
- Rich domain model with private constructors, named transition methods, self-guards only.
- `IOwnershipValidator` as named-business-intent methods, never a generic id-comparison helper.
- Audit policy: business milestones only, targeting the aggregate the business cares about (not necessarily the one directly mutated).
- Notification models are dedicated types in `Common.Notifications`, never feature DTOs.
- Repository/interface/DTO growth strictly on-demand, never speculative.
- Role-based authorization (API layer) vs. resource ownership (`IOwnershipValidator`, Application layer) — decided mechanically by PermissionMatrix's `F`/`S` marking.
- The Application layer generates stable external identifiers before invoking external infrastructure, when doing so lets a Domain guard reject before an irreversible side effect.
- Never truly delete a historical record (retire/void instead) — applies project-wide by default for any future "delete" requirement, not just the specific entities where it's already been formalized (Lead, Invoice, Inspection, CatalogItem).
- No CatalogItem command uses `IOwnershipValidator` — every action is Admin/Inspector-`F` per `PermissionMatrix.md` §6, confirmed during Slice 11's design review.
- `IQueryHandler<TQuery, TResult>` is a distinct interface from `ICommandHandler<TCommand, TResult>`, even though their method signatures currently coincide — queries get their own dispatch abstraction, not a reuse of the command one (`ARCHITECTURE_DECISIONS.md` D36).
- `SearchCatalogItemsQuery` starts with no `includeRetired` parameter — always excludes retired items, no flag until a real documented use case needs one (`ARCHITECTURE_DECISIONS.md` D37).
- A retired `CatalogItem` remains a valid direct `CatalogItemId` reference — retirement only affects discovery, never a direct reference (BR-14, `ARCHITECTURE_DECISIONS.md` D38).
- `AddAngebotItemCommand`'s `Quantity`/`UnitPrice`/`VatRate` are always caller-supplied for both paths; only `Description`/`Specification`/`Unit` are copied from a `CatalogItem` in the Catalog-sourced path.
- `SaveAngebotItemAsCatalogItemCommand` is out of Phase 2's scope and deferred — not a rejection of the feature, just a scope/sequencing decision (BR-14 is unrelated and stands; `ARCHITECTURE_DECISIONS.md` D39).
- No generic `Repository<TEntity>` base class for Infrastructure repositories — hand-written, per-aggregate classes only, matching the project's consistent anti-generic-abstraction stance.
- `RenoTrack.Infrastructure.Tests` uses real SQL Server LocalDB, never the EF Core InMemory provider (`ARCHITECTURE_DECISIONS.md` D40).
- Migrations are regenerated from the model when the model changes, never hand-edited — the migration is a product of the model, not a separately-maintained artifact.
- `IUnitOfWork`'s Infrastructure implementation is an intentionally thin, one-line wrapper over `SaveChangesAsync` — no transaction API, no `IDisposable` (`ARCHITECTURE_DECISIONS.md` D48).
- User-referencing FK constraints (`AssignedInspectorId`, `InspectorId`, `CreatedByInspectorId`, `ReviewedByAdminId`, `AdminUserId`) are deliberately deferred until the Identity slice (Slice 15) — not an oversight (`ARCHITECTURE_DECISIONS.md` D44).

## 5. What Still Requires Future Discussion (Not Yet Decided — Do Not Assume an Answer)

- **`SaveAngebotItemAsCatalogItemCommand`'s lookup design** — how the Application layer resolves an `AngebotItem`'s owning `Angebot`/`Section` from the item's id alone, once this command is actually built (§2 above). Not yet designed; do not pre-decide a repository shape for it now.
- Whether `AngebotItem` should ever gain update/remove methods (currently an open question, not a rule — `ARCHITECTURE_DECISIONS.md` D12). Revisit only with real evidence (a documented endpoint, an explicit business decision).
- The exact HTTP status-code mapping for Domain's own `ArgumentException`/`InvalidOperationException` (likely 400/409 respectively) — deferred to Phase 4's API middleware design.
- OQ-1 through OQ-4 from `SRS.md` §10 remain open at the SRS level (Admin managing Inspector accounts; website language; email provider choice — needed before Phase 9; "revise and resend" after rejection) — none of these block current work, but do not assume an answer to any of them without checking `SRS.md` first.

---

## 6. How to Start Your First Message in a Resumed Conversation

1. Read `CLAUDE.md`, `PROJECT_STATE.md`, `ARCHITECTURE_DECISIONS.md`, `PHASE3_PROGRESS.md`, and this file, in that order, in full (`PHASE2_PROGRESS.md` is historical background at this point, not required reading for resuming Phase 3 work).
2. `git fetch origin`; confirm you're on `feature/phase-3-infrastructure-efcore` and that it's still based on current `origin/main`.
3. Run `dotnet build RenoTrack.slnx` and `dotnet test RenoTrack.slnx` yourself and confirm the counts in `PROJECT_STATE.md` §3 still hold (350 as of Slice 10: 153 Domain + 144 Application + 53 Infrastructure). If they don't, something changed since this handoff was written — investigate before proceeding, don't just trust the stale numbers.
4. Continue with the next slice in `PHASE3_PROGRESS.md`'s order (§1b above) — Slice 11, `INumberGeneratorService`, unless a later slice's commit has already landed since this was written. Every slice: design review (full or abbreviated, per below) → implementation → `RenoTrack.Infrastructure.Tests` integration tests → documentation updates → commit, in that order, without exception. From Slice 7 onward, a detailed design review is reserved only for a genuinely new architectural question — otherwise state in a few sentences that no new decision is needed and proceed directly (per the user's standing instruction). Slice 11 is flagged as needing a full review again — new table, and the project's highest-risk unverified assumption (D34) — including a real concurrency test.
