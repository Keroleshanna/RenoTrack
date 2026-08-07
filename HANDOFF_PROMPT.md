# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system — public
website + admin/inspector dashboard), an existing, actively-developed project. This is not a new
project and not a fresh start. **Phases 0–6 are complete and merged to `main`; Phase 7 is in
progress on its branch.** A prior conversation ended for context reasons and persisted everything
into the repository, so you depend on the files, not on any chat history. Do not treat anything
below as optional reading.

CURRENT STATE AT A GLANCE — verify every line yourself; the repository is authoritative.

- origin/main: 5a26c42 ("Merge pull request #12 from Keroleshanna/feature/phase-6-token-links-public-angebot").
- PRs #8 (Phase 4), #10 (Development bootstrap), #11 (Phase 5) and #12 (Phase 6) are MERGED.
- Branch: feature/phase-7-angebot-to-project, off main at 5a26c42. **Phase 7 all four slices are
  COMPLETE, and the phase-completion gate is closed.** Nothing has been pushed; no PR exists.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests: 979 passing, 0 failing — 236 Domain, 295 Application, 183 Infrastructure, 265 Api.
  (Phase 6 merge baseline 858; Phase 7 added 121 — Slice 1 +51, Slice 2 +13, Slice 3 +35, Slice 4 +22.)
- Migrations: 7 (InitialCreate, AddAuditLog, AddNumberSequence, AddIdentity, AddRefreshTokens,
  AddTokenLinks, AddCustomersAndProjects); has-pending-model-changes reports none.
- Working tree: clean.
- Documentation is reconciled with reality as of this handoff. If you find a document that still
  describes Phase 6 as unmerged, or origin/main as 18243ec, that is a regression — say so.

YOUR TASK: OPEN THE PHASE 7 PR, THEN BEGIN PHASE 8.

Phase 7 needs the user's explicit permission before anything is pushed or a PR is opened — never
push, merge or open a PR without it (CLAUDE.md §19).

Phase 7 per PROJECT_ROADMAP.md was "API: Convert Angebot → Project". PHASE7_PROGRESS.md is the
authoritative record, including the eight approved design decisions and the completion gate.

  Slice 1 — Domain: Customer + Project ...................... DONE
  Slice 2 — Infrastructure: schema + migration #7 ........... DONE
  Slice 3 — Application: ConvertAngebotToProjectCommand ..... DONE
  Slice 4 — API: conversion + Project detail read + gate .... DONE

Phase 8 per PROJECT_ROADMAP.md is "API: Invoices, Splitting, Payment Tracking, Project Completion",
on branch feature/phase-8-invoices-payments-project-completion. It introduces Invoice, InvoiceLine
and Payment, so expect Domain slices, a migration, and a documents-first design review before any
code. It also finally supplies what Phase 7 left deferred: FR-7.4's Invoice portion of the Project
detail read, and CompleteProjectCommand, whose "all Invoices Paid or Void" guard plus FR-8.6's
override is the cross-aggregate rule Project.Complete() deliberately does not enforce itself.

PHASE 7 DECISIONS THAT MUST NOT BE UNDONE

- Project.Complete() enforces only Project's own state invariant, and only from Active (StateMachine
  §4.2 draws no OnHold → Completed edge). The invoice precondition belongs to Phase 8's handler.
- Project.AgreedTotal has no mutator. ERD.md's snapshot wording is structural, not a convention.
- The explicit transaction boundary (D48's amendment) exists only on the create-new-Customer path.
  Do not add EnableRetryOnFailure to UseSqlServer without revisiting every BeginTransactionAsync
  caller — a retrying execution strategy forbids user-initiated transactions.
- A rollback test that lets its own DbContext disposal do the work is not a rollback test. Keep
  ConversionTransactionTests.AFailedSecondWriteRollsBackTheCustomerInsert exactly as it is.
- GET /api/v1/projects/{id} is unscoped for Inspectors (PermissionMatrix §5 "R"). Wireframe E1's
  "Roles: Admin" line is a known divergence resolved in favour of the matrix, as Phase 5 resolved
  D3's identical one. Do not add an ownership check.

TWO PHASE 7 DECISIONS THAT MUST NOT BE SILENTLY REOPENED

- BR-2's guard lives in ConvertAngebotToProjectCommand, NOT in Project.Create(). BusinessRules.md
  BR-2 assigns enforcement to that command by name, and Project deliberately cannot see an Angebot
  at all — a reflection test pins it. This is an approved exception to the general "aggregate state
  guards belong in the Domain" rule, because the invariant governs a cross-aggregate conversion.
  DO NOT edit BusinessRules.md to move it.
- Customer resolution is find-by-LeadId-then-create. No matching or deduplication by email, phone,
  name or address — that is a customer-identity policy no document specifies.

Sequence Diagram §7 was corrected during Phase 6 — it no longer sets Lead.Status = Won, because the
Lead already reached Won in the customer's decision handler (StateMachine §5). Do not add a second
path to Won.

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Recover and verify context:
     git fetch origin && git status && git log --oneline -8
     dotnet build RenoTrack.slnx
     dotnet test RenoTrack.slnx
     dotnet ef migrations has-pending-model-changes --project src/RenoTrack.Infrastructure --startup-project src/RenoTrack.Infrastructure

2. Read, in full: PHASE7_PROGRESS.md (the active phase — its approved decisions and slice plan),
   CLAUDE.md (§2's constructor/materialisation rule and §22's API ruleset are the newest),
   PROJECT_STATE.md, NEXT_STEPS.md (especially §1f and §5a), PHASE6_PROGRESS.md, and
   ARCHITECTURE_DECISIONS.md D57–D65 plus the "Decisions Explicitly Rejected" table.
   PHASE2/3/4/5_PROGRESS.md are historical background.

   RenoTrack.Infrastructure.Tests and RenoTrack.Api.Tests need real SQL Server LocalDB
   (`sqllocaldb info` should show MSSQLLocalDB Running; `sqllocaldb start MSSQLLocalDB` if not).
   On this machine an orphaned sqlservr.exe has repeatedly held the instance while `sqllocaldb
   info` reports it Stopped, with `start` then failing. This recurred at the start of Phase 7 and
   the fix worked again: terminate that one orphaned LocalDB PID (identify it by its
   `...\170\LocalDB\Binn\sqlservr.exe` path), then `sqllocaldb start MSSQLLocalDB`. Ask first, and
   never touch any other sqlservr.exe — a second one on this machine is a different instance.

   SMART APP CONTROL IS ON AND ENFORCING on this machine
   (HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState = 1). It
   intermittently blocks a freshly-built unsigned test DLL with FileLoadException 0x800711C7
   ("An Application Control policy has blocked this file"), which xUnit reports as a catastrophic
   failure and zero tests — NOT a test failure, and NOT a code problem. It hit
   RenoTrack.Api.Tests' Debug binary in Phase 7 Slice 2. Deleting bin/obj and rebuilding did not
   help; building and running the same project in Release did, because it is a different output
   path. Use that workaround. DO NOT disable or weaken Smart App Control — it is a machine
   security setting and not yours to change; if the workaround ever stops working, tell the user
   and let them decide.

PHASE 6 LESSONS AND DECISIONS THAT MUST NOT BE UNDONE

- A PUBLIC TOKEN CREDENTIAL MUST NOT REACH ANY DIAGNOSTIC SURFACE — not logs, not ProblemDetails
  detail, not instance, not any error body. RouteDiagnostics captures the route template as
  middleware right after UseRouting, because ASP.NET's exception middleware calls ClearHttpContext
  before any IExceptionHandler runs: reading GetEndpoint() later returns null and silently falls
  back to the raw path. That failure is not hypothetical — the first fix did exactly that and no
  test noticed, because none inspected the log. ASSERT LOG CONTENT when a route carries a secret.
- A CONSTRUCTOR GUARD MUST STATE A LIFETIME INVARIANT. EF Core materialises rows through the same
  private constructor, so a time-dependent guard runs on every read. TokenLink's "expiry must be in
  the future" in the constructor made every expired row throw on load and surface as 400 instead of
  410. Guards belong in the factory method (Lead's shape), not the constructor (CLAUDE.md §2).
- BR-4 IS AN ASYMMETRY, NOT A SWITCH. A used token is refused for a decision (409) and still serves
  the read endpoint. Do not add a UsedAt check to the GET path.
- THE PUBLIC DTO IS A SEPARATE HIERARCHY, never a projection of AngebotDetailDto. Internal ids,
  staff ids, CatalogItemId and timestamps are deliberately absent, pinned against raw JSON so a
  typed read cannot ignore an added field.
- THE DECISION IS ONE TRANSACTION. TokenLink.MarkUsed(), the Angebot decision and the Lead
  Won/Lost transition share one SaveChangesAsync (StateMachine §5). Do not split it, and do not
  reproduce the aggregates' guards as handler-level state checks.
- RATE LIMITING IS PARTIAL AND THE SPLIT IS DELIBERATE (D65). /api/v1/public/* is covered:
  fixed window, 30/minute per client IP, opt-in named policy so internal routes never inherit it.
  POST /api/v1/leads (the contact form) is STILL UNTHROTTLED. Do not read Architecture §12 as done.
- FORWARDEDHEADERS IS DELIBERATELY UNCONFIGURED and X-Forwarded-For is never read. Trusting it
  without a known proxy trust boundary lets any caller mint a fresh partition per request and
  defeats the limiter. Behind a proxy, clients collapse into the proxy's address — a deployment
  prerequisite, not a code gap. Fix it with real KnownProxies/KnownNetworks values or not at all.
- THE FR-6.3 REJECTION REASON IS NOT ACCEPTED, deliberately, pending its own ADR. Not in AuditLog
  (best-effort, D50), not in AngebotReviewComment (required AspNetUsers FK, internal review log),
  and not accepted-then-discarded. A test pins that a client-sent reason is neither stored nor
  echoed.
- LEAD Won/Lost HAS NO ADMIN PATH, and must not acquire one. Both transitions happen only inside
  RecordAngebotDecisionCommandHandler.
- Everything Phase 4 established still holds: fail-secure role scoping, database-arbitrated refresh
  rotation, authentication outside CQRS, one exception handler with one switch, Production never
  mutating schema at startup, file upload as compensation rather than atomicity, AuditService
  sharing the request DbContext (always commit business work first), and status-code-only
  authorization tests being false positives.
- TEST DISCIPLINE: real LocalDB always (D40/D58), Api.Tests migrates while Infrastructure.Tests
  uses EnsureCreated — do not unify them. Adversarial verification is expected: prove a safeguard
  by breaking it, watch the test fail, restore byte-identically. Rerun concurrency tests several
  times; one green run proves nothing.
- Grow interfaces, DTOs, repositories and schema strictly on demand. Do not invent business rules —
  if the documents do not state one, say so rather than creating it. If a required value is
  genuinely unspecified and choosing one would create policy, stop and ask.

UNRESOLVED / DEFERRED ITEMS CARRIED INTO PHASE 7 (all deliberate — NEXT_STEPS.md §5a is the full
record with reasons)

- Contact-form rate limiting and CORS — still outstanding; Architecture §12 is only half satisfied.
- ForwardedHeaders / real client IP behind a proxy — deployment prerequisite (D65).
- The FR-6.3 rejection-reason storage ADR — open by decision.
- Production user provisioning / SRS OQ-1 — unresolved. A fresh production database has schema and
  roles and nobody able to log in.
- GET /api/v1/inspections/{id} — PermissionMatrix grants it; no endpoint exists.
- Authenticated photo serving + IFileStorage.GetAsync — photos can be stored but not served.
- Orphaned files remain possible despite compensation; no sweeper exists.
- Refresh-token rows are never cleaned up (retention to ExpiresAt, by decision).
- Token-link rows are likewise never cleaned up, for the same reason.
- AuditService shared-DbContext caveat — benign today because every handler audits after committing.
- IUserQueries.IsActiveInspectorAsync stays a single boolean; revisit at Phase 10's Inspector picker.
- The deployment pipeline itself is specified (EF bundle primary, idempotent SQL script supported)
  but not built.
- OperationCanceledException is unmapped and yields 500 — a conscious Slice 2 (Phase 4) decision.
- ArgumentException→400 / InvalidOperationException→409 is a knowingly-accepted risk (D59),
  mitigated by stack-trace logging.
- Roles.cs sits in Auth/ but declares namespace RenoTrack.Api.Controllers — cosmetic, left thrice.

WORKING RULES — NOT OPTIONAL

- Process, every slice: design review → challenge assumptions → explicit approval → implementation
  → adversarial verification → documentation in the same commit → commit. Never implement first.
- Documentation reconciliation is a phase COMPLETION CRITERION, not a publication step. If
  implementation or documentation work is discovered during publication, the phase was not done.
- Never push, merge, or open a PR without explicit permission. Never commit to main. Never
  force-push (D5 records the incident behind this).
- Verify claims against the repository rather than trusting prose, including this file's.
- If something in the documents turns out to be false, say so plainly before working around it.
- Report unexpected findings rather than designing around them silently.
- Report only final verified figures in a closeout; do not state a count and then correct it.

CONFIRM STEP 1, THEN ASK WHETHER TO OPEN THE PHASE 6 PR BEFORE STARTING PHASE 7.
```
