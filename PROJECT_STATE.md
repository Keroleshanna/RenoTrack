# PROJECT_STATE.md — Where RenoTrack Actually Stands

**Last updated:** 2026-08-09, immediately after the Phase 8 merge — **Phases 0–8 are complete and merged to `main`.** `origin/main` is at `0c12948` (PR #14, the Phase 8 merge); Phase 7 merged as PR #13 (`697292b`). Phase 6 merged as PR #12 (`5a26c42`), Phase 5 as PR #11 (`18243ec`), Phase 4 as PR #8 (`e1a4d9e`), Phase 3 as PR #6 (`85df430`, handoff docs in PR #7 `babfff9`), Phase 2 as PR #5 (`dc85de1`), and the Development bootstrap as PR #10 (`7ce9774`).
**Purpose:** A precise, current snapshot — not a summary of history (see `PHASE2_PROGRESS.md` and `ARCHITECTURE_DECISIONS.md` for that). If a fact here conflicts with something you infer from reading old chat history, **this file and the actual code are authoritative.**

---

## 1. Current Phase

**Phase 8 — API: Invoices, Splitting, Payment Tracking, Project Completion — ✅ complete and merged** (PR #14, merge commit `0c12948`, branch `feature/phase-8-invoices-payments-project-completion`, off `main` at `697292b`, tip `4218fcc`), per `PROJECT_ROADMAP.md`. **All seven slices are done, the completion gate is closed, and publication is done** — the `Invoice` aggregate and its `Payment` child, migration #8 `AddInvoicesAndPayments`, invoice creation with the per-rate VAT allocation and BR-3's remaining-balance read, sending an Invoice as a token link with its anonymous read-only view, the mark-paid and void transitions, and Project completion with its invoice guard plus FR-7.4's invoice information on the Project detail read. See `PHASE8_PROGRESS.md`, which records the thirteen design decisions approved before any code was written, including the approved VAT-allocation strategy, the `InvoiceLine` deferral, the full-payment-only semantics, and the deliberate absence of any overdue scheduler. **Slice 6 additionally settled a three-way contradiction over the completion guard and added two rules the documents never stated — `ARCHITECTURE_DECISIONS.md` D67 and `StateMachine.md` §4.4 are the record. Slice 7 confirmed the overdue capability needed nothing further (adding no production code) and ran the completion gate as a full cross-document audit; its findings and checklist are in `PHASE8_PROGRESS.md`.** The branch was publishable-complete and **has now been published and merged** — the merge commit added no content (`git diff 4218fcc..0c12948` is empty).

**The current deliverable is Phase 9 (Email Service Integration). Its design is APPROVED; implementation has NOT started.** No `feature/phase-9-*` branch exists and no Phase 9 code exists. **`PHASE9_PROGRESS.md` records the eight approved decisions (F1–F8) and the slice plan**, written before any code, in the same discipline as Phase 8's thirteen.

**SRS OQ-3 no longer blocks Phase 9.** It was split on 2026-08-09: **OQ-3a (transport) is resolved — SMTP via MailKit** behind the unchanged `IEmailSender` (`ARCHITECTURE_DECISIONS.md` **D68**); **OQ-3b (the real mailbox, sender identity and recipients) is deferred to deployment by decision**, since no company mailbox exists yet and no company-specific value is compiled in or defaulted. The other Phase 9 decisions are **D69** (notification persistence, Infrastructure-owned, written after the business commit, with an accepted crash window — no Outbox, no queue, no hosted service), **D70** (manual synchronous retry that re-sends only the notification), and **D71** (Admin recipients are a configured list, independent of the Identity Admin role). `PermissionMatrix.md` §9 adds the two new Admin-only operations.

- **Phase 7 (convert Angebot → Project) — ✅ complete and merged** (PR #13, merge commit `697292b`, branch `feature/phase-7-angebot-to-project`). Four slices: the `Customer` and `Project` Domain aggregates, their schema via migration #7 `AddCustomersAndProjects`, `ConvertAngebotToProjectCommand` with the explicit transaction boundary D48's amendment introduced, and the `ProjectsController` conversion + detail-read endpoints. `PHASE7_PROGRESS.md` is the per-slice record and carries the eight design decisions approved before any code was written — including the two that must not be silently reopened: **BR-2's guard belongs to `ConvertAngebotToProjectCommand`, not `Project.Create`**, and **Customer resolution is find-by-`LeadId`-then-create**.

- **Phase 6 (token links + public Angebot decision) — ✅ complete and merged** (PR #12, merge commit `5a26c42`, branch `feature/phase-6-token-links-public-angebot`). Four slices: the `TokenLink` aggregate and its schema (migration #6 `AddTokenLinks`), `POST /angebote/{id}/send`, the anonymous `GET /public/angebote/{token}`, and `POST /public/angebote/{token}/decision` with public-route rate limiting (D65). It also added a fourth Application exception type, `GoneException` → 410 — which shipped undocumented and was recorded in `CLAUDE.md` §17/§22 during Phase 7 Slice 1.

- **Phase 5 (Angebot builder + internal review) — ✅ complete and merged** (PR #11, merge commit `18243ec`, branch `feature/phase-5-angebot-builder-review`). Four slices: builder core endpoints and reads, the internal review loop plus comment history, the Catalog surface plus FR-4.10 save-as, and FR-4.11 duplication. See `PHASE5_PROGRESS.md` — written during Phase 6 to close a gap Phase 5 left, and labelled as such.
- Phase 0 (Solution bootstrap) — ✅ merged to `main`.
- Phase 1 (Domain core: Lead, Inspection, Angebot) — ✅ merged to `main`.
- Phase 1b (Domain: CatalogItem) — ✅ merged to `main`.
- **Phase 2 (Application layer) — ✅ merged to `main`** (PR #5, merge commit `dc85de1`; 15 vertical slices + documentation commits, branch `feature/phase-2-application-layer`). `CatalogItem`'s Application layer (Slices 11–14) was a justified in-scope insertion, needed by `AddAngebotItemCommand`. `SaveAngebotItemAsCatalogItemCommand` reviewed and confirmed out of scope (`ARCHITECTURE_DECISIONS.md` D39) — not a gap, a deliberate exclusion, to be revisited in Phase 3+.
- **Phase 3 (Infrastructure) — ✅ complete and merged to `main`** (PR #6, merge commit `85df430`, branch `feature/phase-3-infrastructure-efcore`). All 15 slices done (`RenoTrackDbContext` + entity configurations + `RenoTrack.Infrastructure.Tests`; `InitialCreate`/`AddAuditLog`/`AddNumberSequence`/`AddIdentity` migrations; `UnitOfWork`; `ILeadRepository`; `IInspectionRepository`; `IAngebotRepository`; `IAngebotReviewCommentRepository`; `ICatalogItemRepository`; `ICatalogItemQueries`; `IAuditService`; `INumberGeneratorService`; `IFileStorage` placeholder; `IEmailSender` placeholder; `AddInfrastructure()` + `Program.cs` wiring; Identity storage + role seeding). A pre-merge code review found three Should-Fix issues, all fixed (`c085058`). A real concurrency bug in `IdentityRoleSeeder`, found during final CI verification (not by the original review), was root-caused and fixed with a genuine design change — `IdentityRoleSeeder` became a dedicated DI service (D55) — rather than patched around. CI was split into a Linux job (build + non-Infrastructure tests) and a Windows job (Infrastructure tests against real LocalDB) to fix an environmental CI failure without weakening D40 (D56). See `PHASE3_PROGRESS.md` and §11 below for the full closeout record.

- **Phase 4 (API layer) — ✅ complete and merged to `main`** (PR #8, merge commit `e1a4d9e`, branch `feature/phase-4-api-auth-leads-inspections`, off `babfff9`). Scope confirmed against `PROJECT_ROADMAP.md`'s own Phase 4 entry (narrower than "the whole API layer"): API foundation, JWT authentication, Lead endpoints, Inspection endpoints, global exception handling, `LocalDiskFileStorage`, `AddApplication()` DI. Angebot/Catalog (Phase 5), token links (Phase 6), Projects (Phase 7), Invoices (Phase 8) are explicitly out of scope. **all 11 slices complete** (API foundation/conventions/docs — D57, D58; global exception-handling middleware — D59; `AddApplication()` DI extension; JWT authentication with persisted rotating refresh tokens — D60, plus a fifth migration `AddRefreshTokens`; public Lead creation — D61; Lead read endpoints with fail-secure role scoping and pagination; Inspection scheduling with assignee-eligibility enforcement — D62; Inspection photo upload with the real `LocalDiskFileStorage`, extension validation, and best-effort compensation after a failed commit; Inspection completion, the first endpoint needing no request record and no Application-layer change at all; Inspection notes via `PATCH`, a slice redefined during design review, which also reassigned Lead Won/Lost to Phase 6 and reconciled a real `Architecture.md`/`PermissionMatrix.md` contradiction; and the database bootstrap policy — D63 — closing the standing gap that **nothing had ever applied a migration in production**). Full slice list and log in `PHASE4_PROGRESS.md`.

**Phase 4's eleven agreed slices are all complete, reviewed, and merged.** See §9 and §12.

## 2. Current Branch State

- **`feature/phase-8-invoices-payments-project-completion` is the current branch**, branched off `origin/main` at `697292b`, tip `4218fcc`. **It has been pushed and merged via PR #14**; `origin/main` is at `0c12948` (the Phase 8 merge). Local `main` has been fast-forwarded to `0c12948`. The branch has deliberately **not** been deleted.
- Every earlier feature branch is merged and no longer active: Phase 7 (`feature/phase-7-angebot-to-project`), Phase 6 (`feature/phase-6-token-links-public-angebot`), Phase 5 (`feature/phase-5-angebot-builder-review`), the Development bootstrap (`feature/phase-5-development-bootstrap`), Phase 4 (`feature/phase-4-api-auth-leads-inspections`), Phase 3 (`feature/phase-3-infrastructure-efcore`, final commit `f5d3108`) and Phase 2 (`feature/phase-2-application-layer`).
- **Next step:** Phase 9 (Email Service Integration) — **not started**, and blocked on SRS OQ-3. **Phase 8 is complete and published (PR #14, `0c12948`).** Per `CLAUDE.md` §19, no direct commits to `main`, no force-push ever, and no push or PR without explicit permission.

## 3. Build & Test Status (verify this yourself before trusting it — it may be stale)

As of the last verified run, on `feature/phase-8-invoices-payments-project-completion` (tip `4218fcc`) at the end of Phase 8 Slice 6 — **a local pre-merge result that has NOT been re-verified since the PR #14 merge:**
- `dotnet build RenoTrack.slnx` → **0 Warnings, 0 Errors**.
- `dotnet test RenoTrack.slnx` → **1,324 tests passing, 0 failing.** (The Phase 7 merge baseline, re-verified independently at the start of Phase 8, was **979**; Slice 1 +74, Slice 2 +15, Slice 3 +80, Slice 4 +42, Slice 5 +54, Slice 6 +80.)
- **Environment caveat, verified on 2026-08-09:** Smart App Control on this machine intermittently blocks freshly-built unsigned assemblies with `FileLoadException 0x800711C7`, which xUnit reports as a catastrophic failure and **zero tests** — not a test failure. During Slice 6 it hit `RenoTrack.Application.dll` in Debug and `RenoTrack.Infrastructure.Tests.dll` / `RenoTrack.Api.dll` in Release, and cleared on retry. **The figures above come from a genuine full Release run with all four projects executing.** If a run reports zero tests for a project, retry before believing anything; never weaken Smart App Control.
- `dotnet ef migrations has-pending-model-changes` → no pending changes (**eight** migrations: `InitialCreate`, `AddAuditLog`, `AddNumberSequence`, `AddIdentity`, `AddRefreshTokens`, `AddTokenLinks`, `AddCustomersAndProjects`, `AddInvoicesAndPayments`).
  - `RenoTrack.Domain.Tests`: **332 tests.**
  - `RenoTrack.Application.Tests`: **419 tests.**
  - `RenoTrack.Infrastructure.Tests`: **230 tests** (real SQL Server LocalDB integration tests; `LoggingNoOpEmailSenderTests`/`DependencyInjectionTests`/`LocalDiskFileStorageTests`/`TokenLinkServiceTests` open no database connection — `LocalDiskFileStorageTests` uses real disk I/O in a temporary root).
  - `RenoTrack.Api.Tests`: **343 tests** (real `WebApplicationFactory<Program>` against real LocalDB, schema via `MigrateAsync`, D58 — including Phase 6's public token-link surface and rate-limiting coverage; `PublicRateLimitPartitionTests` is the one class here that runs against plain `HttpContext` objects rather than the host, deliberately, because `TestServer` supplies no `RemoteIpAddress`).
- **Run both commands again yourself at the start of any new session before writing code.** Do not trust this count without re-verifying; it reflects only what existed when this file was written.

## 4. Domain Layer — Complete Inventory

### 4.1 Aggregate Roots

| Aggregate | File | Children | Key methods |
|---|---|---|---|
| `Lead` | `src/RenoTrack.Domain/Entities/Lead.cs` | none (owns nothing; references by id) | `Create`, `MarkInspectionScheduled`, `MarkInspectionDone`, `MarkAngebotInProgress`, `MarkAngebotSent`, `MarkWon`, `MarkLost`, `AssignInspector` (no status guard — see `CLAUDE.md` §2 and `ARCHITECTURE_DECISIONS.md`) |
| `Inspection` | `src/RenoTrack.Domain/Entities/Inspection.cs` | `InspectionPhoto` | `Schedule`, `AddPhoto` (returns the created `InspectionPhoto`), `UpdateNotes`, `Complete` — all except `Schedule` guarded by BR-10 (immutable once `CompletedAt` is set) |
| `Angebot` | `src/RenoTrack.Domain/Entities/Angebot.cs` | `AngebotSection` → `AngebotItem` | `Create`, `AddSection`, `AddItemToSection(AngebotSection section, ...)` (takes the section object, not an id — see `CLAUDE.md` §2), `SubmitForReview`, `Approve`, `RequestChanges`, `Send`, `RecordCustomerApproval`, `RecordCustomerRejection` |
| `CatalogItem` | `src/RenoTrack.Domain/Entities/CatalogItem.cs` | none (independent) | `Create`, `Update`, `Retire` (BR-12 — sets `IsRetired`, no delete method exists) |
| `AngebotReviewComment` | `src/RenoTrack.Domain/Entities/AngebotReviewComment.cs` | none (independent) | `Create` only — append-only, no update/delete (ERD.md: "append-only log") |
| `TokenLink` | `src/RenoTrack.Domain/Entities/TokenLink.cs` | none (independent) | `Create`, `IsExpired(asOf)`, `MarkUsed` (BR-4). Polymorphic `EntityType`/`EntityId`, no DB-level FK. Guards live in `Create`, never the constructor — see `CLAUDE.md` §2 (Phase 6) |
| `Customer` | `src/RenoTrack.Domain/Entities/Customer.cs` | none (independent) | `Create` only — no mutator at all. `Address` is nullable, because `Lead.Address` is (Phase 7 Slice 1) |
| `Project` | `src/RenoTrack.Domain/Entities/Project.cs` | none (references Customer/Angebot by id) | `Create`, `PutOnHold`, `Resume`, `Complete` (StateMachine §4.3). `Complete` only from `Active`; its invoice precondition lives in `CompleteProjectCommandHandler` (Phase 8 Slice 6), not this aggregate, and **no override reaches the `Active` guard**. `AgreedTotal` has no mutator — the ERD snapshot guarantee is structural |
| `Invoice` | `src/RenoTrack.Domain/Entities/Invoice.cs` | `Payment` | `Create`, `Send`, `MarkOverdue(asOf)`, `MarkPaid`, `Void(reason)` (StateMachine §3.3). `Create` enforces `Net + VAT == Gross`; **no `SentAt`** (ERD defines no column); a void reason is required from every source state; `MarkPaid` takes no amount — Phase 8 is full-payment-only (Phase 8 Slice 1) |

### 4.2 Child Entities

| Entity | Parent | Notes |
|---|---|---|
| `InspectionPhoto` | `Inspection` | `internal` constructor; `Id, FileUrl, Caption, UploadedAt` |
| `AngebotSection` | `Angebot` | `internal` constructor + `internal AddItem`; `Subtotal` is a computed property (never stored) |
| `Payment` | `Invoice` | `internal` constructor; `Amount, Method, PaidAt, RecordedByAdminId`. Reachable only via `Invoice.MarkPaid`, which always passes the Invoice's own `GrossAmount` — the one-to-many ERD shape is forward-compatibility, **not** partial-payment support (Phase 8 Slice 1) |
| `AngebotItem` | `AngebotSection` | `internal` constructor; **no update/remove method** (deliberately left open, not a documented rule — see `CLAUDE.md` §2); `LineTotal` is a computed property; `CatalogItemId` is nullable, passive traceability data only (BR-8) |

### 4.3 Value Objects

| Type | File | Purpose |
|---|---|---|
| `Money` | `src/RenoTrack.Domain/ValueObjects/Money.cs` | Always exact to 2 decimal places (intrinsic invariant). `FromExact(decimal)` wraps an already-exact value; `RoundedPerBR11(decimal)` applies BR-11's rounding policy to a raw calculation result — the only two ways to construct one. `+`, `-` and `Sum(...)` never re-round (adding or subtracting already-rounded values can't create new precision). `-` was added in Phase 8 Slice 1 for BR-3's remaining balance and **may produce a negative** — an over-invoiced Project's negative balance *is* BR-3's warning, so clamping it would hide the mistake BR-3 exists to catch. No `*` operator (deliberately removed — see `ARCHITECTURE_DECISIONS.md`). |
| `ItemUnit` | `src/RenoTrack.Domain/ValueObjects/ItemUnit.cs` | Five standard units (`SquareMeter, Piece, LinearMeter, LumpSum, Meter`) matching SRS FR-4.3's literal codes (`m2, Stk, lfm, pauschal, m`), plus an open `Custom(label)` escape hatch. `Custom` rejects (case-insensitively) any label colliding with a reserved standard code, making stored ambiguity structurally impossible. `Code`/`FromCode` are the single round-trip surface Infrastructure will use in Phase 3 — Domain has zero EF Core awareness. |
| `UnitKind` | `src/RenoTrack.Domain/ValueObjects/UnitKind.cs` | The enum backing `ItemUnit`; not used directly outside it. |
| `VatBreakdownLine` | `src/RenoTrack.Domain/ValueObjects/VatBreakdownLine.cs` | `record(VatRate Rate, Money NetAmount, Money VatAmount)` — one row of `Angebot.VatBreakdown`, always computed, never stored (no ERD column exists for it). |

### 4.4 Enums

| Enum | File | Values |
|---|---|---|
| `LeadStatus` | `Enums/LeadStatus.cs` | `New, InspectionScheduled, InspectionDone, AngebotInProgress, AngebotSent, Won, Lost` |
| `AngebotStatus` | `Enums/AngebotStatus.cs` | `Draft, InReview, ChangesRequested, ApprovedInternally, Sent, CustomerApproved, CustomerRejected` |
| `LeadSource` | `Enums/LeadSource.cs` | `Website, Phone, Email` |
| `VatRate` | `Enums/VatRate.cs` | `Zero=0, Reduced=7, Sixteen=16, Standard=19` (+ `VatRateExtensions.ToPercentage()`) |
| `ProjectStatus` | `Enums/ProjectStatus.cs` | `Active, OnHold, Completed` — exactly StateMachine §4.1's three states (Phase 7 Slice 1) |
| `InvoiceStatus` | `Enums/InvoiceStatus.cs` | `Draft, Sent, Paid, Overdue, Void` — exactly StateMachine §3.1's five states (Phase 8 Slice 1) |
| `PaymentMethod` | `Enums/PaymentMethod.cs` | `BankTransfer, Cash, Other` — exactly SRS FR-8.4's three. **No gateway value**, deliberately (Phase 8 Slice 1) |

### 4.5 Domain Test Coverage (`RenoTrack.Domain.Tests` — see §3 for the current count)

One test class per entity/value-object, in `tests/RenoTrack.Domain.Tests/{Entities,ValueObjects}/`: `ItemUnitTests`, `MoneyTests`, `LeadTests`, `InspectionTests`, `InspectionPhotoTests`, `AngebotItemTests`, `AngebotSectionTests`, `AngebotTests`, `CatalogItemTests`, `AngebotReviewCommentTests`, `TokenLinkTests`, `CustomerTests`, `ProjectTests`, `InvoiceTests`, `PaymentTests`. `RenoTrack.Domain.csproj` has `<InternalsVisibleTo Include="RenoTrack.Domain.Tests" />` so tests can exercise `internal` constructors directly.

---

## 5. Application Layer — Complete Inventory

### 5.1 Common Infrastructure (`RenoTrack.Application.Common`)

| Item | Location | Notes |
|---|---|---|
| `ICommandHandler<TCommand, TResult>` | `Common/ICommandHandler.cs` | The write-side dispatch abstraction — no MediatR |
| `IQueryHandler<TQuery, TResult>` | `Common/IQueryHandler.cs` | The read-side counterpart — a deliberate second abstraction, not a reuse of `ICommandHandler` (`ARCHITECTURE_DECISIONS.md` D36). First (and so far only) consumer: `SearchCatalogItemsQuery`. |
| `AuditAction` (enum) | `Common/AuditAction.cs` | Current values: `LeadCreated, InspectionScheduled, InspectionDone, AngebotCreated, AngebotSubmittedForReview, AngebotApproved, AngebotChangesRequested, CatalogItemCreated, CatalogItemUpdated, CatalogItemRetired` |
| `OwnershipValidator` : `IOwnershipValidator` | `Common/OwnershipValidator.cs` | Implemented directly in Application (no external dependency); methods: `EnsureInspectionOwnership`, `EnsureLeadOwnership`, `EnsureAngebotOwnership` |
| `NotFoundException` | `Common/Exceptions/NotFoundException.cs` | → 404 (Phase 4) |
| `ForbiddenException` | `Common/Exceptions/ForbiddenException.cs` | → 403 (Phase 4) |
| `ConflictException` | `Common/Exceptions/ConflictException.cs` | → 409 (Phase 4) |

### 5.2 Repository & Service Interfaces (`Common/Interfaces/`)

| Interface | Methods (all as of this writing) |
|---|---|
| `ILeadRepository` | `AddAsync`, `GetByIdAsync` |
| `IInspectionRepository` | `AddAsync`, `GetByIdAsync` |
| `IAngebotRepository` | `AddAsync`, `GetByIdAsync`, `HasActiveAngebotForLeadAsync` |
| `IAngebotReviewCommentRepository` | `AddAsync` only |
| `IUnitOfWork` | `SaveChangesAsync`, `BeginTransactionAsync` (Phase 7 Slice 3 — D48 amendment) |
| `IUnitOfWorkTransaction` | `CommitAsync` + `IAsyncDisposable`; no `RollbackAsync` — disposal rolls back |
| `ICustomerRepository` | `AddAsync`, `FindByLeadIdAsync` (both Phase 7 Slice 3), `GetByIdAsync` (Phase 8 Slice 4 — `SendInvoiceCommand` needs the customer's email) |
| `IProjectRepository` | `AddAsync`, `ExistsForAngebotAsync` (Phase 7 Slice 3), `GetByIdAsync` (Phase 8 Slice 3) |
| `IInvoiceRepository` | `AddAsync` (Slice 3), `GetByIdAsync` (Slice 4 — eagerly loads `Payments`), `HasCompletionBlockingInvoicesForProjectAsync` (Slice 6 — the two-clause completion predicate, D67) |
| `ITokenLinkRepository` | `AddAsync`, `FindByTokenAsync` (Phase 6 Slice 1) |
| `ITokenLinkService` | `GenerateAsync` (Phase 6 Slice 1 — cryptographic token + expiry, Infrastructure-side) |
| `IAuditService` | `LogAsync` |
| `IEmailSender` | `SendNewWebsiteLeadNotificationAsync`, `SendAngebotSubmittedForReviewNotificationAsync`, `SendAngebotChangesRequestedNotificationAsync`, `SendAngebotReadyNotificationAsync` (Phase 6), `SendAngebotDecisionNotificationAsync` (Phase 6), `SendInvoiceReadyNotificationAsync` (Phase 8 Slice 4) — **six**, one per documented notification (FR-9.1/FR-9.2) |
| `IFileStorage` | `SaveAsync`, `DeleteAsync` (**`GetAsync` still not built** — nothing reads a stored file back; see §8) |
| `INumberGeneratorService` | `NextAngebotNumberAsync`, `NextInvoiceNumberAsync` (Phase 8 Slice 3 — same mechanism, own sequence row; unique and never reused, **not gapless**, D66) |
| `IOwnershipValidator` | `EnsureInspectionOwnership`, `EnsureLeadOwnership`, `EnsureAngebotOwnership` |
| `ICatalogItemRepository` | `AddAsync`, `GetByIdAsync` |
| `IUserQueries` | `IsActiveInspectorAsync` (Phase 4 Slice 7, D62 — one combined question, deliberately not splittable) |

**Query interfaces live in their feature folder, not `Common/Interfaces/`**, because their return types are feature DTOs and `Common` must not depend on a feature folder (D23). `IUserQueries` *does* live in `Common/Interfaces/` because it returns a `bool`, so the constraint does not apply. The full set: `ICatalogItemQueries` (`SearchAsync`), `ILeadQueries` (`GetPagedAsync`), `IAngebotQueries` (`GetByIdAsync`, `GetForLeadAsync`, `GetPublicByTokenAsync`), `IAngebotReviewCommentQueries` (`GetForAngebotAsync`), `IProjectQueries` (`GetByIdAsync`, `GetInvoiceBalanceAsync`).

**Verified in the Phase 8 completion sweep: every method on all 18 interfaces has at least one production consumer.** No speculative interface growth exists anywhere (CLAUDE.md §4).

### 5.3 Notification Models (`Common/Notifications/`)

Six models, one per `IEmailSender` method — never a feature DTO (CLAUDE.md §11):

- `NewWebsiteLeadNotification` (FR-9.2)
- `AngebotSubmittedForReviewNotification` (FR-9.2)
- `AngebotChangesRequestedNotification` (Sequence Diagram §5)
- `AngebotReadyNotification` (Phase 6 — FR-9.1, the customer's token link)
- `AngebotDecisionNotification` (Phase 6 — FR-9.2's third trigger)
- `InvoiceReadyNotification` (Phase 8 Slice 4 — FR-9.1's Invoice half; carries **no** bank details, G-5)

### 5.4 Commands & Queries Implemented — complete inventory as of Phase 8

*Reconciled in the Phase 8 completion sweep. This section previously listed Phase 2's fifteen slices
only and carried nothing from Phases 5–8.* **26 commands + 10 queries = 36 handlers**, each with a
Command/Query + Validator (where applicable) + Handler + tests. **Every one is reachable from
exactly one endpoint** — the 36 non-auth actions across 8 controllers map 1:1, verified by audit, so
the unreachable-handler defect Phase 4 Slice 10 had to close has not recurred. Authentication's two
endpoints deliberately have no command (D60).

**Phases 5–8 additions** (the Phase 2 list follows below, unchanged):

- **Angebote:** `RemoveAngebotSectionCommand`, `RemoveAngebotItemCommand`, `DuplicateAngebotCommand` (FR-4.11), `SendAngebotCommand` (Phase 6, FR-6.1), `RecordAngebotDecisionCommand` (Phase 6, FR-6.3/6.5 — the only path to Lead `Won`/`Lost`); queries `GetAngebotByIdQuery`, `GetLeadAngeboteQuery`, `GetAngebotReviewCommentsQuery`, `GetPublicAngebotByTokenQuery`
- **CatalogItems:** `SaveAngebotItemAsCatalogItemCommand` (FR-4.10 — D39's deferral, resolved in Phase 5)
- **Leads:** queries `GetLeadByIdQuery`, `GetLeadsQuery` (paged, server-forced Inspector scope, FR-2.4 filtering by status/inspector/date)
- **Projects:** `ConvertAngebotToProjectCommand` (Phase 7, BR-2), `CompleteProjectCommand` (Phase 8 Slice 6, FR-7.3/FR-8.6, D67); queries `GetProjectByIdQuery` (FR-7.4), `GetProjectInvoiceBalanceQuery` (BR-3)
- **Invoices:** `CreateInvoiceCommand` (FR-8.1/8.2), `SendInvoiceCommand` (FR-8.3), `RecordPaymentCommand` (FR-8.4, full payment only), `VoidInvoiceCommand` (BR-9); query `GetPublicInvoiceByTokenQuery`

#### Phase 2's original fifteen

**Leads** (`Application/Leads/`):
- `CreateLeadCommand` → `LeadDto`

**Inspections** (`Application/Inspections/`):
- `ScheduleInspectionCommand` → `InspectionDto`
- `CompleteInspectionCommand` → `InspectionDto`
- `UploadInspectionPhotoCommand` → `PhotoDto`
- `UpdateInspectionNotesCommand` → `InspectionDto`

**Angebote** (`Application/Angebote/`):
- `CreateAngebotCommand` → `AngebotDto`
- `AddAngebotSectionCommand` → `SectionDto`
- `SubmitAngebotForReviewCommand` → `AngebotDto`
- `ApproveAngebotCommand` → `AngebotDto`
- `RequestAngebotChangesCommand` → `AngebotDto`
- `AddAngebotItemCommand` → `AddAngebotItemResult` (`ItemDto` + `AngebotSummaryDto`) — both the custom and Catalog-sourced paths, from the start (BR-8, BR-14)

**CatalogItems** (`Application/CatalogItems/`):
- `CreateCatalogItemCommand` → `CatalogItemDto`
- `UpdateCatalogItemCommand` → `CatalogItemDto`
- `RetireCatalogItemCommand` → `CatalogItemDto`
- `SearchCatalogItemsQuery` → `IReadOnlyList<CatalogItemDto>` — **the first query in the codebase**, using `IQueryHandler<TQuery, TResult>` instead of `ICommandHandler`; always excludes retired items (BR-12); no parameters (see `ARCHITECTURE_DECISIONS.md` D36/D37)

~~**Not yet implemented (deliberately out of Phase 2's scope):** `SaveAngebotItemAsCatalogItemCommand`~~ — **built in Phase 5 Slice 3**, resolving D39's deferral. `UploadInspectionPhotoCommand`'s `IFileStorage.GetAsync` companion **is still not built** and remains a known gap (§8).

### 5.5 DTOs

| DTO | Location | Notes |
|---|---|---|
| `LeadDto` | `Leads/Dtos/LeadDto.cs` | |
| `InspectionDto` | `Inspections/Dtos/InspectionDto.cs` | |
| `PhotoDto` | `Inspections/Dtos/PhotoDto.cs` | |
| `AngebotDto` | `Angebote/Dtos/AngebotDto.cs` | Header-level only — **no nested `Sections`.** `NetTotal`/`GrossTotal` exposed as plain `decimal` |
| `SectionDto` | `Angebote/Dtos/SectionDto.cs` | No nested `Items` |
| `CatalogItemDto` | `CatalogItems/Dtos/CatalogItemDto.cs` | Header/scalar only; `DefaultUnit`/`SuggestedUnitPrice` unwrapped from `ItemUnit`/`Money` |
| `ItemDto` | `Angebote/Dtos/ItemDto.cs` | No `SectionId` — `AngebotItem` has no such property to map from |
| `AngebotSummaryDto` | `Angebote/Dtos/AngebotSummaryDto.cs` | Lighter than `AngebotDto` — Id/AngebotNumber/Status/NetTotal/GrossTotal only |
| `AngebotDetailDto` | `Angebote/Dtos/AngebotDetailDto.cs` | Phase 5 — the full tree (header, sections, items, VAT breakdown) for `GET /angebote/{id}` |
| `AngebotReviewCommentDto` | `Angebote/Dtos/AngebotReviewCommentDto.cs` | Phase 5 — the internal review history |
| `PublicAngebotDto` | `Angebote/Dtos/PublicAngebotDto.cs` | **Phase 6 — a separate hierarchy, never a projection of `AngebotDetailDto`.** Internal ids, staff ids, `CatalogItemId` and timestamps deliberately absent, pinned against raw JSON |
| `ProjectDto` | `Projects/Dtos/ProjectDto.cs` | Phase 7 — what conversion and completion return |
| `ProjectDetailDto` | `Projects/Dtos/ProjectDetailDto.cs` | Phase 7, extended Phase 8 Slice 6 — FR-7.4 in full: origin ids, `AlreadyInvoiced`, `Remaining`, and the `Invoices` list |
| `ProjectInvoiceDto` | `Projects/Dtos/ProjectInvoiceDto.cs` | Phase 8 Slice 6 — one row of Wireframe E1's invoice table (id, number, gross, status, due date) |
| `ProjectInvoiceBalanceDto` | `Projects/Dtos/ProjectInvoiceBalanceDto.cs` | Phase 8 Slice 3 — BR-3's three figures; `Remaining` may be negative and is never clamped |
| `InvoiceDto` | `Invoices/Dtos/InvoiceDto.cs` | Phase 8 Slice 3 — every ERD `Invoices` column, no `Payments` list |
| `PublicInvoiceDto` | `Invoices/Dtos/PublicInvoiceDto.cs` | **Phase 8 Slice 4/5 — a separate hierarchy.** Carries a dedicated `PublicInvoiceStatus` (`Open`/`Paid`/`Void`), never the internal enum; no internal ids, no issue date, no void reason, no payments, no bank details (G-5) |

**17 DTO files in total.** *Reconciled in the Phase 8 completion sweep — this table previously listed only the eight from Phase 2.*

**Not yet created:** a `CatalogItemDto` equivalent for `SaveAngebotItemAsCatalogItemCommand`'s response is unnecessary — it already reuses the existing `CatalogItemDto`.

### 5.6 Application Test Coverage (`RenoTrack.Application.Tests` — see §3 for the current count)

- `RenoTrack.Application.Tests.csproj` references `RenoTrack.Domain` explicitly (added when the first handler test needed to assert on Domain state).
- **20 fakes** in `tests/RenoTrack.Application.Tests/Fakes/` — one per interface, hand-written, never a mocking framework (CLAUDE.md §14): `FakeLeadRepository`, `FakeInspectionRepository`, `FakeAngebotRepository`, `FakeAngebotReviewCommentRepository`, `FakeCatalogItemRepository`, `FakeCustomerRepository`, `FakeProjectRepository`, `FakeInvoiceRepository`, `FakeTokenLinkRepository`, `FakeCatalogItemQueries`, `FakeAngebotQueries`, `FakeAngebotReviewCommentQueries`, `FakeProjectQueries`, `FakeUserQueries`, `FakeUnitOfWork`, `FakeAuditService`, `FakeEmailSender`, `FakeFileStorage`, `FakeNumberGeneratorService`, `FakeTokenLinkService`. *(Count reconciled in the Phase 8 completion sweep — this list previously named 11.)* `FakeLeadRepository`/`FakeInspectionRepository`/`FakeAngebotRepository`/`FakeCatalogItemRepository` each expose a `Seed(entity)` helper (reflection-based id assignment — test-only). `FakeCatalogItemQueries` implements the same BR-12 retired-item filtering a real implementation must perform, not a dumb passthrough. `AddAngebotItemCommandHandlerTests` additionally assigns `AngebotSection.Id` via the same reflection pattern, inline in the test class — the first test needing to distinguish between sibling child entities by id.
- One test class per handler, in `tests/RenoTrack.Application.Tests/{Leads,Inspections,Angebote,CatalogItems}/Commands/<CommandName>/`, plus `tests/RenoTrack.Application.Tests/CatalogItems/Queries/SearchCatalogItems/` (the first query test) and `tests/RenoTrack.Application.Tests/Common/OwnershipValidatorTests.cs`.

---

## 6. Infrastructure Layer — Complete Inventory (Phase 3, complete)

### 6.1 `RenoTrackDbContext` (`src/RenoTrack.Infrastructure/Persistence/RenoTrackDbContext.cs`)

One `DbSet<T>` per aggregate root only: `Leads`, `Inspections`, `Angebote`, `CatalogItems`, `AngebotReviewComments`, `TokenLinks`, `Customers`, `Projects`, `Invoices`. No `DbSet` for child entities (`AngebotSection`, `AngebotItem`, `InspectionPhoto`, `Payment`) — reachable only through their aggregate root's navigation. `OnModelCreating` calls `ApplyConfigurationsFromAssembly` — no configuration inlined there. `AuditLog`, `NumberSequence`, `RefreshToken` and the Identity tables were each added by the later slice that actually needed them (Phase 3 Slices 10, 11 and 15; Phase 4 Slice 4), never speculatively — see §6.4 and `CLAUDE.md` §21.

**`InitialCreate` migration** (`Persistence/Migrations/`) — the first real migration, generated via `RenoTrackDbContextFactory` (`IDesignTimeDbContextFactory<RenoTrackDbContext>`, design-time only — DI composition is still Slice 14). Creates exactly 8 tables (matching §6.1's `DbSet`s + child entities); confirmed via `InitialCreateMigrationTests` to apply cleanly to a fresh LocalDB database and to have zero pending model changes (i.e., the migration and the current EF model are in sync). Not yet applied to any shared/persistent database.

### 6.2 Entity Configurations (`src/RenoTrack.Infrastructure/Persistence/Configurations/`)

| Configuration | Notes |
|---|---|
| `LeadConfiguration` | `Source`/`Status` stored as string enums (ERD.md's own stated reason: readability in raw SQL). Index on `(Status, AssignedInspectorId)` for pipeline filtering. `AssignedInspectorId` has no FK yet (Identity slice). |
| `InspectionConfiguration` | `Photos` navigation bound to its backing field (`PropertyAccessMode.Field`) — see D43. `InspectorId` has no FK yet. |
| `InspectionPhotoConfiguration` | `InspectionId` FK is a shadow property. |
| `AngebotConfiguration` | `AngebotNumber` unique index; `Status` index; `Status` stored as string. `NetTotal`/`GrossTotal` via `MoneyConverter`, `decimal(18,2)`. `VatBreakdown` ignored (no ERD column, always computed). `Sections` navigation bound to its backing field. `CreatedByInspectorId`/`ReviewedByAdminId` have no FK yet. |
| `AngebotSectionConfiguration` | `Subtotal` ignored — computed property, no column (D41). `Items` navigation bound to its backing field. |
| `AngebotItemConfiguration` | `LineTotal` ignored (D41). `Unit` via `ItemUnitConverter`; `UnitPrice` via `MoneyConverter`; `VatRate` uses EF's default enum-to-int mapping. `CatalogItemId` **has** a real FK to `CatalogItems` (`DeleteBehavior.Restrict`) — both tables exist today, unlike the Users-referencing columns. |
| `CatalogItemConfiguration` | `DefaultUnit`/`SuggestedUnitPrice` via the same converters. `CreatedFromAngebotItemId` **has** a real FK to `AngebotItems` (`DeleteBehavior.Restrict`). |
| `AngebotReviewCommentConfiguration` | `AngebotId` **has** a real FK to `Angebote` (`DeleteBehavior.Restrict`). `AdminUserId` has no FK yet. |
| `CustomerConfiguration` (Phase 7 Slice 2) | `LeadId` FK → `Leads` (`Restrict`) plus a **unique** index — ERD.md's "One Customer per Lead". `Address` **nullable**. String lengths match `LeadConfiguration`'s exactly, since every value is copied verbatim from a Lead. |
| `InvoiceConfiguration` (Phase 8 Slice 2) | `ProjectId` FK → `Projects` (`Restrict`), deliberately **not** unique (ERD §4: one Project, many Invoices — FR-8.1's splitting depends on it). `InvoiceNumber` **unique** index (BR-9), `nvarchar(30)` matching `AngebotNumber`. `Status` stored as string; `Net`/`Vat`/`GrossAmount` all via `MoneyConverter` at `decimal(18,2)`; `VoidReason` nullable `nvarchar(4000)`. Composite `(Status, DueDate)` index per ERD §3. `Payments` child collection bound to its backing field, `IsRequired()`, **`Cascade`** — aggregate composition, matching `Angebot`→`Sections` and `Inspection`→`Photos`. **No `CreatedAt` column** (ERD defines none; `IssueDate` is the timestamp) |
| `PaymentConfiguration` (Phase 8 Slice 2) | `InvoiceId` FK is a shadow property (configured from `InvoiceConfiguration`). `RecordedByAdminId` FK → `AspNetUsers` (`Restrict`). `Amount` via `MoneyConverter`, `decimal(18,2)`; `Method` stored as string, `nvarchar(50)`. No `DbSet` — child of the Invoice aggregate |
| `ProjectConfiguration` (Phase 7 Slice 2) | `CustomerId` FK → `Customers` and `AngebotId` FK → `Angebote`, both `Restrict`; **unique** index on `AngebotId` only ("one Angebot converts to exactly one Project"), deliberately **not** on `CustomerId` (ERD.md §4: one Customer, many Projects). `Status` stored as string; `AgreedTotal` via `MoneyConverter`, `decimal(18,2)`. No navigation property on either relationship. |

### 6.3 Value Converters (`src/RenoTrack.Infrastructure/Persistence/ValueConverters/`)

`MoneyConverter` (`Money` ↔ `decimal`, via `.Amount`/`Money.FromExact`), `ItemUnitConverter` (`ItemUnit` ↔ `string`, via `.Code`/`ItemUnit.FromCode` — the exact round-trip surface `ItemUnit`'s own Domain doc comment anticipated).

### 6.4 Repository & Service Interfaces — Implementation Status

- **`IUnitOfWork` → `UnitOfWork` — ✅ done (Slice 3).** One-line wrapper over `RenoTrackDbContext.SaveChangesAsync` — confirmed intentionally thin by explicit design review, `ARCHITECTURE_DECISIONS.md` D48. No `IDisposable` (doesn't own the injected `DbContext`).
- **`ILeadRepository` → `LeadRepository` — ✅ done (Slice 4).** `src/RenoTrack.Infrastructure/Persistence/Repositories/LeadRepository.cs` — the first concrete repository, establishing the `Persistence/Repositories/` folder. `AddAsync`/`GetByIdAsync` only, matching the interface exactly. `GetByIdAsync` uses `DbSet<Lead>.FindAsync` (no `Include` needed — Lead has no navigation properties); its tracked result is relied upon by every handler that loads-then-mutates a Lead, since no `UpdateAsync` exists anywhere in this project. `AddAsync` performs no validation (Domain already guards its own invariants) and never calls `SaveChangesAsync` — that stays exclusively `IUnitOfWork`'s job, verified by a dedicated test.
- **`IInspectionRepository` → `InspectionRepository` — ✅ done (Slice 5).** `src/RenoTrack.Infrastructure/Persistence/Repositories/InspectionRepository.cs` — the first repository with a child collection. `GetByIdAsync` eagerly `.Include(i => i.Photos)`s (CLAUDE.md §4's "full aggregate" rule, `FindAsync` doesn't support `Include` so `FirstOrDefaultAsync` is used instead). A photo added via `Inspection.AddPhoto(...)` on an aggregate loaded through this repository is persisted by `IUnitOfWork.SaveChangesAsync()` alone, verified by a dedicated test.
- **`IAngebotRepository` → `AngebotRepository` — ✅ done (Slice 6).** `src/RenoTrack.Infrastructure/Persistence/Repositories/AngebotRepository.cs` — the first repository for a two-level aggregate (`Sections` → `Items`). `GetByIdAsync` uses `.Include(a => a.Sections).ThenInclude(s => s.Items)` (`AsSplitQuery` deliberately not used — a single chain, not sibling collections, so no cartesian-product concern at this aggregate size). `HasActiveAngebotForLeadAsync` is a plain `AnyAsync` existence check over `LeadId`/`Status` only (StateMachine.md §2.4's non-terminal definition), no `Include`.
- **`IAngebotReviewCommentRepository` → `AngebotReviewCommentRepository` — ✅ done (Slice 7).** `src/RenoTrack.Infrastructure/Persistence/Repositories/AngebotReviewCommentRepository.cs` — `AddAsync` only, matching the interface exactly. No design review beyond confirming it's a strict subset of already-approved `AddAsync` patterns.
- **`ICatalogItemRepository` → `CatalogItemRepository` — ✅ done (Slice 8).** `src/RenoTrack.Infrastructure/Persistence/Repositories/CatalogItemRepository.cs` — same `AddAsync`/`GetByIdAsync` shape as `LeadRepository` (no children, no navigation). `GetByIdAsync` deliberately does not filter by `IsRetired` (BR-14/D38 — a retired item remains a valid direct reference); that filtering belongs to `ICatalogItemQueries.SearchAsync` (Slice 9), not this repository.
- **`ICatalogItemQueries` → `CatalogItemQueries` — ✅ done (Slice 9).** `src/RenoTrack.Infrastructure/Persistence/Queries/CatalogItemQueries.cs` — the first query implementation (new `Persistence/Queries/` folder, parallel to `Persistence/Repositories/`). Projects directly to `CatalogItemDto` inside `Select(...)` (not via `.ToDto()`, which would force full-entity materialization) — verified genuinely SQL-translatable by the integration tests actually running. `AsNoTracking()`, no `IUnitOfWork` dependency, always excludes `IsRetired` (BR-12/D37, the only place this filter is applied), no parameters (zero-parameter shape already settled in Phase 2).
- **`IAuditService` → `AuditService` — ✅ done (Slice 10).** `src/RenoTrack.Infrastructure/Persistence/AuditService.cs`, backed by `AuditLog` (`src/RenoTrack.Infrastructure/Persistence/Entities/AuditLog.cs`) — an Infrastructure-only persistence model, not a Domain entity (D49). Implements the **Best-Effort Audit strategy** (D50): commits its own write independently of `IUnitOfWork` (nothing else would ever persist it, since every handler calls `LogAsync` after its own `SaveChangesAsync`), catches and logs any failure as a warning, never rethrows — an audit-write fault can never invalidate an already-committed business operation.
- **`INumberGeneratorService` → `NumberGeneratorService` — ✅ done (Slice 11).** `src/RenoTrack.Infrastructure/Persistence/NumberGeneratorService.cs`, backed by `NumberSequence` (Infrastructure-only, D51). Implements an atomic single-statement increment (`UPDATE ... OUTPUT`, raw SQL — a deliberate, narrowly-scoped exception to the EF-Core-only convention, D52), decoupled from the Angebot's own `SaveChangesAsync` (not achievable given `CreateAngebotCommandHandler`'s call order — `Architecture.md`/`ERD.md` corrected accordingly). Proven collision-free under real concurrent load by a 50-parallel-caller integration test, resolving the project's highest-risk unverified assumption (D34).
- **`IFileStorage` → `LocalDiskFileStorage` — ✅ done, superseding Phase 3's placeholder (Phase 4 Slice 8).** `src/RenoTrack.Infrastructure/FileStorage/LocalDiskFileStorage.cs`, configured by `FileStorageOptions` (`FileStorage:RootPath`, validated eagerly at startup). Independently enforces root containment on every key, refuses to overwrite an existing file, and has an idempotent `DeleteAsync`. **Phase 3's `PlaceholderFileStorage` and its test were deleted** as superseded — `PHASE3_PROGRESS.md` Slice 12 remains an accurate historical record of why the placeholder existed, but the class no longer does.
- **`IEmailSender` → `LoggingNoOpEmailSender` — ✅ done (Slice 13).** `src/RenoTrack.Infrastructure/Email/LoggingNoOpEmailSender.cs` — never throws (unlike `PlaceholderFileStorage`; the interface's own doc comment explicitly sanctions a no-op/logging placeholder here so Phase 2's handlers run end-to-end without SMTP), but logs a `Warning` on every call so it's never silent. Real SMTP-backed implementation remains Phase 9's deliverable.
- **`AddInfrastructure()` + `Program.cs` wiring — ✅ done (Slice 14).** `src/RenoTrack.Infrastructure/DependencyInjection.cs` registers `RenoTrackDbContext` (Scoped, from `ConnectionStrings:RenoTrackDb`) and all 11 repository/query/service interfaces above, every one Scoped. Deliberately excludes `IOwnershipValidator` (Application-layer implementation, CLAUDE.md §9) and all Application-layer DI (validators, command handlers) — out of scope, belongs to a future `AddApplication()` extension. `Program.cs` calls `AddInfrastructure(builder.Configuration)`.
- **Identity storage + role seeding — ✅ done (Slice 15, the last Phase 3 slice).** `ApplicationUser : IdentityUser<int>` (`src/RenoTrack.Infrastructure/Identity/ApplicationUser.cs`, Infrastructure-only per D53) adds `Name`/`IsActive`/`CreatedAt`. `RenoTrackDbContext` now inherits `IdentityDbContext<ApplicationUser, IdentityRole<int>, int>`. `AddIdentityCore` (not `AddIdentity`, D54) registered inside the existing `AddInfrastructure()`. `IdentityRoleSeeder` seeds `Admin`/`Inspector` only, idempotently and safely under concurrent startup (D54's race mitigation, proven by a 10-concurrent-instance test). The five deferred user-referencing FKs from D44 are now real constraints (`Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, `AngebotReviewComment.AdminUserId`), all `Restrict`. No authentication/JWT wiring — storage only, per the standing Phase 3 scope.
- **Every Application interface now has exactly one Infrastructure implementation, and every planned Phase 3 slice is done.**

**Added after Phase 3** *(reconciled in the Phase 8 completion sweep — the list above stops at Phase 4)*:

- **`ITokenLinkRepository` → `TokenLinkRepository`, `ITokenLinkService` → `TokenLinkService` (Phase 6 Slice 1).** Cryptographically random token, configurable expiry. `TokenLinks` is the one table with no FK on its entity reference — the polymorphic `EntityType` + `EntityId` design is Architecture §7.2's explicit choice, pinned by a test asserting a dangling `EntityId` is accepted.
- **`ICustomerRepository` → `CustomerRepository`, `IProjectRepository` → `ProjectRepository` (Phase 7 Slice 3).** `ProjectRepository.ExistsForAngebotAsync` is the business question behind ERD's one-Angebot-one-Project rule; the unique index remains the concurrency backstop (D62's principle).
- **`IProjectQueries` → `ProjectQueries` (Phase 7 Slice 4, extended Phase 8 Slices 3 and 6).** `GetByIdAsync` joins three tables explicitly (no navigation properties exist to `Include`) and, since Slice 6, also projects the Project's Invoices and derives `AlreadyInvoiced`/`Remaining` from the fetched rows. `GetInvoiceBalanceAsync` uses `EF.Property<decimal>` and two statements because **a value-converted `Money` does not translate inside an EF Core aggregate or correlated subquery** — found by failing tests, not inspection.
- **`IInvoiceRepository` → `InvoiceRepository` (Phase 8 Slices 3–6).** `GetByIdAsync` eagerly includes `Payments`; `HasCompletionBlockingInvoicesForProjectAsync` answers D67's two-clause predicate with two indexed existence probes, both backed by `IX_Invoices_ProjectId`.
- **`IAngebotQueries` → `AngebotQueries`, `IAngebotReviewCommentQueries` → `AngebotReviewCommentQueries` (Phase 5).** DTO projections, `AsNoTracking`, no aggregate hydration (D36).
- **`INumberGeneratorService`** gained `NextInvoiceNumberAsync` (Phase 8 Slice 3) — the same `UPDATE … OUTPUT` mechanism as Angebot numbers, its own sequence row, proven collision-free by a 50-parallel-caller test. Unique and never reused; **not gapless** (D66).
- **Identity/auth infrastructure (Phase 4):** `ITokenService`, `RefreshToken` storage, `DatabaseInitializer` (D63), `DevelopmentBootstrap` (D64). `ITokenService` lives in `RenoTrack.Infrastructure.Identity`, **not** `Application.Common.Interfaces` — Application neither consumes nor could consume it (D60).

### 6.5 Infrastructure Test Coverage (`RenoTrack.Infrastructure.Tests` — see §3 for the current count)

> **Reconciled in the Phase 8 completion sweep.** The narrative below stops at Phase 4. Test classes
> added since, all in the shared `"Infrastructure Database"` collection against real LocalDB:
> `TokenLinkPersistenceTests`, `TokenLinkRepositoryTests`, `TokenLinkServiceTests` (Phase 6);
> `CustomerPersistenceTests`, `ProjectPersistenceTests`, `ProjectQueriesTests`,
> `ConversionTransactionTests` (Phase 7 — the last of these proves a real rollback, and its
> "disposal does not count as a rollback test" shape must be preserved);
> `InvoicePersistenceTests`, `InvoiceRepositoryTests`, `ProjectInvoiceBalanceQueriesTests`
> (Phase 8), plus `AngebotQueriesTests`, `AngebotReviewCommentQueriesTests`, `CatalogItemSearchTests`
> and `DevelopmentBootstrapTests`. `InitialCreateMigrationTests.EveryDefinedMigration_IsAppliedToAFreshDatabase`
> (Phase 7 Slice 2) picks up each new migration automatically, which is why migrations #7 and #8
> needed no per-migration test of their own.
>
> **`RenoTrack.Api.Tests` (Phase 4+, not covered anywhere else in this file):** 21 test classes —
> `ApiFoundationTests`, `ProblemDetailsExceptionHandlerTests`, `DependencyInjectionTests` (the
> reflection-driven registration check), `AuthenticationTests`, `CreateLeadEndpointTests`,
> `LeadReadEndpointsTests`, `ScheduleInspectionEndpointTests`, `UploadInspectionPhotoEndpointTests`,
> `CompleteInspectionEndpointTests`, `UpdateInspectionNotesEndpointTests`,
> `AngebotBuilderEndpointsTests`, `AngebotReviewEndpointsTests`, `DuplicateAngebotEndpointTests`,
> `CatalogItemEndpointsTests`, `SendAngebotEndpointTests`, `PublicAngebotViewEndpointTests`,
> `PublicAngebotDecisionEndpointTests`, `PublicRateLimitEndpointTests`,
> `PublicRateLimitPartitionTests`, `ProjectEndpointsTests`, `InvoiceEndpointsTests`.

Real SQL Server LocalDB integration tests, never the EF Core InMemory provider (`ARCHITECTURE_DECISIONS.md` D40). `RenoTrackDbContextFixture` (`IAsyncLifetime` + `ICollectionFixture<T>`) creates/drops one shared LocalDB database (`RenoTrackInfrastructureTests`) per test run; every test class in the shared `"Infrastructure Database"` collection also seeds a real `Lead` row (via a `SeedLeadAsync` helper) before referencing its id, rather than a hardcoded placeholder — needed once real FKs made a coincidental id-match insufficient. Test classes: `LeadPersistenceTests`, `InspectionPersistenceTests`, `AngebotPersistenceTests`, `CatalogItemPersistenceTests`, `AngebotReviewCommentPersistenceTests` (15 tests total, including the 3 FK-rejection tests added in Slice 2, `EnsureCreated`-based schema), `UnitOfWorkTests` (3 tests), `InitialCreateMigrationTests` (2 tests, its own throwaway database, exercises `Database.MigrateAsync()` and `HasPendingModelChanges()` directly), `LeadRepositoryTests` (4 tests, Slice 4), `InspectionRepositoryTests` (5 tests, Slice 5 — `GetByIdAsync` eagerly loads `Photos`; a photo added post-load persists via `SaveChangesAsync` alone), `AngebotRepositoryTests` (9 tests, Slice 6 — `GetByIdAsync`'s two-level `Include`/`ThenInclude`, a section+item added post-load persisting via `SaveChangesAsync` alone, and `HasActiveAngebotForLeadAsync`'s non-terminal-status semantics driven directly via EF's change tracker since `Angebot.Status`'s only reachable terminal states through Domain methods require a `Sent` precondition), `AngebotReviewCommentRepositoryTests` (3 tests, Slice 7 — `AddAsync`-only contract), `CatalogItemRepositoryTests` (5 tests, Slice 8 — same `AddAsync`/`GetByIdAsync` shape as `LeadRepositoryTests`, plus a BR-14/D38 confirmation that `GetByIdAsync` still returns a retired item), `CatalogItemQueriesTests` (3 tests, Slice 9 — proves the DTO projection is genuinely SQL-translatable and that `IsRetired` is excluded), `AuditServiceTests` (4 tests, Slice 10 — proves `LogAsync` commits independently of `IUnitOfWork`, and that a real underlying write failure is caught and swallowed per the Best-Effort Audit strategy, D50), `NumberGeneratorServiceTests` (4 tests, Slice 11 — including a 50-parallel-caller concurrency test proving no duplicate numbers under real concurrent load against LocalDB), plus `DatabaseInitializerTests` (12 tests, Phase 4 Slice 11 — `Migrate`/`Verify` behaviour, both history directions, the Production refusal), all under `tests/RenoTrack.Infrastructure.Tests/Persistence/`, `LocalDiskFileStorageTests` (Phase 4 Slice 8, under `tests/RenoTrack.Infrastructure.Tests/FileStorage/` — real disk I/O in a temporary root, no database; it replaced Phase 3's `PlaceholderFileStorageTests`, deleted along with the placeholder itself), `LoggingNoOpEmailSenderTests` (3 tests, Slice 13, under `tests/RenoTrack.Infrastructure.Tests/Email/` — no database involved, uses a capturing `ILogger` fake to verify the Warning log is actually emitted), `DependencyInjectionTests` (4 tests, Slice 14, at the project root — builds the real DI container with `ValidateOnBuild`/`ValidateScopes` and resolves every registered service; no database connection is ever actually opened, only a `DbContext` object constructed), plus (Slice 15, `tests/RenoTrack.Infrastructure.Tests/Identity/`) `IdentityRoleSeederTests` (3 tests, including a 10-concurrent-instance race proof) and `ApplicationUserTests` (1 test, password-hasher sanity check) — both against real LocalDB via a DI-built `UserManager`/`RoleManager`.

---

## 7. Documentation State

All eight original spec documents live in the repo root and have been actively maintained (not just written once in Phase 0).

**Phase 5–8 changes, reconciled in the Phase 8 completion sweep** (the table below covers Phases 1–3 and was never extended):

| Document | Changed in Phases 5–8 | What changed |
|---|---|---|
| `SRS.md` | **No** — still unmodified since Phase 0 | Open questions OQ-1…OQ-4 remain open |
| `Wireframes.md` | **No** — still unmodified since Phase 0 | E1's project title and A4's bank details/PDF remain unbacked by schema; recorded, not invented |
| `Architecture.md` | **Yes** | §5.2 grew a row per endpoint through Phases 5–8 and was **retitled from "Representative Endpoints" to "API Endpoint Inventory"** in the completion sweep, with the three missing rows (`review-comments`, `auth/login`, `auth/refresh`) added; §7.2 settled the post-decision viewing question; §8 corrected to state the numbering guarantee that exists (unique, never reused — **not** gapless, D66) and the BR-5→BR-9 mis-citation fixed; §6 corrected for the `InvoiceLine` deferral; §14 reframed as an indicative grouping, **not** a second phase-numbering source |
| `StateMachine.md` | **Yes** | §3.3's `Draft → Void` guard reconciled (G-10); §3.1/§3.4's BR-5→BR-9 mis-citations fixed; §3.4 corrected and **new §4.4** added for the completion guard's three-way reconciliation, the zero-invoice rule and the override's exact reach (D67) |
| `PermissionMatrix.md` | **Yes** | §1's "Change Lead status directly" corrected to `—`/`—`; §5 gained the financial-summary row (Slice 3) and its "View Project detail" row was clarified to cover the invoice list (Slice 6); §7's Angebot-viewing row corrected for BR-4 |
| `ERD.md` | **Yes** | `TokenLinks`, `Customers`, `Projects`, `Invoices`, `Payments`, `RefreshTokens` rows; the `InvoiceLines` deferral and its revisit trigger; the `Customers.LeadId UK` repeat-customer limitation |
| `Sequence Diagram.md` | **Yes** | §6/§7 corrected for the single path to Lead `Won`; §8 and §12 corrected in the completion sweep (`GetProjectInvoiceBalanceQuery`; `ValidateTokenLinkHandler` never existed); §9 annotated for the `IPdfGenerator` deferral; §10 annotated for the completion guard |
| `BusinessRules.md` | **Yes** | BR-3 and BR-4's "Enforced by" lines corrected in the completion sweep. **No new rule since BR-14** — Slice 6's two new rules are state-transition rules and live in `StateMachine.md` §4.4 + D67, per this document's own "How to add a new rule" guidance |
| `PROJECT_ROADMAP.md` | **Yes**, first time | Phase 8's scheduler promise reconciled with G-3; its two "Phase 11" PDF references corrected to **Phase 14**; the balance-query name corrected. **It is the canonical source for phase numbering** |

#### Original Phase 1–3 record (unchanged)

| Document | Modified during Phase 1/2? | What changed |
|---|---|---|
| `SRS.md` | No | Unmodified since Phase 0 |
| `Architecture.md` | **Yes, extensively** | §6.1/§6.2 (Domain design decisions), §7.3 (role vs. ownership — new), §9 (stable external resource identifiers — new), §11 (audit-target principle — new). Phase 3: §3's solution structure updated to add `RenoTrack.Infrastructure.Tests` |
| `ERD.md` | **Yes** | `CatalogItem.IsRetired` column added (BR-12). Phase 3: `Subtotal`/`LineTotal`/`DecisionResult` removed to match confirmed Domain state (D41); `NumberSequences`' transaction-boundary wording corrected (D52); `USER`/`ROLE` corrected from a simplified single-table sketch to the real `AspNetUsers`/`AspNetRoles`/`AspNetUserRoles` Identity schema, and all five deferred user-referencing FK notes updated from "deferred" to "resolved" (D44/D53) |
| `Sequence Diagram.md` | **Yes** | §4 corrected (added missing AuditLog step for Angebot creation; fixed stale `CreateDraft` → `Create` reference) |
| `StateMachine.md` | **Yes** | §1.3 `ScheduleInspection` row's side-effects updated for BR-13 |
| `BusinessRules.md` | **Yes, extensively** | BR-10, BR-11, BR-12, BR-13, BR-14 all added, each with a Changelog row |
| `PermissionMatrix.md` | **Yes** | §1 "Assign/reassign Inspector" row clarified for BR-13; §6 "Delete/retire" row clarified for BR-12 and cross-referenced to BR-14 |
| `Wireframes.md` | No | Unmodified since Phase 0 |
| `PROJECT_ROADMAP.md` | No (but see below) | Still reflects the original phase plan; **does not yet reflect** that AngebotReviewComment work happened inside Phase 2 rather than a dedicated earlier phase, or that Phase 2's Angebot-workflow ordering deferred `AddAngebotItemCommand`. Notably, its own Phase 2 command list is what the Slice 15 closeout review used to confirm `SaveAngebotItemAsCatalogItemCommand` was never in scope — this document's original scoping held up under scrutiny. |

**New permanent documentation (this handoff):** `CLAUDE.md`, `ARCHITECTURE_DECISIONS.md`, `PHASE2_PROGRESS.md`, `PHASE3_PROGRESS.md`, `NEXT_STEPS.md`, this file, and `HANDOFF_PROMPT.md`.

Current `BusinessRules.md` rule count: **BR-1 through BR-14** (BR-1–BR-9 from original SRS extraction; BR-10–BR-14 added during Phase 1/Phase 2).

---

## 8. Deferred / Known-Incomplete Work (do not treat these as bugs — they are intentional, documented deferrals)

> **`NEXT_STEPS.md` §1g and §5a are the authoritative, current deferred list.** The numbered items
> below are the Phase 2/3-era record, re-verified in the Phase 8 completion sweep: items 1, 2, 8, 9,
> 10 and 11 are **resolved**; item 3 (`SaveAngebotItemAsCatalogItemCommand`) was **built in Phase 5
> Slice 3**; item 4 is superseded (10 queries now exist); items 5, 6 and 7 stand, with item 6
> resolved for `Angebot.Send`/decisions in Phase 6. Read `NEXT_STEPS.md` first; this list is kept
> for its reasoning, not as a current status board.

1. **`AddAngebotItemCommand` — ✅ complete (Slice 15).** Both the Catalog-sourced and custom-item paths implemented from the start, per the standing decision. See `PHASE2_PROGRESS.md` Slice 15 for the full design-review record, including BR-14 and the `NEXT_STEPS.md` §2 wording correction.
2. **CatalogItem Application layer — ✅ complete.** `CreateCatalogItemCommand`, `UpdateCatalogItemCommand`, `RetireCatalogItemCommand`, `SearchCatalogItemsQuery` all done (Slices 11–14).
3. **`SaveAngebotItemAsCatalogItemCommand` (SRS FR-4.10) — deliberately deferred, confirmed out of Phase 2's scope.** Reviewed explicitly rather than assumed-in-scope: `PROJECT_ROADMAP.md`'s Phase 2 command list never included it, and building it now would force a new, single-purpose Application-layer lookup capability (resolving an `AngebotItem`'s owning `Angebot` from the item's id alone) with no other justification. See `ARCHITECTURE_DECISIONS.md` D39. Revisit when a phase that actually needs it arrives (most naturally Phase 3, once real EF ids exist).
4. **`SearchCatalogItemsQuery` is the only query in the codebase so far.** Every command still returns a DTO built from the same aggregate it just mutated. Other read-side needs (list views, a Lead pipeline query, etc.) have not been started — this is normal for where Phase 2 currently stands, not a gap to rush to fill.
5. **`IFileStorage.DeleteAsync` — ✅ built (Phase 4 Slice 8)**, because compensation after a failed commit needs it. **`GetAsync` is still not built, and this is now a real gap rather than a neutral deferral:** Architecture §9 says photos are "served back through an authenticated API endpoint", but no such endpoint exists and nothing can read a stored file. **The system can store photos it cannot serve.** Awaiting a documents-first decision, not a silent scope expansion.
6. **`Angebot.Send()`, `RecordCustomerApproval()`, `RecordCustomerRejection()`** exist in the Domain (built in Phase 1) but have **no Application-layer commands yet** — deliberately deferred to Phase 6 (Token-link mechanism) per `PROJECT_ROADMAP.md`, since they depend on `ITokenLinkService`, which doesn't exist yet.
7. **`AngebotItem` has no update/remove method** — an open question, not a bug (see `CLAUDE.md` §2). Revisit only if real evidence (a documented endpoint or explicit business decision) appears.
8. **Infrastructure project — ✅ complete (all 15 slices done).** `RenoTrackDbContext` + entity configurations + `InitialCreate` migration + `UnitOfWork` + all 6 repositories/queries + `IAuditService` + `INumberGeneratorService` + `IFileStorage`/`IEmailSender` placeholders + `AddInfrastructure()` DI wiring + Identity storage all exist and are tested against real LocalDB. Every Application interface listed in §5.2 (except the deliberately-Application-side `IOwnershipValidator`, CLAUDE.md §9) now has exactly one Infrastructure implementation. See §6.4 and `PHASE3_PROGRESS.md`.
9. **`INumberGeneratorService`'s atomic-uniqueness requirement (Architecture §8) is now verified** — the single highest-risk assumption carried since Phase 2 (D34), resolved and proven by a 50-parallel-caller concurrency integration test in Slice 11 (D52).
10. **`LocalDiskFileStorage` — ✅ built in Phase 4 Slice 8** (D42's deferral resolved). **Real `IEmailSender` remains deferred to Phase 9** (`CLAUDE.md` §11, gated on SRS OQ-3's email-provider choice); `LoggingNoOpEmailSender` still stands in, logging a `Warning` on every call so it is never silent.
11. **User-referencing FK constraints** (`Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, `AngebotReviewComment.AdminUserId`) — deferred until the Identity slice (`ARCHITECTURE_DECISIONS.md` D44), and now resolved: all five have real `Restrict` FK constraints as of Slice 15 (D53/D54).

---

## 9. Immediate Next Step

**Phase 9 — Email Service Integration. Design approved 2026-08-09; implementation is in progress on `feature/phase-9-email-integration`.** Slices 1–3 are complete (SMTP sender + six frozen German templates; the safe-failure boundary; `NotificationDeliveries` + migration #9), and Slice 4 (the Admin read endpoint, `GET /api/v1/notification-deliveries`) is implemented. Slices 5 (manual retry) and 6 (documentation reconciliation + completion gate) remain. Phase 8 is complete and merged. **OQ-3 no longer blocks it** — OQ-3a resolved as SMTP + MailKit (D68), OQ-3b deferred to deployment. The approved scope is the *complete* delivery workflow (transport, config, six German templates, safe failure handling, notification persistence + migration #9, Admin visibility, manual retry), not SMTP integration alone. Decisions and slice plan: **`PHASE9_PROGRESS.md`**; permanent record: **D68–D71**.

**Phase 8 (API: Invoices, splitting, payment tracking, project completion) is COMPLETE AND MERGED** (PR #14, merge commit `0c12948`) from `feature/phase-8-invoices-payments-project-completion` (off `main` at `697292b`, tip `4218fcc`). **All seven slices are done, the completion gate is closed, and publication is done.** Slice 6 added `POST /api/v1/projects/{id}/complete` and FR-7.4's invoice information on the Project detail read; it also settled a three-way contradiction over which Invoice statuses block completion (**`Draft`/`Sent`/`Overdue` block, `Paid`/`Void` do not**) and added two rules no document stated — **a Project with zero Invoices is blocked**, and **`forceOverride` with nothing to override is a 400 that writes no audit entry**. `ARCHITECTURE_DECISIONS.md` **D67** and `StateMachine.md` **§4.4** are the record; do not reopen either without new evidence. `PHASE8_PROGRESS.md` is the per-slice record and carries the thirteen design decisions approved before any code was written. Four of them a later slice must not silently reopen: **`InvoiceLine` is deferred entirely**; **Phase 8 is full-payment-only** (`Payment.Amount` is always the Invoice's gross, and the one-to-many ERD shape is not partial-payment support); **no overdue scheduler of any kind is to be invented** — the transition exists, automatic execution is a recorded gap awaiting a job-hosting decision; and **invoice numbering guarantees uniqueness and non-reuse but not gaplessness**.

**Phase 7 (API: Convert Angebot → Project) is complete and merged** (PR #13, merge commit `697292b`). See §1 above and `PHASE7_PROGRESS.md`, which carries the eight design decisions approved before any code was written — including the two that must not be silently reopened: **BR-2's guard belongs to `ConvertAngebotToProjectCommand`, not `Project.Create`** (`BusinessRules.md` must not be edited to move it), and **Customer resolution is find-by-`LeadId`-then-create**, with no matching by email/phone/name.

**Phase 6 is complete and merged** (PR #12, `5a26c42`). Four slices: the `TokenLink` aggregate with migration #6 `AddTokenLinks`; `POST /angebote/{id}/send`; the anonymous `GET /public/angebote/{token}`; and `POST /public/angebote/{token}/decision` with public-route rate limiting (D65). One documentation gap survived its completion gate and was closed in Phase 7 Slice 1: `GoneException` (→410) shipped in code and in the exception-handler switch while `CLAUDE.md` §17 still asserted that three Application exception types existed.

**Phase 5 is complete and merged** (PR #11, `18243ec`). Its four slices — builder core, the internal review loop, the Catalog surface plus FR-4.10 save-as, and FR-4.11 duplication — are recorded in `PHASE5_PROGRESS.md`, written during Phase 6 to close a gap Phase 5 left and labelled as a post-hoc reconstruction from the commit record. The Development bootstrap merged separately as PR #10 (`7ce9774`, D64); it provisions accounts in **Development only** and **does not resolve SRS OQ-1**.

Highlights of Phase 6 worth knowing without opening `PHASE6_PROGRESS.md`: **two real defects were found by tests rather than inspection** — a time-dependent constructor guard made every expired `TokenLink` unreadable, because EF Core materialises rows through the same private constructor (now a rule in `CLAUDE.md` §2); and diagnostic surfaces were writing live customer tokens into logs and ProblemDetails, where the *first* fix silently did nothing because ASP.NET's exception middleware clears the endpoint before any handler runs. **Architecture §12 is only half closed**: `/api/v1/public/*` is rate-limited, `POST /api/v1/leads` is not.

**Slice 11 closed the long-standing bootstrap gap (D63).** Until it landed, **nothing in `src/` had ever applied a migration** while `Program.cs` seeded Identity roles at startup, so a fresh production database failed with `Invalid object name 'AspNetRoles'` — reproduced directly, not inferred. Production now applies migrations as an explicit deployment step and startup only *verifies* (migration history in both directions, plus required roles), refusing to serve if the database is not ready. `Migrate` is a Development opt-in and is hard-refused in Production. **No user was provisioned in any environment** at that point, so a fresh database had schema and roles and nobody able to log in — the SRS OQ-1 gap. Phase 5's `DevelopmentBootstrap` (D64) later closed that for **Development only**; Production is unchanged and OQ-1 remains open.

**Slice 10 was redefined during its design review, and that outcome must not be quietly reversed.** It was planned as "Lead status update (Won/Lost)"; the plan did not survive contact with the repository, and the slice became `PATCH /api/v1/inspections/{id}` (Inspection notes) instead. **Lead `Won`/`Lost` is now formally Phase 6 work.** `Lead.MarkAngebotSent()` is called by nothing, so `AngebotSent` is unreachable and such an endpoint would be 409 for every Lead; and the transition is the customer's token-link decision, not a staff action (`StateMachine.md` §5, SRS FR-6.3/FR-6.5, Sequence Diagram §6). **Do not create Admin `MarkWon`/`MarkLost` commands or endpoints.** The same slice reconciled a real document contradiction: `Architecture.md` §5.2's obsolete `PATCH /api/v1/leads/{id}/status` row was removed and `PermissionMatrix.md` §1's self-contradictory status row corrected to `— | —`.

Everything Phase 4 resolved is recorded per-slice in `PHASE4_PROGRESS.md`, with the cross-cutting rules folded into `CLAUDE.md` §22 and decisions D57–D63 in `ARCHITECTURE_DECISIONS.md`. Highlights worth knowing without opening those files:

- A **fail-open authorization defect** was found and fixed in Slice 6 — "not Inspector" must never be read as "Admin". Unrestricted access is only ever reached by positively establishing the Admin role. The vulnerability was reproduced before being fixed.
- D61's own wording was **corrected in Slice 7**: only values describing *the caller* are server-derived; an Admin-selected `InspectorId` is legitimate request input.
- Slice 8's photo upload writes the file only after every Domain/ownership guard has passed, and compensates with a best-effort delete if the commit then fails — **compensation, not atomicity**.
- Slice 9's completion, by contrast, **is** genuinely atomic across its two aggregates: both repositories and `UnitOfWork` share the one request-scoped `DbContext`, so a single `SaveChangesAsync` writes both in EF Core's implicit transaction. The **audit row is deliberately outside** that guarantee (D50).
- Slice 9 also found two things empirically that were previously assumed: **`AuditService` shares the request's `DbContext`**, so its own `SaveChangesAsync` can flush unrelated pending changes (benign today, since every handler audits after committing); and **a status-code-only authorization test cannot tell a role-gate 403 from an ownership 403**, which let a deliberately-weakened role attribute go undetected until the test also asserted an empty response body.

- The closeout's second review pass then found a **fully reproducible refresh-token rotation race** — see §12. It is the single most consequential finding of the phase and post-dates every slice narrative in `PHASE4_PROGRESS.md`.

**Open items carried forward (all deliberate, none forgotten)** are enumerated in `NEXT_STEPS.md` §5a: rate limiting on the public Lead endpoint (required by Architecture §12, deferred to a hardening slice); production user provisioning (blocked on SRS OQ-1 — login works but is unusable outside tests); `GET /api/v1/inspections/{id}`; an authenticated photo-serving endpoint plus `IFileStorage.GetAsync` (**photos can be stored but not served**); Lead `Won`/`Lost` (Phase 6, and deliberately not an Admin action); the non-isolated audit write; and the `Roles.cs` folder/namespace mismatch. **`PATCH /api/v1/inspections/{id}` is no longer among them — Slice 10 built it**, so `UpdateInspectionNotesCommand` is no longer a registered-but-unreachable handler.

§10, §11 and §12 below remain the historical closeout records for Phases 2, 3 and 4 — they describe what was true at those merges and are deliberately not updated.

---

## 10. Phase 2 Closeout Review

Performed 2026-07-30, immediately before opening the PR.

**1. Every Phase 2 roadmap item complete?** Yes. `PROJECT_ROADMAP.md`'s Phase 2 command list — `CreateLeadCommand`, `ScheduleInspectionCommand`, `CompleteInspectionCommand`, `CreateAngebotCommand`, `AddAngebotSectionCommand`, `AddAngebotItemCommand`, `SubmitAngebotForReviewCommand`, `RequestAngebotChangesCommand`, `ApproveAngebotCommand` — all nine are implemented, tested, and committed (§5.4). `CatalogItem`'s Application layer, while not itself on that list, was a necessary and justified prerequisite for `AddAngebotItemCommand` and is also complete.

**2. Every deferred item explicitly documented with a reason?** Yes — cross-checked against §7 above and `NEXT_STEPS.md` §2:
- `SaveAngebotItemAsCatalogItemCommand` — out of scope, `ARCHITECTURE_DECISIONS.md` D39 (roadmap-scope check + premature-lookup-capability reasoning).
- `IFileStorage.GetAsync`/`DeleteAsync` — no current command needs them (`CLAUDE.md` §4).
- `Angebot.Send()`/`RecordCustomerApproval()`/`RecordCustomerRejection()` Application commands — deferred to Phase 6, depend on `ITokenLinkService`.
- `AngebotItem` update/remove methods — open question, not a rule, `ARCHITECTURE_DECISIONS.md` D12.
- HTTP status-code mapping for Domain exceptions — deferred to Phase 4.
- `INumberGeneratorService`'s atomic-transaction guarantee — unverified, flagged as highest-risk, to be tested in Phase 3.

None of these are silent gaps; each has a named reason and a named future trigger for revisiting.

**3. Are `PROJECT_STATE.md`, `NEXT_STEPS.md`, and `PHASE2_PROGRESS.md` internally consistent?** Yes, verified by cross-reading all three just now: all three agree Phase 2's roadmap scope (§5.4's fifteen slices) is complete, all three agree `SaveAngebotItemAsCatalogItemCommand` is deferred (not "the next slice"), and all three point to the same `ARCHITECTURE_DECISIONS.md` D39 for the reasoning. `PHASE2_PROGRESS.md` gained its own closing "Phase 2 Scope Correction & Closeout" section recording the same decision in slice-log form.

**4. Final test count?** **297 tests, 0 failing** — 153 `RenoTrack.Domain.Tests` + 144 `RenoTrack.Application.Tests` (`RenoTrack.Api.Tests` still empty, Phase 4 not started).

**5. Build clean?** **Yes — 0 Warnings, 0 Errors** (`TreatWarningsAsErrors` solution-wide, `CLAUDE.md` §14).

**6. Recommended PR title and commit range:**
- **Title:** `Phase 2: Application layer — Lead/Inspection/Angebot commands, queries, and guards` (matches `PROJECT_ROADMAP.md`'s own pre-named PR title for this phase, line 92 — no reason to deviate).
- **Commit range:** `main..feature/phase-2-application-layer`, starting at `ef9bc27` (Slice 1, `CreateLeadCommand`) — 15 vertical-slice commits plus a small number of documentation-only commits (mid-phase handoff docs, closeout docs, and closeout sanity-review corrections). Let `git log main..feature/phase-2-application-layer` at PR-open time be the source of truth for the exact count — it was already drifting across this very closeout review as fix commits landed, which is itself the reason not to hardcode it in prose.
- **PR description should note explicitly:** `CatalogItem`'s Application layer was an in-scope, justified insertion (needed by `AddAngebotItemCommand`); `SaveAngebotItemAsCatalogItemCommand` was reviewed and confirmed out of scope (D39), not overlooked.

---

## 11. Phase 3 Closeout Review

Performed 2026-07-30, immediately after Slice 15 (Identity), before opening a PR. Every finding below was checked directly (build/test run, migration CLI, file inspection), not assumed from memory. **§11.7 below records what happened after this review** — the code review's Should-Fix fixes, the CI environmental fix, the `IdentityRoleSeeder` redesign, and the actual merge — since none of that had happened yet when §11.1–§11.6 were written.

### 11.1 Documentation Audit

- **`PROJECT_STATE.md`/`NEXT_STEPS.md`/`PHASE3_PROGRESS.md`** cross-checked for consistency: all three agree all 15 slices are done, all three cite the same decision numbers (D40–D54) for the same facts, none references a "next slice" that's already been built.
- **`ARCHITECTURE_DECISIONS.md`**: D40 through D54 all present, in order, no gaps (`grep` confirms 15 consecutive headers). Each of D49/D51/D53 (Infrastructure-only placement) and D50/D54 (best-effort/race-tolerant behavior) explicitly references its precedent rather than re-deriving the reasoning from scratch.
- **`ERD.md`** corrected three times during Phase 3 (D41 — computed properties; D52 — `NumberSequences`' transaction-boundary wording; D53 — `USER`/`ROLE` real Identity schema) — each correction made in the same commit as the code it describes, per `CLAUDE.md` §15's documentation-first discipline, not batched up for this closeout.
- **`Architecture.md`** §8 corrected (D52) to describe the actual atomic-statement number-generation design rather than literal same-transaction wording that the already-built handler ordering made impossible.
- **`CLAUDE.md`** §13 remains correct from its Slice 1 correction (D42 — `LocalDiskFileStorage` is Phase 4's); no further correction needed during Slices 2–15.
- **No stale cross-references found** — spot-checked every `ARCHITECTURE_DECISIONS.md` reference added in Slices 10–15 against the actual decision numbers; all resolve correctly.

### 11.2 Architecture Decision Audit

15 decisions recorded in Phase 3 (D40–D54), grouped by theme:

| Theme | Decisions |
|---|---|
| Test infrastructure / migration process | D40 (new test project), D45/D46 (schema-review + migration-review catching two real bugs), D47 (design-time factory) |
| Documentation corrections (ERD/CLAUDE.md corrected to match confirmed reality) | D41, D42 |
| EF Core mapping techniques | D43 (backing-field navigation) |
| Deferred-until-Identity FKs | D44 (deferred), resolved in Slice 15 |
| `IUnitOfWork` scope | D48 (confirmed thin) |
| Infrastructure-only persistence models (forced or chosen) | D49 (`AuditLog`, judgment call), D51 (`NumberSequence`, judgment call), D53 (`ApplicationUser`, forced by D1) |
| Best-effort / race-tolerant behavior patterns | D50 (audit), D52 (number generation), D54 (role seeding) — the same "catch, re-verify, treat as benign" shape reused three times for three genuinely different problems, not copy-pasted blindly each time |
| DI/composition choices | D54 (`AddIdentityCore` vs `AddIdentity`) |

Every decision that changed already-written documentation (D41, D42, D52, D53) did so in the same commit as the code change — verified by checking each commit's diff includes both.

### 11.3 Migration Audit

Four migrations, applied in this order (verified via `dotnet ef migrations list`, all currently `(Pending)` — none applied to any shared/persistent database, consistent with every prior status note in this file):

1. `InitialCreate` (Slice 1–2) — the 8 core business tables.
2. `AddAuditLog` (Slice 10) — `AuditLogs`, no FK (deliberately, D50's "no cross-entity linkage").
3. `AddNumberSequence` (Slice 11) — `NumberSequences`, no FK.
4. `AddIdentity` (Slice 15) — 7 `AspNetX` tables, plus 5 retroactive FKs on `Leads`/`Inspections`/`Angebote`(×2)/`AngebotReviewComments`.

**`dotnet ef migrations has-pending-model-changes` → "No changes have been made to the model since the last migration."** — confirmed directly, not assumed: the current C# model and the migration history are in sync. Every migration was manually reviewed at the time it was generated (Slice 2 caught two real bugs this way — D45/D46 — proving the review step itself has caught real issues, not just theoretical ones).

### 11.4 DI Audit

`AddInfrastructure()` registers all 11 Infrastructure-implemented Application interfaces — `ILeadRepository`, `IInspectionRepository`, `IAngebotRepository`, `IAngebotReviewCommentRepository`, `ICatalogItemRepository`, `ICatalogItemQueries`, `IUnitOfWork`, `IAuditService`, `INumberGeneratorService`, `IFileStorage`, `IEmailSender` — every one Scoped, verified by direct comparison against every `public interface I...` declared in `Common/Interfaces/` and `CatalogItems/ICatalogItemQueries.cs` (12 found; 11 registered; the 12th, `IOwnershipValidator`, correctly excluded per CLAUDE.md §9 — its implementation lives in `RenoTrack.Application`, not here). `RenoTrackDbContext` registered via `AddDbContext` (Scoped default); `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<int>>().AddEntityFrameworkStores<RenoTrackDbContext>()` added alongside with no new composition root. `DependencyInjectionTests` proves this container actually builds with `ValidateOnBuild`/`ValidateScopes` enabled (would fail if any Singleton captured the Scoped `DbContext`) and resolves every registration inside a real scope.

### 11.5 Test Summary

**`dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors.** **`dotnet test RenoTrack.slnx` → 371 tests passing, 0 failing:**

| Project | Tests |
|---|---|
| `RenoTrack.Domain.Tests` | 153 |
| `RenoTrack.Application.Tests` | 144 |
| `RenoTrack.Infrastructure.Tests` | 74 |
| `RenoTrack.Api.Tests` | 0 (Phase 4 not started) |
| **Total** | **371** |

`RenoTrack.Infrastructure.Tests`' 74 break down as: 15 Slice-1 persistence tests, 5 migration/schema tests (Slice 2), 3 `UnitOfWork` tests, 4 `LeadRepository`, 5 `InspectionRepository`, 9 `AngebotRepository`, 3 `AngebotReviewCommentRepository`, 5 `CatalogItemRepository`, 3 `CatalogItemQueries`, 4 `AuditService`, 4 `NumberGeneratorService` (including the 50-parallel-caller concurrency proof), 1 `PlaceholderFileStorage`, 3 `LoggingNoOpEmailSender`, 4 `DependencyInjection`, 4 `Identity` (including the 10-concurrent-instance role-seeding race proof) — plus the net effect of Slice 15's real-FK retrofit, which required fixing (not just adding) 19 previously-passing tests across 7 files. Every test class runs against real SQL Server LocalDB except the four classes with genuinely no database concern (`PlaceholderFileStorageTests`, `LoggingNoOpEmailSenderTests`, `DependencyInjectionTests`'s container-build check, and the password-hasher sanity check) — the standing decision (D40) that `RenoTrack.Infrastructure.Tests` never uses the EF Core InMemory provider held for all 15 slices.

### 11.6 Merge Readiness Report

- **Branch:** `feature/phase-3-infrastructure-efcore`, 17 commits ahead of `main`, working tree clean, `origin/main` and local `main` match exactly (`git fetch` confirmed no drift) — safe to open a PR against current `main`.
- **Build/test:** clean, as above.
- **Scope:** every item in Phase 3's approved 15-slice dependency map is done — no partial slices, no "configure this later" left anywhere.
- **Known, deliberate deferrals carried past this phase (not gaps):** real `LocalDiskFileStorage` (Phase 4, D42); real SMTP `IEmailSender` (Phase 9, `CLAUDE.md` §11); `AddApplication()` DI extension for `IOwnershipValidator`/validators/command handlers (Phase 4, since `RenoTrack.Api` has no controllers yet to need them); HTTP status-code mapping for Domain exceptions (Phase 4); authentication/JWT issuance (Phase 4, Architecture.md §7.1) — Slice 15 deliberately stopped at storage.
- **Recommended PR title:** `Phase 3: Infrastructure layer — EF Core persistence, repositories, and Identity storage` (matches the `Phase N: <layer> — <one-line scope>` pattern Phase 2's PR title established).
- **Recommended commit range:** `main..feature/phase-3-infrastructure-efcore`, `1edccae` (Slice 1) through `b6d4d48` (Slice 15) — 15 feature commits, one docs-sync commit (`fca7eb8`, mid-phase).
- **PR description should note explicitly:** the retroactive-FK test breakage in Slice 15 (19 tests, expected and fixed, not a late-discovered bug); the three narrow, explicitly-scoped exceptions to "EF Core only" (raw SQL in `NumberGeneratorService`, D52) and "no custom auth cookies" (`AddIdentityCore`, D54); that `INumberGeneratorService`'s concurrency guarantee (the single highest-risk item flagged since Phase 2, D34) is now proven, not just implemented.
- **Verdict at the time this section was written: ready to open as a PR**, pending the user's own final read-through and explicit go-ahead to push/open. (Superseded — see §11.7: additional work happened between opening the PR and merging it.)

### 11.7 What Happened Between This Review and the Actual Merge

This section records everything that happened *after* §11.1–§11.6 were written — a separate code-review pass, an environmental CI fix, and a real bug found and fixed with a genuine design change — none of which existed yet when the verdict above was recorded.

**Code review (lead-reviewer pass, role-reversed: reviewer only, no implementation until findings were presented):** three Should-Fix findings, no Must-Fix findings:
1. `HANDOFF_PROMPT.md` described the PR as "not yet opened" — stale by the time of review.
2. `tests/RenoTrack.Infrastructure.Tests.csproj` had no explicit `<ProjectReference>` to `RenoTrack.Application` — it compiled only via implicit transitive resolution through `RenoTrack.Infrastructure`, which this project's own layering discipline treats as fragile (explicit references only, `CLAUDE.md` §1).
3. `tests/RenoTrack.Infrastructure.Tests/Identity/IdentityTestServices.cs` duplicated `AddInfrastructure()`'s DI registrations by hand instead of calling the real extension method — a maintenance hazard (two places to keep in sync).

All three fixed in commit `c085058`, followed by a full rebuild/retest/`has-pending-model-changes` re-verification before pushing.

**CI environmental failure, fixed without touching tests or replacing LocalDB (D56):** the first CI run failed because the single Linux job couldn't run `RenoTrack.Infrastructure.Tests` (LocalDB is Windows-only, D40). `.github/workflows/ci.yml` was split into `build-and-test` (`ubuntu-latest`) and `infrastructure-tests` (`windows-latest`, `needs: build-and-test`, starts `sqllocaldb start MSSQLLocalDB`). Verified via the GitHub Actions API: both jobs green.

**A real, previously-undetected concurrency bug found and fixed with a genuine design change (D55), not a patch:** re-running `IdentityRoleSeederTests`'s concurrency test repeatedly during local Release-config verification (not something the original review or a single CI run would have caught) surfaced a ~66% failure rate, caused by `RoleManager`/`DbContext` tracking state from a failed role-seed attempt bleeding into the next role's `SaveChangesAsync()` call. Rather than patch around it (a `DbContext` parameter, or reflection into `RoleManager.Store`), the design was reworked: `IdentityRoleSeeder` became a dedicated `AddScoped` DI service with `IServiceScopeFactory` injected via its constructor, creating one fresh `IServiceScope` per role internally, keeping the public `SeedRolesAsync()` parameterless. Verified empirically: 32 consecutive runs (22 Debug, 10 Release) all passed after the fix, versus the prior ~2-in-3 failure rate. Full alternatives-considered record in `ARCHITECTURE_DECISIONS.md` D55.

**Final push and merge:** the Should-Fix fixes, the CI split, and the `IdentityRoleSeeder` fix were pushed together (`f5d3108`, once everything was consistently green), the PR was marked ready for review, and **PR #6 was merged into `main` via merge commit `85df430`** on 2026-07-31. `feature/phase-3-infrastructure-efcore` remains on the remote (merged, not deleted) but is no longer the active branch.

**Post-merge verification on `main` itself** (not just on the feature branch before merge): `dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors; `dotnet test RenoTrack.slnx` → 371 passing, 0 failing (153 Domain + 144 Application + 74 Infrastructure); `dotnet ef migrations has-pending-model-changes` → no pending changes. All three checked directly against the merged `main`, not carried over from the pre-merge branch state.

**Final verdict: Phase 3 is done. `main` is green, clean, and ready for Phase 4 to begin.**

---

## 12. Phase 4 Closeout Review

Phase 4 went through **two separate review passes** after its eleventh slice, and they are easy to confuse because both numbered their findings `B1`, `B2`, … They are recorded separately here because the second pass changed production authentication code, while the first did not.

### 12.1 Pass one — internal closeout review (`5371fe9`, "docs/test: close Phase 4 review findings")

**No Must-Fix findings. Six Should-Fix findings, all closed.** The only `src/` change was a doc comment; no production behaviour changed.

| # | Finding | Resolution |
|---|---|---|
| B1 | The Slice 8 Admin role-gate test asserted only a status code, so it could not tell which layer produced the 403 — meaning the action's `[Authorize(Roles = Inspector)]` was pinned by no test at all | Asserts an empty body (role-gate rejections carry none; a `ForbiddenException` yields ProblemDetails) and renamed to say so, matching the pattern Slices 9 and 10 already used. Verified adversarially: removing the action's role requirement made it fail with a ProblemDetails body, then the attribute was restored byte-identically |
| B2 | `HANDOFF_PROMPT.md` still described 8 of 11 slices and "YOUR FIRST TASK: SLICE 9", while `NEXT_STEPS.md` §6 calls it the canonical starting point | Rewritten for 11/11 slices; `NEXT_STEPS.md` §6 reconciled, including removing a reference to `AGENTS.md`, which no longer exists |
| B3 | `README.md` said Phase 4 "is next", and Getting Started listed only build/test — a fresh clone running the API failed with no guidance, sharper after Slice 11 since an empty development database now also needs `Database:Mode=Migrate` | Status corrected; a Configuration section added covering all six settings, distinguishing tracked defaults, required values, secrets, the Development `Migrate` bootstrap, and Production's `Verify`-plus-separate-migrations path. States explicitly that no user account is created in any environment and that OQ-1 remains open |
| B4 | Two `RenoTrackApiFactory` comments still described `Program.cs` as seeding Identity roles at startup | Reworded for the Slice 11 path. D58 unchanged — the fixture still owns its database lifetime |
| B5 | D50 was accurate, but the *consequence* — that `AuditService.LogAsync` calls `SaveChangesAsync` on the same scoped `DbContext` — was recorded only in `NEXT_STEPS.md` §5a, not where the code is read | The class now documents it directly: intended usage is after the primary business commit, and unrelated tracked changes pending at that moment will be flushed too. Best-effort semantics unchanged; no redesign |
| B6 | `PROJECT_STATE.md` "Last updated" stale | Corrected |

State after this pass: **549 tests passing** (153 / 165 / 101 / 130).

### 12.2 Pass two — PR review findings (`e908c7d`, "fix(auth): close PR review findings B1-B4")

This pass **did** change production code, all of it authentication.

**B1 — refresh-token rotation could be raced. This is the most consequential finding of Phase 4.** `RevokedAt` is now an **EF concurrency token**, so the database arbitrates "a token transitions from active to revoked exactly once". The losing `UPDATE` matches zero rows and raises `DbUpdateConcurrencyException`; because EF wraps `SaveChanges` in one transaction, the loser's replacement `INSERT` rolls back with it — **revocation and its successor are always committed together**.

**The race was not theoretical, and the PR review had under-stated it as a narrow window.** With the protection removed, **all 8 concurrent refreshes of one token succeeded**, producing 8 live chains from a single token and bypassing reuse detection entirely — reproduced **3 of 3 runs**.

A loser is deliberately **not** treated as reuse: it is a legitimate concurrent request, not a replay, and revoking the chain there would let a client's double-submit log itself out. It returns the same 401 as every other refresh failure, so unknown / revoked / raced stay indistinguishable. Its tracked entities are detached, since the request-scoped `DbContext` is shared and a failed `INSERT` left in `Added` could be committed by a later `SaveChangesAsync` — the Slice 9 hazard.

**No migration was required:** a concurrency token on a non-`rowversion` column is model metadata only, confirmed by `has-pending-model-changes`.

**B1 follow-on, found by the new test rather than by inspection.** Making `RevokedAt` a concurrency token meant `RevokeAllForUserAsync`'s load-mutate-save could throw `DbUpdateConcurrencyException` when a concurrent request revoked the same rows — surfacing as an **unmapped 500** and rolling back the whole batch. Replaced with a set-based `ExecuteUpdateAsync`, which states the intent exactly, is atomic at the database, and bypasses the change tracker. Measured: the old implementation failed **2 of 6** full-suite runs; the replacement passed **6 of 6**.

**B2** — login and refresh 401s were a bare JSON string, the one place the API did not honour its own RFC 7807 contract. Now `ProblemDetails` via `ControllerBase.Problem`, verified to flow through `CustomizeProblemDetails` so `traceId` is present. Status, message text and non-enumeration behaviour unchanged.

**B3** — the no-role Lead test was proving the class-level role gate, not the fail-secure helper it was named for; the helper's final refusal is unreachable through HTTP while the attribute stands. Renamed and documented honestly, and its one HTTP-reachable rule — **narrower role wins for a dual-role account** — is now pinned by a real test using a newly seeded dual-role user.

**B4** — real framework behaviour was observed before anything was changed: omitting the multipart file part yields **400, not 500**, because implicit-required binding rejects it before the action. Production code unchanged; the test was retained to pin it.

**Adversarial verification, each observed then restored byte-identically:**

| Broken implementation | Observed failure |
|---|---|
| Concurrency token removed | 8 of 8 rotations succeeded (3/3 runs) |
| Set-based revoke reverted | 500 in the distribution (2 of 6 runs) |
| Role-check order reversed | A dual-role account saw other inspectors' Leads |

### 12.3 Merge and post-merge verification

**PR #8 was merged into `main` via merge commit `e1a4d9e`** ("Phase 4: API — JWT auth, Lead & Inspection endpoints with role-scoped access"). `feature/phase-4-api-auth-leads-inspections` remains on the remote, merged, with its local copy deleted — the same treatment the Phase 2 and Phase 3 branches received.

**Verified directly on merged `main`, not carried over from the branch:** `dotnet build RenoTrack.slnx` → 0 Warnings, 0 Errors; `dotnet test RenoTrack.slnx` → **553 passing, 0 failing** (153 Domain + 165 Application + 101 Infrastructure + 134 Api); `dotnet ef migrations has-pending-model-changes` → no changes; five migrations, none regenerated, squashed, renamed, or edited by Phase 4 beyond adding `AddRefreshTokens`.

**Final verdict: Phase 4 is done. `main` is green, clean, and ready for Phase 5 to begin.**
