# Entity Relationship Diagram (ERD)

**Project:** RenoTrack — Renovation Company Website & Project-Tracking Dashboard
**Companion documents:** SRS.md, Architecture.md, BusinessRules.md

> Note: this expands the simplified ERD shown in SRS.md §4.2 into full attribute-level detail — this file is the source of truth for the database schema.

---

## 1. Full Diagram (attributes, keys, cardinalities)

```mermaid
erDiagram
    USER {
        int Id PK "AspNetUsers — ASP.NET Core Identity (Architecture.md §7.1), not a hand-rolled table"
        string Name "App-specific addition beyond IdentityUser's own columns"
        string UserName UK "IdentityUser base column"
        string Email "IdentityUser base column — PasswordHash, SecurityStamp, LockoutEnd, AccessFailedCount, etc. also inherited, omitted here for brevity"
        bool IsActive "App-specific addition — deactivation (PermissionMatrix.md, resolves SRS OQ-1), never deletion"
        datetime CreatedAt "App-specific addition"
    }

    ROLE {
        int Id PK "AspNetRoles — plain IdentityRole&lt;int&gt;, no custom subclass"
        string Name "Admin | Inspector — the only two roles (CLAUDE.md §20)"
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
        string Address "nullable"
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

    USER }o--o{ ROLE : "via AspNetUserRoles"
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

**`USER`/`ROLE` corrected (Phase 3, Slice 15, `ARCHITECTURE_DECISIONS.md` D53):** the single-table sketch above with a plain `Role` string column was a simplification from before Phase 3's Identity work started. Architecture.md §7.1 commits to real ASP.NET Core Identity, which is structurally a multi-table schema (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, plus the framework's own `AspNetUserClaims`/`AspNetUserLogins`/`AspNetUserTokens`/`AspNetRoleClaims` — omitted from the diagram above since nothing in this app uses them yet, but present in the actual database as framework-standard tables). `ApplicationUser`'s only additions beyond `IdentityUser<int>`'s own base columns are `Name`, `IsActive`, `CreatedAt`; `Role` uses the framework's `IdentityRole<int>` directly, with no custom subclass.

---

## 2. Physical Schema Notes (per table)

| Table | Primary Key | Notable Foreign Keys | Unique Constraints | Notes |
|---|---|---|---|---|
| AspNetUsers | Id (int, identity) | — | UserName (filtered), NormalizedEmail (non-unique index) | ASP.NET Core Identity's own table (Phase 3, Slice 15) — `Name`/`IsActive`/`CreatedAt` are this project's additions; everything else (`PasswordHash`, `SecurityStamp`, `LockoutEnd`, etc.) is `IdentityUser<int>`'s own base shape. Password hashing delegated entirely to Identity's default `IPasswordHasher` — no custom hashing code. |
| AspNetRoles | Id (int, identity) | — | NormalizedName | Plain `IdentityRole<int>`, no custom subclass — seeded with exactly `Admin`/`Inspector` (`IdentityRoleSeeder`, idempotent and safe under concurrent application startup). |
| Leads | Id | AssignedInspectorId → AspNetUsers (nullable) | — | Status stored as string enum for readability in raw SQL during support/debugging. `AssignedInspectorId` FK added in the Identity slice (Phase 3, Slice 15, D44 resolved). |
| ContactMessages | Id | LeadId → Leads | — | Raw form submissions kept even if the Lead is later edited |
| Inspections | Id | LeadId → Leads, InspectorId → AspNetUsers (required) | — | One Lead usually has one Inspection (SRS notes this could be relaxed later). `InspectorId` FK added in the Identity slice (D44 resolved). |
| InspectionPhotos | Id | InspectionId → Inspections | — | FileUrl points into the storage abstraction (Architecture §9), not a raw disk path exposed to clients |
| Angebote | Id | LeadId → Leads, InspectionId → Inspections (nullable), CreatedByInspectorId → AspNetUsers (required), ReviewedByAdminId → AspNetUsers (nullable) | AngebotNumber | NetTotal/GrossTotal are cached/denormalized for fast list-page rendering; recalculated from AngebotItems on every edit (BR-6, Architecture §6.1). **No `DecisionResult` column** — removed from the Domain entirely as a presentation-mapping concern, not a stored fact (`ARCHITECTURE_DECISIONS.md` D16); derivable from `Status` (`CustomerApproved`/`CustomerRejected`) wherever it's needed. `CreatedByInspectorId`/`ReviewedByAdminId` FKs added in the Identity slice (Phase 3, Slice 15, D44 resolved). |
| AngebotReviewComments | Id | AngebotId → Angebote, AdminUserId → AspNetUsers (required) | — | Append-only log of the review loop (SRS FR-5.4). `AdminUserId` FK added in the Identity slice (D44 resolved). |
| AngebotSections | Id | AngebotId → Angebote | — | **No `Subtotal` column** — it's a pure computed property in the Domain (`AngebotSection.Subtotal`, a `=>` expression with no backing field), never persisted. Recomputed from live child data on every access instead. |
| AngebotItems | Id | SectionId → AngebotSections, CatalogItemId → CatalogItems (nullable) | — | CatalogItemId is a **trace link only** — never joined live for display (BR-8), but still a real FK constraint for data integrity. **No `LineTotal` column** — same reasoning as `AngebotSection.Subtotal`, a pure computed property. |
| CatalogItems | Id | CreatedFromAngebotItemId → AngebotItems (nullable) | — | Grows either via Admin curation or Inspector "save as catalog item" (SRS FR-4.10). Never hard-deleted — PermissionMatrix.md §6's "Delete/retire" action sets `IsRetired = true` instead, preserving the `CatalogItemId` traceability link on any AngebotItem created from it (BR-8, BR-12) |
| Customers | Id | LeadId → Leads | LeadId | One Customer per Lead — created at Project-conversion time. **`Address` is nullable (corrected in Phase 7, Slice 1).** It was originally typed non-null, which no document justified and which `Lead.Address` — legitimately null for every website-sourced Lead, since the public contact form does not collect one — makes unsatisfiable: a required address here would block conversion of an otherwise valid `CustomerApproved` Angebot, inventing a business rule against BR-2. **Known limitation, recorded rather than designed around:** `LeadId UK` means a repeat customer arriving as a new Lead gets a second `Customer` row, so §4's "one Customer can have many Projects (e.g. a repeat customer)" is unreachable under this schema. Resolving it needs a customer-identity design (matching by email/phone/name) that no document specifies; Phase 7 deliberately does not invent one |
| Projects | Id | CustomerId → Customers, AngebotId → Angebote | AngebotId | AgreedTotal is a snapshot of Angebot.GrossTotal at conversion time (doesn't move if the Angebot were ever re-opened, which the workflow doesn't currently allow) |
| Invoices | Id | ProjectId → Projects | InvoiceNumber | Never deleted — Void is a status, not a row removal (BR-9). **Domain aggregate root built in Phase 8 Slice 1.** `NetAmount + VatAmount == GrossAmount` is enforced by `Invoice.Create` as a structural invariant, because the per-rate VAT split is derived by the Application layer from the originating Angebot's rate mix (FR-8.2) and rounded per BR-11 — rounded parts do not automatically re-sum to the figure they were split from. **There is deliberately no `SentAt` column**, unlike `Angebote`: this table defines none, and the asymmetry is the documents', not an oversight to correct in code |
| InvoiceLines | Id | InvoiceId → Invoices | — | Optional finer breakdown; an Invoice can exist with just header-level Net/VAT/Gross amounts if lines aren't needed. **Deliberately not built in Phase 8** (approved before implementation): nothing creates a line — Wireframe E2 collects one amount and a due date — and this row's own "optional" wording is what makes a line-less Invoice legal. The per-rate split is computed to derive the header totals and then discarded. Revisit when a concrete use case needs stored line detail; the most likely trigger is Phase 14's §14 UStG PDF template (BR-5), which is where BR-5's "description/quantity" requirement is enforced |
| Payments | Id | InvoiceId → Invoices, RecordedByAdminId → AspNetUsers | — | Manual v1; future gateway integration adds columns here, not a schema redesign (SRS FR-8.5). **Domain entity built in Phase 8 Slice 1** as a *child of the Invoice aggregate* (Architecture.md §6) — reachable only through `Invoice.MarkPaid`, never constructed directly, so `InvoiceId` is a shadow FK. **Phase 8 records full payment only:** `Amount` is always the Invoice's own `GrossAmount`, because FR-8.4, Sequence Diagram §9's `{ paidAt, method }` body and Wireframe E3 all offer no amount to supply. The one-Invoice-to-many-Payments cardinality below is kept because this document defines it and it is what makes partial payments addable later without a redesign — **it is not evidence that partial payments are supported**, and a Domain test pins the full-payment behaviour so the schema cannot be mistaken for the semantics |
| TokenLinks | Id | (polymorphic: EntityType + EntityId, no DB-level FK) | Token | Polymorphic reference is intentional — one table serves both Angebot and Invoice links (Architecture §7.2) |
| NumberSequences | Id | — | (SequenceType, Year) | Incremented via a single atomic `UPDATE ... OUTPUT` statement, independently committed (not inside the same transaction as the entity it numbers — not achievable given `CreateAngebotCommandHandler`'s call order). Row-level lock scoped to that one statement avoids collisions under concurrent writes (Architecture §8, `ARCHITECTURE_DECISIONS.md` D52) |
| AuditLogs | Id | — (PerformedByUserId is a plain nullable int, deliberately not a real FK — Architecture.md §11: "no cross-entity linkage") | — | Nullable = system-triggered action (e.g. scheduled Overdue transition) |
| NotificationDeliveries *(built in Phase 9 Slice 3, **migration #9**)* | Id | (polymorphic: EntityType + EntityId, no DB-level FK — same structural impossibility as `TokenLinks`) | — | **Infrastructure-owned operational record, not business data** (`ARCHITECTURE_DECISIONS.md` D69) — same category as `AuditLogs`, `NumberSequences` and `RefreshTokens`, and like `RefreshTokens` it is deliberately absent from §1's diagram, which models the business domain. Tracks email delivery state so a failed notification is visible to an Admin and retryable, because a committed business operation stays successful when its email fails. **Columns as built:** `Id`, `NotificationType` (enum → `nvarchar(100)`), `EntityType` (`nvarchar(100)`), `EntityId`, `Status` (enum → `nvarchar(50)`), `Recipient` (`nvarchar(1000)`, **nullable**), `CreatedAt`, `LastAttemptAt` (nullable), `AttemptCount`, `SentAt` (nullable), `FailureType` (`nvarchar(200)`, nullable), `FailureMessage` (`nvarchar(2000)`, nullable). `Recipient` holds the complete resolved recipient **set** joined by `", "` — sized for a set, not one address, because three of the six notifications go to the configured Admin list; `NULL` means delivery failed before a recipient could be resolved, and there is deliberately no sentinel. `FailureType` is the exception's type name; `FailureMessage` is application-authored sanitized category text, never third-party text. **Lifecycle as built (Slice 5): `Pending → Sending → Sent \| Failed`**, plus `Failed → Sending` and `Sending → Sending`, the latter two reachable only through a manual Admin retry. `Sending` is written solely by the atomic compare-and-set claim and was added in Slice 5 **with no migration**, exactly as the string-converted column allowed. **`Sending` is itself retryable** because nothing recovers it automatically — no lease, no timeout, no sweeper — so a crashed attempt is rescued by an Admin retrying again, never by a background process. **The row is written *after* the business commit, not inside it** — the resulting crash window is an accepted, documented trade-off, not an oversight. **Stores no rendered body, no credential, and no copy of business data**: all six notifications are reconstructible from persisted aggregates (Angebot/Invoice/Lead/Customer/TokenLink/AngebotReviewComment), so the row carries identity and delivery state only. **No retention policy** — rows remain until a real requirement exists (contrast `RefreshTokens`) |
| RefreshTokens | Id | UserId → AspNetUsers (Restrict) | TokenHash | Backs Architecture §7.1's "short-lived access token + refresh token" pattern. **Only a SHA-256 hash of the token is stored** — the plaintext is returned to the client once and never persisted, so a database read yields no usable credential. Rotated on every use: the presented row gets `RevokedAt` + `ReplacedByTokenHash` and a new row is inserted. Presenting an already-revoked token revokes every outstanding token for that user (stolen-token reuse detection). Unlike business tables, rows here are **not** kept forever — retention is until `ExpiresAt`, after which a row carries no information (an expired token is rejected on expiry grounds regardless of revocation state) and may be deleted. No cleanup job exists yet, deliberately: steady-state volume is roughly (users × 32 × 7) rows. See `ARCHITECTURE_DECISIONS.md` D60 |

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
| RefreshTokens | TokenHash (unique) | Every refresh is a point lookup by hash; unique because two rows sharing a hash would make that lookup ambiguous |
| RefreshTokens | UserId | Reuse detection revokes every outstanding token for one user — the only non-point-lookup query on this table |
| NotificationDeliveries *(Phase 9 Slice 3)* | Status | The Admin's "failed/pending notifications" read is a status filter, and is the only list query on this table (`PermissionMatrix.md` §9). Used by `GET /api/v1/notification-deliveries` as of Slice 4 |
| NotificationDeliveries *(Phase 9 Slice 3)* | EntityType, EntityId | Answers "what happened to the notifications for this Angebot/Invoice/Lead" — the same pair, for the same reason, as `AuditLogs`. **No API filter exposes it yet** (Slice 4 deliberately ships none); the index stands for whoever needs that question next |

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
