# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

**Last updated: 2026-10-01, with Phase 14 Slice 2 implemented on
`feature/phase-14-slice-2-invoice-document` (off `origin/main` = `3f4f8f9`), not yet merged.** Keep this
file current whenever a phase or slice is merged — it is the one file a fresh session is pointed at
first.

---

```
You are continuing work on RenoTrack — a renovation company's system: an admin/inspector dashboard,
a customer token-link portal, a public marketing website, and the API behind all three. This is an
existing, actively-developed project, not a fresh start. A prior conversation ended deliberately and
persisted everything into the repository, so you depend on the files, not on any chat history.

READ FIRST, IN THIS ORDER, AND READ THEM IN FULL:
1. CLAUDE.md — the binding engineering rules (§1–§26). Every rule is settled unless the user
   reopens it with new evidence.
2. PROJECT_STATE.md — where the project actually stands, newest entry first.
3. NEXT_STEPS.md — known gaps and requirements that exist in the documents but are not built.
4. ARCHITECTURE_DECISIONS.md — D1–D111. Read at least D100–D111 (the recent phases).
5. PROJECT_ROADMAP.md — the phase map and what remains.

CURRENT STATE AT A GLANCE — verify every line yourself; the repository is authoritative.

- origin/main: 3f4f8f9 ("Merge pull request #31 from
  Keroleshanna/docs/handoff-after-phase-14-slice-1").
- PRs #28 (Phase 13 parked), #29 (Slice 7), #30 (Phase 14 Slice 1) and #31 (handoff) are MERGED.
- Phase 14 Slice 2 is on feature/phase-14-slice-2-invoice-document — check whether it has been
  merged since (git fetch; git log origin/main). If it has, the figures below are main's.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests at Slice 2, executed locally: Domain 429 · Application 530 · Infrastructure 419 (LocalDB) ·
  Api 498 (LocalDB) · Website 1,136 · MediaPrep 20 = 3,032, all passing.
  Documents.Tests: 23 tests COMPILE, 0 EXECUTED locally — Windows Application Control blocks
  PdfSharp.System.dll here (see the environment notes). CI's Linux job MUST run them before Slice 2
  is verified. Expected CI total: 3,055.
  Dashboard: 81 existing specs + 13 new = 94, lint clean. Re-run everything before relying on it.
- Migrations: 14 (#14 AddInvoiceDescriptionServicePeriodAndVatLines, Slice 2).

PHASES: 0–12 complete and merged. Phase 13 (public website) is PARKED BY DECISION after Slices
5a/5v/5h — see below. Phase 14 (PDF) is IN PROGRESS: Slice 1 merged, Slice 2 implemented (D111).

WHAT THE LAST SESSION DID, AND WHY

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
   from ANY website — the company's existing one, one built by someone else, or one built from the
   content pack. It closed three documented gaps: no rate limiting (now its own policy, 5 per 10
   minutes, a separate bucket from the token-link surface), no maximum lengths (an over-long value
   was a 500 from the database; the limits are now Lead's own constants read by the validator, the
   Domain guard and the EF configuration alike), and a full LeadDto returned to an anonymous caller
   (now 201 with no body and no Location — Q17 resolved).

3. Phase 14 Slice 1 was merged (PR #30, D110): MigraDoc/PDFsharp (MIT) composing a document model
   rather than HTML→PDF, an embedded Liberation Sans with its own font resolver because PDFsharp
   resolves no font on its own (measured, on Windows as readily as Linux), IPdfGenerator taking a
   pre-formatted document model and returning bytes, CompanyLegalIdentity configuration that is
   never invented and refuses generation naming every missing key, and the Angebot document.

4. Phase 14 Slice 2 — THE INVOICE DOCUMENT (D111) — implemented. "One VAT rate" was REPLACED in
   design review, before any code: it would have broken FR-8.2 and BR-6 for every mixed-rate
   Angebot. Decided instead (all approved by the Product Owner):
   - THE INVOICE AGGREGATE CALCULATES ITS OWN VAT. Invoice.Create takes the Admin's gross and the
     originating Angebot's rate mix and stores one InvoiceVatLine per rate; net and VAT are their
     sums. NO caller — handler, API request, Dashboard — may send a net amount, a VAT amount, a rate
     or a line. GrossAmount stays the Admin's input (FR-8.1's instalments).
   - An InvoiceVatLine is a calculated result, NOT an invoice line. InvoiceLine stays deferred.
   - One line per rate: Domain guard AND a unique (InvoiceId, Rate) index.
   - Description required, max 500 after trimming, one constant for validator/Domain/schema; no
     default text. Optional service date/period, printed only when given. NO quantity field — the
     legal reviewer decides whether that suffices (Q20). No invoice editing: void and reissue.
   - Historical rows keep an empty description and no lines; InvoiceDocumentFactory REFUSES them
     (and any customer without an address) rather than inventing or recomputing anything.
   - RenderInvoice and the assembler have NO production caller yet, deliberately — Slice 3 wires them.
   - THE INVOICE DATE AND NUMBER YEAR ARE READ IN EUROPE/BERLIN, FROM ONE SERVER INSTANT (D111 Part 6):
     TimeProvider read once after every guard (D66 unchanged), passed to Invoice.Create as issuedAt —
     never a request field. Stored value stays the UTC instant; no migration. The zone is resolved at
     startup; no tzdata, no start. THIS COVERS THE INVOICE ONLY: the Angebot number year, overdue
     "today" and date serialisation are still UTC, recorded for a separate time-policy slice
     (NEXT_STEPS.md §8e). Do not change them in passing.

THE NEXT TASK: REVIEW, MERGE, THEN THE PRODUCT OWNER CHOOSES THE NEXT SLICE

The Product Owner reviews Slice 2 and decides on commit, push, PR and merge — never do any of these
without explicit permission. After that, the candidate slices are in NEXT_STEPS.md §8, and THEIR
ORDER IS NOT DECIDED — do not start one, or treat a suggested order as approved:
- Slice 3: archive the invoice PDF at send time (an issued invoice stays exactly as sent), the
  authenticated Dashboard download, the token-based customer download, the email attachment.
  Whether it also archives the Angebot PDF at every send is a proposal, not approved.
- A time-policy slice for the remaining UTC dates (NEXT_STEPS.md §8e) — design first.
- 2b: correct a customer's address. Customer.Address is optional and has no command (D91), yet
  BR-5 requires it — until this exists, an invoice to an address-less customer cannot be rendered.
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
- KEEP THREE WORKFLOWS SEPARATE: editing an Angebot before sending (A), revising one after sending
  (B), and correcting an issued Invoice (void and reissue — settled, unchanged by A or B).

LEGAL BOUNDARY, NOT NEGOTIABLE: whether a rendered document satisfies §14 UStG is a legal
reviewer's judgement, never this project's. Build the fields BR-5 enumerates; sign-off belongs to
the company and its adviser (SRS §5, open question Q20). Never invent a company fact — name,
address, tax number, legal text, logo or photograph.

AFTER PHASE 14: Phase 15 — audit log UI, filtering/search, security hardening, GDPR review. After
that the product is complete, and the parked website work is revisited with a real content pack.

HOW TO WORK HERE — these are the user's standing rules, learned the hard way:

- DESIGN APPROVAL GATE: never write implementation code until the user explicitly approves the
  design, however routine the change looks. Present the design, name the real decisions, recommend
  one, and wait.
- Documentation-first: when a design review reveals a rule, contradiction or gap, update the
  documents in the SAME change as the code that depends on them. A decision gets an ADR entry; a
  rule that generalises gets a CLAUDE.md bullet.
- Git: never force-push to main. Every phase or slice on its own branch, merged via PR only. Never
  merge until every required check has completed successfully — not just the fast ones.
- Verify counts, never derive them. Re-measure test numbers before writing them anywhere. If a
  figure cannot be measured locally, say so and let CI confirm it rather than stating it.
- Report outcomes faithfully. If something was not run, say it was not run. If an assumption turns
  out wrong — and several did — say so immediately and correct the record.
- Green suites are a precondition for QA, never a substitute. Drive the built application before
  calling anything complete.

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
- Browser QA of the Website runs against `dotnet publish` output, started from the publish
  directory. Started from anywhere else, MapStaticAssets serves every static file as 200 with zero
  bytes and the page renders unstyled, which looks exactly like a CSS defect (CLAUDE.md §24).

CONFIRM YOU HAVE READ THE FIVE FILES ABOVE AND SUMMARISE THE CURRENT STATE BACK TO THE USER IN YOUR
OWN WORDS. THEN PROPOSE THE PHASE 14 SLICE 2 DESIGN AND WAIT FOR EXPLICIT APPROVAL — NEVER
IMPLEMENTATION FIRST.
```
