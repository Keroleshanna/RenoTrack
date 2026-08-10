# PHASE9_PROGRESS.md — Email Service Integration (real, not placeholder)

**Branch:** not created yet. **Status: DESIGN APPROVED, IMPLEMENTATION NOT STARTED.**
**Roadmap entry:** `PROJECT_ROADMAP.md` Phase 9. **PR title:** `Phase 9: Real email delivery — SMTP/MailKit, German templates, notification status & retry`.
**Prerequisite:** Phase 8 (merged, PR #14 `0c12948`). SRS **OQ-3a is resolved**; **OQ-3b is deferred to deployment by decision** and blocks nothing.

This file records the decisions approved **before any code was written**, following the same discipline as `PHASE8_PROGRESS.md`'s thirteen. Nothing below has been implemented.

---

## Approved decisions (F1–F8, approved 2026-08-09)

Each is recorded permanently in `ARCHITECTURE_DECISIONS.md`; this table is the index.

| # | Decision | Recorded in |
|---|---|---|
| **F1** | Transport is **SMTP via MailKit**, behind the unchanged `IEmailSender`. Application never sees SMTP, MailKit, a host or a credential. OQ-3a resolved; OQ-3b (real mailbox, sender identity, recipients) deferred to deployment, with **no compiled-in value and no default** | **D68**, `SRS.md` §10, `Architecture.md` §10 |
| **F2** | Failure architecture is **C2b**: a persisted `NotificationDeliveries` record, `Pending → Sending → Sent \| Failed → (retry) → Sending → Sent`, written **after** the business commit. **No Outbox, no broker, no queue, no `BackgroundService`/`IHostedService`, no dispatcher.** The crash window between commit and row-insert is accepted and stated openly | **D69**, `Architecture.md` §10, `ERD.md` §2 |
| **F3** | Admin notification recipients are a **configured list**, independent of the Identity Admin role. No `IUserQueries` lookup is added for this | **D71**, `PermissionMatrix.md` §9 note |
| **F4** | The notification record is **Infrastructure-owned**, never a Domain aggregate, and no notification state is added to an existing aggregate. The Domain stays unaware of email, SMTP, attempts and retry | **D69** |
| **F5** | Retry is **manual, Admin-triggered, synchronous**; no automatic retry, no backoff, no attempt cap. It retries **only the notification**, reconstructed from persisted business data, and never re-executes the business operation | **D70** |
| **F6** | New Admin-only operations: *view failed/pending notifications* and *retry a notification* — Admin `F`, Inspector `—`, no ownership validation | **`PermissionMatrix.md` §9** |
| **F7** | Phase 9 is the **complete email-delivery workflow**, not SMTP integration only: transport, config, six German templates, safe failure handling, notification persistence, Admin visibility, manual retry, documentation gate | `PROJECT_ROADMAP.md` Phase 9 |
| **F8** | `Reply-To` is **optional** deployment configuration. Never hard-coded, never invented when absent. No business rule depends on it | **D68**, `Architecture.md` §10 |

### The behavioural rule that governs the whole phase

> A committed business operation is a **success** even when its notification email fails. The API never turns one into the other — **including on the two anonymous flows** (the website contact form, and the customer's token-link decision), where no Admin is present in the request to be told anything. The failure is persisted, visible to an Admin, and retryable. Logging alone is insufficient, and D69 records exactly why.

### Duplicate-send policy (D70)

- **Definite failure** → retry is safe.
- **Ambiguous failure** (SMTP timeout after `DATA`) → a retry may duplicate. **Accepted**; no message-ID deduplication is introduced.
- **Admin double-click** → prevented by the status transition `Failed → Sending → Sent`. The concurrency mechanism is designed before implementation.
- Harm is bounded because every notification is **content-idempotent** — the same link, the same facts. A duplicate is a second identical email, never a second business effect.

### Retention

**None.** Notification rows remain until a real requirement exists. No 30/90-day deletion, no archival job (contrast `RefreshTokens`, whose retention-to-`ExpiresAt` rule is real and documented in D60).

---

## Facts verified against the repository during the design review

Recorded because the design depends on them, and because a later reader should not have to re-derive them.

- **`IEmailSender` has six methods**, all called from real handlers; `Architecture.md` §10 previously named only **five** templates. The sixth (changes-requested, to the Inspector) comes from `Sequence Diagram.md` §5 and is a known SRS-completeness gap (`CLAUDE.md` §11). **§10 has been corrected.** Producing six templates is not scope creep.
- **All six call sites `await` the sender uncaught, after `SaveChangesAsync` and after `auditService.LogAsync`.** With a real sender this would produce HTTP 500 on committed work. Today it cannot, only because `LoggingNoOpEmailSender` cannot throw.
- **Two of the six senders are anonymous public endpoints** — `CreateLeadCommandHandler` (contact form) and `RecordAngebotDecisionCommandHandler` (customer decision). This is the concrete reason logging is insufficient and persistence is required.
- **All six notifications are reconstructible from persisted state**: Angebot-ready ← `Angebot` + `TokenLink`; invoice-ready ← `Invoice` + `Customer` + `TokenLink`; decision ← `Angebot` + `Lead`; submitted-for-review ← `Angebot`; new-lead ← `Lead`; changes-requested ← `Angebot` + `AngebotReviewComment`. **No payload needs storing.**
- **`TokenLink.Token` is stored raw**, not hashed (`TokenLinkConfiguration`, unique index) — deliberately, since the public read looks it up. A retry therefore re-derives the customer's link without `NotificationDeliveries` holding a second copy of any credential.
- **No `IHostedService`/`BackgroundService` exists anywhere in `src/`.** Any background option would be the first, which is a material part of why C3/C4 were rejected.
- **`Lead.Email` is non-nullable** and validated `NotEmpty().EmailAddress()`, so the customer recipient is always present.
- **`AuditLog` cannot carry delivery state**: D50 makes audit writes best-effort and swallowed, and `CLAUDE.md` §10 reserves audit for business milestones. **No new `AuditAction` value is added for notification failures.**
- **Configuration precedent to follow**: `TokenLinkOptions`/`JwtOptions`/`FileStorageOptions` — `SectionName` const plus `Validate()` throwing and naming the exact key, with no silent default.
- **Secret precedent to follow**: D64 — `dotnet user-secrets` in Development (registered only in Development, so a credential cannot reach Production), environment variables in Production, never committed.
- **Non-production safety precedent to follow**: D64's positive allowlist — refuse by default, and *throw* where enabled-but-not-permitted rather than silently skipping.

---

## Approved slice plan

**Approved 2026-08-09.** Revised the same day by the Slice 1 design review, which found that the original Slice 1 could not be built as scoped: `SmtpEmailSender` implements six `IEmailSender` methods, and the German templates sat in Slice 2, so a Slice-1 sender would have had nothing real to send. **Slices 1 and 2 are therefore merged**, on exactly the reasoning that settled the 3a/3b boundary — a component arrives with its first real consumer, never as an unused abstraction (`CLAUDE.md` §4).

**Label mapping**, so earlier approvals and the commit history stay traceable: old Slice 1 + old Slice 2 → **new Slice 1**; old **3a** → **new Slice 2**; old **3b** → **new Slice 3**; old 4/5/6 → new 4/5/6 unchanged. **The 3a/3b boundary itself is preserved exactly** — only its numbering changed. There is no Slice 2 gap and no overlap.

| # | Slice | Scope | Schema |
|---|---|---|---|
| 1 | **Email configuration + SMTP sender + German templates** *(old 1 + 2 merged)* | `EmailOptions` (+ `Validate()`), **`Email:Enabled`**, `SmtpEmailSender` over MailKit, DI selection, `LoggingNoOpEmailSender` **retained**, **all six German templates**, and the tests for the above. **Plaintext only.** Schema-free, migration-free, `NotificationDeliveries`-free, lifecycle/status-type-free, Outbox-free, queue-free, hosted-service-free, retry-free | none |
| 2 | Safe failure handling **only** *(old 3a — boundary unchanged)* | Catch, log `Warning`, never rethrow. Adversarial pass: remove the catch, watch an `Api.Tests` case turn 200 into 500. **No status type, no delivery-record abstraction, no persistence model, no EF configuration** — see the resolved boundary below | none |
| 3 | Notification persistence *(old 3b — boundary unchanged)* | The lifecycle/status type **and** the Infrastructure-owned `NotificationDeliveries` table + EF configuration + **migration #9**, status transitions, write-after-commit — introduced together with their first real usage | **migration #9** |
| 4 | Admin visibility | Read endpoint for failed/pending notifications (`PermissionMatrix.md` §9) | none |
| 5 | Manual retry | Retry endpoint; per-type reconstruction; double-click guard | none |
| 6 | Documentation reconciliation + adversarial verification + completion gate | Cross-document sweep in the Phase 8 shape | none |

### Slice 1 — approved decisions (2026-08-09)

1. **Merged scope**, as above. `SmtpEmailSender` ships with the six templates that are its first real consumers.
2. **`Email:Enabled` is an explicit key — delivery is never inferred from the presence of an SMTP host.**
   - **Non-production:** defaults to `false`; email configuration is **not required** and **not validated** when disabled; `LoggingNoOpEmailSender` stays the resolved `IEmailSender`. Setting `Email:Enabled=true` explicitly permits real SMTP delivery.
   - **Production:** `Email:Enabled` must be **explicitly `true`**. When enabled, the required configuration is validated **eagerly at startup**, and anything missing or invalid **fails startup naming the exact key**. **Production must never silently fall back to `LoggingNoOpEmailSender`.**
   - Conditional validation is required, not stylistic: `DependencyInjectionTests` composes `AddInfrastructure` with only the connection string, `Jwt:*`, `FileStorage:RootPath` and `TokenLink:LifetimeDays`, so unconditional validation would break a passing test. D64's `DevelopmentBootstrapOptions.Validate()` already defers validation behind its guard for the same class of reason.
   - Both test projects run as Development (`RenoTrackApiFactory` calls `UseEnvironment("Development")`; `TestHostEnvironment` defaults to `Environments.Development`), so refuse-by-default keeps every existing test green with **no real SMTP and no factory override**.
3. **Inspector recipient — still open.** See "Open items" below; no interface is invented until the repository investigation is reviewed.
4. **Plaintext only for v1. No HTML templates**, no multipart alternative. FR-9.3 constrains language and tone, not format.
5. **Logging: never a recipient address, never a token, never a generated token URL.** Permitted: notification type, business identifier, success/failure, exception information. Recipient addresses are Lead/Customer personal data under `Architecture.md` §12, and the token rule is `CLAUDE.md` §22 (which `LoggingNoOpEmailSender` already follows).
6. **SMTP credentials:** username **and** password both absent ⇒ unauthenticated submission is allowed; both present ⇒ authenticate; **exactly one present ⇒ configuration validation failure**. No other authentication behaviour is invented — no OAuth2, no implicit anonymous fallback after an auth failure.

### Slice 1 — design-review decisions (approved 2026-08-09)

**S1-1 — SUPERSEDED before implementation by S1-3 (option A2).** S1-1 originally approved giving `AddInfrastructure` an `IHostEnvironment` parameter so the Production guard could run at composition time. Enumerating the real call sites afterwards showed that would have been wrong, and the decision was revised — see S1-3.

**S1-3 — the Production guard is a separate startup step; `AddInfrastructure`'s signature does not change (approved 2026-08-09).**

- **DI selection depends on `Email:Enabled` alone**: `false` (or absent) ⇒ `LoggingNoOpEmailSender`; `true` ⇒ `SmtpEmailSender`. No environment is consulted at composition, so no signature change is needed anywhere.
- **The refuse-by-default Production rule is enforced at application startup**, by a dedicated verifier resolved in `Program.cs` that takes `IHostEnvironment` from DI exactly as `DatabaseInitializer` does (D63). **Production with `Email:Enabled != true` fails startup naming `Email:Enabled`.** Development/Test may leave it absent or false and resolve `LoggingNoOpEmailSender`.
- **Why S1-1 was revised.** `AddInfrastructure` has **7 call sites in 6 files**, and two existing suites deliberately compose it under `Environments.Production` with no `Email:*` configuration — `DevelopmentBootstrapTests` (lines 412, 454) and `DatabaseInitializerTests` (lines 264, 278). A composition-time email guard would have thrown before those tests reached their subject, and in particular would have pre-empted D64's tested guard *ordering*, where a Production host with the bootstrap enabled must be told the feature is refused in Production rather than asked for a credential it must never supply. **Preserving D63/D64's composition and guard ordering is a hard constraint**, and **no email configuration is added to unrelated Production test fixtures** to work around it.
- **Also rejected:** a factory-resolution approach that defers the failure to first request (loses fail-fast), and reading the environment name from configuration (fragile).

**S1-4 — MailKit version pinned to 4.17.0 (verified, not guessed).** The repository has no central package management and pins every `PackageReference` explicitly, so MailKit is pinned the same way. The version was **verified against nuget.org's flat-container index** (`api.nuget.org/v3-flatcontainer/mailkit/index.json`) as the latest stable release; it was not chosen from memory. **`NU1903` (or any other warning suppression) must not be widened** to accommodate a package advisory without the Product Owner's approval.

**S1-5 — SMTP timeout: MailKit's default is used for Slice 1.** No `Email:TimeoutSeconds` key, no timeout abstraction. Revisit only if a concrete repository or runtime requirement appears.

**S1-2 — German copy: APPROVED AND FROZEN (2026-08-09).** All six templates were drafted in full, reviewed by the Product Owner, and approved. Copy is company voice, not an engineering detail to be settled inside the slice. The frozen text is recorded below under "Slice 1 — approved email copy".

Four copy decisions accompanied the approval:

1. **`{FromDisplayName}` stays in the customer sign-off** (`Mit freundlichen Grüßen` / `{FromDisplayName}`), sourced from `Email:FromDisplayName` configuration. **No company-identity field is invented** — BR-5's company data remains Phase 14's.
2. **Formal "Sie" is kept in the Inspector notification.**
3. **"Diese E-Mail wurde automatisch erzeugt." appears on the four internal templates only.** The two customer templates (Angebot ready, Invoice ready) **must not** contain it.
4. **No link-validity sentence, and no validity period stated.** The notification record carries no `ExpiresAt`, and configuration-derived information must not be duplicated into the email where it could drift from the real `TokenLink.ExpiresAt`.

### Slice 1 — Inspector recipient (approved 2026-08-09)

**D1 — the address is resolved inside Infrastructure, by a small dedicated class**, `InspectorId → AspNetUsers.Email`. **Do not** add a method to `IUserQueries`, **do not** widen `AngebotChangesRequestedNotification`, and **do not** touch the handler or any Application call site. D68 promised Phase 9 changes the `IEmailSender` implementation and not its call sites, and Infrastructure already has the established pattern for a direct read-only `DbContext` query — `UserQueries` reads `dbContext.Users` rather than going through `UserManager`, with the recorded reason that a read-only projection should not materialise a full user. A dedicated DI-registered class rather than an inline query follows D55's shape.

**D2 — a null Inspector address is a delivery failure**, never a silently-skipped or successful notification. This is reachable, not hypothetical: `AspNetUsers.Email` is `nvarchar(256)` with **no `.IsRequired()`** (verified in the model snapshot), because `ApplicationUserConfiguration` configures only `Name`/`IsActive`/`CreatedAt` and leaves the Identity base columns as the framework defines them. **Slice 1 lets that failure propagate** — turning it into catch/log/never-rethrow is Slice 2's job, and Slice 1 must not pre-empt it.

**D3 — `IsActive` is not a delivery condition.** If an Angebot has an `InspectorId` and that Inspector has an address, the notification is sent regardless of `IsActive`. **No `IsActive` check belongs in the email sender.** Deactivation governs whether someone may act in the dashboard (`IUserQueries.IsActiveInspectorAsync` exists for assignment eligibility, a different question); it is not a rule about who is told what happened to work they own.

### Slice 1 — public token-link base URL (D4 investigation, 2026-08-09)

Investigated against the repository; **no decision taken**.

- **No URL is constructed anywhere in `src/` today.** A repository-wide search for absolute URLs and base-address configuration finds only launch profiles and unrelated comments. The two customer notifications carry the raw `Token` and nothing else.
- **`TokenLinkOptions` contains exactly one property, `LifetimeDays`.** There is no base-address setting to reuse, in that section or any other — `appsettings.json` holds only `Logging`, `AllowedHosts`, `RateLimiting`, `TokenLink` and `Database`.
- **The shape is already documented, and must not be reinvented.** `Sequence Diagram.md` §6 line 324 writes `link="https://.../angebot/{token}"` and §9 line 484 writes `link="https://.../invoice/{token}"`. **Note the segments are `/angebot/` and `/invoice/` — the second is English, not `/rechnung/`.** Any earlier suggestion of German path segments throughout is not supported by the documents; the documents win.
- **The link targets the public Website, not the API.** `Sequence Diagram.md` line 332 draws `L->>WS: GET /angebot/{token}` — the Lead's browser goes to the Website participant, which then reads `GET /api/v1/public/angebote/{token}`. So the value needed is the **public website origin**, which is a different origin from the API's.
- **HTTPS is a documented requirement, not an inference.** Both Sequence Diagram links are written `https://`, and `Architecture.md` §12 states "HTTPS enforced everywhere (website, dashboard, API, token links)". Validation may require an absolute `https://` URL on that basis.
- **Phase 9 was already assigned this by the existing code.** `AngebotReadyNotification`'s own doc comment says composing the URL "needs the public website's base address, which is deployment configuration… The Phase 9 implementation owns both the German template and the base URL it is interpolated into." `InvoiceReadyNotification` repeats it. So adding the setting **is necessary** — nothing existing can be reused — and it is Phase 9's by prior decision, not a scope expansion.

**D4.1 — APPROVED (2026-08-09): the key is `TokenLink:PublicBaseUrl`, not `Email:PublicBaseUrl`.** The value belongs to the public token-link system, not to email delivery — email is only its **first** consumer, and the same public links may later be consumed by the Website, a mobile app, a desktop dashboard or another delivery channel. `TokenLinkOptions` already owns token-link configuration (`LifetimeDays`), so lifetime and location of the same customer-facing artefact live in one section. This also follows the existing naming convention, where a section is named after the concern it configures (`FileStorage`, `TokenLink`, `Jwt`, `Database`, `RateLimiting`, `DevelopmentBootstrap`).

The value must be:

- an **absolute HTTPS public Website origin**;
- **deployment configuration, never compiled into source** — no default, failing startup naming the key when required;
- used with the documented paths **exactly as they exist today**:
  - `https://<public-site>/angebot/{token}`
  - `https://<public-site>/invoice/{token}`

**Do not** change `/invoice/` to `/rechnung/`. **Do not** point these links at the API. **The link target is the public Website origin**, which is a different origin from the API's (`Sequence Diagram.md` line 332 draws the Lead's browser reaching the Website, which then reads the public API).

**D4.2 — APPROVED (2026-08-09): the missing Phase 13 Website pages do not block Phase 9 implementation.** The two concerns are separate: **Phase 9 proves and implements the email delivery workflow; Phase 13 provides the customer-facing Website pages that consume the links.** Phase 9 may therefore implement real SMTP delivery and generate the correct public token URLs. **Do not** pull Phase 13 Website work into Phase 9, **do not** invent temporary public pages or an alternate URL shape, and **do not** redesign the token-link contract to avoid the dependency.

This does, however, create a **release/readiness constraint**, recorded here in full:

> **Phase 9 email delivery implementation may be completed independently, but production customer-facing use of the token-link emails depends on the corresponding public Website token pages being available (currently planned for Phase 13, unless an equivalent implementation is deliberately brought forward later).**

Concretely: `src/RenoTrack.Website/Pages` contains only `Index`, `Privacy`, `Error` and shared layouts, so a link emailed today resolves to a **404**. **Real customer-facing email delivery must not be considered production-ready merely because SMTP works while the linked Website pages still return 404.** This is a sequencing dependency to track at release time, not a defect in Phase 9 and not a reason to change anything in Phase 9's design.

**D5 — APPROVED (2026-08-09): Infrastructure owns construction of the public token-link URLs.** `TokenLink:PublicBaseUrl` is deployment configuration; Application deliberately consumes no `IConfiguration` and no hosting configuration (`CLAUDE.md` §22 — `AddApplication()` takes no `IConfiguration` and references nothing from ASP.NET Core or the generic host); and the existing notification documentation already assigns this to Phase 9's implementation in as many words — `AngebotReadyNotification` states that composing the URL "needs the public website's base address, which is deployment configuration: Application knowing it would put a hosting concern in the layer that deliberately takes no IConfiguration at all," and that "the Phase 9 implementation owns both the German template and the base URL it is interpolated into." `InvoiceReadyNotification` repeats it. Keeping construction in Infrastructure is therefore the only reading consistent with both the layering rule and the code's own documentation.

Consequently:

- **Do not** add the base URL to any Application notification record.
- **Do not** add a new Application interface merely for URL construction.
- **Do not** move `TokenLinkOptions` or configuration access into Application.
- Infrastructure may compose `https://<public-site>/angebot/{token}` and `https://<public-site>/invoice/{token}` directly, using the paths exactly as D4.1 fixes them.

**Sequencing note.** Between Slice 1 and Slice 2 there is a window in which a host with `Email:Enabled=true` can turn a committed business operation into a 500, because the handlers still `await` the sender uncaught until Slice 2 adds the catch. This is intended — Slice 1 must not pre-empt Slice 2's change, or Slice 2's adversarial test has nothing to remove. **Do not enable delivery in any environment until Slice 2 has landed.**

### Slice 2 — approved decisions and implementation record (2026-08-09)

**S2-1 — the failure boundary lives inside `SmtpEmailSender` (Option A), following D50.** The six handlers are untouched, no Application-level wrapper or decorator was introduced, and `IEmailSender`'s contract is unchanged. D50's `AuditService` already establishes this exact shape for the system's other best-effort side effect: catch, log at `Warning` with the exception attached, never rethrow.

**S2-2 — the guarded region covers the *complete* delivery operation**: Inspector recipient resolution, message construction/template formatting, **and** SMTP transport. Message construction is therefore *deferred into* the boundary (`Func<CancellationToken, Task<MimeMessage>>`) rather than built at the call site — otherwise a `MailboxAddress` parse of a malformed stored address, and the Inspector lookup, would sit outside the `try` and still reach a handler that has already committed. Adversarial experiment 2 confirms this is load-bearing.

**S2-3 — log level is `Warning`**, matching D50 and `LoggingNoOpEmailSender`. No `Error`-level logging was introduced.

**S2-4 — `OperationCanceledException` is swallowed like any other delivery failure.** Cancellation still cancels the SMTP operation (nothing is delivered), but it does not escape: the business operation has already committed, so reporting a cancelled notification as a failed request would misdescribe what happened. D50's `catch (Exception)` already has this property.

**S2-5 — accepted limitation: a failing logger is not guarded.** An exception thrown by `ILogger` itself would escape the boundary. **This exposure is identical to D50's and is accepted deliberately** rather than wrapped in a nested `try`, which would make the one place that reports problems the one place that hides them. Recorded here rather than left implicit.

**S2-6 — three Slice 1 tests were intentionally inverted, not fixed.** `A_refused_connection_propagates_rather_than_being_swallowed`, `Cancellation_is_observed` and `A_missing_inspector_address_fails_rather_than_skipping_silently` asserted propagation, which was *correct* under Slice 1's semantics and is *wrong* under Slice 2's. They now assert swallow-and-log. Not Slice 1 defects.

**S2-7 — transaction ordering was verified and left untouched.** All six handlers already do `business mutation → SaveChangesAsync → audit → notification`, confirmed line-by-line (`CreateLead` 31/33/45, `SubmitAngebotForReview` 39/41/49, `RequestAngebotChanges` 41/43/51, `SendAngebot` 69/75/85, `SendInvoice` 77/83/93, `RecordAngebotDecision` 100/104/112). **No handler sends before its commit; nothing needed correcting.**

**Slice 2 test scope.** Infrastructure: refused connection swallowed+logged; cancellation observed and does not escape; missing Inspector address swallowed+logged; malformed recipient address swallowed+logged; all six notification types swallow a transport failure; the failure log identifies notification + business record; the failure log contains no token, URL, recipient address, SMTP credentials or body; successful delivery produces no `Warning`; a failure is attempted exactly once (no retry, observed via the listener's session count). Application: a handler does not catch notification failures itself; the business work is already committed when the notification fails; `FakeEmailSender` still never throws. A new test-only `ThrowingEmailSender` was added rather than changing `FakeEmailSender`'s established behaviour, and **real SMTP was not enabled in any Development test configuration**.

**Adversarial verification — nine experiments, every one produced observable failures**, then every file was restored byte-identically: catch rethrows (8 failures) · construction moved outside the boundary (2) · null-Inspector guard removed (1) · token logged (2) · recipient address logged (3) · `Warning`→`Information` (6) · retry introduced (1) · notification moved before `SaveChanges` (2) · handler swallows the failure itself (2). Two earlier attempts produced compile errors and were re-run in a form that compiles — a build failure silently leaves the *previous* binary in place, so a "pass" against it proves nothing.

### The Slice 2/3 boundary — RESOLVED (approved 2026-08-09, formerly 3a/3b)

The open question was whether the failure-handling slice's "lifecycle preparation" meant introducing a status type or delivery-record abstraction ahead of the table that stores it. **Resolved as option (i): it does not.**

**Slice 2 is strictly failure handling.** It contains catch / log `Warning` / never rethrow, and nothing else:

- **no** `NotificationDelivery` lifecycle or status type,
- **no** delivery-record abstraction or interface,
- **no** persistence model,
- **no** EF configuration,
- **no** migration and **no** schema change of any kind.

**Do not introduce an abstraction in Slice 2 merely because Slice 3 will need it.** That is `CLAUDE.md` §4's rule applied unchanged — every abstraction in this codebase exists because one specific, real caller needed it at that exact moment, and a type whose only consumer arrives in the next slice does not meet that bar. The same discipline that governs repositories, DTOs and schema governs this, and it is the same reasoning that merged old Slices 1 and 2.

**The lifecycle/status type and the `NotificationDeliveries` persistence model are introduced in Slice 3**, together with their first real usage and migration #9. Slice 3 is therefore where the type, the table, the EF configuration and the status transitions all land at once — which is also what makes it reviewable as a single coherent schema change, the reason the split exists.

**Consequence, stated plainly:** at the end of Slice 2 the system sends real email and can never fail a committed business operation, but a failure is visible **only in the logs**. Admin visibility does not exist until Slice 4, and retry until Slice 5. That is an intended intermediate state, not a gap to be closed early by pulling Slice 3's model forward.

**Slices 1–2 deliver FR-9.1/9.2/9.3 in full.** Slices 3–5 exist solely to satisfy the approved visibility-and-retry requirement, which is the Product Owner's addition beyond the roadmap's original Phase 9 wording.

---

## Slice 1 — approved email copy (FROZEN 2026-08-09)

> **COPY FREEZE.** Implementation must use the wording below **verbatim**. Do not rewrite, shorten, "improve", re-translate, or otherwise alter it while implementing. **If an implementation detail genuinely requires a copy change, STOP and report the exact proposed change and the reason — never make it automatically.**

**Global rules:** German, plaintext only, UTF-8, no HTML, no attachments.

| Value | Rendering | Implementation note |
|---|---|---|
| A date (`DueDate`) | `31.08.2026` | `ToString("d", CultureInfo.GetCultureInfo("de-DE"))` |
| A money amount (`GrossAmount`) | `1.234,56 €` | `ToString("C", de-DE)`. **Correction (verified during Slice 1 implementation):** the design review claimed the space before `€` is U+00A0. It is not — on this runtime .NET renders a **plain space, U+0020**, confirmed by char-code inspection and by an exact-string test. Assertions use a normal space |

Every placeholder is sourced from its notification record, **except** `{FromDisplayName}`, which comes from `Email:FromDisplayName` configuration. Internal ids (`LeadId`, `AngebotId`, `InvoiceId`, `InspectorId`) are deliberately never rendered — they mean nothing to a reader.

### 1. New website Lead → Admin recipients

`NewWebsiteLeadNotification(LeadId, LeadName, LeadPhone, LeadEmail)`

**Subject:** `Neue Anfrage über die Website: {LeadName}`

```
Über das Kontaktformular der Website ist eine neue Anfrage eingegangen.

Name:     {LeadName}
Telefon:  {LeadPhone}
E-Mail:   {LeadEmail}

Die Anfrage wurde als neuer Lead im Dashboard angelegt.

Diese E-Mail wurde automatisch erzeugt.
```

Placeholders: `{LeadName}` ← `LeadName`, `{LeadPhone}` ← `LeadPhone`, `{LeadEmail}` ← `LeadEmail`. **No link** — no dashboard base URL exists in configuration and none is invented.

### 2. Angebot submitted for review → Admin recipients

`AngebotSubmittedForReviewNotification(AngebotId, AngebotNumber, LeadId)`

**Subject:** `Angebot {AngebotNumber} wartet auf Prüfung`

```
Ein Angebot wurde zur internen Prüfung eingereicht.

Angebot: {AngebotNumber}

Es kann jetzt im Dashboard geprüft, freigegeben oder zur Überarbeitung
zurückgegeben werden.

Diese E-Mail wurde automatisch erzeugt.
```

Placeholders: `{AngebotNumber}` ← `AngebotNumber`.

### 3. Angebot decision → Admin recipients

`AngebotDecisionNotification(AngebotId, AngebotNumber, LeadId, LeadName, Approved)` — `Approved` selects the variant.

**3a — `Approved == true`. Subject:** `Angebot {AngebotNumber} wurde angenommen`

```
Der Kunde hat das Angebot angenommen.

Angebot: {AngebotNumber}
Kunde:   {LeadName}

Diese E-Mail wurde automatisch erzeugt.
```

**3b — `Approved == false`. Subject:** `Angebot {AngebotNumber} wurde abgelehnt`

```
Der Kunde hat das Angebot abgelehnt.

Angebot: {AngebotNumber}
Kunde:   {LeadName}

Diese E-Mail wurde automatisch erzeugt.
```

Placeholders: `{AngebotNumber}` ← `AngebotNumber`, `{LeadName}` ← `LeadName`. **No rejection reason, and no wording hinting at one** — FR-6.3's optional reason is deliberately not accepted or stored (Phase 6), and a test pins that.

### 4. Angebot changes requested → Inspector

`AngebotChangesRequestedNotification(AngebotId, AngebotNumber, Comment, InspectorId)`

**Subject:** `Änderungswünsche zu Angebot {AngebotNumber}`

```
Zu Ihrem Angebot {AngebotNumber} wurden Änderungen angefordert.

Anmerkung:
{Comment}

Das Angebot ist im Dashboard wieder bearbeitbar.

Diese E-Mail wurde automatisch erzeugt.
```

Placeholders: `{AngebotNumber}` ← `AngebotNumber`, `{Comment}` ← `Comment` (the Admin's own words, verbatim). The opening line is fixed as written; **do not reword it.** Recipient address is resolved by the Infrastructure lookup (D1).

### 5. Angebot ready → Lead / customer

`AngebotReadyNotification(AngebotId, AngebotNumber, RecipientName, RecipientEmail, Token)`

**Subject:** `Ihr Angebot {AngebotNumber}`

```
Guten Tag {RecipientName},

vielen Dank für Ihr Interesse. Ihr Angebot {AngebotNumber} steht für Sie bereit.

Sie können es hier ansehen und direkt zu- oder absagen:
{AngebotUrl}

Der Link ist persönlich für Sie bestimmt – bitte geben Sie ihn nicht weiter.

Mit freundlichen Grüßen
{FromDisplayName}
```

Placeholders: `{RecipientName}` ← `RecipientName`, `{AngebotNumber}` ← `AngebotNumber`, `{AngebotUrl}` ← `{TokenLink:PublicBaseUrl}/angebot/{Token}` composed in Infrastructure (D4.1, D5), `{FromDisplayName}` ← configuration.

**"Guten Tag {RecipientName}," is deliberate** and must not become "Sehr geehrte Frau…"/"Sehr geehrter Herr…": nothing on the record carries a title, and deriving one from a name would misgender real customers. The personal-link warning stays. **No validity period.**

### 6. Invoice ready → Customer

`InvoiceReadyNotification(InvoiceId, InvoiceNumber, RecipientName, RecipientEmail, GrossAmount, DueDate, Token)`

**Subject:** `Ihre Rechnung {InvoiceNumber}`

```
Guten Tag {RecipientName},

Ihre Rechnung {InvoiceNumber} steht für Sie bereit.

Rechnungsbetrag: {GrossAmount}
Fällig am:       {DueDate}

Sie können die Rechnung hier ansehen:
{InvoiceUrl}

Mit freundlichen Grüßen
{FromDisplayName}
```

Placeholders: `{RecipientName}` ← `RecipientName`, `{InvoiceNumber}` ← `InvoiceNumber`, `{GrossAmount}` ← `GrossAmount` rendered `1.234,56 €`, `{DueDate}` ← `DueDate` rendered `31.08.2026`, `{InvoiceUrl}` ← `{TokenLink:PublicBaseUrl}/invoice/{Token}`, `{FromDisplayName}` ← configuration.

**The customer-facing German word is "Rechnung" while the URL path stays `/invoice/{token}`.** That asymmetry is intended: the path is the locked technical contract (D4.1), the copy is customer-facing German. **Do not change the path to `/rechnung/`.**

**Deliberately absent, and must not be added:** bank details, IBAN/BIC, any payment instruction, any VAT rate, any PDF attachment or claim of one. `Fällig am` states the `DueDate` field as data; it is not a payment instruction.

---

## Open items to settle at each slice's design review

Not decided here, and deliberately not invented:

- Exact column names and types for `NotificationDeliveries` (derived from `ERD.md` conventions; reviewed before the migration). **The table name itself is settled — `NotificationDeliveries`, chosen deliberately over `Notifications` so the name states that this is an Infrastructure-level delivery record and not a Domain "Notification" aggregate.** *(Slice 3)*
- The concurrency mechanism preventing an Admin double-click from double-sending. *(Slice 5)*
- How `AngebotChangesRequestedNotification` identifies *which* review comment to reconstruct when an Angebot has several (its `Comment` is re-derived, and "the latest" is an assumption, not a rule). *(Slice 5)*

**Not Product Owner decisions — engineering design, to be settled inside the slice that needs them:**

- **The transport test mechanism** belongs in **Slice 1's own design review**, not on this list as a question to be answered first. The current recommendation is an **in-process SMTP listener**, because Docker is unavailable on the development machine and neither CI job provides an external SMTP server.
- **A single manual real-send** is **not a Slice 1 blocker.** It is carried as a Phase 9 **completion/release-gate** question, to be revisited at the completion gate (Slice 6) alongside D4.2's readiness constraint.

**Settled by the Slice 1 design review (2026-08-09), no longer open:** merged Slice 1/2 scope; `Email:Enabled` semantics; plaintext-only; recipient/token/URL logging prohibitions; SMTP credential pairing rules; Inspector address resolution (D1), null-address handling (D2), `IsActive` (D3), the `TokenLink:PublicBaseUrl` key and URL shape (D4.1), the Phase 13 sequencing constraint (D4.2), and **Infrastructure ownership of URL construction (D5)**.

**Nothing on this list blocks Slice 1's implementation.** The three remaining open items belong to Slice 3 and Slice 5.

---

## Slice log

*(empty — implementation has not started)*
