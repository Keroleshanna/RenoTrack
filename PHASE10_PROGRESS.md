# PHASE10_PROGRESS.md — Dashboard (Angular)

**Branch:** `feature/phase-10-dashboard` (off `main` at `6cd8856`, the Phase 9 merge) · **Status:** complete, four commits, accepted by the Product Owner after a manual end-to-end pass.

**Scope note.** `PROJECT_ROADMAP.md` splits the Dashboard across Phase 10 (login, Lead pipeline, Inspection screens), Phase 11 (Angebot builder + Catalog picker) and Phase 12 (Angebot review). **This branch delivers all three, plus the Project/Invoice screens, the Catalog management workspace and the notification operations screen** — because a Dashboard that stops at the Lead pipeline cannot reach the commercial workflow the system exists for. SRS §1.1's 3–4 hour Word/Excel job is the *quote*, and it was unreachable. The roadmap's phase boundaries are recorded as delivered here rather than left looking outstanding.

---

## 1. What was built

### 1.1 Foundation (first session)

| Area | Delivered |
|---|---|
| Project setup | Angular 20 standalone + signals, `OnPush` everywhere, lazy route chunks, ESLint at `--max-warnings=0` |
| Design system | `styles/_tokens.scss` + `styles/_base.scss` — palette, type ramp, surfaces, tables, buttons, form controls. Material supplies behaviour/a11y only (**D75**) |
| Auth | In-memory access token, `sessionStorage` refresh token, one shared in-flight refresh, fail-secure role mapping (**D73**) |
| API client | One `RenoTrackApi`, all relative `/api/v1/...` paths, dev-only proxy (**D74**) |
| Errors | `ApiError` maps status → dictionary string; the server's English `detail` is never rendered |
| i18n | Typed `de.ts`/`en.ts` dictionary, runtime switch, German primary, compile-time completeness (**D79**) |
| Cockpit | KPIs, decision queue, funnel, receivables band, day plan, active projects — exact counts, no invented figures (**D78**) |
| Workspaces | Leads, Besichtigungen, Angebote, Rechnungen, Projekte |
| Backend reads | `GET /angebote`, `/invoices`, `/invoices/receivables`, `/projects`, `/inspections`, `/inspections/{id}`, `/users` (**D76**, **D77**) |

### 1.2 Commercial workflows (second session)

| Screen | Wireframe | Delivered |
|---|---|---|
| Angebot document | **D1 + D3** | One route, one component; capability derived from role × status (**D80**). Sections, line items in both FR-4.9 modes, remove, live server-computed totals and mixed-rate VAT breakdown, submit, request changes, approve, send, convert to Project, review history, save-as-Catalog (FR-4.10) |
| Catalog picker | **D2** | Debounced search, `switchMap`, retired entries never shown |
| Lead detail | **C1** | Contact, notes, Angebot history, schedule-inspection (**C2**), create-Angebot |
| Inspection detail | **C3** | Mobile-first on-site screen: notes, photo upload, mark complete; every control removed once BR-10 closes it |
| Project detail | **E1–E3** | Agreed/invoiced/remaining with BR-3's negative-balance warning, invoice list, create/send/mark-paid/void, completion with the FR-8.6 override |

### 1.3 Operational completion (this session)

The driving rule was **backend capability → real UI workflow**, never a client method with no caller. Every remaining endpoint a Dashboard role is permitted to call now has a workflow behind it.

| Workspace / screen | Delivered |
|---|---|
| **Katalog** (Wireframe **F1**, new) | Both roles browse; Admin creates, edits and retires. Retirement is confirmed and explained as BR-12/BR-14 retirement, never deletion. An Inspector sees no management controls and is told where their own contributions come from instead (FR-4.10) |
| **Benachrichtigungen** (new, Admin only) | The §9 operational view of outgoing email: status filter across all four states, recipient, attempt count, failure text, and manual retry with a confirmation naming exactly what will and will not be repeated (**D70**). A 409 refusal and a *failed delivery* are reported differently, because the API distinguishes them |
| **Leads** workspace | "Neuer Lead" (Admin) — the FR-2.1 manual entry form, which navigates to the new Lead so the visit can be scheduled next |
| **Lead detail** | "Kontaktdaten bearbeiten" (Admin any, Inspector own) and "Bauleitung zuweisen/ändern" (Admin). The assigned colleague is now shown **by name** rather than as `#id` |
| **Inspection detail** | "Bauleitung ändern" (Admin), absent once BR-10 closes the visit |
| **Project detail** | "Projekt pausieren" / "Projekt fortsetzen" (Admin), each offered only from the state §4.3 allows, with an explanation of why a paused project shows no Complete button |
| **Angebot document** | "Als Vorlage verwenden" — FR-4.11 duplication onto another Lead, offered from every status |

---

## 2. Backend enablers added, and why

Five documented permissions had no endpoint. Each was built as a full vertical slice — command, validator, handler, controller action, DI registration, tests, authorization — and each is justified by a `PermissionMatrix.md` row that already existed.

**Three of the five needed no new Domain code at all**: `Lead.AssignInspector`, `Project.PutOnHold()` and `Project.Resume()` already existed with correct guards and were simply unreachable through the API. That is the shape to look for first.

| Enabler | Route | Justified by | Decision |
|---|---|---|---|
| Manual Lead creation | `POST /api/v1/leads/manual` | §1, FR-2.1, Sequence §2 | **D86** |
| Lead contact correction | `PUT /api/v1/leads/{id}` | §1 | **D87**, **D88** |
| Lead inspector assignment | `PUT /api/v1/leads/{id}/inspector` | §1 | — |
| Inspection reassignment | `PUT /api/v1/inspections/{id}/inspector` | §2 | **D89** |
| Project hold / resume | `POST /api/v1/projects/{id}/hold` · `/resume` | §5, StateMachine §4.3 | **D90** |

**No schema change: migrations are unchanged at nine.** Two new Domain methods (`Lead.UpdateContactDetails`, `Inspection.Reassign`) and five new `AuditAction` values, all mapping to existing columns.

### One document conflict, resolved rather than absorbed

`Sequence Diagram.md` §2 showed manual Lead entry posting to `POST /api/v1/leads` under `[Authorize(Roles="Admin")]`. That route is the anonymous contact form, and one action cannot be both. Serving both would mean a controller branching on `User.Identity.IsAuthenticated` to decide whether to trust a body-supplied `source` — a decision in a controller, on the one field that gates FR-9.2's notification, behind the fail-closed default D57 exists to protect. **Resolved as a separate Admin-only route**, with the diagram and `Architecture.md` §5.2 corrected in the same change (**D86**).

---

## 3. What was deliberately *not* built

| Not built | Why | Recorded as |
|---|---|---|
| Customers workspace | `PermissionMatrix.md` has no Customers section, SRS names no requirement, and the aggregate has **zero commands** — a list existing because an entity exists | **D91** |
| Activity Timeline on the Lead detail (C1 draws one) | No audit-log read endpoint exists; Phase 15 owns that screen. Faking it from Lead fields would be a fabricated audit trail | **D85** |
| Editing a Lead's `Notes` | §1 grants "contact details"; FR-2.1 lists notes separately. Widening a documented permission by assumption is the mistake CLAUDE.md §2 already records once | **D87**, `NEXT_STEPS.md` |
| A reason on Project hold | StateMachine §4.3 mentions one, but `ERD.md` has no column and the only home would be a best-effort audit row (D50) | **D90** |
| `Angebot.InspectionId` populated from the Dashboard | No per-Lead Inspection read exists; guessing from a date window attaches the wrong visit | **D84** |
| User account administration (§8) | Directly contradicts D64 and CLAUDE.md §22 — **no code path creates a user in Production**, pinned by `DatabaseInitializerTests`. SRS OQ-1 is still open | `NEXT_STEPS.md` |
| Photo thumbnails on C3 | `IFileStorage` has no `GetAsync` and no endpoint serves a stored file (CLAUDE.md §13) | Known gap, `NEXT_STEPS.md` |
| Invoice actions on the Rechnungen list | One workflow, one place — creating one needs BR-3's balance, which exists only on a Project | **D82** |

---

### 1.4 Three QA rounds (commits `ce91314`, `5dea592`, `090aef0`)

The Product Owner ran three rounds of manual QA against the built application. **Every defect below was found by operating the product; none was found by a test**, and the suites were green throughout.

**Round 1 — the Quote workflow and the Catalog relationship (`ce91314`).** The state machine and the data model were both wrong, not the UI:

| Finding | Root cause | Resolution |
|---|---|---|
| Removing a line that had been saved to the Catalog threw a 500 | `CreatedFromAngebotItemId` was `Restrict` | `SetNull` + **migration #10** (**D95**) |
| "Save to Catalog" stayed on offer and claimed the item already existed | The screen keyed off `catalogItemId` — the *opposite* direction of the relationship | New `ItemDto.SavedToCatalog` from a batched query; the command is now idempotent |
| A returned quote could not be resubmitted without an artificial edit | `SubmitForReview` accepted `Draft` only | Accepts `ChangesRequested` too (**D94**) |
| The Inspector never learned changes had been requested | The Cockpit had no entry for it and the note lived only in the history | Dedicated decision-queue entry + an action-required callout carrying the Admin's note |
| A quote-writing task vanished the moment the draft was created | The task counted Leads in `InspectionDone`, and creating the draft moves the Lead on | Counts unwritten *and* unsubmitted work |
| A typo in a line could only be fixed by delete-and-recreate | No update path existed at any layer | `Angebot.UpdateItem` + `PUT .../items/{itemId}`, on an explicit Product-Owner requirement |
| A completed visit was a dead end | BR-10, correctly — but its own named remedy was never built | `Inspection.Reopen` (**D92**) |
| "This week" and "next 30 days" returned identical results | Both were rolling windows from today | Real calendar weeks, adjacent and mutually exclusive |
| A picker asked for `pageSize=200` against a cap of 100 | The contract was not mirrored client-side | Clamped in the single query-parameter funnel, with tests |

**Round 2 — contact actions, photos, notices (`5dea592`).** Multiple photo selection and a separate camera capture (two inputs, because `capture` suppresses the gallery); one reusable contact-actions component (call, WhatsApp, email, directions) wherever contact details appear; the appointment column on the Leads list; funnel percentages that name their own denominator; error toasts that expire like success ones.

**Round 3 — browser QA against the real API (`090aef0`).** Two defects that all 1,600+ tests had missed:

1. **Re-completing a reopened visit returned 409.** The handler re-drove a Lead transition that had already happened, so a corrected visit could never be closed — defeating the feature Round 1 had just added. Fixed with an explicit "already past the visit" status set (**D93**); three Application-layer tests pin it, which is the layer the coordination actually lives in.
2. **The appointment column never had data.** It requested ~456 days against a documented 366-day cap, so every load was a 400 — and because the failure was caught to keep the pipeline usable, the column read "not scheduled" for every Lead and looked healthy.

One of our own test expectations was also wrong and was corrected rather than the code: `ItemUnit` is a deliberately **open** value object, so a custom unit code is accepted, not rejected. That correction changed the Catalog form's design (select **plus** a free-text escape hatch).

### 1.5 Product Owner acceptance

The Product Owner ran a final manual pass covering login and logout for both roles, Lead scoping, the on-site screen, notes, multi-photo upload, completion, reopen and re-completion, the Angebot builder, the full review cycle through to **send**, and responsive behaviour — and confirmed the Dashboard correctly reflects the quote as `Sent`. **The Dashboard work is accepted as functionally complete.**

The customer-facing half of that workflow — email delivery, the magic-token quote page, Accept/Decline and status propagation — is **explicitly the next phase** and is not part of this branch. The API endpoints behind it exist and are tested (`GET`/`POST /api/v1/public/angebote/{token}`); what does not exist is the customer-facing page and a configured mailbox.

---

## 4. Defects found by driving the running application

None of the five was visible in code review. All were found by operating the app against the real API and LocalDB.

1. **Catalog picker hung on "Wird geladen …".** `distinctUntilChanged()` suppressed a repeated search term, so no response ever arrived to clear the loading flag. **Removed** — a repeat must genuinely re-query, because the Catalog changes underneath.
2. **Tables pushed the whole page sideways on a phone.** `.rt-table-wrap` had `overflow-x: auto` but sat in a flex column, where `min-width: auto` refuses to shrink below content width. **Fixed in the base layer**, repairing every table screen.
3. **Catalog create failed with a 400 the UI could not explain.** The write contract takes `defaultUnitCode` (a value object addressed by its code) while the read returns `defaultUnit`; assuming symmetry sent the wrong field name. **Fixed with a named `CatalogItemWrite` type** so the asymmetry is stated once and cannot recur silently.
4. **The unit was a free-text box.** `ItemUnit.FromCode` rejects anything unrecognised with a 400 the user cannot diagnose. **Now a `<select>`** over the same `STANDARD_UNITS` the Angebot line-item form offers.
5. **The retry confirmation stayed open after a refusal.** A 409 is terminal for that click, so the dialog sat on top of the message explaining why and invited an identical second refusal. **Now closed on the error path too.** Found by clicking retry against a deployment with `Email:Enabled=false`.

---

## 5. End-to-end verification actually performed

Driven through the running Dashboard against the real API and LocalDB, as both roles.

**Previous sessions:** public contact form → Lead `New` → Admin schedules (BR-13 assigns) → Inspector records notes and completes (BR-10 closes the screen) → creates `ANG-2026-00003`, adds a section, a custom line and a Catalog-sourced line at a different VAT rate, saves it to the Catalog → submits → Admin requests changes → approves → sends (real token link) → customer accepts via the public endpoint → Admin converts to Project → creates, sends and marks an invoice paid → completion refused with **409** → override with a reason → `Completed` → voids the open invoice.

**This session, on the new work:**

- **Katalog:** created an entry as Admin (persisted, re-read, German formatting), then confirmed an **Inspector sees the same list with no management controls** and both explanatory hints.
- **Manual Lead:** created `Familie Weber` with source `Telefon` — persisted with the manual source, navigated to the detail screen, and **no notification path reachable**.
- **Assignment:** assigned the Inspector; the panel switched from "Nicht zugewiesen" to the colleague's **name**, the button relabelled to "ändern", and the Lead's status **did not move** (BR-7).
- **Contact correction:** form pre-filled from the server and **had no notes field**; changed the phone and cleared the address — `PUT` semantics honoured, notes and assignment untouched.
- **Inspection reassignment:** as Admin the C3 screen offered **only** "Bauleitung ändern"; `PUT /leads/1/inspector` and `PUT /inspections/1/inspector` both returned **200**. As Inspector the same screen offered the on-site controls and **no reassign button** — the capability split verified from both sides.
- **Notifications:** screen rendered a seeded `Failed` row with all four status filters translated; retry against a deployment with email disabled returned **409** and was reported as a German conflict message mapped from the status code, never the server's English `detail`.
- **Angebot duplication:** "Als Vorlage verwenden" offered on a `Draft`; the target picker correctly reported that no other Lead is assigned to that Inspector, and hid its create button in that state.
- **Role-derived navigation:** Admin sees Katalog **and** Benachrichtigungen; the Inspector sees Katalog only, with Rechnungen and Benachrichtigungen absent — matching §6 and §9.

The seeded notification row was **deleted afterwards**: it was raw SQL, not a real business event, and leaving fabricated data behind would misrepresent the system's state.

---

## 6. Gate results

**Re-verified on 2026-09-04 at `090aef0`**, not carried over from an earlier run.

| Check | Result |
|---|---|
| Backend build | **0 warnings, 0 errors** |
| Backend tests | **1,645 passing** (372 Domain + 441 Application + 390 Infrastructure + 442 Api), 0 failing, 0 skipped — **up from 1,534** at the Phase 9 merge |
| Frontend build | **389.68 kB** initial, 0 warnings, no budget change |
| Frontend lint | passes at `--max-warnings=0` |
| Frontend tests | **74 passing** (21 → 41 → 45 → 74 across the four sessions) |
| Migrations | **ten** — `RelaxCatalogItemOriginFkToSetNull` (**D95**) is Phase 10's only schema change |
| Console | no JavaScript exceptions; only deliberate 401/403 probes and the pre-fix statuses |
| Routes | all eight render for the roles permitted to reach them; no dead routes |
| Unused API client methods | **none** — every method has a caller |
| Browser QA | full workflow driven end to end as both roles against the real API and LocalDB |

**Test growth by round:** 1,534 at the Phase 9 merge → 1,602 (Dashboard + five enablers) → 1,638 (QA round 1) → 1,642 (round 2) → **1,645** (round 3).
