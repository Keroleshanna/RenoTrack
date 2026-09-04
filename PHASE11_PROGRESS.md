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
| **1** | TokenLink concurrency protection + deterministic 409 for the losing concurrent decision + repeated race testing | ✅ **complete** — build verified locally (0 errors, 0 warnings); tests blocked by the environment, see §4.5 |
| **2** | Customer page skeleton — Razor route, server-side API client, the four states, security headers | ✅ **complete, pending CI verification** |
| **3** | Render the quote (Wireframe A3) **and its decision state** | in progress |
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
