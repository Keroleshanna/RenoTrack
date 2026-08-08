# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system — public
website + admin/inspector dashboard), an existing, actively-developed project. This is not a new
project and not a fresh start. **Phases 0–7 are complete and merged to `main`; Phase 8 is in
progress on its branch.** A prior conversation ended for context reasons and persisted everything
into the repository, so you depend on the files, not on any chat history. Do not treat anything
below as optional reading.

CURRENT STATE AT A GLANCE — verify every line yourself; the repository is authoritative.

- origin/main: 697292b ("Merge pull request #13 from Keroleshanna/feature/phase-7-angebot-to-project").
- PRs #8 (Phase 4), #10 (Development bootstrap), #11 (Phase 5), #12 (Phase 6) and #13 (Phase 7) are MERGED.
- Branch: feature/phase-8-invoices-payments-project-completion, off main at 697292b.
  **Phase 8 Slices 1–4 of 7 are COMPLETE.** Nothing has been pushed; no PR exists.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests: 1,190 passing, 0 failing — 332 Domain, 342 Application, 215 Infrastructure, 301 Api.
  (Phase 7 merge baseline 979; Slice 1 +74, Slice 2 +15, Slice 3 +80, Slice 4 +42.)
- Migrations: 8 (InitialCreate, AddAuditLog, AddNumberSequence, AddIdentity, AddRefreshTokens,
  AddTokenLinks, AddCustomersAndProjects, AddInvoicesAndPayments); has-pending-model-changes
  reports none.
- Working tree: clean.
- Documentation is reconciled with reality as of this handoff. If you find a document that still
  describes Phase 7 as unmerged, or origin/main as 5a26c42, that is a regression — say so.

YOUR TASK: CONTINUE PHASE 8 AT SLICE 5.

Phase 8 per PROJECT_ROADMAP.md is "API: Invoices, Splitting, Payment Tracking, Project Completion".
PHASE8_PROGRESS.md is the authoritative record, including the thirteen approved design decisions.

  Slice 1 — Domain: Invoice + Payment child ................. DONE
  Slice 2 — Infrastructure: schema + migration #8 ........... DONE
  Slice 3 — Create Invoice + balance + numbering + VAT split ... DONE
  Slice 4 — Send Invoice + public token read ............... DONE
  Slice 5 — Mark Paid + Void .............................. next
  Slice 6 — Complete Project + FR-7.4 invoice information
  Slice 7 — Overdue capability + Phase 8 completion gate

PHASE 8 DECISIONS THAT MUST NOT BE SILENTLY REOPENED (full list in PHASE8_PROGRESS.md)

- INVOICELINE IS DEFERRED ENTIRELY — no Domain type, table, configuration, repository, DTO or
  migration content. Migration #8 excludes it by decision, not by omission.
- PHASE 8 IS FULL-PAYMENT-ONLY. Invoice.MarkPaid takes no amount and always records GrossAmount;
  a reflection test asserts no Money appears in its signature. ERD's one-to-many Payments shape is
  forward-compatibility, NOT evidence that partial payments work.
- NO OVERDUE SCHEDULER OF ANY KIND. The Sent → Overdue transition is real capability and exists in
  the Domain; automatic execution is a recorded gap awaiting a job-hosting decision. An Admin
  endpoint, a BackgroundService and read-time derivation were each considered and each rejected.
- THE VAT SPLIT IS PER-RATE AND PROPORTIONAL to the originating Angebot's gross rate mix, never a
  blended rate, and must satisfy sum(Net) + sum(VAT) == GrossAmount exactly with deterministic,
  tested residual-cent handling. Invoice.Create enforces that equality structurally.
- INVOICE NUMBERS are unique and never reused; GAPLESSNESS IS NOT GUARANTEED. Make no claim about
  what German law requires.
- NO PDF, no IPdfGenerator, no bank-detail configuration/schema/DTO field. Wireframe A4's "Download
  PDF" and bank details are recorded gaps.
- A VOID REASON IS REQUIRED from every source state including Draft; StateMachine §3.3's blank
  guard cell was a documentation omission and is reconciled.
- THERE IS NO Invoice.SentAt — ERD defines no such column, and a test asserts its absence.
- PutOnHold/Resume remain assigned to NO phase. Phase 8 does not claim them.

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

2. Read, in full: PHASE8_PROGRESS.md (the active phase — its approved decisions and slice plan),
   CLAUDE.md (§2's constructor/materialisation rule and §22's API ruleset are the newest),
   PROJECT_STATE.md, NEXT_STEPS.md (especially §1g and §5a), PHASE7_PROGRESS.md, and
   ARCHITECTURE_DECISIONS.md D57–D65 plus the "Decisions Explicitly Rejected" table.
   PHASE2/3/4/5/6_PROGRESS.md are historical background.

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

UNRESOLVED / DEFERRED ITEMS CARRIED INTO PHASE 8 (all deliberate — NEXT_STEPS.md §1g and §5a are
the full record with reasons)

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

CONFIRM STEP 1, THEN CONTINUE PHASE 8 AT SLICE 2 — DESIGN REVIEW AND EXPLICIT APPROVAL FIRST,
NEVER IMPLEMENTATION FIRST.
```
