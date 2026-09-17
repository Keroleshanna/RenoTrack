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
| `Services[].Offerings[]` | list of text ≤ 160 | At least one; none blank. What the company actually offers, one item each. |
| `Theme.PrimaryColor` | `#RRGGBB` | Optional (Slice 2, D103). Must reach **4.5:1 against white** — it carries white button and link text. Absent: a neutral product default. |
| `Theme.AccentColor` | `#RRGGBB` | Optional. Must reach **3:1 against the effective primary** — used for decoration, borders and large text only. Absent: a neutral product default. |

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
    "Services": [
      {
        "Slug": "test-leistung",
        "Name": "Testleistung",
        "Summary": "Eine erfundene Leistung zur Veranschaulichung des Schemas.",
        "Offerings": [ "Erstes erfundenes Angebot", "Zweites erfundenes Angebot" ]
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
