# Sequence Diagrams

**Project:** RenoTrack — Renovation Company Website & Project-Tracking Dashboard
**Companion documents:** SRS.md, Architecture.md, StateMachine.md

This document details, flow by flow, exactly what happens between the Client (Browser), the API, the Application layer (Command/Query handlers), the Domain, the Database, and external services (Email). Each diagram maps back to its SRS requirement IDs.

---

## 1. Lead Creation — Website Contact Form
**Covers:** FR-1.3, FR-9.2

```mermaid
sequenceDiagram
    actor V as Public Visitor
    participant WS as Website (Razor Pages)
    participant API as API: LeadsController
    participant APP as Application: CreateLeadCommandHandler
    participant VAL as CreateLeadValidator
    participant DOM as Domain: Lead Aggregate
    participant REPO as ILeadRepository
    participant DB as SQL Server
    participant AUD as IAuditService
    participant MAIL as IEmailSender
    actor AD as Admin

    V->>WS: Fill contact form (name, phone, email, message)
    WS->>WS: Client-side required-field check
    WS->>API: POST /api/v1/leads { name, phone, email, message, source: Website }
    API->>VAL: Validate request DTO
    alt Invalid input
        VAL-->>API: Validation errors
        API-->>WS: 400 Bad Request (ProblemDetails)
        WS-->>V: Show inline form errors
    else Valid input
        API->>APP: Send(CreateLeadCommand)
        APP->>DOM: Lead.Create(name, phone, email, message, source=Website, status=New)
        DOM-->>APP: Lead instance (unsaved)
        APP->>REPO: AddAsync(lead)
        REPO->>DB: INSERT INTO Leads (...)
        DB-->>REPO: New Lead.Id
        APP->>AUD: LogAsync(entityType=Lead, action=Created, by=System)
        AUD->>DB: INSERT INTO AuditLogs
        APP->>MAIL: SendAsync(template=NewLeadNotification, to=AdminEmail)
        MAIL-->>APP: Accepted (queued)
        APP-->>API: LeadDto { Id, Status=New }
        API-->>WS: 201 Created { leadId }
        WS-->>V: "Thanks — we'll contact you soon"
        MAIL--)AD: Email delivered: "New Lead from Website"
    end
```

---

## 2. Lead Creation — Manual (Phone / Email)
**Covers:** FR-2.1

```mermaid
sequenceDiagram
    actor AD as Admin
    participant DASH as Dashboard (Angular)
    participant API as API: LeadsController
    participant APP as Application: CreateLeadCommandHandler
    participant REPO as ILeadRepository
    participant DB as SQL Server
    participant AUD as IAuditService

    AD->>DASH: Open "New Lead" form, choose source = Phone/Email
    AD->>DASH: Fill name, phone, email, address, notes
    DASH->>API: POST /api/v1/leads (Authorization: Bearer <JWT>) { ..., source }
    API->>API: [Authorize(Roles="Admin")]
    API->>APP: Send(CreateLeadCommand)
    APP->>REPO: AddAsync(lead)
    REPO->>DB: INSERT INTO Leads (...)
    DB-->>REPO: Lead.Id
    APP->>AUD: LogAsync(LeadCreated, by=Admin)
    APP-->>API: LeadDto
    API-->>DASH: 201 Created
    DASH-->>AD: Lead appears at top of pipeline (status = New)
```

---

## 3. Scheduling & Completing an Inspection
**Covers:** FR-2.3, FR-3.1–FR-3.4

```mermaid
sequenceDiagram
    actor AD as Admin
    actor INS as Inspector
    participant DASH as Dashboard
    participant API as API: InspectionsController
    participant APP as Application
    participant REPO as IInspectionRepository / ILeadRepository
    participant DB as SQL Server
    participant FS as IFileStorage
    participant AUD as IAuditService

    Note over AD,DASH: Step A — Scheduling
    AD->>DASH: Open Lead, click "Schedule Inspection"
    AD->>DASH: Pick date/time + assign Inspector
    DASH->>API: POST /api/v1/leads/{leadId}/inspections { scheduledAt, inspectorId }
    API->>APP: Send(ScheduleInspectionCommand)
    APP->>REPO: Load Lead (must exist, status allows scheduling)
    APP->>REPO: AddAsync(Inspection { LeadId, ScheduledAt, InspectorId })
    REPO->>DB: INSERT INTO Inspections
    APP->>REPO: Lead.Status = InspectionScheduled
    REPO->>DB: UPDATE Leads SET Status
    APP->>AUD: LogAsync(InspectionScheduled)
    APP-->>API: InspectionDto
    API-->>DASH: 201 Created
    DASH-->>AD: Lead card now shows "Inspection Scheduled"

    Note over INS,DASH: Step B — On-site visit (later, possibly on mobile browser)
    INS->>DASH: Open assigned Inspection (mobile view)
    INS->>DASH: Take/select photos, add note ("re-tile bathroom, ~10m2")
    loop For each photo
        DASH->>API: POST /api/v1/inspections/{id}/photos (multipart/form-data)
        API->>APP: Send(UploadInspectionPhotoCommand)
        APP->>FS: SaveAsync(file, path=/inspections/{id}/{fileId}.jpg)
        FS-->>APP: FileUrl
        APP->>REPO: AddAsync(InspectionPhoto { InspectionId, FileUrl })
        REPO->>DB: INSERT INTO InspectionPhotos
        APP-->>API: PhotoDto
        API-->>DASH: 201 Created
    end
    INS->>DASH: Enter notes text
    DASH->>API: PATCH /api/v1/inspections/{id} { notes }
    API->>APP: Send(UpdateInspectionNotesCommand)
    APP->>REPO: Update Inspection.Notes
    REPO->>DB: UPDATE Inspections
    INS->>DASH: Click "Mark Inspection Complete"
    DASH->>API: POST /api/v1/inspections/{id}/complete
    API->>APP: Send(CompleteInspectionCommand)
    APP->>REPO: Inspection.CompletedAt = now
    APP->>REPO: Lead.Status = InspectionDone
    REPO->>DB: UPDATE Inspections, UPDATE Leads
    APP->>AUD: LogAsync(InspectionCompleted)
    APP-->>API: OK
    API-->>DASH: 200 OK
    DASH-->>INS: "Inspection complete — you can now draft the Angebot"
```

---

## 4. Building the Angebot (Draft, Sections, Items, Live Totals)
**Covers:** FR-4.1–FR-4.7

```mermaid
sequenceDiagram
    actor INS as Inspector
    participant DASH as Dashboard
    participant API as API: AngeboteController
    participant APP as Application
    participant NUM as INumberGeneratorService
    participant DOM as Domain: Angebot Aggregate
    participant REPO as IAngebotRepository
    participant DB as SQL Server

    Note over INS,DASH: Create the draft
    INS->>DASH: Click "Create Angebot" on Lead (status = InspectionDone)
    DASH->>API: POST /api/v1/leads/{leadId}/angebote { inspectionId? }
    API->>APP: Send(CreateAngebotCommand)
    APP->>REPO: Guard: Lead.Status == InspectionDone
    APP->>NUM: NextAngebotNumber(year)
    NUM->>DB: UPDATE NumberSequences (atomic increment)
    NUM-->>APP: "ANG-2026-00042"
    APP->>DOM: Angebot.CreateDraft(leadId, inspectionId, number)
    APP->>REPO: AddAsync(angebot)
    REPO->>DB: INSERT INTO Angebote
    APP-->>API: AngebotDto { Id, Status=Draft }
    API-->>DASH: 201 Created
    DASH-->>INS: Empty Angebot editor opens

    Note over INS,DASH: Add a Section
    INS->>DASH: Click "Add Section" → "Pos. 3 Vorarbeiten Kellerböden"
    DASH->>API: POST /api/v1/angebote/{id}/sections { title, sortOrder }
    API->>APP: Send(AddAngebotSectionCommand)
    APP->>REPO: GetByIdWithDetailsAsync(angebotId)
    APP->>DOM: angebot.AddSection(title, sortOrder)
    REPO->>DB: INSERT INTO AngebotSections
    APP-->>API: SectionDto
    API-->>DASH: 201 Created

    Note over INS,DASH: Add a Line Item (repeats per item)
    INS->>DASH: Fill item form: description, qty=13.77, unit=m2, unitPrice=18.56, vatRate=19
    DASH->>API: POST /api/v1/angebote/{id}/sections/{sectionId}/items { ... }
    API->>APP: Send(AddAngebotItemCommand)
    APP->>DOM: section.AddItem(description, qty, unit, unitPrice, vatRate)
    DOM->>DOM: item.LineTotal = qty * unitPrice
    DOM->>DOM: section.Subtotal = sum(items.LineTotal)
    DOM->>DOM: angebot.RecalculateTotals()
    Note right of DOM: NetTotal, VAT-by-rate breakdown,<br/>GrossTotal all recomputed here (Architecture §6.1)
    APP->>REPO: SaveChangesAsync (via IUnitOfWork)
    REPO->>DB: INSERT INTO AngebotItems; UPDATE AngebotSections; UPDATE Angebote (cached totals)
    APP-->>API: ItemDto + updated AngebotSummaryDto
    API-->>DASH: 201 Created { item, summary }
    DASH-->>INS: Row appears; section subtotal and grand total update live

    Note over INS,DASH: Add a Line Item FROM the Catalog (faster path — SRS FR-4.8/4.9)
    INS->>DASH: Open Catalog picker, search/select "Bodenbelag trockengepresste Fliesen/Platten…"
    DASH->>API: GET /api/v1/catalog-items?search=...
    API->>APP: Send(SearchCatalogItemsQuery)
    APP-->>API: List of matching CatalogItem
    API-->>DASH: 200 OK
    DASH-->>INS: Form pre-filled with description, specification, unit; Inspector only enters qty + confirms price
    DASH->>API: POST /api/v1/angebote/{id}/sections/{sectionId}/items { catalogItemId, qty, unitPrice, vatRate }
    API->>APP: Send(AddAngebotItemCommand)
    Note right of APP: Same recalculation logic as the custom-item path above
    APP->>DOM: section.AddItem(..., catalogItemId)
    REPO->>DB: INSERT INTO AngebotItems (CatalogItemId set, own copy of description/spec/price — BR-8)
    APP-->>API: ItemDto + updated summary
    API-->>DASH: 201 Created

    Note over INS,DASH: Save a custom item back to the Catalog (grows the Catalog organically — FR-4.10)
    INS->>DASH: After typing a custom item, click "Save as Catalog item"
    DASH->>API: POST /api/v1/angebot-items/{itemId}/save-as-catalog-item
    API->>APP: Send(SaveAngebotItemAsCatalogItemCommand)
    APP->>REPO: AddAsync(CatalogItem { Title, DefaultSpecification, DefaultUnit, SuggestedUnitPrice, CreatedFromAngebotItemId })
    REPO->>DB: INSERT INTO CatalogItems
    APP-->>API: CatalogItemDto
    API-->>DASH: 201 Created
    DASH-->>INS: "Saved — available for future Angebote"

    Note over INS,DASH: Save as Draft (at any point)
    INS->>DASH: Navigate away / click "Save Draft"
    DASH->>API: (No extra call needed — every add/edit above is already persisted)
    DASH-->>INS: "Draft saved automatically"
```

---

## 5. Internal Review Loop (Submit → Review → Approve / Changes Requested)
**Covers:** FR-5.1–FR-5.4

```mermaid
sequenceDiagram
    actor INS as Inspector
    actor AD as Admin
    participant DASH as Dashboard
    participant API as API: AngeboteController
    participant APP as Application
    participant REPO as IAngebotRepository
    participant DB as SQL Server
    participant AUD as IAuditService
    participant MAIL as IEmailSender

    INS->>DASH: Click "Submit for Review"
    DASH->>API: POST /api/v1/angebote/{id}/submit-for-review
    API->>APP: Send(SubmitAngebotForReviewCommand)
    APP->>REPO: Guard: Angebot.Status == Draft, has >= 1 section/item
    APP->>REPO: Angebot.Status = InReview
    REPO->>DB: UPDATE Angebote
    APP->>AUD: LogAsync(SubmittedForReview, by=Inspector)
    APP->>MAIL: Notify Admin "Angebot ANG-2026-00042 ready for review"
    APP-->>API: OK
    API-->>DASH: 200 OK
    DASH-->>INS: Status badge → "In Review"

    AD->>DASH: Open Angebot in review
    AD->>DASH: Reads sections/items/totals

    alt Admin requests changes
        AD->>DASH: Click "Request Changes", enters comment
        DASH->>API: POST /api/v1/angebote/{id}/request-changes { comment }
        API->>APP: Send(RequestAngebotChangesCommand)
        APP->>REPO: Angebot.Status = ChangesRequested
        REPO->>DB: UPDATE Angebote
        APP->>REPO: AddAsync(AngebotReviewComment { comment, byAdmin })
        REPO->>DB: INSERT INTO AngebotReviewComments
        APP->>AUD: LogAsync(ChangesRequested)
        APP->>MAIL: Notify Inspector with comment
        APP-->>API: OK
        API-->>DASH: 200 OK
        DASH-->>AD: Status → "Changes Requested"
        Note over INS,DASH: Inspector edits (see Diagram 4), then submits again
        INS->>DASH: Edit items, click "Submit for Review" again
        Note over INS,AD: Loop repeats until Admin approves
    else Admin approves
        AD->>DASH: Click "Approve & Send to Customer"
        DASH->>API: POST /api/v1/angebote/{id}/approve
        API->>APP: Send(ApproveAngebotCommand)
        APP->>REPO: Angebot.Status = ApprovedInternally
        REPO->>DB: UPDATE Angebote
        APP->>AUD: LogAsync(ApprovedInternally, by=Admin)
        APP-->>API: OK
        API-->>DASH: 200 OK
        Note over AD,DASH: Continues directly into Diagram 6 (Send)
    end
```

---

## 6. Sending the Angebot & Customer Decision (Token Link)
**Covers:** FR-6.1–FR-6.6

```mermaid
sequenceDiagram
    actor AD as Admin
    participant DASH as Dashboard
    participant API as API: AngeboteController / PublicController
    participant APP as Application
    participant TOK as ITokenLinkService
    participant REPO as IAngebotRepository / ITokenLinkRepository
    participant DB as SQL Server
    participant MAIL as IEmailSender
    actor L as Lead (Customer)
    participant WS as Website (Token-Link Pages)

    Note over AD,DASH: Trigger send (auto-continues from Approve, or explicit click)
    AD->>DASH: Click "Send to Customer" (if not auto-triggered)
    DASH->>API: POST /api/v1/angebote/{id}/send
    API->>APP: Send(SendAngebotCommand)
    APP->>TOK: GenerateToken(entityType=Angebot, entityId, expiresIn=30 days)
    TOK->>TOK: token = CryptoRandom(32 bytes, URL-safe)
    TOK->>REPO: AddAsync(TokenLink { EntityType, EntityId, Token, ExpiresAt })
    REPO->>DB: INSERT INTO TokenLinks
    APP->>REPO: Angebot.Status = Sent, Angebot.SentAt = now
    REPO->>DB: UPDATE Angebote
    APP->>MAIL: SendAsync(template=AngebotReady, to=Lead.Email, link="https://.../angebot/{token}")
    MAIL-->>APP: Sent
    APP-->>API: OK
    API-->>DASH: 200 OK
    DASH-->>AD: Status → "Sent"
    MAIL--)L: Email: "Your quote is ready — view here"

    Note over L,WS: Customer opens the link (no login)
    L->>WS: GET /angebot/{token}
    WS->>API: GET /api/v1/public/angebote/{token}
    API->>APP: Send(GetAngebotByTokenQuery)
    APP->>REPO: Find TokenLink by token
    alt Token invalid / expired / already used
        APP-->>API: 404 / 410 Gone
        API-->>WS: Error response
        WS-->>L: "This link is no longer valid, please contact us"
    else Token valid
        APP->>REPO: Load Angebot (sections, items, totals) read-only
        APP-->>API: AngebotPublicViewDto
        API-->>WS: 200 OK
        WS-->>L: Renders read-only Angebot (sections, VAT breakdown, grand total)

        L->>WS: Click "Approve" or "Reject" (+ optional reason)
        WS->>API: POST /api/v1/public/angebote/{token}/decision { result, reason? }
        API->>APP: Send(RecordAngebotDecisionCommand)
        APP->>REPO: Guard: TokenLink.UsedAt == null
        APP->>REPO: TokenLink.UsedAt = now
        APP->>REPO: Angebot.Status = CustomerApproved | CustomerRejected
        APP->>REPO: Angebot.DecisionAt = now, DecisionResult = result
        REPO->>DB: UPDATE TokenLinks, UPDATE Angebote
        APP->>APP: LeadStatus update (Won-pending / Lost)
        APP->>MAIL: Notify Admin of decision
        APP-->>API: OK
        API-->>WS: 200 OK
        WS-->>L: "Thank you — we've recorded your decision"
        MAIL--)AD: Email: "Lead X approved/rejected Angebot ANG-2026-00042"
    end
```

---

## 7. Converting an Approved Angebot into a Project
**Covers:** FR-7.1

```mermaid
sequenceDiagram
    actor AD as Admin
    participant DASH as Dashboard
    participant API as API: ProjectsController
    participant APP as Application
    participant REPO as IProjectRepository / ICustomerRepository / IAngebotRepository
    participant DB as SQL Server
    participant AUD as IAuditService

    Note over AD,DASH: Only reachable when Angebot.Status == CustomerApproved
    AD->>DASH: Open approved Angebot, click "Convert to Project"
    DASH->>API: POST /api/v1/angebote/{id}/convert-to-project
    API->>APP: Send(ConvertAngebotToProjectCommand)
    APP->>REPO: Guard: Angebot.Status == CustomerApproved
    APP->>REPO: Find or create Customer from Lead's contact details
    REPO->>DB: INSERT INTO Customers (if new)
    APP->>REPO: AddAsync(Project { CustomerId, AngebotId, AgreedTotal = Angebot.GrossTotal, Status = Active })
    REPO->>DB: INSERT INTO Projects
    APP->>REPO: Lead.Status = Won
    REPO->>DB: UPDATE Leads
    APP->>AUD: LogAsync(ProjectCreated, by=Admin)
    APP-->>API: ProjectDto
    API-->>DASH: 201 Created
    DASH-->>AD: Redirects to new Project detail page
```

---

## 8. Creating & Splitting Invoices
**Covers:** FR-8.1, FR-8.2

```mermaid
sequenceDiagram
    actor AD as Admin
    participant DASH as Dashboard
    participant API as API: InvoicesController
    participant APP as Application
    participant NUM as INumberGeneratorService
    participant REPO as IInvoiceRepository / IProjectRepository
    participant DB as SQL Server

    AD->>DASH: Open Project, click "Add Invoice"
    DASH->>API: GET /api/v1/projects/{id}/invoice-balance
    API->>APP: Send(GetRemainingInvoiceBalanceQuery)
    APP->>REPO: remaining = Project.AgreedTotal - SUM(existing Invoices.GrossAmount)
    APP-->>API: { agreedTotal, alreadyInvoiced, remaining }
    API-->>DASH: 200 OK
    DASH-->>AD: Shows "€25,673.36 agreed / €0 invoiced / €25,673.36 remaining"

    AD->>DASH: Enter first invoice: amount = €8,000 (e.g. project start)
    DASH->>API: POST /api/v1/projects/{id}/invoices { grossAmount, dueDate }
    API->>APP: Send(CreateInvoiceCommand)
    APP->>NUM: NextInvoiceNumber(year)
    NUM->>DB: UPDATE NumberSequences (atomic)
    NUM-->>APP: "RE-2026-00017"
    APP->>APP: Derive Net/VAT split proportionally from the Angebot's VAT-rate mix (Architecture §6.1)
    APP->>REPO: AddAsync(Invoice { ProjectId, Number, NetAmount, VatAmount, GrossAmount, Status=Draft })
    REPO->>DB: INSERT INTO Invoices
    APP-->>API: InvoiceDto
    API-->>DASH: 201 Created
    DASH-->>AD: Invoice appears under Project; remaining balance updates
    Note over AD,DASH: Repeats for each additional invoice (BR-3: warn if sum != AgreedTotal)
```

---

## 9. Sending an Invoice & Recording Payment
**Covers:** FR-8.3, FR-8.4

```mermaid
sequenceDiagram
    actor AD as Admin
    participant DASH as Dashboard
    participant API as API: InvoicesController / PublicController
    participant APP as Application
    participant TOK as ITokenLinkService
    participant PDF as IPdfGenerator
    participant MAIL as IEmailSender
    participant REPO as IInvoiceRepository
    participant DB as SQL Server
    actor C as Customer
    participant WS as Website (Token-Link Pages)

    AD->>DASH: Click "Send Invoice"
    DASH->>API: POST /api/v1/invoices/{id}/send
    API->>APP: Send(SendInvoiceCommand)
    APP->>PDF: GenerateAsync(invoiceData)
    PDF-->>APP: PDF bytes
    APP->>TOK: GenerateToken(entityType=Invoice, entityId)
    TOK->>DB: INSERT INTO TokenLinks
    APP->>REPO: Invoice.Status = Sent
    REPO->>DB: UPDATE Invoices
    APP->>MAIL: SendAsync(to=Customer.Email, attachment=PDF, link="https://.../invoice/{token}")
    APP-->>API: OK
    API-->>DASH: 200 OK
    MAIL--)C: Email with invoice PDF + payment instructions + view link

    Note over C,WS: Customer pays by bank transfer/cash, outside the system
    C->>WS: (Optional) Opens link to review invoice details
    WS->>API: GET /api/v1/public/invoices/{token}
    API-->>WS: Read-only invoice view
    WS-->>C: Shows amount, due date, bank details

    Note over AD,DASH: Once payment is confirmed manually (bank statement, etc.)
    AD->>DASH: Open Invoice, click "Mark as Paid"
    AD->>DASH: Enter payment date + method (Bank Transfer / Cash / Other)
    DASH->>API: POST /api/v1/invoices/{id}/mark-paid { paidAt, method }
    API->>APP: Send(RecordPaymentCommand)
    APP->>REPO: AddAsync(Payment { InvoiceId, Amount, Method, PaidAt })
    REPO->>DB: INSERT INTO Payments
    APP->>REPO: Invoice.Status = Paid
    REPO->>DB: UPDATE Invoices
    APP-->>API: OK
    API-->>DASH: 200 OK
    DASH-->>AD: Invoice shows "Paid" — Project balance updates
```

---

## 10. Project Completion
**Covers:** FR-7.3, FR-8.6

```mermaid
sequenceDiagram
    actor AD as Admin
    participant DASH as Dashboard
    participant API as API: ProjectsController
    participant APP as Application
    participant REPO as IProjectRepository / IInvoiceRepository
    participant DB as SQL Server

    AD->>DASH: Open Project, click "Mark as Completed"
    DASH->>API: POST /api/v1/projects/{id}/complete
    API->>APP: Send(CompleteProjectCommand)
    APP->>REPO: Load all Invoices for Project
    alt Any invoice not Paid
        APP-->>API: 409 Conflict "Unpaid invoices remain" (unless AD passes forceOverride + reason)
        API-->>DASH: Error / confirmation prompt
        DASH-->>AD: "2 invoices are still unpaid — override?"
        opt Admin confirms override
            AD->>DASH: Confirm with reason
            DASH->>API: POST /api/v1/projects/{id}/complete { forceOverride: true, reason }
            API->>APP: Send(CompleteProjectCommand, override)
            APP->>REPO: Project.Status = Completed
            REPO->>DB: UPDATE Projects
        end
    else All invoices Paid
        APP->>REPO: Project.Status = Completed
        REPO->>DB: UPDATE Projects
        APP-->>API: OK
        API-->>DASH: 200 OK
        DASH-->>AD: Project marked "Completed"
    end
```

---

## 11. Admin / Inspector Authentication
**Covers:** FR-10.1–FR-10.3

```mermaid
sequenceDiagram
    actor U as Admin / Inspector
    participant DASH as Dashboard
    participant API as API: AuthController
    participant ID as ASP.NET Core Identity
    participant JWT as JWT Issuer
    participant DB as SQL Server

    U->>DASH: Enter email + password
    DASH->>API: POST /api/v1/auth/login { email, password }
    API->>ID: CheckPasswordAsync(user, password)
    ID->>DB: SELECT User by email
    alt Invalid credentials
        ID-->>API: Failed (increments lockout counter)
        API-->>DASH: 401 Unauthorized
        DASH-->>U: "Invalid email or password"
    else Valid credentials
        ID-->>API: Success, user + role claim (Admin/Inspector)
        API->>JWT: Issue access token (short-lived) + refresh token
        JWT-->>API: Tokens
        API-->>DASH: 200 OK { accessToken, refreshToken }
        DASH->>DASH: Store tokens, redirect to role-appropriate dashboard view
    end
```

---

## 12. Token Validation Detail (Cross-cutting)
**Covers:** Architecture §7.2 (referenced by Diagrams 6 and 9)

```mermaid
sequenceDiagram
    participant API as API: PublicController
    participant APP as Application: ValidateTokenLinkHandler
    participant REPO as ITokenLinkRepository
    participant DB as SQL Server

    API->>APP: ValidateToken(token, expectedEntityType)
    APP->>REPO: FindByTokenAsync(token)
    REPO->>DB: SELECT * FROM TokenLinks WHERE Token = @token
    alt Not found
        APP-->>API: NotFound
    else Found
        APP->>APP: Check EntityType matches expected
        APP->>APP: Check ExpiresAt > now
        APP->>APP: Check UsedAt == null (for decision-type actions only)
        alt Any check fails
            APP-->>API: Gone / Forbidden (specific reason)
        else All checks pass
            APP-->>API: Valid — return EntityId to caller
        end
    end
```
