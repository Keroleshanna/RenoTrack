# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system —
public website + admin/inspector dashboard), an existing, actively-developed project. This
is not a new project. A prior conversation ran out of context and prepared a full handoff
package so you can continue with zero loss of architectural context. Do not treat anything
below as optional reading.

CURRENT STATE AT A GLANCE (verify all of this yourself in steps 1–7 below — do not trust it
without re-checking):

- Branch: feature/phase-3-infrastructure-efcore (created off main at dc85de1)
- Latest commit: ff4da09 — "feat(infrastructure): implement UnitOfWork"
- Phase: Phase 3 — Infrastructure (per PROJECT_ROADMAP.md)
- Slice: Slices 1–3 of 15 complete (RenoTrackDbContext + entity configurations +
  RenoTrack.Infrastructure.Tests; InitialCreate migration; IUnitOfWork). Slice 4
  (ILeadRepository) is the next task.
- Tests: 317 passing, 0 failing (153 RenoTrack.Domain.Tests, 144 RenoTrack.Application.Tests,
  20 RenoTrack.Infrastructure.Tests — all against real SQL Server LocalDB, never EF Core
  InMemory). RenoTrack.Api.Tests still has 0 tests (Phase 4 not started).
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Working tree: clean as of the last commit.

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Read CLAUDE.md in full. This is the permanent engineering-rules document for this
   repository — every convention in it (Clean Architecture, DDD, CQRS without a mediator
   library, rich domain model, thin handlers, repository-growth-on-demand, ownership vs.
   role-based authorization, audit policy, notification policy, exception strategy, and —
   new since Phase 3 started — §21's Infrastructure/EF Core conventions) is an established,
   binding convention, not a suggestion you're free to deviate from.

2. Read PROJECT_STATE.md in full. This tells you exactly what exists right now: every
   aggregate, every repository/service interface and its implementation status, every
   command, every DTO, every EF Core entity configuration, every test count, every
   deferred/incomplete piece of work, and the immediate next task.

3. Read ARCHITECTURE_DECISIONS.md in full. This is a chronological log of every significant
   decision made on this project, including alternatives considered and rejected, and why.
   Several entries record real bugs caught and fixed (not hypothetical concerns) — read those
   carefully so you don't reintroduce the same mistakes. Pay particular attention to the most
   recent ones from Phase 3 (D40–D48): D41/D42 (documentation contradictions found and fixed
   before Slice 1's code was written), D45 (three missing foreign keys found by a deliberate
   pre-migration three-way schema review), D46 (a shadow-FK nullability bug found by manually
   reviewing the generated migration — caught by a different review than D45, proving neither
   review was redundant with the other), D48 (IUnitOfWork confirmed intentionally thin, with
   the reasoning recorded, not just the conclusion).

4. Read PHASE3_PROGRESS.md in full. This is the detailed, non-summarized log of every
   vertical slice built in Phase 3 so far (Slice 1 through Slice 3) — goals, design
   discussions, what was introduced, what documentation was updated, what tests were added,
   and the final outcome of each. (PHASE2_PROGRESS.md is historical background at this point —
   read it only if you need context on a specific Phase 2 decision; it is not required for
   resuming Phase 3 work.)

5. Read NEXT_STEPS.md in full. This tells you precisely what to do next (§1b has the full
   15-slice Phase 3 order with each completed slice's one-line summary), what NOT to change,
   which decisions are considered final, and which questions genuinely remain open.

6. Run `dotnet build RenoTrack.slnx` and `dotnet test RenoTrack.slnx` yourself. Confirm the
   test counts match what PROJECT_STATE.md states (as of this handoff: 317 tests passing —
   153 Domain, 144 Application, 20 Infrastructure — 0 warnings, 0 errors). RenoTrack.Infrastructure.Tests
   requires a real SQL Server LocalDB instance to be available on the machine
   (`sqllocaldb info` should list MSSQLLocalDB) — if it's not available, that is itself
   something to flag before proceeding, not something to work around with a different provider.
   If the counts don't match, stop and investigate before writing any code — something changed
   since this handoff was written, and the discrepancy itself is information you need first.

7. Run `git status`, `git branch --show-current`, and `git log --oneline -10`. Confirm you
   are on `feature/phase-3-infrastructure-efcore` at commit `ff4da09` (or later, if more
   slices have landed since this was written), and that this branch has not yet been pushed
   or opened as a PR.

COMPLETED WORK (verify against PROJECT_STATE.md / PHASE3_PROGRESS.md, don't just trust this
summary):

- Phase 0, 1, 1b, 2 are all merged to main. Phase 2 merged via PR #5 (merge commit dc85de1).
- Phase 3 Slice 1: RenoTrackDbContext + one IEntityTypeConfiguration<T> per existing Domain
  entity (Lead, Inspection, InspectionPhoto, Angebot, AngebotSection, AngebotItem,
  CatalogItem, AngebotReviewComment) + MoneyConverter/ItemUnitConverter value converters.
  New RenoTrack.Infrastructure.Tests project (a deliberate addition beyond Architecture.md's
  originally-documented 3-test-project structure, D40).
- Phase 3 Slice 2: InitialCreate migration, generated via RenoTrackDbContextFactory
  (IDesignTimeDbContextFactory, design-time only — real DI composition is still Slice 14).
  Not yet applied to any shared/persistent database.
- Phase 3 Slice 3: UnitOfWork — a one-line wrapper over SaveChangesAsync, confirmed
  intentionally thin by explicit design review (D48).

REMAINING WORK — Phase 3's approved slice order (Identity deliberately last, so repository
work stays independent of it):

4. ILeadRepository        <- YOU ARE HERE (next task)
5. IInspectionRepository
6. IAngebotRepository
7. IAngebotReviewCommentRepository
8. ICatalogItemRepository
9. ICatalogItemQueries
10. IAuditService
11. INumberGeneratorService (+ a real concurrency test — the single highest-risk unverified
    assumption carried since Phase 2, D34 — do not skip this test)
12. IFileStorage placeholder (the REAL LocalDiskFileStorage belongs to Phase 4, not Phase 3 —
    CLAUDE.md was corrected on this exact point during Phase 3's design review, D42; do not
    reopen that scope question)
13. IEmailSender placeholder (the real SMTP-backed implementation is Phase 9's, CLAUDE.md §11)
14. AddInfrastructure() DI extension + Program.cs wiring
15. Identity storage + role seeding (Admin/Inspector) — deliberately last

After Slice 15, Phase 3 should get the same closeout review Phase 2 got before opening a PR
(verify every roadmap item complete, every deferred item has a reason, docs are consistent,
test count, build status, recommended PR title/commit range) — do not skip that step just
because it isn't explicitly numbered as a slice.

OUTSTANDING DECISIONS / OPEN QUESTIONS (do not assume an answer to any of these):

- Whether AngebotItem should ever gain update/remove methods (open question, not a rule — D12).
- The exact HTTP status-code mapping for Domain's ArgumentException/InvalidOperationException
  (likely 400/409) — deferred to Phase 4's API middleware design.
- SaveAngebotItemAsCatalogItemCommand's eventual lookup design (how to resolve an AngebotItem's
  owning Angebot from the item's id alone) — deferred out of Phase 2's scope entirely (D39);
  not Phase 3's concern unless a future phase's design review decides otherwise.
- OQ-1 through OQ-4 from SRS.md §10 remain open at the SRS level — check SRS.md before
  assuming an answer to any of them.
- User-referencing FK constraints (AssignedInspectorId, InspectorId, CreatedByInspectorId,
  ReviewedByAdminId, AdminUserId) are deliberately unconstrained until Slice 15 adds a Users
  table (D44) — this is settled, not open, but easy to mistake for an oversight if you don't
  read D44 first.

CRITICAL WORKING RULES — THESE ARE NOT OPTIONAL:

- Never re-open an architectural decision recorded in ARCHITECTURE_DECISIONS.md or listed as
  "final" in NEXT_STEPS.md §4 unless you have discovered genuinely new evidence (a real bug,
  a newly-noticed documentation contradiction, an explicit new instruction from the user) —
  not a fresh stylistic opinion arrived at by re-reading the same documents.
- Never force-push to `main`. Always `git fetch origin` before any push and verify actual
  remote state — do not assume your local view of `origin/main` is current (ARCHITECTURE_DECISIONS.md D5).
- Never commit directly to `main`. All work happens on feature/phase-3-infrastructure-efcore
  until Phase 3 is closed out and merged via PR, exactly like every prior phase.
- Follow the same process every prior slice in this project used: for anything touching new
  architectural territory, do analysis and get explicit user sign-off on the design BEFORE
  writing code — do not implement first and explain after.
- Grow repositories, interfaces, DTOs, and EF Core schema strictly on demand — add a
  method/field/table only when one specific, real command or entity you are currently
  building actually needs it. This applies to Infrastructure exactly as much as it did to
  Application in Phase 2: no DbSet, no FK, no repository method "while you're at it."
  No generic Repository<TEntity> base class, ever (same anti-generic-abstraction stance
  as IOwnershipValidator, D28).
- Before generating ANY new migration, perform the same three-way comparison Slice 2
  established: Domain code <-> EF configurations <-> ERD.md. After generating it, manually
  review the migration's Up/Down methods before considering it complete — check every
  operation is expected, no accidental cascade deletes, no unnecessary columns, no
  unexpected tables, nothing missing. If the model changes after a migration already exists,
  fix the configuration and regenerate (dotnet ef migrations remove, then re-add) — never
  hand-edit a generated migration file. (Only remove a migration that has never been applied
  to a shared database; once applied, add a new migration instead.)
- Every Infrastructure slice is vertically complete before moving to the next: design review
  -> implementation -> RenoTrack.Infrastructure.Tests integration tests (real LocalDB, never
  EF Core InMemory) -> documentation updates (PROJECT_STATE.md, NEXT_STEPS.md,
  PHASE3_PROGRESS.md, and ARCHITECTURE_DECISIONS.md if a genuine decision was made) -> commit.
  No partially-finished infrastructure, no "configure this later."
- Documentation is updated in the same commit as the code that depends on it, whenever a
  design review or implementation reveals a genuine gap or contradiction — never left for
  "later."

YOUR FIRST TASK:

Begin with a short design review for ILeadRepository (Phase 3, Slice 4) covering at least:
the exact shape of the interface it must implement (ILeadRepository already exists in
RenoTrack.Application.Common.Interfaces — read it, don't guess its members), how GetByIdAsync
should load the aggregate (Lead has no child entities, so this should be the simplest
repository in the whole set), and what — if anything — an integration test needs to prove
beyond what Slice 1's LeadPersistenceTests in RenoTrack.Infrastructure.Tests already covers
(don't duplicate Slice 1's round-trip proof; the new tests should be about the repository
class's own behavior, e.g. AddAsync/GetByIdAsync contract behavior including the not-found
case). Do not write any code until that design has been reviewed and explicitly approved by
the user, exactly as every previous slice in both Phase 2 and Phase 3 has been handled.

Confirm you have completed steps 1–7 above, and give a brief summary (not a re-summary of
history — the user was there for all of it) of your understanding of exactly where the
project stands, before beginning the ILeadRepository design review.
```
