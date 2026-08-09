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

**Approved 2026-08-09**, including the Slice 3 split (3a / 3b), whose stated purpose is that **migration #9 is isolated and reviewed independently of the failure-handling work**. The rest of the sequence is the Product Owner's, unchanged; the repository revealed no dependency requiring further adjustment. The non-production delivery guard sits in Slice 1, since it is part of choosing the implementation at DI time.

| # | Slice | Scope | Schema |
|---|---|---|---|
| 1 | Email configuration + SMTP/MailKit sender | `EmailOptions` (+ `Validate()`), `SmtpEmailSender`, DI selection, **non-production refuses to deliver by default** (D64 shape), `LoggingNoOpEmailSender` **retained** | none |
| 2 | Message construction + six German templates | Notification → `MimeMessage`, From/Reply-To, link construction from the configured base URL. Testable with **no network** | none |
| 3a | Safe failure handling **only** | Catch, log `Warning`, never rethrow. Adversarial pass: remove the catch, watch an `Api.Tests` case turn 200 into 500. **No status type, no delivery-record abstraction, no persistence model, no EF configuration** — see the resolved boundary below | none |
| 3b | Notification persistence | The lifecycle/status type **and** the Infrastructure-owned `NotificationDeliveries` table + EF configuration + **migration #9**, status transitions, write-after-commit — introduced together with their first real usage | **migration #9** |
| 4 | Admin visibility | Read endpoint for failed/pending notifications (`PermissionMatrix.md` §9) | none |
| 5 | Manual retry | Retry endpoint; per-type reconstruction; double-click guard | none |
| 6 | Documentation reconciliation + adversarial verification + completion gate | Cross-document sweep in the Phase 8 shape | none |

**Why split Slice 3.** 3a is independently valuable and independently verifiable: after it, the system sends real email and can never fail a committed operation, with no schema at all. 3b is where the only migration in Phase 9 appears. Keeping them separate means the schema change is reviewed on its own, and a slip in 3b does not hold back a safe, working sender.

### The 3a/3b boundary — RESOLVED (approved 2026-08-09)

The open question was whether Slice 3a's "lifecycle preparation" meant introducing a status type or delivery-record abstraction ahead of the table that stores it. **Resolved as option (i): it does not.**

**Slice 3a is strictly failure handling.** It contains catch / log `Warning` / never rethrow, and nothing else:

- **no** `NotificationDelivery` lifecycle or status type,
- **no** delivery-record abstraction or interface,
- **no** persistence model,
- **no** EF configuration,
- **no** migration and **no** schema change of any kind.

**Do not introduce an abstraction in Slice 3a merely because Slice 3b will need it.** That is `CLAUDE.md` §4's rule applied unchanged — every abstraction in this codebase exists because one specific, real caller needed it at that exact moment, and a type whose only consumer arrives in the next slice does not meet that bar. The same discipline that governs repositories, DTOs and schema governs this.

**The lifecycle/status type and the `NotificationDeliveries` persistence model are introduced in Slice 3b**, together with their first real usage and migration #9. 3b is therefore where the type, the table, the EF configuration and the status transitions all land at once — which is also what makes 3b reviewable as a single coherent schema change, the reason the split exists.

**Consequence, stated plainly:** at the end of Slice 3a the system sends real email and can never fail a committed business operation, but a failure is visible **only in the logs**. Admin visibility does not exist until Slice 4, and retry until Slice 5. That is an intended intermediate state, not a gap to be closed early by pulling 3b's model forward.

**Slices 1–3a deliver FR-9.1/9.2/9.3 in full.** Slices 3b–5 exist solely to satisfy the approved visibility-and-retry requirement, which is the Product Owner's addition beyond the roadmap's original Phase 9 wording.

---

## Open items to settle at each slice's design review

Not decided here, and deliberately not invented:

- Exact column names and types for `NotificationDeliveries` (derived from `ERD.md` conventions; reviewed before the migration). **The table name itself is settled — `NotificationDeliveries`, chosen deliberately over `Notifications` so the name states that this is an Infrastructure-level delivery record and not a Domain "Notification" aggregate.**
- The concurrency mechanism preventing an Admin double-click from double-sending.
- Whether the token-link URL is constructed in Infrastructure (recommended — it is deployment data) or in Application.
- How `AngebotChangesRequestedNotification` identifies *which* review comment to reconstruct when an Angebot has several (its `Comment` is re-derived, and "the latest" is an assumption, not a rule).
- The transport test mechanism (an in-process SMTP listener is recommended; there is no Docker on the development machine and no SMTP server in either CI job).
- Whether a single manual real-send is a Phase 9 completion criterion or a deployment-time check.

---

## Slice log

*(empty — implementation has not started)*
