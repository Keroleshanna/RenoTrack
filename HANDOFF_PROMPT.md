# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system —
public website + admin/inspector dashboard), an existing, actively-developed project. This is
not a new project and not a fresh start. Phases 0–3 are merged to main; Phase 4 is in
progress on a feature branch with 8 of 11 slices complete. A prior conversation ended for
context reasons and persisted everything into the repository, so you depend on the files, not
on any chat history. Do not treat anything below as optional reading.

CURRENT STATE AT A GLANCE — verify every line of this yourself in steps 1–7. It was accurate
when written; the repository is authoritative if anything disagrees.

- Branch: feature/phase-4-api-auth-leads-inspections
- HEAD: 102dde7 ("feat(api): add Inspection photo upload with real LocalDiskFileStorage
  (Phase 4 Slice 8)") — plus one documentation-only handoff commit made immediately after,
  which is where this file's current content came from. `git log` is authoritative.
- origin/main: babfff9. The branch is 12+ commits ahead, 0 behind. NOTHING IS PUSHED for the
  handoff commit; earlier slices are also unpushed. No PR has been opened for Phase 4.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests: 516 passing, 0 failing — 153 Domain, 164 Application, 89 Infrastructure, 110 Api.
- Migrations: 5 (InitialCreate, AddAuditLog, AddNumberSequence, AddIdentity,
  AddRefreshTokens). `dotnet ef migrations has-pending-model-changes` reported no pending
  changes.
- Working tree: clean apart from AGENTS.md, which is UNTRACKED AND PRE-EXISTING. It is not
  part of this work and was deliberately excluded from every commit. Do not add it.

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Read CLAUDE.md in full — the permanent engineering rules. §22 (API layer) is the newest
   and most relevant section; it grew substantially during Phase 4. Every rule there is
   settled convention, not a suggestion.

2. Read PROJECT_STATE.md in full — what exists right now, layer by layer.

3. Read NEXT_STEPS.md in full — especially §5a, which lists every open item carried out of
   Phase 4 and explicitly separates deliberate deferrals from forgotten work. Also §3 ("What
   Should NOT Be Changed") and §4 ("Decisions Considered Final").

4. Read PHASE4_PROGRESS.md in full — the detailed narrative of Slices 1–8: what was designed,
   what was challenged and changed under review, what was proven by reproduction, and what was
   deliberately not built. This is the densest source of Phase 4 context.

5. Read ARCHITECTURE_DECISIONS.md, at minimum D57–D62 (Phase 4's decisions) plus the
   "Decisions Explicitly Rejected" table at the end. Several entries record real defects found
   and fixed — read those carefully so you do not reintroduce them. Note D61 carries an
   explicit self-correction made in Slice 7.

6. Run these yourself and confirm they match the figures above:
     dotnet build RenoTrack.slnx
     dotnet test RenoTrack.slnx
     dotnet ef migrations has-pending-model-changes --project src/RenoTrack.Infrastructure --startup-project src/RenoTrack.Infrastructure
   RenoTrack.Infrastructure.Tests and RenoTrack.Api.Tests both need real SQL Server LocalDB
   (`sqllocaldb info` should list MSSQLLocalDB). If it is unavailable, say so before writing
   code — that is information, not an obstacle to work around with a weaker substitute.

7. Run git status, git branch --show-current, git log --oneline origin/main..HEAD, and
   git fetch origin. Confirm the branch has not moved and main has not advanced.

WHAT PHASE 4 HAS DELIVERED SO FAR (Slices 1–8, all committed, none pushed)

Slice 1 — API foundation, conventions, docs.
  api/v1 routing by literal URL segment, no versioning library (D57). [Authorize] on
  controllers by default, [AllowAnonymous] opted into per action. RenoTrack.Api.Tests boots
  the real app via WebApplicationFactory<Program> against real LocalDB, schema created with
  Database.MigrateAsync() — deliberately NOT EnsureCreated, unlike Infrastructure.Tests, and
  the two fixtures must not be "unified" (D58). Scalar serves the OpenAPI document; the JWT
  bearer scheme is declared by a document transformer. Api.Tests runs in CI's Windows job
  (database-backed-tests), never Linux.

Slice 2 — Global RFC 7807 exception handling (D59).
  ONE IExceptionHandler with a single explicit switch — never one handler per type, never a
  try/catch in a controller. Mapping: NotFoundException→404, ForbiddenException→403,
  ConflictException→409, FluentValidation ValidationException→400 with a field-keyed errors
  dictionary, ArgumentException→400, InvalidOperationException→409, everything else→500.
  Mapped exceptions surface their message as `detail`; unmapped ones deliberately do not (an
  unexpected SqlException must not leak connection strings). The ArgumentException/
  InvalidOperationException mapping is a KNOWINGLY ACCEPTED RISK — both are BCL-wide types —
  mitigated by logging every mapped exception at Warning WITH ITS FULL STACK TRACE. Do not
  remove that logging. traceId is added in AddProblemDetails' CustomizeProblemDetails, not in
  the handler, so it covers responses ASP.NET produces itself.

Slice 3 — AddApplication() DI composition root.
  Every registration explicit — no assembly scanning, no Scrutor, no AddValidatorsFromAssembly.
  Handlers registered BY INTERFACE so ICommandHandler stays load-bearing. Uniformly Scoped.
  Registrations grouped by category (validators, command handlers, query handlers, services)
  and ordered by business workflow, not alphabetically. The "forgot to register" risk is
  covered by a reflection-based test in Api.Tests that discovers every handler/validator in the
  Application assembly and asserts each resolves — reflection in the test, explicit in
  production. That test also asserts no service type is registered twice. ValidateOnBuild alone
  would NOT catch a missing handler registration; do not weaken it to a container-build check.

Slice 4 — JWT authentication (D60), fifth migration AddRefreshTokens.
  Access tokens (15 min) + persisted refresh tokens (7 days), both configurable. Refresh tokens
  are stored ONLY as a SHA-256 hash, rotated on every use, and reuse of an already-revoked
  token revokes the entire chain for that user. Retention is until ExpiresAt — revoked-but-
  unexpired rows MUST be kept, because they are what makes reuse detection work. No cleanup
  job and no logout endpoint, both deliberate. SRS FR-10.3 lockout is implemented explicitly
  via IsLockedOutAsync/AccessFailedAsync/ResetAccessFailedCountAsync, because
  UserManager.CheckPasswordAsync does not touch lockout counters and AddIdentityCore does not
  register SignInManager — removing those calls silently removes a documented security
  requirement. A deactivated user is rejected at login AND at refresh. Every login failure
  returns an IDENTICAL 401 (unknown email, wrong password, inactive, locked out) to avoid
  account enumeration; do not make those messages more helpful. AUTHENTICATION IS DELIBERATELY
  OUTSIDE THE CQRS PIPELINE — AuthController calls UserManager and ITokenService directly,
  and ITokenService lives in Infrastructure, not Application. This is not an inconsistency to
  tidy up; read D60 before attempting to "fix" it.

Slice 5 — Public Lead creation (D61).
  POST /api/v1/leads, anonymous. CreateLeadRequest is narrower than CreateLeadCommand: Source
  and CreatedByUserId are server-derived, because Source gates the FR-9.2 Admin notification,
  so a caller controlling it could suppress that notification. Enums serialize as NAMES, not
  ordinals. Deliberately NOT idempotent — two identical submissions create two Leads. Note the
  enum converter is on MVC's JsonOptions only; IProblemDetailsService uses a separate
  Http.Json.JsonOptions that still has no converters (no ProblemDetails carries an enum today).

Slice 6 — Lead read endpoints.
  GET /leads/{id} goes through ILeadRepository + IOwnershipValidator (403, not 404) because
  Lead owns no children so hydration costs nothing; GET /leads uses ILeadQueries with a
  projection, pagination, and a WHERE clause. A collection cannot be ownership-checked after
  loading — reads split by shape, not by symmetry.
  *** THE MOST IMPORTANT LESSON IN PHASE 4: a fail-open authorization defect was found and
  fixed here. NEVER interpret "not Inspector" as "Admin". Unrestricted access must only ever
  be reached by POSITIVELY establishing the Admin role. The original helper returned "unscoped"
  for anyone who merely was not an Inspector, which would have granted every Lead to a broken
  role-claim mapping, a role-less account, a role-name typo, or any future third role. The fix
  checks Inspector FIRST (so a dual-role account is scoped, not unrestricted), then Admin
  explicitly, then refuses. The vulnerability was reproduced before being fixed. Do not
  simplify that helper back into a single negated check. ***
  Role-claim mapping is now actually tested; before this slice nothing proved it worked, and
  that failure mode is silent. Paging limits live in Application.Common.Pagination; list
  queries order deterministically with a tiebreaker before Skip/Take.

Slice 7 — Admin-only Inspection scheduling (D62).
  POST /api/v1/leads/{leadId}/inspections, on InspectionsController with an absolute route so
  all Inspection behaviour stays in one file. D61's wording was CORRECTED here: only values
  describing the CALLER are server-derived. An Admin-selected InspectorId is legitimate request
  input — deriving it from the token would make it impossible to schedule anyone but oneself.
  New IUserQueries.IsActiveInspectorAsync rejects a nonexistent, non-Inspector, or DEACTIVATED
  assignee BEFORE any mutation; previously the FK caught only the first case and surfaced it as
  a 500. Keep that check atomic — one boolean, not three separate checks. Role names are now
  constants on IdentityRoleSeeder, forwarded by the API's Roles class.

Slice 8 — Inspection photo upload + real LocalDiskFileStorage.
  POST /api/v1/inspections/{id}/photos, INSPECTOR ONLY — an Admin gets 403, inverting Slice 7,
  because PermissionMatrix §2 keeps the evidence chain with whoever was on site.
  ORDERING: every validation, ownership, and Domain rejection happens BEFORE the filesystem
  write. This was proven by reproduction — reordering the handler made a test fail with
  "expected 0 files, actual 1". Do not reorder it.
  COMPENSATION: if the database commit fails after a successful write, the file is deleted
  best-effort and the ORIGINAL commit exception is rethrown; a failure of the delete is logged
  and swallowed. THIS IS COMPENSATION, NOT ATOMICITY — process death can still leave an
  orphan. Never document or extend it as a consistency guarantee.
  LocalDiskFileStorage independently enforces root containment (canonicalize, prove the
  destination stays under the root, compare against root+separator so a sibling sharing the
  prefix cannot pass) and refuses to overwrite an existing file. A caller's filename cannot
  influence directory structure: keys are inspection id + GUID + sanitized extension, verified
  empirically.
  EXTENSION VALIDATION is platform-independent by design: an empty extension is allowed;
  otherwise "." plus 1–31 ASCII alphanumerics, total max 32. This is a DEFENSIVE FILESYSTEM
  RULE, NOT A FILE-TYPE ALLOWLIST — no document restricts formats. Path.GetInvalidFileNameChars()
  was deliberately rejected: it returns 41 characters on Windows and 2 on Linux, so validation
  built on it would behave differently per deployment OS and per CI job.
  IFileStorage.DeleteAsync was added on demand for compensation. GetAsync still does not exist.
  File.Delete's missing-DIRECTORY behaviour (it throws, unlike a missing file) was found by a
  test and is handled, so DeleteAsync's documented idempotency is actually true.
  Microsoft.Extensions.Logging.Abstractions is now an accepted Application dependency for this
  orchestration concern; hosting, configuration, EF, and filesystem dependencies remain
  forbidden there.

YOUR FIRST TASK: SLICE 9 — INSPECTION COMPLETION — DESIGN REVIEW ONLY.

DO NOT IMPLEMENT SLICE 9. Produce a design review, present it, and wait for explicit approval.
The user reviews and challenges designs before any code is written; that process has caught
several real defects in this phase and is not a formality.

Start by reading the code that already exists — CompleteInspectionCommand, its validator and
handler, Inspection.Complete(), Lead.MarkInspectionDone(), and PermissionMatrix.md §2 — before
proposing anything. The endpoint contract should be driven by the existing use case, not the
reverse.

The specific questions this design review must answer:

- Atomicity across the Inspection and Lead mutations: completion touches two aggregates.
  What is actually guaranteed, and by what mechanism?
- Exact guard ordering: which guard runs first, and can one aggregate be mutated in memory
  before a guard on the second throws? If so, does that matter given the scoped DbContext?
- Inspector ownership and JWT-derived caller identity (this is the "who is acting" case, so
  the inspector id is server-derived — contrast Slice 7).
- Lead/Inspection state consistency: what happens if the Lead is not in the state
  MarkInspectionDone expects while the Inspection is completable, or vice versa.
- Repeated completion: what should a second completion attempt return, and which guard
  produces it.
- Whether photos are actually required before completion — DO NOT INVENT THIS RULE. Check the
  documents; if no rule exists, say so and do not create one.
- HTTP response contract and status codes.
- Audit target: §10's rule is that the audit entry goes against the aggregate the business
  cares about, which for completion is likely Lead rather than Inspection — verify against the
  existing handler rather than assuming.
- Cross-aggregate persistence tests: what must be asserted against the database, not just the
  response.

WORKING RULES — NOT OPTIONAL:

- Process, every slice: design review → challenge assumptions → explicit approval →
  implementation → adversarial verification → documentation in the same commit → commit.
  Never implement first and explain after.
- ADVERSARIAL VERIFICATION IS EXPECTED. This project's standard is to prove a safeguard works
  by breaking it: temporarily remove a registration, reorder a call, weaken an attribute, and
  confirm the test actually fails — then restore. Several real defects were caught this way.
  A green test that has never been shown to fail proves little.
- Never push, never merge, never open a PR without explicit permission. Never commit to main.
  Never force-push (D5 records the incident behind this).
- Verify claims against the repository rather than trusting prose, including this file's.
- Grow interfaces, DTOs, repositories, and schema strictly on demand. No speculative
  abstractions.
- Before generating any migration: three-way review (Domain ↔ EF configuration ↔ ERD.md),
  then manually review the generated migration, then confirm no pending model changes.
- When a design review reveals a documentation gap or contradiction, fix the documentation in
  the same commit as the code that depends on it.
- Do not add AGENTS.md to any commit.
- If something in the documents turns out to be false, say so plainly before working around it.

CONFIRM YOU HAVE COMPLETED STEPS 1–7, then present the Slice 9 design review. Do not write
implementation code until the design is approved.
```
