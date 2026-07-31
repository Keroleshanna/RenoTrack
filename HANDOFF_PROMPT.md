# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system —
public website + admin/inspector dashboard), an existing, actively-developed project. This
is not a new project. A prior conversation completed Phase 3 in full, merged it to main, and
prepared this handoff package so you can continue with zero loss of architectural context.
Do not treat anything below as optional reading.

CURRENT STATE AT A GLANCE (verify all of this yourself in steps 1–7 below — do not trust it
without re-checking):

- Branch: main, at merge commit 85df430 (PR #6, "Phase 3: Infrastructure layer — EF Core
  persistence, repositories, and Identity storage"). feature/phase-3-infrastructure-efcore
  is merged and no longer the active branch (it still exists on the remote, unmerged-looking
  tools aside — trust `git log`/`git merge-base --is-ancestor`, not a possibly-cached GitHub
  API response, if you ever need to re-confirm this).
- Phase: Phase 3 — Infrastructure (per PROJECT_ROADMAP.md) — COMPLETE AND MERGED. All 15
  planned slices are done, reviewed, tested, documented, committed, and merged to main.
- Tests: 371 passing, 0 failing (153 RenoTrack.Domain.Tests, 144 RenoTrack.Application.Tests,
  74 RenoTrack.Infrastructure.Tests — all against real SQL Server LocalDB, never EF Core
  InMemory, D40). RenoTrack.Api.Tests still has 0 tests (Phase 4 not started). Verified
  directly against merged main, not carried over from the pre-merge branch state.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Migrations: 4 total (InitialCreate, AddAuditLog, AddNumberSequence, AddIdentity), all
  Pending — none applied to any shared/persistent database yet.
  `dotnet ef migrations has-pending-model-changes` confirms the model and migration history
  are in sync.
- CI: green, two jobs — build-and-test (ubuntu-latest: build + Domain/Application/Api
  tests) and infrastructure-tests (windows-latest, gated on the first job, starts real
  LocalDB then runs RenoTrack.Infrastructure.Tests). Split by OS specifically so
  Infrastructure tests keep using real LocalDB in CI too, not a weaker substitute (D56).
- Working tree: clean as of the last commit on main.
- Between opening the PR and merging it: a lead-reviewer pass found and fixed three
  Should-Fix issues (no Must-Fix issues); a real concurrency bug in IdentityRoleSeeder was
  found (by rerunning its concurrency test repeatedly, not a single pass) and fixed with a
  genuine design change, not a patch — IdentityRoleSeeder became a dedicated DI service with
  constructor-injected IServiceScopeFactory (D55). Full record in PROJECT_STATE.md §11.7.

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Read CLAUDE.md in full. This is the permanent engineering-rules document for this
   repository — every convention in it (Clean Architecture, DDD, CQRS without a mediator
   library, rich domain model, thin handlers, repository-growth-on-demand, ownership vs.
   role-based authorization, audit policy, notification policy, exception strategy, §21's
   Infrastructure/EF Core conventions covering all 15 Phase 3 slices, and the newest entries
   on concurrency-test verification and the IServiceScopeFactory/dedicated-service pattern)
   is an established, binding convention, not a suggestion you're free to deviate from.

2. Read PROJECT_STATE.md in full, including §10 (Phase 2 Closeout, historical) and §11
   (Phase 3 Closeout Review, including §11.7 — what happened between the initial closeout
   review and the actual merge). This tells you exactly what exists right now: every
   aggregate, every repository/service interface and its implementation, every command,
   every DTO, every EF Core entity configuration, every test count.

3. Read ARCHITECTURE_DECISIONS.md in full. This is a chronological log of every significant
   decision made on this project, including alternatives considered and rejected, and why.
   Several entries record real bugs caught and fixed (not hypothetical concerns) — read those
   carefully so you don't reintroduce the same mistakes. Pay particular attention to the
   Phase 3 entries (D40–D56): D45/D46 (two independent real bugs caught by two different
   review steps); D49/D51/D53 (which Infrastructure types are Domain-excluded, and why the
   reasoning differs between a judgment call and a hard framework constraint); D50/D52/D54
   (the same "catch, re-verify, treat as benign" concurrency-safety pattern applied
   independently to three different problems); D55 (a real concurrency bug found late, and
   why the fix was a genuine design change — a dedicated DI service with constructor-injected
   IServiceScopeFactory — rather than a patch); D56 (why CI is split by OS instead of
   weakening D40's real-LocalDB requirement).

4. Read PHASE3_PROGRESS.md in full. This is the detailed, non-summarized log of all 15
   vertical slices built in Phase 3 — goals, design discussions, what was introduced, what
   documentation was updated, what tests were added, and the final outcome of each.
   (PHASE2_PROGRESS.md is historical background at this point — read it only if you need
   context on a specific Phase 2 decision.)

5. Read NEXT_STEPS.md in full. §1b confirms Phase 3 is complete and merged. This file also
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

7. Run `git status`, `git branch --show-current`, `git log --oneline -20`, and `git fetch
   origin` to confirm main hasn't moved further since this handoff was written. Do not assume
   the state this file describes still holds without checking.

WHAT PHASE 3 DELIVERED (verify against PROJECT_STATE.md / PHASE3_PROGRESS.md / ARCHITECTURE_DECISIONS.md,
don't just trust this summary):

- Phases 0, 1, 1b, 2, 3 are all merged to main (Phase 2 via PR #5, merge commit dc85de1;
  Phase 3 via PR #6, merge commit 85df430).
- Phase 3: full EF Core persistence for every existing Domain aggregate, all 6
  repository/query implementations, IUnitOfWork, IAuditService (Best-Effort Audit strategy,
  D50), INumberGeneratorService (atomic, proven-concurrent-safe number generation, D52),
  IFileStorage/IEmailSender placeholders (D42/CLAUDE.md §11 — real implementations remain
  Phase 4/Phase 9), AddInfrastructure() DI composition, and ASP.NET Core Identity storage +
  role seeding (D53/D54/D55) with the five FKs deferred since D44 now real constraints.

YOUR FIRST TASK IS PHASE 4:

Phase 4 is the API layer (per PROJECT_ROADMAP.md) — controllers, the AddApplication() DI
extension (IOwnershipValidator, FluentValidation validators, command handlers — none of
these are wired into DI yet, deliberately deferred since RenoTrack.Api had no controllers to
need them), authentication/JWT issuance (Architecture.md §7.1 — Phase 3 built storage only,
no [Authorize] attributes or login endpoints exist yet), HTTP status-code mapping for Domain
exceptions (RFC 7807 ProblemDetails, Architecture.md §5.3), and the real LocalDiskFileStorage
(D42). Also worth an explicit early decision, not a silent default: how/when migrations get
applied to a real database — nothing in the codebase does this yet (no Database.MigrateAsync()
call, no CI/CD deployment step), so the first real run against a fresh database will fail at
Identity role-seeding with an unhelpful raw SQL error until this is decided.

Do not start Phase 4 code without a design review and explicit user sign-off first, exactly
as every Phase 2/3 slice was handled — read CLAUDE.md's process conventions again before
assuming you know the expected workflow. Start Phase 4 work on a new branch created off
current main (never commit directly to main, CLAUDE.md §19).

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
- A component needing several independent units of scoped work outside a single request
  (e.g. multi-item startup seeding) is a dedicated DI-registered class with IServiceScopeFactory
  injected via its constructor — never a static utility, never IServiceScopeFactory as a
  per-call method parameter (D55, CLAUDE.md §21). This pattern is now established, not
  something to redesign from scratch if it recurs.
- A concurrency test passing once is not proof — rerun it several times before trusting it
  (D55 was found exactly this way; CLAUDE.md §14).
- Before generating ANY new migration, perform the same three-way comparison established in
  Phase 3: Domain code <-> EF configurations <-> ERD.md. After generating it, manually review
  the migration's Up/Down methods before considering it complete. If the model changes after
  a migration already exists, fix the configuration and regenerate (dotnet ef migrations
  remove, then re-add) — never hand-edit a generated migration file. (Only remove a migration
  that has never been applied to a shared database; once applied, add a new migration instead.)
- Documentation is updated in the same commit as the code that depends on it, whenever a
  design review or implementation reveals a genuine gap or contradiction — never left for
  "later."

YOUR FIRST TASK:

Confirm you have completed steps 1–7 above, and give a brief summary (not a re-summary of
history — the user was there for all of it) of exactly where the project stands before
proposing a design for Phase 4's first slice.
```
