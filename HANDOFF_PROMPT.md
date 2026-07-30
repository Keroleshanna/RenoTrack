# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system —
public website + admin/inspector dashboard), an existing, actively-developed project. This
is not a new project. A prior conversation completed Phase 3 in full and prepared this
handoff package so you can continue with zero loss of architectural context. Do not treat
anything below as optional reading.

CURRENT STATE AT A GLANCE (verify all of this yourself in steps 1–7 below — do not trust it
without re-checking):

- Branch: feature/phase-3-infrastructure-efcore (created off main at dc85de1)
- Latest commit: 8a2597f — "docs: fix stale Phase 3 'in progress'/'deferred' wording found
  during final merge-readiness review"
- Phase: Phase 3 — Infrastructure (per PROJECT_ROADMAP.md) — COMPLETE. All 15 planned slices
  are done, reviewed, tested, documented, and committed. A Pull Request has been opened
  (not yet merged as of this writing) — if it has since merged, your branch/next steps are
  different from what this file assumes; check git log/GitHub before proceeding.
- Slice: 15 of 15 complete — RenoTrackDbContext + entity configurations +
  RenoTrack.Infrastructure.Tests; InitialCreate/AddAuditLog/AddNumberSequence/AddIdentity
  migrations; UnitOfWork; all 6 repositories/queries (Lead, Inspection, Angebot,
  AngebotReviewComment, CatalogItem ×2); IAuditService; INumberGeneratorService (with a
  proven concurrency guarantee); IFileStorage/IEmailSender placeholders;
  AddInfrastructure() DI wiring; Identity storage + role seeding.
- Tests: 371 passing, 0 failing (153 RenoTrack.Domain.Tests, 144 RenoTrack.Application.Tests,
  74 RenoTrack.Infrastructure.Tests — all against real SQL Server LocalDB, never EF Core
  InMemory). RenoTrack.Api.Tests still has 0 tests (Phase 4 not started).
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Migrations: 4 total (InitialCreate, AddAuditLog, AddNumberSequence, AddIdentity), all
  currently Pending — none applied to any shared/persistent database yet.
  `dotnet ef migrations has-pending-model-changes` confirms the model and migration history
  are in sync.
- Working tree: clean as of the last commit.
- A PR review already happened on this branch (lead-reviewer pass covering architectural
  consistency, migrations, DI, docs, test quality) — three Should-Fix findings were
  identified and fixed in commit range after the closeout review; no Must-Fix issues were
  found. See PROJECT_STATE.md §11 for the full closeout/merge-readiness report.

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Read CLAUDE.md in full. This is the permanent engineering-rules document for this
   repository — every convention in it (Clean Architecture, DDD, CQRS without a mediator
   library, rich domain model, thin handlers, repository-growth-on-demand, ownership vs.
   role-based authorization, audit policy, notification policy, exception strategy, and
   §21's Infrastructure/EF Core conventions, now covering all 15 Phase 3 slices) is an
   established, binding convention, not a suggestion you're free to deviate from.

2. Read PROJECT_STATE.md in full, including §10 (Phase 2 Closeout, historical) and §11
   (Phase 3 Closeout Review — documentation/architecture-decision/migration/DI audit, test
   summary, and merge-readiness report). This tells you exactly what exists right now: every
   aggregate, every repository/service interface and its implementation, every command,
   every DTO, every EF Core entity configuration, every test count.

3. Read ARCHITECTURE_DECISIONS.md in full. This is a chronological log of every significant
   decision made on this project, including alternatives considered and rejected, and why.
   Several entries record real bugs caught and fixed (not hypothetical concerns) — read those
   carefully so you don't reintroduce the same mistakes. Pay particular attention to the
   Phase 3 entries (D40–D54): D45/D46 (two independent real bugs caught by two different
   review steps — a pre-migration schema comparison and a post-generation migration review);
   D49/D51/D53 (which Infrastructure types are Domain-excluded, and why the reasoning differs
   between a judgment call and a hard framework constraint); D50/D52/D54 (the same
   "catch, re-verify, treat as benign" concurrency-safety pattern applied independently to
   three different problems — best-effort audit logging, atomic number generation, and
   race-tolerant role seeding, the last two each proven by a real concurrency test, not just
   asserted).

4. Read PHASE3_PROGRESS.md in full. This is the detailed, non-summarized log of all 15
   vertical slices built in Phase 3 — goals, design discussions, what was introduced, what
   documentation was updated, what tests were added, and the final outcome of each.
   (PHASE2_PROGRESS.md is historical background at this point — read it only if you need
   context on a specific Phase 2 decision.)

5. Read NEXT_STEPS.md in full. §1b confirms all 15 Phase 3 slices are done. This file also
   tells you what NOT to change, which decisions are considered final, and which questions
   genuinely remain open.

6. Run `dotnet build RenoTrack.slnx` and `dotnet test RenoTrack.slnx` yourself. Confirm the
   test counts match what's stated above (371 tests — 153 Domain, 144 Application,
   74 Infrastructure — 0 warnings, 0 errors). RenoTrack.Infrastructure.Tests requires a real
   SQL Server LocalDB instance to be available on the machine (`sqllocaldb info` should list
   MSSQLLocalDB) — if it's not available, that is itself something to flag before proceeding.
   Also run `dotnet ef migrations has-pending-model-changes --project src/RenoTrack.Infrastructure
   --startup-project src/RenoTrack.Infrastructure` and confirm it reports no pending changes.
   If any of this doesn't match, stop and investigate before writing any code — something
   changed since this handoff was written, and the discrepancy itself is information you need
   first.

7. Run `git status`, `git branch --show-current`, `git log --oneline -20`, and check whether
   the Phase 3 PR has been merged (via `gh pr view` or the GitHub UI) — do not assume the
   state this file describes still holds. If the PR has merged, your branch and next task are
   different from everything below (most likely: start Phase 4 from an up-to-date `main`,
   not continue on `feature/phase-3-infrastructure-efcore`).

WHAT PHASE 3 DELIVERED (verify against PROJECT_STATE.md / PHASE3_PROGRESS.md / ARCHITECTURE_DECISIONS.md,
don't just trust this summary):

- Phases 0, 1, 1b, 2 are all merged to main (Phase 2 via PR #5, merge commit dc85de1).
- Phase 3: full EF Core persistence for every existing Domain aggregate, all 6
  repository/query implementations, IUnitOfWork, IAuditService (Best-Effort Audit strategy,
  D50), INumberGeneratorService (atomic, proven-concurrent-safe number generation, D52),
  IFileStorage/IEmailSender placeholders (D42/CLAUDE.md §11 — real implementations remain
  Phase 4/Phase 9), AddInfrastructure() DI composition, and ASP.NET Core Identity storage +
  role seeding (D53/D54) with the five FKs deferred since D44 now real constraints.

IF THE PR HAS ALREADY MERGED — YOUR FIRST TASK IS PHASE 4:

Phase 4 is the API layer (per PROJECT_ROADMAP.md) — controllers, the AddApplication() DI
extension (IOwnershipValidator, FluentValidation validators, command handlers — none of
these are wired into DI yet, deliberately deferred since RenoTrack.Api had no controllers to
need them), authentication/JWT issuance (Architecture.md §7.1 — Phase 3 built storage only,
no [Authorize] attributes or login endpoints exist yet), HTTP status-code mapping for Domain
exceptions (RFC 7807 ProblemDetails, Architecture.md §5.3), and the real LocalDiskFileStorage
(D42). Also worth an explicit early decision, not a silent default: how/when migrations get
applied to a real database — nothing in the codebase does this yet (no Database.MigrateAsync()
call, no CI/CD step), so the first real run against a fresh database will fail at Identity
role-seeding with an unhelpful raw SQL error until this is decided.

Do not start Phase 4 code without a design review and explicit user sign-off first, exactly
as every Phase 2/3 slice was handled — read CLAUDE.md's process conventions again before
assuming you know the expected workflow.

IF THE PR HAS NOT YET MERGED:

Confirm with the user whether to proceed with the merge (do not force-push, do not merge
without explicit instruction), or whether there's new feedback to address on the branch
first. Do not start Phase 4 work on an unmerged branch.

OUTSTANDING DECISIONS / OPEN QUESTIONS (do not assume an answer to any of these):

- Whether AngebotItem should ever gain update/remove methods (open question, not a rule — D12).
- The exact HTTP status-code mapping for Domain's ArgumentException/InvalidOperationException
  (likely 400/409) — deferred to Phase 4's API middleware design.
- SaveAngebotItemAsCatalogItemCommand's eventual lookup design (how to resolve an AngebotItem's
  owning Angebot from the item's id alone) — deferred out of Phase 2's scope (D39); real EF
  ids now exist (Phase 3 is done), which resolves the original blocker, but the command
  itself still hasn't been built — revisit only with an explicit decision to do so.
- OQ-1 through OQ-4 from SRS.md §10 remain open at the SRS level — check SRS.md before
  assuming an answer to any of them.
- Migration-application strategy for real environments (auto-migrate at startup vs.
  CI/CD-driven `dotnet ef database update`) — not yet decided, flagged above as a likely
  early Phase 4 item.

CRITICAL WORKING RULES — THESE ARE NOT OPTIONAL:

- Never re-open an architectural decision recorded in ARCHITECTURE_DECISIONS.md or listed as
  "final" in NEXT_STEPS.md §4 unless you have discovered genuinely new evidence (a real bug,
  a newly-noticed documentation contradiction, an explicit new instruction from the user) —
  not a fresh stylistic opinion arrived at by re-reading the same documents.
- Never force-push to `main`. Always `git fetch origin` before any push and verify actual
  remote state — do not assume your local view of `origin/main` is current (ARCHITECTURE_DECISIONS.md D5).
- Never commit directly to `main`. All work happens on its own feature branch, merged via PR,
  exactly like every prior phase.
- Follow the same process every prior slice in this project used: for anything touching new
  architectural territory, do analysis and get explicit user sign-off on the design BEFORE
  writing code — do not implement first and explain after.
- Grow repositories, interfaces, DTOs, and schema strictly on demand — add a
  method/field/table only when one specific, real command or entity actually needs it.
  No generic Repository<TEntity> base class, ever (same anti-generic-abstraction stance
  as IOwnershipValidator, D28).
- Before generating ANY new migration, perform the same three-way comparison established in
  Phase 3: Domain code <-> EF configurations <-> ERD.md. After generating it, manually review
  the migration's Up/Down methods before considering it complete. If the model changes after
  a migration already exists, fix the configuration and regenerate (dotnet ef migrations
  remove, then re-add) — never hand-edit a generated migration file. (Only remove a migration
  that has never been applied to a shared database; once applied, add a new migration instead.)
- Documentation is updated in the same commit as the code that depends on it, whenever a
  design review or implementation reveals a genuine gap or contradiction — never left for
  "later." (A real example from this exact branch: a final pre-merge review found CLAUDE.md
  and PROJECT_STATE.md still describing Phase 3 as "in progress"/certain FKs as "deferred"
  after they'd actually shipped — fixed immediately, not left for a future cleanup pass.)

YOUR FIRST TASK:

Confirm you have completed steps 1–7 above, determine whether the Phase 3 PR has merged, and
give a brief summary (not a re-summary of history — the user was there for all of it) of
exactly where the project stands before proposing what to work on next.
```
