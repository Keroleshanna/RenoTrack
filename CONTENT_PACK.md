# CONTENT_PACK.md — The Company Content Pack Schema

**Status:** Living document, introduced in Phase 13 Slice 1 (**D102**). Extended in the slice that adds each new field.

**What this is.** The schema a company's private content repository is written against. The pack holds one company's identity, marketing-site content and legal text, and it lives **outside this repository and outside the application**. This file describes the *shape* only: every value below is fictional, on reserved `.test` domains and `+49 000` numbers. **Never paste real company content into this file** (D100).

Deployment wiring (where the pack is, what happens when it is absent) is in `DEPLOYMENT_CONFIGURATION.md` §2.5.

---

## 1. Layout and allowed sections

```
<ContentPack:RootPath>/
├─ site.json    required
├─ legal.json   optional
└─ brand/       optional — logo files, served at /brand/<file>
```

| File | May contain **only** these top-level sections |
|---|---|
| `site.json` | `CompanyIdentity`, `Site` |
| `legal.json` | `Legal` |

**Anything else fails startup**, naming the file and the section, never its value. That includes product settings (`Logging`, `PublicApi`, `ConnectionStrings`, …), the other file's section, and a top-level `"//"` comment key. **Comments go inside an allowed section**, where they are ignored:

```json
{
  "CompanyIdentity": {
    "//": "Comments live inside a section, never at the top level.",
    "DisplayName": "Beispielbetrieb (Testdaten)"
  }
}
```

A file must be valid JSON with an object at the top level and no duplicate keys (compared case-insensitively). An allowed section must be an object with content: `"Site": "x"`, `"Site": {}` and an empty file are refused. `legal.json` may be absent, but not present and empty.

---

## 2. `site.json` → `CompanyIdentity`

| Key | Type | Rules |
|---|---|---|
| `DisplayName` | text ≤ 100 | The company name, exactly as it should appear everywhere. **Required for a marketing site.** |
| `OwnerName` | text ≤ 100 | Optional. |
| `ContactPhone` | text | International form: `+`, country code, digit groups separated by single spaces, 8–15 digits. **Required for a marketing site.** |
| `ContactEmail` | text ≤ 254 | Exactly one plain address. **Required for a marketing site.** |
| `LogoPath` | text | Under `/brand/`, e.g. `/brand/logo.svg`; the file goes in the pack's `brand/`. Requires `DisplayName`. |
| `LogoOnDarkPath` | text | Optional (Slice 5v, **D107**). The reversed variant for dark surfaces — the marketing footer. Same `/brand/` rules as `LogoPath`, named by its own key when it fails. **Absent is a finished state:** the company name alone is the brand treatment. The accent brand zone in the header gets no mark at all (**D108**), because a mark in the brand's own colours loses whichever parts match its surface. |
| `Address.StreetAddress` | text ≤ 100 | All four address parts or none. **Required for a marketing site.** |
| `Address.PostalCode` | text ≤ 10 | Letters, digits, spaces, hyphens. |
| `Address.Locality` | text ≤ 80 | |
| `Address.CountryCode` | text | ISO 3166-1 alpha-2, upper case (`DE`). |
| `OpeningHours[]` | list | Each `{ "Days": [...], "Opens": "HH:mm", "Closes": "HH:mm" }`. Day names in English, exactly (`Monday` … `Sunday`). A day appears in at most one block. `Closes` later than `Opens`. **Regular spans only.** |
| `OpeningHoursNote` | text ≤ 200 | What a span cannot say, e.g. "Samstag nach Vereinbarung". Shown to people; never published as machine-readable hours. |
| `ServiceArea.Places[]` | list | Each `{ "Name": text ≤ 80, "Kind": "City" \| "Region" }`. Names unique ignoring case. |
| `ServiceArea.Note` | text ≤ 300 | Optional, e.g. that larger projects are taken on further away. |

"Text" is single-line plain text throughout: no control characters (line breaks, tabs), no markup. Everything is rendered encoded; HTML is displayed, never interpreted.

## 3. `site.json` → `Site`

| Key | Type | Rules |
|---|---|---|
| `PublicBaseUrl` | text | **The marketing site's switch.** Absent: no marketing site. Present: an HTTPS origin only — scheme and host (and a port if genuinely needed), no path, query, fragment or user info. |
| `Services[]` | list | **At least one for a marketing site.** Validated whenever present. |
| `Services[].Slug` | text ≤ 40 | Lowercase ASCII letters and digits, single hyphens: `tueren`, not `Türen`. Unique. **Becomes a URL — keep it stable.** |
| `Services[].Name` | text ≤ 60 | Unique ignoring case. |
| `Services[].Summary` | text ≤ 300 | Required. One or two plain sentences. |
| `Services[].Offerings[]` | list of text ≤ 160 | **1–12**; none blank. What the company actually offers, one item each. Shown as the service page's "Leistungsumfang" list (Slice 4, D105); the limit keeps that list scannable. |
| `Services[].MetaTitle` | text ≤ 70 | **Required for a marketing site** (Slice 4, D105). The service page's `<title>` and `og:title`, used **verbatim**. There is no fallback to `"{Name} \| {DisplayName}"`. See §6. |
| `Services[].Headline` | text ≤ 90 | Optional. The service page's one `h1`. Absent: `Name`. |
| `Services[].MetaDescription` | text ≤ 160 | Optional. The service page's meta description and `og:description`. Absent: none is rendered. `Summary` is never used for this, because code does not shorten company text. |
| `Services[].Sections[]` | list | Optional: none, or **1–4** blocks `{ "Heading": text ≤ 80, "Paragraphs": [ 1–4 × text ≤ 600 ] }`, in order. They describe the service and its typical uses on its page. Absent: no descriptive blocks. |
| `Theme.PrimaryColor` | `#RRGGBB` | Optional (Slice 2, D103). Must reach **4.5:1 against white** — it carries white button and link text. Absent: a neutral product default. |
| `Theme.AccentColor` | `#RRGGBB` | Optional. Must reach **4.5:1 against the derived dark surface** (**D107**, raised from 3:1): it now fills the primary button and carries that button's label. Absent: a neutral product default. |

**Two values, six published roles (D107).** From these two colours the application derives the dark band, a second dark band, accent text for dark surfaces and accent text for light ones, and publishes all six in `/site/theme.css`. The derivation is deterministic and every pairing is contrast-checked at startup, so a company supplies two colours and the whole visual system follows.

Colours are served as the generated `/site/theme.css`; a pack never supplies CSS, and a colour that fails its contrast minimum fails startup naming the key.

### 3.1 `Site:Home` — the homepage (Slice 3, D104)

| Key | Type | Rules |
|---|---|---|
| `Home.MetaTitle` | text ≤ 70 | **Required for a marketing site.** The homepage's `<title>` and `og:title`, used **verbatim** — no company-name suffix is added. Say what the company does and where it works (see §6). It never falls back to `DisplayName`. |
| `Home.Headline` | text ≤ 90 | Optional. The homepage's one `h1`. Absent: `DisplayName`. |
| `Home.Subheadline` | text ≤ 160 | Optional. Shown under the headline, and used as the meta description and `og:description`. Absent: none of them is rendered. |
| `Home.Advantages[]` | list | Optional: none, or **2–6** entries `{ "Title": text ≤ 60, "Text": text ≤ 200 }`, both required. Absent: no "Ihre Vorteile" section. When `CompanyIdentity:OwnerName` is set, the section also shows `Inhaber: <OwnerName>`. |
| `Home.Process[]` | list | Optional: none, or **2–6** steps `{ "Title": text ≤ 60, "Text": text ≤ 200 }`, in order. Absent: no "So läuft es ab" section. |

Both lists must come from one configuration source, like `Services`. A supplied-but-blank value is refused rather than rendered empty.

### 3.2 `Site:ServicesPage` and the service pages (Slice 4, D105)

| Key | Type | Rules |
|---|---|---|
| `ServicesPage.MetaTitle` | text ≤ 70 | **Required for a marketing site.** The `/leistungen` overview's `<title>` and `og:title`, used **verbatim**, with no fallback. |
| `ServicesPage.Headline` | text ≤ 90 | Optional. The overview's `h1`. Absent: "Leistungen". |
| `ServicesPage.Intro` | text ≤ 160 | Optional. Shown under the headline, and used as the overview's meta description. Absent: neither is rendered. |

**What the pages are.** `/leistungen` lists every service in pack order, each card linking to `/leistungen/<Slug>`. Each service page shows:
- a breadcrumb;
- a hero with `Headline` (or `Name`), `Summary`, the company's service area and the contact actions;
- "Leistungsumfang" (`Offerings`);
- `Sections`;
- the contact section;
- "Weitere Leistungen", linking the other services.

Every service is also linked from the footer of every page.

**Rules the application enforces:**
- **Every page has its own title and description.** The homepage, the overview and every service must not share a `MetaTitle`, and no two supplied descriptions (`Home.Subheadline`, `ServicesPage.Intro`, `Services[].MetaDescription`) may match. Comparison ignores case and surrounding spaces, and a refusal names both keys, never the text.
- **A slug is matched exactly.** `/leistungen/waende` finds the service whose `Slug` is `waende`, and nothing else does. There is no case folding, no transliteration (`wände` is not `waende`), no closest match and no fallback. An upper-case or trailing-slash address is permanently redirected to its lower-case form first.
- **Changing a slug breaks its old address.** The old URL answers 404, and there are no redirects from old slugs. Choose slugs once. The inquiry flow (Slice 8) will use the same key.
- **No images yet.** Service photos arrive with the approved-media pipeline (Slice 5). Until then a service page is text only, and must read as complete that way, because some services may never have approved photos.
- **The service area is the company's**, from `CompanyIdentity:ServiceArea`, shown on every service page. There is no per-service area and there are no pages per town.
- **Nested lists** (`Offerings`, `Sections`, `Paragraphs`) are part of `Site:Services` and must come from the same single configuration source.

### 3.3 `Site:Media` and photo references (Slice 5a, D106)

The pack's `media/` directory holds **finished derivatives only**, produced by `tools/RenoTrack.MediaPrep` (see `MEDIA_PREPARATION.md`). Never put source photos there.

```
<ContentPack:RootPath>/media/{Id}-480.webp   {Id}-960.webp   {Id}-1600.webp
                             {Id}-480.jpg    {Id}-960.jpg    {Id}-1600.jpg    ({Id}-og.jpg)
```

| Key | Type | Rules |
|---|---|---|
| `Media[].Id` | text ≤ 60 | Lowercase ASCII letters and digits, single hyphens; unique. Describes what the photo shows. Its derivative file names are built from it. **A replacement photo gets a new id.** |
| `Media[].Alt` | text ≤ 150 | **Required.** Alt text describes the actual image for accessibility, and also gives search engines and other machine consumers useful context. It is not a keyword field. |
| `Media[].Caption` | text ≤ 200 | Optional; must not repeat `Alt`. Rendered by the projects gallery (Slice 5b), not by the hero or service photos. |
| `Media[].SourceRef` | text ≤ 40 | **Required.** An opaque reference into the company's private approval register (lowercase ASCII, digits, hyphens). Never rendered, never logged. |
| `Media[].HasSocialImage` | bool | `true` when `{Id}-og.jpg` exists. A page using this photo then declares it as its `og:image`. |
| `Home.HeroImage` | media id | Optional. The homepage hero's photo. Absent: the text-only hero. |
| `Services[].Image` | media id | Optional. The service page hero's photo. Absent: that service's text-only hero. |

**Rules the application enforces:**
- **Startup verifies every derivative of every listed photo** and refuses to run if one is missing, over its byte budget, the wrong format or size, or carries any metadata (EXIF including GPS, XMP, IPTC, comments, appended data).
- **Only listed files are served**, at `/medien/`. A file in `media/` that no entry lists answers 404.
- **Every listed photo must be used** by `Home.HeroImage` or a service's `Image`, and every reference must name a listed photo. A listed but unused photo fails startup, because listing it publishes it.
- **Card photos are all-or-nothing.** Service cards show photos only when *every* service has an `Image`. Otherwise every card stays text-only, on every page.
- **No fallback `og:image`.** A page without its own photo with a social image declares none.
- **`Media` is a list** and must come from one configuration source.

## 4. `legal.json` → `Legal`

Unchanged from D100: `Legal.Impressum` and `Legal.Datenschutz`, each `{ "Sections": [ { "Heading", "Paragraphs": [ { "Text", "LinkText", "LinkUrl" } ] } ] }`. `LinkText` and `LinkUrl` come together; link schemes are `http`, `https`, `mailto`, `tel`, or a site-relative path. A document with no content is a 404, not an empty page. See `DEPLOYMENT_CONFIGURATION.md` §2.3.

---

## 5. A complete fictional example

```json
{
  "CompanyIdentity": {
    "DisplayName": "Beispielbetrieb (Testdaten)",
    "OwnerName": "Testperson",
    "ContactPhone": "+49 000 1234567",
    "ContactEmail": "kontakt@beispielbetrieb.test",
    "LogoPath": "/brand/logo.svg",
    "LogoOnDarkPath": "/brand/logo-invers.svg",
    "Address": { "StreetAddress": "Teststraße 1", "PostalCode": "00000", "Locality": "Testort", "CountryCode": "DE" },
    "OpeningHours": [
      { "Days": [ "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" ], "Opens": "08:00", "Closes": "17:00" }
    ],
    "OpeningHoursNote": "Samstag nach Vereinbarung",
    "ServiceArea": {
      "Places": [ { "Name": "Testort", "Kind": "City" }, { "Name": "Testregion", "Kind": "Region" } ],
      "Note": "Größere Projekte auch in weiterer Entfernung nach Absprache."
    }
  },
  "Site": {
    "PublicBaseUrl": "https://beispielbetrieb.test",
    "Theme": { "PrimaryColor": "#1F4B7A", "AccentColor": "#E8C27A" },
    "Home": {
      "MetaTitle": "Testleistungen in Testort und Testregion",
      "Headline": "Erfundene Testleistungen für Testort und Testregion",
      "Subheadline": "Eine erfundene Einleitung zur Veranschaulichung des Schemas.",
      "Advantages": [
        { "Title": "Erster erfundener Vorteil", "Text": "Ein Satz, der den Vorteil erklärt." },
        { "Title": "Zweiter erfundener Vorteil", "Text": "Noch ein Satz, der den Vorteil erklärt." }
      ],
      "Process": [
        { "Title": "Erster Schritt", "Text": "Was in diesem Schritt geschieht." },
        { "Title": "Zweiter Schritt", "Text": "Was danach geschieht." }
      ]
    },
    "ServicesPage": {
      "MetaTitle": "Alle Testleistungen in Testort und Testregion",
      "Intro": "Eine erfundene Einleitung zur Leistungsübersicht."
    },
    "Services": [
      {
        "Slug": "test-leistung",
        "Name": "Testleistung",
        "Summary": "Eine erfundene Leistung zur Veranschaulichung des Schemas.",
        "Offerings": [ "Erstes erfundenes Angebot", "Zweites erfundenes Angebot" ],
        "MetaTitle": "Testleistung in Testort und Testregion",
        "MetaDescription": "Eine erfundene Beschreibung der Testleistung.",
        "Sections": [
          { "Heading": "Erfundener Abschnitt", "Paragraphs": [ "Ein Absatz, der die Leistung beschreibt." ] }
        ]
      }
    ]
  }
}
```

---

## 6. Rules for the content repository

- **Validation checks shape, not truth.** The application refuses what cannot be rendered as intended; whether a fact is correct is verified in the content repository before it is deployed.
- **Invent nothing.** A missing fact is left out; it is never filled with a plausible placeholder.
- **No secrets.** The pack is public-facing content. No key in it can hold a credential, and a pack that tries to set product configuration is refused.
- **One spelling per fact.** The name, phone number and address are written once here and derived everywhere else.
- **Homepage copy (D104) — checked in review, because code cannot check it:**
  - **`MetaTitle` says what the company does and where it works**, in natural German, with the company name (if used) spelled exactly as `DisplayName`. No keyword lists, no chains of place names.
  - **No unverified claims** in the title, headline, subheadline, advantages or process: no superlatives or rankings ("Nr. 1", "bester", "führend"), no prices, "kostenlos", "Festpreis" or discounts, no response-time promises, and no ratings, review counts, years in business, project counts or certifications.
  - **The headline states what the company does**, not a quality slogan. The service area is shown from `ServiceArea`, not repeated as a city list in the copy.
  - **The owner is shown only as `Inhaber: <OwnerName>`** — a restatement of the identity field. The site attaches no role, availability or contact claim to the person.
- **Service copy (D105) — checked in review, because code cannot check it:**
  - **Each `MetaTitle` names the service and the region** in natural German, e.g. the trade and the main town or region. No keyword lists, no chains of towns, no second town-specific variant of the same page.
  - **One service entry per real service the company performs.** Never an entry per town, and never an entry for work the company does not do.
  - **`Offerings` and `Sections` describe what is actually done.** Typical uses are fine; invented project details, customer situations or results are not.
  - **No unverified claims** anywhere in the service copy:
    - no Meister or Meisterbetrieb claim, and no other qualification, certification, norm or standard compliance unless verified;
    - no manufacturer, brand-partner or dealer claims unless verified;
    - no guarantee or warranty wording beyond what is verified;
    - no prices, "kostenlos", "Festpreis" or discounts, and no response-time promises;
    - no superlatives, rankings, ratings, review counts, years in business or project counts.
  - **Slugs are plain ASCII forms of the service name** (`waende`, `innenausbau`), chosen once and kept.
- **Photos (D106) — checked in review and in the private approval register, because code cannot check it:**
  - **Only owner-approved photographs of the company's own work.** No stock, no AI-generated image, no rendering presented as work.
  - **The privacy checklist in `MEDIA_PREPARATION.md` §2 is completed for every photo** before it gets an id: no identifiable person without recorded consent, no house number, number plate, sign, document or other detail that identifies a customer or a property.
  - **Alt text describes the actual image for accessibility, and also gives search engines and other machine consumers useful context. It is not a keyword field.** No place names, company name or service list added to it.
  - **Ids and captions state no customer, place, street or date** unless the owner has confirmed it and it is safe to publish.
  - **A service without an approved photo has no `Image`.** Its text-only layout is complete. Never borrow another service's photo to fill the gap, because it would show work the page is not about.
  - **A replacement photo is a new id.** Remove the old entry and its files.

The §5 example deliberately lists no photos, because a pack with photos needs their derivatives in `media/`. §3.3 shows the shape.
