# PHASE10_PROGRESS.md — Dashboard (Angular)

**Branch:** `feature/phase-10-dashboard` · **Status:** complete.

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

| Check | Result |
|---|---|
| Backend build | **0 warnings, 0 errors** |
| Backend tests | **1,602 passing** (354 Domain + 438 Application + 386 Infrastructure + 424 Api), 0 failing, 0 skipped — **up from 1,534** |
| Frontend build | **386.11 kB** initial, 0 warnings, no budget change |
| Frontend lint | passes at `--max-warnings=0` |
| Frontend tests | **45 passing** (41 before this session) |
| Migrations | **unchanged — nine.** No schema change |
| Console | no JavaScript errors; only the deliberate 400/409 probes |
| Dead routes | none — every nav entry, list row and Cockpit tile resolves to a real screen |
| Unused API client methods | **none** — every method has a caller |
