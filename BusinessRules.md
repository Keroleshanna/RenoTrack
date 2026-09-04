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
**Enforced by:** `GetProjectInvoiceBalanceQuery` surfaces the running total to the Admin on every invoice-creation screen (Sequence Diagram §8), served by `GET /api/v1/projects/{id}/invoice-balance` and repeated inside `GET /api/v1/projects/{id}` (FR-7.4). *(Corrected in the Phase 8 completion sweep: this rule, Sequence Diagram §8 and `PROJECT_ROADMAP.md` all named a `GetRemainingInvoiceBalanceQuery` that was never built under that name. The shipped class name is authoritative; nothing about the rule changed.)* The warning is the number itself — `Remaining` goes negative when invoices exceed the agreed total and is never clamped, never blocked and never accompanied by a separate warning flag.

### BR-4 — A token link is single-use for decisions
**Rule:** Once a Lead has approved or rejected an Angebot via a token link, or an Invoice's decision-type action has been used, the same link cannot be reused for another state-changing action. Viewing (read-only) remains allowed.
**Rationale:** Prevents a forwarded or leaked email link from being used to flip a decision after the fact.
**Enforced by:** the `TokenLink.UsedAt` check inside `RecordAngebotDecisionCommandHandler`, which is the only state-changing token action that exists; it consumes the link in the same `SaveChangesAsync` as the decision. The read-only handlers — `GetPublicAngebotByTokenQueryHandler` and `GetPublicInvoiceByTokenQueryHandler` — validate existence, entity type and expiry but deliberately **do not** check `UsedAt`, which is this rule's "viewing (read-only) remains allowed" half. *(Corrected in the Phase 8 completion sweep: this line and Sequence Diagram §12 named a shared `ValidateTokenLinkHandler` that was never built. Validation is inlined in each handler instead, and no such abstraction is required — three call sites with different outcomes do not justify one. This is a documentation correction, not a recorded gap.)*

**The rule became enforceable against concurrent requests in Phase 11 (`ARCHITECTURE_DECISIONS.md` D96).** `MarkUsed()`'s check only ever saw the state one `DbContext` had loaded, so two simultaneous decisions on the same link both passed it and both committed — consuming the link twice and, when the two callers chose differently, leaving `Angebot` and `Lead` in states that contradict each other. `TokenLinks.UsedAt` is now an optimistic-concurrency token, so the decision `UPDATE` carries `WHERE UsedAt IS NULL`; the loser matches no row and EF Core rolls back its entire batch, which is what makes a token on this one column sufficient to protect all three aggregates. `UnitOfWork` translates the loss into `ConflictException`, so the loser gets the same **409** a sequential second attempt already got. **This did not change the rule** — it closed the gap between what the rule said and what the database would actually refuse.

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
**Enforced by:** Self-guard inside the `Inspection` aggregate's `AddPhoto`/`UpdateNotes`/`Reassign` methods (Domain layer) — all three require `CompletedAt == null`.
**The anticipated "reopen" action now exists (Phase 10, `ARCHITECTURE_DECISIONS.md` D92).** `Inspection.Reopen()` clears `CompletedAt`, exposed as `POST /api/v1/inspections/{id}/reopen` and restricted to the **assigned Inspector** — `PermissionMatrix.md` §2 gives Admin `—` on every other edit to a visit, so the action that re-enables those edits belongs to whoever was on site. **This implements the escape hatch this rule already named; it does not weaken the lock:** the three guards above still refuse outright while the visit is complete, and reopening is a deliberate, audited act (`InspectionReopened`) rather than implicit editing. The Lead deliberately stays at `InspectionDone` — the visit did happen, and any Angebot built on it remains valid. **The audit trail, not `CompletedAt`, is what preserves the fact that the visit was completed**, reading `InspectionDone → InspectionReopened → InspectionDone`. Re-completing a reopened visit required a matching fix in the Application layer (**D93**), because the Lead-level transition must fire once, not once per completion.

### BR-11 — Monetary rounding strategy
**Rule:** All monetary calculations round to two decimal places immediately upon computation, using `MidpointRounding.AwayFromZero`. Line totals are rounded first; section subtotals and the Angebot's net total are plain sums of already-rounded line totals (no further rounding applied to the sum itself); VAT is calculated per VAT rate from the net amount at that rate, and each rate's VAT amount is itself rounded to two decimals; the gross total is the sum of the (already-rounded) net total and the (already-rounded) per-rate VAT amounts.
**Rationale:** No source document specified a rounding strategy, yet different reasonable choices produce different final Euro-cent totals on a legally significant document (BR-5, §14 UStG). Rounding immediately and always summing already-rounded values guarantees every number shown on any screen or PDF adds up exactly to the numbers displayed above it — avoiding an apparent "off by one cent" discrepancy that would look like an error to a customer manually checking an Angebot or invoice.
**Enforced by:** A single `Money` value object in `RenoTrack.Domain` that applies this rounding at construction for every derived monetary value (line totals, per-rate VAT amounts), so the rule lives in exactly one place instead of being repeated at each call site. Reused for Invoice totals (Architecture.md §6.1: "This same calculation is reused for Invoices").

### BR-12 — Catalog items are retired, never deleted
**Rule:** A Catalog item can never be physically deleted. PermissionMatrix.md §6's "Delete/retire a Catalog item" action sets an `IsRetired` flag instead; a retired item is excluded from the Catalog picker (Wireframes.md D2) but the row itself is kept.
**Rationale:** BR-8 relies on `AngebotItem.CatalogItemId` remaining a valid traceability link back to the Catalog item an item was created from. A physical delete would leave that link dangling on every AngebotItem ever created from it, destroying the very traceability BR-8 exists to preserve. This matches the same "never truly delete a historical record" philosophy already applied elsewhere: Leads are never deleted (PermissionMatrix.md §1), Invoices are voided rather than deleted (BR-9), and a completed Inspection becomes immutable (BR-10).
**Enforced by:** `CatalogItem.Retire()` in `RenoTrack.Domain` sets `IsRetired = true`; there is no `Delete`/`Remove` method or repository operation that removes a row. `SearchCatalogItemsQuery` (Architecture.md §5.2) filters out retired items when building the picker (Application layer, Phase 5).

### BR-13 — Scheduling an Inspection assigns its Inspector to the Lead
**Rule:** When an Inspection is scheduled, the Inspector it is scheduled for automatically becomes the Lead's `AssignedInspectorId`. Scheduling for one Inspector while leaving the Lead assigned to a different (or no) Inspector is not supported.
**Rationale:** The Inspector being scheduled is the one performing work for that Lead. PermissionMatrix.md §1 scopes an Inspector's own pipeline view by `AssignedInspectorId` — without this automatic assignment, a scheduled Inspector would never see the Lead in their pipeline unless a separate, undocumented "assign inspector" step happened to precede or follow scheduling every time. No current requirement calls for scheduling one Inspector while the Lead stays assigned to someone else.
**Enforced by:** `ScheduleInspectionCommandHandler` (Application layer) calls both `Inspection.Schedule(...)` and `Lead.AssignInspector(inspectorId)` within the same operation. `Lead.AssignInspector` itself remains a general-purpose, status-independent method (Architecture.md §6.2) — this rule governs *when* the Application layer chooses to call it, not a change to `AssignInspector`'s own guard (or lack thereof).

### BR-14 — A retired CatalogItem remains a valid direct reference; retirement only affects discovery
**Rule:** Retiring a CatalogItem (BR-12) excludes it from discovery (`SearchCatalogItemsQuery` / the Catalog picker, Wireframes.md D2) only. It does **not** invalidate the item as a target of `AddAngebotItemCommand`'s `CatalogItemId` parameter — an Inspector who already holds a `CatalogItemId` (e.g. from a stale client-side cache, or a race between browsing and a concurrent retirement) may still successfully add an `AngebotItem` sourced from it.
**Rationale:** BR-8's copy-on-create semantics mean the resulting `AngebotItem` is functionally independent of a CatalogItem's retired status the instant it is created — the source's current state (including `IsRetired`) has no bearing on an item already snapshotted. No document previously stated that retired items become invalid references; BR-12 and PermissionMatrix.md §6 only describe exclusion from the picker and preservation of the traceability link for *already-created* items. This rule extends that same "kept for traceability" reasoning to also cover a possible *new* reference, rather than contradicting it.
**Enforced by:** `AddAngebotItemCommandHandler`'s `ICatalogItemRepository.GetByIdAsync` call is deliberately **not** filtered by `IsRetired` — unlike `ICatalogItemQueries.SearchAsync`, which filters per BR-12.

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
| BR-12 | 2026-07-28 | Defined jointly with Product Owner during Phase 1b design for the CatalogItem aggregate — resolves a contradiction found between PermissionMatrix.md (grants a "delete" action) and ERD.md (had no field to represent it) in favor of retiring, consistent with how Leads/Invoices/Inspections already treat historical records |
| BR-13 | 2026-07-28 | Defined jointly with Product Owner during Phase 2 design of ScheduleInspectionCommand — no prior document explicitly linked Inspection scheduling to Lead.AssignedInspectorId, despite PermissionMatrix.md's Inspector-pipeline scoping depending on that field being set |
| BR-14 | 2026-07-30 | Defined during Phase 2 design of AddAngebotItemCommand — no prior document stated whether a retired CatalogItem remains a valid CatalogItemId reference; verified the documentation set was genuinely silent (not just unread) before recording this as a new rule, rather than inferring an answer |
| BR-4 | 2026-09-04 | **Clarified, not changed**, during Phase 11 Slice 1: the rule always said "single-use", but nothing made the read-then-write atomic across concurrent requests, so two simultaneous decisions both succeeded (`ARCHITECTURE_DECISIONS.md` D96). `TokenLinks.UsedAt` became an optimistic-concurrency token. Found by reading the code during the Phase 11 assessment, not by a failing test — every existing test drove the endpoint sequentially |
| BR-10 | 2026-08-16 | **Amended, not relaxed**, during Phase 10 QA: the "reopen" use case this rule anticipated from the outset was built (`Inspection.Reopen`, `ARCHITECTURE_DECISIONS.md` D92). The immutability guards are unchanged; what changed is that correcting a completed visit is now possible through one explicit, audited action instead of being impossible. The Product Owner's original requirement stands |
