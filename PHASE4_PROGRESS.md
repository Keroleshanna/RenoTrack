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
| 6 | Lead read endpoints | ✅ done |
| 7 | Inspection scheduling | ✅ done |
| 8 | Inspection photo upload + `LocalDiskFileStorage` | ✅ done |
| 9 | Inspection completion | ✅ done |
| 10 | Inspection notes (`PATCH`) — **redefined**; Lead Won/Lost moved to Phase 6 | ✅ done |
| 11 | Migration-application strategy | ✅ done |

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

---

## Slice 6 — Lead Read Endpoints

**Goal:** `GET /api/v1/leads/{id}` (Wireframe C1) and `GET /api/v1/leads` (Wireframe B2 pipeline, SRS FR-2.4). The first slice adding Application-layer work — neither query existed.

### Design review

Three findings from the documents shaped it:

- **`PermissionMatrix.md` §1 says the Inspector's pipeline is "filtered server-side"** — so for a collection, scoping is a filter, not a rejection. Documented, not a judgement call.
- **Wireframe B2 is a Kanban**, with a `Status / Inspector / Date range` filter row matching FR-2.4 exactly.
- **`Lead` owns no children** — `LeadRepository.GetByIdAsync` is a single `FindAsync`, no `Include`.

**Decisions:**

- **The two reads use different mechanisms, deliberately.** Single-resource: `IOwnershipValidator` on the loaded aggregate (§16's `S` rule → 403). Collection: a `WHERE` clause, because a set cannot be ownership-checked after the fact without loading every row first. Treating them uniformly was the trap.
- **The single read uses `ILeadRepository`, not a DTO-projecting query.** `IOwnershipValidator.EnsureLeadOwnership` takes the Domain entity; a projection would force either an inline `dto.AssignedInspectorId` comparison (bypassing the abstraction §9 exists to centralize) or a second ownership-aware SQL predicate (splitting one rule across two layers). The cost is nil here because Lead has no children — D36's projection-over-hydration rationale exists for `Angebot`-shaped aggregates, which Lead is the opposite of. `ILeadQueries` therefore has **no** `GetByIdAsync`.
- **403, not 404**, for an Inspector reading another's Lead. 404-to-avoid-disclosure was considered (consistent with D60's non-enumeration stance) and rejected: §16 maps `S` to `ForbiddenException`, and inventing a different disclosure posture for one endpoint without a documented requirement would be speculative rule-making.
- **Inspector scoping is decided in the controller** (D61 applied to a read): the caller's own id comes from the JWT, and whatever `assignedInspectorId` an Inspector supplies is discarded. Keeps role vocabulary out of the Application layer, where §16 says it does not belong.
- **`PagedResult<T>` and `Pagination` in `Application.Common`.** Paging limits (`FirstPage`, `DefaultPageSize` 25, `MaxPageSize` 100) live in one place at the user's request, so future list endpoints reuse them rather than re-inventing literals. `Pagination` is non-generic deliberately — constants on `PagedResult<T>` would read as though the limit varied by item type.
- **Slice 5's deferred `Location` header now lands**, since `GetById` exists to point at. A test follows the header and asserts it resolves to 200 — the whole reason it was deferred.

**Tension recorded rather than silently resolved:** Wireframe B2 is a Kanban, which does not obviously paginate, while `Architecture.md` §5.1 mandates pagination on list endpoints unconditionally. Pagination was built, on the grounds that an unbounded list endpoint is an operational hazard regardless of the UI above it.

### A real fail-open defect, caught in review before any test was written

The first implementation of `RequestingInspectorId()` returned `null` (meaning *unrestricted*) for anyone who simply was not an Inspector. The user flagged it as fail-open. It was — in four distinct ways, all pointing the same direction:

| Scenario | `IsInRole("Inspector")` | Old result |
|---|---|---|
| Role-claim mapping broken | `false` | **all Leads** |
| User seeded with no role | `false` | **all Leads** |
| Role-name typo in the seeder | `false` | **all Leads** |
| A future third role added | `false` | **all Leads** |

The defect was structural: `null` was reached by *falling through* rather than by establishing anything, so Admin was never actually verified — it was merely the absence of Inspector.

**The fix:** check Inspector first (so a mis-provisioned dual-role account is scoped, not unrestricted — when two rules could apply, the narrower wins), then Admin explicitly, then refuse outright with `ForbiddenException`. Plus `[Authorize(Roles = "Admin,Inspector")]` on the controller as defence in depth, with both layers kept deliberately: they can drift apart, and unnoticed drift means unrestricted data access.

**Demonstrated, not argued.** Two experiments:

1. Weakening the class attribute to a bare `[Authorize]` and re-running the no-role test — it still passed, proving the in-method guard stands alone rather than hiding behind the attribute.
2. Restoring the old fall-through logic *and* the weakened attribute — the no-role account got **`NotFound`**, meaning it reached the handler as an unrestricted Admin, looked up lead id 1, and simply did not find it. Had that Lead existed, the response would have been **200 with another user's data**. That is the vulnerability, reproduced.

A seeded no-role user now exists in the fixture specifically to keep this path covered.

**Also closed here: role-claim mapping had never been verified.** Slice 4 added an `[Authorize(Roles = "Admin")]` test endpoint but no test ever called it, so nothing had proven role claims survive issuance and validation — and that failure mode is silent, since a broken mapping makes `IsInRole` false everywhere and every scope check fails open. Now covered.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Application/Common/Pagination.cs` | New — the single source of paging limits |
| `src/RenoTrack.Application/Common/PagedResult.cs` | New |
| `src/RenoTrack.Application/Leads/ILeadQueries.cs` | New — list only, with the "no `GetByIdAsync`" reasoning inline |
| `src/RenoTrack.Application/Leads/Queries/GetLeadById/` | New — query, validator, handler |
| `src/RenoTrack.Application/Leads/Queries/GetLeads/` | New — query, validator, handler |
| `src/RenoTrack.Infrastructure/Persistence/Queries/LeadQueries.cs` | New — `AsNoTracking`, projection in `Select`, count before paging |
| `src/RenoTrack.Api/Controllers/LeadsController.cs` | `GetById`, `GetAll`, fail-secure scope helper, `CreatedAtAction` |
| `src/RenoTrack.Api/Auth/Roles.cs` | New — role-name constants |
| Both `DependencyInjection.cs` | 2 handlers, 2 validators, `ILeadQueries` |

**Ordering was added beyond the design and confirmed in review.** `Skip`/`Take` over an unordered query has no defined result — pages can silently repeat or omit rows — so `LeadQueries` orders by `CreatedAt` descending with `Id` as tiebreaker (`CreatedAt` is not unique). Treated as making pagination correct rather than as a new feature.

### Tests

21 new (90 in `RenoTrack.Api.Tests` total), of which 4 came free: the Slice 3 reflection DI test discovered the two new handlers and two new validators automatically and asserted each resolves.

Beyond the happy paths, the ones that matter: an Inspector is forbidden another Inspector's Lead; an Inspector's list contains only their own; **an Inspector supplying another's `assignedInspectorId` still receives their own Leads**; a no-role account is refused rather than treated as unrestricted; role claims are proven to actually reach `[Authorize]`; and the `Location` header is followed to a real 200.

Api suite run three consecutive times per `CLAUDE.md` §14 — 90/90 each.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **461 passing, 0 failing** (153 Domain + 144 Application + 74 Infrastructure + **90 Api**).

---

## Slice 7 — Inspection Scheduling

**Endpoint:** `POST /api/v1/leads/{leadId}/inspections` (SRS FR-2.3, Sequence Diagram §3), Admin only per `PermissionMatrix.md` §2 (`Schedule an Inspection | F | —`).

### Design review

**A documentation error of my own was found and corrected.** D61's Consequences claimed *"the inspector id for scheduling (Slice 7), photo upload (Slice 8), and completion (Slice 9) all come from the JWT's `sub` claim."* Reading `ScheduleInspectionCommand` showed that is wrong for scheduling: it carries **two** user ids of opposite kinds. `ScheduledByAdminId` is who is acting (server-derived, as the rule says); `InspectorId` is *who the work is assigned to*, a third party the Admin deliberately chooses. Taking it from the token would have made it impossible for an Admin to schedule anyone but themselves — the entire operation. D61 now carries an explicit correction, since as written it would have pushed a future implementer straight into that mistake.

**Decisions:**

- **`InspectionsController`, with an absolute route template for this one action.** Slices 8–9 add `POST /inspections/{id}/photos` and `/complete`; putting scheduling on `LeadsController` would split three views of one resource across two files and give `LeadsController` a dependency it has no other use for. Cohesion by resource beats cohesion by URL prefix.
- **No `IOwnershipValidator`** — `PermissionMatrix` §2 marks this `F`, and §16 says using it for an `F` action is a semantic error, not merely redundant.
- **201 without a `Location` header, expected to stay that way.** No `GET /api/v1/inspections/{id}` exists, it is absent from Architecture §5.2's endpoint table, and no agreed slice adds one.
- **`ScheduledAt` is not required to be in the future.** No document requires it, and back-dating a visit already carried out is plausible; inventing the rule here would be speculative, and it would need a numbered `BusinessRules.md` entry first.

### D62 — a real defect fixed rather than deferred

Nothing validated `InspectorId`. Because `Inspection.InspectorId` has a real FK to `AspNetUsers` (D53), a mistyped id failed at `SaveChangesAsync` with an unmapped `DbUpdateException` — **500 on an ordinary client mistake**. And the FK could not catch the two other ways the value can be wrong: a real user who is an **Admin**, or an Inspector whose account is **deactivated**. Both would have been accepted by the database and produced invalid business data.

Three options were weighed. The user rejected deferring it to the hardening slice with a clear framing: *this is not hardening, it is a business rule* — an Inspection assigned to a non-existent, non-Inspector, or deactivated account is invalid data, so the Application layer should refuse it before persistence rather than rely on a storage constraint. The user also added the `IsActive` case, which the original proposal had missed.

Implemented as `IUserQueries.IsActiveInspectorAsync` — one method rather than three, because every caller wants the same conjunction and splitting it would invite checking two of three and permitting the case the third would catch. Throws `NotFoundException` (→404) for all three cases: from the caller's point of view the resource they named — an assignable Inspector with that id — does not exist, which is honest for all three and does not disclose whether the id belongs to some other kind of account. The check runs **before** the Lead is mutated, so a rejected assignee leaves no partial state.

**Verified by reproducing the defect.** Disabling the check made the non-existent-user test return `InternalServerError` — exactly the failure it exists to prevent — then pass again on restore.

Worth recording because it looks like a contradiction and is not: D60 kept *authentication* out of the Application layer because logging in has no business rule; D62 puts a *user-related business rule* into it. Both state the same principle — business rules live in Application, mechanisms live in Infrastructure.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Application/Common/Interfaces/IUserQueries.cs` | New — one method, with the D60-consistency reasoning inline |
| `src/RenoTrack.Application/.../ScheduleInspectionCommandHandler.cs` | Eligibility check before any mutation |
| `src/RenoTrack.Infrastructure/Identity/UserQueries.cs` | New — existence check over Identity tables, not `UserManager` |
| `src/RenoTrack.Infrastructure/Identity/IdentityRoleSeeder.cs` | Role names became named constants |
| `src/RenoTrack.Api/Auth/Roles.cs` | Now forwards to the seeder's constants instead of repeating literals |
| `src/RenoTrack.Api/Controllers/InspectionsController.cs` | New |
| `src/RenoTrack.Api/Inspections/Dtos/ScheduleInspectionRequest.cs` | New — two fields, with the "assignment target, not caller identity" note |
| `tests/RenoTrack.Application.Tests/Fakes/FakeUserQueries.cs` | New |

**Role-name duplication closed as a side effect.** Adding a role predicate to `UserQueries` made the literal `"Inspector"` appear in a third place, alongside the seeder and the API's `[Authorize]` attributes. A mismatch between any two of them fails **open** for an `IsInRole` check — the exact defect shape found in Slice 6 — so the names became constants with one definition. The `Roles.cs` namespace/folder inconsistency noted at the end of Slice 6 was left alone, still cosmetic.

### Tests

13 new (2 Application, 11 API; 474 total). Beyond the happy path: BR-13's side effect is asserted **against the database**, not the response — after scheduling, the Lead really is assigned and really has moved to `InspectionScheduled`; an Inspector is refused (`PermissionMatrix` §2 grants them nothing); a second scheduling attempt is 409 via the Lead's own guard; and each of the three ineligible-assignee cases — non-existent, Admin, deactivated — is rejected, with a further test proving a rejected assignee leaves the Lead untouched.

The Application-level test also pins that eligibility is checked for the **assigned** Inspector and not the scheduling Admin, since confusing the two would silently require Admins to hold the Inspector role.

Api suite run three consecutive times per `CLAUDE.md` §14 — 101/101 each.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **474 passing, 0 failing** (153 Domain + 146 Application + 74 Infrastructure + **101 Api**).

---

## Slice 8 — Inspection Photo Upload + `LocalDiskFileStorage`

**Endpoint:** `POST /api/v1/inspections/{id}/photos` (SRS FR-3.2). **Inspector only** — `PermissionMatrix.md` §2 grants Admin nothing here, inverting Slice 7, because evidence should come from whoever was actually on site. The "S" means the *assigned* Inspector, enforced by `IOwnershipValidator`.

### Path assumptions verified empirically before any code was written

The user asked for the traversal claim to be measured rather than documented as an unverified threat, and for the two boundaries to be distinguished. Both were probed directly:

**Their expectation held.** `Path.GetExtension` never returns a directory separator — `../../evil.jpg` → `.jpg`, `..\..\evil.jpg` → `.jpg`, `C:\Windows\evil.exe` → `.exe`, `a.b/c` → empty. Across 18 hostile shapes, not one produced a separator. The handler's key is therefore built from controlled components only (`inspections/{id}/{guid}{ext}`), and **the filename cannot influence directory structure**.

**Root containment works and is worth keeping anyway.** All six escape attempts (`../`, `../../`, `..\`, `/etc/passwd`, `C:\Windows\evil.exe`, `inspections/../../`) were correctly rejected by canonicalize-then-`StartsWith(root + separator)`; both legitimate keys passed. Kept at the storage boundary because `IFileStorage.SaveAsync` takes a plain string — the contract is broader than today's one caller.

**Two problems the design had not anticipated, found by measurement:**

1. **An unbounded extension is copied verbatim.** A 301-character one composed a 373-character path, long enough for `File.Create` to throw `IOException` — which D59 does not map, so **a caller-controlled filename could force a 500**.
2. **A NUL inside the extension** makes `Path.Combine` throw `ArgumentException`. That happens to map to 400, so the outcome was accidentally correct — correct for the wrong reason.

### The validation rule settled on

> When `Path.GetExtension(FileName)` is non-empty it must be a dot followed by 1–31 ASCII letters or digits (total ≤ `MaxFileExtensionLength` = 32). An empty extension stays valid.

- **A character-class rule, not a file-type allowlist** — the distinction the user drew. `.heic`, `.avif`, `.dng`, `.cr2`, `.nef`, `.JPEG` all pass; no format is restricted, because no document restricts any.
- **Deliberately not `Path.GetInvalidFileNameChars()`.** Measured: 41 characters on Windows, 2 on Linux. Validation built on it would accept or reject the same request differently per deployment OS — and concretely, a test asserting `.jp*g` is rejected would pass on the Windows test job and fail on the Linux one.
- **32 is documented as an application-level defensive bound**, explicitly not derived from `MAX_PATH`, at the user's direction — long-path support makes that a moving target and a rule pinned to it would be unstable.

### Both halves of the consistency problem

**First half — every rejection precedes the write.** Shape validation, not-found, ownership, and BR-10 all sit at or above the `AddPhoto` call; role and authentication reject earlier still. **Proven, not asserted:** reordering the handler so `SaveAsync` ran before `AddPhoto` made the completed-Inspection test fail with *expected 0 files, actual 1* — the orphan CLAUDE.md §12 exists to prevent, reproduced on demand.

**Second half — write succeeds, commit fails.** Previously unhandled. Now compensated: the file is deleted best-effort and the **original** commit exception is rethrown; a failure of the delete is logged and swallowed (D50's shape — a secondary failure must not replace an accurate report of the primary one).

The ordering itself was re-examined and kept: committing first would trade an inert, invisible orphaned file for a database row pointing at a file that was never written — visible breakage every time the dashboard renders it. **This is compensation, not atomicity**, and is documented as such in three places (the interface, the handler, and a test that asserts the orphan genuinely survives a failed delete). A process crash between the two steps still leaks a file.

### An assumption that turned out false during implementation

**`File.Delete` is not fully idempotent.** It no-ops for a missing *file* but throws `DirectoryNotFoundException` for a missing *directory* — so `IFileStorage.DeleteAsync`'s documented "does nothing if it does not exist" was false as written. Caught by the idempotency test, not by reading the docs. Fixed by catching that exception (rather than a racy `Directory.Exists` pre-check), so the contract is now true. This matters for the compensation path, whose whole job is running after something already failed.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Application/Common/Interfaces/IFileStorage.cs` | `DeleteAsync` added — a real caller now exists; `GetAsync` still deliberately absent |
| `.../UploadInspectionPhotoCommandValidator.cs` | Extension rule + `MaxFileExtensionLength` |
| `.../UploadInspectionPhotoCommandHandler.cs` | Compensating delete; `ILogger` for the secondary failure |
| `src/RenoTrack.Infrastructure/FileStorage/LocalDiskFileStorage.cs` | New — replaces the placeholder |
| `src/RenoTrack.Infrastructure/FileStorage/FileStorageOptions.cs` | New — root path, validated eagerly at startup |
| `src/RenoTrack.Infrastructure/FileStorage/PlaceholderFileStorage.cs` | **Deleted** — superseded; its test too |
| `src/RenoTrack.Api/Controllers/InspectionsController.cs` | `UploadPhoto` action |
| `src/RenoTrack.Api/Inspections/Dtos/UploadInspectionPhotoRequest.cs` | New — `IFormFile` stays in the API layer |

`RenoTrack.Application` gained `Microsoft.Extensions.Logging.Abstractions`, its third package. Flagged rather than slipped in, since `CLAUDE.md` documented the previous two: it is a pure abstraction like `DependencyInjection.Abstractions`, and the alternative was either a silent swallow or making `DeleteAsync` never throw — which would have been worse for future callers. The layering rule now reads "no **hosting or configuration** package", which is what it always meant.

### Tests

35 new (516 total). The ones that carry the slice:

- **Ordering:** upload to a completed Inspection → 409 **and the file count on disk is unchanged**. A status-code assertion alone would still pass with the order reversed; this one does not.
- **Compensation, all four cases the user listed:** rejected upload → no file; write failure → no commit; commit failure → file removed and the *original* exception surfaces; delete failure → original exception still surfaces and the orphan is asserted to remain, so the test states the limitation rather than implying atomicity.
- **The two path boundaries, separately:** a hostile filename cannot alter the storage directory (handler level), *and* `LocalDiskFileStorage` independently rejects unsafe keys passed straight to it (storage level) — including a sibling directory sharing the root's prefix, which a naive `StartsWith(root)` would admit.
- Refusal to overwrite; idempotent delete; Admin → 403; non-owning Inspector → 403 with no file written.

Failure-path suites run five times (Application) and three times (Api, Infrastructure) per `CLAUDE.md` §14 — green every run.

**Eager `FileStorage:RootPath` validation caught two test helpers** composing `AddInfrastructure` without it, exactly the fail-fast behaviour intended.

### Still deliberately not built

No `GetAsync` and no photo-serving endpoint. Architecture §9 says photos are "served back through an authenticated API endpoint", and none exists — **the system now stores photos it cannot serve.** Recorded as a documented gap, alongside the missing `GET /inspections/{id}` from Slice 7, pending a documents-first decision. `DeleteAsync` was added because it has a caller; `GetAsync` does not.

The upload size limit remains Kestrel's ~30 MB default — no project-specific number invented, and the effective cap is now recorded rather than assumed.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **516 passing, 0 failing** (153 Domain + 164 Application + 89 Infrastructure + 110 Api).

---

## Slice 9 — Inspection Completion

**Endpoint:** `POST /api/v1/inspections/{id}/complete` (SRS FR-3.4, Architecture.md §5.2, Sequence Diagram §3 Step B). **Inspector only, and specifically the assigned one** — `PermissionMatrix.md` §2 marks it `— | S`, so an Admin gets 403 exactly as in Slice 8, inverting Slice 7.

### The slice added no Application-layer code, and that is a finding

`CompleteInspectionCommand`, its validator, handler, `InspectionDto`, and 9 handler tests have all existed since Phase 2, and the handler was already registered in `AddApplication()`. Slices 6, 7 and 8 each added Application work; this one did not. The endpoint contract was genuinely driven by the existing use case rather than the reverse, which is the ideal the standing instruction aims at. Recorded explicitly so the absence never reads as an omission.

### Design review conclusions

- **No request record at all.** D61 says the wire contract is a strict subset of the command's parameters and that a request type is justified exactly when that subset differs. Here it is *empty*: `InspectionId` comes from the route, `CompletedByInspectorId` from the JWT's `sub`. This is unambiguously D61's "who is acting" case — the contrast with Slice 7's `InspectorId` (an assignment target) is what D61's own Slice 7 correction is about. First Phase 4 endpoint with no request DTO.
- **No `IsActiveInspectorAsync` check, deliberately.** D62 exists because a *third party's* id arrived off the wire with nothing behind it. Here the id comes from a signed token: the caller authenticated moments ago, `AuthController` rejects deactivated accounts at both login and refresh, the role attribute enforces Inspector, and ownership proves they are *the* assigned Inspector. Adding it would re-verify the token issuer's own work. **Accepted residual:** an Inspector deactivated within the last 15 minutes can still complete their own Inspection until the access token expires — a documented property of D60's short-lived-JWT model, not a hole in this slice.
- **Photos and notes are not required before completion, and no such rule was invented.** Checked exhaustively: SRS FR-3.2 grants a capability rather than stating a precondition; FR-3.4 states only the consequence; StateMachine.md §1.3's guard for `CompleteInspection` is exactly "Inspection belongs to this Lead"; Sequence Diagram §3's `loop For each photo` permits zero iterations; no `BusinessRules.md` entry mentions either (BR-10 runs the opposite direction — immutability *after* completion); `Inspection.Complete()` has one guard. A test pins the absence so a future edit that invents the rule fails CI rather than passing review. Same reasoning that rejected "`ScheduledAt` must be in the future" in Slice 7.
- **200 OK with `InspectionDto`**, matching Sequence Diagram §3. Not 201 (nothing created), not 204 (the timestamp is server-generated and no `GET /inspections/{id}` exists to fetch it from).
- **Repeated completion is not idempotent** — 409 from the Inspection's own guard, with the original `CompletedAt` never overwritten. A silent 200 would hide a real client bug (a double-tap on the mobile browser SRS §90 anticipates); rewriting the timestamp would undo the evidentiary value BR-10 protects. Same stance D61 took for `POST /api/v1/leads`.
- **Audit target unchanged and verified rather than assumed** — against `Lead`, with `AuditAction.InspectionDone`, per §10's rule that the entry goes against the aggregate the business cares about.

### Atomicity — genuine here, unlike Slice 8

`InspectionRepository`, `LeadRepository` and `UnitOfWork` each constructor-inject `RenoTrackDbContext`, registered **Scoped**, so within one request all three hold the same instance; neither `GetByIdAsync` uses `AsNoTracking`. One `SaveChangesAsync` therefore emits both `UPDATE`s inside EF Core's single implicit transaction — the guarantee D48 relied on when it confirmed `UnitOfWork` needs no explicit transaction API. **Both land or neither does.** This is real atomicity, unlike Slice 8's photo upload, which spans a filesystem and a database and can only compensate.

**The audit entry is deliberately outside that guarantee.** `IAuditService` is best-effort and commits after the business transaction (D50), so a completed Inspection with no audit row is possible and accepted. Do not describe the audit row as part of the atomic operation.

### Guard ordering — kept, with the reason recorded

Order is: validate → load Inspection (404) → ownership (403) → load Lead (404) → `inspection.Complete()` → `lead.MarkInspectionDone()` → one `SaveChangesAsync` → audit.

So the Inspection **is** mutated in memory before the Lead's guard can throw. Nothing is written, because `SaveChangesAsync` is never reached and the request-scoped `DbContext` is disposed with its change tracker. **That safety comes from the scope lifetime, not from a guard** — it holds only while no handler shares a `DbContext` across two units of work and none saves after catching a Domain exception. Neither happens today (CLAUDE.md §17: Domain exceptions propagate unwrapped).

Both orderings are equally safe, since nothing irreversible happens before either guard (§12/D29 do not apply). The deciding factor was **error quality on the most likely failure, a double-submit**: Inspection-first yields `"Inspection 7 is already completed."`, while Lead-first would yield `"…Lead 3 is in status 'InspectionDone', expected 'InspectionScheduled'."` — naming the wrong aggregate for the mistake and leaking a Lead id into an Inspection response. Rejected outright: pre-checking `inspection.CompletedAt` in the handler, which is D29's rejected `Inspection.IsEditable` in handler form and violates §6.

### Two assumptions that turned out false, both found by the adversarial experiments

**1. `AuditService` shares the request's `DbContext`, so its `SaveChangesAsync` is not write-isolated.** Under experiment 2 (commit moved ahead of the Lead mutation) the cross-aggregate test unexpectedly still passed. Cause: `AuditService` injects the same scoped `RenoTrackDbContext` and calls `dbContext.SaveChangesAsync()`, which flushed the still-pending Lead mutation along with the audit row. D50's "commits its own write independently" means independently of `IUnitOfWork`, **not** in an isolated context. Benign in production today — every handler calls `LogAsync` after its own commit, which is D50's own stated precondition, so nothing is ever pending — but it means a handler that ever had pending changes at `LogAsync` time would have them silently committed inside a `try/catch` that swallows failures. **Not changed in this slice** (it alters no Slice 9 behaviour); recorded in `NEXT_STEPS.md` §5a as a known property. It is a further reason not to reorder this handler.

**2. A status-code-only Admin test proved nothing.** Experiment 3 (weakening the action's `[Authorize(Roles = Roles.Inspector)]` to bare `[Authorize]`) **did not fail** on the first attempt. The class-level attribute admits `Admin,Inspector`, so the Admin reached the handler and got 403 from `EnsureInspectionOwnership` instead — the same 403 the role gate would have produced. This is precisely the attribute-vs-guard drift CLAUDE.md §22 warns about, and the test could not see it. Fixed by asserting the 403 body is **empty**: an authorization-middleware rejection carries no body, while a `ForbiddenException` reaching the D59 handler produces a ProblemDetails document. Re-running the experiment then failed with the ProblemDetails body, proving the test now pins *which layer* rejected. The test was renamed to `An_admin_is_forbidden_by_the_role_gate_before_reaching_the_handler` to say what it actually asserts.

### Adversarial verification — all four run, none assumed

| # | Broken implementation | Observed failure | Restored |
|---|---|---|---|
| 1 | `lead.MarkInspectionDone()` removed | `Completion_persists_both…` → `Expected: InspectionDone / Actual: InspectionScheduled` (plus the mismatch test, whose guard also disappeared) | ✅ green again |
| 2 | `SaveChangesAsync` moved ahead of the Lead mutation | `A_lead_in_the_wrong_state…` → `Expected: null / Actual: 2026-08-01T20:43:05` — the orphaned `CompletedAt` persisted | ✅ green again |
| 3 | Action's `Roles = Roles.Inspector` removed | **Initially passed** — see finding 2 above; after strengthening, failed with a ProblemDetails body | ✅ green again |
| 4 | `EnsureInspectionOwnership` removed | `A_non_owning_inspector…` → `Expected: Forbidden / Actual: OK` — one Inspector completed another's Inspection | ✅ green again |

Experiment 4 could not even be compiled at first: `TreatWarningsAsErrors` turned the now-unread constructor parameter into `error CS9113`, so the check cannot be silently deleted without the build failing — a stronger safeguard than any test. The parameter had to be removed too before the experiment could run.

`git diff` confirms `CompleteInspectionCommandHandler.cs` is byte-identical to its pre-experiment state.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Api/Controllers/InspectionsController.cs` | `Complete` action + the handler injected by interface |
| `tests/RenoTrack.Api.Tests/Inspections/CompleteInspectionEndpointTests.cs` | New — 10 tests |
| `tests/.../CompleteInspectionCommandHandlerTests.cs` | +1 test — the Lead-guard failure path |
| `Sequence Diagram.md` §3 | `LogAsync(InspectionCompleted)` → `LogAsync(InspectionDone)` — a stale name; no such enum value has ever existed |

No Application, Domain, Infrastructure, migration, or DI changes.

### Tests

11 new (527 total). The ones that carry the slice are the database reads, not the status codes — `InspectionDto` has no Lead field, so completion's cross-aggregate side effect is **invisible over HTTP**, and a response-only test would still pass with `lead.MarkInspectionDone()` deleted outright (experiment 1 proves exactly that).

The Application-level Lead-guard test deliberately asserts only that `SaveChangesAsync` and the audit were never called. It does **not** assert the in-memory Inspection mutation was rolled back, because that would be asserting a falsehood: the mutation genuinely happened and the fakes have no change tracker. The discard is a property of the real request-scoped `DbContext`, provable only in `Api.Tests`. In-memory truth belongs in the Application test; storage truth belongs in the Api test.

Api suite run three consecutive times per `CLAUDE.md` §14 — 120/120 each.

### Environmental note

LocalDB became unstartable partway through this slice (Windows error 575, seven consecutive attempts) because an orphaned `sqlservr.exe` still held the `MSSQLLocalDB` instance while `sqllocaldb info` reported it Stopped and `stop -k` reported success. Work was halted and reported rather than worked around; no test strategy was weakened and no instance or database was deleted. The orphaned processes exited on their own before any intervention, after which the instance started normally and a connection was verified. Recorded because the same failure will recur on this machine and the diagnosis is not obvious from the error message.

### Still deliberately not built

`PATCH /api/v1/inspections/{id}` for notes. `UpdateInspectionNotesCommand` exists and is registered, `PermissionMatrix.md` §2 grants "Edit Inspection notes — Inspector S", and Sequence Diagram §3 shows the `PATCH` between photo upload and completion — but `Architecture.md` §5.2's endpoint table omits it and no agreed Phase 4 slice covers it. After this slice it is a fully-built, registered, tested, **unreachable** handler. Recorded in `NEXT_STEPS.md` §5a rather than built, following Slice 7's precedent with the missing `GET /inspections/{id}` — the documentation disagreement needs a documents-first decision, not a scope expansion.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **527 passing, 0 failing** (153 Domain + 165 Application + 89 Infrastructure + 120 Api). `dotnet ef migrations has-pending-model-changes` → no pending changes. No new `ARCHITECTURE_DECISIONS.md` entry — every decision applies an existing rule (D48, D50, D59, D61, D62, §10, §16).

---

## Slice 10 — Inspection Notes (`PATCH`), and Lead Won/Lost Reassigned to Phase 6

**Endpoint:** `PATCH /api/v1/inspections/{id}` (SRS FR-3.3, Sequence Diagram §3 Step B). **Inspector only, assigned one only** — `PermissionMatrix.md` §2's "Edit Inspection notes — | S", the same shape as photo upload and completion.

### The slice was redefined during design review, because the planned one rested on a false premise

Slice 10 was planned as *"Lead status update — in practice `MarkWon`/`MarkLost` only."* Reading the repository rather than that title produced four findings, and the user chose to redefine the slice rather than design around them:

1. **No Application command for Won/Lost exists.** `grep` for `MarkWon|MarkLost` across the solution returns six hits, all in `Lead.cs` or `LeadTests.cs`. Unlike Slice 9, this would have been net-new Application work.
2. **`AngebotSent` is unreachable — not merely over HTTP, but in Application code at all.** `Lead.MarkAngebotSent()` is called by nothing; no `SendAngebotCommand` exists (Phase 6, gated on `ITokenLinkService`). Both `MarkWon` and `MarkLost` guard on `Status == AngebotSent`, so the endpoint would have returned 409 for every Lead in every environment, and its happy path could only ever have been exercised by seeding that state directly in a test. The user's position was explicit: do not build an endpoint whose success state can only be manufactured in tests.
3. **Won/Lost is the customer's decision, not a staff action.** `StateMachine.md` §1.3 guards both on "TokenLink valid & unused"; **§5 states the invariant outright** — *"Lead.Status is only set to `Won` inside the same transaction as the Angebot decision handler"*; SRS FR-6.3/FR-6.5 assign the decision to the Lead; Sequence Diagram §6 routes it through `RecordAngebotDecisionCommand`. **SRS.md never contains the words "Won" or "Lost" at all.** An Admin override would have created a second path to a decision BR-4 exists to make tamper-proof.
4. **A real document contradiction**, reconciled below.

**Decision: Lead Won/Lost is formally reassigned to Phase 6**, where the token-link decision workflow makes those transitions reachable. No Admin `MarkWon`/`MarkLost` command or endpoint was created, and **no speculative `AuditAction` values were added** — `LeadWon`/`LeadLost` arrive with the use case that performs them, per §10's growth-on-demand rule.

### Documentation reconciliation (evidence re-verified before any edit)

`PATCH /api/v1/leads/{id}/status` appeared in **exactly one place repo-wide** (`Architecture.md` §5.2) with no code and no other document referencing it, while three documents said a free-standing status edit does not exist. `PermissionMatrix.md` §1's row **contradicted itself**, granting Admin "F" for an action its own note said was not a free-standing edit.

Reconciled, inventing nothing:

- `Architecture.md` §5.2 — obsolete `PATCH /api/v1/leads/{id}/status` row **removed**; two paragraphs added recording why no such endpoint exists and that Won/Lost belong to the token-link decision, each citing the pre-existing rule it restates (BR-7, BR-4, StateMachine §1.3/§5, SRS FR-6.3/6.5, Sequence Diagram §6).
- `PermissionMatrix.md` §1 — "Change Lead status directly" corrected from **`F | —`** to **`— | —`**, so the permission and its explanation finally agree.
- `Architecture.md` §5.2 — `PATCH /api/v1/inspections/{id}` **added**, closing the omission that had left this use case undocumented in the endpoint table while `PermissionMatrix.md` §2 and Sequence Diagram §3 both described it.
- `StateMachine.md` — **unchanged**, because it was already correct on every point.

### The endpoint

- **No new Application, Domain, Infrastructure, migration, or DI work.** `UpdateInspectionNotesCommand`, its validator and handler have existed since Phase 2 and were already registered. This closes the gap recorded since Slice 7: a fully-built, registered, tested handler that no HTTP route reached. **After this slice, `UpdateInspectionNotesCommand` is no longer unreachable** — every remaining unreachable handler belongs to Phase 5/6.
- **`PATCH`, not `POST`** — Sequence Diagram §3's own route, and semantically a partial update rather than a state transition or a new sub-resource.
- **Genuinely idempotent, unlike completion.** Repeating the same update is legitimate and returns 200; a test pins it. No repeat-submission guard was invented, because for an edit a repeat is not an error.
- **`null` clears the notes** — supported, not an edge case: `Inspection.UpdateNotes` accepts null and the validator deliberately places no rule on the field.
- **No length cap**, matching Slice 5's stance on `Lead.Notes`: no document states one, so the effective bound remains Kestrel's ~30 MB default rather than an invented number.
- **BR-10** makes a completed Inspection immutable, enforced by the aggregate's own guard and surfacing as 409 via D59. **No audit entry** — editing notes is operational activity, not a workflow milestone (§10), the same classification photo upload carries.
- `UpdateInspectionNotesRequest` carries only `Notes`. D61's subset rule now has three distinct shapes in one controller from one principle: `ScheduleInspectionRequest` (a third party's id is a legitimate input), completion (empty — nothing but route and token), and this (one caller-suppliable field).

### Tests — 10 new, and zero added to the Application layer

The existing `UpdateInspectionNotesCommandHandlerTests` was inspected first, per instruction. Its 8 tests already cover the happy path, clearing to null, save-count, not-found, ownership, BR-10 (asserting both notes-unchanged **and** `SaveChangesCallCount == 0`), and validation. **No behavioural gap existed, so no Application test was added** — count is not a goal.

The 10 Api tests assert against the database rather than the returned DTO, since the DTO is built from the same in-memory aggregate the handler mutated and so proves the mutation but not the commit.

### Adversarial verification — all four run, none assumed

| # | Broken implementation | Observed failure | Restored |
|---|---|---|---|
| 1 | `EnsureInspectionOwnership` removed | `A_non_owning_inspector…` → `Expected: Forbidden / Actual: OK` | ✅ |
| 2 | Action's `Roles = Roles.Inspector` removed | `An_admin_is_forbidden_by_the_role_gate…` → failed on the **body** assertion (ProblemDetails present ⇒ reached the handler) | ✅ |
| 3 | `inspection.UpdateNotes(...)` removed, flow preserved | 5 failures incl. `The_notes_are_persisted_to_the_database` → `Expected: "Re-tile…" / Actual: null` | ✅ |
| 4 | BR-10 guard bypassed in `Inspection.UpdateNotes` | `A_completed_inspection_cannot_be_edited…` → `Expected: Conflict / Actual: OK` | ✅ |

**Experiment 2 is the direct payoff from Slice 9's finding.** A status-code-only Admin assertion would have passed with the role gate removed, because ownership produces the same 403. The empty-body assertion — carried over deliberately — caught it. Experiment 1 again required removing the constructor parameter too, since `TreatWarningsAsErrors` turns the unread parameter into `error CS9113` before any test can run.

`git diff` confirms `Inspection.cs` and `UpdateInspectionNotesCommandHandler.cs` are byte-identical to their pre-experiment state.

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Api/Inspections/Dtos/UpdateInspectionNotesRequest.cs` | New — one field |
| `src/RenoTrack.Api/Controllers/InspectionsController.cs` | `UpdateNotes` action + handler injected by interface |
| `tests/RenoTrack.Api.Tests/Inspections/UpdateInspectionNotesEndpointTests.cs` | New — 10 tests |
| `Architecture.md` §5.2 | Obsolete Lead-status row removed; Inspection `PATCH` row added; two explanatory paragraphs |
| `PermissionMatrix.md` §1 | "Change Lead status directly" corrected `F | —` → `— | —` |

Api suite run three consecutive times per `CLAUDE.md` §14 — 130/130 each.

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **537 passing, 0 failing** (153 Domain + 165 Application + 89 Infrastructure + 130 Api). `dotnet ef migrations has-pending-model-changes` → no pending changes. No new `ARCHITECTURE_DECISIONS.md` entry — the endpoint applies existing rules, and the documentation cleanup reconciles existing ones rather than creating any.

---

## Slice 11 — Migration Application & Database Bootstrap (D63)

**The last Phase 4 slice.** Deployment/runtime architecture rather than an endpoint, and the one slice in this phase that genuinely warranted a numbered decision.

### The failure mode, reproduced rather than predicted

`grep` for `Migrate`/`EnsureCreated` across `src/` returned no match outside the migration files: **nothing in production had ever applied a migration**, while `Program.cs` unconditionally ran `IdentityRoleSeeder.SeedRolesAsync()` at startup. Running the real application against a genuinely fresh database produced:

```
Unhandled exception. Microsoft.Data.SqlClient.SqlException: Invalid object name 'AspNetRoles'.
   at Microsoft.AspNetCore.Identity.RoleManager`1.RoleExistsAsync(String roleName)
   at RenoTrack.Infrastructure.Identity.IdentityRoleSeeder.SeedRolesAsync() ... line 64
```

**Two findings the design review had not anticipated:**

1. **There are two distinct fresh-deploy failures.** A database that does not *exist* fails with SQL error 4060, which EF Core wraps as *"likely due to a transient failure… Consider enabling transient error resiliency"* — pointing an operator at retry policies rather than at the missing database. Only once the database exists but is empty does error 208 above appear. The initializer's diagnostics now distinguish them explicitly.
2. **`RenoTrackApiFactory` already documented the bug** — *"The schema must exist before the host is first created: Program.cs seeds Identity roles during startup, which fails against a database with no tables."* The harness had been working around, in test code, the exact step production lacked.

### What was decided (full reasoning in D63)

Strategy **C**: Development may explicitly migrate; Production applies migrations as a deployment step and startup only **verifies**, read-only, so the runtime login needs no DDL permission. Automatic startup migration was rejected on least-privilege grounds, on unreviewed-schema-change-on-restart grounds, and because EF takes no cross-process lock — App Service performs overlapped restarts even at one configured instance, the same hazard D54/D55 refused to hand-wave for role seeding.

Two modes only, `Verify` (default when absent) and `Migrate` (**hard-refused in Production**; a warning was explicitly rejected). A `None`/`Skip` mode was considered and **not built** — a DBA-managed database is a reason to use `Verify`, not to bypass it.

**Migration-history compatibility is checked in both directions**, which is a history comparison and deliberately not a schema diff: known-but-not-applied means the database is behind; applied-but-unknown means its schema is newer than this build, a direction `GetPendingMigrationsAsync` alone cannot detect.

**Role seeding moved out of normal startup** into explicit initialization, on operational grounds rather than symmetry — it shares migrations' precondition and it writes. Startup verifies the roles instead. **`IdentityRoleSeeder` is unchanged**; only its caller moved. **No user is provisioned in any environment** — SRS OQ-1 stays open, and a fresh database has schema and roles and nobody able to log in.

### An unexpected consequence, caught by an existing safety net

`DatabaseInitializer` needs `IHostEnvironment`, which the generic host supplies free and a hand-composed container does not. The **Slice 3 DI test failed within minutes**: *"Unable to resolve service for type 'IHostEnvironment' while attempting to activate 'DatabaseInitializer'"*. This is the second time that test has caught a later slice's mistake (the first was `TokenService` in Slice 4).

Resolved by following the existing `AddLogging()` precedent — the same category of host-provided dependency — rather than changing `AddInfrastructure`'s signature: three test helpers now register a hand-written `TestHostEnvironment`, and the requirement is documented on `AddInfrastructure` itself so it is declared rather than discovered.

### D40/D58 untouched

`Api.Tests` still migrates its own database; `Infrastructure.Tests` still uses `EnsureCreated`. Neither lifecycle is routed through the initializer. `RenoTrackApiFactory` does set `Database:Mode=Migrate` — not for consistency, but because normal startup no longer seeds roles and `Verify` would fail against its freshly-created database.

### Tests — 12 new, all state-based

Every assertion inspects real database state (`__EFMigrationsHistory` rows, `AspNetRoles` rows) or the refusal that state produces. A test that only checked "`MigrateAsync` was called" would pass against a database that failed to migrate.

Bootstrap from zero; idempotency across repeated runs; `Verify` succeeds when ready; **both history directions proven independently**; missing role refused; `Verify` proven not to repair what it finds (it must not, or the runtime would need write permission); Production refuses `Migrate` **and writes nothing**; `Verify` permitted in Production; omitted mode defaults to `Verify` and writes nothing; unrecognised mode fails eagerly naming key and allowed values; non-existent database names the connection rather than a migration fault.

**Concurrency deliberately not tested or defended:** Production never migrates at startup, so concurrent migration is not a production scenario, and distributed locking would be architecture for a case that does not exist.

### Adversarial verification — five experiments, each observed

| # | Broken implementation | Observed failure | Restored |
|---|---|---|---|
| 1 | "database behind" detection disabled | exactly 1 failure: `Verify_refuses_when_a_required_migration_is_missing` | ✅ |
| 2 | "database ahead" detection disabled | exactly 1 failure: `Verify_refuses_when_the_database_has_an_unknown_applied_migration` | ✅ |
| 3 | role verification disabled | 2 failures: the missing-role refusal and the does-not-repair test | ✅ |
| 4 | Production guard downgraded to a warning | `Migrate_is_refused_in_production_and_writes_nothing` | ✅ |
| 5 | migration application skipped in `Migrate` mode | 8 failures incl. the state-based bootstrap test | ✅ |

Experiments 1 and 2 failing **exactly one test each** is what proves the two history directions are covered independently rather than by one combined assertion.

### End-to-end bootstrap, verified against the real application

| Step | Result |
|---|---|
| Production + `Verify`, database absent | Refused: *"Cannot connect… the database itself has been created — a database that does not yet exist reports a login failure rather than a missing schema."* |
| Production + `Verify`, database empty | Refused, naming **all five** pending migration ids |
| Production + `Migrate` | Refused: *"…refused in the Production environment…"* |
| Development + `Migrate`, empty database | `Applying database migrations` → `Seeding Identity role reference data` → `verification succeeded` → `Now listening on: http://localhost:5000` |
| Production + `Verify`, initialized database | `verification succeeded` → serving, with **no** "Applying database migrations" line |

### What was built

| File | Change |
|---|---|
| `src/RenoTrack.Infrastructure/Persistence/DatabaseInitializationOptions.cs` | New — two-value mode, eager hand-rolled parsing |
| `src/RenoTrack.Infrastructure/Persistence/DatabaseInitializer.cs` | New — migrate/seed/verify, Production guard, both-direction history check |
| `src/RenoTrack.Infrastructure/DependencyInjection.cs` | Registers both; documents the host-provided requirements |
| `src/RenoTrack.Api/Program.cs` | Startup seeder block replaced by the initializer |
| `src/RenoTrack.Api/appsettings.json` | `Database:Mode = Verify`, stated explicitly in a tracked file |
| `src/RenoTrack.Infrastructure/RenoTrack.Infrastructure.csproj` | Explicit `Microsoft.Extensions.Hosting.Abstractions` |
| `tests/.../DatabaseInitializerTests.cs` | New — 12 state-based tests |
| `tests/RenoTrack.Infrastructure.Tests/TestHostEnvironment.cs`, `tests/RenoTrack.Api.Tests/TestHostEnvironment.cs` | New — hand-written host-environment doubles |
| `Architecture.md` §13.1, `CLAUDE.md` §22, `ARCHITECTURE_DECISIONS.md` D63 | The durable policy |

### Outcome

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **549 passing, 0 failing** (153 Domain + 165 Application + 101 Infrastructure + 130 Api). `dotnet ef migrations has-pending-model-changes` → no pending changes. **The five existing migrations were not regenerated, squashed, renamed, or edited.**
