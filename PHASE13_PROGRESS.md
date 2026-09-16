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
| 2 | Site shell (`_SiteLayout`, tokens, header/footer, marketing headers + CSP, host/lowercase 301s) | ⏸ |
| 3 | Homepage | ⏸ |
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

## 6. Open actions

- **Slice 1 mutation gap — accepted as documented (Tech Lead, 2026-09-16).** No test-only seam is added: validation and the independently tested filter overlap, and an unobservable call removal is not a production security gap.

- **Transfer company-specific records to the private content repository once it exists:** the design review's image inventory and classification, and the company-specific legal verification findings. These are deliberately not recorded here (D100).
- **ADRs** for the content pack, marketing layout and script policy, the inquiry → Lead mapping, the anonymous-endpoint hardening, map consent, and the discoverability strategy. Each is written in the slice that implements it.
