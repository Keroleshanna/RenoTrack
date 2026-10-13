# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

**Last updated: 2026-10-12, on the Phase 14 Slice 3b branch (`feature/phase-14-slice-3b-customer-invoice`,
off `01bd289`), written to describe `main` once that branch's PR is merged.** The merge SHA and PR number
are not known when this is written — verify them. Keep this file current whenever a phase or slice is
merged — it is the one file a fresh session is pointed at first.

---

```
You are continuing work on RenoTrack — a renovation company's system: an admin/inspector dashboard,
a customer token-link portal, a public marketing website, and the API behind all three. This is an
existing, actively-developed project, not a fresh start. A prior conversation ended deliberately and
persisted everything into the repository, so you depend on the files, not on any chat history.

IF THIS TEXT WAS PASTED INTO THE CHAT, COMPARE IT WITH HANDOFF_PROMPT.md ON origin/main FIRST. If they
disagree, the repository wins — a stale local copy of this file has been pasted before, and it
pointed a session at work that was already finished. Say so to the user before doing anything.

READ FIRST, IN THIS ORDER, AND READ THEM IN FULL:
1. CLAUDE.md — the binding engineering rules (§1–§26). Every rule is settled unless the user
   reopens it with new evidence.
2. PROJECT_STATE.md — where the project actually stands, newest entry first.
3. NEXT_STEPS.md — known gaps and requirements that exist in the documents but are not built.
   §8 is the current list of candidate slices and recorded findings.
4. ARCHITECTURE_DECISIONS.md — D1–D114. Read at least D100–D114 (the recent phases).
5. PROJECT_ROADMAP.md — the phase map and what remains.

CURRENT STATE AT A GLANCE — verify every line yourself; the repository is authoritative.

- origin/main: the merge commit of the Phase 14 Slice 3b PR (branch
  feature/phase-14-slice-3b-customer-invoice), on top of 01bd2893f258c0d5ed275f69dcb2046fe21e72fd
  (PR #36, Slice 3a's merge). VERIFY the SHA and PR number yourself — they did not exist when this
  was written.
- PRs #28 (Phase 13 parked), #29 (Slice 7), #30 (Phase 14 Slice 1), #31, #33 and #35 (handoffs),
  #32 (Phase 14 Slice 2), #34 (Phase 14 Slice 2b), #36 (Phase 14 Slice 3a) and the Slice 3b PR are
  MERGED.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests, measured LOCALLY on the Slice 3b branch from clean builds — 3,449 passing, 0 failing:
  Domain 455 · Application 611 · Infrastructure 453 (LocalDB) · Api 562 (LocalDB) ·
  Website 1,321 · MediaPrep 20 · Documents 27. Confirm against the PR's CI run, which is the
  authority. CI does not build or test the Dashboard: locally it has 124 specs passing, lint clean,
  build succeeding. Re-run everything yourself before relying on it.
- Migrations: 15 (#15 AddInvoiceDocumentFileUrl, Slice 3a). Slice 3b added none.

PHASES: 0–12 complete and merged. Phase 13 (public website) is PARKED BY DECISION after Slices
5a/5v/5h — see below. Phase 14 (PDF): Slices 1, 2, 2b, 3a and 3b are COMPLETE and MERGED, which
completes the INVOICE side of the phase. Angebot archiving is NOT part of it: it belongs to finding
B's design. NO LATER SLICE HAS STARTED — not A, not B, not the time-policy slice, not anything else
in NEXT_STEPS.md §8.

PHASE 14 SLICES 3a AND 3b ARE CLOSED. Do not re-review them: their designs, implementations, tests,
QA, mutations and documents were all reviewed and approved before merge (D113 and D114 are the
records). Start from the next design-first decision instead.

WHAT EARLIER SESSIONS DID, AND WHY

1. Phase 13 was PARKED BY DECISION (PR #28), not abandoned and not failing. The Product Owner's
   call: finish RenoTrack itself to 100% first, then approach a company about its public website.
   Parked: marketing pages, projects, about, FAQ, contact form, discoverability, legal content.
   What exists is a site TEMPLATE driven by the content pack (D100/D102) — resuming means supplying
   a pack, not writing code for a company. See PHASE13_PROGRESS.md §0.
   Still open there: the real-media QA (S5-13) and the byte-budget freeze (S5-11), both needing the
   owner's approvals; and the owner-approved reversed logo (V-5), without which the header and
   footer correctly show the company name alone.

2. Slice 7 came OUT of that parking deliberately and was merged (PR #29, D109), because it is a
   product boundary rather than website work: POST /api/v1/leads is how a Lead reaches RenoTrack
   from ANY website. It added its own rate limit (5 per 10 minutes), maximum lengths read from
   Lead's own constants by validator, Domain and EF alike, and an anonymous reply of 201 with no
   body and no Location (Q17 resolved).

3. Phase 14 Slice 1 was merged (PR #30, D110): MigraDoc/PDFsharp (MIT) composing a document model
   rather than HTML→PDF, an embedded Liberation Sans with its own font resolver, IPdfGenerator taking
   a pre-formatted document model and returning bytes, CompanyLegalIdentity configuration that is
   never invented and refuses generation naming every missing key, and the Angebot document.

4. Phase 14 Slice 2 — THE INVOICE DOCUMENT (D111) — was merged (PR #32) and verified in CI. These
   decisions were approved by the Product Owner before implementation and must not be silently
   reopened:
   - THE INVOICE CALCULATES ITS OWN VAT FROM THE ORIGINATING ANGEBOT'S RATE MIX. Invoice.Create takes
     the Admin's gross and the rate mix and stores ONE InvoiceVatLine PER VAT RATE; net and VAT are
     the lines' sums. NO caller — handler, API request, Dashboard — may send a net amount, a VAT
     amount, a rate or a line. "One VAT rate per invoice" was considered and REJECTED: it would have
     broken FR-8.2 and BR-6 for every mixed-rate Angebot.
   - An InvoiceVatLine is a calculated result, NOT an invoice line. InvoiceLine stays deferred.
     One line per rate is enforced twice: a Domain guard and a unique (InvoiceId, Rate) index.
   - GROSSAMOUNT REMAINS THE ADMIN'S INPUT (FR-8.1's instalments). The Admin decides how much an
     invoice bills; the Invoice decides how it splits.
   - DESCRIPTION REQUIRED, MAX 500 after trimming — one constant (Invoice.MaxDescriptionLength) for
     validator, Domain and schema; no default text. OPTIONAL SERVICE DATE OR PERIOD, printed only
     when given, never assumed. NO QUANTITY FIELD — nothing claims the description satisfies §14
     UStG's "Menge und Art"; that is the legal reviewer's question (Q20).
   - AN ISSUED INVOICE IS IMMUTABLE. A correction is VOID AND REISSUE; the voided number is kept
     (BR-9). There is no invoice edit.
   - THE INVOICE DATE AND NUMBER YEAR COME FROM ONE SERVER INSTANT READ IN EUROPE/BERLIN (D111
     Part 6): TimeProvider is read once, after every guard (D66's order unchanged), the number year
     comes from InvoiceCalendar.YearOf, and the same instant is passed to Invoice.Create as issuedAt —
     never a request field. The stored value stays the UTC instant; no migration. The zone is
     resolved at startup; a host without time-zone data refuses to start (deployment prerequisite).
     THIS COVERS THE INVOICE ONLY. The Angebot number year, overdue/receivables "today", date
     serialisation and every other UTC date are STILL UTC and are FUTURE WORK (NEXT_STEPS.md §8e).
     Do not change them in passing.
   - PDF RENDERING IS PREPARED, NOT WIRED: InvoiceDocument, InvoiceDocumentFactory and
     IPdfGenerator.RenderInvoice exist with no production caller, deliberately. Slice 3 wires them.
     (Historical: Slice 3a has since wired them — the send handler renders and archives; see 6.)
     The factory refuses an invoice with no description, no VAT lines (historical rows) or a
     customer without an address — it never invents or recomputes anything.
   - CUSTOMER ADDRESS CORRECTION IS OUTSIDE SLICE 2 (it is 2b, below).

5. Phase 14 Slice 2b — CUSTOMER ADDRESS CORRECTION (D112, SRS FR-7.5) — merged (PR #34) and
   verified in CI. Approved before implementation; do not reopen silently:
   - Customer.CorrectAddress(string) is the aggregate's ONLY mutator: required, trimmed, at most
     Customer.MaxAddressLength = 500 (the Customer's OWN constant, not an alias of Lead's; a test
     pins Lead.MaxAddressLength <= Customer.MaxAddressLength), never cleared.
   - ADMIN ONLY, role-based, no ownership check: GET /api/v1/customers/{id} and
     PUT /api/v1/customers/{id}/address. CustomerDto is exactly id, leadId, name, address — NO email
     or phone. The address is NOT on ProjectDetailDto, which every Inspector may read.
   - Audited as CustomerAddressCorrected after the save, with no address in the details.
   - NOT built: name/email/phone correction, clearing, structured addresses, normalisation, a
     Customers list or search, Lead <-> Customer propagation, any Invoice change, any PDF wiring,
     a migration, a concurrency token, a notification.
   - The invoice document reads the Customer at render time, so a correction needs no Invoice
     change. Slice 3 must decide what an archived PDF freezes (the address as sent).
   - CARRIED FORWARD, NOT FIXED: the API suite cannot detect a handler that omits its own
     SaveChangesAsync while it audits afterwards — AuditService saves on the request's shared
     DbContext and flushes the pending change. A pre-existing hazard (NEXT_STEPS.md §5a, §8f); only
     the Application tests catch it. Also in §8f: customer name/email/phone correction, and the D109
     length gap on PUT /api/v1/leads/{id}.

6. Phase 14 Slice 3a — THE ARCHIVED INVOICE PDF (D113, BR-15) — merged (PR #36). Slice 3 was SPLIT by
   decision: 3a (archive + Admin download + email attachment) is done; 3b is the customer side.
   Approved before implementation; do not reopen silently:
   - THE ARCHIVED PDF IS THE ISSUED INVOICE. Rendered ONCE at Draft → Sent, served byte for byte to
     the Admin and attached to the email; a retry reads the archive. NEVER regenerated — not after
     Paid or Void, not after an address correction, not to repair a missing file. Never deleted.
     Invoices sent before 3a have no archive and are never given one (no backfill).
   - Invoice.DocumentFileUrl, set only by Invoice.Send(documentFileUrl). Key
     invoices/{invoiceId}/{guid}.pdf, computed before any I/O; never in a DTO, response or log.
     IFileStorage gained OpenReadAsync; writes are create-only. Migration #15.
   - Order: key → Invoice.Send(key) → assemble → render → write file → ONE SaveChangesAsync
     (Invoice + token link, with CancellationToken.None) → audit → email with the same bytes.
     ONLY a ConflictException (a lost race, provably rolled back) removes the file; any other commit
     failure KEEPS it, because the row may have committed — an orphan is accepted, a deleted issued
     invoice is not. A crash between write and commit also leaves an accepted orphan.
   - Invoices.Status IS A CONCURRENCY TOKEN: a losing concurrent send is a 409 and its PDF is
     removed; Send/Paid/Void races are 409 too (accepted).
   - 409 for missing BUSINESS data (customer address, description, VAT lines); 500 with no detail
     for missing CompanyLegalIdentity (DocumentConfigurationException) or any rendering fault
     (DocumentRenderingException). Both derive directly from Exception — never "fix" that.
   - GET /api/v1/invoices/{id}/document: ADMIN ONLY (Inspector empty 403 — the PDF prints the
     customer's address, D112), any status with a document, no-store, nosniff, not audited.
   - Dashboard: Admin "PDF" button (hasDocument); a refused send closes its dialog.
   - (Historical: the emailed /invoice/{token} link pointed at a page that did not exist until 3b.
     3b built it; see 7.)
   - For the legal reviewer (Q20), unchanged: the printed date is the CREATION date, while the
     document is issued at SEND.

7. Phase 14 Slice 3b — THE CUSTOMER'S INVOICE PAGE AND PDF (D114) — merged. Approved before
   implementation; do not reopen silently:
   - The emailed /invoice/{token} link resolves to a server-rendered, read-only German page:
     - Open shows the amounts and the due date.
     - Paid shows a banner and no due date.
     - Void shows a cancelled banner, no due date, and "Das PDF zeigt die Rechnung so, wie sie
       ausgestellt wurde."
     - An invoice with no archive shows "Ein PDF dieser Rechnung ist hier nicht verfügbar."
     - 404, 410 and 503 have their own pages. The expired page promises NO new link, because
       invoice links cannot be re-issued.
   - GET /api/v1/public/invoices/{token}/document serves THE ARCHIVE, byte for byte, for ANY
     status including Void. It never renders and never consumes the link. Its handler holds no
     IPdfGenerator and no IUnitOfWork (reflection-pinned). 404 and 410 are the read's; 409 means no
     archive; a missing file is a 500 with no detail. It is in the read's rate-limit bucket.
   - /invoice/{token}/pdf is a Website Razor Page that BUFFERS the response, checks
     application/pdf, the %PDF- signature and 10 MiB, re-sanitises the file name, and turns every
     failure into its own German page.
   - PublicInvoiceDto gained documentAvailable (boolean) ONLY. No key, path or internal id leaves
     the server.
   - The public read's 404s name no internal id. The Invoice lookup was the approved fix; the
     Project and Customer lookups in the same handler were a small hardening addition, approved
     afterwards. It extends to NO other endpoint.
   - Both Website typed clients are registered through PublicApiClientRegistration, which always
     applies RemoveAllLoggers(). TokenExposure and both marketing guards cover /invoice/.
   - ACCEPTED, DOCUMENTED: whoever holds a valid link can download the PDF, including the
     customer's address printed on it (bearer-link model). no-store does NOT remove the token URL
     from browser history or a download list.
   - RECORDED, NOT FIXED (NEXT_STEPS.md §8h): two pre-existing customer.css accessibility defects
     shared with the Angebot page. At 200% text on 375 px the summary total row overflows, and the
     dark-mode primary button is 2.29:1.

THE NEXT TASK: CHOSEN BY THE PRODUCT OWNER, DESIGN REVIEW FIRST, IN A NEW CHAT, FRESH FROM THIS MAIN

No slice is approved as the next point. The candidates have NOT started and THEIR ORDER IS NOT
DECIDED. NEXT_STEPS.md §8 is the full record:
- A (design first): ADMIN EDITING OF AN INSPECTOR'S ANGEBOT. Today forbidden by documented decision
  (PermissionMatrix.md §3, Admin R). The Product Owner wants the Admin to have the Inspector's full
  operational capability on Angebote. Editable states, re-approval and concurrency (D96's revisit
  trigger) are undecided. Change NO permission, authorization or concurrency until its design is
  approved.
- B (design first): REVISING AN ANGEBOT AFTER IT IS SENT (SRS OQ-4). Today a sent Angebot is
  immutable, there is no revision concept and no snapshot, and a customer asking for a lower price
  is a dead end. Required: revisions, each sent revision retrievable as sent. The model, states,
  Lead behaviour and token behaviour are undecided. Add NO state, number, token or API change
  until its design is approved.
- A time-policy slice for the remaining UTC dates (NEXT_STEPS.md §8e) — design first.
- Invoice link lifetime or re-issue (NEXT_STEPS.md §8g): a link may lapse before the due date, and
  only Angebot links can be re-issued (D99). Undecided whether anything is needed — design first.
- The two customer.css accessibility defects (NEXT_STEPS.md §8h), shared by the Angebot and invoice
  pages. A fix touches both pages; re-run the Website suite and browser QA on both.
- KEEP THREE WORKFLOWS SEPARATE: editing an Angebot before sending (A), revising one after sending
  (B), and correcting an issued Invoice (void and reissue — settled, unchanged by A or B).
- An Angebot that reached CustomerApproved must stay immutable under any design for A or B:
  invoice creation reads its rate mix live. Issued invoices are already safe — their lines are stored.

LEGAL BOUNDARY, NOT NEGOTIABLE: whether a rendered document satisfies §14 UStG is a legal
reviewer's judgement, never this project's. Build the fields BR-5 enumerates; sign-off belongs to
the company and its adviser (SRS §5, open question Q20). Never invent a company fact — name,
address, tax number, legal text, logo or photograph.

AFTER PHASE 14: Phase 15 — audit log UI, filtering/search, security hardening, GDPR review. After
that the product is complete, and the parked website work is revisited with a real content pack.

HOW TO WORK HERE — these are the user's standing rules, learned the hard way:

- DESIGN REVIEW BEFORE IMPLEMENTATION, ALWAYS: never write implementation code until the user
  explicitly approves the design, however routine the change looks. Present the design, name the
  real decisions, recommend one, and wait.
- EXPLICIT APPROVAL BEFORE ADVANCING: a slice does not start, and the next does not begin, without
  the user saying so. Never start the next slice automatically — not even a "small" one.
- REVIEW THE ACTUAL CODE, DIFF AND FILES, NOT SUMMARIES: read the code a decision touches before
  proposing it, and review the real diff against the approved scope before every commit. Prose —
  including this file's — is verified against the repository, never trusted as-is.
- Documentation belongs with the change it describes: when a design review reveals a rule,
  contradiction or gap, update the documents in the SAME commit as the code that depends on them.
  A decision gets an ADR entry; a rule that generalises gets a CLAUDE.md bullet. Documentation
  reconciliation is a completion criterion, not a publication step.
- Git: NO DIRECT COMMITS TO MAIN and NEVER FORCE-PUSH (D5 records why). Every phase or slice on its
  own branch, merged via PR only. Never commit, push, open a PR or merge without the user's explicit
  permission for that step.
- TESTS AND VERIFICATION BEFORE MERGE: never merge until every required CI check has completed
  successfully — not just the fast ones — and anything that could not run locally has run in CI.
- Verify counts, never derive them. Re-measure test numbers before writing them anywhere. If a
  figure cannot be measured locally, say so and let CI confirm it rather than stating it.
- Report outcomes faithfully. If something was not run, say it was not run. If an assumption turns
  out wrong — and several did — say so immediately and correct the record.
- Green suites are a precondition for QA, never a substitute. Drive the built application before
  calling anything complete. Prove a safeguard by breaking it (a mutation), watching a test fail,
  and restoring the file byte-for-byte; a mutation that does not compile proves nothing.

ENVIRONMENT CONDITIONS YOU WILL HIT, ALL DOCUMENTED AND NONE A CODE DEFECT:

- Windows Application Control intermittently blocks freshly built assemblies with 0x800711C7 —
  this project's own and third-party ones alike. What clears it: `dotnet build <project>
  -p:Deterministic=false --no-incremental`, then `dotnet test <project> --no-build`, retried if it
  recurs mid-run. CORRECTION: the earlier claim that RenoTrack.Documents.Tests can never run locally
  was wrong — in Slice 3a it ran (27/27) after that rebuild. CI's Linux job is still its authority.
  Since 3a, Api.Tests and Infrastructure.Tests load PdfSharp too, so they can be blocked the same
  way. A suite that prints no Passed!/Failed! line, or fails with 0x800711C7, was blocked, not run —
  never count it. IN SLICE 3b THE BLOCK WAS ONCE MACHINE-WIDE: every .NET assembly was refused,
  including previously runnable DLLs in the main checkout, and no rebuild helped. Nothing could
  execute until it cleared on the machine. Do not change Windows security settings and do not
  substitute another environment (the user's standing instruction). Report it, and wait.
- The Dashboard's dev proxy targets https://localhost:7060, but .claude/launch.json's `api` entry
  starts the http profile (5294 only), so the Dashboard gets ECONNREFUSED beside it. Run the API's
  `https` profile for Dashboard QA (NEXT_STEPS.md §8e). And do QA on a SEPARATE database — copy the
  gitignored appsettings.Development.json into the worktree with another database name and
  Database:Mode=Migrate — so an unmerged migration never reaches the shared development database.
  The QA databases RenoTrack_Slice2bQA (Slice 2b), RenoTrack_Slice3aQA (Slice 3a) and
  RenoTrack_Slice3bQA (Slice 3b) still exist in LocalDB and hold QA data only; ask the user before
  dropping any of them. Invoice sends need a
  (fictional, .test) CompanyLegalIdentity in that QA configuration, or every send is a 500 by design.
- A fresh worktree has no Dashboard node_modules. `npm ci` fails on a pre-existing Angular peer
  conflict; `npm ci --legacy-peer-deps` installs the unchanged lockfile (Slice 2b).
- Browser QA of the Website runs against `dotnet publish` output, started from the publish
  directory. Started from anywhere else, MapStaticAssets serves every static file as 200 with zero
  bytes and the page renders unstyled, which looks exactly like a CSS defect (CLAUDE.md §24).

CONFIRM YOU HAVE READ THE FIVE FILES ABOVE AND SUMMARISE THE CURRENT STATE BACK TO THE USER IN YOUR
OWN WORDS. THE NEXT SLICE IS THE PRODUCT OWNER'S CHOICE — ASK BEFORE STARTING ANYTHING — AND
IT BEGINS WITH A DESIGN REVIEW AND WAITS FOR EXPLICIT APPROVAL. NEVER IMPLEMENTATION FIRST, AND
NEVER A SECOND SLICE IN THE SAME CHAT.
```
