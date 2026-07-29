# Business Rules

**Project:** RenoTrack — Renovation Company Website & Project-Tracking Dashboard
**Companion documents:** SRS.md, Architecture.md, StateMachine.md, ERD.md

This is the **single source of truth** for business rules. It exists as a standalone document (rather than a section inside SRS.md) specifically so new rules can be added over time without re-issuing the whole SRS. Rule IDs (`BR-n`) are permanent once assigned — a retired rule is marked **Superseded/Retired**, never renumbered or deleted, since other documents (SRS, Architecture, StateMachine) cite these IDs directly.

Each rule states: **the rule itself**, **why it exists**, and **where it's enforced**.

---

## How to add a new rule
1. Assign the next sequential `BR-n` — never reuse or renumber.
2. State it in the same format as below (Rule / Rationale / Enforced by).
3. Note the date and who requested it in the Changelog (§ bottom of this file).
4. If the rule affects a state transition, also update **StateMachine.md**. If it affects an entity's shape, also update **ERD.md**.

---

## Core Workflow Rules

### BR-1 — Angebot must be internally approved before sending
**Rule:** An Angebot cannot be sent to the Lead until it has been Approved internally by the Admin.
**Rationale:** Protects the company from a customer seeing an unreviewed, possibly-incorrect quote (this was an explicit pain point — the review loop exists precisely to catch mistakes before the customer sees them).
**Enforced by:** Guard clause in `SendAngebotCommand` (Application layer) — see StateMachine.md §2 (Angebot: `ApprovedInternally → Sent` only).

### BR-2 — Only an approved Angebot becomes a Project
**Rule:** Only an Angebot that the Lead has approved (`CustomerApproved`) can be converted into a Project.
**Rationale:** A Project represents committed, paid work — it must not exist without customer sign-off.
**Enforced by:** Guard clause in `ConvertAngebotToProjectCommand`. See StateMachine.md §4.

### BR-3 — Invoices should sum to the agreed total
**Rule:** The sum of all Invoices for a Project should equal the Project's Agreed Total. The system warns (does not hard-block) the Admin if invoices being created do not sum to the agreed total.
**Rationale:** Gives the Admin flexibility (e.g. rounding, a discount applied later) while still catching obvious data-entry mistakes.
**Enforced by:** `GetRemainingInvoiceBalanceQuery` surfaces the running total to the Admin on every invoice-creation screen (Sequence Diagram §8).

### BR-4 — A token link is single-use for decisions
**Rule:** Once a Lead has approved or rejected an Angebot via a token link, or an Invoice's decision-type action has been used, the same link cannot be reused for another state-changing action. Viewing (read-only) remains allowed.
**Rationale:** Prevents a forwarded or leaked email link from being used to flip a decision after the fact.
**Enforced by:** `TokenLink.UsedAt` check in `ValidateTokenLinkHandler` (Architecture §7.2; Sequence Diagram §12).

### BR-7 — Lead status only moves via explicit actions
**Rule:** A Lead's status can only move forward through the defined pipeline via explicit, named actions — never silently or as a side effect of an unrelated operation.
**Rationale:** Keeps the audit trail meaningful: every status change has a clear "why," which matters both for day-to-day trust in the dashboard and for resolving disputes later.
**Enforced by:** Every Lead status change happens inside a named Command handler that also writes an AuditLog entry (see StateMachine.md §1).

### BR-8 — Catalog edits don't retroactively change past Angebote
**Rule:** Editing or deleting a Catalog item never changes any AngebotItem previously created from it. Each AngebotItem stores its own copy of description, specification, unit, and price at creation time; `CatalogItemId` is a traceability link only, not a live reference.
**Rationale:** An Angebot is a historical/legal document once sent — its content must never silently change after the fact just because someone updated a template.
**Enforced by:** `AddAngebotItemCommand` copies catalog field values into the new `AngebotItem` row rather than joining live to `CatalogItem` at read time (Architecture §6, domain model).

### BR-10 — A completed Inspection is immutable
**Rule:** Once an Inspection is marked complete, its photos and notes can no longer be changed — `AddPhoto` and `UpdateNotes` are rejected once `CompletedAt` is set. Any future need to change a completed Inspection's record requires a distinct, explicit action (e.g. a "reopen" use case), not implicit editing.
**Rationale:** A completed Inspection is the evidentiary basis the Angebot is built from (FR-3.4). Allowing silent edits after completion would blur exactly what evidence the Angebot was based on and create audit ambiguity — the same risk BR-1's internal review gate exists to prevent for Angebote, and the same "locked after a workflow gate" pattern StateMachine.md §2.4 already applies to Angebot editing.
**Enforced by:** Self-guard inside the `Inspection` aggregate's `AddPhoto`/`UpdateNotes` methods (Domain layer) — both require `CompletedAt == null`.

### BR-11 — Monetary rounding strategy
**Rule:** All monetary calculations round to two decimal places immediately upon computation, using `MidpointRounding.AwayFromZero`. Line totals are rounded first; section subtotals and the Angebot's net total are plain sums of already-rounded line totals (no further rounding applied to the sum itself); VAT is calculated per VAT rate from the net amount at that rate, and each rate's VAT amount is itself rounded to two decimals; the gross total is the sum of the (already-rounded) net total and the (already-rounded) per-rate VAT amounts.
**Rationale:** No source document specified a rounding strategy, yet different reasonable choices produce different final Euro-cent totals on a legally significant document (BR-5, §14 UStG). Rounding immediately and always summing already-rounded values guarantees every number shown on any screen or PDF adds up exactly to the numbers displayed above it — avoiding an apparent "off by one cent" discrepancy that would look like an error to a customer manually checking an Angebot or invoice.
**Enforced by:** A single `Money` value object in `RenoTrack.Domain` that applies this rounding at construction for every derived monetary value (line totals, per-rate VAT amounts), so the rule lives in exactly one place instead of being repeated at each call site. Reused for Invoice totals (Architecture.md §6.1: "This same calculation is reused for Invoices").

---

## Financial & Legal Rules

### BR-5 — Mandatory German invoice fields (§14 UStG)
**Rule:** Every Invoice must contain, at minimum: full name and address of the company (incl. tax number/USt-IdNr.), full name and address of the customer, invoice date, a unique sequential invoice number, description/quantity of goods or services, net amount, applicable VAT rate(s) and VAT amount(s), gross total, and (if applicable) the delivery/service date.
**Rationale:** German legal requirement (Umsatzsteuergesetz §14) — not following this exposes the company to compliance risk.
**Enforced by:** Invoice PDF template (Architecture §10) is built to always include these fields; the Invoice entity's schema (ERD.md) captures all required data points.

### BR-6 — VAT rate is per line item, not per document
**Rule:** VAT rate is set on each individual line item, not once for the whole document — a single Angebot or Invoice may legitimately mix multiple VAT rates (e.g. 0%, 7%, 16%, 19%), as demonstrated in the company's real sample document.
**Rationale:** This is a proven real-world requirement (observed directly in the sample Angebot, not a hypothetical edge case) — some services/materials are taxed differently within the same job.
**Enforced by:** `VatRate` field lives on `AngebotItem`/`InvoiceLine`, not on `Angebot`/`Invoice` (ERD.md); totals calculation groups by rate before summing (Architecture §6.1).

### BR-9 — Invoice numbers are never reused
**Rule:** An Invoice number, once issued, is never reused or reassigned — even if that Invoice is later Voided. Void invoices are marked, not deleted.
**Rationale:** German invoice numbering must be strictly sequential with no gaps that suggest a "missing" invoice was destroyed — voiding preserves the sequence's integrity.
**Enforced by:** `NumberSequence` service only ever increments; `Invoice.Status = Void` is a state, not a delete (StateMachine.md §3).

---

## Changelog

| Rule | Added | Notes |
|---|---|---|
| BR-1 – BR-8 | Initial SRS v1.0 | Extracted from SRS.md §6 into this standalone document |
| BR-9 | Initial SRS v1.0 | Was implied in SRS.md §6 narrative ("invoice numbers never reused") but not previously numbered — formalized here |
| BR-10 | 2026-07-28 | Requested by Product Owner during Phase 1 Domain implementation of the Inspection aggregate — completed Inspections must be immutable; a future "reopen" action is the intended fix path, not silent editing |
| BR-11 | 2026-07-28 | Defined jointly with Product Owner during Phase 1 financial model design for the Angebot aggregate — no prior document specified a rounding strategy, so this codifies one explicitly before any financial code was written |
