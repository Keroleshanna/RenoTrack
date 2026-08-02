# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system —
public website + admin/inspector dashboard), an existing, actively-developed project. This is
not a new project and not a fresh start. Phases 0–3 are merged to main. **Phase 4 is COMPLETE —
all 11 slices — on a feature branch, fully green, and awaiting review and merge.** Nothing is
pushed. A prior conversation ended for context reasons and persisted everything into the
repository, so you depend on the files, not on any chat history. Do not treat anything below as
optional reading.

CURRENT STATE AT A GLANCE — verify every line yourself in steps 1–6. It was accurate when
written; the repository is authoritative if anything disagrees.

- Branch: feature/phase-4-api-auth-leads-inspections
- HEAD: the Phase 4 closeout-fix commit ("docs/test: close Phase 4 review findings"), which sits
  on top of b05fb34 ("feat(infrastructure): apply migrations by deployment step, verify at
  startup (Phase 4 Slice 11)"). `git log` is authoritative.
- origin/main: babfff9. The branch is 17 commits ahead, 0 behind. NOTHING IS PUSHED — the branch
  does not exist on the remote — and NO PULL REQUEST HAS BEEN OPENED.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests: 549 passing, 0 failing — 153 Domain, 165 Application, 101 Infrastructure, 130 Api.
- Migrations: 5 (InitialCreate, AddAuditLog, AddNumberSequence, AddIdentity, AddRefreshTokens).
  Only AddRefreshTokens was added by Phase 4; the four Phase 3 migrations are untouched.
  `dotnet ef migrations has-pending-model-changes` reports no pending changes.
- Working tree: clean.

YOUR TASK: PHASE 4 REVIEW AND PULL REQUEST. **Not implementation. Not Phase 5.**

A full closeout review has already been performed and its findings applied. It found **no
Must-Fix items** and six Should-Fix items (a test assertion plus documentation currency), all of
which are now closed. The remaining work is the user's own review, then — only with explicit
permission — pushing the branch and opening the PR.

Do not start Phase 5. Do not push, merge, or open a PR without explicit permission.

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Read CLAUDE.md in full — the permanent engineering rules. §22 (API layer) is the newest and
   largest section; every rule there is settled convention, not a suggestion.

2. Read PROJECT_STATE.md in full — what exists right now, layer by layer.

3. Read NEXT_STEPS.md in full — especially §5a (every open item carried out of Phase 4, with
   deliberate deferrals separated from forgotten work), §3 ("What Should NOT Be Changed") and
   §4 ("Decisions Considered Final").

4. Read PHASE4_PROGRESS.md in full — the slice-by-slice narrative of all 11 slices: what was
   designed, what was challenged and changed under review, what was proven by reproduction, and
   what was deliberately not built. This is the densest source of Phase 4 context.

5. Read ARCHITECTURE_DECISIONS.md, at minimum D57–D63 (Phase 4's decisions) plus the "Decisions
   Explicitly Rejected" table at the end. Several entries record real defects found and fixed —
   read those carefully so you do not reintroduce them. D61 carries an explicit self-correction
   made in Slice 7.

6. Run these yourself and confirm they match the figures above:
     dotnet build RenoTrack.slnx
     dotnet test RenoTrack.slnx
     dotnet ef migrations has-pending-model-changes --project src/RenoTrack.Infrastructure --startup-project src/RenoTrack.Infrastructure
     git status && git log --oneline origin/main..HEAD && git fetch origin
   RenoTrack.Infrastructure.Tests and RenoTrack.Api.Tests both need real SQL Server LocalDB
   (`sqllocaldb info` should list MSSQLLocalDB, State: Running). If it is unavailable, say so
   before writing code — that is information, not an obstacle to work around with a weaker
   substitute. Note this machine has repeatedly produced an orphaned sqlservr.exe that holds the
   MSSQLLocalDB instance while `sqllocaldb info` reports it Stopped; `start` then fails with
   Windows error 575. The fix is to terminate that one orphaned PID — ask first, and never touch
   any other sqlservr.exe process.

WHAT PHASE 4 DELIVERED (all 11 slices, committed, none pushed)

  1  API foundation, conventions, docs — api/v1 by literal URL segment, no versioning library
     (D57). [Authorize] on controllers by default, [AllowAnonymous] per action. Api.Tests boots
     the real app via WebApplicationFactory<Program> against real LocalDB, schema via
     MigrateAsync — deliberately NOT EnsureCreated, unlike Infrastructure.Tests; the two fixtures
     must not be "unified" (D58). Scalar serves the OpenAPI document. Api.Tests runs in CI's
     Windows job, never Linux.
  2  Global RFC 7807 exception handling (D59) — ONE IExceptionHandler with a single explicit
     switch. NotFoundException→404, ForbiddenException→403, ConflictException→409,
     ValidationException→400 (field-keyed errors), ArgumentException→400,
     InvalidOperationException→409, everything else→500. Mapped exceptions surface their message;
     unmapped ones deliberately do not. The ArgumentException/InvalidOperationException mapping
     is a KNOWINGLY ACCEPTED RISK mitigated by logging every mapped exception at Warning WITH ITS
     FULL STACK TRACE — do not remove that logging. traceId lives in CustomizeProblemDetails.
  3  AddApplication() DI composition root — every registration explicit, no scanning, no Scrutor.
     Handlers registered BY INTERFACE. The "forgot to register" risk is covered by a
     reflection-based test in Api.Tests. That test has now caught two later slices' mistakes
     (TokenService in Slice 4, DatabaseInitializer in Slice 11) — do not weaken it.
  4  JWT authentication (D60) — 15-min access tokens, persisted refresh tokens stored ONLY as
     SHA-256 hashes, rotated every use, whole-chain revocation on reuse. Retention until
     ExpiresAt; revoked-but-unexpired rows MUST be kept or reuse detection breaks. No cleanup job
     and no logout endpoint, both deliberate. FR-10.3 lockout wired explicitly. Every login
     failure returns an IDENTICAL 401. AUTHENTICATION IS DELIBERATELY OUTSIDE CQRS — read D60
     before "fixing" it.
  5  Public Lead creation (D61) — POST /api/v1/leads, anonymous. Source and CreatedByUserId are
     server-derived, because Source gates the FR-9.2 Admin notification. Enums serialize as
     NAMES. Deliberately NOT idempotent.
  6  Lead read endpoints. *** THE MOST IMPORTANT LESSON IN PHASE 4: a fail-open authorization
     defect was found and fixed here. NEVER interpret "not Inspector" as "Admin". Unrestricted
     access must only ever be reached by POSITIVELY establishing the Admin role. The vulnerability
     was reproduced before being fixed. Do not simplify that helper back into a single negated
     check. *** Paging limits live in Application.Common.Pagination; list queries order
     deterministically with a tiebreaker before Skip/Take.
  7  Admin-only Inspection scheduling (D62) — D61's wording was CORRECTED here: only values
     describing the CALLER are server-derived. An Admin-selected InspectorId is legitimate input.
     IUserQueries.IsActiveInspectorAsync rejects a nonexistent, non-Inspector, or DEACTIVATED
     assignee BEFORE any mutation. Keep that check atomic — one boolean, not three.
  8  Inspection photo upload + real LocalDiskFileStorage — Inspector only. ORDERING: every
     validation, ownership, and Domain rejection happens BEFORE the filesystem write, proven by
     reproduction. COMPENSATION, NOT ATOMICITY: a failed commit triggers a best-effort delete and
     rethrows the ORIGINAL exception; process death can still leave an orphan. Never document or
     extend it as a consistency guarantee. Extension validation is a CHARACTER-CLASS rule, not a
     file-type allowlist.
  9  Inspection completion — 200 + InspectionDto. The first endpoint needing NO request record at
     all (D61's subset is empty) and no Application-layer change. Genuinely ATOMIC across
     Inspection and Lead: both repositories and UnitOfWork share the one request-scoped DbContext,
     so a single SaveChangesAsync writes both. THE AUDIT ROW IS DELIBERATELY OUTSIDE that
     guarantee (D50). Guard ordering is Inspection-first for error quality; do not reorder.
 10  Inspection notes (PATCH /api/v1/inspections/{id}) — **this slice was redefined during design
     review.** It was planned as "Lead status update (Won/Lost)", which did not survive contact
     with the repository: Lead.MarkAngebotSent() is called by nothing, so AngebotSent is
     unreachable and such an endpoint would 409 for every Lead; and StateMachine §5 states Lead
     reaches Won only inside the Angebot decision handler's transaction. LEAD WON/LOST IS PHASE 6
     WORK — do not create Admin MarkWon/MarkLost commands or endpoints. The slice also reconciled
     a real contradiction: Architecture §5.2's obsolete PATCH /leads/{id}/status was removed and
     PermissionMatrix §1's self-contradictory row corrected.
 11  Migration application / database bootstrap (D63) — closed a real, reproduced defect: nothing
     in src/ had ever applied a migration, so a fresh production database died with "Invalid
     object name 'AspNetRoles'". Production now applies migrations by an explicit deployment step
     (EF bundle primary, idempotent SQL script supported) and startup only VERIFIES — read-only,
     so the runtime login needs no DDL permission. Database:Mode is Verify (default when absent)
     or Migrate (Development opt-in, HARD-REFUSED in Production). Verification checks migration
     history IN BOTH DIRECTIONS plus required roles. Role seeding moved out of normal startup;
     IdentityRoleSeeder itself unchanged. NO USER IS PROVISIONED IN ANY ENVIRONMENT.

TWO CROSS-CUTTING FINDINGS THAT MUST STAY VISIBLE

- AuditService calls SaveChangesAsync on the SAME scoped RenoTrackDbContext. Its intended usage is
  after the primary business commit. If called while unrelated tracked changes are pending, its
  SaveChangesAsync will flush those too. This is not transaction isolation, and callers must not
  rely on it to isolate pending changes. (Found empirically in Slice 9, where it masked a
  deliberately-introduced defect.)
- A status-code-only authorization test can be a FALSE POSITIVE when both the API role gate and
  the Application ownership guard produce 403. Where that distinction matters, assert the response
  body is empty (role gate) versus a ProblemDetails document (ownership). All three affected
  endpoints now do this.

WORKING RULES — NOT OPTIONAL

- Process, every slice: design review → challenge assumptions → explicit approval →
  implementation → adversarial verification → documentation in the same commit → commit.
  Never implement first and explain after.
- ADVERSARIAL VERIFICATION IS EXPECTED: prove a safeguard works by breaking it, confirm the test
  actually fails, then restore. Several real defects were caught this way. A green test that has
  never been shown to fail proves little.
- Never push, never merge, never open a PR without explicit permission. Never commit to main.
  Never force-push (D5 records the incident behind this).
- Verify claims against the repository rather than trusting prose, including this file's.
- Grow interfaces, DTOs, repositories, and schema strictly on demand. No speculative abstractions.
- Do not invent business rules. If the documents do not state one, say so rather than creating it.
- When a design review reveals a documentation gap or contradiction, fix the documentation in the
  same commit as the code that depends on it.
- If something in the documents turns out to be false, say so plainly before working around it.

KNOWN OPEN ITEMS (all deliberate — see NEXT_STEPS.md §5a for the full list with reasons)

Rate limiting on the public Lead endpoint and CORS (hardening slice); production user provisioning
(blocked on SRS OQ-1 — login works but nobody can log in on a fresh database); GET
/api/v1/inspections/{id}; authenticated photo serving plus IFileStorage.GetAsync (photos can be
stored but not served); orphaned files remain possible; no refresh-token cleanup job; Lead
Won/Lost deferred to Phase 6; deployment pipeline documented but not built; Roles.cs
folder/namespace mismatch (cosmetic).

CONFIRM YOU HAVE COMPLETED STEPS 1–6, then report the branch's readiness and wait for the user's
direction on the pull request. Do not begin Phase 5.
```
