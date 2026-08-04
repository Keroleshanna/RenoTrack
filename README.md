# RenoTrack

Renovation Company Website & Project-Tracking Dashboard for a German home-renovation/tiling ("Fliesen") company.

RenoTrack replaces a manual Word/Excel-based Angebot (quote) and Rechnung (invoice) process with a digital pipeline covering the full customer journey: public lead capture → on-site inspection → digital Angebot builder with a reusable line-item Catalog → internal review → secure no-login customer decision via token link → Project conversion → split invoicing → payment tracking.

## Documentation

The following documents are the single source of truth for this project. Code must conform to them; any conflict is a bug, not a design choice.

- `SRS.md` — Software Requirements Specification
- `Architecture.md` — Technical architecture
- `ERD.md` — Full database schema
- `Sequence Diagram.md` — Flow-by-flow interaction diagrams
- `StateMachine.md` — Entity state machines (Lead, Angebot, Invoice, Project)
- `BusinessRules.md` — Numbered business rules (BR-n)
- `PermissionMatrix.md` — Role-based access control matrix
- `Wireframes.md` — Structural UI wireframes
- `PROJECT_ROADMAP.md` — Phased build plan (start here for "what's next")
- `CLAUDE.md` — Permanent engineering rules/conventions this codebase has committed to
- `PROJECT_STATE.md` — Precise, current snapshot of what exists (start here for "what's actually built")
- `ARCHITECTURE_DECISIONS.md` — Chronological log of every significant decision, with alternatives considered and why

## Getting Started

```bash
dotnet build RenoTrack.slnx
dotnet test RenoTrack.slnx
```

Building and testing need no configuration — both test projects that touch a database supply their own settings and manage their own LocalDB databases. **Running the API does need configuration**; see below.

> `RenoTrack.Infrastructure.Tests` and `RenoTrack.Api.Tests` require real SQL Server **LocalDB** (`sqllocaldb info` should list `MSSQLLocalDB`). This is deliberate — they exist to verify real constraints, precision, and Identity behaviour, so they never substitute a weaker provider.

## Configuration

`src/RenoTrack.Api/appsettings.json` is tracked and holds only safe defaults. Everything else comes from `appsettings.Development.json` (gitignored) locally, or environment variables / a secrets manager in a deployed environment.

| Setting | Tracked default | Required to run the API | Notes |
|---|---|---|---|
| `ConnectionStrings:RenoTrackDb` | — | **Yes** | Environment-specific |
| `Jwt:Issuer` | — | **Yes** | |
| `Jwt:Audience` | — | **Yes** | |
| `Jwt:SigningKey` | — | **Yes — secret** | Min. 32 chars. **Never commit it** |
| `Jwt:AccessTokenMinutes` / `Jwt:RefreshTokenDays` | 15 / 7 | No | |
| `FileStorage:RootPath` | — | **Yes** | Where inspection photos are written |
| `Database:Mode` | `Verify` | No | `Verify` or `Migrate` — see below |
| `DevelopmentBootstrap:Enabled` | `false` | No | Development only — see below |
| `DevelopmentBootstrap:Admin:Password` / `…:Inspector:Password` | — | Only if the above is `true` — **secret** | No default. **Never commit them** |

Every required setting is validated at startup and fails immediately, naming the exact key.

### Database startup behaviour (`Database:Mode`)

| Mode | Behaviour |
|---|---|
| `Verify` | **The tracked default.** Read-only readiness check: migration history must match this build, and the required Identity roles must exist. Never writes, so the runtime login needs no DDL permission. |
| `Migrate` | Applies migrations, seeds role reference data, then verifies. **Refused outright in Production** — startup fails rather than proceeding. |

**Local development against a fresh database:** set `Database:Mode` to `Migrate` (in your `appsettings.Development.json` or as `Database__Mode=Migrate`). With the tracked `Verify` default, an empty database correctly refuses to start, listing the migrations that have not been applied.

**Production:** leave `Database:Mode` at `Verify` and apply migrations as an explicit deployment step *before* starting the application — an EF migration bundle (`dotnet ef migrations bundle`, recommended) or an idempotent SQL script (`dotnet ef migrations script --idempotent`) where a DBA reviews changes. Role reference data is seeded by the same explicit initialization step. See `Architecture.md` §13.1 and `ARCHITECTURE_DECISIONS.md` D63.

> **No user account is created by any of this.** A freshly initialized database has the schema and the two roles and **nobody who can log in**. In Production that is final. For Development, see the next section. How real Inspector/Admin accounts are provisioned is SRS open question **OQ-1** and is still unresolved — see `NEXT_STEPS.md`.

### Getting a login on a fresh Development database (`DevelopmentBootstrap`)

Since the above leaves nobody able to log in, a separate startup step can provision two development accounts — one Admin and one Inspector, because an Admin alone cannot exercise any ownership-scoped permission. **Development only, off by default, and refused outright anywhere else.**

Passwords have no default and are never compiled in. **User Secrets is the recommended source** — not merely for tidiness: the user-secrets configuration provider is registered only when the environment is Development, so a password stored there cannot reach a Production host at all.

```bash
dotnet user-secrets set "DevelopmentBootstrap:Admin:Password" "<choose-one>" --project src/RenoTrack.Api
```

```bash
dotnet user-secrets set "DevelopmentBootstrap:Inspector:Password" "<choose-one>" --project src/RenoTrack.Api
```

Then run the API with the feature on (and `Database:Mode=Migrate` if the database is new):

```bash
DevelopmentBootstrap__Enabled=true Database__Mode=Migrate dotnet run --project src/RenoTrack.Api
```

`appsettings.Development.json` (gitignored) and environment variables also work; standard configuration precedence applies, so User Secrets override the file and environment variables override both.

| | |
|---|---|
| Default addresses | `dev-admin@renotrack.test`, `dev-inspector@renotrack.test` — override with `DevelopmentBootstrap:Admin:Email` / `…:Inspector:Email` |
| Passwords must satisfy | ASP.NET Core Identity's real password policy; a rejected password fails startup and names the key to change |
| Outside Development | **Startup fails.** Enabled in Staging or Production is a hard refusal, never a silent skip |
| Enabled with no password | **Startup fails**, naming the exact key |

**Provisioning is create-only: an existing account is never modified.** Changing the configured password does *not* change an existing account's password, and an account you deactivated to test the rejected-login path stays deactivated. To repair a development account, delete its row (or drop the database) and let the next start recreate it.

Existing accounts are still *inspected*, which is not the same as modified. If one exists without its expected role — possible if a previous run was interrupted between creating the account and assigning its role — startup logs a **warning** naming the account and the role, and continues. That account can log in but will be refused by every role-protected endpoint; the fix is to delete it and restart.

The two accounts must have **different** addresses. Configuring the same address for both fails startup naming both keys, because create-only would otherwise leave you with a single account holding only the Admin role. Addresses are compared case-insensitively, matching how Identity normalizes them.

This is a development convenience and **does not resolve OQ-1**. See `Architecture.md` §13.2 and `ARCHITECTURE_DECISIONS.md` D64.

## Solution Structure

Clean Architecture, built incrementally per `PROJECT_ROADMAP.md`:

```
src/
├── RenoTrack.Domain/            # Entities, enums, domain rules — no dependencies
├── RenoTrack.Application/       # Commands/Queries, DTOs, validators, interfaces
├── RenoTrack.Infrastructure/    # EF Core, repositories, email, file storage
├── RenoTrack.Api/               # ASP.NET Core Web API
├── RenoTrack.Dashboard/         # Angular SPA (Admin/Inspector) — added in a later phase
└── RenoTrack.Website/           # Public site + token-link pages — added in a later phase

tests/
├── RenoTrack.Domain.Tests/
├── RenoTrack.Application.Tests/
├── RenoTrack.Infrastructure.Tests/   # Real SQL Server LocalDB integration tests
└── RenoTrack.Api.Tests/
```

## Status

Phases 0–4 (solution bootstrap, Domain core, Domain CatalogItem, Application layer, Infrastructure/EF Core, API layer) are complete and merged to `main`.

**Phase 4 (API layer) merged via PR #8** — all eleven slices: JWT authentication with rotating refresh tokens, public Lead creation, Lead reads with role-scoped access, the Inspection endpoints (schedule, notes, photos, complete), RFC 7807 error handling, real disk-backed file storage, and the database bootstrap policy.

**Phase 5 (Angebot builder + internal review workflow) is next**, and opens with a Development bootstrap / seed-data slice — because a freshly initialized database currently has schema and roles and nobody able to log in (see the note above).

See `PROJECT_STATE.md` for the current, precise snapshot, `PHASE4_PROGRESS.md` for the slice-by-slice record, `NEXT_STEPS.md` for open items, and `PROJECT_ROADMAP.md` for the full build plan.
