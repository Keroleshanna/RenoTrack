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

### Slice 3 — approved decisions (2026-08-09, before implementation)

**S3-1 — `AttemptCount` and `LastAttemptAt` are included in Slice 3, not deferred.** D69 and the committed `ERD.md` row both define this record as answering *"when it was last attempted"* and *"how many attempts have occurred"*. In Slice 3 those values are `1` and the attempt timestamp — **real historical facts about what happened, not scaffolding for Slice 5.** Deferring them would also have cost a migration #10, since a column (unlike a string-converted enum value) cannot be added for free. **They are part of the approved record itself.**

**S3-2 — `FailureType` holds the exception type name; `FailureMessage` holds an application-authored, sanitized description — never raw exception text.** The design review established that MailKit surfaces the SMTP server's own reply in `SmtpCommandException.Message`, and real servers routinely echo the recipient address (`550 5.1.1 <…>: Recipient address rejected`). Persisting that would put third-party-controlled text, and PII we did not choose to place there, into the database.

**Forbidden in `FailureMessage`:** `exception.ToString()`, stack traces, raw SMTP server response text, tokens, URLs, credentials, message subject or body. **The full original exception remains available through Slice 2's `Warning` log**, which already attaches it — the database records *what kind of thing went wrong*, the log records *exactly what happened*.

**The smallest practical category set is three**, derived from the delivery path that actually exists rather than from a general taxonomy:

| Category | When | Application-authored message |
|---|---|---|
| Preparation | recipient resolution or message construction failed | "The notification could not be prepared." |
| Transport | connect, authenticate or send failed | "The mail server could not be reached or rejected the message." |
| Cancelled | the operation was cancelled | "Delivery was cancelled before it completed." |

**Why three and not more:** splitting Preparation into "recipient unavailable" and "message construction failed" adds nothing, because `FailureType` already distinguishes them for free (`InvalidOperationException` vs MimeKit's parse exception) and the log carries the rest. Splitting Transport by SMTP reply code would be exactly the large taxonomy this decision forbids. Cancellation stays separate because S2-4 already treats it as its own case and an Admin would read it differently from a rejection.

**Classification is by delivery phase, not by exception type** — a local phase marker inside the single guarded region, read when the exception is caught. Type-matching would be a heuristic (`InvalidOperationException` is thrown both by the recipient guard and by the security-mode switch); the phase is exact, needs no new exception type, and keeps S2-2's one-guarded-region rule intact.

**S3-3 — `Recipient` is nullable (approved 2026-08-09, after a collision found while tracing the delivery path).**

Three approved instructions could not all hold: `Recipient` required, the `Pending` row inserted *before* the SMTP attempt, and preparation failures persisted. For five of the six notifications the recipient is known before any work (configured Admin list, or `RecipientEmail` on the record); for `SendAngebotChangesRequestedNotificationAsync` it is produced by `InspectorEmailLookup` **inside** the guarded region, and a null result throws there. So at insert time the address is genuinely unknown for that one path — a `NOT NULL` column would have made a recipient-resolution failure impossible to record.

**Persisted meaning:**

- `Recipient` holds the **actual resolved destination address** whenever one was available — including when delivery later failed.
- `Recipient` is `NULL` **only** when delivery failed before a recipient could be resolved.
- **No sentinel** — never `"(unresolved)"`, `"unknown"`, or any other non-address value in an address column.

**Row shape for a preparation failure occurring before recipient resolution:** `Status = Failed`, `Recipient = NULL`, `AttemptCount = 1`, `LastAttemptAt` = the attempt timestamp, the `Preparation` failure category recorded per S3-2, and the original technical exception left **only** in the Slice 2 log.

**Why Option 2 was rejected.** The alternative kept `Recipient` `NOT NULL` by resolving the address *before* inserting the row, which would have meant recipient-resolution failures were logged but never persisted. That inverts Slice 3's entire justification: D69 exists because a failure must stop being invisible, and D2 singled out the missing Inspector address as the failure that must never be silent. Keeping a column non-nullable at the price of making that one failure invisible was not a trade worth taking. One nullable column with a documented meaning is honest about what happened.

**S3-4 — the S3-2/S3-3 wording collision over `FailureType`: RESOLVED in favour of S3-2 (approved 2026-08-09).** S3-2 assigned `FailureType` the **exception type name**; the S3-3 instruction briefly assigned it the delivery **category** instead. One column cannot hold both. **S3-2 stands unchanged:**

- **`FailureType`** → the exception type name (`InvalidOperationException`, `SmtpCommandException`, …). Library- or self-authored identifiers, never third-party reply text, so this carries no PII risk.
- **`FailureMessage`** → the application-authored sanitized operational message, selected by the approved delivery-phase category (`Preparation` / `Transport` / `Cancelled`). The category is a **classification used to choose the message**, not a persisted value of its own.

**Why this reading and not the other:** S3-2's justification for three categories rather than four depends on `FailureType` carrying the exception type — that is what keeps a recipient-unavailable failure distinguishable from a message-construction failure in the database (`InvalidOperationException` vs MimeKit's parse exception). Reassigning the column to the category would have collapsed those two and reopened the three-vs-four question for no gain.

**Never persisted, in either column:** raw `exception.Message`, `exception.ToString()`, stack traces, raw SMTP server responses, recipient addresses or other PII incidentally contained in third-party exception text, tokens, URLs, credentials, message subject or body. The full technical detail stays in Slice 2's `Warning` log, which already attaches the exception.

**No `FailureExceptionType` column, no fourth failure column, no taxonomy expansion.** Two failure columns, three categories, full stop.

### Slice 3 — implementation record (2026-08-10)

**Delivered:** `NotificationDelivery` entity + `NotificationType` and `NotificationDeliveryStatus` enums (`Persistence/Entities/`), `NotificationDeliveryConfiguration`, one `DbSet`, migration **#9 `AddNotificationDeliveries`**, and the persistence integration inside `SmtpEmailSender`. **9 migrations** total; `has-pending-model-changes` reports none.

**Three-way review performed before generating the migration** (`CLAUDE.md` §21): entity ↔ configuration ↔ committed `ERD.md`. All nine questions D69 fixes map to exactly one column each, with none left over. **The generated migration was inspected manually** and is additive only — one `CreateTable`, two `CreateIndex`, no FK, no unique constraint, no alteration to any existing table, `Down` drops the single table.

**Delivery flow, as built:** handler commits → `Pending` row inserted (`AttemptCount = 1`, `LastAttemptAt = CreatedAt`) → message prepared → recipient recorded from the addresses actually on the message → SMTP attempt → terminal `Sent` or `Failed`. The Slice 2 boundary is unchanged and still swallows everything. The terminal write is a separate step using `CancellationToken.None`, so a cancelled request is still recorded; its own failure is swallowed and logged, leaving the row `Pending` rather than escaping.

**Tests: 1,421 passing, 0 failing** (Domain 332, Application 422, Infrastructure **324**, Api 343) — **+22** over Slice 2's 1,399, all in Infrastructure. Release build 0 warnings / 0 errors.

**Adversarial verification — nine experiments, every one produced observable failures**, then all three touched files were restored byte-identically (verified with `diff -q`, and no `adversarial:` residue remains): Pending-inserted-after-send (1) · failure never recorded (4) · raw `exception.Message` persisted (4) · recipient never recorded (2) · `AttemptCount = 0` (5) · `LastAttemptAt` never set (7) · `MarkSent` leaves `Pending` (5) · `Status` index removed (1) · `Invoice` recorded as `Angebot` (1). Every experiment was confirmed to **compile** before its test run, per the Slice 2 methodological note.

**Two deviations found during implementation, both corrected in place, neither a design change:**

1. **A stale doc comment on `SmtpEmailSender`** still claimed "nothing is persisted … until Slice 3 lands", which Slice 3 made false. Rewritten to describe what the class now does. The same applied to `SmtpEmailSenderTests`' class comment, which still described Slice 1 semantics.
2. **A test of mine had wrong arithmetic** — `new string('a', 300) + "@example.invalid"` is 316 characters, under the 320-character column, so the over-length test passed for the wrong reason. Corrected to 336. **The production code was never at fault**; the test was.

**S3-5 — `Recipient` is a recipient *set*, `nvarchar(1000)`, and `Email:AdminRecipients` is validated against the persisted representation (approved 2026-08-10).** This resolves a contradiction found after the first implementation pass.

- **`Recipient` persists the complete resolved recipient set as delivered**, not necessarily a single address. Multiple addresses are joined with `", "` — the same separator the sender uses, now a single shared constant (`NotificationDelivery.RecipientSeparator`) so the value persisted, the value built, and the value measured cannot drift apart.
- **`nvarchar(1000)` is intentional.** Three of the six notifications go to the configured Admin list, so this column was never holding one address.
- **320 was never an approved constraint.** It appears in **no** committed document — not `ERD.md`, not `ARCHITECTURE_DECISIONS.md`, not `Architecture.md`. It came from carrying over the single-address convention used by `Leads.Email` and `Customers.Email`, which was wrong for a field representing a set. Correcting it therefore contradicts nothing that was approved.
- **`Email:AdminRecipients` is now validated at startup against the exact persisted representation** (`NotificationDelivery.MaxRecipientLength`), naming the key and reporting the actual length.
- **An over-limit configuration fails at startup**, not at runtime. It cannot become a `Pending` persistence failure — which matters because the row that would fail to insert *is* the delivery record, so a successfully-sent email would otherwise be recorded forever as an unresolved attempt.
- **No truncation.** A shortened recipient list is a wrong answer to "who was this sent to?", not a smaller one.
- **No one-row-per-recipient redesign.** That would change the record's meaning from "one notification attempt" to "one recipient attempt" and is well beyond Slice 3.
- **Migration #9 was regenerated, not supplemented.** It had been applied nowhere, so `dotnet ef migrations remove` followed by a clean `add` was safe (`CLAUDE.md` §21). **There is no migration #10.**

**Capacity, calculated exactly** (*n* addresses of length *L* joined with a two-character separator occupy `n·L + 2(n−1)`, which must be ≤ 1000): **45** addresses of 20 characters, **37** of 25, **31** of 30, **23** of 40, **19** of 50. Each figure was verified by checking that *n* fits and *n+1* does not — which caught two errors in the process: the investigation report's "24 of 40" (it is 23), and a first draft of this line claiming "18 of 50" (it is 19).

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

| Slice | Status | Commit | Notes |
|---|---|---|---|
| 1 — Email configuration + SMTP sender + six German templates | **Complete** | `7b70fd4` | MailKit 4.17.0, `EmailOptions`, `EmailSecurityMode`, `EmailMessageFactory`, `SmtpEmailSender`, `EmailConfigurationVerifier`, `InspectorEmailLookup`, DI selection, startup readiness verification, `TokenLink:PublicBaseUrl` validation, in-process SMTP test listener. **Application unchanged** — `IEmailSender` and all six notification records untouched |
| 2 — Safe failure handling | **Complete** | `1d2ca22` | One failure boundary inside `SmtpEmailSender.DeliverAsync`, covering recipient resolution, message construction and SMTP transport. Catch → `LogWarning` with the original exception → never rethrow. Cancellation swallowed. No handler catches notification failures |
| 3 — Notification persistence | **Complete** | `06f7711` | `NotificationDeliveries` + EF configuration + **migration #9**, `NotificationType`/`NotificationDeliveryStatus`, write-after-commit. Recipient stored as the complete joined set (`", "`, max 1000 chars, validated at startup against the same shared constants) — a design contradiction found and corrected during the slice, since the original 320 was a single-address limit copied onto a column that holds an Admin recipient *set* |
| 4 — Admin visibility | **Complete** | `0198d43` | `GET /api/v1/notification-deliveries`, Admin-only. See the Slice 4 record below |
| 5 — Manual retry | **Complete** | `6bdde9d` | `POST /api/v1/notification-deliveries/{id}/retry`. See the Slice 5 implementation record below |
| 6 — Reconciliation + completion gate | **Complete** | *(this commit)* | Cross-document audit, stale-state corrections, adversarial re-verification, and one test-only correction found by mutation testing. **No production code, schema or migration changed.** See the completion record below |

### Slice 4 — approved decisions (2026-08-12, before implementation)

1. **Infrastructure-owned on both sides.** `INotificationDeliveryQueries`, `NotificationDeliveryQueries` and `NotificationDeliveryDto` all live in `RenoTrack.Infrastructure.Persistence.Queries`, and `NotificationDeliveriesController` consumes the interface directly. **No Application notification-persistence abstraction**, and `NotificationType`/`NotificationDeliveryStatus` are neither moved nor duplicated into Application. The precedent is D60 (`AuthController`), applied on its own test: no aggregate, no Domain invariant, no state transition, no audit milestone. Recorded permanently in `CLAUDE.md` §11.
2. **Default status filter: omitted returns every status, `Sent` included.** §9's "failed/pending" wording is not a mandate to hide successes — an Admin needs to confirm a delivery eventually succeeded, which Slice 5's retry makes essential.
3. **No `entityType`/`entityId` filter.** Growth on demand (`CLAUDE.md` §4). The `(EntityType, EntityId)` index remains correct and does not oblige an API filter.
4. **Endpoint `GET /api/v1/notification-deliveries`** — kebab-case literal route, matching `CatalogItemsController`, since `[controller]` would render this multi-word resource as `/NotificationDeliveries`.
5. **Ordering `CreatedAt DESC, Id DESC`**, deterministic across pages. The tiebreaker is load-bearing rather than defensive here: a burst can write several rows within one `datetime2` value.
6. **Pagination follows `LeadQueries`/`GetLeadsQuery` exactly** — `Pagination` constants, count before paging, `PagedResult<T>`. Required because D70 defines **no retention policy**, so the table only grows.
7. **All twelve persisted columns exposed.** Nothing here is secret by construction (D69 already bars tokens, bodies, credentials and raw exception text). `Recipient = NULL` serializes as JSON `null` — no sentinel, no empty string. `FailureType`/`FailureMessage` are exposed **as stored, with no second sanitization layer**. `EntityType`/`EntityId` stay flat: no Lead/Angebot/Invoice/User is reloaded and no label or link is synthesized.
8. **`[Authorize(Roles = Roles.Admin)]`, no ownership validation** — §9 marks both actions `F`, so per `CLAUDE.md` §16 an `IOwnershipValidator` call would be a semantic error, not merely redundant. Inspector → 403, anonymous → 401. Being single-role, there is no scope to derive and therefore no fall-through that could fail open.
9. **No migration.** Migration #9 already provides the table and both indexes.

### Slice 4 — implementation record (2026-08-12)

- **Shape validation uses DataAnnotations, not FluentValidation** — a deliberate, narrow deviation. FluentValidation is an Application-layer package and `RenoTrack.Infrastructure` does not reference it; adding it for two integer bounds would be a heavier change than the thing validated. `[ApiController]` renders a violation as the same RFC 7807 `ProblemDetails` with a field-keyed `errors` dictionary that FluentValidation failures produce, so the wire contract is uniform even though the mechanism differs.
- **No `IsInEnum()` equivalent on `status`, and the absence is verified rather than assumed.** The design review expected one to be necessary, on the historical MVC behaviour of binding an undefined *numeric* value (`?status=99`) to an enum without complaint — a nonsense filter answered with a cheerful empty page instead of a 400. An `[EnumDataType]` attribute was written for it, then **deleted**: removing it adversarially left both bad shapes (`NotAStatus`, `99`) still returning 400, so on this runtime the binder refuses them unaided and the attribute was decoration. Both shapes stay pinned by `Rejects_an_invalid_status`, which asserts the **behaviour**, not the mechanism — a future runtime that loosened the binder fails that test rather than silently accepting garbage.
- **`DependencyInjectionTests` gained an explicit resolution test.** Its reflection-driven theories discover types in the **Application** assembly only, so they cannot cover an interface declared in Infrastructure — the "forgot to register it" safety net does not extend here, and an explicit assertion replaces it.
- **Tests: 24 new — 7 Infrastructure, 17 Api (16 endpoint cases + 1 DI resolution).** Suite total 1,430 → 1,454, none failing, none skipped. The ordering test forces three rows to share one `CreatedAt` via `ExecuteUpdateAsync`, because the tiebreaker is otherwise untestable: rows created microseconds apart would pass with or without it.

---

### Slice 5 — approved decisions (2026-08-12, before implementation)

Approved from the read-only Slice 5 design investigation. **Not yet implemented.**

**S5-1 — Infrastructure-owned retry service.** `IEmailSender` and all six notification records are **unchanged**, and no Application notification-persistence abstraction is introduced. A new Infrastructure-owned service loads the existing `NotificationDelivery`, claims it atomically, reconstructs the notification from persisted business data, resolves the current recipient, and drives the existing delivery machinery **against the existing row**. It never creates a second row, and it never re-executes the business operation.

> **Why a service rather than "call `IEmailSender` again", stated plainly:** `SmtpEmailSender.DeliverAsync` unconditionally constructs a new `NotificationDelivery`, and `IEmailSender` has no parameter — and could have none, without breaking D23/D69 — representing an existing row. Calling it again would leave the original row `Failed` forever and pin `AttemptCount` at 1, contradicting both D70 and the entity's own doc comment. This was found by tracing the delivery path, not by reading the decision records.

**S5-2 — Concurrency is one conditional compare-and-set, no model change.** The claim is a single `ExecuteUpdateAsync`, conditional on the current status, setting `Status = Sending`, `AttemptCount = AttemptCount + 1`, `LastAttemptAt = now`. **Only the caller whose update affects a row owns the attempt**; zero rows affected means another request claimed it first, which surfaces as **409**. Atomic at the database, bypasses the change tracker, and is still EF Core LINQ — the same shape and the same justification as `TokenService.RevokeAllForUserAsync`, so D52's narrowly-scoped raw-SQL exception does not come into play. **No `IsConcurrencyToken`, no migration, no lock, no queue, no hosted service, no worker, no Polly.**

**S5-3 — Retryable statuses are `Failed`, `Pending` and `Sending`. `Sent` never is.** The lifecycle becomes:

```
Pending → Sending → Sent | Failed
Failed  → Sending → Sent | Failed
Pending → Sending             (stranded initial attempt)
Sending → Sending             (stranded retry attempt)
```

`Sending` is retryable **because** there is deliberately no lease, timeout or background recovery: a process that dies mid-attempt strands a row there, and an Admin must be able to recover it by hand. Duplicate delivery is the already-accepted consequence of D69/D70. **This is manual recovery, not automatic recovery** — nothing polls, nothing schedules, nothing re-enters the path unattended.

**S5-4 — `AngebotChangesRequested` reconstructs the latest review comment** for that Angebot, ordered `CreatedAt DESC, Id DESC`. **Accepted historical imprecision, recorded deliberately:** retrying an older changes-requested notification after further review cycles have occurred will send the *newest* comment, not the one the original attempt carried. No `CommentId` column, no `EntityType`/`EntityId` change, no comment text in the delivery row, no migration. **Do not silently "fix" this by adding schema** — revisit only on a real incident.

**S5-5 — The recipient is always re-resolved**, never read back from the persisted `Recipient`. Configuration and user data may have changed, the retry model *is* reconstruction, and — decisively — `Recipient` is `NULL` precisely when resolution failed, which is the case retry most needs to serve. The newly resolved set is written to the existing row, consistent with `LastAttemptAt`/`AttemptCount` already meaning "the latest attempt".

**S5-6 — Business-state protection: refuse, never repair.**
- **`AngebotReady` / `InvoiceReady`:** **no new token is ever generated.** If the token link is missing, expired, or already used, the retry is **refused with 409** and an application-authored reason. Minting a fresh link would be a new business action, which D70 forbids outright.
- **`InvoiceReady`:** additionally refused with 409 when the Invoice is now `Void` or `Paid`.
- **Internal notifications** (`NewWebsiteLead`, `AngebotSubmittedForReview`, `AngebotChangesRequested`, `AngebotDecision`): **no invented staleness rules.** Retry operates on the current reconstructible state; a submitted-for-review notice for an Angebot since approved is stale but harmless, and no business rule says otherwise.

**S5-7 — `POST /api/v1/notification-deliveries/{id}/retry`.** Admin only, no request body (id from the route, Admin from the token per D61), no ownership validation (§9 is `F`, so per §16 an `IOwnershipValidator` call would be a semantic error). **404** unknown id; **409** for `Sent`, for a lost claim race, and for every S5-6 refusal; **200** with the updated delivery representation once the attempt reaches its terminal state. RFC 7807 throughout, per the existing `ProblemDetails` contract.

**S5-9 — With `Email:Enabled = false`, retry is refused with 409, not 503.** The delivery record exists and retry is a valid operation in principle; what forbids it is the application's own configuration, which is a state conflict rather than a transient outage. **The retry refusal contract is therefore uniform — every refusal is 409:** email disabled, already `Sent`, a non-retryable state, a claim lost to another Admin, an expired or already-used token, and a `Void`/`Paid` Invoice.

**S5-8 — TokenLink lookup is deterministic, never `SingleAsync`.** Ordered `CreatedAt DESC, Id DESC`, taking the first. In practice at most one link exists per entity — `Angebot.Send()` guards `ApprovedInternally`, `Invoice.Send()` guards `Draft` — but **no database constraint enforces that**, and `SingleAsync` would turn a violated assumption into an unmapped 500. No uniqueness migration is added.

#### Slice 5 — pre-implementation consistency check (2026-08-12)

Checked across `NotificationDeliveryStatus`, `NotificationDelivery`, D69/D70, `ERD.md`, `PermissionMatrix.md`, the Slice 3 decisions, Slice 4's endpoint and query, the current `SmtpEmailSender`, the retry endpoint design, and the existing tests.

**Verified clean — none of the seven prohibited consequences is introduced:**

- **No automatic recovery, background processing, retry loop, queue or lease.** Every state change originates in one Admin HTTP request; `Sending → Sending` is reachable only by a human issuing a second request, and nothing re-enters the path unattended.
- **No migration.** `Sending` is a new member on a string-converted column (`HasConversion<string>().HasMaxLength(50)` — comfortably wider than the value), so the EF model is unchanged and `has-pending-model-changes` stays clean. Confirmed against `NotificationDeliveryConfiguration`.
- **No second delivery row.** The claim updates in place; no code path on the retry side inserts.
- **`NotificationDelivery` needs no new mutator for the claim** — `ExecuteUpdateAsync` writes `Status`/`AttemptCount`/`LastAttemptAt` at the database. `MarkSent`/`MarkFailed` remain correct terminal transitions, and neither touches `AttemptCount`, so the claim's increment survives.
- **Slice 4 absorbs `Sending` with no change.** `?status=` binds the enum by name, so `?status=Sending` starts working automatically; the default (no filter) already returns every status; `Rejects_an_invalid_status` uses `NotAStatus` and `99`, neither of which a fourth member makes valid.
- **`PermissionMatrix.md` §9 already grants "Retry a notification — Admin `F`, Inspector `—`"** with the no-ownership note. No permission change is required.
- **The Slice 2 failure boundary is preserved:** the retry path must fail the same way — logged with the original exception, terminal state persisted, never escaping as a 500. `A_failure_is_not_retried` stays valid because it pins the *automatic* path; manual retry is a separate, human-initiated entry point.

**Three implementation items, all now resolved by approved decisions.**

1. **Registration under `Email:Enabled = false` — resolved.** `SmtpEmailSender`, `EmailMessageFactory` and `InspectorEmailLookup` are registered **only when email is enabled**; otherwise `LoggingNoOpEmailSender` resolves and none of the delivery machinery exists in the container. **Both test projects and every non-production host run in that state** — `appsettings.json` ships `Enabled: false`. A retry service registered only when enabled would make `NotificationDeliveriesController` unconstructable and **break Slice 4's `GET` endpoint and `ValidateOnBuild` together**. **The retry abstraction is therefore registered unconditionally**, and the service itself reads `Email:Enabled` and refuses with S5-9's 409 before any SMTP work. **No fake or second sender is introduced** — `EmailOptions` is already registered unconditionally as a singleton *before* the `Enabled` branch, so the guard needs nothing new. The `IEmailSender` selection itself is untouched, exactly as Slice 1 established.
2. **Reaching the delivery machinery — resolved: minimal internal entry point.** The Slice 2 `DeliverAsync` **remains the single failure boundary**; an `internal` retry entry point on `SmtpEmailSender` accepts the existing row, and `SmtpEmailSender` is registered as a concrete type **only in the enabled branch**, purely so the retry service can reach it. No dispatcher extraction, no broad refactor, `IEmailSender`'s six public methods and all six notification records unchanged. Because the concrete registration exists only when enabled, the unconditionally-registered retry service resolves it **after** its own enabled-guard has proven it is there — the narrow, fixed, named-type resolution `CLAUDE.md` §21 sanctions, not an open-ended service locator.
3. **Claim-before-load ordering — resolved and must be tested.** The flow is fixed: validate access → **claim atomically** → *then* load the row → reconstruct → re-resolve the recipient → deliver on the existing row → persist `Sent`/`Failed`. `ExecuteUpdateAsync` bypasses the change tracker, so an entity loaded *before* the claim holds a stale `Status`/`AttemptCount`, and a later `SaveChangesAsync` would write the stale count back, **silently undoing the increment**. Same class of hazard as D55 and the `AuditService` scoping bug. **A behavioural test must prove the claim's increment survives the terminal update** — a comment is not sufficient.

#### Slice 5 — locked scope (confirmed 2026-08-12)

Present in the design: Infrastructure-owned retry service; existing-row retry; CAS claim; `Sending`; retryable `Failed`/`Pending`/`Sending`; non-retryable `Sent`; manual recovery of a stranded `Sending`; latest review comment; recipient re-resolution; token and Invoice staleness protection; `POST /api/v1/notification-deliveries/{id}/retry`; Admin-only authorization; uniform 409 conflict contract.

Absent from the design, deliberately and verifiably: no new migration; no second delivery row; no automatic recovery; no queue; no hosted service; no background worker; no Polly; no Application notification-persistence abstraction.

**S5-10 — staleness is validated *before* the claim; a refused retry mutates nothing (approved 2026-08-12, resolving an implementation gap).**

The first Slice 5 implementation claimed the row, then discovered staleness, then marked the row `Failed` and returned 409. **That was not covered by S5-1 … S5-9, and it was wrong on four counts** — found by review, not by a failing test:

1. **It gave `Failed` a second meaning.** `MarkFailed` is documented as *"the attempt ended without delivery"*; a refusal is an attempt that never happened.
2. **It broke S3-2.** `FailureMessage` is restricted to three application-authored category messages (`Preparation` / `Transport` / `Cancelled`) — *"two failure columns, three categories, full stop."* Free-form refusal text was a fourth kind.
3. **It broke S3-4.** `FailureType` is *the exception type name* of an exception that actually ended a delivery attempt. `nameof(ConflictException)` named an exception thrown afterwards to the HTTP caller, which never participated in a delivery.
4. **It made a permanently-invalid notification permanently retryable.** S5-3 makes `Failed` retryable, so an expired token or a `Void` Invoice became a row an Admin could retry forever, incrementing `AttemptCount` on every refusal, with no possible success.

**The resolution — Option A.** `NotificationRetryExecutor.ValidateAsync` performs the staleness checks **read-only, before the compare-and-set claim**. A refusal therefore leaves the row untouched: no `Status`, no `AttemptCount`, no `LastAttemptAt`, no `FailureType`, no `FailureMessage`, no `Recipient`, no `SentAt`. `Failed` keeps its single approved meaning, S3-2 and S3-4 stand unchanged, and no new status was invented.

**This does not violate S5-2's "claim first, then load".** That rule exists to stop a *tracked* entity loaded before the claim from writing a stale `AttemptCount` back. The pre-claim read is an `AsNoTracking` projection of two columns (`NotificationType`, `EntityId`) — nothing tracked, nothing saved. The entity itself is still loaded only after a successful claim, and the regression test proving it still passes.

**Rejected alternatives**, recorded so they are not revisited by accident: leaving a refused row `Sending` (the row would claim an attempt was in flight when none was, and would still be retryable); adding a terminal `Refused`/`Undeliverable` status (a new lifecycle state, and the locked design deliberately has none); releasing the claim by restoring the prior status (a second write, and a window where the row is briefly `Sending` for no reason); and ratifying the original behaviour by amending S3-2/S3-4 (which would have widened the failure taxonomy this project spent Slice 3 narrowing).

**Consequence, stated plainly:** a permanently-invalid notification can still be retried repeatedly, and each attempt is refused with 409 — but every one of those refusals is a pure no-op. Nothing accumulates and nothing is corrupted. Making such a row *un*-retryable would need the terminal status rejected above; that is a live option if a real incident ever justifies it.

#### Slice 5 — implementation record (2026-08-12)

Built exactly to S5-1 … S5-9. `IEmailSender`, the six notification records and every handler are unchanged; no Application file was touched; **no migration** (drift verified clean).

- **`DeliverAsync` gained an optional existing row rather than a sibling.** `internal RetryAsync` forwards into it, so the Slice 2 `try`/`catch` remains literally the single failure boundary and retry inherits its semantics because it *is* that code, not because two paths were kept in step by hand. The retry path never calls `Add`.
- **Two halves, split exactly where the container splits.** `INotificationRetryService` is registered **unconditionally** (it holds the `Email:Enabled` guard and the CAS claim); `NotificationRetryExecutor` and the concrete `SmtpEmailSender` are registered **only when email is enabled**, and the service resolves the executor from `IServiceProvider` *after* its guard has proven it exists — the narrow, fixed, named-type resolution `CLAUDE.md` §21 sanctions. No fake sender was introduced.
- **A staleness refusal mutates nothing at all (S5-10).** `ValidateAsync` runs read-only before the claim and *returns* a reason rather than throwing; the service turns it into a 409 without touching the row. After the claim there is deliberately **no second refusal path**: business state that changed since validation surfaces inside the Slice 2 boundary as an ordinary preparation failure, carrying the approved category message and the real exception type — so no code path can invent a failure category or a synthetic `FailureType`.
- **Every read used to rebuild a message happens *inside* the delivery boundary.** That is load-bearing rather than tidy: a reconstruction read that threw outside the boundary would escape as an unmapped 500 instead of being recorded on the row.
- **`AttemptCount` is incremented from the column** (`d => d.AttemptCount + 1`), never from a value read earlier — correct under concurrency rather than merely usually correct.
- **A successful retry is unreachable from `Api.Tests`** because that host runs with email disabled, and it is *not* faked to make it reachable. Delivery outcomes are proven for real over a socket in `NotificationRetryServiceTests`; `Api.Tests` owns what the API layer adds (routing, the role gate, 404, ProblemDetails).
- **Tests: 31 new — 23 Infrastructure, 8 Api.** Suite total 1,454 → 1,485, none failing, none skipped.

**Adversarial verification — each load-bearing guard removed, rebuilt, and re-run:**

| Guard removed | Result |
|---|---|
| `AttemptCount + 1` → `AttemptCount` | **3 failures**, including the concurrency and no-loop tests |
| Row loaded **before** the claim | **1 failure** — `The_claims_attempt_count_increment_survives_the_terminal_update`, reproducing the exact silent-undo regression S5-2's ordering exists to prevent |
| `Sent` added to `RetryableStatuses` | **2 failures**, including a second delivery of an already-sent notification |
| Pre-claim `ValidateAsync` call removed (S5-10) | **7 failures** — every staleness refusal plus the permanent-staleness regression, which is exactly the defect Option A was chosen to remove |

All guards restored; 23/23 green afterwards, 0 warnings.

**One test defect found and fixed, not a code defect:** the missing-Inspector-address case originally seeded `CreatedByInspectorId = int.MaxValue`, which violates the real `Angebote → AspNetUsers` foreign key added in Phase 3 Slice 15. Replaced with a genuine Inspector row carrying a `NULL` email — which is the actual D2 scenario, and a stronger test than the fabricated id would have been.

**Documentation that becomes stale at implementation, not before.** `CLAUDE.md` §11, `ERD.md` and `NEXT_STEPS.md` currently state that `Sending` does not exist — **true today**, and to be corrected in the Slice 5 implementation commit, not now. `ARCHITECTURE_DECISIONS.md` D69/D70 stay unchanged: they are decision records, and Slice 5 implements them rather than revising them. `Architecture.md` §5.2 needs the retry endpoint row, and the open-items list above can drop its two Slice 5 questions once S5-2 and S5-4 are built.

---

## Slice 6 — Phase 9 completion record (2026-08-12)

**Slice 6 added no production code, no schema and no migration.** It is a cross-document audit, the stale-state corrections that audit found, an adversarial re-verification of the phase's load-bearing guarantees, and **one test-only correction** the adversarial pass uncovered (below). The test count is unchanged at **1,485** — the correction strengthened an existing test rather than adding one.

### What was audited, code-first

Each claim was checked against the source rather than against the documentation:

| Claim | Verified against |
|---|---|
| Slice 1 foundation | `EmailOptions`, `EmailSecurityMode`, `EmailMessageFactory` (6 template methods), `SmtpEmailSender` (6 `IEmailSender` methods), `EmailConfigurationVerifier`, `InspectorEmailLookup`, `LoggingNoOpEmailSender` all present |
| Slice 2 single boundary | Three `catch` blocks exist in `SmtpEmailSender`; only `DeliverAsync`'s classifies a delivery outcome. The other two are the bookkeeping write and an SMTP disconnect cleanup, neither of which decides that a notification failed — the documented claim is accurate |
| Slice 3 persistence | Migration #9 `AddNotificationDeliveries`; twelve columns as documented; migration count exactly 9 |
| Slice 4 visibility | `GET /api/v1/notification-deliveries`, Admin-only, unchanged by Slice 5 |
| Slice 5 retry | Existing-row retry, CAS claim, pre-claim validation, recipient re-resolution — see the adversarial table |
| No background machinery | Repository-wide scan of `src/`: no `IHostedService`, `BackgroundService`, `AddHostedService`, `Polly`, `Outbox`, `System.Threading.Channels`, timer or scheduler |
| No business re-execution | The retry path references no `ICommandHandler`, no `IUnitOfWork`, no aggregate mutator, and no `TokenLink.Create` |
| `PermissionMatrix.md` §9 | Both rows present, Admin `F` / Inspector `—`, with the no-ownership note — matches `[Authorize(Roles = Roles.Admin)]` and the absence of any `IOwnershipValidator` call |

### Adversarial re-verification

Every mutation compiled before its test ran; where a mutation failed to compile, **no test was run**. Each file was restored with `git checkout` and confirmed **byte-identical by SHA-256** afterwards.

| Guard removed | Compiled | Result |
|---|---|---|
| CAS `AttemptCount + 1` → `AttemptCount` | yes | 3 failures |
| Tracked row loaded **before** the claim | yes | 1 failure — the silent-undo regression |
| `Sent` added to `RetryableStatuses` | yes | 1 failure — and, **after the test correction below, `A_sent_delivery_is_never_retryable` now fails too**, which it did not before |
| Pre-claim `ValidateAsync` removed (S5-10) | yes | 7 failures |
| Slice 2 boundary disabled (`when (exception is null)`) | yes | **15 of 24** `SmtpEmailSenderTests` fail — the boundary is what keeps a delivery failure off the caller |
| Slice 3 `Pending` insert removed | yes | 11 failures across sender and persistence tests |

Two mutation attempts at the Slice 2 boundary (`throw;` and `when (false)`) were **rejected by the compiler** — unreachable code and `CS8360` respectively, both errors under solution-wide `TreatWarningsAsErrors`. No test was run for either; the third formulation compiled and was used.

### Finding, and its fix: one test had become a weaker guard than its name implied

`A_sent_delivery_is_never_retryable` seeded its delivery against a **fabricated** `EntityId`. Once S5-10 moved staleness validation ahead of the claim, that row was refused at validation ("Lead … no longer exists") **before** the `Sent` rule was ever consulted — so with `Sent` wrongly added to `RetryableStatuses`, the test still passed. The exclusion was genuinely enforced and `Only_one_of_two_competing_retries_claims_the_delivery` did catch its removal, but the test named after the rule no longer proved it.

**Found by mutation testing during this completion gate, not by inspection** — which is the point worth keeping: the test was green, had always been green, and would have stayed green through a real regression.

**Fixed in Slice 6** (test-only; no production code, schema, migration or architecture touched). The test now seeds a **real** `Lead`, so pre-claim validation succeeds, the row reaches the compare-and-set, and the claim is what refuses it. It additionally asserts the refusal message is the claim's own ("not in a retryable state") rather than a staleness reason, and that `FailureType` stays null — so the test cannot pass for the wrong reason again. The SMTP port is one nothing listens on: a correct implementation never reaches SMTP on this path.

**Re-verified by mutation:** adding `Sent` to `RetryableStatuses` now makes this test fail; the source was restored and confirmed byte-identical by SHA-256.

This is `CLAUDE.md` §14's rule applied to a guard rather than an assertion — a test that reveals a flaw in its own construction is fixed, not discarded.

### Deliberate constraints carried out of Phase 9 — not gaps

- **The customer-facing token URLs point at Website pages that do not exist yet.** `https://<public-site>/angebot/{token}` and `/invoice/{token}` are built in **Phase 13** (D4.2). Production customer-facing use of those two notifications depends on it.
- **No real send has been performed.** Every transport assertion is against an in-process SMTP listener over a real socket. A single manual real-send remains a deployment-time step, not a code gate — there is no mailbox to send from (OQ-3b).
- **`Recipient` is a historical fact, not a copy of aggregate data**, and a retry overwrites it with the newly resolved set. That is intended (S5-5).
- **A permanently-invalid notification stays retryable**, and every such retry is refused with 409 as a pure no-op. Making it un-retryable would need a terminal status the locked design deliberately does not have (S5-10).

### Completion gate

| Check | Result |
|---|---|
| Debug build | 0 warnings, 0 errors |
| Release build | 0 warnings, 0 errors |
| Full test suite | **1,485 passing, 0 failing, 0 skipped** |
| EF model drift | none |
| Migration count | exactly **9** |
| `git diff --check` | clean |
| Unexpected files | none |
| Phase 10 functionality | none |

**Phase 9 is complete.** The branch is publishable; pushing, opening a PR and merging each require explicit permission (`CLAUDE.md` §19).
