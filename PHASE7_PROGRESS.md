# PHASE7_PROGRESS.md — API: Convert Angebot → Project

**Branch:** `feature/phase-7-angebot-to-project`, off `main` at `5a26c42` (PR #12, the Phase 6 merge).
**Roadmap entry:** `PROJECT_ROADMAP.md` Phase 7. **PR title:** `Phase 7: API — Convert approved Angebot into a Project (BR-2)`.

Four implementation slices. Documentation is written in the slice that makes each decision real, not
deferred to a cleanup pass; Slice 4 ends with a cross-document completion sweep, which is a gate, not
a slice of its own.

| # | Slice | Status |
|---|---|---|
| 1 | Domain: `Customer` + `Project` | ✅ done |
| 2 | Infrastructure: schema + migration #7 `AddCustomersAndProjects` | ✅ done |
| 3 | Application: `ConvertAngebotToProjectCommand` + repositories | ✅ done |
| 4 | API: conversion + Project detail read + phase completion gate | ✅ done |

---

## Approved design decisions carried into this phase

Settled in the Phase 7 design review, before any code was written. Recorded here so a later slice
cannot silently reopen one.

1. **`Customer.Address` is nullable.** `Lead.Address` is legitimately null for every website-sourced
   Lead, so requiring one would block conversion of a valid `CustomerApproved` Angebot — inventing a
   rule against BR-2. `ERD.md` corrected rather than the Domain bent to fit it.
2. **`GET /api/v1/projects/{id}` is in scope**, with FR-7.4's Invoice portion explicitly deferred to
   Phase 8. It is also the `Location` target for conversion's 201. `Architecture.md` §5.2 gains the
   row it lacks. Authorization: Admin `F` / Inspector `R`, role-only, **no ownership check**.
3. **The full Project state machine ships in the Domain now** (`PutOnHold`, `Resume`, `Complete`),
   with **no** Application command or endpoint for those three in Phase 7. Precedent: `Angebot.Send()`
   and the two `RecordCustomer*` methods existed from Phase 1 and only got commands in Phase 6.
4. **BR-2's guard lives in `ConvertAngebotToProjectCommand`, not in `Project.Create`.** An explicitly
   approved clarification: the invariant governs a *cross-aggregate conversion*, and `BusinessRules.md`
   BR-2 assigns enforcement to that command by name. `Project.Create` therefore never receives an
   `Angebot` — only the values it needs, including an `AgreedTotal` already derived from
   `Angebot.GrossTotal`. **`BusinessRules.md` is not to be edited to move this guard.**
5. **Customer resolution is find-by-`LeadId`-then-create.** No matching or deduplication by email,
   phone, name or address — that is a customer-identity policy no document specifies.
6. **No Project title/name/description column**, despite Wireframe E1 rendering one. Flagged for
   Phase 12, not invented here.
7. **`GoneException`'s missing documentation is fixed inside this phase**, not as a separate
   pre-phase cleanup commit.
8. **The stale current-state documents are reconciled inside this phase**, kept current as work lands
   and swept before Phase 7 is declared done — not in a pre-phase commit.

---

## Slice 1 — Domain: `Customer` + `Project`

**Scope:** two aggregate roots and one enum. No schema, no EF configuration, no Application layer, no
API. Nothing in this slice can be reached over HTTP; that is Slices 2–4.

### Files added

| File | Why |
|---|---|
| `src/RenoTrack.Domain/Enums/ProjectStatus.cs` | `Active`/`OnHold`/`Completed` — exactly StateMachine.md §4.1's three states, no more (FR-7.2) |
| `src/RenoTrack.Domain/Entities/Customer.cs` | Aggregate root, Architecture.md §6. `LeadId` + copied contact details |
| `src/RenoTrack.Domain/Entities/Project.cs` | Aggregate root, Architecture.md §6. References Customer and Angebot by id only |
| `tests/RenoTrack.Domain.Tests/Entities/CustomerTests.cs` | 21 tests |
| `tests/RenoTrack.Domain.Tests/Entities/ProjectTests.cs` | 30 tests |

### Design points worth not rediscovering

- **Both constructors assign only; every guard is in the factory.** CLAUDE.md §2's rule, which exists
  because Phase 6's `TokenLink` put a time-dependent guard in its constructor and made every expired
  row throw on load. Neither of these aggregates has a time-dependent invariant today, but placing
  guards in the constructor would be relying on that staying true.
- **`Project` cannot see an `Angebot`, structurally.** That is the other half of decision 4 above: the
  BR-2 guard is not merely *placed* in the Application layer, it is *unreachable* from `Project`,
  pinned by a reflection test over properties, fields and their generic type arguments.
- **`Complete()` is reachable only from `Active`, not from `OnHold`.** StateMachine.md §4.2 draws
  `Active --> Completed` and gives `OnHold` no path to it except `Resume` first. The permissive
  reading was rejected as a new transition rather than an implementation detail.
- **`Complete()` enforces only Project's own state invariant.** §4.3's "all Invoices Paid or Void"
  guard and FR-8.6's override are cross-aggregate and belong to Phase 8's `CompleteProjectCommand`,
  exactly as StateMachine.md §5 assigns them. Invoices do not exist yet, so the guard could not be
  written here even if the layering allowed it.
- **`AgreedTotal` has no mutator at all**, so ERD.md's snapshot wording is a structural guarantee
  rather than a convention — the same shape BR-8 gives `AngebotItem`. A test drives every transition
  and asserts the value never moves.
- **Zero is a legal `AgreedTotal`; negative is not.** `Money.Zero` is a legal `Angebot.GrossTotal`, so
  refusing it would invent a minimum-value rule. Negative needs no external knowledge to reject.
- **`PutOnHold()` takes no reason parameter.** StateMachine.md §4.3 notes "Reason optional/free text",
  but `ERD.md` has no column for it — accepting a value nothing stores is the accept-and-discard shape
  Phase 6 rejected for the FR-6.3 rejection reason.

### Adversarial verification

Each safeguard was broken, the suite run, and the file restored byte-identically.

| # | Defect introduced | Result |
|---|---|---|
| 1 | `Complete()` guard weakened to permit `OnHold → Completed` | **2 failures** — `Complete_FromAnyOtherState_Throws(OnHold)` and `FailedTransition_LeavesCompletedAtUntouched` |
| 2 | `Resume()` made to reset `AgreedTotal` to `Money.Zero` | **1 failure** — `AgreedTotal_SurvivesEveryTransition` |
| 3 | `Customer.Create` made to require `address` | **2 failures** — `Create_AllowsOmittingAddress` and `Create_LeavesIdUnassigned` |

Experiment 3 is the one worth keeping: it proves the approved nullability decision is pinned by a test
and cannot drift back to a required address without a visible failure.

### Documentation updated in this slice

- **`ERD.md`** — `Customers.Address` marked nullable in the diagram, with the physical-schema row
  recording both the correction's reasoning and the repeat-customer limitation (`LeadId UK` makes
  §4's "one Customer can have many Projects" unreachable), explicitly as a recorded limitation rather
  than something redesigned here.
- **`CLAUDE.md` §17** — corrected from "Three Application-layer exception types exist" to four, with
  `GoneException` → 410 added to the table. **`CLAUDE.md` §22** — `GoneException`→410 added to the
  ProblemDetails mapping. This closes a Phase 6 documentation gap: the type shipped in code and in the
  handler switch but appeared in no permanent document.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test tests/RenoTrack.Domain.Tests` → **236 passing, 0 failing** (was 185; **+51**).
- Other suites untouched by this slice and unchanged from the phase baseline: Application 263,
  Infrastructure 161, Api 249. **Solution total 909.**

---

## Slice 2 — Infrastructure: schema + migration #7 `AddCustomersAndProjects`

**Scope:** two `IEntityTypeConfiguration<T>` classes, two `DbSet`s, one migration, LocalDB constraint
tests. No Application layer, no API.

### Three-way review, performed before generating anything

CLAUDE.md §21 requires Domain ↔ EF configuration ↔ `ERD.md` to be compared explicitly, not inferred
from a clean compile. **All three agreed on every point; no mismatch was found and nothing was
reconciled.**

| Aspect | Domain | `ERD.md` | Configuration |
|---|---|---|---|
| `Customer.Id` | `int`, private set | `int Id PK` | identity PK |
| `Customer.LeadId` | `int`, guarded `> 0` | `int LeadId FK UK` | required; FK → `Leads`, `Restrict`; **unique index** |
| `Customer.Name` | required, trimmed | `string Name` | required, `nvarchar(200)` |
| `Customer.Email` | required, trimmed | `string Email` | required, `nvarchar(320)` |
| `Customer.Phone` | required, trimmed | `string Phone` | required, `nvarchar(50)` |
| `Customer.Address` | `string?` | `string Address "nullable"` | **nullable**, `nvarchar(500)` |
| `Project.CustomerId` | `int`, guarded `> 0` | `int CustomerId FK` | required; FK → `Customers`, `Restrict`; **not unique** |
| `Project.AngebotId` | `int`, guarded `> 0` | `int AngebotId FK UK` | required; FK → `Angebote`, `Restrict`; **unique index** |
| `Project.Status` | `ProjectStatus` | `string "Active \| OnHold \| Completed"` | **string**, `nvarchar(20)` |
| `Project.AgreedTotal` | `Money`, non-null | `decimal AgreedTotal` | `MoneyConverter`, **`decimal(18,2)`**, required |
| `Project.CreatedAt` | `DateTime` | `datetime CreatedAt` | required, `datetime2` |
| `Project.CompletedAt` | `DateTime?` | `datetime CompletedAt "nullable"` | nullable, `datetime2` |
| Navigation properties | none on either type | — | none — both FKs use the generic `HasOne<T>().WithMany()` overload, as `LeadConfiguration` already does |

Two points settled during the review rather than left implicit:

- **String lengths match `LeadConfiguration`'s exactly** (200/320/50/500). `ERD.md` specifies lengths
  for no table, so the binding constraint is that these columns hold values copied verbatim from a
  Lead — a narrower column here would reject at conversion time a value the Lead row already holds.
- **`Project.CustomerId` is deliberately *not* unique.** `ERD.md` §4 says one Customer may have many
  Projects. That the `Customers.LeadId UK` design currently makes repeat customers unreachable is a
  recorded limitation; tightening this column would bake that limitation into the schema and make
  resolving it later a breaking change. Pinned by a test.

### Migration review (manual, after generation)

`20260807152900_AddCustomersAndProjects`. Every operation checked against the approved schema:

- **Creates exactly two tables**, `Customers` and `Projects`. **No `AlterColumn`, `AddColumn`,
  `DropColumn`, `RenameTable` or any other operation touching an existing table** — the seven
  pre-existing tables are untouched.
- Every column, type, nullability and constraint matches the review table above, including
  `Address` as `nullable: true` and `AgreedTotal` as `decimal(18,2)`.
- Three FKs, all `ReferentialAction.Restrict`: `FK_Customers_Leads_LeadId`,
  `FK_Projects_Customers_CustomerId`, `FK_Projects_Angebote_AngebotId`. No cascade anywhere.
- `Down()` drops `Projects` before `Customers` — the correct order given the FK between them.
- **One index was generated that the configuration does not declare: `IX_Projects_CustomerId`**
  (non-unique). This is EF Core's standard FK-backing index, and it is the established convention
  throughout this schema rather than something new — `InitialCreate` contains the same for
  `IX_Angebote_LeadId`, `IX_Angebote_InspectionId`, `IX_AngebotItems_CatalogItemId`,
  `IX_Inspections_LeadId` and six others, none of which appear in `ERD.md` §3 either. Verified
  against those migrations rather than assumed. `Customers.LeadId` and `Projects.AngebotId` need no
  such index because their unique indexes already back their FKs. **No undocumented column, table or
  additional index was introduced.**

### Tests added (13)

`CustomerPersistenceTests` (4) and `ProjectPersistenceTests` (8), real LocalDB per D40: full-field
round trips, the two unique constraints, all three FK rejections, `Address` persisting as null,
`Status` read back through raw SQL to prove it is stored as a name rather than an ordinal,
`AgreedTotal` round-tripping at full precision through raw SQL, one Customer holding many Projects,
and `Complete()` on a loaded entity persisting through `SaveChangesAsync` alone (no `UpdateAsync`
exists anywhere in this project, so Phase 8's `CompleteProjectCommand` will depend on exactly that).

One test was added to `InitialCreateMigrationTests` (1): **`EveryDefinedMigration_IsAppliedToAFreshDatabase`**.
`MigrateAsync` has always applied every migration in the assembly, but nothing asserted it — the
class only checked `_InitialCreate` by name. Pinning the whole set means a migration that fails to
apply is caught by name rather than surfacing indirectly, and it keeps covering later migrations
with no per-migration test to remember to add.

### Adversarial verification

Each defect introduced, tests run, configuration restored byte-identically.

| # | Defect introduced | Result |
|---|---|---|
| 1 | `.IsUnique()` dropped from the `Projects.AngebotId` index | `TwoProjectsCannotShareAnAngebot` failed — the duplicate insert succeeded. **Additionally, every `MigrateAsync`-based test failed** with EF's `PendingModelChangesWarning`, because the model no longer matched the migration |
| 2 | `Customers.Address` made `IsRequired()` | `ACustomerWithNoAddressPersists` failed with SQL's own `Cannot insert the value NULL into column 'Address'` |
| 3 | `AgreedTotal` column type changed to `decimal(18,0)` | `AgreedTotalRoundTripsAtFullPrecision` failed: **12345.67 stored as 12346** — silent corruption of a legally-agreed figure, caught |

Experiment 1's second effect is worth recording: model drift does not merely fail a dedicated drift
assertion, it makes EF Core refuse to migrate at all, so any configuration change made without a
matching migration fails loudly across the whole migration-based suite rather than quietly.

### Documentation updated in this slice

`PROJECT_STATE.md` (§3 migration count and test figures, §6 configuration inventory),
`NEXT_STEPS.md` §1f, `HANDOFF_PROMPT.md` (migration count, slice status, test figures), and this
file. `ERD.md` needed no further change — Slice 1 already corrected the only thing that differed,
and the three-way review confirmed the rest of it already described what was built.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **922 passing, 0 failing** (236 Domain, 263 Application,
  174 Infrastructure, 249 Api). Slice 2 added **13**, all in Infrastructure.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Seven** migrations.

### Environment note (not a code finding)

`RenoTrack.Api.Tests` refused to load mid-slice with `FileLoadException` **0x800711C7 — "An
Application Control policy has blocked this file"**, which xUnit surfaces as a catastrophic failure
and zero tests rather than as a test failure. **Smart App Control is on and enforcing on this
machine** (`HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState = 1`,
usermode CI enforcement status 2), and it blocked that project's freshly-rebuilt unsigned Debug
binary. Deleting `bin`/`obj` and rebuilding did not clear it; building and running the same project
in **Release** did, because that is a different output path. The 249 Api tests are genuinely
verified — against the new schema, since `Api.Tests` creates it with `MigrateAsync` and therefore
applied migration #7. Smart App Control was **not** disabled or weakened: it is a machine security
setting, and the workaround is sufficient.

---

## Slice 3 — Application: conversion command + repositories

**Scope:** the conversion use case and its two repositories, plus the transaction abstraction the
use case forced. No API — that is Slice 4.

### The design problem this slice surfaced, and the approved answer

The handler could not be written as planned. Four constraints hold individually and cannot hold
together: `Project` references `Customer` by id only (no navigation property); Customer and Project
must persist atomically; `Project.Create` requires `customerId > 0`; and EF Core assigns identity
only at `SaveChanges`. On the create-Customer path — **every first conversion** — `customer.Id` is
still `0` when `Project.Create` is called.

Implementation stopped and the alternatives were put to the user rather than one being chosen
silently. The approved answer is an explicit transaction: **`IUnitOfWork.BeginTransactionAsync`**
returning **`IUnitOfWorkTransaction : IAsyncDisposable`** with a single `CommitAsync` and no
`RollbackAsync`. `ARCHITECTURE_DECISIONS.md` D48 carries the full amendment, including the six
rejected alternatives and the two standing constraints it creates (never reuse a `DbContext` after
a rollback; never add `EnableRetryOnFailure` without revisiting every caller).

**None of the approved boundaries were weakened to make this fit:** no navigation property,
no relaxed `customerId > 0` guard, no PK-strategy change, no compensation, and `Project.Create`
still never receives an `Angebot` or a `Customer` aggregate.

### Handler shape

Every rejection is evaluated before anything is constructed: validate → Angebot exists → **BR-2**
→ not already converted → Lead exists. Only then is a Customer resolved and a Project created.

- **Create-Customer path:** one explicit transaction, two saves (the first is what produces the id),
  then commit. Any escape path disposes without committing, which rolls back.
- **Reuse-Customer path:** one save, **no** explicit transaction — EF's implicit per-save
  transaction already covers a single insert.
- **BR-2's guard is in the handler**, the approved exception to CLAUDE.md §6. `Project` cannot see
  an `Angebot` at all, so no aggregate could own this rule. `BusinessRules.md` was **not** edited.
- **No `IOwnershipValidator`** — `PermissionMatrix.md` §5 marks the action Admin `F`, so an
  ownership call would be a semantic error, not merely redundant (CLAUDE.md §16).
- **`Lead.Status` is never touched** — Sequence Diagram §7's Phase 6 correction; the Lead reached
  `Won` in the customer's decision handler and there is no second path.
- **Customer resolution is by `LeadId` only.** A Customer on a *different* Lead with identical name,
  email, phone and address is deliberately not reused, and an existing Customer's details are never
  refreshed from the Lead. Both are pinned by tests.
- **Audit** logged against `Project` with new `AuditAction.ProjectCreated`, after the commit.

### Tests added (33)

**Application (27) — orchestration and guard ordering only.** These deliberately do **not** claim
to prove atomicity: a fake `IUnitOfWork` has no database and rolls nothing back. They prove the
handler opens a transaction on one path and not the other, commits on success, and reaches disposal
without committing on failure. `FakeCustomerRepository` mirrors a real repository by *not*
assigning an id on `AddAsync`, so a handler using an unsaved id fails in tests exactly as it would
against SQL Server.

**Infrastructure (6) — transaction semantics, real LocalDB, and the only place they are proved.**
Committed path persisting both rows with the Project carrying the Customer's real id; a forced FK
failure on the second write rolling back the first insert; dispose-without-commit discarding
everything; the reuse path needing no transaction; and both unique indexes still refusing duplicates
*past* the Application pre-check, so the backstop and the control flow are each pinned separately.

### Adversarial verification

| # | Defect introduced | Result |
|---|---|---|
| 1 | `await using` dropped from the handler's transaction | **3 failures** — the transaction was never disposed |
| 2 | `IUnitOfWorkTransaction.DisposeAsync` gutted to a no-op | **See below — initially passed** |
| 3 | BR-2 guard weakened to reject only `Draft` | **6 failures**, including the no-side-effects test |
| 4 | Already-converted check moved after Customer creation | **1 failure** — `AnAlreadyConvertedAngebotIsRejectedWithNoSideEffects` |

**Experiment 2 found a weak test, which is the most valuable result of this slice.** The rollback
test originally wrapped its `DbContext` in `await using` and verified through a fresh context — and
it **passed with `DisposeAsync` gutted to a no-op**, because disposing the context tears down the
connection, which rolls back any open transaction as a side effect. It proved the business outcome
while proving nothing about the mechanism, and would have gone on passing if someone deleted the
disposal entirely, leaving transactions open and holding locks in production.

The test now disposes the transaction **explicitly while its context is still alive** and re-reads
through that same context (which cannot deadlock, where a fresh context would block on the held
locks). Re-run with the same defect, it fails: *"no orphaned Customer may survive the rolled-back
transaction"*. **A rollback test that lets its own context disposal do the work is not a rollback
test** — now recorded in D48's amendment.

### Documentation updated in this slice

`ARCHITECTURE_DECISIONS.md` (the D48 amendment plus ten new rows in the rejected-decisions table),
`PROJECT_STATE.md`, `NEXT_STEPS.md`, `HANDOFF_PROMPT.md`, and this file. `BusinessRules.md`
deliberately unchanged.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **957 passing, 0 failing** (236 Domain, 290 Application,
  180 Infrastructure, 251 Api). Slice 3 added **35** — 27 Application, 6 Infrastructure, and 2 in
  `Api.Tests` DependencyInjectionTests, which reflects over the Application assembly and therefore
  discovers the new handler and validator automatically.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. Seven migrations; Slice 3
  adds no schema.

---

## Slice 4 — API: conversion endpoint, Project detail read, phase completion gate

### Scope reconstructed from the documents, before any code

| Question | Answer, and where it comes from |
|---|---|
| Endpoints | `POST /api/v1/angebote/{id}/convert-to-project` (Architecture.md §5.2, verbatim) and `GET /api/v1/projects/{id}` (approved in the design review; §5.2 lacked the row) |
| Controller | `ProjectsController` (`PROJECT_ROADMAP.md` Phase 7), with the conversion route as an absolute template — the precedent `AngeboteController` set for `POST /api/v1/leads/{leadId}/angebote` |
| Authorization | POST Admin only; GET Admin `F` / Inspector `R` (`PermissionMatrix.md` §5). **No `IOwnershipValidator` anywhere** — both actions are `F`/`R`, never `S` |
| Application layer | POST exposes the existing `ConvertAngebotToProjectCommand` unchanged. GET needed **new** read-side behaviour: `GetProjectByIdQuery`, `IProjectQueries`, `ProjectQueries`, `ProjectDetailDto` |
| Response contract | POST → 201 + `ProjectDto` + `Location` → the GET. GET → 200 + `ProjectDetailDto`, 404 if absent |
| Duplicate conversion | `ConflictException` → **409** through the existing single exception handler. No controller-level `if`, no `try`/`catch` |
| New Domain / schema / migration / repository / transaction | **None.** One read-side query interface and its implementation, nothing more |

### One apparent document conflict, resolved by precedent rather than by decision

Wireframe E1 heads the Project-detail screen **"Roles: Admin"**, while `PermissionMatrix.md` §5
grants Inspector `R`. That is not an unresolved conflict: **D3 already carries the identical
divergence** (Wireframes line 235 says "Roles: Admin"; §4 grants Inspector `R` for an `InReview`
Angebot), and Phase 5 settled it by following the matrix — `AngeboteController.GetReviewComments`
admits both roles. A wireframe's "Roles" line names the screen's primary audience; CLAUDE.md §16
says to decide authorization from `PermissionMatrix.md`'s letter. Followed here, and recorded in
the controller's own remarks so the next reader does not re-litigate it.

### Design points worth not rediscovering

- **`GetProjectByIdQuery` carries no `RequestingInspectorId`, unlike `GetLeadByIdQuery`.** That one
  has it because a Lead is `S`; a Project is `R` — read-only but unscoped, and §5's own note says
  why ("Inspector can view, e.g. to see the outcome of a Lead they worked"). A reflection test pins
  the query's single-parameter shape, so reintroducing a scope would be a visible signature change
  reviewed against §5, not a predicate quietly added to a `WHERE` clause.
- **The read projects rather than hydrating** — the opposite of `GetLeadByIdQueryHandler`, and for a
  stated reason: that handler needs the Domain entity so `IOwnershipValidator` can judge it. Here no
  ownership rule applies and the response spans three tables, so D36's projection rule governs.
- **`ProjectQueries` writes its joins out explicitly**, because `Project` holds no navigation
  property to `Customer` or `Angebot` (CLAUDE.md §2) and there is nothing to `Include`. The read
  side paying a small visible cost for a write-side guarantee, not a workaround.
- **`LeadId` is sourced from the Angebot**, the originating document E1's "Originating:" line names.
  `Customer.LeadId` holds the same value by construction, so the choice is about meaning.
- **`ProjectDetailDto` is E1 minus Phase 8.** No invoice list, no "Invoiced", no "Remaining" — a
  documented gap against FR-7.4, pinned by a test that asserts the exact JSON property set.
- **No `PutOnHold`/`Resume`/`Complete` endpoints**, as agreed in Slice 1: the Domain has all three,
  `PROJECT_ROADMAP.md` places `CompleteProjectCommand` in Phase 8 where its invoice guard can be
  enforced, and on-hold/resume are assigned to no phase.

### Tests added (22)

Application 5 (`GetProjectByIdQueryHandlerTests`, including the no-scope-parameter pin);
Infrastructure 3 (`ProjectQueriesTests` — the three-table projection is genuinely SQL-translatable,
returns null for an unknown id, and reflects a non-default status); Api 14 (`ProjectEndpointsTests`
plus 2 discovered automatically by the reflection-driven `DependencyInjectionTests`).

The Api tests drive a Lead all the way to `CustomerApproved` **through the real endpoints**,
including the customer's own anonymous token-link decision — never by writing a status directly, so
BR-2's precondition is reached the way production reaches it.

### Adversarial verification

| # | Defect introduced | Result |
|---|---|---|
| 1 | `[Authorize(Roles = Admin)]` removed from the conversion action | `An_inspector_cannot_convert` failed |
| 2 | GET restricted to Admin | `An_inspector_can_read_the_project_detail_and_is_not_scoped` failed |
| 3 | A speculative `InvoicedTotal` field added to `ProjectDetailDto` | `The_project_detail_carries_no_invoice_fields_yet` failed |

### One finding during verification

The `Location` header is `/api/v1/Projects/{id}` — the `[controller]` route token capitalises the
segment, which routing then matches case-insensitively. This is API-wide pre-existing behaviour
(`Angebote`, `Leads`, `CatalogItems` all do it), **not** something this slice introduced. The test
was changed to follow `LeadReadEndpointsTests`' stronger precedent — follow the `Location` and
assert it returns 200 — rather than string-matching a lowercase path, because what the contract
actually promises is that the header resolves.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- **979 passing, 0 failing** — 236 Domain, 295 Application, 183 Infrastructure, 265 Api.
  Slice 4 added **22**.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Seven** migrations;
  this slice adds no schema.

---

## Phase 7 completion gate

Documentation reconciliation is a completion criterion, not a publication step. Checked item by
item against the repository, not from memory.

| Document | Outcome |
|---|---|
| `Architecture.md` | §5.2 gained the `GET /api/v1/projects/{id}` row it lacked, and the conversion row now records its contract. §6 already listed Customer and Project as aggregate roots — no change needed |
| `ERD.md` | Corrected in Slice 1 (`Customers.Address` nullable + the repeat-customer limitation). Slice 2's three-way review confirmed the rest already described what was built. **No further change** |
| `BusinessRules.md` | **Deliberately unchanged.** BR-2 assigns enforcement to `ConvertAngebotToProjectCommand` and that is exactly where the guard lives |
| `StateMachine.md` | §4's transition table matches the Domain exactly, including `Complete` only from `Active`. **No change** |
| `PermissionMatrix.md` | §5 matches what was built (`F`/`—` for conversion, `F`/`R` for the read). **No change** |
| `Sequence Diagram.md` | §7 matches the implementation, including its Phase 6 correction that `Lead.Status` is untouched. **No change** |
| `Wireframes.md` | Unchanged. E1's "Roles: Admin" line is the known, precedented divergence recorded above; E1's project title has no schema backing and stays flagged for Phase 12 |
| `CLAUDE.md` | §17/§22 corrected in Slice 1 for `GoneException` |
| `ARCHITECTURE_DECISIONS.md` | D48 amended in Slice 3; ten rows added to the rejected-decisions table |
| `PROJECT_STATE.md` / `NEXT_STEPS.md` / `HANDOFF_PROMPT.md` | Current as of this slice, with final verified figures |
| `PROJECT_ROADMAP.md` | Phase 7's entry describes what was built. **No change** |

**Known gaps carried out of Phase 7, all deliberate and recorded in `NEXT_STEPS.md`:** FR-7.4's
Invoice portion (Phase 8); no `PutOnHold`/`Resume`/`Complete` endpoints (Phase 8 and unassigned);
no Project title column (Phase 12); the repeat-customer limitation from `Customers.LeadId UK`; and
everything Phase 4/6 already carried forward.

**Phase 7 is complete: four slices, no implementation residue, no documentation residue.**
