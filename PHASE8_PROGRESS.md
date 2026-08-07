# PHASE8_PROGRESS.md — API: Invoices, Splitting, Payment Tracking, Project Completion

**Branch:** `feature/phase-8-invoices-payments-project-completion`, off `main` at `697292b` (PR #13, the Phase 7 merge).
**Roadmap entry:** `PROJECT_ROADMAP.md` Phase 8. **PR title:** `Phase 8: API — Invoice splitting, payment tracking, project completion guard`.

Seven implementation slices. Documentation is written in the slice that makes each decision real, not
deferred to a cleanup pass; Slice 7 ends with a cross-document completion sweep, which is a gate, not
a slice of its own.

| # | Slice | Status |
|---|---|---|
| 1 | Domain: `Invoice` + `Payment` child | ✅ done |
| 2 | Infrastructure: schema + migration #8 `AddInvoicesAndPayments` | ⬜ not started |
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
