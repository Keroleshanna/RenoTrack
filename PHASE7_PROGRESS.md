# PHASE7_PROGRESS.md — API: Convert Angebot → Project

**Branch:** `feature/phase-7-angebot-to-project`, off `main` at `5a26c42` (PR #12, the Phase 6 merge).
**Roadmap entry:** `PROJECT_ROADMAP.md` Phase 7. **PR title:** `Phase 7: API — Convert approved Angebot into a Project (BR-2)`.

Four implementation slices. Documentation is written in the slice that makes each decision real, not
deferred to a cleanup pass; Slice 4 ends with a cross-document completion sweep, which is a gate, not
a slice of its own.

| # | Slice | Status |
|---|---|---|
| 1 | Domain: `Customer` + `Project` | ✅ done |
| 2 | Infrastructure: schema + migration #7 `AddCustomersAndProjects` | not started |
| 3 | Application: `ConvertAngebotToProjectCommand` + repositories | not started |
| 4 | API: conversion + Project detail read + phase completion gate | not started |

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
