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
| 4 | Authentication — JWT login | not started |
| 5 | Lead creation (public) | not started |
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

`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors. `dotnet test RenoTrack.slnx` → **419 passing, 0 failing** (153 Domain + 144 Application + 74 Infrastructure + **48 Api**).
