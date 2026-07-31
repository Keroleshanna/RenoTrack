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

Phases 0–3 (solution bootstrap, Domain core, Domain CatalogItem, Application layer, Infrastructure/EF Core) are complete and merged to `main`. Phase 4 (API layer) is next — see `PROJECT_STATE.md` for the current, precise snapshot and `PROJECT_ROADMAP.md` for the full build plan.
