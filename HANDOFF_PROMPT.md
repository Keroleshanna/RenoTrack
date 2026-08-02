# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system — public
website + admin/inspector dashboard), an existing, actively-developed project. This is not a new
project and not a fresh start. **Phases 0–4 are all complete and merged to `main`.** A prior
conversation ended for context reasons and persisted everything into the repository, so you depend
on the files, not on any chat history. Do not treat anything below as optional reading.

CURRENT STATE AT A GLANCE — verify every line yourself; the repository is authoritative.

- Branch: main. Phase 4's feature branch is merged; its local copy was deleted and the remote copy
  kept, matching how the Phase 2 and Phase 3 branches were left.
- HEAD / origin/main: e1a4d9ef8d8653ab44f1ff7822c855a4be6ea40c
  ("Merge pull request #8 from Keroleshanna/feature/phase-4-api-auth-leads-inspections").
- PR #8 is MERGED. Phase 4 needs no further delivery work.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests: 553 passing, 0 failing — 153 Domain, 165 Application, 101 Infrastructure, 134 Api.
- Migrations: 5 (InitialCreate, AddAuditLog, AddNumberSequence, AddIdentity, AddRefreshTokens);
  has-pending-model-changes reports none.
- Working tree: clean.
- Documentation is reconciled with merged reality as of this handoff. If you find a document that
  still describes Phase 4 as in progress or unpushed, that is a regression — say so.

YOUR TASK: PHASE 5, STARTING WITH A DEVELOPMENT BOOTSTRAP / SEED DATA SLICE.

Phase 5 per PROJECT_ROADMAP.md is "API: Angebot Builder + Internal Review Workflow", on branch
feature/phase-5-angebot-builder-review. **It opens with a Development bootstrap / seed-data slice,
before any business feature**, so the backend is manually testable as a real running application
rather than only through the automated suites.

Present a DESIGN REVIEW for that slice and wait for approval before writing any code.
Intended direction (a direction, not a specification — challenge it):
  - a Development-only Admin account and a Development-only Inspector account;
  - opt-in, never silently running everywhere;
  - idempotent;
  - no passwords or secrets committed to the repository;
  - MUST NEVER provision users in Production;
  - this does NOT resolve SRS OQ-1 — production first-Admin provisioning stays an explicit open
    question;
  - do not seed large amounts of fake business data unless a later design review establishes a
    real need.
This interacts with D63, which deliberately separates schema initialization, role reference data,
and user provisioning, and states that no code path creates a user in any environment. The seeding
slice must respect that separation rather than quietly overturn it.

After that slice is implemented and verified, run a MANUAL Phase 4 smoke test against the real API:
Admin login → create/read Lead → schedule Inspection → Inspector login → access assigned Lead →
upload photo → update notes → complete Inspection. Only then move to the first actual Phase 5
business slice (Angebot/Catalog endpoints per PROJECT_ROADMAP.md).

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Recover and verify context from `main`:
     git fetch origin && git status && git log --oneline -5
     dotnet build RenoTrack.slnx
     dotnet test RenoTrack.slnx
     dotnet ef migrations has-pending-model-changes --project src/RenoTrack.Infrastructure --startup-project src/RenoTrack.Infrastructure

2. Read, in full: CLAUDE.md (§22 is the API-layer ruleset and the newest), PROJECT_STATE.md
   (especially §12, Phase 4's closeout record), NEXT_STEPS.md (especially §5a), PHASE4_PROGRESS.md
   (including its closing "What Happened After Slice 11" section), and ARCHITECTURE_DECISIONS.md
   D57–D63 plus the "Decisions Explicitly Rejected" table. PHASE2/3_PROGRESS.md are historical
   background.

   RenoTrack.Infrastructure.Tests and RenoTrack.Api.Tests need real SQL Server LocalDB
   (`sqllocaldb info` should show MSSQLLocalDB Running; `sqllocaldb start MSSQLLocalDB` if not).
   On this machine an orphaned sqlservr.exe has repeatedly held the instance while `sqllocaldb
   info` reports it Stopped, with `start` then failing with Windows error 575. The fix is to
   terminate that one orphaned PID — ask first, and never touch any other sqlservr.exe.

PHASE 4 LESSONS AND DECISIONS THAT MUST NOT BE UNDONE

- FAIL-SECURE ROLE SCOPING. "Not an Inspector" must never mean "Admin". Unrestricted access is only
  ever reached by positively establishing the Admin role; the narrower role is checked first so a
  dual-role account is scoped; anything else is refused. A fail-open version of this was found and
  reproduced in Slice 6. Keep both the [Authorize(Roles=...)] attribute and the in-method guard.
- REFRESH-TOKEN ROTATION IS DATABASE-ARBITRATED. RefreshToken.RevokedAt is an EF concurrency token.
  Without it, 8 of 8 concurrent rotations of one token succeeded (reproduced 3/3) — this was not a
  narrow or theoretical race. Revocation and its replacement must stay in ONE SaveChanges. Chain
  revocation uses a set-based ExecuteUpdateAsync, because load-mutate-save threw
  DbUpdateConcurrencyException and surfaced as a 500.
- AUTHENTICATION SITS OUTSIDE CQRS (D60), and every login failure returns an identical 401. Do not
  make those messages more helpful. Lockout depends on AuthController's explicit
  IsLockedOutAsync/AccessFailedAsync/ResetAccessFailedCountAsync calls.
- SERVER-DERIVED CALLER IDENTITY (D61, as corrected in Slice 7): only values describing *who is
  acting* come from the JWT. A third party the caller legitimately chooses (an Admin picking which
  Inspector to send) is genuine request input.
- BUSINESS RULES ABOUT STAFF ACCOUNTS GO THROUGH IUserQueries (D62). A database FK is not a business
  rule.
- ONE EXCEPTION HANDLER, ONE SWITCH (D59). Mapped exceptions surface their message; unmapped ones
  never do. Every mapped exception is logged at Warning WITH ITS STACK TRACE — do not remove that.
- PRODUCTION NEVER MUTATES SCHEMA AT STARTUP (D63). Database:Mode is Verify (default) or Migrate
  (Development opt-in, hard-refused in Production). Verification checks migration history in BOTH
  directions plus required roles. Do not add a mode that skips verification.
- FILE UPLOAD IS COMPENSATION, NOT ATOMICITY. Every rejection precedes the write; a failed commit
  triggers a best-effort delete that rethrows the original exception. Orphans remain possible. Never
  document it as a consistency guarantee.
- AUDITSERVICE IS NOT WRITE-ISOLATED. It calls SaveChangesAsync on the same request-scoped
  DbContext, so if called with unrelated pending changes it will flush them too. Always commit the
  business operation first, then audit.
- A STATUS-CODE-ONLY AUTHORIZATION TEST CAN BE A FALSE POSITIVE when a role gate and an ownership
  guard both yield 403. Assert an empty body (role gate) versus a ProblemDetails body (ownership).
- LEAD Won/Lost IS PHASE 6 WORK, driven by the customer's token-link decision. StateMachine §5
  requires Lead to reach Won only inside the Angebot decision handler's transaction. Do NOT create
  Admin MarkWon/MarkLost commands or endpoints.
- TEST DISCIPLINE: Api.Tests migrates its database, Infrastructure.Tests uses EnsureCreated — do not
  unify them (D58). Real LocalDB always, never InMemory (D40). Adversarial verification is expected:
  prove a safeguard by breaking it, watch the test fail, restore byte-identically. Rerun concurrency
  tests several times; one green run proves nothing.
- Grow interfaces, DTOs, repositories and schema strictly on demand. Do not invent business rules —
  if the documents do not state one, say so rather than creating it.

UNRESOLVED / DEFERRED ITEMS CARRIED INTO PHASE 5 (all deliberate — NEXT_STEPS.md §5a is the full
record with reasons)

- Rate limiting on the anonymous POST /api/v1/leads, and CORS — deferred to a hardening slice.
  The endpoint is public, state-creating and currently unthrottled.
- Production user provisioning / SRS OQ-1 — unresolved. A freshly initialized production database
  has schema and roles and nobody able to log in. The Development bootstrap slice must NOT be
  treated as resolving this.
- GET /api/v1/inspections/{id} — PermissionMatrix grants the permission; no endpoint exists and it
  is absent from Architecture §5.2. Needs a documents-first decision. This is why scheduling returns
  201 with no Location header.
- Authenticated photo serving + IFileStorage.GetAsync — photos can be stored but not served.
- Orphaned files remain possible despite compensation; no sweeper exists.
- Refresh-token rows are never cleaned up (retention to ExpiresAt, by decision).
- AuditService shared-DbContext caveat (above) — benign today because every handler audits after
  committing.
- IUserQueries.IsActiveInspectorAsync stays a single boolean; revisit when Phase 10's Inspector
  picker makes "exists but ineligible" worth distinguishing.
- Lead Won/Lost → Phase 6.
- The deployment pipeline itself is specified (EF bundle primary, idempotent SQL script supported)
  but not built.
- OperationCanceledException is unmapped and yields 500 — a conscious Slice 2 scope decision.
- ArgumentException→400 / InvalidOperationException→409 is a knowingly-accepted risk (D59),
  mitigated by stack-trace logging.
- Roles.cs sits in Auth/ but declares namespace RenoTrack.Api.Controllers — cosmetic, left twice.
- TestProtectedController's "delete once redundant" note is now unmet; keeping it is defensible.

WORKING RULES — NOT OPTIONAL

- Process, every slice: design review → challenge assumptions → explicit approval → implementation
  → adversarial verification → documentation in the same commit → commit. Never implement first.
- Never push, merge, or open a PR without explicit permission. Never commit to main. Never
  force-push (D5 records the incident behind this).
- Verify claims against the repository rather than trusting prose, including this file's.
- If something in the documents turns out to be false, say so plainly before working around it.
- Report unexpected findings rather than designing around them silently.

CONFIRM STEP 1, THEN PRESENT THE DESIGN REVIEW FOR THE DEVELOPMENT BOOTSTRAP / SEED DATA SLICE.
Do not implement it until it is explicitly approved.
```
