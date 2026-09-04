# PHASE11_PROGRESS.md — Customer-Facing Workflow

**Phase:** 11 — Customer-Facing Workflow (email → magic token → customer quote page → Accept/Decline → Dashboard status).
**Branch:** `claude/customer-workflow-phase-rvh4rj`, off `main` at `a5124ca` (the Phase 10 merge).
**Started:** 2026-09-04.

**Relationship to `PROJECT_ROADMAP.md`.** This phase delivers the **token-link half of roadmap Phase 13** — Wireframe A3, the customer's Angebot decision page — and nothing else from it. A1/A2 (marketing home and the public contact form) are **deferred to Phase 13 by explicit Product-Owner decision (Q6)**, so this phase does not touch the anonymous Lead-creation flow. The numbering follows Phase 10's precedent, which absorbed roadmap Phases 11–12 for the same reason: the work the product actually needed next did not line up with the roadmap's ordering.

---

## 1. What Already Existed Before This Phase

**The backend for this workflow was built and merged in Phase 6 and Phase 9.** The assessment that opened this phase found that the entire chain `Send → TokenLink → anonymous read → Accept/Decline → Angebot + Lead transition → Admin notification` exists, is covered by tests, and was exercised in Phase 10 QA. **Nothing in §1 is to be rebuilt.**

| Element | Where | State |
|---|---|---|
| `TokenLink` aggregate (`Create`/`IsExpired`/`MarkUsed`, BR-4) | `Domain/Entities/TokenLink.cs` | Built, Phase 6 |
| `Angebot.Send` / `RecordCustomerApproval` / `RecordCustomerRejection` | `Domain/Entities/Angebot.cs` | Built, Phase 1/6 |
| `SendAngebotCommand` (three writes, one commit) | `Application/Angebote/Commands/SendAngebot` | Built, Phase 6 |
| `GetPublicAngebotByTokenQuery` + `PublicAngebotDto` | `Application/Angebote/Queries/GetPublicAngebotByToken` | Built, Phase 6 |
| `RecordAngebotDecisionCommand` | `Application/Angebote/Commands/RecordAngebotDecision` | Built, Phase 6 |
| `GET`/`POST /api/v1/public/angebote/{token}` + rate limiting (D65) | `Api/Controllers/PublicController.cs` | Built, Phase 6 |
| `TokenLinkService` (32-byte CSPRNG, base64url, configurable lifetime) | `Infrastructure/TokenLinks` | Built, Phase 6 |
| SMTP transport, six frozen German templates incl. `CreateAngebotReady` | `Infrastructure/Email` | Built, Phase 9 |
| `NotificationDeliveries` + Admin retry | `Infrastructure/Email`, `Api/Controllers` | Built, Phase 9 |
| Dashboard send action and post-decision status | `Dashboard/features/angebote` | Built, Phase 10 |

**What was missing:** the customer-facing page (`RenoTrack.Website` is still the untouched Razor scaffold), a configured mailbox, and the four decisions below.

---

## 2. Approved Design Decisions (agreed before any code was written)

Approved by the Product Owner on 2026-09-04, in answer to the assessment's open questions.

| # | Question | Decision |
|---|---|---|
| **Q1** | Customer page architecture | **Razor Pages in `RenoTrack.Website`, server-side `HttpClient` to the existing API.** No browser-JS-to-API SPA flow for the customer page. `ForwardedHeaders` **only** with an explicit trusted-proxy allowlist — arbitrary `X-Forwarded-For` is never trusted. An ADR records the trust boundary and how it amends **D65**. |
| **Q2** | Rejection reason | **Yes** — optional `Angebot.DecisionReason`, `nvarchar(1000)` NULL, wired Domain → Application → persistence → API → customer UI. Optional. **Never** into `AuditLog`. A recorded decision can never be modified afterwards. |
| **Q3** | Token re-issue | **Yes** — Admin-only `POST /api/v1/angebote/{id}/resend`. Only while `Sent`; never after `CustomerApproved`/`CustomerRejected`. The old token is invalidated **in the same transaction** that creates the new one, so there is never more than one live credential per Angebot. The new email carries only the new token. Reuses the existing notification architecture — no second email mechanism. |
| **Q4** | Token storage | **Keep `TokenLink.Token` plaintext.** A deliberate operational decision for diagnosis and support; documented explicitly. No hashing in this phase. |
| **Q5** | Development email | **Yes** — a development-only SMTP sink so the real Send → Email → link flow is testable end to end. No token logging. No production endpoint that exposes tokens. No weakening of the production security model. |
| **Q6** | Contact form | **Deferred to Phase 13.** This phase does not expand into the public lead/contact workflow. |
| **Q7** | Company identity | **Invent nothing** — no company name, address, legal text, logo identity, `Reply-To`, or Impressum content. The Website is built so identity and legal content arrive through configuration/content structure; real content is supplied before the completion gate. |
| **Q8** | Customer language | **German only** for this phase. |

**Ordering.** The Product Owner explicitly approved doing Slice 1 (the concurrency defect) **before** any customer UI, on the grounds that it is a real correctness defect and the UI is what will start generating the concurrent traffic that triggers it.

---

## 3. Slices

| # | Slice | Status |
|---|---|---|
| **1** | TokenLink concurrency protection + deterministic 409 for the losing concurrent decision + repeated race testing | ✅ **complete, pending CI verification** |
| 2 | Customer page skeleton — Razor route, server-side API client, the four states, security headers | not started |
| 3 | Render the quote (Wireframe A3) | not started |
| 4 | Accept / Decline | not started |
| 5 | Rejection reason (Q2) — migration #12 | not started |
| 6 | Token re-issue (Q3) | not started |
| 7 | Legal pages and company-identity structure (Q7) | not started |
| 8 | Completion gate — end-to-end run against the development SMTP sink, browser QA, documentation reconciliation | not started |

---

## 4. Slice 1 — TokenLink Concurrency Protection

### 4.1 The defect

`RecordAngebotDecisionCommandHandler` reads the `TokenLink`, checks `UsedAt`, mutates `TokenLink`/`Angebot`/`Lead`, and commits all three in one `SaveChangesAsync`. **Nothing made that read-then-write atomic across requests.** `TokenLinks` carried no concurrency token; only `RefreshToken.RevokedAt` did.

Two simultaneous decisions on the same link therefore both read `UsedAt` as null, both passed `MarkUsed()`'s in-memory guard, and **both committed** against separate request-scoped `DbContext` instances. The link was consumed twice, two audit rows and two Admin emails were produced, and — because `Angebot` and `Lead` carry no token either and the two transactions wrote them independently — an Approve racing a Reject could leave `Angebot = CustomerApproved` with `Lead = Lost`. That is the exact pair of rows `StateMachine.md` §5 exists to keep in agreement.

**Found by reading the code during the Phase 11 assessment, not by a failing test.** All twenty-two of Phase 6's public-endpoint tests drove the endpoint sequentially, where the aggregate's own guard is sufficient.

### 4.2 The fix (`ARCHITECTURE_DECISIONS.md` D96)

1. **`TokenLinks.UsedAt` is an EF Core optimistic-concurrency token.** The decision `UPDATE` becomes `… WHERE Id = @id AND UsedAt IS NULL`. The loser matches no row, EF Core throws `DbUpdateConcurrencyException`, and **its entire batch rolls back** — so the `Angebot` and `Lead` writes it had queued never land either. That batch-level atomicity is why a token on this one column is sufficient for all three aggregates, and why none was added to `Angebot` or `Lead`.
2. **`UnitOfWork.SaveChangesAsync` translates `DbUpdateConcurrencyException` into `ConflictException`**, which the API already maps to 409 — so no new arm was added to `ProblemDetailsExceptionHandler`. `ConflictException` gained an inner-exception overload so the real cause survives D59's Warning-with-stack-trace logging.
3. **Migration #11 `AddTokenLinkConcurrencyToken`**, with empty `Up`/`Down`. A non-`rowversion` concurrency token is client-side `WHERE`-clause behaviour, so there is no DDL; the migration exists because the model snapshot records `.IsConcurrencyToken()` and `has-pending-model-changes` must be clean. **Migration count: ten → eleven.**

**No Domain, Application, API or Dashboard code changed.** The handler, the endpoint and `MarkUsed()` were all already correct; what was missing was purely the database-level guard.

### 4.3 Why the 409 is deterministic

Whichever way the two requests interleave, the caller sees 409:

- **Genuine race** — the loser's `UPDATE` matches no row → `DbUpdateConcurrencyException` → `ConflictException` → 409.
- **Serialized** — the second request reloads a consumed link → `MarkUsed()` throws `InvalidOperationException` → 409 (CLAUDE.md §22).

The tests assert that contract rather than which guard fired, which is why they have no flaky branch to tolerate: a run where the host happens to serialize the two requests is still a valid run.

### 4.4 Files changed

| File | Change |
|---|---|
| `src/RenoTrack.Infrastructure/Persistence/Configurations/TokenLinkConfiguration.cs` | `UsedAt` → `.IsConcurrencyToken()` |
| `src/RenoTrack.Infrastructure/Persistence/UnitOfWork.cs` | `DbUpdateConcurrencyException` → `ConflictException` |
| `src/RenoTrack.Application/Common/Exceptions/ConflictException.cs` | Inner-exception overload |
| `src/RenoTrack.Infrastructure/Persistence/Migrations/20260904113000_AddTokenLinkConcurrencyToken{,.Designer}.cs` | New, empty `Up`/`Down` |
| `src/RenoTrack.Infrastructure/Persistence/Migrations/RenoTrackDbContextModelSnapshot.cs` | `.IsConcurrencyToken()` on `TokenLink.UsedAt` |
| `tests/RenoTrack.Infrastructure.Tests/Persistence/TokenLinkPersistenceTests.cs` | +6 tests (1 deterministic interleaving, 5 repeated real races) |
| `tests/RenoTrack.Infrastructure.Tests/Persistence/UnitOfWorkTests.cs` | +3 tests (translation, message discloses nothing, constraint violation not translated) |
| `tests/RenoTrack.Api.Tests/Public/PublicAngebotDecisionEndpointTests.cs` | +3 repeated end-to-end races with opposing decisions |
| `ARCHITECTURE_DECISIONS.md` | D96 + three rejected alternatives |
| `CLAUDE.md` | §14, §17, §21 |
| `BusinessRules.md` | BR-4 + changelog row |

**Ten new tests.** Repetition is not decoration: **D55** is the precedent — a race proof that passed when first written then failed about two runs in three once it was actually repeated.

### 4.5 Verification status — read this before merging

**This slice was implemented in an environment with no .NET SDK.** `builds.dotnet.microsoft.com` is blocked by egress policy there, and `RenoTrack.Infrastructure.Tests`/`RenoTrack.Api.Tests` require Windows SQL Server LocalDB (**D40**, **D56**), which that Linux container could not provide in any case. **`dotnet build`, `dotnet test` and `dotnet ef` were therefore not run locally.**

CI is the gate: `.github/workflows/ci.yml` runs on `pull_request` and covers all four test projects, including the `windows-latest` LocalDB job. **Three things must be confirmed green before merge, and none of them has been confirmed yet:**

1. `dotnet build RenoTrack.slnx` → 0 warnings, 0 errors (`TreatWarningsAsErrors` is on solution-wide).
2. All four test projects passing — in particular the ten new tests, and the repeated race cases run more than once (§14).
3. **`dotnet ef migrations has-pending-model-changes` → clean.** Migration #11's `.cs`, `.Designer.cs` and the snapshot edit were **hand-produced** because `dotnet ef` was unavailable. The designer file is a byte-for-byte copy of the updated snapshot with the migration header substituted (verified by `diff`), which is exactly what the tool emits — but it has not been machine-verified, and this check is the thing that would catch it if it were wrong.

If (3) reports a pending change, the fix is to delete the three hand-produced artefacts and regenerate with `dotnet ef migrations add AddTokenLinkConcurrencyToken` — never to hand-patch them further (**CLAUDE.md** §21).
