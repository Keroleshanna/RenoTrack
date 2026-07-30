# Entity Relationship Diagram (ERD)

**Project:** RenoTrack — Renovation Company Website & Project-Tracking Dashboard
**Companion documents:** SRS.md, Architecture.md, BusinessRules.md

> Note: this expands the simplified ERD shown in SRS.md §4.2 into full attribute-level detail — this file is the source of truth for the database schema.

---

## 1. Full Diagram (attributes, keys, cardinalities)

```mermaid
erDiagram
    USER {
        int Id PK
        string Name
        string Email UK
        string PasswordHash
        string Role "Admin | Inspector"
        bool IsActive
        datetime CreatedAt
    }

    LEAD {
        int Id PK
        string Name
        string Phone
        string Email
        string Address
        string Notes
        string Source "Website | Phone | Email"
        string Status "New..Won..Lost (see StateMachine.md)"
        int AssignedInspectorId FK
        datetime CreatedAt
    }

    CONTACTMESSAGE {
        int Id PK
        string Name
        string Email
        string Phone
        string Message
        int LeadId FK
        datetime SubmittedAt
    }

    INSPECTION {
        int Id PK
        int LeadId FK
        datetime ScheduledAt
        int InspectorId FK
        string Notes
        datetime CompletedAt "nullable"
    }

    INSPECTIONPHOTO {
        int Id PK
        int InspectionId FK
        string FileUrl
        string Caption "nullable"
        datetime UploadedAt
    }

    ANGEBOT {
        int Id PK
        int LeadId FK
        int InspectionId FK "nullable"
        string AngebotNumber UK "ANG-YYYY-NNNNN"
        string Status "Draft..CustomerRejected (see StateMachine.md)"
        int CreatedByInspectorId FK
        int ReviewedByAdminId FK "nullable"
        decimal NetTotal
        decimal GrossTotal
        datetime SentAt "nullable"
        datetime DecisionAt "nullable"
        datetime CreatedAt
    }

    ANGEBOTREVIEWCOMMENT {
        int Id PK
        int AngebotId FK
        int AdminUserId FK
        string Comment
        datetime CreatedAt
    }

    ANGEBOTSECTION {
        int Id PK
        int AngebotId FK
        string Title
        int SortOrder
    }

    ANGEBOTITEM {
        int Id PK
        int SectionId FK
        int CatalogItemId FK "nullable, traceability only (BR-8)"
        string Description
        string Specification "nullable, long text"
        decimal Quantity
        string Unit "m2 | Stk | lfm | pauschal | m"
        decimal UnitPrice
        int VatRate "enum-backed: 0 | 7 | 16 | 19 (Architecture §11 — enum chosen over decimal(5,2))"
    }

    CATALOGITEM {
        int Id PK
        string Title
        string DefaultSpecification "nullable"
        string DefaultUnit
        decimal SuggestedUnitPrice
        int CreatedFromAngebotItemId FK "nullable"
        bool IsRetired "default false (BR-12)"
        datetime CreatedAt
    }

    CUSTOMER {
        int Id PK
        int LeadId FK UK
        string Name
        string Address
        string Email
        string Phone
    }

    PROJECT {
        int Id PK
        int CustomerId FK
        int AngebotId FK UK
        string Status "Active | OnHold | Completed"
        decimal AgreedTotal
        datetime CreatedAt
        datetime CompletedAt "nullable"
    }

    INVOICE {
        int Id PK
        int ProjectId FK
        string InvoiceNumber UK "RE-YYYY-NNNNN"
        datetime IssueDate
        datetime DueDate
        string Status "Draft | Sent | Paid | Overdue | Void"
        decimal NetAmount
        decimal VatAmount
        decimal GrossAmount
        string VoidReason "nullable"
    }

    INVOICELINE {
        int Id PK
        int InvoiceId FK
        string Description
        decimal NetAmount
        decimal VatRate
    }

    PAYMENT {
        int Id PK
        int InvoiceId FK
        decimal Amount
        string Method "BankTransfer | Cash | Other"
        datetime PaidAt
        int RecordedByAdminId FK
    }

    TOKENLINK {
        int Id PK
        string EntityType "Angebot | Invoice"
        int EntityId
        string Token UK
        datetime ExpiresAt
        datetime UsedAt "nullable"
        datetime CreatedAt
    }

    NUMBERSEQUENCE {
        int Id PK
        string SequenceType "Angebot | Invoice"
        int Year
        int LastValue
    }

    AUDITLOG {
        int Id PK
        string EntityType
        int EntityId
        string Action
        int PerformedByUserId FK "nullable, null = System"
        string Details "nullable"
        datetime Timestamp
    }

    USER ||--o{ LEAD : "assigned as Inspector"
    LEAD ||--o{ CONTACTMESSAGE : "originated from"
    LEAD ||--o| INSPECTION : has
    USER ||--o{ INSPECTION : conducts
    INSPECTION ||--o{ INSPECTIONPHOTO : contains
    LEAD ||--o{ ANGEBOT : receives
    INSPECTION ||--o| ANGEBOT : "referenced by (optional)"
    USER ||--o{ ANGEBOT : "drafts (Inspector) / reviews (Admin)"
    ANGEBOT ||--o{ ANGEBOTREVIEWCOMMENT : has
    ANGEBOT ||--o{ ANGEBOTSECTION : contains
    ANGEBOTSECTION ||--o{ ANGEBOTITEM : contains
    CATALOGITEM ||--o{ ANGEBOTITEM : "pre-fills (optional)"
    ANGEBOTITEM ||--o| CATALOGITEM : "created from (optional, reverse trace)"
    LEAD ||--o| CUSTOMER : becomes
    CUSTOMER ||--o{ PROJECT : has
    ANGEBOT ||--o| PROJECT : "converts to"
    PROJECT ||--o{ INVOICE : has
    INVOICE ||--o{ INVOICELINE : contains
    INVOICE ||--o{ PAYMENT : receives
    USER ||--o{ PAYMENT : records
    ANGEBOT ||--o{ TOKENLINK : "sent via"
    INVOICE ||--o{ TOKENLINK : "sent via"
```

---

## 2. Physical Schema Notes (per table)

| Table | Primary Key | Notable Foreign Keys | Unique Constraints | Notes |
|---|---|---|---|---|
| Users | Id (int, identity) | — | Email | Password hashed at rest; never stored/logged in plaintext |
| Leads | Id | AssignedInspectorId → Users | — | Status stored as string enum for readability in raw SQL during support/debugging. `AssignedInspectorId` has no FK constraint until the Identity slice adds a `Users` table (Phase 3). |
| ContactMessages | Id | LeadId → Leads | — | Raw form submissions kept even if the Lead is later edited |
| Inspections | Id | LeadId → Leads, InspectorId → Users | — | One Lead usually has one Inspection (SRS notes this could be relaxed later). `InspectorId` has no FK constraint until the Identity slice. |
| InspectionPhotos | Id | InspectionId → Inspections | — | FileUrl points into the storage abstraction (Architecture §9), not a raw disk path exposed to clients |
| Angebote | Id | LeadId → Leads, InspectionId → Inspections (nullable), CreatedByInspectorId → Users, ReviewedByAdminId → Users (nullable) | AngebotNumber | NetTotal/GrossTotal are cached/denormalized for fast list-page rendering; recalculated from AngebotItems on every edit (BR-6, Architecture §6.1). **No `DecisionResult` column** — removed from the Domain entirely as a presentation-mapping concern, not a stored fact (`ARCHITECTURE_DECISIONS.md` D16); derivable from `Status` (`CustomerApproved`/`CustomerRejected`) wherever it's needed. `CreatedByInspectorId`/`ReviewedByAdminId` have no FK constraint until the Identity slice adds a `Users` table (Phase 3). |
| AngebotReviewComments | Id | AngebotId → Angebote, AdminUserId → Users | — | Append-only log of the review loop (SRS FR-5.4). `AdminUserId` has no FK constraint until the Identity slice. |
| AngebotSections | Id | AngebotId → Angebote | — | **No `Subtotal` column** — it's a pure computed property in the Domain (`AngebotSection.Subtotal`, a `=>` expression with no backing field), never persisted. Recomputed from live child data on every access instead. |
| AngebotItems | Id | SectionId → AngebotSections, CatalogItemId → CatalogItems (nullable) | — | CatalogItemId is a **trace link only** — never joined live for display (BR-8), but still a real FK constraint for data integrity. **No `LineTotal` column** — same reasoning as `AngebotSection.Subtotal`, a pure computed property. |
| CatalogItems | Id | CreatedFromAngebotItemId → AngebotItems (nullable) | — | Grows either via Admin curation or Inspector "save as catalog item" (SRS FR-4.10). Never hard-deleted — PermissionMatrix.md §6's "Delete/retire" action sets `IsRetired = true` instead, preserving the `CatalogItemId` traceability link on any AngebotItem created from it (BR-8, BR-12) |
| Customers | Id | LeadId → Leads | LeadId | One Customer per Lead — created at Project-conversion time |
| Projects | Id | CustomerId → Customers, AngebotId → Angebote | AngebotId | AgreedTotal is a snapshot of Angebot.GrossTotal at conversion time (doesn't move if the Angebot were ever re-opened, which the workflow doesn't currently allow) |
| Invoices | Id | ProjectId → Projects | InvoiceNumber | Never deleted — Void is a status, not a row removal (BR-9) |
| InvoiceLines | Id | InvoiceId → Invoices | — | Optional finer breakdown; an Invoice can exist with just header-level Net/VAT/Gross amounts if lines aren't needed |
| Payments | Id | InvoiceId → Invoices, RecordedByAdminId → Users | — | Manual v1; future gateway integration adds columns here, not a schema redesign (SRS FR-8.5) |
| TokenLinks | Id | (polymorphic: EntityType + EntityId, no DB-level FK) | Token | Polymorphic reference is intentional — one table serves both Angebot and Invoice links (Architecture §7.2) |
| NumberSequences | Id | — | (SequenceType, Year) | Incremented inside the same transaction as the entity it numbers, to avoid collisions under concurrent writes (Architecture §8) |
| AuditLogs | Id | PerformedByUserId → Users (nullable) | — | Nullable user = system-triggered action (e.g. scheduled Overdue transition) |

---

## 3. Recommended Indexes

| Table | Index | Purpose |
|---|---|---|
| Leads | Status, AssignedInspectorId | Pipeline filtering (SRS FR-2.4) |
| Angebote | Status | Dashboard "needs my review" / "in progress" lists |
| Angebote | AngebotNumber (unique) | Lookup + legal uniqueness |
| Invoices | InvoiceNumber (unique) | Lookup + legal uniqueness (BR-9) |
| Invoices | Status, DueDate | Overdue-detection scheduled check (StateMachine.md §3) |
| TokenLinks | Token (unique) | Public token-link lookup is the hottest unauthenticated read path |
| AuditLogs | EntityType, EntityId | Fetching an entity's full history efficiently |

---

## 4. Cardinality Summary (plain English)

- One **User** (Inspector) is assigned to many **Leads**; one **User** (Admin) reviews many **Angebote**.
- One **Lead** has at most one **Inspection** (v1 assumption) and can have many **Angebote** over time (though only one active at a time, per BR/StateMachine rules).
- One **Angebot** has many **Sections**; one **Section** has many **Items**.
- One **CatalogItem** can pre-fill many **AngebotItems**, across many different Angebote — but each AngebotItem keeps its own copy (BR-8), so this is a "was created from" link, not a live join.
- One **Lead** becomes at most one **Customer**; one **Customer** can have many **Projects** (e.g. a repeat customer).
- One **Angebot** converts to exactly one **Project**.
- One **Project** has many **Invoices**; one **Invoice** has many **Payments** (though v1 typically expects exactly one payment per invoice) and optionally many **InvoiceLines**.
- **TokenLink** relates polymorphically to either one **Angebot** or one **Invoice** at a time.
