# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

**Last updated: 2026-10-01, at `origin/main` = `c18a541`.** Keep this file current whenever a phase
or slice is merged — it is the one file a fresh session is pointed at first.

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
4. ARCHITECTURE_DECISIONS.md — D1–D110. Read at least D100–D110 (the recent phases).
5. PROJECT_ROADMAP.md — the phase map and what remains.

CURRENT STATE AT A GLANCE — verify every line yourself; the repository is authoritative.

- origin/main: c18a541 ("Merge pull request #30 from
  Keroleshanna/feature/phase-14-pdf-generation").
- PRs #28 (Phase 13 parked), #29 (Slice 7) and #30 (Phase 14 Slice 1) are MERGED. All CI green.
- Build: 0 Warnings, 0 Errors (TreatWarningsAsErrors solution-wide).
- Tests, measured in CI at the Phase 14 Slice 1 run — 2,932 passing, 0 failing:
  Domain 396 · Application 470 · Infrastructure 412 (LocalDB) · Api 489 (LocalDB) ·
  Website 1,136 · MediaPrep 20 · Documents 9.
  Re-run them yourself before relying on them.
- Migrations: 13. None was added by Phase 14 Slice 1.

PHASES: 0–12 complete and merged. Phase 13 (public website) is PARKED BY DECISION after Slices
5a/5v/5h — see below. Phase 14 (PDF) is IN PROGRESS: Slice 1 is merged, Slice 2 is next.

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

THE NEXT TASK: PHASE 14 SLICE 2 — THE INVOICE DOCUMENT

Slice 1 found, by reading the code first, that the Invoice aggregate CANNOT produce a §14 UStG
compliant document today. It stores net, VAT and gross amounts — and no description, no quantity
and NO VAT RATE. Phase 8 computed the per-rate split to derive the totals and discarded it, which
ERD.md explicitly predicted would be revisited here.

The Product Owner has APPROVED the narrower resolution: the Invoice gains ONE description and ONE
VAT rate — not an InvoiceLine collection — because a partial invoice against a Project does not
correspond to the Angebot's lines anyway. This needs: Domain fields and guards, a migration, the
creation path (command, validator, API, and the Dashboard's invoice form), the Invoice document
model and template, and tests.

Then Slice 3: archive the generated PDF at send time rather than regenerating it (an issued invoice
must stay exactly as it was sent), the authenticated download for the Dashboard, the token-based
download for the customer page, and the email attachment.

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

TWO ENVIRONMENT CONDITIONS YOU WILL HIT, BOTH DOCUMENTED AND NEITHER A CODE DEFECT:

- Windows Application Control intermittently blocks freshly built assemblies with 0x800711C7. For
  this project's own assemblies, building with -p:Deterministic=false usually clears it. For
  third-party DLLs (PdfSharp.System.dll) it does not, so RenoTrack.Documents.Tests cannot be run
  locally at all — CI is its verification.
- Browser QA of the Website runs against `dotnet publish` output, started from the publish
  directory. Started from anywhere else, MapStaticAssets serves every static file as 200 with zero
  bytes and the page renders unstyled, which looks exactly like a CSS defect (CLAUDE.md §24).

CONFIRM YOU HAVE READ THE FIVE FILES ABOVE AND SUMMARISE THE CURRENT STATE BACK TO THE USER IN YOUR
OWN WORDS. THEN PROPOSE THE PHASE 14 SLICE 2 DESIGN AND WAIT FOR EXPLICIT APPROVAL — NEVER
IMPLEMENTATION FIRST.
```
