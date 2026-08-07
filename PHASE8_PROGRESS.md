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
| 3 | Create Invoice + remaining balance + numbering + VAT allocation | ⬜ not started |
| 4 | Send Invoice + public token read | ⬜ not started |
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
