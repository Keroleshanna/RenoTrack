# PHASE4_PROGRESS.md — Phase 4 (API Layer) Slice Log

**Purpose:** the detailed, non-summarized record of every Phase 4 vertical slice — its goal, the design discussion that preceded it, what was built, what documentation changed, what tests were added, and how it ended. Same role `PHASE3_PROGRESS.md` played for Phase 3. `PROJECT_STATE.md` remains the current-state snapshot; this file is the narrative.

**Branch:** `feature/phase-4-api-auth-leads-inspections` (off `main` at `babfff9`).

---

## Phase 4 Scope (agreed before any code)

Confirmed against `PROJECT_ROADMAP.md`'s own Phase 4 entry rather than the broader "the API layer" wording in `PROJECT_STATE.md` §9 / `HANDOFF_PROMPT.md`. Phase 4 covers **only**:

- API foundation (routing conventions, documentation surface, test harness)
- Authentication (JWT login)
- Lead endpoints
- Inspection endpoints
- Global exception handling (RFC 7807 ProblemDetails)
- `LocalDiskFileStorage`
- `AddApplication()` DI extension

Angebot/Catalog endpoints (Phase 5), token links (Phase 6), Projects (Phase 7), Invoices (Phase 8) are explicitly **not** Phase 4.

### Agreed slice order

| # | Slice | Status |
|---|---|---|
| 1 | API foundation, conventions & docs | ✅ done |
| 2 | Global exception-handling middleware | ✅ done |
| 3 | `AddApplication()` DI extension | ✅ done |
| 4 | Authentication — JWT login | ✅ done |
| 5 | Lead creation (public) | ✅ done |
| 6 | Lead read endpoints | not started |
| 7 | Inspection scheduling | not started |
| 8 | Inspection photo upload + `LocalDiskFileStorage` | not started |
| 9 | Inspection completion | not started |
| 10 | Lead status update (Won/Lost) | not started |
| 11 | Migration-application strategy | not started |

Three orderings changed from the first draft, each for a stated reason: exception middleware moved ahead of `AddApplication()` (it has no dependency on handlers/validators being resolvable); `LocalDiskFileStorage` folded into the photo-upload slice that consumes it rather than standing alone several slices earlier (a real implementation with no caller is the same speculative-growth mistake §4 forbids); Lead status update moved after the Inspection slices, which is what revealed that `MarkInspectionScheduled`/`MarkInspectionDone` are already driven as side effects of the Inspection commands and `MarkAngebotInProgress`/`MarkAngebotSent` belong to Phase 5 — leaving `MarkWon`/`MarkLost` as the only transitions that endpoint can legitimately drive in Phase 4.

---

## Slice 1 — API Foundation, Conventions & Documentation

**Goal:** establish the API's routing and documentation conventions and stand up `RenoTrack.Api.Tests` as a working integration-test harness, so every later slice has both a place to put endpoints and a proven way to test them over real HTTP. No business endpoints.

### Design review findings (from reading the actual code, not the planning docs)

Three things were true in the repository that none of the handoff documents recorded, and each changed the slice:

1. **`Program` was not reachable from the test project.** Top-level statements compile to an `internal partial class Program`, so `WebApplicationFactory<Program>` could not name it.
2. **Startup role-seeding collided with the deferred migration decision — in Slice 1, not Slice 11.** `Program.cs` unconditionally runs `IdentityRoleSeeder.SeedRolesAsync()` at startup. `WebApplicationFactory` boots the real `Program.cs`, so the very first test to start the app would execute that seeder against a database whose schema had never been created (all four migrations still Pending). This is the failure `HANDOFF_PROMPT.md` predicted, arriving ten slices early. It did **not** force the Slice 11 decision forward — the test fixture owns its own schema — but it had to be handled deliberately.
3. **CI ran `Api.Tests` on the Linux job.** Giving `Api.Tests` a LocalDB dependency would have broken CI on the first PR — the same class of failure as D56.

Also found: `MapOpenApi()` is guarded by `IsDevelopment()`, while `WebApplicationFactory` defaults the host environment to `Production` — so a smoke test asserting the OpenAPI document would have failed without an explicit `UseEnvironment`.

### Design decisions

- **D57 — URL-segment versioning, no versioning library.** Literal `[Route("api/v1/[controller]")]`; `Asp.Versioning.Mvc` rejected as speculative. Also settled: `[Authorize]` by default, `[AllowAnonymous]` per action.
- **D58 — `Api.Tests` strategy.** Real pipeline via `WebApplicationFactory<Program>`, real LocalDB, no mocking framework, no fake Identity store; schema via `Database.MigrateAsync()`.
- **`InternalsVisibleTo`, not `public partial class Program`** — grants one named assembly access rather than widening the public surface, matching the D7 precedent for `RenoTrack.Domain.Tests`.
- **Scalar for the documentation UI**, layered on the existing `AddOpenApi`/`MapOpenApi` rather than adopting Swashbuckle as a second, overlapping document-generation stack. Explicitly signed off by the user, since it adds a third-party package to the shipped API project.
- **The JWT bearer scheme is declared in the OpenAPI document now, though nothing enforces it until Slice 4.** Recorded openly in the transformer's own doc comment so it is not later mistaken for a security gap.
- **No controller and no health/ping endpoint were created.** Inventing an endpoint solely to give the smoke test something to call would violate the on-demand discipline; the OpenAPI document is already-intended behavior and serves the same purpose.
- **Test-user seeding deferred to Slice 4.** This reversed the assistant's own earlier roadmap wording, which had listed seeded Admin/Inspector users as a Slice 1 deliverable — nothing in Slice 1 consumes them, and their first real consumer is the login endpoint.

#### `MigrateAsync` vs `EnsureCreated` — the one decision that changed under review

The design initially proposed `EnsureCreatedAsync()`, reasoning from `RenoTrackDbContextFixture`'s precedent. The user challenged this and asked for a comparison across schema fidelity, production alignment, interaction with the existing migration tests, and long-term maintenance. The comparison reversed the proposal:

- **Fidelity:** equivalent *today* — verified directly, no migration in this repo contains `migrationBuilder.Sql`, `InsertData`, or `HasData`, and `InitialCreateMigrationTests` proves migrations match the model. But that equivalence is a property of the current migrations, not a guarantee.
- **Production alignment:** the decisive asymmetry. `Infrastructure.Tests` constructs a `DbContext` directly and never runs `Program.cs`; `Api.Tests` boots the real application, which in production always runs against a migrated database. The precedent does not transfer, and had been applied without checking whether it should.
- **Migration coverage:** `EnsureCreated` would leave `InitialCreateMigrationTests` as the only place migrations are ever executed.
- **Maintenance:** `EnsureCreated` never writes `__EFMigrationsHistory`. If Slice 11 lands on startup-time `Database.MigrateAsync()`, that call — executed by `WebApplicationFactory` via the real `Program.cs` — would find zero applied migrations against existing tables and fail on `CREATE TABLE`, forcing a rewrite of this fixture. `MigrateAsync` is correct under **both** possible Slice 11 outcomes.

A third decision entry for the fixture's schema mechanism was considered and deliberately **not** created, at the user's direction: it is a test-harness implementation detail, not a cross-cutting architectural rule, and padding `ARCHITECTURE_DECISIONS.md` with implementation minutiae dilutes it. The reasoning lives in `RenoTrackApiFactory`'s XML doc comment and here.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Api/RenoTrack.Api.csproj` | `Scalar.AspNetCore` 2.16.17; `<InternalsVisibleTo Include="RenoTrack.Api.Tests" />` |
| `src/RenoTrack.Api/OpenApi/BearerSecuritySchemeTransformer.cs` | New — declares the JWT bearer scheme in the generated document |
| `src/RenoTrack.Api/Program.cs` | Registers the transformer via `AddOpenApi(options => ...)`; maps `MapScalarApiReference()` inside the existing Development guard |
| `tests/RenoTrack.Api.Tests/RenoTrack.Api.Tests.csproj` | `Microsoft.AspNetCore.Mvc.Testing`; explicit `ProjectReference` to `RenoTrack.Infrastructure` (the fixture uses `RenoTrackDbContext` directly — explicit, not transitive, per D2 and the Phase 3 review finding) |
| `tests/RenoTrack.Api.Tests/RenoTrackApiFactory.cs` | New — `WebApplicationFactory<Program>` + `IAsyncLifetime`; owns schema create/drop, connection-string override, `UseEnvironment("Development")`; plus the `[CollectionDefinition("Api")]` fixture |
| `tests/RenoTrack.Api.Tests/ApiFoundationTests.cs` | New — 3 smoke tests |
| `.github/workflows/ci.yml` | `Api.Tests` moved off the Linux job; Windows job renamed `infrastructure-tests` → `database-backed-tests` and now runs both database-backed suites |

A minor implementation correction: `Microsoft.OpenApi` 2.x flattened its namespaces, so the transformer imports `Microsoft.OpenApi`, not `Microsoft.OpenApi.Models`, and `SecuritySchemes` is `IDictionary<string, IOpenApiSecurityScheme>`.

### Tests

Four, all in the shared `"Api"` collection:

1. `Application_starts_successfully` — creating the client builds and starts the real host, which also proves `Program.cs`'s Identity role seeding runs successfully against the migrated test database (finding 2 above, now covered rather than merely avoided).
2. `OpenApi_document_is_served` — `GET /openapi/v1.json` returns 200 and a parseable document.
3. `Scalar_api_reference_ui_is_served` — `GET /scalar/v1` returns 200.
4. `OpenApi_document_declares_the_bearer_security_scheme` — asserts `type`/`scheme`/`bearerFormat`.

Test 3 was added during Slice 1's review, at the user's request. The original design specified three tests, and the Scalar UI endpoint had been verified only manually (via a temporary probe deleted before commit) and flagged as uncovered. The user's reasoning — the documentation UI is a deliverable of this slice, so CI should protect it from accidental removal — was correct, and the gap was one the assistant had surfaced but not closed on its own initiative. `/scalar/v1` is Scalar's own default route (`/scalar/{documentName}` for the `v1` document); `MapScalarApiReference()` is called with no route argument, so nothing about that path is a project-specific choice.

Per `CLAUDE.md` §14's rule that a single green run proves little for anything involving shared external state, the suite was run three consecutive times — passing every time — both before and after the fourth test was added.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **375 passing, 0 failing** (153 Domain + 144 Application + 74 Infrastructure + **4 Api**). `RenoTrack.Api.Tests` is no longer an empty project.

---

## Slice 2 — Global Exception-Handling Middleware

**Goal:** every exception escaping a command/query handler becomes an RFC 7807 `ProblemDetails` response (`Architecture.md` §5.3), so every controller from Slice 4 onward stays thin and never contains a `try`/`catch`. Resolves the HTTP status-code mapping deferred since Phase 2 (`CLAUDE.md` §17, `NEXT_STEPS.md` §5).

**Out of scope:** authentication challenge responses (401/403 emitted by auth middleware rather than thrown — nothing wires auth until Slice 4); any real controller.

### Design review

Grounded in a direct count of what the codebase actually throws, rather than reasoning abstractly about BCL types:

| Type | Occurrences in backend `src` | Where |
|---|---|---|
| `ArgumentException` | 17 | Domain guards |
| `NotFoundException` | 15 | Application handlers |
| `InvalidOperationException` | 9 | 7 Domain guards + 2 Infrastructure (**both startup-only**: DI connection-string validation, role-seeding failure) |
| `ForbiddenException` | 3 | Application handlers |
| `ConflictException` | 1 | `CreateAngebotCommandHandler` |

That count is what made the central decision defensible rather than a guess — see D59.

**Decisions:**

- **One `IExceptionHandler` with an explicit `switch`**, not one handler per exception type (chained handlers make registration order silently significant and scatter the table across six files — the "hidden pipeline" property D22 rejected MediatR for), and not a hand-written middleware (`IExceptionHandler` + `AddProblemDetails()` is the framework's own seam and yields `application/problem+json` content negotiation for free).
- **`ArgumentException`→400, `InvalidOperationException`→409, with logging mitigation.** The masking risk was raised explicitly during review rather than inherited from `CLAUDE.md` §17's provisional lean: both are BCL-wide types, and EF Core throws `InvalidOperationException` for tracking conflicts, so a real infrastructure fault could surface as a plausible-looking 409. Three alternatives were weighed and rejected — unmapped→500 (plainly wrong for the 24 Domain guards that exist today), dedicated Domain exception types (reopens a settled decision and modifies the stable Domain baseline on a hypothetical risk), and mapping by originating assembly via `ex.TargetSite` (reflective, silently degrades when null, forces a Domain reference into `RenoTrack.Api`). The user approved the mapping on the strength of the occurrence count above, with the explicit position that it should be reopened with concrete evidence of a real masking incident rather than pre-emptive complexity.
- **Message-leakage asymmetry.** Mapped exceptions surface their message as `detail`; unmapped ones get a fixed generic title and no `detail` member at all.
- **`traceId` in `CustomizeProblemDetails`, not in the handler** — so it covers ProblemDetails responses ASP.NET produces itself with no exception involved.
- **Explicit `FluentValidation` package reference added to `RenoTrack.Api`** — it catches `ValidationException` by type; the transitive reference through `RenoTrack.Application` would have compiled, but D2's discipline is to declare what a project actually uses.
- **`OperationCanceledException` deliberately left out of scope**, at the user's direction: it belongs to the hosting/runtime layer, not to Domain/Application exception mapping, and should be addressed with real evidence of log noise rather than speculation.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Api/ErrorHandling/ProblemDetailsExceptionHandler.cs` | New — the whole mapping table in one `switch`, plus the logging mitigation |
| `src/RenoTrack.Api/Program.cs` | `AddProblemDetails` (with `traceId`/`instance`), `AddExceptionHandler`, `UseExceptionHandler()` first in the pipeline |
| `src/RenoTrack.Api/RenoTrack.Api.csproj` | Explicit `FluentValidation` 12.1.1 |
| `tests/RenoTrack.Api.Tests/RenoTrack.Api.Tests.csproj` | Explicit `ProjectReference` to `RenoTrack.Application` + `FluentValidation` (the test controller throws these types by name) |
| `tests/RenoTrack.Api.Tests/ErrorHandling/TestErrorsController.cs` | New — test-only, documented as such |
| `tests/RenoTrack.Api.Tests/ErrorHandling/ProblemDetailsExceptionHandlerTests.cs` | New — 13 tests |

`TestErrorsController` lives in the **test assembly**, not in `RenoTrack.Api` behind a conditional, which makes shipping it structurally impossible rather than merely unlikely. It reaches the application only because the test class registers this assembly as an MVC `ApplicationPart` via `WithWebHostBuilder`/`ConfigureTestServices`; nothing in `Program.cs` knows the type exists. It is routed under `api/test-errors`, deliberately not `api/v1/...`, so it can never collide with a real route or imply membership in the versioned public surface. At the user's request during design review, it carries a prominent comment stating it exists solely to exercise the exception pipeline in integration tests and is never part of the production application.

### Tests

13 new (17 in `RenoTrack.Api.Tests` total), all through the real MVC pipeline rather than unit-testing the handler against a synthetic `HttpContext`:

- 5 (theory) — each mapped exception yields its documented status and title.
- 4 (theory) — each message-carrying mapped exception surfaces its message as `detail`.
- 1 — `ValidationException` → 400 with a field-keyed `errors` dictionary, asserting specifically that **two failures on the same property group under one key** rather than overwriting each other.
- 1 — an unmapped exception yields 500, and neither the fake password nor the fake host in its message appears anywhere in the response body, with no `detail` member present at all.
- 1 — `traceId` is populated.
- 1 — the response content type is `application/problem+json`.

Two compile errors were hit and fixed during implementation, neither affecting the design: `ConfigureTestServices` needs `using Microsoft.AspNetCore.TestHost`, and a collection-expression `Assert.Equal` overload could not infer `string?` (replaced with `Assert.Single`).

Api suite run three consecutive times per `CLAUDE.md` §14 — 17/17 each time.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **388 passing, 0 failing** (153 Domain + 144 Application + 74 Infrastructure + **17 Api**).

---

## Slice 3 — `AddApplication()` DI Extension

**Goal:** make every Application-layer service resolvable from the composition root, so Slice 4's first controller can constructor-inject a handler. Before this slice nothing in `RenoTrack.Application` was registered anywhere — `AddInfrastructure()` deliberately excluded all of it, and said so in its own doc comment.

### Exact inventory (counted from the code, not estimated)

| Kind | Count |
|---|---|
| `IValidator<T>` (`AbstractValidator<T>` subclasses) | 14 |
| `ICommandHandler<TCommand, TResult>` | 14 |
| `IQueryHandler<TQuery, TResult>` | 1 |
| `IOwnershipValidator` → `OwnershipValidator` | 1 |
| **Total** | **30** |

15 handlers but only 14 validators — the gap is `SearchCatalogItemsQuery`, which takes no parameters and so has nothing to shape-validate (D37). The asymmetry is correct, not a missing file. The reflection-based test independently rediscovered exactly 15 handlers and 14 validators, confirming the inventory.

### Design review

**No new `ARCHITECTURE_DECISIONS.md` entry.** Agreed with the user: this is `AddInfrastructure()`'s already-settled conventions (explicit registrations, uniform Scoped lifetime, composition owned by the layer it composes) applied consistently to a second layer — not a new cross-cutting rule. Recording it would be the same padding rejected for Slice 1's fixture mechanism.

**Decisions:**

- **Explicit registrations, no assembly scanning.** `AddValidatorsFromAssemblyContaining<T>()` (one line for 14 validators) and Scrutor (scanning both) were both considered and rejected — a new package plus reflective magic in production, in a codebase that rejected MediatR (D22) and generic catch-all abstractions (D28) on exactly this principle. The user added a framing worth recording: the explicit list *is* documentation of the application's capabilities.
- **A reflection-based test as the safety net.** The genuine argument for scanning is "someone adds a 16th handler and forgets to register it" — and that risk is worse than it first appears: **`ValidateOnBuild` would not catch it today**, because no controller depends on the handlers yet, so a missing registration would sit undetected until a later slice wired it up. The resolution is reflection in the *test*, explicit registration in production (`CLAUDE.md` §14 sanctions the former specifically). `DependencyInjectionTests` discovers every handler/validator in the Application assembly and asserts each resolves.
- **Handlers registered by interface**, not concrete type. The counter-argument was raised honestly — injecting `ICommandHandler<CreateLeadCommand, LeadDto>` means "Go to Definition" lands on the interface rather than the handler, slightly against §3's traceability goal. It was rejected because §3 names that interface "the only shared abstraction"; never injecting it would reduce it to a decorative marker every handler implements for no runtime purpose.
- **Uniformly Scoped**, matching `AddInfrastructure()`'s stated uniform-lifetime rule. Validators and `OwnershipValidator` are stateless and would be safe as Singletons, but one rule removes a class of captive-dependency mistakes before it can happen (D48's reasoning for the dependency-free placeholders).
- **`AddApplication(this IServiceCollection services)` takes no `IConfiguration`.** `AddInfrastructure` needs one for the connection string; Application has nothing configurable, and adding the parameter for symmetry would be speculative (§4).
- **`Microsoft.Extensions.DependencyInjection.Abstractions` added to `RenoTrack.Application`** — flagged explicitly during review rather than slipped in, since it is the first non-FluentValidation package that layer takes on. Approved: it is a DI contract package with no framework or hosting baggage, and keeping the composition extension owned by the layer it composes is cleaner than moving that knowledge into the Api project (the alternative, rejected).

**Two user requests, both implemented:** registrations grouped by category (validators → command handlers → query handlers → services) rather than alphabetically or by creation order, each category in its own private method with its own explanatory summary; and a file-header comment stating this is the Application layer's composition root, registers Application services only, depends solely on DI abstractions, and must not accumulate hosting or configuration concerns.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Application/DependencyInjection.cs` | New — 30 explicit registrations in four categorized private methods |
| `src/RenoTrack.Application/RenoTrack.Application.csproj` | `Microsoft.Extensions.DependencyInjection.Abstractions` 10.0.10 |
| `src/RenoTrack.Api/Program.cs` | `builder.Services.AddApplication();` ahead of `AddInfrastructure(...)` |
| `tests/RenoTrack.Api.Tests/DependencyInjectionTests.cs` | New — 31 tests |

One naming collision surfaced during implementation: both `RenoTrack.Application` and `RenoTrack.Infrastructure` declare a `DependencyInjection` class, so `typeof(DependencyInjection)` was ambiguous in the test. Resolved by anchoring assembly discovery on `typeof(ICommandHandler<,>).Assembly` instead — which also reads better, since that interface is precisely what the test is scanning for.

### Tests

31 new (48 in `RenoTrack.Api.Tests` total):

- 1 — the container builds with `ValidateOnBuild`/`ValidateScopes` enabled.
- 15 (theory, discovered by reflection) — every `ICommandHandler<,>`/`IQueryHandler<,>` in the Application assembly resolves.
- 14 (theory, discovered by reflection) — every `IValidator<>` in the Application assembly resolves.
- 1 — `IOwnershipValidator` resolves to an implementation from the **Application** assembly, pinning `CLAUDE.md` §9's placement rule rather than merely that something resolves.

**The safety net was proven to fail, not assumed to work.** A reflection-discovery test that silently found zero types would pass vacuously, so the `CreateLeadCommandHandler` registration was temporarily removed: the suite failed with exactly one test naming the missing registration (`No service for type ... ICommandHandler<CreateLeadCommand, LeadDto>`), then passed again once restored. The counts (15/14) independently matching the hand-counted inventory is the second confirmation.

Api suite run three consecutive times per `CLAUDE.md` §14 — 48/48 each.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **420 passing, 0 failing** (153 Domain + 144 Application + 74 Infrastructure + **49 Api**).

---

## Slice 4 — Authentication (JWT Login + Refresh)

**Goal:** real dashboard authentication — login, JWT issuance, refresh-token rotation, and the bearer-token validation every protected endpoint from Slice 5 onward will depend on.

The design review deliberately focused on the **authentication model** rather than controller wiring, at the user's direction: the contract, the claims, the refresh-token storage model, rotation/revocation, lifetimes, password verification, and how all of it meets the Identity storage Phase 3 built. The controller was the mechanical part once those were settled.

### Constraints found in existing code that bounded the design

- `ApplicationUser.IsActive` existed since Slice 15 and **nothing read it** — authentication is its first real consumer.
- `AddIdentityCore` is registered and `AddDefaultTokenProviders()` deliberately omitted; more importantly **`SignInManager` is not registered** (D54, avoiding cookie-auth defaults), which turns out to matter for lockout.
- `IdentityRoleSeeder` seeds only the two roles. **No user exists in any environment**, and no code path creates one.

### Decisions (all recorded as D60)

- **Login lives in the API layer, not as an Application command.** The single deliberate exception to §3, because authentication has no aggregate, invariant, transition, or audit milestone. The rejected alternative — `LoginCommand` + an `IIdentityService` abstraction over Identity — would exist purely so a layer with no business rules about authentication could appear to own it. At the user's explicit request, D60 leads with *why*, so a future contributor who notices the inconsistency reads the reasoning before "fixing" it.
- **Persisted refresh tokens, SHA-256 hash only.** Plaintext returned once, never stored. Stateless JWT refresh tokens were rejected (unrevocable, defeating the point), as was having no refresh token at all (contradicts Architecture §7.1).
- **Rotation on every use, with chain revocation on reuse.** A revoked token being presented revokes every outstanding token for that user.
- **Retention until `ExpiresAt`, no cleanup job** — decided consciously at the user's request rather than left to accumulate; see below.
- **Lockout implemented, not deferred.** The user's position was explicit: Phase 4 must not ship authentication that ignores a documented security requirement.
- **Identical 401 for every login failure**, with the real reason logged server-side.
- **15-minute / 7-day lifetimes from configuration**, validated eagerly at startup.

#### The refresh-token lifecycle question, answered before implementation

The user asked for a conscious decision rather than accidental accumulation. The analysis:

A row carries information only until `ExpiresAt`. Revoked-but-unexpired rows **must** be kept — they are exactly what makes reuse detection possible — but past expiry a token is rejected on expiry grounds regardless of revocation state, so the row is dead weight. **Retention is therefore until `ExpiresAt`**, and anything older is deletable at any time with zero behavioural change.

Volume: 15-minute access tokens mean an active user produces ~32 rows per working day; at a 7-day window steady state is about `users × 32 × 7` — a few hundred rows at this company's real staff count, a few thousand at twenty users. **No cleanup mechanism was built**, because building one for that volume would be solving a non-problem; the revisit trigger (tens of thousands of rows, or an order-of-magnitude user increase) and the fix (a background job deleting rows past `ExpiresAt`) are both recorded in D60 and in `RefreshToken`'s own doc comment. `CLAUDE.md` §2's "never delete a historical record" was checked and found not to apply — it governs business records, not authentication mechanisms.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Infrastructure/Persistence/Entities/RefreshToken.cs` | New — Infrastructure-only entity; hash/generate helpers; `Revoke` is idempotent so re-revoking never overwrites the original timestamp |
| `src/RenoTrack.Infrastructure/Persistence/Configurations/RefreshTokenConfiguration.cs` | New — `nchar(64)` fixed-length hash, unique index on it, index on `UserId`, real FK to `AspNetUsers` with `Restrict` |
| `src/RenoTrack.Infrastructure/Persistence/RenoTrackDbContext.cs` | `RefreshTokens` DbSet |
| `src/RenoTrack.Infrastructure/Persistence/Migrations/*_AddRefreshTokens.cs` | New — the fifth migration |
| `src/RenoTrack.Infrastructure/Identity/JwtOptions.cs` | New — settings + eager `Validate()` naming the exact failing key |
| `src/RenoTrack.Infrastructure/Identity/ITokenService.cs` | New — `IssueAsync`/`RotateAsync` + `TokenPair` |
| `src/RenoTrack.Infrastructure/Identity/TokenService.cs` | New — issuance, rotation, reuse detection, inactive-user revocation on refresh |
| `src/RenoTrack.Infrastructure/DependencyInjection.cs` | New `AddJwtAuthentication` extension |
| `src/RenoTrack.Api/Controllers/AuthController.cs` | New — login + refresh |
| `src/RenoTrack.Api/Auth/Dtos/AuthDtos.cs` | New |
| `src/RenoTrack.Api/Program.cs` | `AddJwtAuthentication`, `UseAuthentication()` before `UseAuthorization()` |
| `src/RenoTrack.Api/appsettings.Development.json` | `Jwt` section (dev-only key) |
| `tests/RenoTrack.Api.Tests/RenoTrackApiFactory.cs` | Seeds four known-password users via the real `UserManager` |
| `tests/RenoTrack.Api.Tests/Auth/TestProtectedController.cs` | New — test-only protected endpoint |
| `tests/RenoTrack.Api.Tests/Auth/AuthenticationTests.cs` | New — 13 tests |

**Migration process followed per §21:** `ERD.md` gained its `RefreshTokens` row and two index rows *before* the migration was generated (documentation-first, §15); the generated migration was then manually reviewed (one table, correct `Restrict` FK, no cascade, both indexes, clean `Down`); `has-pending-model-changes` confirmed no drift afterwards.

**A CI-breaking bug caught before pushing.** The test host initially read its JWT settings from `appsettings.Development.json` — which is **gitignored** (it holds a signing key). The suite therefore passed locally purely because that file existed on this machine, and would have failed every Api test on CI's fresh clone, where `AddJwtAuthentication` throws at startup for a missing `Jwt:Issuer`. Fixed by having `RenoTrackApiFactory` supply its own JWT settings via `UseSetting`, exactly as it already does for the connection string, with `AuthenticationTests` referencing the factory's constants rather than duplicating them (so the expired-token test can never drift into passing for a signature mismatch instead of the expiry it means to prove). **Verified by temporarily removing the gitignored file and re-running: 62/62 still pass.** Note this means a developer running the API itself still needs a local `appsettings.Development.json` containing a `Jwt` section — the tests no longer do.

**Two design corrections during implementation, both real improvements:**

1. `AuthController.Refresh` initially re-parsed the access token it had just been handed in order to recover the user id. Replaced by carrying `UserId` on `TokenPair` — a service that just issued a token should state who it belongs to, not make the caller deserialize it back out.
2. `ITokenService` was first registered in `AddInfrastructure`, which **broke the Slice 3 DI test immediately**: `TokenService` depends on `JwtOptions`, supplied only by `AddJwtAuthentication`, so `ValidateOnBuild` failed the whole container. This was a genuine cohesion bug, not a test gap — `AddInfrastructure` was advertising a service that could not be constructed. The registration moved to `AddJwtAuthentication` alongside the options it needs, and the DI test now composes all three extensions exactly as `Program.cs` does. **The Slice 3 safety net caught a Slice 4 mistake within minutes of it being made**, which is the clearest evidence yet that it was worth building.

### Tests

13 new (62 in `RenoTrack.Api.Tests` total), covering every behaviour the user asked for:

| Behaviour | Test |
|---|---|
| Successful login | returns tokens + user details, with a future `expiresAt` |
| Claims | `sub`/`email`/`name`/`jti`/`role` present; asserts **no** ownership-style claim, pinning §16 |
| Wrong password | 401 |
| Unknown email | 401 **byte-identical to the wrong-password response**, pinning non-enumeration |
| Inactive user | 401 despite a correct password |
| Locked-out user | 5 deliberate failures, then 401 **with the correct password** — proves `AccessFailedAsync` is actually called |
| Refresh rotation | new pair differs; the rotated-away token stops working |
| Reuse of a revoked token | replay is rejected **and the legitimate current token dies too** (chain revocation) |
| Unknown refresh token | 401 |
| Valid access token | reaches a protected endpoint |
| No token | 401 |
| Invalid signature | forged token with a different key → 401 |
| Expired access token | signed with the *real* key so only expiry can reject it → 401 |

`TestProtectedController` was needed because as of this slice the only endpoints are `/auth/login` and `/auth/refresh`, both anonymous — nothing existed to reject a bad token against. Same test-assembly/`ApplicationPart` pattern as Slice 2's `TestErrorsController`, and its doc comment says to delete it once real protected endpoints make it redundant.

Api suite run three consecutive times per `CLAUDE.md` §14 — 62/62 each. This mattered more than usual: the lockout and chain-revocation tests mutate shared database state, so a single passing run would not have proven order-independence.

### Known gap carried forward (not a defect of this slice)

**No user exists in production, and nothing creates one.** `IdentityRoleSeeder` seeds roles only. After this slice the API has a working login endpoint that nobody can use until either OQ-1 (does Admin manage Inspector accounts?) is resolved or a seeding path is added. Flagged during design review rather than discovered later; it is an SRS-level open question, not something Slice 4 should have silently invented an answer to.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **433 passing, 0 failing** (153 Domain + 144 Application + 74 Infrastructure + **62 Api**). `dotnet ef migrations has-pending-model-changes` → no pending changes.

---

## Slice 5 — Lead Creation (Public)

**Goal:** the first business endpoint — `POST /api/v1/leads`, the public website contact form (SRS FR-1.3, Sequence Diagram §1) — proving routing, DI, and the exception middleware work together on real Application-layer work.

Per the agreed approach, the design review concentrated on the HTTP surface (contract, routes, validation boundary, status codes, idempotency, public-endpoint security, logging/audit, tests) rather than the handler, **and began by reading the existing `CreateLeadCommandHandler`, command, validator, and `LeadDto`** — the standing instruction being that the endpoint contract is driven by the existing use case, not the reverse.

**Result of that reading: nothing in the Application layer changed.** The handler, command, validator, and DTO are all untouched. The only new type is a request record, and it exists for a security reason rather than a shape preference.

### Decisions

- **D61 — the request contract is narrower than the command.** `CreateLeadCommand` has seven parameters; `CreateLeadRequest` has five. `Source` and `CreatedByUserId` are server-derived. This is the slice's substantive decision and it generalizes into a standing rule governing every remaining slice (inspector id from the JWT, aggregate id from the route). Full reasoning in D61, including why binding the command directly — the tempting "purest" reading of "let the use case drive the endpoint" — is the wrong call: `Source` gates the FR-9.2 Admin notification, so a caller controlling it can suppress that notification.
- **Enums serialize as names, not ordinals** (recorded under D61). Decided here rather than left to the System.Text.Json default, because `LeadDto` is the first response carrying enums and a default is still a choice.
  - **Verified during review that the registration changes nothing else.** Dumping the live container's options confirmed MVC's `JsonSerializerOptions` still carries only framework defaults — camelCase naming, case-insensitive binding, `DefaultIgnoreCondition.Never`, `NumberHandling.AllowReadingFromString`, no indenting, default encoder — with `JsonStringEnumConverter` as the sole converter. The one subtlety found: ASP.NET Core keeps **two** JSON configurations, and `AddControllers().AddJsonOptions(...)` configures only MVC's. `IProblemDetailsService` writes through `Microsoft.AspNetCore.Http.Json.JsonOptions`, which still has **no** converters (camelCase preserved). No `ProblemDetails` we produce carries an enum today, so there is no current difference in behaviour — but if one ever does, it will serialize as an ordinal until that second options object is configured too. Recorded so the asymmetry is a known fact rather than a future surprise. The converter also makes request bodies *accept* enum names; no request DTO currently has an enum field.
- **Deliberately not idempotent.** Two identical submissions create two Leads; silent de-duplication would be an invented rule that discards a genuine second enquiry.
- **201 without a `Location` header**, since `GET /api/v1/leads/{id}` does not exist until Slice 6 and a `Location` pointing at a 404 is worse than none. Revisit in Slice 6.
- **No controller-side validation**, no controller-side auditing, no controller-side logging. `CreateLeadCommandValidator` already covers these fields and the Slice 2 middleware maps its failure to a field-keyed 400; the handler already logs `LeadCreated` (§10), and auditing here would double-log a business milestone.

### Deferred, deliberately (user decision)

**Rate limiting was proposed for this slice and explicitly deferred.** `Architecture.md` §12 requires "rate limiting / basic abuse protection on public endpoints … and the contact form," and this endpoint *is* the contact form. The user's reasoning: Slice 5's purpose is to deliver the first public Lead endpoint, not to introduce public-endpoint hardening infrastructure, and rate limiting, CORS, and similar concerns belong together in a dedicated hardening slice once the public endpoints actually exist. Recorded here and in `NEXT_STEPS.md` so it is a tracked commitment rather than a forgotten requirement — **`POST /api/v1/leads` is currently unthrottled and publicly reachable.**

Also flagged and left alone: `Notes` has no length cap anywhere (validator or Domain). Kestrel's 30 MB default body limit applies, which is generous for a contact form. Adding a cap would mean inventing a number no document specifies, so it is noted rather than guessed.

### Known gap (deliberate, not an omission)

**FR-2.1's Admin manual-entry path (`Source = Phone`/`Email`) is not built.** It does not appear in Architecture §5.2's endpoint table, and Slice 5's agreed scope is the public endpoint. When it arrives it is an authenticated action supplying `Source` and the Admin's id from the JWT — which is precisely why the command keeps all seven parameters even though this endpoint uses five.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Api/Leads/Dtos/CreateLeadRequest.cs` | New — five fields, with the security reasoning for the two omissions in its own doc comment |
| `src/RenoTrack.Api/Controllers/LeadsController.cs` | New — one action; `[Authorize]` at class level, `[AllowAnonymous]` on the action (D57) |
| `src/RenoTrack.Api/Program.cs` | `JsonStringEnumConverter` |

### Tests

7 new (69 in `RenoTrack.Api.Tests` total):

| Behaviour | Why it earns its place |
|---|---|
| 201 with the created resource | Happy path |
| **Server-derived fields resist injection** | Posts `source: "Phone"` and `createdByUserId: 999` anyway, asserts the Lead is still `Website` with no inspector — verifies D61 rather than trusting it |
| Requires no authentication | Proves `[AllowAnonymous]` is genuinely applied, since the controller is `[Authorize]` by default |
| Persists the Lead | Reads the row back from the database — returning a DTO proves the handler ran, only a read proves it committed |
| Invalid email → field-keyed 400 | Pins the alignment between request property names and validator error keys: the client sent `email`, so the error must return under `Email` |
| Missing required fields → 400 | Two error keys in one response |
| Two identical submissions → two Leads | Pins the non-idempotency decision, so a future "optimization" fails a test rather than silently changing behaviour |

Api suite run three consecutive times per `CLAUDE.md` §14 — 69/69 each.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **440 passing, 0 failing** (153 Domain + 144 Application + 74 Infrastructure + **69 Api**).
