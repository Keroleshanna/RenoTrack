# DEPLOYMENT_CONFIGURATION.md — What a Deployment Must Supply

**Status:** Living document, introduced in Phase 11 Slice 7 (**D100**).

**What this is for.** No company-specific value is committed to this repository — not a name, an address, a phone number, an email address, a logo, a sentence of legal text, a host, or a credential. That is a deliberate, repeatedly-taken decision (SRS **OQ-3b**, Phase 11 **Q7**, `ARCHITECTURE_DECISIONS.md` **D68**, **D100**), not an omission to be tidied up. This file is the checklist of what must therefore arrive from outside, and what happens when it does not.

**How values arrive.** Any ASP.NET Core configuration source: `appsettings.Production.json`, an additional JSON file, environment variables (`Section__Key`), user-secrets in Development, or a mounted secret. Secrets belong in environment variables or a secret store, never in a tracked file.

---

## 1. How absence behaves — three different answers, deliberately

| Kind | Absent behaviour | Why |
|---|---|---|
| **Wiring** — the API origin, the connection string, the JWT key, the SMTP host | **Fails startup**, naming the exact key | Without it nothing works; a silent default turns an operator's mistake into a customer-visible fault |
| **Content** — company identity, legal text | **Warns once at startup**; the page omits what it cannot show | The site is plainer but entirely functional, and failing startup would block work on copy nobody has written yet |
| **Malformed content** — a half-specified link, an off-origin logo | **Fails startup**, naming the exact key | Absent and broken are different mistakes. Broken content reaches a customer as a dead anchor, an unlabelled image, or a third-party request |

**Never invent a value to satisfy this file.** A plausible-looking placeholder on a page a real customer reads is worse than a plain page, and it is the variant that ships unnoticed.

---

## 2. `RenoTrack.Website` — the customer-facing site

### 2.1 Wiring (absence fails startup)

| Key | Notes |
|---|---|
| `PublicApi:BaseUrl` | The API origin. **Absolute HTTPS, enforced in every environment** — the customer's token travels in this request's path. |
| `PublicApi:TimeoutSeconds` | Optional; defaults to 10. A timeout's default *is* the policy. |

### 2.2 Company identity (absence warns; malformed fails)

| Key | Required? | Behaviour |
|---|---|---|
| `CompanyIdentity:DisplayName` | No | Absent ⇒ one startup warning; header and page titles carry no name |
| `CompanyIdentity:ContactEmail` | No | Absent ⇒ the footer contact line is omitted |
| `CompanyIdentity:ContactPhone` | No | Absent ⇒ as above |
| `CompanyIdentity:LogoPath` | No | **Must be under `/brand/`**, e.g. `/brand/logo.svg`, with the file placed in a `brand` directory **beside the application — not in `wwwroot`** (see the note below). **An absolute or protocol-relative URL fails startup even over HTTPS** — a customer page loads nothing off-origin, because a third-party request would disclose which customer opened which quote and when. **A site-relative path outside `/brand/` fails too**, since nothing would serve it. **Set without `DisplayName` also fails startup**: the name is the image's alternative text. A path resolving to no file only warns. |

> **`CompanyIdentity:DisplayName` and `Email:FromDisplayName` must name the same company.**
> They are separate keys serving separate processes — one heads the customer's quote page, the other signs every customer email — and **nothing in code makes them agree**. A deployment can sign mail as one company and head the page as another. Checked by eye at deployment and again in Slice 8's end-to-end run; deliberately not unified in code, because D71's precedent is that two audiences are two settings.

> **Why the logo does not go in `wwwroot`.** `MapStaticAssets` serves only the endpoints in its
> **build-time** manifest, so a file an operator copies into `wwwroot` after publishing is on disk
> and still answers **404**. Deployment-supplied assets are therefore served from a `brand`
> directory beside the application, mounted at `/brand`. Create the directory, put the file in it,
> and name it as `/brand/<file>`. Only known file types are served, and nothing outside that
> directory is reachable through the mount.

### 2.3 Legal content (absence warns; malformed fails) — SRS FR-1.4

`Legal:Impressum` and `Legal:Datenschutz`, each an ordered `Sections` list; each section an optional `Heading` and a `Paragraphs` list; each paragraph a `Text` and/or a `LinkText` + `LinkUrl` pair.

- **A document with no content ⇒ its route answers 404 and no link to it is rendered.** Not an empty page: a page that looks like a legal page and says nothing is worse than a 404. One startup warning per unwritten document.
- **`LinkText` and `LinkUrl` must be supplied together**, or startup fails naming the key.
- **`LinkUrl` schemes are an allowlist: `http`, `https`, `mailto`, `tel`, plus site-relative paths beginning with `/`.** Anything else fails startup. Razor encodes an attribute's value but does not refuse its scheme, so a `javascript:` link would otherwise execute on a page that deliberately runs no script.
- Text is rendered through Razor's encoding expressions; markup in it is displayed, not executed. The content model carries no HTML.

**FR-1.4 is not met until this content exists.** The mechanism is complete; the requirement waits on the company's text, which the company authors (SRS §5). Do not write it here.

### 2.4 Trusted forwarders (empty by default — see §4)

`TrustedForwarders:KnownProxies`, `TrustedForwarders:KnownNetworks`.

---

## 3. `RenoTrack.Api`

| Section | Keys | Absence |
|---|---|---|
| `ConnectionStrings:RenoTrackDb` | — | Fails startup |
| `Jwt:*` | `Issuer`, `Audience`, `SigningKey` (minimum length enforced); `AccessTokenMinutes`/`RefreshTokenDays` have defaults | Fails startup naming the key at fault. **`SigningKey` is never committed.** |
| `Database:Mode` | `Verify` (default) or `Migrate` | `Migrate` is **hard-refused in Production**. Migrations are applied by an explicit deployment step. |
| `TokenLink:PublicBaseUrl` | The **Website's** origin, not the API's — this is what the customer's emailed link points at | Fails startup **when `Email:Enabled` is true**; absolute HTTPS. With email disabled it is not required, because nothing composes a link. |
| `TokenLink:LifetimeDays` | SRS FR-6.4 | Has a default |
| `Email:Enabled` | Stated explicitly, so deployed behaviour is visible in a tracked file | `false` ⇒ `LoggingNoOpEmailSender`, and no email configuration is required |
| `Email:*` when enabled | `Host`, `Port`, `SecurityMode`, `FromAddress`, `FromDisplayName`, `AdminRecipients` (≥1). Optional: `ReplyToAddress`; `Username`+`Password` together or not at all | Fails startup |
| `DevelopmentBootstrap:*` | Development-only account provisioning | Three independent guards; **enabled outside Development throws**, never silently skips. No password is ever compiled in. |

**`Email:AdminRecipients` is deliberately independent of the Identity Admin role (D71).** Holding the Admin role does not subscribe an account to operational mail, and appearing in this list confers no dashboard permission.

---

## 4. Two settings that are safe by default and wrong by omission

**`TrustedForwarders` is empty in every committed configuration**, so both applications partition rate limits and log addresses by the immediate peer. That is the safe default and never *wrong* — only less precise. But a real deployment behind a reverse proxy **must** name it, or every customer shares one rate-limit bucket.

- `KnownNetworks` entries must be the **canonical** network address for their prefix: `10.0.0.1/8` is refused at startup rather than silently widened to `10.0.0.0/8`. To trust a single host use `KnownProxies`.
- `0.0.0.0/0` **would be accepted** — it is canonical — and would trust every address on the internet. Nothing rejects it; an operator must not write it.

---

## 5. Before launch

- [ ] Every wiring key above supplied; both applications start.
- [ ] `CompanyIdentity:DisplayName` and `Email:FromDisplayName` name the **same** company.
- [ ] `Legal:Impressum` and `Legal:Datenschutz` carry real text — **FR-1.4 is open until they do.**
- [ ] `CompanyIdentity:LogoPath` resolves to a file actually present in the `brand` directory beside the application (a wrong path only warns, and the page then renders a broken image).
- [ ] `TrustedForwarders` names the real proxy, and is not `0.0.0.0/0`.
- [ ] No startup warning left unexplained — each one names a key that is genuinely intended to be unset.
- [ ] Migrations applied by the deployment step; `Database:Mode` left at `Verify`.
