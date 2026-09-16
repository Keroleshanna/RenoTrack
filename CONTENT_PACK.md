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
