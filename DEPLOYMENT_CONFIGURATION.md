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
| **Content a switch makes required** — the marketing site's identity once `Site:PublicBaseUrl` is set (D102) | **Fails startup**, naming every missing key in one message | A public business site cannot be published without a name, a way to reach the company and where it is; with the switch off, the same absence is only content |
| **A content pack reaching for configuration** — any section in a pack file outside its allowed roots (D102) | **Fails startup**, naming the file and the sections, never their values | The pack is content, never configuration authority (§2.5) |

**Never invent a value to satisfy this file.** A plausible-looking placeholder on a page a real customer reads is worse than a plain page, and it is the variant that ships unnoticed.

---

## 2. `RenoTrack.Website` — the customer-facing site

### 2.1 Wiring (absence fails startup)

| Key | Notes |
|---|---|
| `PublicApi:BaseUrl` | The API origin. **Absolute HTTPS, enforced in every environment** — the customer's token travels in this request's path. |
| `PublicApi:TimeoutSeconds` | Optional; defaults to 10. A timeout's default *is* the policy. |
| `ContentPack:RootPath` | Optional; **wiring once set** (D102). The absolute path of the company's content pack (§2.5). Relative, missing, or without `site.json` ⇒ startup fails naming this key. Must come from the host's own configuration — a pack cannot name itself. |

### 2.2 Company identity (absence warns; malformed fails)

| Key | Required? | Behaviour |
|---|---|---|
| `CompanyIdentity:DisplayName` | No | Absent ⇒ one startup warning; header and page titles carry no name |
| `CompanyIdentity:ContactEmail` | No* | Absent ⇒ the footer contact line is omitted. **Exactly one plain address** — no display name, no list — or startup fails (every deployment, D102). |
| `CompanyIdentity:ContactPhone` | No* | Absent ⇒ as above. **International form only**: `+`, country code, digit groups separated by single spaces, 8–15 digits, e.g. `+49 30 1234567`; anything else fails startup (every deployment, D102). |
| `CompanyIdentity:OwnerName` | No | Shown where the company introduces itself |
| `CompanyIdentity:Address:StreetAddress` / `PostalCode` / `Locality` / `CountryCode` | No* | **All four or none**; `CountryCode` is ISO 3166-1 alpha-2, upper case |
| `CompanyIdentity:OpeningHours` | No | List of `{ Days: ["Monday", …], Opens: "HH:mm", Closes: "HH:mm" }`. English day names exactly; each day in at most one block; `Closes` later than `Opens`. **Regular spans only.** |
| `CompanyIdentity:OpeningHoursNote` | No | What spans cannot say, e.g. "Samstag nach Vereinbarung". Shown to people; never published as machine-readable hours. |
| `CompanyIdentity:ServiceArea:Places` / `Note` | No | Places are `{ Name, Kind }` with `Kind` `City` or `Region`, names unique |

\* **Required once the marketing site is enabled** (`Site:PublicBaseUrl` set, §2.5): `DisplayName`, `ContactPhone`, `ContactEmail` and the whole `Address`. Every identity text field refuses control characters and has a length limit.
| `CompanyIdentity:LogoPath` | No | **Must be under `/brand/`**, e.g. `/brand/logo.svg`, with the file placed in a `brand` directory **beside the application — not in `wwwroot`** (see the note below). **An absolute or protocol-relative URL fails startup even over HTTPS** — a customer page loads nothing off-origin, because a third-party request would disclose which customer opened which quote and when. **A site-relative path outside `/brand/` fails too**, since nothing would serve it. **Set without `DisplayName` also fails startup**: the name is the image's alternative text. A path resolving to no file only warns — and the page then shows the **company name as the image's alternative text** where the logo belongs, on screen and in print, which looks exactly like a deployment with no logo configured. The startup warning is the only signal. |

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

### 2.5 Content pack and marketing site (D102)

The company's content is a **content pack**: a directory outside the application, kept in the company's private content repository, never in this one. The schema is in `CONTENT_PACK.md`.

```
<ContentPack:RootPath>/
├─ site.json    required   may contain only: CompanyIdentity, Site
├─ legal.json   optional   may contain only: Legal
└─ brand/       optional   served at /brand/ instead of the brand directory beside the application
```

- **The pack is content, never configuration.** A section in a pack file outside its allowed roots — `Logging`, `PublicApi`, `TrustedForwarders`, `ConnectionStrings`, `Jwt`, `Email`, `TokenLink`, `AllowedHosts`, `ContentPack`, the other file's section, even a top-level `"//"` comment — **fails startup** naming the file and section. Product settings stay in the application's own configuration.
- **Precedence for content:** command line > environment variables > **pack** > user-secrets > `appsettings*.json`. An operator can still correct one value with an environment variable without editing the company's repository.
- **A list comes from one source.** `Site:Services`, `CompanyIdentity:OpeningHours` and `CompanyIdentity:ServiceArea:Places` supplied by the pack *and* anything else fails startup: configuration merges lists entry by entry and would silently mix them. Remove the stray copy — typically in a developer's `appsettings.Development.json`.
- **Changes take effect on restart.** Pack files are not watched.
- **Nothing in the pack but `brand/` is served.**

| Key | Required? | Behaviour |
|---|---|---|
| `Site:PublicBaseUrl` | No — **the switch** | Absent ⇒ no marketing site; one startup warning; token and legal pages unaffected. Present ⇒ must be an **HTTPS origin only** (no path, query, fragment or user info); the site is enabled and the identity marked \* in §2.2 plus at least one service become required. |
| `Site:Services` | When enabled | List of `{ Slug, Name, Summary, Offerings[] }`. `Slug` is lowercase ASCII with single hyphens (`tueren`, not `türen`) and unique — it becomes a URL the company must keep stable. `Name` unique ignoring case; `Summary` required; at least one non-blank offering. Validated whenever supplied, enabled or not. |

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

## 4a. Logging — what configuration may and may not change (D101)

Both applications serve URLs whose path **is** a customer credential (`/angebot/{token}`, `/api/v1/public/angebote/{token}`). Logging configuration is therefore part of the security boundary, and only some of it is enforced in code.

| Concern | Enforced by | What an operator may change |
|---|---|---|
| ASP.NET's per-request `RequestPath` scope | **Code.** `HostingRequestScopeSuppression` turns `Microsoft.AspNetCore.Hosting.Diagnostics` off for every provider, after configuration is read | Nothing. No configuration value re-enables it, and none should be attempted: it is the scope that wrote tokens into the Windows Application event log |
| Request trace ids (`TraceId`/`SpanId`) | **Code.** `RequestActivityTracing` | Nothing needed. Scope-writing sinks (EventLog, JSON console with scopes, OpenTelemetry) are safe for this scope now and carry ids, not paths |
| Everything else under `Microsoft.AspNetCore` | **Configuration only** | Keep at **`Warning` or quieter**. At `Information`, MVC logs `redirecting to /angebot/<token>` after every customer decision |
| Website → API HTTP client | **Code.** `RemoveAllLoggers()` on the typed client, and `System.Net.Http.HttpClient: Warning` as defence in depth | Nothing |

- **`RequestId` does not appear in logs.** It lived only in the suppressed scope. Correlate on **`TraceId`**. For API errors, the ProblemDetails `traceId` is `00-<TraceId>-<SpanId>-00`.
- **Trace ids are caller-controlled.** An inbound `traceparent` header is honoured, so a client can choose the `TraceId` its requests are logged under. Treat trace ids as untrusted correlation metadata: never as an identity, never in an authorization, rate-limit, audit or deduplication decision.

---

## 5. Before launch

- [ ] Every wiring key above supplied; both applications start.
- [ ] `CompanyIdentity:DisplayName` and `Email:FromDisplayName` name the **same** company.
- [ ] `ContentPack:RootPath` names the company's pack, outside the application directory and outside any product checkout; the startup log line "Content pack loaded from …" reports the expected service count.
- [ ] `Site:PublicBaseUrl` is the canonical origin the site is served on, and names the **same** origin as the API's `TokenLink:PublicBaseUrl` — the two applications cannot check each other.
- [ ] `Legal:Impressum` and `Legal:Datenschutz` carry real text — **FR-1.4 is open until they do.**
- [ ] `CompanyIdentity:LogoPath` resolves to a file actually present in the `brand` directory beside the application. A wrong path only warns, and the page then shows the company name in the logo's place. That is indistinguishable from "no logo" by eye, so check `GET /brand/<file>` answers 200 rather than looking at the page. The `brand` directory is resolved against the content root, which is the project directory under `dotnet run` and the publish directory when published.
- [ ] `Logging:LogLevel:Microsoft.AspNetCore` is `Warning` or quieter in **both** applications (see §4a).
- [ ] Event logs on hosts that ran a pre-D101 build have been reviewed. They may hold customer tokens, and clearing them is an operator decision.
- [ ] `TrustedForwarders` names the real proxy, and is not `0.0.0.0/0`.
- [ ] No startup warning left unexplained — each one names a key that is genuinely intended to be unset.
- [ ] Migrations applied by the deployment step; `Database:Mode` left at `Verify`.
