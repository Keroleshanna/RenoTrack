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
| 5 | Mark Paid + Void | ✅ done |
| 6 | Complete Project + FR-7.4 Project detail invoice information | ✅ done |
| 7 | Overdue capability + Phase 8 completion gate | ✅ done |

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

---

## Slice 5 — Mark Paid + Void

**Scope:** the two remaining Admin-driven Invoice transitions, plus the public-status decision they
made unavoidable. No overdue, no project completion, no `InvoiceLine`, no schema change, no PDF, no
notifications.

### Scope reconstructed from the documents, before any code

| Question | Answer, and where it comes from |
|---|---|
| Operations | **Mark Paid and Void.** Architecture §5.2, PermissionMatrix §5, Sequence §9's second half |
| Transitions | `Sent`/`Overdue` → `Paid` (guard "—"); `Draft`/`Sent`/`Overdue` → `Void` (guard: a reason). StateMachine §3.3 — **both already in the Domain since Slice 1**, so this slice adds no Domain code |
| Endpoints | `POST /api/v1/invoices/{id}/mark-paid` and `POST /api/v1/invoices/{id}/void`, both Admin `F` |
| Ownership | **None** — both `F`, no Inspector operation exists in this slice at all |
| Payment | Created by `Invoice.MarkPaid`; `Amount` always the Invoice's gross; `paidAt`/`method` from the body; `RecordedByAdminId` from the token |
| Partial payment | Impossible — no amount parameter exists anywhere on the path |
| Duplicate payment | Impossible — `Paid` is terminal, so a second confirmation is a 409 |
| Transactions | One `SaveChangesAsync` each. **No explicit transaction** — mark-paid's two changes are two changes to *one* aggregate |
| Audit | `InvoicePaid` and `InvoiceVoided`, both against `Invoice`, both after the commit. Void's `details` carries the reason, which §3.3 requires explicitly |
| Notifications | **None.** FR-9.1 covers sending; FR-9.2's three triggers include neither; §9's mark-paid segment draws no mail step |
| Schema / migration | **None** |

### The decision this slice needed, and the answer

Slice 4 flagged that `PublicInvoiceDto` carried no status, and that the question became real the
moment `Paid` and `Void` were reachable. The documents do not decide it — Wireframe A4 renders no
status and PermissionMatrix §7 grants only "read-only". Put to the user with three options; **option
(b) approved**:

- A dedicated **`PublicInvoiceStatus`** enum — `Open` / `Paid` / `Void` — never the internal
  `InvoiceStatus`, matching the shape `PublicAngebotDecision` already established.
- `Draft`, `Sent` and `Overdue` all map to `Open`. The customer knows their own due date; exposing a
  dunning state is a decision no document makes.
- **The token link is not invalidated by `Paid` or `Void`** — no 404, no 410. It stays readable and
  now says what happened.
- Nothing else added. In particular **`VoidReason` stays internal**: the customer is told *that* the
  invoice was cancelled, never the staff wording behind it.

Without this field a voided invoice would have gone on rendering as an ordinary payable bill, and a
paid one would still have shown a due date as though outstanding.

### Design points worth not rediscovering

- **Mark-paid's two writes are one aggregate.** The status change and the new `Payment` child are
  tracked together, so a single `SaveChangesAsync` is genuinely atomic — D48's explicit boundary
  would add a lock scope for nothing.
- **Duplicate payment is structurally impossible**, not merely rejected: `MarkPaid` guards
  `Sent`/`Overdue` and `Paid` is terminal. A test asserts the second attempt leaves the Payment count
  at one.
- **The void reason is stored twice, and both are required by documents.** `Invoice.VoidReason` is
  the business data; §3.3's side-effect column additionally specifies an "AuditLog entry **with
  reason**". The audit row records who cancelled it and why *at that moment*; the invoice row records
  what the document now carries.
- **Voiding needs no balance code.** §3.3's "excluded from remaining balance math going forward" was
  already satisfied by Slice 3's query filter — an API test drives the whole path and watches
  `alreadyInvoiced` fall to zero.
- **Marking paid moves the balance by nothing**, and a test pins that too. §3.3's "Project balance
  recalculated" is imprecise rather than wrong: no balance is stored, and a `Sent` invoice already
  counted. `StateMachine.md` was left unedited — the wording describes an effect that is simply
  vacuous here, not a rule the code contradicts.
- **Neither handler takes an `IEmailSender`**, pinned by reflection, so a notification cannot be
  added without a visible signature change to review against FR-9.1/FR-9.2.

### Two findings from the adversarial run worth keeping

**1. Removing the void-reason *validator* rule is invisible at the API layer.** The Application test
caught it immediately (a `ValidationException` became an `ArgumentException`), but
`Voiding_without_a_reason_is_a_bad_request` **still passed** — because D59 maps both
`ValidationException` and `ArgumentException` to **400**, so the HTTP status is identical either way.
That is defence in depth working as intended, and it is also the same lesson Phase 4 Slice 9 recorded
about role-gate versus ownership 403s: **a status-code assertion cannot tell you which layer
rejected a request.** The layer is pinned by the Application test, and only there.

**2. A mangled adversarial edit produced 18 build errors and a stale test run that looked like a
result.** The rerun after fixing the edit gave the real answer. Worth stating plainly: an adversarial
experiment that does not compile proves nothing, and `--no-build` will happily re-run the previous
binary. Check the build before believing the run.

### Tests added (54)

**Application (35)** — `RecordPaymentCommandHandlerTests` (15): both source states, exactly one
Payment with the supplied date and method, the amount always the gross, one save with no transaction,
the audit target, all four rejections, the duplicate-payment proof, and three reflection pins (no
ownership validator, no email sender, no amount anywhere on the command).
`VoidInvoiceCommandHandlerTests` (14): all three voidable states, the number and row surviving (BR-9),
one save, the audit entry carrying the reason, both terminal-state refusals, blank-reason rejection,
no-residue, and two reflection pins. `GetPublicInvoiceByTokenQueryHandlerTests` grew by 6 — the
updated property pin, the dedicated-enum pin, and the four mapping cases (`Sent`→`Open`,
`Overdue`→`Open`, `Paid`→`Paid` and still readable, `Void`→`Void` and still readable), plus the
reason-never-exposed assertion.

**Api (19)** — `InvoiceEndpointsTests` grew by 15 (mark-paid happy path, the Payment row really
landing with the right amount/method/admin, the duplicate 409 adding no second row, a Draft refused,
the Inspector gate, the balance *not* moving on payment; void happy path, the row and number
surviving, the balance *falling* on void, blank-reason 400, paid-invoice 409, the Inspector gate; and
three public-surface tests — Paid and Void both still resolving 200 with the right status, and the
void reason never appearing in the body), plus 4 discovered by `DependencyInjectionTests`.

### Adversarial verification

| # | Defect introduced | Result |
|---|---|---|
| 1 | `Void` dropped from the public status mapping (a voided invoice would read `Open`) | **1 failure** — `AVoidedInvoiceRemainsReadableAndReadsAsVoid` |
| 2 | Audit written *before* the commit in `RecordPaymentCommandHandler` | **1 failure** — `ThePaymentIsAuditedAgainstTheInvoice` (two entries where one was expected) |
| 3 | `Reason` `NotEmpty` rule removed from the validator | **2 failures** in Application — and, notably, **none at the API layer** (see finding 1 above) |
| 4 | Void reason dropped from the audit `details` | **1 failure** — `TheVoidIsAuditedAgainstTheInvoiceWithTheReason` |
| 5 | `VoidReason` added to `PublicInvoiceDto` | **4 failures** across Application and Api — both property pins and both reason-never-exposed tests |

Experiment 5 is the one worth keeping: the leak fails at two layers and in two different ways (a
structural property pin and a content assertion), so removing either test alone would not silently
open the hole.

### Documentation updated in this slice

`Architecture.md` §5.2 (the `mark-paid` row filled in with its real contract; a **new `void` row**,
which the table had never carried despite PermissionMatrix §5 granting the action; and the
public-invoice row updated for the new `status` field), `PROJECT_STATE.md`, `NEXT_STEPS.md`,
`HANDOFF_PROMPT.md`, and this file. **`PermissionMatrix.md`, `StateMachine.md`, `BusinessRules.md`
and `ERD.md` needed no change** — every rule implemented here was already stated correctly in them.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **1,244 passing, 0 failing** (332 Domain, 377 Application,
  215 Infrastructure, 320 Api). Slice 5 added **54** — 35 Application, 19 Api. Domain and
  Infrastructure unchanged, correctly: both transitions and the `Payments` schema already existed.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Eight** migrations.

---

## Slice 6 — Complete Project + FR-7.4 Project detail invoice information

**Scope:** the Project's terminal transition with its invoice guard and FR-8.6 override, plus
FR-7.4's Invoice portion on the Project detail read. No overdue, no `InvoiceLine`, no PutOnHold /
Resume, no schema change, no migration, no notification, no Domain change.

### Scope reconstructed from the documents, before any code

| Question | Answer, and where it comes from |
|---|---|
| Operations | **Complete a Project**, and **serve FR-7.4's Invoice portion**. FR-7.3, FR-8.6, StateMachine §4.3/§5, Sequence §10, Wireframe E1 |
| Endpoints | `POST /api/v1/projects/{id}/complete` (Admin `F`, PermissionMatrix §5) and the existing `GET /api/v1/projects/{id}`, extended |
| Transition | `Active → Completed`, terminal. **Already in `Project.Complete()` since Phase 7**, so this slice adds **no Domain code** |
| Ownership | **None.** Completion is `F`; the detail read is `F`/`R` — read-only but unscoped |
| Audit | `ProjectCompleted` → target `Project`, after the commit. New `AuditAction` value |
| Notification | **None.** FR-9.1 covers sending; FR-9.2's three triggers exclude this; Sequence §10 draws no mail participant |
| Transaction | One `SaveChangesAsync`. **No explicit transaction** — one status change on one aggregate |
| Schema / migration | **None.** Eight migrations, unchanged |

### The design review found four unanswered questions and one three-way contradiction

All five were put to the Product Owner **before any code**, and none was reconciled silently. The
full record is **D67**; the reconciliation lives in `StateMachine.md` §4.4.

1. **Which Invoice statuses block completion was contradicted three ways** — `StateMachine.md`
   §4.3 ("all `Paid` or `Void`", so a `Draft` blocks), §3.4 ("any in `Sent` or `Overdue`", so a
   `Draft` does not) — *four sections apart in the same file* — and Sequence §10 ("any invoice not
   Paid", so a `Void` blocks, contradicting both). **Decided: §4.3.** `Draft`/`Sent`/`Overdue`
   block; `Paid`/`Void` do not.
2. **A Project with zero Invoices was undefined** — "all are `Paid` or `Void`" is vacuously true
   over an empty set. **Decided: blocked**, completable only through the override. A new rule, not
   a reading of an old one.
3. **`forceOverride` when nothing blocks was undefined.** **Decided: 400, and no audit entry** —
   an override must override something.
4. **Whether a non-override completion is audited was undefined** (§4.3 names an AuditLog entry on
   its override row only). **Decided: audit every successful completion**, `details` null on the
   normal path.
5. **The override reason has no column.** **Decided: AuditLog only** — follow the documents, record
   D50's best-effort consequence as a known limitation, invent no schema.

### Design points worth not rediscovering

- **The blocking predicate has two clauses, and it is declared in the Application layer.**
  `IInvoiceRepository.HasCompletionBlockingInvoicesForProjectAsync`'s doc comment is where the rule
  is stated; Infrastructure answers it with two indexed existence probes. Putting the rule in the
  interface keeps an Application decision out of the persistence layer while still letting the
  repository answer one named business question (CLAUDE.md §4).
- **Two guards, two layers, never merged.** `Project.Complete()` owns `Active`-only; the handler
  owns the invoice precondition. **The override reaches the second and never the first** — pinned
  by a test for both `OnHold` and `Completed`.
- **The handler never checks `Project.Status`** (CLAUDE.md §6). The `Active` rule has exactly one
  home.
- **The empty-override 400 uses FluentValidation's own `ValidationException`**, so both of the
  endpoint's 400s produce one field-keyed body. `ArgumentException` would have given the same
  status with a different shape for the same class of caller error.
- **A reason without an override is refused, not dropped** — the accept-and-discard pattern Phase 6
  refused for FR-6.3.
- **`AlreadyInvoiced` on the detail read is summed from the rows already fetched**, not by a second
  SQL `SUM`. The list is needed anyway and carries the same `GrossAmount`; a third round trip would
  also meet the value-converted-`Money`-inside-an-aggregate constraint Slice 3 hit. **An
  Infrastructure test asserts the detail read and the balance endpoint report identical figures**,
  so the deliberate duplication FR-7.4's "in one place" requires cannot drift.
- **Voided Invoices stay in the list and leave the arithmetic.** Two independent failure modes, two
  independent tests.
- **The list is ordered `IssueDate` then `Id`.** No document specifies an order; an unordered list
  read can present rows differently between identical requests, and `IssueDate` is not unique.
- **A completed Project can no longer be invoiced**, because StateMachine §5's existing guard in
  `CreateInvoiceCommand` requires `Active`/`OnHold`. Slice 3 and Slice 6 wrote those guards without
  reference to each other; an API test drives the whole path to prove they meet correctly.

### Guard ordering — a three-rule conflict, surfaced and decided rather than interpreted away

**The shipped order is: load → invoice predicate → 409 / 400 → `Complete()` → save → audit.**
Nothing touches the aggregate until every precondition has passed.

Reaching that took three attempts, and the middle one is worth recording because it was rejected
for the right reason.

**The conflict.** The reviewed ordering asked for the Project's own `Active` state to be verified
*before* the invoice predicate. There are exactly three mechanisms for that, and each is closed by
a rule this project has already committed to:

| Mechanism | Blocked by |
|---|---|
| Handler reads `project.Status` | **CLAUDE.md §6** — "A handler never checks `Status`… itself before calling a Domain method", whose only exception is ordering "for a non-Domain reason… **never to duplicate a state check**" |
| A public throwing probe (`Project.EnsureCanComplete()`) | **CLAUDE.md §2** — "do not grow the Domain's public surface just to answer a question the aggregate's own mutator already answers by throwing" |
| Read `Status` from `IProjectQueries` | §6 again, on a read projection |

An intermediate implementation called `Complete()` first and let the request-scoped `DbContext`'s
disposal discard the mutation when a later guard threw. **That was rejected by the Product Owner**:
a Domain state transition must not be used as a validation probe, and correctness must not rest on
scope lifetime. An `EnsureCanComplete()` probe was then proposed and also rejected — the reading
that §2's prohibition is limited to *properties* leans on its parenthetical example against its
general sentence, and reinterpreting a rule to make an implementation possible is not a decision
this project makes.

**Decided: keep §2 and §6 exactly as written, and change the ordering instead.** The cost is error
precedence on a Project that is not `Active`:

| Project state | Invoices | `forceOverride` | Result |
|---|---|---|---|
| `OnHold` / `Completed` | blocking | no | 409, **invoice wording** (not the Project-state message) |
| `OnHold` / `Completed` | blocking | yes + reason | 409, `Project.Complete()`'s own state message |
| `OnHold` / `Completed` | all settled | no | 409, `Project.Complete()`'s own state message |
| `OnHold` / `Completed` | all settled | yes + reason | **400 "nothing to override"** |

**Every cell refuses, and no combination of inputs completes a non-`Active` Project** — the
security and Domain outcomes are correct throughout; only the reported reason differs in rows 1
and 4. All eight combinations (both non-`Active` statuses × blocking/settled × override/no-override)
are enumerated in one Theory that asserts the exception type *and* that the Project's status is
unchanged, nothing committed and nothing audited.

**No refusal path leaves a mutated aggregate**, and that is now a property of ordering rather than
of scope lifetime — asserted directly for all three blocking statuses and for the rejected
override.

**`Project.Complete()` remains the only transition to `Completed`.** `ProjectTests`'
`ExposesExactlyTheDocumentedTransitions` pins the aggregate's public mutating surface to exactly
`Complete`/`PutOnHold`/`Resume`, so a second path — including a precondition probe — fails a Domain
test before it can be used.

### Tests added (80)

**Domain (0)** — correctly: `Project.Complete()` and its exhaustive state-machine coverage have
existed since Phase 7 Slice 1, and this slice adds no Domain code.

**Application (42)** — `CompleteProjectCommandHandlerTests` (40): both settled statuses and a mixed
set; one save with no transaction; all three unsettled statuses blocking; the two named
reconciliation tests (a `Draft` blocks *though §3.4 would not*, a `Void` does not *though Sequence
§10 would*); the zero-Invoice clause; isolation from another Project's Invoices; the override from
every blocked state; the empty-override 400 auditing nothing; three bad-reason cases and the mirror
rule; **all eight non-`Active` combinations, each asserting the accepted exception type plus an
unchanged status, no commit and no audit**; **two "leaves the Project untouched" tests covering both
refusal paths**; both audit shapes; no audit after a failed commit; not-found before any Invoice is
read; and two reflection pins (no `IOwnershipValidator`, no `IEmailSender`).
`GetProjectByIdQueryHandlerTests` gained 2 — the invoice portion passing through untouched, and the
`ProjectInvoiceDto` property pin.

**Infrastructure (15)** — `ProjectQueriesTests` (7) against real LocalDB: the empty case, E1's
worked example, `Void` in the list but out of the figures, agreement with the balance endpoint, a
negative remainder, the `IssueDate`-then-`Id` ordering (with two same-day rows making the tiebreaker
load-bearing), and isolation between Projects. `InvoiceRepositoryTests` (8): the zero-Invoice
clause, all three blocking statuses, both settling statuses, one unsettled among settled, and
per-Project isolation in both directions.

**Api (23)** — `ProjectEndpointsTests` grew by 19 (the updated raw-JSON property pin, the invoice-row
pin, the list plus figures agreeing with the balance endpoint, `Void` visible but excluded, the
Inspector seeing the list while still being refused invoice creation, the happy path with the row
really reaching the database, the explicit-`false` body, both 409s, the zero-Invoice 409, the
override happy path, the reason reaching `AuditLogs`, the empty-override 400 leaving no row, three
bad-reason 400s, the mirror-rule 400, the double-completion 409, the Inspector 403 with an
empty-body assertion, unauthenticated 401, unknown 404, and the completed-Project-cannot-be-invoiced
crossover), plus 4 discovered automatically by the reflection-driven `DependencyInjectionTests`.

### Adversarial verification

Each defect introduced, the suite run, the file restored and confirmed **byte-identical** by `diff`.

| # | Defect introduced | Result |
|---|---|---|
| 1 | Zero-Invoice clause removed from the repository | **2 failures** across Infrastructure and Api — `AProjectWithNoInvoicesIsBlocked`, `Completing_a_project_with_no_invoices_at_all_is_a_conflict` |
| 2 | Guard set changed to §3.4's (a `Draft` stops blocking) | **13 failures** across all three layers, including both named reconciliation tests |
| 3 | The empty-override 400 removed | **2 failures** — `AnOverrideWithNothingToOverrideIsRejectedAndAuditsNothing` and its Api counterpart |
| 4 | `Void` allowed to count toward `AlreadyInvoiced` on the detail read | **3 failures** — the two Infrastructure tests (including the balance-agreement test) and the Api one |
| 5 | Audit moved before `SaveChangesAsync` | **1 failure** — `AFailedCommitAuditsNothing` |

A second round of seven was run after the ordering decision, each one aimed at a property the
Product Owner named explicitly. Projects run are stated per row; each file was restored and
confirmed byte-identical by `diff`.

| # | Property under test | Defect introduced | Result |
|---|---|---|---|
| 1 | `Draft`/`Sent`/`Overdue` block without an override | Status clause disabled in both the repository and the fake | **29 failures** — 26 Application, 3 Infrastructure |
| 2 | `Paid`/`Void` stay non-blocking | `Paid` and `Void` added to the status clause | **17 failures** — 14 Application, 3 Infrastructure |
| 3 | Zero Invoices still require an override | Zero-Invoice clause removed | **3 failures** — `AProjectWithNoInvoicesAtAllIsBlocked`, `AnOverrideCompletesAProjectWithNoInvoicesAtAll`, `AProjectWithNoInvoicesIsBlocked` |
| 4 | `forceOverride` with nothing blocking returns 400 | The empty-override branch disabled | **6 failures** — 4 Application, 2 Api |
| 5 | No override completes `OnHold`/`Completed` | `Project.Complete()`'s guard loosened to admit `OnHold` | **4 failures** — 2 Domain (`Complete_FromAnyOtherState_Throws(OnHold)`, `FailedTransition_LeavesCompletedAtUntouched`), 2 Application ordering cells |
| 6 | `Complete()` is the only transition to `Completed` | A public `ForceComplete()` added to `Project` | **1 failure** — `ExposesExactlyTheDocumentedTransitions` |
| 7 | No Project mutation on any refusal path | `Complete()` moved back ahead of the guards (the rejected intermediate design) | **8+ failures** in Application — every "leaves the Project untouched" assertion and six ordering cells |

Experiment 6 is the one worth keeping. It shows the aggregate's existing public-surface test is
what would have caught an `EnsureCanComplete()` probe **as a failing Domain test**, independently
of anyone reading §2 — the rule and the test agree without being coupled.

Two findings worth keeping.

**1. Round one's experiment 2 exposed the error-precedence question empirically, not by
inspection.** Making a `Draft` non-blocking caused the non-`Active` test to fail for **both**
`OnHold` and `Completed`, because with nothing blocking, the empty-override 400 fired before
`project.Complete()` could refuse. That is what surfaced the whole ordering question — which then
turned out to be a three-rule conflict requiring a decision, not a bug requiring a fix. **An
adversarial experiment that fails for a reason you did not predict is a finding, not noise.**

**2. `git checkout` is the wrong tool for restoring an adversarial edit to an uncommitted file.**
Restoring experiment 1 that way reverted the file to `HEAD`, silently deleting the entire new
repository method along with the injected defect. The build caught it immediately, but the lesson
generalises: within an unfinished slice, copy the file aside first and restore from that copy —
`git checkout` restores the last commit, not the last good state.

### Documentation updated in this slice

- **`StateMachine.md`** — §4.3's two rows rewritten with the real guards; **new §4.4** recording the
  three-way reconciliation, the zero-Invoice rule, the override's exact reach, the empty-override
  refusal and the reason's storage; §3.4's contradicting invariant corrected; §4.2's diagram label
  fixed from "all invoices Paid" to "Paid/Void".
- **`Sequence Diagram.md` §10** — a correction note: its `alt Any invoice not Paid` reads as though
  `Void` blocks; it does not. The note also records the two steps the diagram omits (the audit on
  every completion, and the empty-override 400).
- **`Architecture.md` §5.2** — the missing `POST /api/v1/projects/{id}/complete` row, with its full
  contract; the `GET /api/v1/projects/{id}` row updated for FR-7.4 now being served in full.
- **`PermissionMatrix.md` §5** — "View Project detail" clarified to cover the Invoice list, and
  "Mark Project Completed" annotated with the endpoint and the override's limits.
- **`ARCHITECTURE_DECISIONS.md`** — **D67** added; fourteen rows added to the rejected-decisions
  table.
- **`PROJECT_STATE.md` / `NEXT_STEPS.md` / `HANDOFF_PROMPT.md`** and this file.
- **`ERD.md` and `BusinessRules.md` deliberately unchanged.** No schema moved, and no new `BR-n` was
  minted: the two new rules are state-transition rules, which `BusinessRules.md`'s own "How to add a
  new rule" routes to `StateMachine.md`. Promoting them to a numbered BR remains available if the
  Product Owner wants them cited from elsewhere.

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **1,324 passing, 0 failing** (332 Domain, 419 Application,
  230 Infrastructure, 343 Api). Slice 6 added **80** — 0 Domain, 42 Application, 15 Infrastructure,
  23 Api.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Eight** migrations; this
  slice adds no schema.
- **Environment note:** Smart App Control blocked `RenoTrack.Application.dll`,
  `RenoTrack.Infrastructure.Tests.dll` and `RenoTrack.Api.dll` intermittently during this slice
  (`FileLoadException 0x800711C7`), in Debug *and* initially in Release. It cleared on retry, and
  every figure above comes from a genuine Release run of the whole suite. Smart App Control was not
  modified, weakened or worked around.

---

## Slice 7 — Overdue capability + the Phase 8 completion gate

**Scope:** confirm the overdue capability against G-3, then run the phase's completion gate as a
full cross-document/repository audit. **No production code was added, and none was required** — see
the finding below. No schema, no migration, no new test.

### The overdue capability was already complete, and that is the finding

G-3 says *"the `Sent → Overdue` transition and whatever query/repository capability it genuinely
requires are built and tested"* while forbidding **every** mechanism that could invoke it (no Admin
endpoint, no `BackgroundService`, no read-time derivation). Reconstructed against the repository:

- `Invoice.MarkOverdue(DateTime asOf)` exists, guards `Sent` only, compares calendar days (an
  invoice due today is not overdue today), and is exhaustively tested — Slice 1.
- The `(Status, DueDate)` index exists — Slice 2, because `ERD.md` §3 defines it.
- **`MarkOverdue` has zero callers in `src/`.**

With no consumer, `CLAUDE.md` §4 forbids adding a repository method, and Phase 4 Slice 10 treats an
unreachable handler as a defect to close rather than a state to create. **"Whatever it genuinely
requires" therefore evaluates to nothing further.** Building an unused
`GetOverdueCandidatesAsync`, an unreachable `MarkInvoiceOverdueCommand`, or a scheduler were each
put to the Product Owner and each rejected.

**Recorded consequence, not a defect:** `InvoiceStatus.Overdue` is **unreachable in production**.
No invoice can enter it today, which means the `Overdue`-blocks-completion clause (K-1) and the
`Overdue → Open` public mapping are correct but currently unexercisable outside tests, and
`Overdue → Paid` / `Overdue → Void` are likewise unreachable. **Revisit trigger:** an explicitly
chosen job-hosting/scheduling strategy. Do not invent one to make a roadmap line look complete.

### The completion gate: a full audit, not a tidy-up

The Product Owner directed a **full** cross-document and cross-repository audit rather than only
fixing the contradictions already known. Twelve areas were audited. The findings are separated
below into **pre-existing contradictions**, **corrections made**, **deferred requirements** and
**verified-clean results**, so a later reader can tell which is which.

#### Pre-existing contradictions found (none caused by Phase 8)

| # | Contradiction | Resolution |
|---|---|---|
| X1 | `PROJECT_ROADMAP.md` Phase 8 promised a *"Scheduled check"* — the scheduler G-3 rejects | Roadmap corrected to describe the capability and record the automation gap |
| X2 | `PROJECT_ROADMAP.md` Phase 8 said real PDF generation is "Phase 11" (twice); Phase 11 is the Angebot Builder UI and **Phase 14** owns PDF, as `Architecture.md`/`ERD.md`/this file already said | Roadmap corrected to Phase 14 |
| X3 | `Architecture.md` §6 listed `InvoiceLine` as a built `Invoice` child, contradicting G-2 | §6 corrected to mark it designed-not-built, pointing at `ERD.md`'s deferral row |
| X4 | `Architecture.md` §14's milestone table numbers phases differently from `PROJECT_ROADMAP.md` (its "6" ≈ real Phases 7–8, its "11" = real Phase 14, its "12" = real Phase 15) | **`PROJECT_ROADMAP.md` declared canonical**; §14 retitled and reframed as an indicative grouping. Neither list renumbered |
| X5 | `BusinessRules.md` BR-3, `Sequence Diagram.md` §8 and `PROJECT_ROADMAP.md` all named a `GetRemainingInvoiceBalanceQuery` **that has never existed**; the shipped class is `GetProjectInvoiceBalanceQuery` | All three documents corrected. **The production class was not renamed** |
| X6 | `BusinessRules.md` BR-4 named `ValidateTokenLinkHandler` as BR-4's enforcer and `Sequence Diagram.md` §12 drew it as a participant — **it was never built** | Both corrected to describe the inlined validation in the three real handlers. **Explicitly not recorded as a missing implementation**: three call sites with different outcomes do not justify a shared abstraction, and building one would need a flag argument to express BR-4's asymmetry |
| X7 | `Architecture.md` §5.2 was titled *"Representative Endpoints"* while every slice since Phase 4 treated it as the authoritative inventory and logged missing rows as defects | Retitled **"API Endpoint Inventory"** and declared authoritative and exhaustive |
| X8 | `Sequence Diagram.md` §9 still drew `IPdfGenerator`/`GenerateAsync` with no correction note, unlike §7 and §10 | §9 annotated: deferred design intent, not implemented, consistent with G-4 |
| X9 | `CLAUDE.md` §21 asserts no schema exists for `Customer`/`Project`/`Invoice`/`InvoiceLine`/`Payment`/`TokenLink` — five of the six now do. **The rule is still right; the statement of fact is three phases stale** | **Recorded, deliberately not fixed.** The Product Owner ruled `CLAUDE.md` out of scope for this slice |

#### Implemented but undocumented (found by the endpoint reconciliation)

`Architecture.md` §5.2 was reconciled against all 8 controllers and their 38 actions. Three rows
were missing and have been added:

- `GET /api/v1/angebote/{id}/review-comments` — built Phase 5 Slice 2
- `POST /api/v1/auth/login` and `POST /api/v1/auth/refresh` — built Phase 4 Slice 4

The table now covers every endpoint the API exposes. Rows may cover sibling routes sharing one
contract; they may never omit one.

#### Deferred requirements newly recorded

Five `PermissionMatrix.md` grants have no endpoint and had never been written down anywhere: **edit
Lead contact details**, **assign/reassign an Inspector to a Lead**, **view a Lead's activity
timeline**, **reassign an Inspection**, and **change own password**. All predate Phase 8; none is
assigned a phase, because no authoritative document claims them. `NEXT_STEPS.md` §5a carries the
full record, including that reassigning an Inspection would need a Domain transition that does not
exist, and that Phase 15's *global* Audit Log screen does **not** cover Wireframe C1's per-Lead
timeline.

#### Verified clean — stated because absence of a finding is itself a result

- **No unreachable handlers.** 26 commands + 10 queries ↔ 36 non-auth endpoints, 1:1. The defect
  Phase 4 Slice 10 closed has not recurred; authentication's two endpoints deliberately have no
  command (D60).
- **No speculative interface growth.** Every method on all 18 interfaces has a production consumer.
- **No dead `AuditAction` values.** All 19 have a producer.
- **All 13 Phase 8 decisions hold in code**, checked individually: no `BackgroundService` or
  `IHostedService` anywhere; no `IPdfGenerator`; no IBAN/BIC field (all three appear only in
  explanatory comments saying why they are absent); no `InvoiceLine` type, `DbSet`, configuration or
  migration content; `VatAllocation` present; `Payments` the only cascade; `MarkPaid` still takes no
  amount.
- **All previously deferred items re-verified as still deferred**, including `POST /api/v1/leads`
  still carrying no rate limiter and `Roles.cs`'s namespace mismatch.
- **`ERD.md` matches the schema**: 12 `DbSet`s, 17 configurations (child entities correctly have a
  configuration and no `DbSet`), `RefreshTokens` documented, 8 migrations, no model drift.
- **`PermissionMatrix.md` matches every `[Authorize]`**, including `PublicController`'s deliberate
  class-level `[AllowAnonymous]` inversion and `InvoicesController`'s class-level Admin gate.
- **SRS FR-2.4's filtering is implemented** — `GetLeadsQuery` carries `Status`,
  `AssignedInspectorId` and `CreatedFrom`. It had never been confirmed anywhere.

### Documentation updated in this slice

`Architecture.md` (§5.2 retitled + 3 rows, §6, §14), `PROJECT_ROADMAP.md` (scheduler, two PDF phase
references, query name), `BusinessRules.md` (BR-3, BR-4 — **no new rule minted**),
`Sequence Diagram.md` (§8, §9, §12), `PROJECT_STATE.md` (§5.2–§8 fully reconciled),
`NEXT_STEPS.md` (five newly-recorded gaps), `HANDOFF_PROMPT.md`, and this file.
**`CLAUDE.md` deliberately untouched.**

### Phase 8 completion checklist

Phase 6's gate missed `GoneException`'s documentation because there was no list. There is one now;
every box was checked against the repository, not from memory.

- [x] Every approved Phase 8 decision (G-1…G-13) verified against the implementation
- [x] Every Phase 8 endpoint present in `Architecture.md` §5.2 — and §5.2 reconciled against *all*
      controllers, not only Phase 8's
- [x] `PermissionMatrix.md` reconciled against every `[Authorize]`/`[AllowAnonymous]`
- [x] `StateMachine.md` reconciled against the Domain, including unreachable-by-decision transitions
- [x] `ERD.md` reconciled against EF configurations, migrations and the model snapshot
- [x] `BusinessRules.md` "Enforced by" lines verified to name artefacts that exist
- [x] `Sequence Diagram.md` annotated wherever it depicts deferred design intent
- [x] Every deferred item re-verified as still deferred, and newly-found gaps recorded
- [x] Current-state documents (`PROJECT_STATE.md`, `NEXT_STEPS.md`, `HANDOFF_PROMPT.md`) reconciled
- [x] No unreachable handler, no speculative interface method, no dead enum value
- [x] `CLAUDE.md` unmodified; no approved decision reopened
- [x] Build (Debug **and** Release) 0 Warnings / 0 Errors; full Release suite green with all four
      projects executing; no model drift; 8 migrations
- [ ] **Publication** — push, PR, merge. **Deliberately not done.** "Phase 8 complete" means the
      branch is publishable-complete; publication is a separate, explicitly-authorised action

### Verification

- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors** in Debug **and** Release.
- `dotnet test RenoTrack.slnx -c Release` → **1,324 passing, 0 failing** (332 Domain, 419
  Application, 230 Infrastructure, 343 Api). **Slice 7 adds 0 tests** — correctly, since it adds no
  production code.
- `dotnet ef migrations has-pending-model-changes` → no pending changes. **Eight** migrations.
- Working tree clean; `CLAUDE.md` unchanged (`git diff` empty for it).
