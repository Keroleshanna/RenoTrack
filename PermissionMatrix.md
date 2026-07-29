# Permission Matrix

**Project:** RenoTrack — Renovation Company Website & Project-Tracking Dashboard
**Companion documents:** SRS.md, Wireframes.md, StateMachine.md

Legend: **F** = Full access · **S** = Scoped (own assigned records only) · **R** = Read-only · **—** = No access

There are two internal roles (Admin, Inspector). Public Visitors and Leads/Customers never hold dashboard permissions — they act only through the public website and token-link pages, which are covered separately in §7.

---

## 1. Leads & Pipeline (Wireframes B2, C1)

| Action | Admin | Inspector | Notes |
|---|---|---|---|
| View Lead pipeline (all Leads) | F | — | Inspector's pipeline view is filtered server-side |
| View Lead pipeline (own assigned Leads) | F | S | Inspector only sees Leads where `AssignedInspectorId == self` |
| Create Lead manually (phone/email) | F | — | FR-2.1 — Admin-only per SRS |
| Edit Lead contact details | F | S | Inspector may correct details on their own assigned Lead (e.g. wrong phone number found on-site) |
| Change Lead status directly | F | — | Status changes happen as side effects of other actions (BR-7), not a free-standing edit — neither role edits status directly except via the defined transitions |
| Assign/reassign Inspector to a Lead | F | — | Admin decision |
| Delete a Lead | — | — | Not supported in v1 — Leads are never hard-deleted (matches BR-9's spirit for Invoices; nothing legal requires it for Leads, but it keeps the audit trail intact) |
| View Lead activity/audit timeline | F | S | Same scoping as the Lead itself |

---

## 2. Inspections (Wireframes C2, C3)

| Action | Admin | Inspector | Notes |
|---|---|---|---|
| Schedule an Inspection | F | — | FR-2.3 |
| View an Inspection | F | S | Inspector only if it's their own assignment |
| Upload photos to an Inspection | — | S | Only the assigned Inspector; Admin can view but not add photos (keeps evidence chain-of-custody clear to "who was actually on site") |
| Edit Inspection notes | — | S | Assigned Inspector only |
| Mark Inspection complete | — | S | Assigned Inspector only |
| Reassign an Inspection to a different Inspector | F | — | Admin only |

---

## 3. Angebot Builder (Wireframes D1, D2)

| Action | Admin | Inspector | Notes |
|---|---|---|---|
| Create Angebot draft | — | S | Only for a Lead assigned to that Inspector, and only once `InspectionDone` (FR-4.1 guard) |
| Edit Angebot while `Draft` or `ChangesRequested` | R | S | Admin can view a draft in progress but cannot edit it directly — keeps authorship/accountability clean; if Admin wants a change, they use "Request Changes" during review instead |
| Add/remove Sections & Items | — | S | Only the owning Inspector, only while editable (StateMachine.md §2.4) |
| Use Catalog picker | R | S | Both roles can browse the Catalog (R for Admin, S/F for Inspector — Inspector isn't "scoped" here since Catalog is shared, not per-Lead) |
| Save item as new Catalog entry | — | F | Any Inspector can contribute (FR-4.10); not Lead-scoped since the Catalog is shared company-wide |
| Duplicate a past Angebot/Section | — | S | Only from Angebote the Inspector has access to (their own past drafts, or any `Sent`/decided Angebot they're shown for reference — exact reference-library scope is an implementation detail, default to "their own" for v1) |
| Submit Angebot for review | — | S | Owning Inspector only |

---

## 4. Angebot Review & Sending (Wireframes D3)

| Action | Admin | Inspector | Notes |
|---|---|---|---|
| View Angebot in `InReview` | F | R | Inspector can see their own submission's status, read-only once submitted |
| Approve Angebot | F | — | Admin-only — this is the entire point of the internal review gate (BR-1) |
| Request changes (with comment) | F | — | Admin-only |
| Send Angebot to Lead (generate token link) | F | — | Admin-only (FR-6.1) |
| View review comment history | F | R | Both can read; only Admin writes new review comments |

---

## 5. Projects & Invoices (Wireframes E1–E3)

| Action | Admin | Inspector | Notes |
|---|---|---|---|
| Convert Angebot to Project | F | — | Admin-only (FR-7.1) |
| View Project detail | F | R | Inspector can view (e.g. to see the outcome of a Lead they worked), but not act on it |
| Create Invoice | F | — | Admin-only (FR-8.1) |
| Send Invoice | F | — | Admin-only |
| Mark Invoice Paid | F | — | Admin-only (FR-8.4) |
| Void an Invoice | F | — | Admin-only, requires a reason (StateMachine.md §3) |
| Mark Project Completed (incl. override) | F | — | Admin-only, override requires a reason (FR-8.6) |
| Put Project On Hold / Resume | F | — | Admin-only |

---

## 6. Catalog Management (Wireframes F1)

| Action | Admin | Inspector | Notes |
|---|---|---|---|
| View Catalog | F | F | Shared resource, both roles browse freely |
| Create/curate Catalog item directly (not via "save as") | F | — | Admin manages the "official" library |
| Add Catalog item via "save as Catalog item" (from an Angebot) | — | F | Organic growth path, any Inspector (FR-4.10) |
| Edit an existing Catalog item | F | — | Admin-only, to avoid one Inspector's edit surprising others (BR-8 already protects past Angebote from this, but future *new* Angebote using that template should reflect a deliberate, reviewed change) |
| Delete/retire a Catalog item | F | — | Admin-only. "Delete" means retiring the item (`IsRetired = true`), never a physical row delete — a retired item stops appearing in the Catalog picker (D2) but is kept so any AngebotItem previously created from it (BR-8) keeps a valid `CatalogItemId` trace link (BR-12) |

---

## 7. Public / Token-Link Surfaces (No Dashboard Role)

| Action | Public Visitor | Lead/Customer (via token) |
|---|---|---|
| Browse public website | ✅ | ✅ |
| Submit contact form | ✅ | ✅ |
| View Angebot via token link | — | ✅ (read-only, until decision made) |
| Approve/Reject Angebot via token link | — | ✅ (single-use, BR-4) |
| View Invoice via token link | — | ✅ (read-only) |
| Log in to the Dashboard | — | — (no account exists for this role, by design — SRS §"Out of scope") |

---

## 8. User/Account Administration

| Action | Admin | Inspector | Notes |
|---|---|---|---|
| Create/deactivate Inspector accounts | F* | — | *Resolves SRS Open Question OQ-1 as: yes, Admin manages Inspector accounts from the dashboard (recommended default — see note below) |
| Change own password | F | F | Every authenticated user can manage their own credentials |
| View Audit Log | F | — | Admin-only company-wide visibility; an Inspector's own actions still appear inside the Leads/Angebote they can already see (§1–3 above), just not as a separate global log screen |

> **Note on OQ-1:** This matrix assumes Admin can manage Inspector accounts in-app (simpler for the client than asking a developer for DB access every time they hire someone) — flag if a one-time manual setup is preferred instead, and this row will be removed from v1 scope.
