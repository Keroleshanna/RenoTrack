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
| **1** | TokenLink concurrency protection + deterministic 409 for the losing concurrent decision + repeated race testing | ✅ **complete and merged** — PR [#18](https://github.com/Keroleshanna/RenoTrack/pull/18), merge commit `78a8406`; build verified locally (0 errors, 0 warnings), tests blocked by the environment, see §4.5 |
| **2** | Customer page skeleton — Razor route, server-side API client, the four states, security headers | ✅ **complete and merged** — PR [#18](https://github.com/Keroleshanna/RenoTrack/pull/18), merge commit `78a8406`, CI green on both jobs |
| **3** | Render the quote (Wireframe A3) **and its decision state** | ✅ **complete and merged** — PR [#19](https://github.com/Keroleshanna/RenoTrack/pull/19), merge commit `450ebbd`; CI green on both jobs (1,838/1,838), browser QA passed (§6.13) |
| **4** | Accept / Decline | ✅ **complete and merged** — PR [#21](https://github.com/Keroleshanna/RenoTrack/pull/21), merge commit `022cf7c`; CI green on both jobs, browser QA passed (§7.10) |
| **5** | Rejection reason (Q2) — migration #12 | ✅ **complete and merged** — PR [#22](https://github.com/Keroleshanna/RenoTrack/pull/22), merge commit `5514b17`; CI green on both jobs including Windows/LocalDB |
| **6** | Token re-issue (Q3) — migration #13 (empty `Up`/`Down`) | ✅ **complete and merged** — PR [#23](https://github.com/Keroleshanna/RenoTrack/pull/23), merge commit `314f486`; CI green on both jobs including Windows/LocalDB, browser QA passed (§9.6, §9.7), **D99** |
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

### 4.6 Verification outcome (Product Owner, 2026-09-04)

- `dotnet build` — **passed, 0 errors, 0 warnings.**
- `dotnet test` — **blocked by the environment, not by the code.** Windows Code Integrity / Application Control refuses to load `RenoTrack.Application.dll` (`0x800711C7`, Event 3077, policy `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`). This is the same class of failure `PROJECT_STATE.md` §3 already records as an environment caveat, and it breaks test discovery rather than reporting failures.
- `dotnet ef migrations has-pending-model-changes` — **inconclusive**, blocked by the same assembly-load refusal at design time.

**Accepted by explicit decision**, with Slice 1 left exactly as written: no change to the concurrency implementation, no change to migration #11, and no project-side workaround for a host security policy. **The migration-artefact check in §4.5 therefore remains outstanding** and falls to CI or a later local run.

---

## 5. Slice 2 — Customer Website Skeleton

### 5.1 What it delivers

The route the customer's email has pointed at since Phase 6. `EmailMessageFactory.CreateAngebotReady` composes `{TokenLink:PublicBaseUrl}/angebot/{token}` (D4.1) and `GET /api/v1/public/angebote/{token}` has answered since Phase 6 — but `RenoTrack.Website` was still the untouched Razor scaffold, so that link resolved to a 404. It now resolves to a page.

**Server-rendered, per Q1** (`ARCHITECTURE_DECISIONS.md` **D97**): Razor Pages, a typed `HttpClient` to the API on the server, no browser-JS-to-API flow, and no script element on a customer page at all.

| Concern | How |
|---|---|
| Token entry route | `@page "/angebot/{token}"` on `Pages/Angebot.cshtml`, matching the emailed URL exactly |
| Backend boundary | `IPublicAngebotClient` → `PublicAngebotClient`, a typed client; the Website references no backend project and mirrors the API's JSON contract in its own types |
| Error handling | Four outcomes — `Available` (200), `NotFound` (404), `Expired` (410), `Unavailable` (503). Nothing the API says crosses the boundary |
| Layout | `_CustomerLayout`, separate from the marketing `_Layout`: German, no navigation, responsive, print- and dark-mode aware |
| Token security | Never rendered, never logged, escaped into the outgoing path, `no-store` / `noindex` / `no-referrer` on token routes |
| Configuration | `PublicApi:BaseUrl` (required, absolute HTTPS, fails startup), `PublicApi:TimeoutSeconds` (defaults to 10), `CompanyIdentity:*` (optional content), `TrustedForwarders:*` (empty = trust nothing) |

### 5.2 The one thing beyond the eight stated goals, and why

**`X-Forwarded-For` handling on both the Website and the API** (D97, amending **D65**). Not in the eight Slice 2 goals, but mandated by the approved **Q1** answer and, more importantly, made urgent *by this slice*: D65 partitions the public rate limiter per client IP and deliberately never read `X-Forwarded-For` for want of a trust boundary. Once the Website calls the API on the customer's behalf, **every customer would share one 30-per-minute bucket** — one busy afternoon, or one abusive visitor, throttles everyone else's quote. Introducing that regression and deferring the fix is exactly what the Phase 11 assessment warned against.

It is deliberately small and fails closed: **empty allowlist by default**, in which case `UseForwardedHeaders` is never registered and behaviour is byte-for-byte pre-D97. A malformed entry fails startup naming the key.

### 5.3 Design points settled while building

- **There is no "already used" state on the read path, and its absence is correct.** BR-4 makes a token single-use for *decisions* only and says outright that viewing remains allowed; `PermissionMatrix.md` §7 agrees, and `GetPublicAngebotByTokenQueryHandler` deliberately does not check `UsedAt`. A consumed link therefore reads exactly like an unconsumed one. Consumption matters on the decision surface, which is Slice 4's.
- **An outage is a distinct outcome from an invalid link**, answering 503 rather than 404, because conflating them tells a customer to abandon a link that is perfectly good.
- **HTTPS is required for `PublicApi:BaseUrl` in every environment, including Development.** The customer's token travels in that request's path, so `dotnet dev-certs https --trust` is a documented prerequisite rather than a plaintext escape hatch being added.
- **The Development API URL lives in `Properties/launchSettings.json`**, which is tracked, rather than `appsettings.Development.json`, which is gitignored — a value there would work on one machine and fail on every fresh clone.
- **A token-disclosure defect was found reviewing this slice's own diff, and fixed before commit.** `IHttpClientFactory` attaches logging handlers that write `Sending HTTP request GET {uri}` at Information, and every URI this Website requests contains the customer's token. They log under `System.Net.Http.HttpClient.*`, *outside* the `Microsoft.AspNetCore` category the site already pins to `Warning`, so at the `Default` level of Information a live credential would have reached every log sink on every page view. Removed structurally with `RemoveAllLoggers()` — a configuration change cannot reintroduce it — with the category pinned to `Warning` as well. **The general rule, now in `CLAUDE.md` §24: when a URL is a credential, enumerate every framework component that logs a URL, not just the obvious one.**

### 5.4 Files changed

**New — `src/RenoTrack.Website`:** `PublicApi/{PublicApiOptions, CustomerAngebot, IPublicAngebotClient, PublicAngebotClient, ClientAddressForwardingHandler}.cs`, `Security/{TrustedForwardersOptions, CustomerSecurityHeaders}.cs`, `Content/CompanyIdentityOptions.cs`, `Pages/Angebot.cshtml{,.cs}`, `Pages/Shared/_CustomerLayout.cshtml`, `wwwroot/css/customer.css`.
**Modified — Website:** `Program.cs`, `appsettings.json`, `Properties/launchSettings.json`, `RenoTrack.Website.csproj`.
**New — API:** `Security/TrustedForwardersOptions.cs`. **Modified — API:** `Program.cs`, `appsettings.json`.
**New — tests:** `tests/RenoTrack.Website.Tests/` (project, `CustomerWebsiteFactory`, `PublicApi/{StubHttpMessageHandler, PublicAngebotClientTests, PublicApiOptionsTests, ClientAddressForwardingHandlerTests}`, `Security/TrustedForwardersOptionsTests`, `Pages/{AngebotModelTests, AngebotPageTests}`).
**Modified:** `RenoTrack.slnx`, `.github/workflows/ci.yml`, `ARCHITECTURE_DECISIONS.md` (D97), `CLAUDE.md` (§24), `Architecture.md` (§2, §12), `PROJECT_STATE.md`, `NEXT_STEPS.md`.

**Eighty test cases**, all database-free and therefore running in CI's **Linux** job. Nothing was added to the Windows job.

### 5.5 Deliberately not in this slice

Angebot rendering and details (Slice 3), Accept/Decline and `DecisionReason` (Slices 4–5), link re-issue (Slice 6), the contact form (Phase 13), real company and legal content (Slice 7), and the development SMTP sink (Q5, folded into Slice 8's end-to-end run). `CustomerAngebot` carries one field because one field is what the skeleton renders — §7's growth-on-demand discipline, not an omission.

### 5.6 Verification status

**Unchanged from Slice 1: this environment has no .NET SDK** (`builds.dotnet.microsoft.com` is blocked by egress policy), so `dotnet build` and `dotnet test` were **not run** for Slice 2 either. Every file was reviewed by hand against the conventions above.

CI is the gate, and for this slice it is a genuinely strong one: **all eighty new test cases run in the Linux job**, which needs no LocalDB and is unaffected by the Windows Application Control policy that blocked local verification. A green `build-and-test` job therefore verifies essentially all of Slice 2.

Most likely places for a compile error, since nothing was compiled: the Razor pages and layout (not type-checked by inspection), `WebApplicationFactory<Program>` resolving the Website's internal `Program`, the framework's `IPNetwork` overload in `TrustedForwardersOptions.Build` (deliberately written as a target-typed `new` to survive either namespace), and `RemoveAll<IPublicAngebotClient>` in the test factory.

### 5.7 CI round 1 — build failed, one defect, fixed

**PR [#18](https://github.com/Keroleshanna/RenoTrack/pull/18), run [33876907649](https://github.com/Keroleshanna/RenoTrack/actions/runs/33876907649), commit `af3ce86`.**

| Job | Result |
|---|---|
| `build-and-test` (ubuntu-latest) | ❌ **failure** — `Build FAILED. 0 Warning(s), 4 Error(s)` |
| `database-backed-tests` (windows-latest) | ⏭️ **skipped** — gated on `needs: build-and-test` |

**No test executed.** `restore` succeeded for all ten projects; `Domain`, `Domain.Tests`, `Application`, `Application.Tests`, `Infrastructure` and `Infrastructure.Tests` all built. `RenoTrack.Website` and `RenoTrack.Api` failed, which stopped `Api.Tests` and `Website.Tests` from building at all.

**One defect, four occurrences:**

```
error ASPDEPR005: 'ForwardedHeadersOptions.KnownNetworks' is obsolete:
'Please use KnownIPNetworks instead.'
```

`Security/TrustedForwardersOptions.cs` in both the Website and the API, two sites each. `ForwardedHeadersOptions.KnownNetworks` is deprecated in .NET 10 in favour of `KnownIPNetworks`, which takes `System.Net.IPNetwork` rather than the older `Microsoft.AspNetCore.HttpOverrides` type — and `TreatWarningsAsErrors` (solution-wide, `Directory.Build.props`) correctly turns the obsoletion into a build error.

**The code carried a comment reasoning about exactly this area and reached the wrong conclusion.** It said a target-typed `new` would "keep this compiling either way instead of pinning a namespace that may move" — but the hazard was never the type's namespace, it was the *property* being deprecated, which no amount of target-typing addresses. The comment has been replaced with what is actually true.

**Fix:** `KnownIPNetworks` throughout, populated via `System.Net.IPNetwork.TryParse` (fully qualified, since `IPNetwork` also exists in the `Microsoft.AspNetCore.HttpOverrides` namespace this file imports for the `ForwardedHeaders` flags). **No suppression was added** — `WarningsNotAsErrors` still lists only `NU1903`.

~~**A behaviour improvement fell out of it…** `IPNetwork.TryParse` rejects it…~~ — **this claim was wrong and is corrected in §5.10.** `IPNetwork.TryParse` does *not* reject a non-canonical network; it normalises silently. The assertion added here failed in CI round 3, and the rejection is now enforced by application-level validation instead. The empty-string case added at the same time was correct and stands.

**Nothing else changed.** No production behaviour was altered beyond that stricter rejection, and no Slice 2 design decision was reopened.

### 5.8 CI round 2 — build green, 21 test failures, one defect, fixed

**Run [33877314864](https://github.com/Keroleshanna/RenoTrack/actions/runs/33877314864), commit `d83ac5b`.**

| Job | Result |
|---|---|
| `build-and-test` (ubuntu-latest) | ❌ **failure** — build **succeeded**, `RenoTrack.Website.Tests` failed |
| `database-backed-tests` (windows-latest) | ⏭️ **skipped** — still gated on `needs: build-and-test` |

`Failed! - Failed: 21, Passed: 59, Skipped: 0, Total: 80, Duration: 681 ms — RenoTrack.Website.Tests.dll`

The round-1 fix worked: **0 errors, 0 warnings**, and `Domain.Tests` and `Application.Tests` both ran and passed before `Website.Tests` was reached (the step is one `bash -e` script, so a failure in either would have stopped it). All 21 failures were `AngebotPageTests` — every test that boots the host — and all 59 non-host tests passed.

**One defect, and it was a real production bug rather than a test-harness problem:**

```
System.InvalidOperationException : Configuration value 'Defence in depth alongside
Program.cs's RemoveAllLoggers(). …' is not supported.
   at Microsoft.Extensions.Logging.LoggerFilterConfigureOptions.TryGetSwitch(String value, LogLevel& level)
   …
   at Program.<Main>$(…) in src/RenoTrack.Website/Program.cs:line 52
```

Slice 2 added `"//Microsoft.AspNetCore"` and `"//System.Net.Http.HttpClient"` explanatory keys **inside `Logging:LogLevel`**. That section is enumerated as category-to-level pairs, so **every** key under it must parse as a `LogLevel`; a `"//"` comment key throws at `builder.Build()`. **The Website would not have started in any environment** — this was never merely a test failure.

**The `"//"` convention is used correctly elsewhere in this repository** — `RateLimiting`, `TokenLink`, `Email`, `Database`, and Slice 2's own `PublicApi`, `CompanyIdentity` and `TrustedForwarders` — because all of those are bound with `.Get<T>()`, which ignores unknown keys. That is exactly why it read as universally safe. **The rule is now in `CLAUDE.md` §24.**

**Fix:** the notes moved to a `"//Logging"` sibling key outside the `Logging` section; `Logging:LogLevel` now contains only `Default`, `Microsoft.AspNetCore` and `System.Net.Http.HttpClient`, all real levels. **No logging behaviour changed** — the levels themselves and `RemoveAllLoggers()` are untouched.

**Every `appsettings.json` in the repository was audited** for the same defect class; only this one was affected. The API's is clean (its Slice 2 addition, `TrustedForwarders`, is `.Get<T>()`-bound).

**No new test was added.** The 21 `AngebotPageTests` already pin it — they boot the real application against the real `appsettings.json`, which is precisely why they failed — and a second, path-dependent test would add fragility for no extra coverage.

**A count correction:** this slice's tests were reported as "62". That was the number of test *methods*; xUnit expands the theories to **80 cases**. Corrected throughout.

### 5.9 CI round 3 — build green, 2 test failures

**Run [33877784971](https://github.com/Keroleshanna/RenoTrack/actions/runs/33877784971), commit `73b3b45`.**

| Job | Result |
|---|---|
| `build-and-test` (ubuntu-latest) | ❌ **failure** — build succeeded; 2 Website tests failed |
| `database-backed-tests` (windows-latest) | ⏭️ **skipped** — gated on `needs: build-and-test` |

| Project | Failed | Passed | Skipped | Total |
|---|---|---|---|---|
| `RenoTrack.Domain.Tests` | 0 | **372** | 0 | 372 |
| `RenoTrack.Application.Tests` | 0 | **441** | 0 | 441 |
| `RenoTrack.Website.Tests` | **2** | 78 | 0 | 80 |

Both failures were **wrong expectations in tests written for this slice**, not production defects. Website failures across the three rounds: build error → 21 → 2.

### 5.10 The `IPNetwork` canonicality question, and what was actually verified

The second round-3 failure — `A_malformed_network_fails_startup_naming_the_key("10.0.0.1/8")`, "no exception was thrown" — was **not corrected by editing the test**, because the correct action depended on a security question nobody had established: *what does the current implementation actually trust?*

**Verified by reading `dotnet/runtime` at tag `v10.0.11`** (the runtime CI reports installed, confirmed byte-identical to `release/10.0`), plus `aspnetcore/release/10.0`'s `ForwardedHeadersMiddleware`:

- **`IPNetwork.TryParse("10.0.0.1/8")` returns `true`**, with `BaseAddress = 10.0.0.0`, `PrefixLength = 8`. The constructor calls `ClearNonZeroBitsAfterNetworkPrefix`, which masks the host bits away. **There is no throw path for non-canonical input anywhere.**
- **.NET's own documentation says the opposite.** The type remark claims "the constructor and the parsing methods will throw in case there are non-zero bits after the prefix", and the constructor declares an `ArgumentException` that is never raised. **Trusting that written contract over the implementation is exactly what produced the wrong assertion.**
- `ForwardedHeadersMiddleware.CheckKnownAddress` iterates `KnownIPNetworks` calling `Contains`, which masks the candidate and compares to the normalised base. So `10.0.0.0/8` contains `10.0.0.1`, `10.0.0.50`, `10.255.255.255`; it does not contain `11.0.0.1`.

**Effective trust before the fix:** `KnownNetworks: ["10.0.0.1/8"]` trusted **16,777,216 addresses**. Blast radius is bounded — `X-Forwarded-For` affects only rate-limit partitioning and the request scheme, and is never an authentication or authorisation input — so the worst case is rate-limit bypass from inside the over-trusted range, not privilege escalation. It is still a silent widening of a security boundary, which D97 exists to prevent.

**Decision (Product Owner, Option B):** enforce canonicality in the application. After `TryParse`, the parsed `BaseAddress` is compared against the address the operator supplied; a mismatch **fails startup**, naming the key, the range that would have been trusted, and `KnownProxies` as the right home for a single host. Deliberately unchanged: the fail-closed-when-unconfigured behaviour, `KnownProxies` (exact-match, no CIDR), and the forwarded-header trust mechanism itself.

**Coverage added:** three non-canonical rejections (`10.0.0.1/8`, `192.168.1.5/24`, `172.16.5.1/12`) asserting the message names all three things; five canonical acceptances including a `/32` host network and an IPv6 network; seven containment assertions pinning exactly what `10.0.0.0/8` trusts; and loopback proven untrusted despite the framework defaulting to it.

**`0.0.0.0/0` is deliberately not covered.** It is canonical and would be accepted, and it trusts every address on the internet. Rejecting it would be a new production rule beyond Option B's approved scope, and a test asserting acceptance would enshrine something questionable — so neither was written. **Recorded as a follow-up in `NEXT_STEPS.md`.**

**The other failure** — `PublicApiOptionsTests.A_relative_base_url_fails_startup("/api/v1")` — was a wrong expectation about *which* guard fires. `/api/v1` is refused either way, but on Unix `Uri.TryCreate(…, UriKind.Absolute, …)` succeeds with the `file` scheme, so the HTTPS guard rejects it, while on Windows the absolute-URL guard does. **Neither branch is pinned:** the test now asserts only that the value is refused and that the message names the key, both true on every OS. Pinning the Linux branch would have passed in CI and failed on a contributor's Windows machine — the exact failure mode `CLAUDE.md` §22 records for `Path.GetInvalidFileNameChars()`.

---

## 6. Slice 3 — Customer Angebot Rendering

### 6.1 Approved design

Reviewed and approved on 2026-09-04. The proposal is summarised here so the branch carries its own decision record.

**The headline is that almost nothing changes outside `RenoTrack.Website`.** `PublicAngebotDto` was built complete in Phase 6 and already carries every field Wireframe A3 renders, so Slice 3 grows the Website's mirrored type from Slice 2's single skeleton field and renders it. `GET /api/v1/public/angebote/{token}` is unchanged, there is no new endpoint, no schema, no migration, and no permission change — `PermissionMatrix.md` §7 already grants "View Angebot via token link".

| Question | Decision |
|---|---|
| **Q1** — commercial validity date ("gültig bis") | **No.** `TokenLink.ExpiresAt` is not exposed. Token expiry and commercial offer validity are different business rules, and conflating them would tell the customer something the business never decided. |
| **Q2** — document date | **No.** `CreatedAt`/`SentAt` are not added to the public contract for this page. Wireframe A3 shows neither. |
| **Q3** — quantity formatting | Explicit `de-DE`, up to 2 decimals, trailing zeros trimmed: `10`, `2,5`, `0,75`. **Never the ambient server culture.** |
| **Q4** — decision state | **Rendered — this reverses the proposal's recommendation.** `PublicAngebotDto` already carries `decision`, and BR-4 lets a customer re-read a quote they have already answered; showing a decided Angebot exactly like a pending one is misleading. |

### 6.2 Q4 in detail — what Slice 3 does and does not do

The responsibilities stay split at the same line as before; only the *read* half moves forward.

**Slice 3 renders the current decision state.** `Pending` renders the document as normal. `Approved` and `Rejected` render the same document plus a clear German status message saying the Angebot was already accepted or rejected. Nothing internal is exposed — no ids, no employee information, no token, and **no internal status vocabulary**: the customer is told what they did, not that the aggregate is in `CustomerApproved`.

**Slice 3 performs no mutation.** Accept/Decline, the decision request, the concurrency handling (D96) and the resulting state transitions all remain Slice 4's. **Decision mutation logic must not move into Slice 3.**

**`decisionAt` is deliberately not rendered and not mirrored into the Website's type.** The status message conveys the state without it, and rendering a UTC timestamp as a German date raises a timezone-policy question no document answers. Slice 4 can add it if it needs it — the growth-on-demand discipline `CLAUDE.md` §7 applies to this DTO like any other.

### 6.3 The one API-side change

**Deterministic item ordering.** `PublicAngebotMappingExtensions.ToPublicDto` orders sections explicitly (`SortOrder`, then `Id`) but does **not** order items, and `AngebotItem` has no `SortOrder` column. EF Core issues no `ORDER BY` for the collection, so item order is whatever SQL Server returns — usually clustered-index order, but not guaranteed. A priced document that can reorder its own lines between two reads is not a document, and the position numbers Wireframe A3 shows (`1.001`) are meaningless without it.

`.OrderBy(item => item.Id)` — insertion order, which is the order the Inspector entered the lines. No schema, no new field. **This is the only production change outside the Website.**

### 6.4 Sequencing

Three checkpoints on one branch: **3a** ordering + its test, **3b** the mirrored contract + deserialization tests, **3c** rendering + formatting/safety/edge tests.

**These are planned checkpoints, not claims.** None is green until CI proves it — the Slices 1–2 record (three defects, none found by inspection) is why that distinction is written down rather than assumed.

### 6.5 Out of scope

Accept/Decline and `DecisionReason` (Slice 4–5), link re-issue (6), company and legal identity and the A3 logo (7), PDF (Phase 14, gap `G-4`), the contact form (Phase 13). No unrelated refactors.

### 6.6 Implementation record

**Branch:** `claude/customer-workflow-phase-rvh4rj`, restarted from `main` at `78a8406` (PR #18 merged, so the branch is reused for fresh work rather than stacked on merged history).

| Checkpoint | Commit | Scope |
|---|---|---|
| — | `5d757c8` | The approved design and the Q4 reversal, recorded before any code |
| **3a** | `b2144c1` | Deterministic item ordering + 3 Application tests |
| **3b + 3c** | `8662a1f` | The mirrored contract, the document, formatting, styles + Website tests |

**Files changed**

*Application (the only production change outside the Website):* `Angebote/Dtos/PublicAngebotDto.cs` — `.OrderBy(item => item.Id)`.

*Website:* `PublicApi/CustomerAngebot.cs` (grown to `CustomerAngebot` + `CustomerSection` + `CustomerItem` + `CustomerVatLine` + `CustomerAngebotDecision`), `PublicApi/PublicAngebotClient.cs` (usability guard widened to null collections), `Rendering/CustomerFormatting.cs` (new), `Pages/Shared/_AngebotDocument.cshtml` (new), `Pages/Angebot.cshtml`, `wwwroot/css/customer.css`.

*Tests:* `Application.Tests/Angebote/Dtos/PublicAngebotMappingTests.cs` (new), `Website.Tests/CustomerAngebotBuilder.cs` (new), `Website.Tests/Rendering/CustomerFormattingTests.cs` (new), `Website.Tests/Pages/AngebotDocumentTests.cs` (new), plus updates to `PublicAngebotClientTests`, `AngebotPageTests`, `AngebotModelTests` and `CustomerWebsiteFactory` for the grown contract.

**Decisions taken while building, none of them reopening an approved one**

- **`decisionAt` is not mirrored into the Website's type at all.** The status message says what the customer did without it, and rendering a UTC value as a German date raises a timezone-policy question no project document answers. Unknown JSON properties are ignored on deserialization, so omitting it costs nothing.
- **An unrecognised `decision` value is an outage, not a `Pending` Angebot.** Defaulting would tell a customer their recorded answer was never taken — a wrong statement about their own decision is worse than an honest "not available right now".
- **A 200 whose `sections` or `vatBreakdown` is `null` is an outage.** The API always emits an array, so `[]` arrives as an empty list; `null` means the two sides disagree about the contract. An empty list stays a legitimate document.
- **Only `m2` is rewritten** (to `m²`) — the one standard code whose storage form is an ASCII compromise. Everything else, including any custom label, passes through untouched.
- **A section with no lines renders with a zero `Zwischensumme` rather than being suppressed.** Only one section needs items for an Angebot to be submittable, so this is reachable, and the Inspector put the section in the document.

**Not done, and it is a stated completion gate:** the browser QA pass (desktop, mobile, print). This environment has no .NET SDK, so the application cannot be launched here — see §6.7.

### 6.7 CI round 1 — Razor build failure, one root cause, fixed

**Run [33906802660](https://github.com/Keroleshanna/RenoTrack/actions/runs/33906802660), commit `d38eed2`, PR [#19](https://github.com/Keroleshanna/RenoTrack/pull/19).**

| Job | Result |
|---|---|
| `build-and-test` (ubuntu-latest) | ❌ **failure** — `0 Warning(s), 3 Error(s)` |
| `database-backed-tests` (windows-latest) | ⏭️ **skipped** — gated on `needs: build-and-test` |

```
_AngebotDocument.cshtml(52,25): error RZ1011: The 'section' directives value(s) must be separated by whitespace.
_AngebotDocument.cshtml(61,44): error RZ2005: The 'section' directive must appear at the start of the line.
_AngebotDocument.cshtml(61,51): error RZ1011: The 'section' directives value(s) must be separated by whitespace.
```

**One root cause: the loop variable was named `section`, so `@section.Title` parses as Razor's `@section` directive** rather than as a property access. Nothing about the C# is wrong; the identifier simply collides with a language construct in the templating layer.

**Fixed by renaming the variable to `angebotSection`, not by escaping each use as `@(section.Title)`.** Escaping fixes the two call sites and leaves the same trap armed for the next property anyone adds to this partial; renaming removes it.

**A second hazard was found and fixed in the same push**, before it could cost another cycle: the decision banner's class was built inline as `class="customer-status-@(… ? "approved" : "rejected")"` — double-quoted string literals inside an `@(...)` expression inside a double-quoted attribute value, which is the nesting Razor's attribute parser handles least predictably. Hoisted to a local. The whole partial was also swept for every other Razor directive keyword used in property-access position; none remained.

**The general lesson, now in `CLAUDE.md` §24:** a Razor view is not C# — an identifier that is perfectly ordinary in a handler can collide with a directive in a template, and it fails at build rather than at review.

### 6.8 CI round 2 — a second Razor error, from the round-1 fix itself

**Run [33907410208](https://github.com/Keroleshanna/RenoTrack/actions/runs/33907410208), `0 Warning(s), 1 Error(s)`:**

```
_AngebotDocument.cshtml(31,6): error RZ1010: Unexpected "{" after "@" character.
Once inside the body of a code block (@if {}, @{}, etc.) you do not need to use "@{" to switch to code.
```

**Introduced by round 1's own second fix.** Hoisting the status class to a local was right; wrapping it in a code-block opener was not — inside a code block's body Razor is already in code context, so a local is declared as a plain statement. The same file's item loop had been doing exactly that correctly all along, one screen further down.

Fixed by removing the block opener. The comment left inaccurate by the round-1 rename was corrected in the same push, and every `@` in the file was then read individually: each is a directive at the top, a Razor comment, a control-flow keyword, or an expression beginning with a non-directive identifier.

**Both rounds were the templating layer, not the C#, and neither was visible to review** — the same class of finding as Slice 2's `Logging:LogLevel` comment keys. In an environment with no SDK, a Razor view is the part of a change with the least local verification available and the most syntax that is not C#, and it is worth reading in full rather than patching by line number. This round-2 fix was made that way: the whole file was read before anything was touched.

### 6.9 CI round 3 — Razor clean, one test-authoring defect

**Run [33907629207](https://github.com/Keroleshanna/RenoTrack/actions/runs/33907629207), commit `a093bb6`. `0 Warning(s), 1 Error(s)`.**

**`RenoTrack.Website` compiled** — the Razor work is done. The remaining error was in the new test file:

```
CustomerFormattingTests.cs(69,6): error xUnit1025: Theory method
'Quantity_trims_trailing_zeros_and_uses_a_german_decimal_separator' has InlineData duplicate(s).
```

**The analyser was right about more than it said.** The rows were `[InlineData(10, "10")]` next to `[InlineData(10.00, "10")]`, written to prove that a quantity's trailing zeros are trimmed. They cannot prove that: an `InlineData` argument is a compile-time constant passed as `int` or `double`, and neither carries a scale — only `decimal` does. So `10.00` arrived as the same value as `10`, and the rows were not merely redundant, they were **testing something other than what they appeared to test.**

Fixed by removing the redundant rows and adding a separate `[Fact]` using real `decimal` literals (`10.00m`, `2.50m`, `0.7500m`), with an assertion first that those literals really do carry their scale — so the test proves its own premise rather than assuming it. Every other theory in the slice's new tests was swept for numerically-equal rows; none.

### 6.10 CI round 4 — build clean, the partial rendered nothing

**Run [33907803685](https://github.com/Keroleshanna/RenoTrack/actions/runs/33907803685), commit `eeec2ca`.** Build clean; `Failed: 13, Passed: 165, Total: 178` in `RenoTrack.Website.Tests`.

Every failure was the same symptom: the page returned 200 and rendered `<!DOCTYPE html><html lang="de">…`, and **none of the document was in it** — no section title, no `m²`, no subtotal.

~~**Cause: `@await Html.PartialAsync(...)` inside a `@switch` case body**… the `IHtmlContent` the call returns is simply discarded.~~ — **this diagnosis was wrong; see §6.11.** The partial was rendering all along. `Html.RenderPartialAsync` was adopted and is kept as the explicit form for a code block, but it fixed nothing, and round 5 returned the identical 13 failures.

**The Slice 2 test that should have caught it did not, and it has been strengthened.** `An_available_angebot_shows_its_number` asserted only that the Angebot number appears somewhere in the HTML — and it does, in the `<title>`, which the page still set. It therefore passed while the feature was entirely broken. It now also anchors on the document container and the summary heading, so a page rendering only its chrome fails. This is precisely the pattern `CLAUDE.md` §23 already records from Phase 10: *"a caught error must not be able to make a broken feature look healthy"* — here it was a weak assertion rather than a swallowed exception, with the same result.

**Three rounds, three different Razor context rules** — a directive collision, a code block reopened inside a code block, and a return value discarded in code context. None was visible in review, none is C#, and only the third produced no build error at all.

### 6.11 CI round 5 — the same 13 failures, and the actual cause

**Run [33908019337](https://github.com/Keroleshanna/RenoTrack/actions/runs/33908019337), commit `ee867e9`.** Build clean; `Failed: 13, Passed: 165, Total: 178` — **byte-identical to round 4.** Round 4's fix changed nothing, which is what forced a real diagnosis instead of a second guess.

**The passing tests were the evidence, not the failing ones.**

| Observation | What it ruled out |
|---|---|
| `Positions_are_numbered_per_wireframe_a3` passes on `1.001`, `Pos. 1` | The partial renders. Item rows render. |
| `Free_text_is_html_encoded_not_executed` passes on `&lt;script&gt;` | The item description reaches the page. |
| `The_document_is_labelled_in_german` passes on `Zwischensumme`, `Einzelpreis` | The table and summary render. |
| **All 13 failures contain `ä`, `²` or `€`. Every passing content assertion is pure ASCII.** | Leaves exactly one explanation. |

**Cause: ASP.NET Core's default `HtmlEncoder` allows only Basic Latin and escapes everything else to numeric character references.** `Wände` was served as `W&#xE4;nde`, `m²` as `m&#xB2;`, `1.650,00 €` as `1.650,00&#x20AC;`. A browser renders all of those identically to the intended text — which is exactly why this ships unnoticed — but the document is neither readable in view-source nor searchable, and on a page whose entire audience is German-speaking that is the wrong default.

**Fixed by widening `WebEncoderOptions.TextEncoderSettings` to `UnicodeRanges.All`.** This does not weaken escaping: the range governs which characters pass through unescaped, while `< > & " '` are escaped regardless — so the protection over Inspector-typed free text, which `Free_text_is_html_encoded_not_executed` pins, is untouched. The page is served as UTF-8 and declares that charset.

**A second weak assertion was exposed and fixed.** `Sections_and_lines_render_in_server_order` passed throughout, because `IndexOf` returns `-1` for an absent needle and `-1` is less than any real index — so it was ordering text that was not on the page. It now asserts presence before order.

**Round 4's diagnosis was wrong and is withdrawn.** `@await Html.PartialAsync` in a code block was never the defect; the rule it produced has been removed from `CLAUDE.md` §24 and replaced with what the evidence actually supports. `RenderPartialAsync` is kept as the explicit form for a code block, on its own merits. **Five rounds, and the one that cost the most was the one where a plausible cause was accepted without checking what the passing tests already ruled out.**

### 6.12 CI round 6 — green

**Run [33908391383](https://github.com/Keroleshanna/RenoTrack/actions/runs/33908391383), commit `5b78a7b`.** Both jobs succeeded: `build-and-test` (Linux) and `database-backed-tests` (Windows + LocalDB). **1,838 / 1,838 tests passing, 0 errors, 0 warnings.**

Six rounds, five of them Razor or test-authoring defects invisible to review in an environment with no SDK. The one that cost the most was round 4, where a plausible cause was accepted without checking what the passing tests already ruled out.

### 6.13 Browser QA

A stated completion gate for this slice, and the reason it could finally be run: a .NET SDK was obtained in the authoring environment (`dotnet-sdk-10.0`, 10.0.111, from the distribution's own repository after switching its sources to HTTPS). **The API could not be run** — no SQL Server, no LocalDB, no container runtime — so the substitution point was chosen deliberately and is stated here rather than implied.

**Method.** The real, unmodified `RenoTrack.Website` was built from `5b78a7b`, **published** (`dotnet publish`) and served over HTTPS with an OS-trusted development certificate. A throwaway stub, outside the repository and outside the solution, served `GET /api/v1/public/angebote/{token}` emitting the genuine `PublicAngebotDto` wire contract, with the token prefix selecting the state. **TLS validation was not disabled anywhere.** Everything from the Website's HTTP call inwards — deserialisation, the contract guard, outcome mapping, Razor rendering, `CustomerFormatting`, the stylesheet and the security headers — was exercised for real; **the API handler, the DTO projection, the `TokenLink` lookup and the database were not**, and remain covered by the automated suite alone.

**Checks performed** (Chromium via Playwright 1.56.1, headless):

| Surface | Result |
|---|---|
| Desktop 1280×900 | Title and `<h1>` both `ANG-2026-00042`; three `Pos. N` headings in server order; position numbers `1.001`–`2.004`; descriptions and specifications; quantities `1 / 42,5 / 62,25 / 48 / 2 / 86,4 / 12`; units `pauschal / m² / Stk / lfm / Sack`; subtotals `6.500,00 € / 11.930,00 € / 0,00 €`; summary `18.430,00 €`, `zzgl. 16% MwSt 1.040,00 €`, `zzgl. 19% MwSt 2.565,70 €`, `Gesamtsumme 22.035,70 €`. **0 scripts, 0 links**, no horizontal overflow. |
| Decision state | `Pending` — no banner. `Approved` — *"Sie haben dieses Angebot angenommen."* **and the full document still below it.** `Rejected` — *"Sie haben dieses Angebot abgelehnt."*, likewise. No Accept/Decline control in any state. |
| Mobile 390×844 and 320×720 (DPR 3) | No horizontal overflow on either; the list of elements wider than the viewport is **empty** on both. Rows collapse to labelled stacked pairs; `Gesamtsumme 22.035,70 €` readable and unwrapped at 320 px. |
| Print | No navigation chrome, white background, `thead` as `table-header-group` so headers repeat. A4 PDF is **two pages**: the break falls between `Pos. 3`'s subtotal and `Zusammenfassung`, so **no row is split, nothing clipped, nothing missing.** |
| German and typography | `Wände`, `Gerüst`, `Sanitär`, `Großformat`, `einschließlich`, `m²`, `€`, `60×120 cm` all render correctly **and are served as literal UTF-8**, verified over HTTP — §6.11's encoder fix confirmed end to end rather than only by unit test. |
| Failure states | 404 *Dieser Link ist nicht gültig*; 410 *Dieser Link ist abgelaufen*; 503 *Ihr Angebot ist gerade nicht abrufbar*. Distinct, correct status codes, 0 scripts on each. |
| Token leakage | The token appears in no page, no heading, no link and **no log line**, in every state. |

**One finding, and it is not a product defect.** The first attempt showed an unstyled page and a 457 px overflow at 390 px, with Chromium refusing `customer.css` for an empty MIME type. It was **isolated rather than guessed at**: temporarily removing `app.UseCustomerSecurityHeaders();` changed nothing, ruling the middleware out (that edit was reverted immediately and the tree confirmed clean). The cause is that `dotnet run --no-build` serves `MapStaticAssets` from an incomplete asset manifest — the compressed variants only exist after `dotnet publish`. Against the published build the stylesheet is served as `text/css` with `Content-Encoding: br` and everything renders. **No repository change is required**, but it is worth knowing locally: a customer-facing rendering check run under `dotnet run` can look broken when the application is not.

### 6.14 Merged

**Approved for merge by the Product Owner on 2026-09-04** against the full completion gate — design and the Q4 decision-state change, checkpoints 3a/3b/3c, German formatting, security, both CI jobs, 1,838/1,838 tests, 0 warnings, desktop/390 px/320 px/print browser QA, the three decision states, the three failure states, token-leakage checks, clean working tree, and no out-of-scope functionality. The browser-QA methodology and its documented API/database limitation were accepted explicitly.

**PR [#19](https://github.com/Keroleshanna/RenoTrack/pull/19) merged into `main` as `450ebbd`** (merge commit, parents `78a8406` and `5b78a7b`), per §19's "no direct commits to `main`" rule.

**Slice 3 is complete. Slice 4 (Accept / Decline) is blocked pending explicit Product-Owner approval** and has not been started.

---

## 7. Slice 4 — Accept / Decline

### 7.1 Approved design

Reviewed and approved on 2026-09-05. **The API needs no change at all.** `POST /api/v1/public/angebote/{token}/decision`, `RecordAngebotDecisionCommandHandler` and the three-aggregate single commit were all built complete in Phase 6, and Slice 1 (D96) already made the double-decision race a deterministic 409. Slice 4 is therefore a **Website-only** slice: no endpoint, no Domain change, no schema, no migration, no permission change. Should any of that turn out to be factually untrue during implementation, the instruction is to **stop and report the discrepancy**, never to widen scope silently.

| Question | Decision |
|---|---|
| **Q1** — confirmation before recording | **Yes, for both choices.** The decision is irreversible under BR-4, which is exactly D83's test. Server-rendered, no JavaScript, no `confirm()`. |
| **Q2** — behaviour on 409 | **Re-read and show the persisted decision.** The API and database are authoritative; what the customer *attempted* is not. |
| **Q3** — render `decisionAt` | **No.** No timezone policy is invented for this feature. |

### 7.2 The two-step flow, exactly

This is the flow the implementation must produce, and the property that matters most is on the third line: **the first click changes nothing.**

```
GET  /angebot/{token}                          → the document, Pending, with two buttons
  │
  ├─ "Angebot annehmen"  ─┐
  └─ "Angebot ablehnen"  ─┴─→ GET /angebot/{token}/entscheidung/{annehmen|ablehnen}
                                    │   re-reads the Angebot; renders the confirmation page
                                    │   NO TokenLink consumption, NO business mutation
                                    │
                                    ├─ "Abbrechen"   → back to GET /angebot/{token}
                                    └─ "Bestätigen"  → POST /angebot/{token}/entscheidung/{choice}
                                                            │  antiforgery validated
                                                            │  → API POST .../decision
                                                            │
                                                            └─ 302 → GET /angebot/{token}
                                                                        → Approved / Rejected banner
```

**The first step is a `GET` and therefore cannot mutate.** That is not a convention being trusted — it is the reason the step is a GET rather than a POST: an HTTP-safe method makes "the confirmation page records nothing" a property of the shape of the request, not of the care taken inside a handler. A test pins it anyway (§7.5).

**The decision travels in the route, never anywhere else.** `{choice}` is a route segment, exactly as `{token}` is. No hidden field carries it, no query string carries it, no cookie, no `TempData`, no session — so there is no client-supplied state for a customer to edit into a different decision than the one whose confirmation page they read. The two buttons on the document are ordinary links to two distinct URLs; `Referrer-Policy: no-referrer` already covers the only concern that raises.

**German route segments** (`entscheidung`, `annehmen`, `ablehnen`) match `/angebot/` itself. §23's "URLs are never translated" governs multi-language URL variants, which cannot arise here — the customer surface is German-only by Q8, so there is nothing to translate and no second variant to drift.

**`CustomerSecurityHeaders` already covers the new routes with no change**, because its strict rules key on a route *parameter named `token`* rather than on a path — the case its own remarks anticipated by name (`{token}/entscheidung`). `no-store`, `noindex` and `no-referrer` apply to the confirmation page and the POST response for free.

### 7.3 Post-Redirect-Get, and the 409

**Every terminating outcome of the POST redirects to `GET /angebot/{token}`.** Without it, a refresh or a back-then-forward re-POSTs a consumed link and shows a failure for an action that actually succeeded.

**The 409 redirects to the same place, and that is how Q2 is satisfied.** The document page re-reads the Angebot and renders whatever the API says is persisted:

> Customer A approves (200). Customer B rejects (409). **B is shown the Angebot as approved** — the real state — not a message about the rejection they attempted.

**The "already answered" sentence proposed at design review is deliberately dropped.** Carrying a one-off message across a redirect means `TempData`, which in Razor Pages is cookie-backed by default — client-side state on a page whose entire design avoids it, for a sentence the banner already makes true. The persisted banner is the honest and sufficient answer. This is a narrowing of the reviewed design, recorded here rather than made quietly.

### 7.4 The boundary grows by the minimum

`IPublicAngebotClient` gains **one method**, per §4's growth-on-demand discipline:

```csharp
Task<CustomerDecisionOutcome> RecordDecisionAsync(
    string token, CustomerDecisionChoice choice, CancellationToken cancellationToken);
```

Two small enums and nothing else. **No result record**, because there is no payload to carry: every outcome ends in a redirect that re-reads the document, so a returned DTO would be a second, staler copy of what the next request fetches authoritatively.

- `CustomerDecisionChoice` — `Approve`, `Reject`. **Two values, never three.** It is an action, and `Pending` would be meaningless as an input; the API's own `CustomerDecision` makes the same distinction for the same reason.
- `CustomerDecisionOutcome` — `Recorded`, `NotFound`, `Expired`, `AlreadyDecided`, `Unavailable`.

**`AlreadyDecided` is the one outcome the read surface does not have, and its absence there was never an omission.** Slice 2's `CustomerAngebotOutcome` documents why: BR-4 makes a link single-use *for decisions only*, and viewing stays open, so a consumed link reads exactly like an unconsumed one. Slice 4 is where consumption finally becomes observable, which is exactly where that value appears.

The API-status mapping is preserved unchanged from the read client: 404 → invalid, 410 → expired, **409 → already decided**, 5xx / network / timeout / unparseable → unavailable. The mapping stays in `PublicAngebotClient` so it remains reviewable in one file.

### 7.5 Tests this slice must carry

- The two buttons appear **only** for `Pending`, and are absent for `Approved` and `Rejected`.
- The confirmation page renders for both choices, names the Angebot, and offers Bestätigen and Abbrechen.
- **The confirmation step records nothing** — driving the GET leaves the decision client uncalled. This is the test the whole two-step design exists for.
- The final POST calls the client exactly once, with the choice taken from the route.
- PRG: a recorded decision answers **302** to `/angebot/{token}`, not 200.
- **409 re-read**: the losing customer ends on the document page showing the *persisted* decision.
- Every outcome maps to the right page and status: 404, 410, 409, unavailable.
- **Antiforgery**: a POST without a valid token is rejected.
- **Token non-leakage**: the token appears in no form field, no hidden input, no link body and no log line, on both new routes.

### 7.6 Out of scope

`DecisionReason` (Slice 5, migration #12 — the API deliberately does not accept one today, and sending a value it discards would break the expectation that anything accepted is kept), link re-issue (Slice 6), company and legal identity (Slice 7), PDF (Phase 14), the contact form (Phase 13). No JavaScript, on any page, at all.

### 7.7 Implementation record

**Branch:** `claude/customer-workflow-phase-rvh4rj`, restarted from `main` at `702e1ff` (PR #20 merged, so the branch is reused for fresh work rather than stacked on merged history).

**The design held: no API, Domain, Infrastructure or database change was needed, and none was made.** The endpoint, the handler, the three-aggregate commit and D96's concurrency token were all already there.

**Files added** — `PublicApi/CustomerDecision.cs` (the two enums), `Pages/AngebotDecision.cshtml(.cs)` (the confirmation and the POST), `tests/.../TokenExposure.cs`, `tests/.../Pages/AngebotDecisionPageTests.cs`.
**Files changed** — `PublicApi/IPublicAngebotClient.cs` and `PublicAngebotClient.cs` (one method), `Pages/Angebot.cshtml(.cs)` (the two buttons and the URL helper), `wwwroot/css/customer.css`, and four existing test files.

**The mechanism, as built.** The buttons are two ordinary links to `/angebot/{token}/entscheidung/{annehmen|ablehnen}`. `GET` renders the confirmation; `POST` to the same URL records the decision and redirects to the document. The first step is a GET **so that "the confirmation records nothing" is a property of the HTTP method rather than of care taken inside a handler** — and a test asserts the decision client is never called by it, because the property is worth more than the reasoning.

The action links live in `Angebot.cshtml`, not in `_AngebotDocument.cshtml`: the partial is the priced document the customer reads and prints, and these are navigation. That separation is also why the print stylesheet needed only one rule for them rather than a rethink.

### 7.8 Two defects found while building, neither by review

**1 — A capitalised URL would have inverted the customer's decision.** `ASP.NET` matches route constraints **case-insensitively**, so `/entscheidung/Annehmen` routes to this page perfectly happily. The first implementation compared the segment with `StringComparison.Ordinal`, fell through to the `else`, and would have **rejected the Angebot the customer was trying to accept** — the worst available failure in this flow, silently, on a link a mail client had merely title-cased.

It was found by a test written on the opposite assumption: `An_unknown_choice_segment_is_not_routable` included `"Annehmen"` expecting a 404. The 404 never came. §14 says a failing test that reveals a mistake in its own expectation is still valuable — here correcting the expectation also uncovered the defect, and the test that replaced it (`A_capitalised_route_records_the_choice_it_names`) pins the behaviour rather than the assumption. The comparison is now `OrdinalIgnoreCase`, and the unreachable fallback resolves to `Reject`, which is the direction a customer can still recover from.

**2 — The blanket "the token is never rendered into the page" assertion could no longer hold.** Slices 2 and 3 asserted the token appeared nowhere in the HTML at all, and that was right while the page had no navigation. Slice 4's decision routes live *under* the token — which is exactly what keeps the credential in the route and out of a hidden field, a query string and the request body, as required — so a link to one necessarily contains it. Two existing tests failed.

**The assertion was narrowed, not deleted.** Deleting it is how a security property disappears without anyone deciding to give it up. `TokenExposure.AssertOnlyInSameOriginLinks` now proves the token appears in **no visible text, no `input` of any kind, and no query string**, and that every remaining occurrence is inside an `href` under `/angebot/`. The narrower rule is safe for reasons that do not extend to the excluded places: the browser is already on a URL containing the token, `Referrer-Policy: no-referrer` means clicking a link hands it to nobody, and no script runs on these pages to read the DOM. Both rules are now in `CLAUDE.md` §24.

### 7.9 Local verification

`dotnet build` — **0 errors, 0 warnings** (solution-wide, `TreatWarningsAsErrors`).

| Suite | Result |
|---|---|
| `RenoTrack.Domain.Tests` | 372 / 372 |
| `RenoTrack.Application.Tests` | 444 / 444 |
| `RenoTrack.Website.Tests` | **218 / 218** (up from 178) |

`RenoTrack.Infrastructure.Tests` and `RenoTrack.Api.Tests` need LocalDB and run in CI's Windows job (D40, D56); this slice touches neither project.

### 7.10 Browser QA

Run against a **`dotnet publish` output**, never `dotnet run`, per §24's rule. Chromium via Playwright, driving the real unmodified Website; the API stubbed at its HTTP boundary as in Slice 3 — but this time the stub **records decisions**, so BR-4's single use is genuine rather than simulated and the second attempt on a link really does get a 409.

| Check | Result |
|---|---|
| Pending document | Both actions present, `href`s `/angebot/{token}/entscheidung/{annehmen,ablehnen}`, 0 scripts, no overflow |
| Confirmation (annehmen) | "Sie sind dabei, das Angebot ANG-2026-00042 über 22.035,70 € verbindlich anzunehmen."; Annahme bestätigen / Abbrechen; **the only hidden input is `__RequestVerificationToken`** |
| Confirmation (ablehnen) | Correct heading and "Ablehnung bestätigen" |
| **Confirmation records nothing** | Document still Pending afterwards; Abbrechen returns to it still undecided |
| Approve flow | Ends on `/angebot/{token}`, banner "Sie haben dieses Angebot angenommen.", actions gone, document still shown |
| Reject flow | Ends on the document, banner "Sie haben dieses Angebot abgelehnt." |
| **Double submit — reload** | URL after deciding is the document; **reload answers 200 with the banner and re-posts nothing** |
| **Double submit — back button** | Returning to the confirmation and re-opening it redirects to the document and shows the persisted decision |
| **Two customers, one link** | A approves and wins; **B, who pressed *ablehnen*, ends on the document reading "Sie haben dieses Angebot angenommen."** — the persisted state, with no rejection message anywhere |
| Failure states | 404 "nicht gültig", 410 "abgelaufen", 503 "Ihre Antwort konnte nicht gespeichert werden", 0 scripts each |
| Mobile 390×844 and 320×720 | No overflow on document or confirmation; submit button 44 px tall on both |
| Print | The actions are `display: none` on paper |
| Token leakage | **The token appears in no Website log line at all** across the whole run |

**An independent confirmation of the two-step property:** the stub recorded exactly **five** decisions across a run that opened the confirmation page nine times. Had the first step mutated, the count would have matched the page views.

**One observation, not a defect, and not changed here.** In the two-customer case the losing reader is shown Slice 3's approved banner, which is phrased *"**Sie** haben dieses Angebot angenommen."* — accurate for the link holder, mildly odd for a second person sharing one link. The wording is Slice 3's, already approved, and one link is one customer by design; raising it rather than silently rewording approved copy.

**A harness artefact worth naming:** QA ran the Website over HTTP because Chromium in this container no longer trusts the local development certificate (`certutil` is gone, so the NSS store could not be refreshed). Nothing under test depends on the Website's own scheme — the security headers, rendering, routing, antiforgery and the flow are identical — and the Website→API call stayed HTTPS throughout, as `PublicApi:BaseUrl` requires. The one visible consequence is a `Failed to determine the https port for redirect` warning in the log, which is the HTTPS-redirection middleware correctly reporting that no HTTPS port was configured for this run.

---

## 8. Slice 5 — The Rejection Reason

### 8.1 Approved design

Reviewed and approved on 2026-09-05, after a full design review covering current state, business rules, layer impact, schema, contract, both front ends, security, concurrency, tests and documentation. The decision record is **`ARCHITECTURE_DECISIONS.md` D98**, which resolves the ADR Phase 6 deliberately deferred.

**The trigger fired rather than being invented.** `NEXT_STEPS.md` recorded the FR-6.3 gap with an explicit revisit trigger — "any requirement that reads a reason back" — and Slice 4's decision UI is that requirement. The gap entry is now removed rather than amended.

| Question | Decision |
|---|---|
| **Q1** — echo the reason in the public/customer DTO | **No.** The customer submits it; only staff read it back. The anonymous token is already a credential, and echoing customer-authored free text through it widens the one contract any holder of a forwarded email can reach, for no benefit. |
| **Q2** — include the reason in the Admin notification email | **No.** The email stays a notification. Customer free text must not be copied into mailbox storage, outside the Dashboard's access model. `AngebotDecisionNotification` is unchanged, so D70's manual retry keeps working untouched. |
| **Q3** — maximum length | **1000 characters**, as originally approved. |

**Boundaries, all explicit:** rejection-only (`RecordCustomerApproval()` takes no reason); immutable once recorded (no edit endpoint, no clear endpoint, no Domain method); an approval carrying a reason is a **400**, per K-4/D67's existing rule rather than a new judgement; never in `AuditLog` (D50); never an `AngebotReviewComment` (its `AdminUserId` is a required FK to `AspNetUsers`).

**Concurrency needs nothing new.** D96's `TokenLinks.UsedAt` token already serialises this path and the reason is written in the same EF batch, so a loser's batch rolls back and takes the reason with it. `Angebote` gets no token of its own.

### 8.2 Implementation order

Four commits, checkpointed — build 0/0 and the relevant suites green before each next step:

1. **Documentation** — D98, `ERD.md`, `Architecture.md` §5.2, `NEXT_STEPS.md`, this section.
2. **Domain + migration #12** — the property, the guard, `RecordCustomerRejection(string?)`, the EF configuration, the migration, Domain and Infrastructure tests.
3. **Application + API** — command, validator, handler, request record, `AngebotDetailDto`, the 400 rule, and the **replaced** contract test.
4. **Customer Website + Dashboard** — the optional textarea on the ablehnen confirmation, the client parameter, the Dashboard rendering, tests, browser QA.

### 8.3 The obsolete contract test is replaced, not deleted

`PublicAngebotDecisionEndpointTests.A_rejection_reason_is_not_part_of_the_contract` pinned the Phase 6 gap so it could not drift into accept-and-discard. That gap is now closed, so the test is genuinely obsolete — but the guarantee it carried is not. It is replaced by tests proving the **new** contract: the reason is accepted, persisted, returned to staff, refused alongside an approval, refused over-length, and **absent from the public DTO**.

This is the same discipline Slice 4 applied to `TokenExposure`: when a rule changes, the executable guarantee changes with it. Deleting the test would leave every one of those six properties unenforced.

### 8.4 Implementation record

**Four commits, as planned.** Documentation (`cf61294`), Domain + migration #12 (`ca95876`), Application + API (`7122af6`), and this one.

**Website.** `IPublicAngebotClient.RecordDecisionAsync` grew one `string? reason` parameter — still one method, still growth-on-demand. The confirmation page binds an optional `Reason`, and passes `null` on the approval path rather than trusting the form's shape, so the API's approval-with-reason refusal is unreachable through the UI rather than merely handled. `maxlength` rather than a live counter, because a counter needs JavaScript and no customer page runs any; the number is a mirrored constant with a test, exactly as `PAGE_SIZE_MAX` and `MAX_SCHEDULE_WINDOW_DAYS` are (§23).

**A 400 stopped meaning what it used to, and that needed a new outcome.** Before this slice, the only way to get one was for the two sides to disagree about the contract — this Website's fault, correctly reported as an outage and logged as an error. An over-length reason makes it customer-reachable, so `CustomerDecisionOutcome.Invalid` was added and the page **re-offers the form with the text still in it** rather than replacing it with an error the customer cannot act on without retyping. The theory row asserting `BadRequest → Unavailable` was updated rather than deleted, with the reason recorded beside it.

**That refusal path is also the only place a customer's own words return to the screen** — unsaved input handed back within the same exchange, never the persisted value, which Q1 keeps staff-facing. It is therefore the surface where inert rendering has to be proven, and a test drives a `<script>`-bearing reason through it.

**Dashboard.** The reason renders on the Angebot detail screen only when the status is `CustomerRejected` **and** a reason exists, attributed as *„Begründung des Kunden"*. No placeholder when absent: a rejection without a reason is normal, and captioning its absence implies something is missing. Interpolation only — no `innerHTML`, no `bypassSecurityTrust*` anywhere in the Dashboard.

### 8.5 Verification

`dotnet build` — **0 errors, 0 warnings**. `has-pending-model-changes` — no drift; **migration count unchanged at 12** (this commit adds no schema).

| Suite | Result |
|---|---|
| `RenoTrack.Domain.Tests` | 384 / 384 |
| `RenoTrack.Application.Tests` | 453 / 453 |
| `RenoTrack.Website.Tests` | **231 / 231** (from 218) |
| `RenoTrack.Dashboard` (Karma) | **80 / 80** (from 74) |

`Infrastructure.Tests` and `Api.Tests` need LocalDB and run in CI's Windows job. **The Dashboard suite does not run in CI at all** — `ci.yml` has no Node job — so its 80 passing tests are a local result, stated as such rather than implied.

### 8.6 Browser QA

Against a **`dotnet publish` output**, Chromium via Playwright, with the stub enforcing D98's own shape rules so the 400 is real rather than simulated.

| Check | Result |
|---|---|
| Ablehnen confirmation | One `<textarea>`, `maxlength="1000"`, label *„Möchten Sie uns kurz sagen, warum? (optional)"*, **0 scripts** |
| Annehmen confirmation | **0 textareas** — the field is absent, not disabled |
| Reject with a reason | Recorded; **the API received 59 characters**; customer lands on the document with the rejected banner |
| **Reason echoed to the customer** | **No** — Q1 holds end to end |
| Reject without a reason | Recorded; the API received `null`, not `""` |
| Approval | Unaffected; the API received `null` |
| Over-length (attribute stripped, as a non-browser caller would) | **400**, form re-offered, **text preserved**, German error shown |
| Hostile `<script>` in that refusal | **0 scripts in the DOM**, the raw tag absent, `&lt;script&gt;` present — inert |
| Mobile 390 × 844 and 320 × 720 | No overflow; the textarea fits the viewport on both |
| Print | The actions stay hidden on paper |
| Leakage | The only `input` on the page is `__RequestVerificationToken`; the token appears **only** in `href` attributes; **neither the token nor the reason appears in any Website log line** |

---

## 9. Slice 6 — Token Re-issue

### 9.1 Approved design

Reviewed and approved on 2026-09-05, **after one round that found a real concurrency flaw in the first proposal**. The decision record is `ARCHITECTURE_DECISIONS.md` **D99**.

**No requirement document named this capability.** SRS had no FR, `PermissionMatrix.md` §4 no row, `Architecture.md` §5.2 no endpoint — it exists because of the Product Owner's **Q3** decision, which is the footing `Angebot.UpdateItem` stood on in Phase 10. So the documents moved first (§15), and **`SRS.md` gains FR-6.1a** rather than leaving an approved requirement recorded only in a phase note. SRS **OQ-4** ("revise and resend" after a rejection) is a different question and stays open.

| Question | Decision |
|---|---|
| **Q1** — where the "`Sent` only" check lives | **The Application handler**, throwing `ConflictException`. A documented exception to §6, because re-issuing changes no aggregate state, so there is no mutator to call and let throw — and a public `Angebot.EnsureResendable()` probe is exactly what D29 rejected for `Inspection.IsEditable`. |
| **Q2** — re-issue a link that already lapsed | **Allowed.** The Angebot is still `Sent` and the customer has a dead link; this is the most valuable case. |
| **Q3** — how the old link dies | **`ExpiresAt` set to now.** Not `UsedAt` (that means "a decision was recorded" and is D96's token), not a new `RevokedAt` column, not deletion. The customer page's existing 410 wording becomes literally true. |
| **Q4** — update `SentAt`? | **No.** It records the original send; re-issues live in the audit trail. |
| **Q5** — serialising concurrent re-issues | **`ExpiresAt` becomes an optimistic-concurrency token**, alongside `UsedAt`. |

### 9.2 The flaw the review caught, and why it matters more than the fix

The first design claimed D96's `UsedAt` token already serialised two simultaneous re-issues. **It does not, and the Product Owner caught it.**

EF Core puts a concurrency token's *original* value in the `WHERE` clause. A re-issue never writes `UsedAt`, so two concurrent re-issues both issue `WHERE Id = @id AND UsedAt IS NULL`, both match, both commit, and **two usable credentials exist** — violating the invariant the mechanism was cited to protect.

`UsedAt` does gate a customer decision against a re-issue, because the decision writes it. It gates nothing between two writers that both leave it alone.

**The rule, sharpened:** a concurrency guarantee comes from **the column the operation actually writes**, never from the presence of a token on the row. The first design inferred protection from the token existing — the same shape of error `CLAUDE.md` §21 already warns about, one level further in.

### 9.3 The accepted concurrency model

| Race | Gate | Why it works |
|---|---|---|
| Customer decision vs. re-issue | **`UsedAt`** | The decision writes it |
| Re-issue vs. re-issue | **`ExpiresAt`** | The re-issue writes it |

Both readers see the same original `ExpiresAt`; the first commits; the second matches zero rows; EF throws; `UnitOfWork` translates to `ConflictException` → **409**. **The loser's new link is never persisted**, because EF wraps its `UPDATE` and `INSERT` in one batch and rolls the whole batch back — the property D96 already recorded, applied to the correct column.

It also **strengthens** the customer path: a decision arriving through a link superseded mid-flight now conflicts deterministically rather than committing against a replaced credential.

**Rejected mechanisms**, each on its own terms: a pessimistic row lock satisfies the invariant but *chains* rather than refuses, so the customer receives two emails and the first link is dead on arrival; a unique filtered index is the only schema-level guarantee but cannot be expressed on today's columns, because expiry is time-relative and `GETUTCDATE()` is non-deterministic in a filtered-index predicate — adopting it would reopen the `RevokedAt` column Q3 closed, and it is a deliberate non-goal here; serializable isolation buys nothing the chosen mechanism does not, at a higher cost.

### 9.4 The trap in Q2, recorded before it can be walked into

**`TokenLink.Expire()` must always write, even when the link is already expired.** An implementation that skips the write when `IsExpired` is already true would cause EF to issue no `UPDATE`, the concurrency predicate would not exist, and the serialisation would silently vanish for exactly the case Q2 added. The write *is* the guard. Pinned by a test, not by this paragraph.

### 9.5 Scope and implementation order

**In scope:** `TokenLink.Expire()`; `ExpiresAt` as a concurrency token (migration #13, empty `Up`/`Down`); `ResendAngebotCommand` + validator + handler; `POST /api/v1/angebote/{id}/resend` (Admin-only, no token in the response); one new `ITokenLinkRepository` method; a new `AuditAction`; the Dashboard's confirmed "Link erneut senden" action behind a tested `canResend` flag.

**Out of scope:** any schema state beyond the concurrency token, a filtered unique index, `RevokedAt`/`IsCurrent`, changing `SentAt`, OQ-4's revise-and-resend, and all Slice 7+ work. **`NotificationRetryExecutor` needs no change** — it already takes the newest link and refuses an expired or used one; this slice makes that foresight load-bearing, so a test pins it.

**Three commits, each stopping for review:** documentation + D99 → Domain + Application + API + migration + tests → Dashboard + browser QA.

### 9.6 What the Dashboard's browser QA found

**Every confirmation dialog on the Angebot screen stayed open when the server refused the write.** `perform()` ran its caller's callback — which is what closes the dialog — only in the `next` branch, so a 409 left the confirmation sitting on top of the message explaining the refusal, one click from an identical second refusal. That is exactly the rule `CLAUDE.md` §23 already records, learned from the notification-retry dialog; the Angebot screen had never been driven against a refusal, so it had gone unnoticed across all four confirmed actions (submit, approve, send, and now resend) rather than one.

**The first fix was wrong, and the second QA case is what caught it.** Making the callback run on both outcomes fixed the confirmations and silently broke the *form* dialogs, which pass the same parameter to close themselves — an ordinary 400 on a section title would have closed the form and discarded what the user had typed, along with the message telling them to fix it. `perform()` therefore takes **two** callbacks, and the split is the rule rather than an accident of the signature: `onSuccess` closes a form dialog, on success only, because a refusal there is the user's own input still waiting to be corrected; `onSettled` closes a confirmation, on both outcomes, because a refusal is terminal for that click and there is nothing in the dialog to preserve. The reload stays success-only either way. Both halves are pinned by a browser case — a refused resend closing its confirmation, and a refused section keeping `Dachgeschoss` in its field.

**Found by driving the built Dashboard against a stub that returns 409, not by review** — the same shape as every other Phase 10/11 QA finding: the defect is invisible while only the happy path is exercised, and a green suite is a precondition for QA rather than a substitute for it. `canResend` itself is covered exhaustively over every status and role in `angebot-capabilities.spec.ts`, which is what keeps it agreeing with the handler's own `Sent`-only check.

### 9.7 Closure

**Approved and closed 2026-09-05**, across four review checkpoints: the design (with the concurrency flaw the Product Owner found in it), Commit 1's documentation, Commit 2 under a hold that demanded code-level evidence rather than a summary, and Commit 3 with a focused re-review of the shared `perform()` change because it altered Submit/Approve/Send outside the new feature.

| Commit | Contents |
|---|---|
| `4df0750` | Documentation + D99 |
| `fb73e11` | Domain + Application + API + migration #13 + tests |
| `b8fe044` | Test-only fix to the race harness |
| `eb15e96` | Dashboard + the `perform()` split |

**CI green on `eb15e96`, both jobs.** Infrastructure 412/412 and Api 464/464 against real Windows/LocalDB; Domain, Application and Website green on Linux; 81/81 in the Dashboard's own suite locally, since CI does not build the Dashboard. Build 0 errors / 0 warnings, `has-pending-model-changes` clean.

**The published bundle the browser QA ran against was verified rather than assumed.** Its timestamp was *older* than the source files', which a `git stash`/`stash pop` during a formatting comparison had rewritten with identical content. Rather than reason about that, the committed source was built to a separate directory and every emitted JS chunk hashed identically to the bundle under test. **A build output's mtime is not evidence about its provenance** — an ordinary git operation can invert it — so where a QA result depends on which code was running, compare the artefacts.

**Merged as `314f486`** on 2026-09-05, a true merge commit whose parents are `5514b17` (Slice 5's merge) and `98e8b97` (this slice's head).

**What Slice 6 leaves for later, unchanged:** OQ-4's revise-and-resend, a filtered unique index (Mechanism 3, explicitly declined for this slice), and everything in Slice 7+.
