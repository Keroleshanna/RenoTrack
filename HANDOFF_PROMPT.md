# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

**Last updated: 2026-10-04, at `origin/main` = `a87d7a5` (PR #34, Phase 14 Slice 2b merged and
verified).** Keep this file current whenever a phase or slice is merged — it is the one file a fresh
session is pointed at first.

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
4. ARCHITECTURE_DECISIONS.md — D1–D112. Read at least D100–D112 (the recent phases).
5. PROJECT_ROADMAP.md — the phase map and what remains.

CURRENT STATE AT A GLANCE — verify every line yourself; the repository is authoritative.

- origin/main: a87d7a57864dc805808df1a0f4339c9737b90b2c ("Merge pull request #34 from
  Keroleshanna/feature/phase-14-slice-2b-customer-address") — a true merge commit whose second
  parent is the slice head, 62c749aabd743a4f838dcdb00d328c23cdb47096. A later handoff-only docs PR
  may sit on top of it; nothing else should.
- PRs #28 (Phase 13 parked), #29 (Slice 7), #30 (Phase 14 Slice 1), #31 and #33 (handoffs),
  #32 (Phase 14 Slice 2) and #34 (Phase 14 Slice 2b) are MERGED.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests, measured in CI on PR #34 — 3,114 passing, 0 failing:
  Domain 442 · Application 552 · Infrastructure 424 (LocalDB) · Api 517 (LocalDB) ·
  Website 1,136 · MediaPrep 20 · Documents 23.
  The 23 Documents (PDF) tests cannot run on the development machine (see the environment notes);
  CI's Linux job ran them and all 23 passed. CI does not build or test the Dashboard: locally it has
  108 specs passing, lint clean, build succeeding. Re-run everything yourself before relying on it.
- Migrations: 14 (#14 AddInvoiceDescriptionServicePeriodAndVatLines, Slice 2; 2b added none).

PHASES: 0–12 complete and merged. Phase 13 (public website) is PARKED BY DECISION after Slices
5a/5v/5h — see below. Phase 14 (PDF) is IN PROGRESS: Slice 1, Slice 2 and Slice 2b are COMPLETE,
MERGED AND VERIFIED. NO LATER SLICE HAS STARTED — not Slice 3, not A, not B, not the time-policy
slice.

PHASE 14 SLICE 2b IS CLOSED. Do not re-review it: its design, implementation, tests, QA, mutations
and documents were all reviewed and approved before merge (D112 is the record). Start from the next
design-first decision instead.

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

THE NEXT TASK: THE PRODUCT OWNER CHOOSES THE NEXT SLICE, FRESH FROM THIS MAIN — DO NOT START ONE

None of the following has started, and THEIR ORDER IS NOT DECIDED. Do not start one, and do not
treat any suggested order as approved. NEXT_STEPS.md §8 is the full record:
- Slice 3: archive the invoice PDF at send time (an issued invoice stays exactly as sent), the
  authenticated Dashboard download, the token-based customer download, the email attachment.
  Whether it also archives the Angebot PDF at every send is a proposal, not approved.
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

- Windows Application Control intermittently blocks freshly built assemblies with 0x800711C7. For
  this project's own assemblies, building with -p:Deterministic=false usually clears it. For
  third-party DLLs (PdfSharp.System.dll) it does not, so RenoTrack.Documents.Tests cannot be run
  locally at all — CI is its verification. In Slice 2 a block survived plain retries and Release:
  what cleared it was `dotnet build <project> -p:Deterministic=false --no-incremental` followed by
  `dotnet test <project> --no-build`. A suite that prints no Passed!/Failed! line was blocked, not
  run — never count it.
- The Dashboard's dev proxy targets https://localhost:7060, but .claude/launch.json's `api` entry
  starts the http profile (5294 only), so the Dashboard gets ECONNREFUSED beside it. Run the API's
  `https` profile for Dashboard QA (NEXT_STEPS.md §8e). And do QA on a SEPARATE database — copy the
  gitignored appsettings.Development.json into the worktree with another database name and
  Database:Mode=Migrate — so an unmerged migration never reaches the shared development database.
  The QA database from Slice 2b, RenoTrack_Slice2bQA, still exists in LocalDB and holds QA data
  only; ask the user before dropping it. (RenoTrack_Slice2QA, from Slice 2, no longer exists.)
- A fresh worktree has no Dashboard node_modules. `npm ci` fails on a pre-existing Angular peer
  conflict; `npm ci --legacy-peer-deps` installs the unchanged lockfile (Slice 2b).
- Browser QA of the Website runs against `dotnet publish` output, started from the publish
  directory. Started from anywhere else, MapStaticAssets serves every static file as 200 with zero
  bytes and the page renders unstyled, which looks exactly like a CSS defect (CLAUDE.md §24).

CONFIRM YOU HAVE READ THE FIVE FILES ABOVE AND SUMMARISE THE CURRENT STATE BACK TO THE USER IN YOUR
OWN WORDS. THEN ASK THE USER WHICH SLICE COMES NEXT — DO NOT CHOOSE ONE, AND DO NOT START ANY.
WHATEVER THEY CHOOSE BEGINS WITH A DESIGN REVIEW AND WAITS FOR EXPLICIT APPROVAL — NEVER
IMPLEMENTATION FIRST.
```
