# PHASE8_PROGRESS.md — API: Invoices, Splitting, Payment Tracking, Project Completion

**Branch:** `feature/phase-8-invoices-payments-project-completion`, off `main` at `697292b` (PR #13, the Phase 7 merge).
**Roadmap entry:** `PROJECT_ROADMAP.md` Phase 8. **PR title:** `Phase 8: API — Invoice splitting, payment tracking, project completion guard`.

Seven implementation slices. Documentation is written in the slice that makes each decision real, not
deferred to a cleanup pass; Slice 7 ends with a cross-document completion sweep, which is a gate, not
a slice of its own.

| # | Slice | Status |
|---|---|---|
| 1 | Domain: `Invoice` + `Payment` child | ✅ done |
| 2 | Infrastructure: schema + migration #8 `AddInvoicesAndPayments` | ✅ done |
| 3 | Create Invoice + remaining balance + numbering + VAT allocation | ✅ done |
| 4 | Send Invoice + public token read | ✅ done |
| 5 | Mark Paid + Void | ⬜ not started |
| 6 | Complete Project + FR-7.4 Project detail invoice information | ⬜ not started |
| 7 | Overdue capability + Phase 8 completion gate | ⬜ not started |

---

## Approved design decisions carried into this phase

Settled in the Phase 8 design review, before any code was written. Recorded here so a later slice
cannot silently reopen one.

1. **VAT split (G-1): proportional allocation per rate, from the originating Angebot's gross rate
   mix.** The Angebot's distinct rates are preserved, the entered `GrossAmount` is allocated across
   those rate groups, Net and VAT are derived within each group, and BR-11 rounding applies. The
   result must satisfy `sum(Net per rate) + sum(VAT per rate) == Invoice.GrossAmount` **exactly**,
   with deterministic, explicitly-tested residual-cent reconciliation. **No blended VAT rate is to be
   invented.** The residual-cent handling stays an implementation detail unless implementation shows a
   business-level rule is genuinely required.
2. **`InvoiceLine` is deferred (G-2)** — no Domain type, table, configuration, repository, DTO or
   migration content in Phase 8. ERD.md's own "an Invoice can exist with just header-level
   Net/VAT/Gross amounts" is sufficient for this phase.
3. **Overdue: capability yes, scheduler no (G-3).** The `Sent → Overdue` transition and whatever
   query/repository capability it genuinely requires are built and tested. **No Admin endpoint, no
   `BackgroundService`, and no read-time derivation.** Automatic execution is deferred until the
   project has an explicitly chosen job-hosting strategy. The distinction is recorded, not blurred:
   *the business capability exists; automatic scheduled execution is a known implementation gap.*
4. **No PDF and no `IPdfGenerator` (G-4)**, not even a placeholder abstraction. Phase 14 owns it.
5. **No bank-detail configuration, schema or DTO field, and no Download PDF (G-5).** Wireframe A4
   renders both; both are recorded as gaps rather than invented.
6. **`Money` gains subtraction (G-6)** with exact-value semantics and no new rounding step.
7. **Aggregate boundaries (G-7):** `Invoice` is the root, `Payment` is its child with no public
   construction path, and `Invoice` references `Project` by `ProjectId` only with no navigation
   property. **No explicit transaction boundary** unless implementation reveals a real multi-save
   atomicity requirement — if it does, stop and report rather than widening transaction usage.
8. **`PutOnHold`/`Resume` endpoints are out of scope (G-8).** The Domain methods stay; the
   Application/API surface is recorded as unassigned, not silently claimed by this phase.
9. **Invoice numbering (G-9):** numbers are unique and never reused; **gaplessness is not
   guaranteed** and a number may be skipped if persistence fails after reservation. The number is
   reserved only after every pre-evaluable guard has passed and as late as practical before
   persistence. This gets its own numbered decision record, and `Architecture.md` §8 is corrected to
   describe the real guarantee. **No claim is to be made about what German law does or does not
   require**; if legal gaplessness becomes a confirmed requirement, that is a separate design.
10. **A void reason is required for every void transition, including `Draft → Void` (G-10).**
    StateMachine.md §3.3's blank guard cell is a documentation omission, reconciled — never
    accepted-and-discarded.
11. **Audit (G-11):** `InvoiceCreated`/`InvoiceSent`/`InvoicePaid`/`InvoiceVoided` target `Invoice`;
    `ProjectCompleted` targets `Project`. FR-8.6's mandatory override reason goes in
    `AuditLog.Details`, as StateMachine.md §4.3 requires.
12. **Phase 8 supports full payment only.** `Payment.Amount` is always `Invoice.GrossAmount`; there is
    no caller-supplied amount, no partial-payment behaviour, no per-invoice payment balance and no
    multiple-payment workflow. ERD.md's one-to-many shape remains forward-compatible but must not be
    read as already-supported semantics — pinned by tests.
13. **Stale current-state documents are reconciled inside this phase**, kept current as work lands and
    swept before Phase 8 is declared done — not in a pre-phase cleanup commit. This includes the
    stale Phase 7 publication state.

---

## Slice 1 — Domain: `Invoice` + `Payment` child

**Scope:** one aggregate root, one child entity, two enums, and one operator on an existing value
object. No schema, no EF configuration, no Application layer, no API. Nothing in this slice is
reachable over HTTP; that is Slices 2–7.

### Files added

| File | Why |
|---|---|
| `src/RenoTrack.Domain/Enums/InvoiceStatus.cs` | `Draft`/`Sent`/`Paid`/`Overdue`/`Void` — exactly StateMachine.md §3.1's five states |
| `src/RenoTrack.Domain/Enums/PaymentMethod.cs` | `BankTransfer`/`Cash`/`Other` — exactly SRS FR-8.4's three |
| `src/RenoTrack.Domain/Entities/Invoice.cs` | Aggregate root, Architecture.md §6. References `ProjectId` by id only |
| `src/RenoTrack.Domain/Entities/Payment.cs` | Child of Invoice, `internal` constructor |
| `tests/RenoTrack.Domain.Tests/Entities/InvoiceTests.cs` | 61 tests |
| `tests/RenoTrack.Domain.Tests/Entities/PaymentTests.cs` | 9 tests |

### Files modified

| File | Change |
|---|---|
| `src/RenoTrack.Domain/ValueObjects/Money.cs` | `operator -` (G-6) |
| `tests/RenoTrack.Domain.Tests/ValueObjects/MoneyTests.cs` | 4 tests for it |

### Design points worth not rediscovering

- **`Invoice.Create` enforces `Net + VAT == Gross`, and that is the whole point of putting it here.**
  BR-11 rounds each per-rate part, and rounded parts do not automatically re-sum to the figure they
  were split from. Stating the invariant on the aggregate means Slice 3's allocation arithmetic
  cannot lose or invent a cent silently — its residual handling has to be deliberate, because
  anything else fails to construct.
- **Both constructors assign only; every guard is in the factory (`Invoice`) or is a lifetime
  invariant (`Payment`).** CLAUDE.md §2. Every guard that exists here happens to be a lifetime
  invariant anyway — ids, a non-blank number, non-negative amounts that add up — but the split is
  kept regardless, because a clock-dependent constructor guard is exactly what made every expired
  `TokenLink` row throw on load in Phase 6, and "no guard is time-dependent *today*" is not a
  property a later slice can be relied on to preserve.
- **`Invoice` cannot see a `Project`, structurally**, pinned by a reflection test over properties,
  fields and their generic type arguments. That is why StateMachine.md §5's "an Invoice cannot exist
  without an `Active`/`OnHold` Project" is `CreateInvoiceCommand`'s job (Slice 3), exactly as §5
  assigns it.
- **There is no `SentAt`.** `Angebot` has one; ERD.md's `Invoices` table defines none. The asymmetry
  belongs to the documents, and a test asserts the property's absence so it cannot drift in.
- **`MarkOverdue` takes an `asOf` reading rather than calling `DateTime.UtcNow`**, matching
  `TokenLink.IsExpired`: the rule stays deterministic under test, exercised by moving the reading
  rather than by sleeping or reflecting into `DueDate` to backdate it.
- **`MarkOverdue` compares calendar days, not instants.** §3.3 says "DueDate < today", which is a
  comparison between days — so an invoice due today is not overdue today.
- **`DueDate` is not constrained in any way**, including against the issue date. No requirement
  document places a rule on it, so none is enforced.
- **`MarkPaid` accepts no amount parameter at all**, and a reflection test asserts no `Money` appears
  in its signature — partial payment is absent by construction, not by convention.
- **`Money`'s subtraction may produce a negative**, deliberately. BR-3 warns rather than blocks, so an
  over-invoiced Project's remaining balance is negative and *that negative is the warning*. Clamping
  would hide the data-entry mistake BR-3 exists to catch.
- **`PaymentMethod` declares no gateway value.** FR-8.5 describes what adding one will cost later,
  which is not licence to declare an unreachable value now. (`TokenLinkEntityType.Invoice` is not a
  precedent for the opposite: it was declared early because ERD.md documents *that* column's domain
  as exactly two values.)

### Adversarial verification

Each safeguard was broken, the suite run, and the file restored byte-identically (confirmed by
`git diff`).

| # | Defect introduced | Result |
|---|---|---|
| 1 | `Net + VAT == Gross` invariant disabled | **3 failures** — all three `Create_RejectsAmountsThatDoNotAddUp` cases (cent short, cent over, net drifted) |
| 2 | `MarkPaid` records `NetAmount` instead of `GrossAmount` | **1 failure** — `MarkPaid_AlwaysRecordsTheFullGrossAmount` |
| 3 | `MarkOverdue`'s guard loosened from `>=` to `>` (overdue on the due date itself) | **1 failure** — `MarkOverdue_OnTheDueDateItself_Throws` |
| 4 | Void-reason requirement removed | **2 failures** — `Void_RejectsABlankReasonFromEveryVoidableState` for both blank inputs |
| 5 | `Money`'s subtraction clamped at zero | **1 failure** — `Subtraction_ProducesNegativeWhenTheSubtrahendIsLarger` |

Experiments 2 and 5 are the ones worth keeping. Experiment 2 proves the approved full-payment
decision is pinned rather than merely documented — a future partial-payment change has to break a
named test, not slip through. Experiment 5 proves the same for BR-3's warning: clamping the balance
at zero is the most natural-looking "tidy-up" anyone could make to that operator, and it would
silently delete the only signal BR-3 asks the system to produce.

### Documentation updated in this slice

- **`StateMachine.md`** — §3.3's `Draft → Void` guard cell changed from "—" to "Admin provides a
  reason" (G-10), with §3.4 recording the reconciliation. §3.1 and §3.4 both cited **BR-5** for
  invoice-number non-reuse; corrected to **BR-9** (BR-5 is the mandatory §14 UStG field list).
  `BusinessRules.md` itself was correct throughout and is unchanged.
- **`ERD.md`** — the `Invoices` and `Payments` rows updated for what now exists, including the
  no-`SentAt` decision and the full-payment-only semantics; the `InvoiceLines` row records the
  approved deferral and its revisit trigger.
- **`PROJECT_STATE.md` / `NEXT_STEPS.md` / `HANDOFF_PROMPT.md`** — brought current, which also
  cleared the stale claim that Phase 7 was unmerged.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **1,053 passing, 0 failing** (310 Domain, 295 Application,
  183 Infrastructure, 265 Api). Phase 8 baseline was **979**; Slice 1 added **74**, all in Domain —
  61 `InvoiceTests`, 9 `PaymentTests`, 4 `MoneyTests`.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Seven** migrations —
  correct, since these types have no `DbSet` or configuration yet. That is Slice 2.

---

## Slice 2 — Infrastructure: schema + migration #8 `AddInvoicesAndPayments`

**Scope:** two `IEntityTypeConfiguration<T>` classes, one `DbSet`, one migration, LocalDB constraint
tests. No Application layer, no API. **`InvoiceLines` is excluded**, per the approved deferral.

### Three-way review, performed before generating anything

CLAUDE.md §21 requires Domain ↔ EF configuration ↔ `ERD.md` to be compared explicitly, not inferred
from a clean compile. **All three agreed on every point; no mismatch was found and nothing was
reconciled.**

| Aspect | Domain | `ERD.md` | Configuration |
|---|---|---|---|
| `Invoice.Id` | `int`, private set | `int Id PK` | identity PK |
| `Invoice.ProjectId` | `int`, guarded `> 0` | `int ProjectId FK` | required; FK → `Projects`, `Restrict`; **not unique** (§4: one Project, many Invoices) |
| `Invoice.InvoiceNumber` | required, trimmed | `string InvoiceNumber UK "RE-YYYY-NNNNN"` | required, `nvarchar(30)`, **unique index** (§3, BR-9) |
| `Invoice.IssueDate` | `DateTime`, set at creation | `datetime IssueDate` | required, `datetime2` |
| `Invoice.DueDate` | `DateTime`, unconstrained | `datetime DueDate` | required, `datetime2` |
| `Invoice.Status` | `InvoiceStatus` | `string "Draft/Sent/Paid/Overdue/Void"` | **string**, `nvarchar(20)` |
| `Invoice.NetAmount`/`VatAmount`/`GrossAmount` | `Money`, non-null, `Net + VAT == Gross` | three `decimal` columns | `MoneyConverter`, **`decimal(18,2)`**, required |
| `Invoice.VoidReason` | `string?` | `string VoidReason "nullable"` | **nullable**, `nvarchar(4000)` |
| `Invoice.Payments` | `IReadOnlyList<Payment>` over a private field | `INVOICE to many PAYMENT` | `HasMany`/`WithOne`, shadow FK `InvoiceId`, `IsRequired()`, `Cascade`, field access mode |
| `Payment.Amount` | `Money`, non-null | `decimal Amount` | `MoneyConverter`, `decimal(18,2)`, required |
| `Payment.Method` | `PaymentMethod` | `string "BankTransfer/Cash/Other"` | **string**, `nvarchar(50)` |
| `Payment.PaidAt` | `DateTime` | `datetime PaidAt` | required, `datetime2` |
| `Payment.RecordedByAdminId` | `int`, guarded `> 0` | `int RecordedByAdminId FK` | required; FK → `AspNetUsers`, `Restrict` |
| `Payment` FK to Invoice | **no property** | `int InvoiceId FK` | shadow property — same as `InspectionPhotos.InspectionId`, `AngebotItems.SectionId` |
| Navigation properties to other aggregates | none | — | none — `HasOne<T>().WithMany()` generic overload throughout |

Five points settled during the review rather than left implicit:

- **`Payments` uses `Cascade`, not `Restrict`, and that is not a departure.** CLAUDE.md §21's
  "`Restrict` in every case" governs references *between independent aggregates*. Aggregate
  *composition* already uses `Cascade` three times over — `Angebot → Sections`,
  `AngebotSection → Items`, `Inspection → Photos` — and a Payment has no meaning apart from the
  Invoice that owns it. Verified against those three configurations rather than assumed.
  **Not exercised by any test**, deliberately: nothing in this schema is ever hard-deleted, so the
  behaviour never triggers. That is the same position `AngebotItemConfiguration`'s own comment
  records for its `Restrict`, and it is stated here rather than covered by a test that would have to
  delete a row no code path deletes.
- **String widths, where `ERD.md` specifies none.** `InvoiceNumber` `nvarchar(30)` matches
  `Angebote.AngebotNumber` exactly — same generator, same shape. `Status` `20` matches every other
  status column (Lead/Angebot/Project). `Method` `50` matches the schema's non-status string-valued
  enums (`TokenLinks.EntityType`, `AngebotItems.Unit`). `VoidReason` `4000` matches
  `AngebotReviewComments.Comment`, the existing staff-authored free-text column.
- **`Invoices` has no `CreatedAt`**, unlike every other aggregate-root table here. `ERD.md` defines
  none and the Domain has none — `IssueDate` is the business-meaningful timestamp. Adding one for
  symmetry would be inventing schema.
- **The `(Status, DueDate)` index is created now** because `ERD.md` §3 defines it, not because
  anything runs the overdue check on a schedule. Nothing does.
- **`Payments.RecordedByAdminId`'s FK proves the id names a real user and nothing more.** Whether
  that user is an active Admin is a business rule, and D62 places those in the Application layer via
  `IUserQueries`. Whether Phase 8 needs such a check is left to the slice that builds the command.

### The one question inspection could not answer

`Payment` is materialised through an `internal` constructor whose first parameter is a `Money` — a
type that only reaches the database through `MoneyConverter`. EF Core has to apply a value converter
while *binding a constructor parameter*, not merely while writing a settable property.

**It works.** `AngebotItem` has done exactly this since Phase 3 (an `internal` constructor taking
both a converted `Money` and a converted `ItemUnit`), which is strong precedent — but precedent is
an argument, not a verification, so a dedicated round-trip test proves it directly against LocalDB.
No restructuring of the aggregate was needed and none was considered.

### Migration review (manual, after generation)

`20260807201801_AddInvoicesAndPayments`. Every operation checked against the approved schema:

- **Creates exactly two tables**, `Invoices` and `Payments`. **No `AlterColumn`, `AddColumn`,
  `DropColumn`, `RenameTable` or any other operation touching an existing table** — the pre-existing
  tables are untouched, and no pre-existing migration file was modified (only the model snapshot
  changed, by addition only).
- Every column, type and nullability matches the review table above, including `VoidReason` as
  `nullable: true`, all four monetary columns as `decimal(18,2)`, and the shadow `InvoiceId` as
  `nullable: false` — D46's bug avoided because `IsRequired()` was explicit.
- Three FKs: `FK_Invoices_Projects_ProjectId` **Restrict**,
  `FK_Payments_AspNetUsers_RecordedByAdminId` **Restrict**, `FK_Payments_Invoices_InvoiceId`
  **Cascade** — the only cascade, and the composition relationship described above.
- Five indexes: `IX_Invoices_InvoiceNumber` (**unique**), `IX_Invoices_Status_DueDate` (ERD §3), and
  three EF-generated FK-backing indexes (`IX_Invoices_ProjectId`, `IX_Payments_InvoiceId`,
  `IX_Payments_RecordedByAdminId`), all non-unique. The FK-backing indexes are the established
  convention throughout this schema — `InitialCreate` contains the same for `IX_Angebote_LeadId` and
  six others — and `IX_Invoices_ProjectId` being non-unique is exactly right, since one Project must
  hold many Invoices. **No undocumented column, table or additional index was introduced.**
- `Down()` drops `Payments` before `Invoices` — the correct order given the FK between them.

### Tests added (15)

`InvoicePersistenceTests`, real LocalDB per D40: full-field round trip; all three monetary columns
round-tripping through raw SQL at full precision; `Status` and `Method` read back through raw SQL to
prove they are stored as names rather than ordinals; the unique invoice number (BR-9); one Project
holding many Invoices; both FK rejections; `VoidReason` persisting as a value and as null; a voided
Invoice keeping its row and number (BR-9); the `Payment` constructor-materialisation proof; the
shadow FK's `NOT NULL`-ness read from `INFORMATION_SCHEMA`; `MarkPaid` on a loaded aggregate
persisting through `SaveChangesAsync` alone; and a reflection test that `Payment` has no `DbSet`.

`InitialCreateMigrationTests.EveryDefinedMigration_IsAppliedToAFreshDatabase` — added in Phase 7
Slice 2 precisely so later migrations are covered without a new per-migration test — picks up
migration #8 automatically. No test was added there.

### Adversarial verification

Each defect introduced, tests run, configuration restored byte-identically.

| # | Defect introduced | Result |
|---|---|---|
| 1 | `.IsUnique()` dropped from the `InvoiceNumber` index | `TwoInvoicesCannotShareANumber` failed — the duplicate insert succeeded, breaking BR-9. **Additionally 33 migration-based tests failed** with `PendingModelChangesWarning` |
| 2 | `.IsUnique()` added to a `ProjectId` index | `OneProjectCanHaveManyInvoices` and `AVoidedInvoicePersistsItsReason...` both failed — FR-8.1's entire splitting feature becomes impossible |
| 3 | `NetAmount` column type changed to `decimal(18,0)` | `AllThreeAmountsRoundTripAtFullPrecision` failed (**10,378.15 stored as 10,378**) and `AnInvoiceRoundTripsWithEveryField` failed (**6,722.69 read back as 6,723**) |
| 4 | `.IsRequired()` removed from the `Payments` relationship | `ThePaymentInvoiceForeignKeyIsNotNullable` failed — `IS_NULLABLE` came back **"YES"**, D46's exact bug reproduced |

Experiment 3 is the one worth keeping: the corruption is silent in both directions — the stored
figure is wrong *and* the value read back would break the Domain's `Net + VAT == Gross` invariant on
an already-persisted row, which no amount of Domain-side guarding could catch. Experiment 1's
secondary effect repeats Phase 7's finding: a configuration change without a matching migration does
not merely fail a drift assertion, it makes EF refuse to migrate at all.

### Documentation updated in this slice

`PROJECT_STATE.md` (§3 migration count and figures, §6.2 configuration inventory), `NEXT_STEPS.md`,
`HANDOFF_PROMPT.md`, and this file. **`ERD.md` needed no change** — Slice 1 already updated the
three affected rows, and this slice's three-way review confirmed the rest already described what was
built.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **1,068 passing, 0 failing** (310 Domain, 295 Application,
  198 Infrastructure, 265 Api). Slice 2 added **15**, all in Infrastructure.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Eight** migrations.

---

## Slice 3 — Create Invoice + remaining balance + numbering + VAT allocation

**Scope:** the first Invoice use case and the BR-3 balance read. No send, no mark-paid, no void, no
overdue, no project completion, no `InvoiceLine`, no Payment surface, no schema change.

### Scope reconstructed from the documents, before any code

| Question | Answer, and where it comes from |
|---|---|
| Endpoints | `POST /api/v1/projects/{id}/invoices` (Architecture §5.2, Sequence §8, Wireframe E2) and `GET /api/v1/projects/{id}/invoice-balance` (Sequence §8, BR-3; §5.2 lacked the row) |
| Controllers | Creation on a new `InvoicesController` (Admin-only); the balance on `ProjectsController`, because it is Project read data |
| Authorization | Create: Admin `F` / Inspector `—`. Balance: Admin `F` / Inspector `R`. **No `IOwnershipValidator` anywhere** — `F` and `R`, never `S` |
| Lifecycle | Creation only, into `Draft`. **No transition is performed in this slice** |
| Guard | StateMachine §5's, assigned to `CreateInvoiceCommand` by name: an Invoice needs an `Active`/`OnHold` Project |
| Numbering | A second method on the existing `INumberGeneratorService`, own sequence row, `RE-{YYYY}-{NNNNN}` |
| Transactions | One `SaveChangesAsync`. **No explicit transaction** — a single insert is already atomic |
| New Domain / schema / migration | **None**, apart from the pure `VatAllocation` calculation |

### Two questions the documents did not answer — raised before implementation, decided by the user

1. **The balance endpoint's authorization was undocumented.** `PermissionMatrix.md` §5 had no row for
   it, and it grants Inspectors `—` on every Invoice action but `R` on Project detail. **Decided:
   Admin `F` / Inspector `R`, as Project financial-summary data**, governing Slice 6's
   `ProjectDetailDto` too — while conferring no Invoice-management permission. §5 gained the row.
2. **A positive Invoice against a zero-gross Angebot has no defined allocation.** Reachable, not
   hypothetical: `AngebotItem` accepts a `UnitPrice` of zero, so an all-zero Angebot reaches
   `CustomerApproved`. **Decided: reject with `ConflictException` → 409**, inventing no rate — and
   kept as narrow as the arithmetic, so a **zero-gross Invoice** against the same Angebot is still
   allowed, and a zero-valued Project stays valid.

### Design points worth not rediscovering

- **The invariant is enforced twice, on purpose.** `VatAllocation` guarantees
  `Net + VAT == Gross` by construction; `Invoice.Create` re-checks it structurally. The allocator
  could be replaced tomorrow and the aggregate would still refuse an incoherent invoice.
- **Within each rate group, VAT is `share − net`, not a second rate calculation.** That is what makes
  the equality exact rather than approximate: a rounded net cannot leave a stray cent behind.
- **A zero target returns before the divisor is ever touched**, which is what makes the zero/zero case
  safe rather than lucky.
- **The residual rule is internal.** It is not in `BusinessRules.md` and has no ADR: no requirement
  specifies which rate group absorbs a cent, the per-rate detail is neither stored nor returned, and
  the tests pin the externally visible properties (exact totals, BR-11 rounding, proportionality,
  determinism) rather than the rule itself.
- **BR-3 is a warning end to end.** No comparison against `AgreedTotal` exists in the handler, no
  validator maximum exists, `Remaining` is never clamped, and there is no warning flag. The negative
  number *is* the warning, and it is asserted at three layers.
- **`AlreadyInvoiced` excludes `Void` and nothing else** — StateMachine §3.3's own wording. `Draft`
  counts exactly as `Paid` does.
- **The number is reserved last**, after every pre-evaluable guard (D66). Four tests assert the
  sequence is untouched on each rejection path.
- **`IProjectRepository.GetByIdAsync` was added** (write side, not `IProjectQueries`) because the
  status guard is a rule about the aggregate's own state.
- **201 carries no `Location`** — no invoice read endpoint is documented anywhere.

### One implementation fact EF Core forced, found by tests rather than inspection

`GetInvoiceBalanceAsync` was first written as a single query with a correlated `SUM` in the
projection. It does not translate: reading `.Amount` off a value-converted `Money` property works in
a plain projection (the Project detail read depends on that) but **not inside an aggregate, and not
inside a correlated subquery** — EF Core throws `InvalidOperationException` rather than silently
evaluating on the client, which is the good outcome. The shipped version is two statements, with
`EF.Property<decimal>` naming the provider column so the `SUM` still runs in SQL. Both are indexed
reads; the cost is one extra round trip, not a scan.

### Tests added (80)

**Domain (22)** — `VatAllocationTests`: the totals reconcile across a continuous 2,000-value range
and across a four-rate mix; a single rate derives exactly; allocating the whole Angebot gross
reproduces the Angebot's own net and VAT; half the gross carries half the VAT within a cent; the
result is order-independent and repeatable; zero targets never divide; a positive target against a
zero-gross mix is refused.

**Application (26)** — `CreateInvoiceCommandHandlerTests` (20) covering the happy path, the split,
one save, no transaction, the audit target, both 409s, the preserved zero/zero case, over-invoicing
accepted, and four "no number reserved" assertions; `GetProjectInvoiceBalanceQueryHandlerTests` (6)
covering pass-through of a negative remainder, the no-warning-field pin and the no-scope-parameter
pin.

**Infrastructure (12)** — `ProjectInvoiceBalanceQueriesTests` (8) against real LocalDB: no invoices
gives zero rather than null, Sequence §8's worked example, accumulation, the `Void` exclusion, a
negative remainder, isolation between Projects, unknown → null, and full cent precision through the
SQL `SUM`. `NumberGeneratorServiceTests` (4) gained the invoice format, sequential increment,
independence from the Angebot counter within one year, and a 50-parallel-caller uniqueness proof.

**Api (20)** — `InvoiceEndpointsTests` (16), plus 4 discovered automatically by the reflection-driven
`DependencyInjectionTests` (two handlers and two validators): the 201 contract with no `Location`,
the split on the wire, the row reaching the database, both role gates with empty-body assertions,
both 409-free BR-3 paths, the raw-JSON no-warning-field pin, and the two tests that hold Decision 1
together — an Inspector *can* read the balance, and doing so still leaves invoice creation 403.

### Adversarial verification

Each defect introduced, the suite run, the file restored byte-identically.

| # | Defect introduced | Result |
|---|---|---|
| 1 | Over-invoicing hard-blocked with a `ConflictException` | **2 failures** across Application and Api — `AnInvoiceExceedingTheAgreedTotalIsAccepted`, `Over_invoicing_is_allowed_and_reports_a_negative_remaining` |
| 2 | `Remaining` clamped with `Math.Max(0m, …)` | **2 failures** across Infrastructure and Api — the negative remainder disappeared at both layers |
| 3 | `Void` exclusion removed from the balance query | **1 failure** — `VoidInvoicesAreExcludedAndEveryOtherStatusCounts` |
| 4 | Number reserved immediately after loading the Project, before the guards | **2 failures** — `NoNumberIsReservedWhenTheProjectIsCompleted`, `NoNumberIsReservedWhenTheAngebotGrossIsZero` |
| 5 | `InvoicesController` widened to `Admin,Inspector` | **2 failures** — `An_inspector_cannot_create_an_invoice` and `Reading_the_balance_grants_an_inspector_no_invoice_permissions` |
| 6 | Balance read narrowed to Admin only | **2 failures** — `An_inspector_can_read_the_balance_and_is_not_scoped` and, again, the paired test |

Experiments 5 and 6 are worth keeping together: `Reading_the_balance_grants_an_inspector_no_invoice_permissions`
fails in **both** directions, which is what makes Decision 1's boundary — visibility without
management rights — a tested property rather than a comment.

### Documentation updated in this slice

- **`PermissionMatrix.md` §5** — new "View Project financial summary" row, Admin `F` / Inspector `R`,
  recording narrowly why it was added and that it grants no Invoice permission.
- **`Architecture.md` §5.2** — the missing `GET /api/v1/projects/{id}/invoice-balance` row, and the
  `POST /projects/{id}/invoices` row filled in with its real contract. **§8** — the invoice-numbering
  entry corrected to state the guarantee that exists (unique, never reused) rather than the one it
  claimed (gapless), with the BR-5→BR-9 mis-citation fixed and no assertion about German law.
- **`ARCHITECTURE_DECISIONS.md`** — **D66** added; thirteen rows added to the rejected-decisions table.
- **`PROJECT_STATE.md` / `NEXT_STEPS.md` / `HANDOFF_PROMPT.md`** and this file — current figures and
  the two decisions recorded as standing constraints.
- **`BusinessRules.md` deliberately unchanged.** BR-3 and BR-9 already say what the code does; the
  residual-cent rule is not a business rule and was not promoted into one.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **1,148 passing, 0 failing** (332 Domain, 321 Application,
  210 Infrastructure, 285 Api). Slice 3 added **80** — 22 Domain, 26 Application, 12 Infrastructure,
  20 Api.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Eight** migrations; this
  slice adds no schema.

---

## Slice 4 — Send Invoice + public token read

**Scope:** the `Draft → Sent` transition and the customer's read-only view. No mark-paid, no void, no
overdue, no project completion, no Payment surface, no `InvoiceLine`, no schema change, no PDF.

### Scope reconstructed from the documents, before any code

| Question | Answer, and where it comes from |
|---|---|
| Operations | **Send only.** Architecture §5.2, Sequence §9's first half, and this file's slice table |
| Endpoints | `POST /api/v1/invoices/{id}/send` (Admin `F`, PermissionMatrix §5) and `GET /api/v1/public/invoices/{token}` (anonymous, §7 "read-only", Wireframe A4) |
| Transition | `Draft → Sent`, guard `GrossAmount > 0` (StateMachine §3.3) — already in `Invoice.Send()`, so **this slice adds no Domain code** |
| Ownership scoping | **None on either.** Send is `F`; the public read has no principal at all |
| Audit | `InvoiceSent` → target `Invoice`, after the commit |
| Notification | FR-9.1 names Angebot **and Invoice**; Sequence §9 sends to `Customer.Email`. New `SendInvoiceReadyNotificationAsync` + `InvoiceReadyNotification`, after the commit |
| Transaction | One `SaveChangesAsync` covering the status change and the token row. **No explicit transaction** |
| Payment creation | Not here — Payments arrive with mark-paid in Slice 5 |
| `InvoiceLine` | Not required by anything in this slice; stays deferred |
| Project completion | Not assigned here, and its invoice precondition could not be enforced yet |

**No contradiction, no undocumented rule, no authorization or transaction ambiguity was found.**

### Design points worth not rediscovering

- **The Domain guard runs before the token is generated**, per Architecture §9's ordering principle.
  Nothing before the commit is irreversible, but the ordering is kept anyway — and two tests assert
  a refused send issues no token, commits nothing, audits nothing and emails nothing.
- **Both writes share one `SaveChangesAsync`.** A committed token link for an Invoice that never
  reached `Sent` is a live credential for a bill nobody issued; a `Sent` Invoice with no link is a
  customer who cannot see what they owe.
- **`InvoiceSent` is audited against the `Invoice`, not the Project** — unlike `AngebotSent`, which
  is logged against the Lead because sending an Angebot drives `Lead.MarkAngebotSent()`. Sending an
  Invoice changes no other aggregate's state at all.
- **The public read does *not* check `UsedAt`.** Sequence §12 scopes that check to "decision-type
  actions only", and for an Invoice the check is not merely skipped but **unreachable**:
  PermissionMatrix §7 grants the customer viewing and nothing else, so no Invoice decision action
  exists and no Invoice link is ever consumed. BR-4 mentions "an Invoice's decision-type action"
  hypothetically; none is documented and none was invented.
- **A wrong-entity-type token and an unknown token are the same branch**, so they cannot drift into
  producing distinguishable responses — a test asserts the two messages are identical.
- **No `Draft` guard on the public read.** A token link only exists once an Invoice is sent, so such
  a check would be unreachable code, and CLAUDE.md §6 forbids a handler re-checking aggregate state.
- **`PublicInvoiceDto` is a separate hierarchy**, never a projection of `InvoiceDto` — Phase 6's rule.
  Internal ids, `IssueDate`, `Status`, `VoidReason` and Payments are all withheld, pinned by property
  name at the Application layer and against raw JSON at the API layer.
- **The public invoice route inherits D65's rate limiter and `RouteDiagnostics`' token redaction by
  placement**, because it sits on the existing `PublicController` and its route parameter is named
  `token`. Both are pinned by tests rather than assumed.

### Three gaps recorded rather than filled

1. **Wireframe A4's "VAT (19%)" cannot carry a percentage.** An Invoice stores a VAT *amount* and no
   rate, because `InvoiceLine` is deferred and the per-rate split is computed at creation then
   discarded. The amount is exposed; deriving or assuming a rate on a document of this kind would
   fabricate a legally relevant figure.
2. **No bank details** (G-5) — A4 renders IBAN/BIC and no document defines where they live.
3. **No PDF and no attachment field** (G-4) — Sequence §9 draws `IPdfGenerator`; that is Phase 14's.
   FR-8.3's "token link, by email as a PDF, or both" is satisfied by the link.

**Flagged for Slice 5:** the public DTO carries no `Status`. At the end of Slice 4 a token-bearing
Invoice can only be `Sent`, so the field would be dead data — but Void and Paid arrive in Slice 5,
and **that is when it must be decided whether a voided or paid invoice says so on the customer's
page.** A voided invoice silently rendering as an ordinary payable bill would be the failure mode.

### Tests added (42)

**Application (21)** — `SendInvoiceCommandHandlerTests` (13): the transition, exactly one token
issued for the right entity type, both writes in one save, no explicit transaction, the audit target,
the email addressed to `Customer.Email` carrying the same token that was persisted, all four
rejections, and two "no residue on rejection" assertions. `GetPublicInvoiceByTokenQueryHandlerTests`
(8): the happy path, unknown/wrong-type/expired/empty-token/dangling-entity cases, the
identical-message proof, the used-token-still-renders rule, and the DTO property pin.

**Infrastructure (5)** — `InvoiceRepositoryTests` against real LocalDB: round trip, null for an
unknown id, `GetByIdAsync` eagerly loading `Payments` (CLAUDE.md §4's full-aggregate contract, which
Slice 5 will depend on), a mutation persisting through `SaveChangesAsync` alone, and `AddAsync` not
committing.

**Api (16)** — `InvoiceEndpointsTests` grew by 12 (send happy path, the token row actually landing,
the second-send 409, unknown 404, both send role gates, the public read, the raw-JSON field pin,
unknown and Angebot tokens both 404, the token never echoed in an error body, and the rate-limiter
placement pin), plus 4 discovered automatically by the reflection-driven `DependencyInjectionTests`.

### Adversarial verification

| # | Defect introduced | Result |
|---|---|---|
| 1 | `invoice.Send()` moved *after* the token was generated and staged | **2 failures** — `ARefusedSendIssuesNoTokenAndLeavesNoTrace`, `AZeroGrossRejectionIssuesNoToken` |
| 2 | Entity-type check dropped from the public read (an Angebot token would resolve) | **1 failure** — `AnAngebotTokenIsNotFoundRatherThanADistinctError` |
| 3 | Expiry check removed from the public read | **1 failure** — `AnExpiredTokenIsGone` |
| 4 | Internal `Id` added to `PublicInvoiceDto` | **2 failures** across Application and Api — both property pins |
| 5 | Class-level `[Authorize(Roles = Admin)]` weakened to `[Authorize]` | **3 failures** — `An_inspector_cannot_send_an_invoice`, `An_inspector_cannot_create_an_invoice`, `Reading_the_balance_grants_an_inspector_no_invoice_permissions` |

**Experiment 5's first attempt was a non-defect, and that is worth recording.** Adding
`[Authorize(Roles = "Admin,Inspector")]` to the *action* while leaving the class-level attribute in
place changed nothing and failed no test — ASP.NET Core **ANDs** multiple `[Authorize]` attributes,
so an action-level attribute can never widen a class-level gate. The experiment was redone at class
level, where it fails loudly. The lesson generalises: when adversarially testing a role gate, weaken
the attribute that actually governs, or the experiment proves nothing.

### Documentation updated in this slice

`Architecture.md` §5.2 (the send and public-invoice rows filled in with their real contracts),
`PROJECT_STATE.md`, `NEXT_STEPS.md`, `HANDOFF_PROMPT.md`, and this file. **`PermissionMatrix.md`,
`StateMachine.md`, `BusinessRules.md` and `ERD.md` all needed no change** — every rule this slice
implements was already stated correctly in them.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **1,190 passing, 0 failing** (332 Domain, 342 Application,
  215 Infrastructure, 301 Api). Slice 4 added **42** — 21 Application, 5 Infrastructure, 16 Api.
  Domain unchanged, correctly: the transition it uses already existed.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Eight** migrations; this
  slice adds no schema.
