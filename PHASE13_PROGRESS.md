# PHASE13_PROGRESS.md — Public Website (Marketing, Inquiry, Discoverability)

**Phase:** 13 — Public Website, per `PROJECT_ROADMAP.md`: the marketing site (Wireframes A1/A2, superseded by the approved design below), the public inquiry → Lead flow, and search/AI discoverability. The token-link half (A3) was delivered early by Phase 11.
**Branch:** `feature/phase-13-public-website`, off `main` at `2c39aee` (PR #27).
**Started:** 2026-09-16.
**Design status:** **Design Review Gate approved and frozen, 2026-09-16** — including the Search & AI Discoverability addendum, with the Tech Lead's two corrections applied (§3.9).

> **Company-neutral by rule (D100, unchanged).** This file records the *product* design. The first deployment's identity, legal details, business facts, content, image inventory and company-specific verification findings are **not** recorded in this repository; they belong in the separate private content repository (§2, Q9). See §6 for the open transfer action.

---

## 1. Slice status

| # | Slice | Status |
|---|---|---|
| **0** | Housekeeping and preparation | ✅ **closed** — commit `0d2f22a` (§5) |
| **1** | Content model (content pack schema, isolation, startup validation, runtime mount) | ✅ **closed** (§5a) |
| **2** | Site shell (`_SiteLayout`, tokens, header/footer, marketing headers + CSP, host/lowercase 301s) | ✅ **closed** (§5b) |
| **3** | Homepage | 🔍 **implemented, pending Tech Lead review** — not committed (§5c) |
| 4 | Services overview + service detail (`/leistungen/{slug}`) | ⏸ |
| 5 | Media preparation + Projects gallery | ⏸ |
| 6 | About + FAQ + **running-prototype visual checkpoint** (before any integration) | ⏸ |
| 7 | API hardening of anonymous `POST /api/v1/leads` (backend) | ⏸ |
| 8 | Inquiry flow (`/angebot-anfragen`, → Lead) | ⏸ |
| 9 | Contact form + click-to-load map | ⏸ |
| 10 | Search & AI discoverability (JSON-LD, sitemap, robots, crawl suite) | ⏸ |
| 11 | Legal content (drafts in the content repository, lawyer-approved) | ⏸ |
| 12 | Completion gate | ⏸ |
| 13 | Post-launch (Search Console, Bing, Business Profile, AI-search monitoring) — no code | ⏸ |

**No slice starts before the previous one is reviewed.**

---

## 2. Gate decisions (Tech Lead, 2026-09-16)

| Item | Decision |
|---|---|
| **F6 / Q25** — source company material | Both source folders (work photos, company documents) moved **completely outside** the Git repository before implementation. Company documents are never committed, copied or otherwise brought into RenoTrack. |
| **F13 / Q22** — Phase 11 Slice 8 | **Closed/parked.** `main` remains canonical. This phase uses `feature/phase-13-public-website`. |
| **Q9** — content pack location | A **separate private deployment/content repository**, outside this product repository. **D100 unchanged.** RenoTrack stays reusable for another company without code changes or a company-specific fork. |
| **Q15** — client-side script | **Zero JavaScript on V1 marketing pages.** JSON-LD is the only script/data block and does not count as application JavaScript. Any future client-side JavaScript requires an explicit design decision. |
| **Q26** — canonical origin | The production **apex** HTTPS origin is canonical; the `www.` host **301s** to it; HTTP → HTTPS as designed. The concrete domain is deployment configuration. |

---

## 3. Frozen design (summary of the approved review)

### 3.1 Architecture
- Everything stays in `RenoTrack.Website`: no new project, no framework, no bundler, no MediatR/AutoMapper, no backend project reference.
- Marketing pages get their own `_SiteLayout`; `_CustomerLayout`, the token pages and every D97/D100/D101 protection stay **unchanged**, and existing token tests must stay green unmodified.
- Server-rendered; the Website calls the API from the server only (D97).
- Company data comes from a **validated content pack** (bound and validated at startup like `CompanyIdentityOptions`: absent optional content warns, malformed content fails naming the key), mounted at run time like `brand/`. **No company literals in `.cs`, `.cshtml` or `.css`.**
- Service inquiry *question sets* are code, keyed by a service **type**; the content catalog maps each company service to a type.

### 3.2 Routes
`/`, `/leistungen`, `/leistungen/{slug}` (404 for unknown), `/ueber-uns`, `/projekte` (`?kategorie=` canonicalises to `/projekte`), `/faq`, `/kontakt`, **`/angebot-anfragen`** (step 1), `/angebot-anfragen/{leistung}` (step 2, `noindex, follow`), `/angebot-anfragen/danke` (PRG, `noindex`), `/impressum`, `/datenschutz` (existing mechanism), `/robots.txt`, `/sitemap.xml`, `/angebot` → **301** `/angebot-anfragen`.
**Why not `/angebot`:** `/angebot/{token}` is a customer credential route; a step URL such as `/angebot/<service>` would bind the service name as a token.

### 3.3 Inquiry → Lead
- Reuses `POST /api/v1/leads` unchanged in shape: no new endpoint, no Domain change, no migration. `Source`/`CreatedByUserId` stay server-derived (D61).
- The Website composes a deterministic structured German `Notes` block (≤ 2000 characters, guaranteed by bounded options plus a bounded free-text field) through a pure, unit-tested composer; address from PLZ/Ort (+ optional street).
- New Website boundary `IPublicLeadClient` (outcomes `Accepted | Rejected(fieldKeys) | Unavailable`); the API response body is discarded; no API message reaches the visitor.
- No price is calculated or shown anywhere. No customer confirmation email (not documented, §11). No uploads in V1.
- Abuse controls: antiforgery, honeypot, signed minimum fill time, Website-side rate limit, and API-side hardening (Slice 7). Submitted personal data is never logged (pinned by a log-capture test).

### 3.4 Slice 7 — known gaps on the anonymous Lead endpoint
1. `POST /api/v1/leads` is **not rate-limited** (only `PublicController` is).
2. `CreateLeadCommandValidator` has **no maximum lengths**, so input exceeding the schema (e.g. `Notes` > 2000) fails at the database as a 500 instead of a 400.
3. The anonymous response returns the **full `LeadDto`** (sequential `Id`, `Status`, `AssignedInspectorId`). Whether to drop the body is open question Q17.

### 3.5 Privacy and external services
- Fonts self-hosted.
- No analytics, marketing or social integrations.
- The map loads only after an explicit click, as a server-rendered `?karte=anzeigen` variant, with `frame-src` allowed on that response only.
- The antiforgery cookie is the only cookie, so no consent banner is needed.

### 3.6 Security headers (marketing pages)
- **CSP:** `default-src 'self'; script-src 'none'; style-src 'self'; img-src 'self'; font-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'; object-src 'none'`, plus `Permissions-Policy`.
- **Redirects:** lowercase and no-trailing-slash 301s apply to **marketing routes only**. They never apply to token routes, because tokens are case-sensitive.

### 3.7 Images
- **Publishing:** only owner-approved, company-owned photos. Each is prepared offline (never processed at runtime).
- **Derivatives:** WebP and JPEG at 480, 960 and 1600 px, plus an OG crop.
- **Metadata:** all EXIF, GPS and XMP is stripped, and a test verifies it. **Source photos can carry GPS coordinates.**
- **URLs and filenames:** stable, descriptive, no keyword stuffing. A replaced image gets a new filename.
- **Alt text:** required by validation.
- **Captions:** state no location or date unless the owner confirms it.

### 3.8 Accessibility and responsive design
- **Accessibility target:** WCAG 2.2 AA. One H1 per page, skip link, visible focus, accessible forms with an error summary, `<details>` for navigation and FAQ, no parallax, reduced-motion respected.
- **Test widths:** 320, 375, 414, 768, 1024, 1366 and 1920.
- **Browser QA** runs against `dotnet publish` output (§24).

### 3.9 Search & AI discoverability (approved addendum)
- **No guarantee.** This work produces crawlable, consistent, machine-readable, factual public information. It does not guarantee indexing, rankings, AI Overview inclusion, or recommendation or citation by any AI system.
- **No city or doorway pages.** One page per real service. Each page covers what is offered, its use cases, the service area, how to request an offer, and authentic company information.
- **JSON-LD:**
  - One `@graph` with stable `@id`s: `HomeAndConstructionBusiness` (the single business/organisation node), `WebSite`, `WebPage`, `BreadcrumbList`, and `Service` per service page.
  - `FAQPage` only on `/faq`, and `Person` (owner) only on the about page.
  - **Deliberately omitted:** by-appointment hours, `geo`, `priceRange`, ratings and reviews, `sameAs` (until real profiles exist), `alternateName`, and `SearchAction`.
  - `memberOf` and `foundingDate` are held back until their facts are verified.
- **Visible-agreement rule (Correction 1):** *every user-facing factual claim represented in JSON-LD must be supported by visible content on the same page.*
  - Technical identifiers, `@id` values, URLs and graph relationships do not have to appear as visible text.
  - Enforced by a test with an explicit allowlist that classifies each property as a claim (with its visible-support rule) or as exempt. An unclassified property fails the test.
- **Entity consistency:** a single source (the content pack), canonical visible and machine formats, and a forbidden-name-variants test over all rendered output.
- **Crawlability:**
  - Every sitemap URL returns 200, is self-canonical, absolute HTTPS on the apex host, and has no `noindex`, `nofollow`, `nosnippet` or `max-snippet`.
  - Every page has `lang="de"`, a unique title and description, and one H1.
  - A crawl from `/` reaches exactly the sitemap set.
  - All of this lives in a database-free `WebApplicationFactory` crawl suite in the Linux CI job.
- **Robots:** `User-agent: *` / `Allow: /` plus the sitemap, generated from a config list that is empty by default and pinned by a test.
  - **Token URLs are protected by `noindex`/`no-store`, never by robots.txt.** A disallowed URL can still be indexed as a bare URL, and here that URL is the credential.
  - Search and retrieval crawlers (Googlebot, OAI-SearchBot, Claude-SearchBot) stay allowed. Allowing or blocking training crawlers is open question Q28, and any bot-specific rule needs an ADR.
- **No `llms.txt` in V1.** It has no documented effect on Google, would duplicate the business facts (drift risk), and adds nothing for a small, clean, server-rendered site. If it is ever added, it must be generated from the content pack.
- **Sitemap:** only canonical, indexable URLs; `lastmod` taken from the content version (never "now"); `image:image` entries for approved photos.
- **Deployment checklist (Slice 13):**
  - hosting, CDN and WAF bot settings must not block search or AI search crawlers;
  - Search Console domain verification, sitemap submission, and URL Inspection of the home page, service pages and key pages, with canonical confirmation;
  - structured-data reports;
  - a negative check that a token URL shows "Excluded by noindex";
  - Bing Webmaster Tools;
  - **Monitoring (Correction 2):** indexing/coverage and Performance, weekly for the first month and monthly after that. Review **Search Console's generative-AI performance reporting in the same cycle, where it is available for the property.** Its absence is not a defect.
- **Business Profile:** future owner task. Never linked or faked before it exists. Its reviews are never copied into the site's own schema.
- **Post-launch AI-search acceptance check:** manual runs across Google, Bing, ChatGPT search and Claude web search with representative brand, service and area queries. Each run records whether the business appears, whether its facts are correct, and Search Console indexing, query and generative-AI data. Wrong facts are fixed in the site's visible content, never with bot-only content.

---

## 4. Open questions (non-blocking for Slice 1; blocking the slice named)

| Slice | Questions |
|---|---|
| 5 | Photo approval and provenance, consent of people shown, missing photos for some services, logo (vector trace, OG image) |
| 7 | **Q17** — drop the anonymous `LeadDto` response body |
| 8 | **Q11** email mandatory · **Q12** response-time wording · **Q13** multiple services per inquiry |
| 9 | **Q24** map embed vs. link-only |
| 10 | **Q27** unverified FAQ topics · **Q28** AI *training* crawlers |
| 11 / launch | Company-specific legal verification items (held outside this repository) · **Q20** legal reviewer · **Q23** hosting/log/retention facts for the privacy policy |
| 13 | **Q30** hosting/CDN/WAF bot settings |

---

## 5. Slice 0 — Housekeeping and preparation

**Actions:**
1. **Source material moved out of the repository.** The work-photo folder (130 files) and the company-document folder (6 files) moved from the main checkout's root to a sibling directory outside any Git repository.
   - Integrity: SHA-256 manifests taken before and after the move are identical.
   - The destination is not inside a Git working tree (`git rev-parse` fails there).
2. **Checked the rest of the working tree for copies**, by content hash across the main checkout and all worktrees:
   - **No copy of any company document exists anywhere.**
   - **15 work photos had byte-identical copies under `storage/inspections/{1,2,3}/`.** This is the local development file storage from Phase 10 QA photo uploads, and one copy carried GPS metadata. It was reported, and the Tech Lead ruled that **being git-ignored is not a security boundary**: the copies had to go.
   - **Cleanup (Tech Lead-directed, local development state only):**
     - In the local development database (`RenoTrack` on `(localdb)\MSSQLLocalDB`), the 11 `InspectionPhotos` rows referencing 11 of those copies (Ids 1–11, Inspections 1–3) were deleted in one guarded transaction. It was required to affect exactly 11 rows or roll back. No foreign key references `InspectionPhotos`, and no `AuditLogs` row mentions the files.
     - The 15 copies (those 11 plus 4 already-orphaned ones with no row) were then deleted, selected by content hash against the source manifest.
     - **Rows went first**, so no row ever pointed at a missing file.
     - Three unrelated 70-byte `.png` test files remain in `storage/inspections/2/`. They are not company material.
   - **What changed and what did not:**
     - Inspections 1–3 remain completed, now with no photos.
     - No tracked code, migration, Domain behaviour or test artefact changed, and `main` is untouched.
     - This is local development data, not a Domain operation. BR-10's immutability governs the application, not a developer resetting their own database.
   - **After cleanup:**
     - A content-hash scan of the whole tree (every file type, every worktree, build output included) finds **no copy** of any source file.
     - `InspectionPhotos` has 0 rows, so no reference is broken.
3. **Git history verified clean.** None of the 136 files exists as a Git object (loose, packed or unreachable). No matching path appears in any ref, stash or reflog.
4. **Branch created.** `feature/phase-13-public-website` from freshly fetched `origin/main` (`2c39aee`), with **no upstream set**, so a bare `git push` cannot target `main`. Not pushed.
5. **Documentation:** this file; a dated status line and branch entry in `PROJECT_STATE.md`; Slice 8 marked parked in `PHASE11_PROGRESS.md`.

**Migrations:** none. **Code:** none. **Packages:** none.

---

## 5a. Slice 1 — Content model (D102)

**Design:** approved at the Slice 1 gate, including S1-1 to S1-8. S1-8 is the configuration-isolation correction.

**Delivered:**
- **Content pack.** `ContentPack:RootPath` names a directory outside the application holding `site.json` (required), `legal.json` (optional) and `brand/`.
  - Each file enters through `IsolatedContentPackProvider`. It parses privately, **refuses** any top-level section outside the file's allowed roots (`site.json` → `CompanyIdentity`, `Site`; `legal.json` → `Legal`) before any key reaches application configuration, then **filters by construction**. Messages name files and sections, never values.
  - The pack is inserted after the last file source: command line > environment > pack > user-secrets/appsettings.
  - With a pack configured, `/brand` serves the pack's `brand/`.
- **`CompanyIdentityOptions` extended in place.** New fields `OwnerName`, `Address`, `OpeningHours`, `OpeningHoursNote`, `ServiceArea`. Phone, email and text rules now apply to every deployment.
- **`SiteOptions`.** `PublicBaseUrl` (the switch; HTTPS origin only) and `Services`. An enabled site requires name, phone, email, address and at least one service, all missing keys named in one message.
- **`ContentListSourceGuard`.** A content list supplied by more than one configuration source fails startup.
- **Fixtures.** Two fictional content packs (Alpha, Beta) prove the same build serves two companies with no bleed-through.

**Files changed (production):**
- **Modified:** `Program.cs`, `Content/CompanyIdentityOptions.cs`, `appsettings.json` (documentation keys only, no values).
- **New:** `ContactFormats.cs`, `ContentListSourceGuard.cs`, `ContentPackConfigurationSource.cs`, `ContentPackOptions.cs`, `ContentPackSectionPolicy.cs`, `ContentText.cs`, `OpeningHoursOptions.cs`, `PostalAddressOptions.cs`, `ServiceAreaOptions.cs`, `ServiceOptions.cs`, `SiteOptions.cs`, all under `Content/`.

Files beyond the final design's list, all within the approved scope:
- `ContentListSourceGuard.cs` holds the approved single-source rule (S1-5). The design located that rule but named no file for it.
- `ContactFormats.cs` and `ContentText.cs` are shared rule helpers for the approved phone, email and text rules.
- Pack insertion lives on `ContentPackOptions.AddTo`.

No behaviour beyond the design was added.

**Tests (measured, not derived):**

| Project | Before | After | Delta |
|---|---|---|---|
| Domain | 389 | 389 | — |
| Application | 470 | 470 | — |
| Infrastructure (LocalDB) | 412 | 412 | — |
| Api (LocalDB) | 478 | 478 | — |
| Website | 376 | 586 | **+210** |
| **Total** | **2,125** | **2,335** | **+210** |

The +210 splits as: `CompanyIdentityMarketingTests` 67, `SiteOptionsTests` 48, `ContentPackSectionPolicyTests` 34, `ContentPackIsolationTests` 27, `ContentPackStartupTests` 22, `ContentPackOptionsTests` 12. Build: 0 warnings, 0 errors. **No existing test file was edited.** The test `.csproj` gained one `ItemGroup` copying the fixtures to output.

**Mutation runs:** each mutation was applied to the finished code, built, run, and the original restored and byte-compared.

| Mutation | Tests failing |
|---|---|
| Provider equivalent to `AddJsonFile` | 21 |
| Validation removed, filter kept | 21 |
| `Filter` passes everything | 1 |
| Single-source guard removed | 3 |
| Pack inserted at top precedence | 3 |
| **Only the provider's call to `Filter` removed** | **0** |

The last row is inherent (D102 Part 2): while validation holds, filtering removes nothing. The filter is proven at function level. Closing it would take a test-only seam, which was not added without a decision.

**Environment note:** one mutation build was refused by Windows' assembly-load block (`FileLoadException` in xUnit discovery), the same condition `PROJECT_STATE.md` records. Rebuilding the identical mutation in a different form ran normally. No green result above comes from a run that did not execute.

**Also verified:** the complete example in `CONTENT_PACK.md` §5 boots the application, checked with a throwaway test that was deleted afterwards. A scan of every changed and new file finds no real company identifier.

**Documentation:** `ARCHITECTURE_DECISIONS.md` **D102**; `CLAUDE.md` new **§25**; `CONTENT_PACK.md` (new schema reference); `DEPLOYMENT_CONFIGURATION.md` §1, §2.1, §2.2, new §2.5, §5; `Architecture.md` Public Website note; this file; `PROJECT_STATE.md`.

**Migrations:** none. **Packages:** none. **Layers touched:** `RenoTrack.Website` and its tests only.

---

## 5b. Slice 2 — Site shell (D103)

**Design:** approved at the Slice 2 final gate, S2-1 to S2-9. The Figtree download was authorised as part of S2-3.

**Delivered:**
- **Marketing metadata.** `MarketingPageMetadata` is a sealed non-attribute marker, applied only by `MarketingPageConvention` (`/Impressum`, `/Datenschutz`, `/NichtGefunden`), registered only when `Site:PublicBaseUrl` is set. It is guarded against token routes twice: a page-path check, and a startup endpoint scan by `token` route parameter.
- **Request-time consumers read only the endpoint:** marketing headers (CSP, `Permissions-Policy`), canonical-path redirects, the layout of the shared legal pages, and the not-found page's render-or-bare-404.
- **Composed at startup only when the site is enabled:** 404 re-execution (404, GET/HEAD, never token routes) and the canonical-host redirect (derived `www` alias, 301/308, token path byte-preserved).
- **`_SiteLayout`** with head/header/footer/call-bar partials. The marketing layout reads a startup snapshot (`MarketingSite`), never `SiteOptions`.
- **`Site:Theme`** (validated hex, contrast minimums) and the generated `/site/theme.css`.
- **Figtree** 400/600/700 plus `OFL.txt`, self-hosted, and `marketing.css`.

**Font files** — downloaded from `https://cdn.jsdelivr.net/npm/@fontsource/figtree@5.3.0/`; sizes match the approved figures exactly:

| File | Bytes | SHA-256 |
|---|---|---|
| `figtree-latin-400-normal.woff2` | 11,384 | `8f98dd642986f1fa39c45b89665a57372897c235b36028e0e4a136e43dc5f8ab` |
| `figtree-latin-600-normal.woff2` | 11,544 | `367d713287918784702563518f59239989da815c80d3c7337686b9816635a08b` |
| `figtree-latin-700-normal.woff2` | 11,376 | `7ec4f08d09f91d349917dd6592f6aaae66d8fe1bbd58fa24707961e79236616e` |
| `OFL.txt` (package `LICENSE`) | 4,498 | `ee23e6c84000126692e112ff067b456470493349f993ceaa8b4d3766dd6fad5d` |

Git treats the woff2 files as binary, and `OFL.txt` contains no CR, so the committed bytes equal the hashed bytes.

**Design decisions taken at implementation review (Tech Lead, 2026-09-17):**
1. **Mobile phone CTA — accepted as an intentional design decision.** On narrow screens, the fixed mobile call bar is the primary phone CTA; the duplicate header phone CTA is hidden below 768px. The header button is not to be restored.
2. **Mobile navigation — accepted as deferred.** No empty mobile menu is built in Slice 2. The narrow-screen `<details>` navigation is introduced with the first real navigation entries (S2-6).

**Other differences from the design list:**
1. **Files beyond the design list:**
   - `Site/SiteLayout.cs` — partial paths and layout choice;
   - `Site/SiteNotFound.cs` — the re-execution handler;
   - `Site/MarketingSite.cs` — the startup snapshot;
   - `Site/SiteNavigation.cs`, split from `SitePage.cs`.

   No behaviour beyond the design.
2. **Slice 1 documentation defect fixed.** `DEPLOYMENT_CONFIGURATION.md` §2.2 had its "\* Required once the marketing site is enabled" footnote inserted mid-table in commit `ff1d6cb`, which cut the `LogoPath` row out of the table. The footnote now follows the table.

**Found while implementing:**
- A bodyless POST to an unknown address answers **405**, identically with the site disabled. It is existing routing behaviour, left untouched, and pinned against the disabled baseline.
- `MapStaticAssets` fingerprints stylesheet names (`/css/marketing.<hash>.css`).
- The `+` of a phone number renders as `&#x2B;` in `href`, as `CompanyIdentityRenderingTests` already documents.

**Tests — what was actually measured, and how.** There was **no single run in which all five projects passed.** Windows Application Control refused a freshly built binary in each full run (`0x800711C7`, the condition `PROJECT_STATE.md` already records), so the result is reported per project, with its run:

| Project | Before | After | Delta | Measured in |
|---|---|---|---|---|
| Website | 586 | **721** | **+135** | Release, non-deterministic build (see below) |
| Application | 470 | 470 | — | Release full run |
| Infrastructure (LocalDB) | 412 | 412 | — | Release full run |
| Api (LocalDB) | 478 | 478 | — | Release full run |
| Domain | 389 | 389 | — | Debug full run — Release was refused `RenoTrack.Domain.Tests.dll` on every retry |

**How each figure was obtained:**
- **Debug full run:** refused `RenoTrack.Application.dll`, so Application, Infrastructure and Api failed at load. Domain passed 389 and Website 720.
- **Release full run:** refused `RenoTrack.Domain.Tests.dll`. Application 470, Infrastructure 412, Api 478 and Website 720 passed.
- **After accessibility QA added one test:** the rebuilt `RenoTrack.Website.Tests.dll` was refused in both Debug and Release on every retry. Built with `-p:Deterministic=false` — same source, different bytes — it loaded and passed **721/721**.
- **Domain, Application, Infrastructure and Api are not changed by Slice 2** (`git diff` is empty for all four projects and their tests). Their figures equal the Slice 1 baseline, and the environmental limitation is stated here rather than folded into one total.

The +135 by class:
- `CanonicalRedirectTests` 36
- `SiteFormattingTests` 19
- `ThemeOptionsTests` 19
- `SiteLayoutTests` 18
- `SiteNotFoundTests` 13
- `MarketingPageGuardTests` 11
- `MarketingSecurityHeadersTests` 11
- `MarketingPageMetadataTests` 8

Build: 0 warnings, 0 errors, in both Debug and Release. **No existing test file was edited.** The only change under `tests/` besides new files is `Site:Theme` added to the Alpha fixture pack.

**Mutation spot checks** — each applied to the finished code, built, run, then restored and byte-compared:

| Mutation | Tests failing |
|---|---|
| Convention registered even when the site is disabled | 15 — incl. 9 existing `LegalPageTests`/`CompanyIdentityRenderingTests` and the disabled-site endpoint enumeration |
| Both token guards removed and `/Angebot` listed as a marketing page | 12 |
| Host redirect lower-cases the path | 3 |
| Protocol-relative (`//`, `/\`) guard removed | 2 — unit tests only |
| Headers middleware re-reads `SiteOptions` per request | 1 — the DI-replacement test |

The protocol-relative guard is proven at unit level only: host-level, such a path never matches a marketing endpoint, so no host test can reach it (recorded in D103).

**Manual browser QA** — `dotnet publish -c Release` output, Production environment, fictional Alpha pack, in-app browser:

| Check | Result |
|---|---|
| Rendering | `/impressum` renders in `_SiteLayout`: title `Impressum \| Alpha Testbetrieb (Testdaten)`, one `h1`, zero scripts, canonical `https://www.alpha-testbetrieb.test/impressum`, theme colours `#1F4B7A` / `#E8C27A` applied |
| Fonts | Figtree 400/600/700 all loaded; `document.fonts.check` confirms Figtree covers ä ö ü Ä Ö Ü ß € – ² |
| Network | Every resource same-origin: three fonts, `marketing.<hash>.css`, `theme.css?v=…`, the logo |
| CSP enforced by the browser | An in-page `fetch` was refused with "violates … connect-src 'none'" |
| Keyboard | Tab 1 focuses "Zum Inhalt springen" with a 3 px solid outline at the top. Enter moves to `#inhalt`; the next Tab reaches the first focusable element after `main`. |
| 320 px | No horizontal overflow; call bar shown (56 px target); body padding 56 px; header phone hidden; footer 1 column |
| 375 / 414 px | No overflow; call bar shown; 1 column |
| 768 px | No overflow; call bar hidden; header phone shown; 3 columns |
| 1024 px | No overflow; 4 columns |
| 1366 / 1920 px | No overflow; 4 columns; content container capped at 1,152 px |
| Site 404 | `/Unbekannt/Kx9Qm2Zt7Lp4Wv8Rb1Nc6Hd3` → site 404 page, `noindex`, no canonical, footer present, probe string not reflected |
| Canonical path | `/Impressum/` lands on `/impressum` |
| Token page | `/angebot/QaToken…` → `_CustomerLayout`, `customer.<hash>.css` only, `noindex, nofollow, noarchive`, 503 (API deliberately unreachable in QA) |
| Logs | Neither the token nor the 404 probe string appears in the server log |

Screenshots sometimes timed out in the pane, so most widths there were verified by DOM measurement rather than by image.

**Accessibility QA required for closure** — performed in headless Microsoft Edge, driven over the Chrome DevTools Protocol against the same published build with the Alpha pack. The in-app pane cannot emulate any of these. Evidence (PDFs, screenshots, JSON results) was kept in the session scratchpad and is not committed.

| Check | How it was performed | Result |
|---|---|---|
| **Print** | The browser's real print pipeline (`Page.printToPDF`, A4) for `/impressum` and the 404 page, **read back as PDF**; computed styles under `print` media | **Defect found and fixed.** The first PDF printed the footer's phone number, email and Impressum link **white on the removed background** — present in the text layer, invisible on paper. Cause: the print rule `.site-footer a` lost to the screen rule `.site-footer .site-footer-link`. Fixed with a selector naming the specific class, and pinned by a new test. **Re-run after the fix:** every footer link computes `rgb(0, 0, 0)` in print and is visible in the PDF. The call bar, header phone and skip link are `display: none`; body bottom padding is `0px`; address and heading are visible. |
| **prefers-reduced-motion** | Media feature emulated as `reduce`; every element under `.site` inspected | `matchMedia` reports `reduce`. **0** elements with a non-zero `transition-duration`, **0** with an `animation-name`, **0** with smooth scrolling, **0** running animations (46 elements). Without the emulation there were also 0 transitions and 0 animations: the shell uses no motion, and the rule guards future additions. |
| **200% text-only resize** | The browser's own font-size setting doubled (`Page.setFontSizes`, standard 16 → 32 px), layout zoom unchanged | Root font 32 px, body 34 px. **At 1280 px:** no horizontal overflow, nothing clipped by `overflow: hidden`, no element past the viewport, email link inside it, footer 4 columns. **At 375 px:** the same, with the company name wrapping onto 3 lines and the call bar growing with its `rem` height. |
| **True 400% zoom** | The browser's **page zoom** preference set to 400% for a 1280×1024 window, which is how Chromium zooms a page | **Confirmed as real zoom, not a narrow viewport:** CSS viewport **314 px** (1280 ÷ 4, less the scrollbar), `devicePixelRatio` **4**. `/impressum` and the 404 page both reflow to one column with no horizontal overflow, no clipping, no element past the viewport; the call bar is shown and the header phone hidden. **Method correction:** a first attempt with `--force-device-scale-factor=4` left the CSS viewport at 1256 px, so it was not zoom and its result was discarded. |
| **Focus not obscured at 400% zoom and 200% text** (WCAG 2.2 2.4.11, prompted by the call bar taking about a quarter of the zoomed viewport) | **Real Tab key presses** through `/impressum` | Every focus stop visible: skip link at top 8 px; footer phone, email and Impressum links scrolled into view with bottoms at or above 169 px against a call bar starting at 177 px; call-bar link itself last. No focused element covered by the bar. An earlier probe using programmatic `focus()` flagged the skip link as off-screen; the key-driven run — what a keyboard user actually does — shows it at the top. |

**Migrations:** none. **Packages:** none; font files are static assets. **Layers touched:** `RenoTrack.Website` and its tests only. `.claude/launch.json` received a temporary QA entry and was restored byte-for-byte (no diff).

**Documentation:**
- `ARCHITECTURE_DECISIONS.md` **D103**;
- `CLAUDE.md` §25 additions;
- `CONTENT_PACK.md` (`Site:Theme`, example);
- `DEPLOYMENT_CONFIGURATION.md` (`Site:Theme` row, `AllowedHosts`/certificate/`Host` pre-launch item, §2.2 table repair);
- `appsettings.json` (`//Theme`);
- this file; `PROJECT_STATE.md`.

---

## 5c. Slice 3 — Homepage (D104)

**Design:** approved at the Slice 3 gate, S3-1 to S3-11, with the Tech Lead's corrections:
- **C1:** `Site:Home:MetaTitle` is the homepage `<title>`, with no `DisplayName` fallback.
- **C2:** the owner is stated as `Inhaber: {OwnerName}`, never "Ihr Ansprechpartner".

**S3-11** approved adding `MetaTitle` to enabled-site test inputs.

**Delivered:**
- **`/`** — `Pages/Startseite` (`@page "/"`). It is marketing only through the convention; with the site disabled it is a bare 404, and `/Index` and `/startseite` do not exist.
- **Sections:**
  - hero: `h1` from `Headline` or the company name; `Subheadline`; "Einsatzgebiet: …"; `tel:` button from 768 px and `mailto:` button;
  - services: all, in pack order, name and summary, unlinked;
  - "Ihre Vorteile" plus `Inhaber:` line (optional);
  - "So läuft es ab" as an `<ol>` (optional);
  - "Kontakt aufnehmen": phone, email, hours and notes.
- **Head:** `<title>` and `og:title` = `MetaTitle` verbatim; description = `Subheadline`; canonical `origin/`; indexable.
- **Shell:**
  - `SitePage.SetDocumentTitle` and `SetFullWidth`;
  - "Startseite" as the first and only navigation entry, a plain list at every width with `aria-current`;
  - brand linked to `/`;
  - `MarketingSite` snapshot of `Home` and `Services`.

**Frozen for Slice 5 — hero image requirements** (not built):
- an owner-approved photo of the company's own work with recorded provenance; no stock, no AI, no rendering presented as work;
- no identifiable person without consent;
- EXIF, GPS and XMP stripped, with a test;
- WebP and JPEG at 480, 960 and 1600 px;
- `<picture>` with `srcset`/`sizes`, explicit `width`/`height`, `fetchpriority="high"`, never lazy, never a CSS background;
- alt text required;
- captions state no place or date unless the owner confirms it;
- headline text never over the photo.

**Files changed (production):**
- **New:**
  - `Content/HomePageOptions.cs` (with `HomeItemOptions`)
  - `Pages/Startseite.cshtml`
  - `Pages/Startseite.cshtml.cs`
- **Modified:**
  - `Content/SiteOptions.cs`
  - `Content/ContentListSourceGuard.cs`
  - `Site/MarketingPageConvention.cs`
  - `Site/MarketingSite.cs`
  - `Site/SiteNavigation.cs`
  - `Site/SitePage.cs`
  - `Pages/Shared/Site/_SiteLayout.cshtml`
  - `Pages/Shared/Site/_SiteHead.cshtml`
  - `Pages/Shared/Site/_SiteHeader.cshtml`
  - `wwwroot/css/marketing.css`
  - `appsettings.json` (documentation keys only)

**Differences from the design's file list:**
1. **`ContentListSourceGuard.cs` changed.** The design said the guard covered the new lists automatically; it holds an explicit list.
2. **`Program.cs` unchanged.** No startup log line was needed: `Home` is validated inside `SiteOptions.Validate`.
3. **No `SiteNavigationTests.cs`.** The existing `SiteLayoutTests.Every_primary_navigation_link_answers_200`, vacuous in Slice 2, now carries that proof, together with a homepage-wide same-origin link test.
4. **`ContentPackStartupTests.cs` unchanged — an intentional S3-11 deviation, accepted at final review (Tech Lead).** S3-11 approved adding `MetaTitle` to this file's enabled-site input. That input belongs to `An_enabled_site_with_an_incomplete_identity_fails_startup_naming_every_missing_key`, a startup-failure test whose purpose is an incomplete enabled site. Supplying `MetaTitle` there would add nothing, and the test's intent is preserved as written: startup still fails naming every missing key, now including `Site:Home:MetaTitle`. `SiteOptionsTests.cs` received the approved input change.
5. **Four Slice 2 test files changed beyond the S3-11 approval**, because each pinned a "no homepage yet" state the approved design necessarily ends. Intent kept, nothing deleted:
   - `MarketingPageGuardTests`: page-path list gains `/Startseite`;
   - `MarketingPageMetadataTests`: the marked endpoint set gains `""` (the `/` route);
   - `SiteNotFoundTests`: the unknown-address row `/` becomes `/index`;
   - `SiteLayoutTests`: "no empty navigation" becomes "navigation lists only the pages that exist".
6. **Print — corrected at pre-commit review (Tech Lead).** The first implementation hid the contact buttons in print and relied on the footer. That deviation was **not accepted**, so the design now holds:
   - each contact action prints as text, as "Anrufen: <phone>" and "E-Mail schreiben: <address>", in the hero and the contact section;
   - the address is carried by a `home-print-only` span, which is `display: none` on screen, so the visible label and the accessible name stay "E-Mail schreiben";
   - only the call bar is hidden on paper.

**Tests (measured, not derived):**

| Project | Before | After | Delta | Measured in |
|---|---|---|---|---|
| Website | 721 | **800** | **+79** | Release (baseline 721 re-measured before any change) |
| Domain | 389 | 389 | — | Release |
| Application | 470 | 470 | — | Release |
| Infrastructure (LocalDB) | 412 | 412 | — | Release |
| Api (LocalDB) | 478 | 478 | — | Release |

- **+79 by class:** `HomePageTests` 41, `HomePageOptionsTests` 38, each counted with a class filter. Existing classes keep their counts.
- **Build:** 0 warnings, 0 errors.
- **Application Control:** freshly built Website binaries were refused repeatedly (`0x800711C7`), the known condition. Repeated non-deterministic rebuilds loaded, and no green figure above comes from a run that did not execute.

**Mutation checks** — each applied to the finished code, built, run, then restored and byte-compared (all restored identical):

| Mutation | Tests failing |
|---|---|
| Page model ignores `IsMarketingPage` | 4 — incl. the unedited `ScaffoldRemovalTests` root and the disabled-site root tests |
| Offerings rendered | 1 |
| Service cards linked to `/leistungen/{slug}` | 5 |
| Headline through `Html.Raw` | 1 |
| Navigation entry to a missing page | 4 |
| **C1:** homepage title falls back to `"{…} | {DisplayName}"` | 3 |
| **C1:** `MetaTitle` not required | 3 (a first `if (false)` form did not compile under warnings-as-errors and was rewritten) |
| **C2:** label reverted to "Ihr Ansprechpartner" | 1 |
| Print hides the contact actions again | 1 |
| Email address removed from the hero's email action | 2 |

**Also verified:** the complete example in `CONTENT_PACK.md` §5 boots and renders its `MetaTitle` and `Inhaber:` line, checked with a throwaway test that was deleted afterwards.

**Browser QA** — `dotnet publish -c Release` output, Production environment, fictional Alpha pack, plus a five-service variant:

| Check | Result |
|---|---|
| Rendering | `<title>` = `MetaTitle`; one `h1`; `h2`/`h3` hierarchy without gaps; 0 `<script>`, 0 `[style]`; canonical `https://www.alpha-testbetrieb.test/`; `aria-current="page"` on Startseite |
| Network | Six same-origin resources (three Figtree weights, `marketing.<hash>.css`, `theme.css`, logo); nothing off-origin |
| Widths, Alpha | 320 / 375 / 414 / 768 / 1024 / 1366 / 1920: no horizontal overflow, no element past the viewport. Below 768 px: hero phone hidden, call bar shown, actions stacked, 1 column, 4 process rows. From 768 px: hero phone shown, call bar hidden. Process in one row from 1024 px. Header one 73 px row at every width. |
| Five services | 1 column below 768 px; 2 + 2 + 1 at 768; 3 + 2 left-aligned from 1024 through 1920; no overflow |
| Print (real `printToPDF`, read back), **re-run after the pre-commit correction** | The PDF shows "Anrufen: +49 000 1111111" and "E-Mail schreiben: kontakt@alpha-testbetrieb.test" under the hero and again under "Kontakt aufnehmen". All four action links compute black text, no background, no border. Hero and warm band print black on white; step numbers outlined in black; call bar and navigation hidden; footer links black. On screen the address span is `display: none`, the visible text stays "E-Mail schreiben", and the accessibility tree still names both links "E-Mail schreiben" |
| Reduced motion (emulated) | 109 elements after the correction (107 before): 0 transitions, 0 animations, 0 smooth scrolling, 0 running animations |
| 200% text (font-size setting) | 1280 and 375 px: root 32 px, no overflow, nothing clipped, nothing past the viewport; every Tab stop visible. **Defect found and fixed:** at 375 px the brand link was 1,105 px tall (header squeezed by the new navigation entry); after the fix it is 128 px |
| True 400% zoom (page-zoom preference) | CSS viewport 310 px at `devicePixelRatio` 4: no overflow, nothing clipped. **Defect found and fixed:** Tab stopped on the contact "E-Mail schreiben" at 155–202 px against the call bar at 177 px; with `scroll-padding-bottom` every stop clears the bar |
| Token page | `/angebot/<probe>` still `_CustomerLayout`, `customer.<hash>.css` only, `no-store` / `noindex, nofollow, noarchive`, no CSP added; 503 with the API deliberately unreachable |
| Site 404, `/Index` | 404; probe not reflected; `noindex` |
| Logs | Neither the token probe, the 404 probe nor any homepage content value appears; the startup line reports counts only |

Screenshots in the in-app pane timed out as in Slice 2, so widths were verified by DOM measurement. Evidence (PDF, JSON) stays in the session scratchpad, uncommitted. No temporary launch entry was left: `.claude/launch.json` is byte-identical.

**Migrations:** none. **Packages:** none. **Layers touched:** `RenoTrack.Website` and its tests only.

**Documentation:**
- `ARCHITECTURE_DECISIONS.md` **D104**;
- `CLAUDE.md` §25 additions;
- `CONTENT_PACK.md` §3.1, §5, §6;
- `DEPLOYMENT_CONFIGURATION.md` (`Site:Home` rows, list rule);
- `appsettings.json` (`//Home`);
- this file; `PROJECT_STATE.md`.

---

## 6. Open actions

- **Narrow-screen `<details>` navigation:** to be built when there are several real primary navigation entries (Tech Lead decision at the Slice 3 gate). The homepage is the first entry and is a plain list.

- **Slice 1 mutation gap — accepted as documented (Tech Lead, 2026-09-16).** No test-only seam is added: validation and the independently tested filter overlap, and an unobservable call removal is not a production security gap.

- **Transfer company-specific records to the private content repository once it exists:** the design review's image inventory and classification, and the company-specific legal verification findings. These are deliberately not recorded here (D100).
- **ADRs** for the content pack, marketing layout and script policy, the inquiry → Lead mapping, the anonymous-endpoint hardening, map consent, and the discoverability strategy. Each is written in the slice that implements it.
