# UI Wireframes

**Project:** RenoTrack — Renovation Company Website & Project-Tracking Dashboard
**Companion documents:** SRS.md, PermissionMatrix.md

These are low-fidelity, structural wireframes (layout and content, not visual styling) for every primary screen. Each wireframe references the SRS requirement(s) it fulfills, and notes which role(s) can reach it (see PermissionMatrix.md for the full detail).

Legend: `[ Button ]` = action button, `( )` = input field, `[v]` = dropdown, `☐` = checkbox/toggle.

---

## A. Public Website

### A1 — Home Page
**Roles:** Public Visitor · **Covers:** FR-1.1

```
┌───────────────────────────────────────────────────────────┐
│  [Logo]      Home   Services   Portfolio   Contact          │
├───────────────────────────────────────────────────────────┤
│                                                             │
│        HERO IMAGE — bathroom/kitchen renovation photo      │
│        "Professionelle Fliesenarbeiten seit ..."           │
│                          [ Kostenloses Angebot anfordern ]  │
│                                                             │
├───────────────────────────────────────────────────────────┤
│  Our Services                                              │
│  ┌───────────┐ ┌───────────┐ ┌───────────┐                │
│  │ Bathrooms │ │ Kitchens  │ │ Floors    │  ...            │
│  └───────────┘ └───────────┘ └───────────┘                │
├───────────────────────────────────────────────────────────┤
│  Portfolio (before/after gallery)                          │
│  [img] [img] [img] [img]                                    │
├───────────────────────────────────────────────────────────┤
│  Footer: Impressum | Datenschutz | Contact info             │
└───────────────────────────────────────────────────────────┘
```

### A2 — Contact / Get a Quote Page
**Roles:** Public Visitor · **Covers:** FR-1.2, FR-1.3

```
┌───────────────────────────────────────────────────────────┐
│  Get a Free Quote                                          │
│  Name        ( ________________ )                          │
│  Phone       ( ________________ )                          │
│  Email       ( ________________ )                          │
│  What do you need done?                                     │
│              ( multi-line free text ______________ )        │
│                                                             │
│                                       [ Send Request ]      │
├───────────────────────────────────────────────────────────┤
│  On submit → confirmation banner:                            │
│  "Thanks — we'll contact you within 1–2 business days."      │
└───────────────────────────────────────────────────────────┘
```

### A3 — Public Angebot Decision Page (Token Link)
**Roles:** Lead/Customer (no login) · **Covers:** FR-6.2, FR-6.3

```
┌───────────────────────────────────────────────────────────┐
│  [Company Logo]              Angebot ANG-2026-00042         │
├───────────────────────────────────────────────────────────┤
│  Pos. 1  Baustelleneinrichtung                               │
│    1.001  ... description ...   qty  unit  price   total   │
│  ─────────────────────────────────── Zwischensumme: € xxx   │
│  Pos. 2  Abriss ...                                          │
│    ...                                                       │
├───────────────────────────────────────────────────────────┤
│  Zusammenfassung                                             │
│    Nettobetrag                        € xx,xxx.xx           │
│    zzgl. 16% MwSt                     € xxx.xx               │
│    zzgl. 19% MwSt                     € xxx.xx               │
│    Gesamtsumme                        € xx,xxx.xx  (bold)   │
├───────────────────────────────────────────────────────────┤
│           [ ✅ Angebot annehmen ]   [ ❌ Ablehnen ]           │
│           (Reject opens optional reason field)               │
└───────────────────────────────────────────────────────────┘
```

### A4 — Public Invoice View Page (Token Link)
**Roles:** Customer (no login) · **Covers:** FR-8.3

```
┌───────────────────────────────────────────────────────────┐
│  Rechnung RE-2026-00017                                      │
│  Project: Bathroom renovation — [Customer Name]              │
├───────────────────────────────────────────────────────────┤
│  Net amount        € x,xxx.xx                                │
│  VAT (19%)          €   xxx.xx                                │
│  Gross total        € x,xxx.xx                                │
│  Due date            DD.MM.YYYY                                │
│  Bank details for transfer: IBAN ..., BIC ...                 │
├───────────────────────────────────────────────────────────┤
│                                     [ Download PDF ]          │
└───────────────────────────────────────────────────────────┘
```

---

## B. Dashboard — Shared

### B1 — Login
**Roles:** Admin, Inspector · **Covers:** FR-10.1

```
┌───────────────────────────────┐
│      RenoTrack Dashboard     │
│  Email    ( ______________ )    │
│  Password ( ______________ )    │
│                [ Log In ]       │
└───────────────────────────────┘
```

### B2 — Lead Pipeline (Landing Page After Login)
**Roles:** Admin (all Leads), Inspector (own assigned Leads only) · **Covers:** FR-2.4

```
┌───────────────────────────────────────────────────────────┐
│  RenoTrack     [Leads] [Projects] [Catalog] [Logout]      │
├───────────────────────────────────────────────────────────┤
│  Filter: Status [v]   Inspector [v]   Date range ( - )        │
│                                          [ + New Lead ]       │
├───────────────────────────────────────────────────────────┤
│  New (3)      Insp. Sched. (2)   Insp. Done (1)  In Review(2) │
│  ┌─────────┐  ┌─────────┐        ┌─────────┐    ┌─────────┐ │
│  │ M. Klein│  │ A. Weber│        │ F. Braun│    │ S. Fischer│ │
│  │ Website │  │ Phone   │        │ Website │    │ Website  │ │
│  └─────────┘  └─────────┘        └─────────┘    └─────────┘ │
│  ... (Kanban-style columns matching StateMachine.md §1) ...   │
└───────────────────────────────────────────────────────────┘
```

---

## C. Dashboard — Lead & Inspection

### C1 — Lead Detail Page
**Roles:** Admin (full), Inspector (if assigned) · **Covers:** FR-2.2, FR-2.3, FR-2.5

```
┌───────────────────────────────────────────────────────────┐
│  ← Back to Pipeline        Lead: M. Klein   Status: New       │
├───────────────────────────────────────────────────────────┤
│  Contact: Phone, Email, Address                               │
│  Notes: (free text)                                           │
│  Source: Website                                              │
│                                    [ Schedule Inspection ]     │
├───────────────────────────────────────────────────────────┤
│  Activity Timeline (audit trail)                              │
│   • Lead created via Website form — 10:32                    │
│   • ...                                                        │
└───────────────────────────────────────────────────────────┘
```

### C2 — Schedule Inspection (Modal)
**Roles:** Admin · **Covers:** FR-2.3

```
┌───────────────────────────┐
│ Schedule Inspection          │
│ Date/Time  ( __________ )    │
│ Inspector  [v Select ]        │
│              [ Cancel ] [ Save ]│
└───────────────────────────┘
```

### C3 — Inspection Screen (Mobile-First, Inspector On-Site)
**Roles:** Inspector · **Covers:** FR-3.1–FR-3.4

```
┌───────────────────────┐
│ Inspection: M. Klein     │
│ Address: ...              │
├───────────────────────┤
│ Photos                    │
│ [img][img][+ Add Photo]   │
├───────────────────────┤
│ Notes                     │
│ ( multi-line text area )  │
├───────────────────────┤
│   [ Mark Complete ]       │
└───────────────────────┘
```

---

## D. Dashboard — Angebot Builder & Review

### D1 — Angebot Builder (Inspector)
**Roles:** Inspector (own drafts) · **Covers:** FR-4.1–FR-4.11

```
┌───────────────────────────────────────────────────────────┐
│  Angebot Draft — Lead: M. Klein          Status: Draft        │
│                       [ Duplicate Past Angebot ▾ ]  (FR-4.11) │
├───────────────────────────────────────────────────────────┤
│  + Add Section                                                │
│  ▼ Pos. 1  Baustelleneinrichtung          Subtotal: € 1,791.27│
│     [ + Add Item ▾ ]  → ( From Catalog )  ( Custom Item )     │
│     ┌───────────────────────────────────────────────────┐    │
│     │ Description │ Qty │ Unit │ Unit Price │ VAT │ Total │  │
│     │ ...row 1... │ ... │ ...  │ ...        │ 19% │ € ... │  │
│     │ [Save as Catalog item]  (shown for custom rows only) │  │
│     └───────────────────────────────────────────────────┘    │
│  ▼ Pos. 2  Abriss ...                     Subtotal: € ...    │
├───────────────────────────────────────────────────────────┤
│  Summary (live, recalculated on every change)                 │
│    Nettobetrag    € ...   |  zzgl 16% MwSt € ... | 19% € ...  │
│    Gesamtsumme    € ...  (bold)                                │
├───────────────────────────────────────────────────────────┤
│                    [ Save Draft ]   [ Submit for Review ]      │
└───────────────────────────────────────────────────────────┘
```

### D2 — Catalog Item Picker (Modal, opened from D1)
**Roles:** Inspector · **Covers:** FR-4.9

```
┌───────────────────────────────┐
│  Search Catalog: ( fliese... )  │
│  ┌───────────────────────────┐ │
│  │ Bodenbelag trockengepresste │ │
│  │ Fliesen... — €82.25/m²      │ │
│  ├───────────────────────────┤ │
│  │ Grundieren des Verlege...   │ │
│  │ — €4.54/m²                  │ │
│  └───────────────────────────┘ │
│               [ Use Selected ]  │
└───────────────────────────────┘
```

### D3 — Angebot Review (Admin)
**Roles:** Admin · **Covers:** FR-5.1–FR-5.4

```
┌───────────────────────────────────────────────────────────┐
│  Angebot ANG-2026-00042 — In Review     Lead: M. Klein        │
├───────────────────────────────────────────────────────────┤
│  (Same read view as D1, but read-only for Admin)              │
│  Previous review comments (if any), threaded                  │
├───────────────────────────────────────────────────────────┤
│  Comment: ( ________________________ )                        │
│           [ Request Changes ]     [ ✅ Approve & Send ]        │
└───────────────────────────────────────────────────────────┘
```

---

## E. Dashboard — Project & Invoicing

### E1 — Project Detail
**Roles:** Admin · **Covers:** FR-7.4

```
┌───────────────────────────────────────────────────────────┐
│  Project: M. Klein — Bathroom Renovation     Status: Active   │
│  Agreed Total: € 25,673.36   Invoiced: € 8,000   Remaining: € 17,673.36 │
├───────────────────────────────────────────────────────────┤
│  Originating: Lead | Inspection | Angebot ANG-2026-00042       │
├───────────────────────────────────────────────────────────┤
│  Invoices                                    [ + Add Invoice ]│
│  ┌───────────────────────────────────────────────────────┐   │
│  │ RE-2026-00017 │ € 8,000 │ Sent  │ Due 15.08 │ [Mark Paid]│ │
│  │ ...                                                      │ │
│  └───────────────────────────────────────────────────────┘   │
│                                    [ Mark Project Completed ] │
└───────────────────────────────────────────────────────────┘
```

### E2 — Add Invoice (Modal)
**Roles:** Admin · **Covers:** FR-8.1, FR-8.2

```
┌───────────────────────────────┐
│  New Invoice                    │
│  Remaining to invoice: €17,673.36│
│  Amount    ( __________ )        │
│  Due Date  ( __________ )        │
│              [ Cancel ] [ Create ]│
└───────────────────────────────┘
```

### E3 — Mark Invoice Paid (Modal)
**Roles:** Admin · **Covers:** FR-8.4

```
┌───────────────────────────────┐
│  Mark RE-2026-00017 as Paid      │
│  Paid Date  ( __________ )        │
│  Method     [v Bank Transfer ]    │
│              [ Cancel ] [ Confirm ]│
└───────────────────────────────┘
```

---

## F. Dashboard — Catalog Management

### F1 — Catalog List & Editor
**Roles:** Admin (manage), Inspector (view/select only, done inline in D2) · **Covers:** FR-4.8

```
┌───────────────────────────────────────────────────────────┐
│  Catalog Items                             [ + New Item ]     │
│  ┌───────────────────────────────────────────────────────┐   │
│  │ Title                          │ Unit │ Suggested Price │ │
│  │ Bodenbelag trockengepresste...  │ m²   │ €82.25          │ │
│  │ Grundieren des Verlegeuntergr.. │ m²   │ €4.54            │ │
│  └───────────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────┘
```

---

## Notes on Fidelity
- These are **structural** wireframes (what's on the screen and why), not visual design — no colors/typography/branding decisions are implied here. Visual design happens once a front-end framework/design system is chosen (see Architecture.md §4, `frontend-design` conventions).
- Mobile-first attention is called out explicitly only where SRS requires it (C3 — Inspector on-site); other Dashboard screens are desktop/tablet-first per SRS §2.4.
