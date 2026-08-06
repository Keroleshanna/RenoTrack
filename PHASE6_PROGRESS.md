# PHASE6_PROGRESS.md — API: Token-Link Mechanism + Public Angebot Decision Endpoints

**Branch:** `feature/phase-6-token-links-public-angebot`, off `main` at `18243ec` (PR #11, the Phase 5 merge).
**Roadmap entry:** `PROJECT_ROADMAP.md` Phase 6. **Status: in progress.**

This is the first API surface deliberately outside JWT — Architecture.md §7.2 calls it a small, single-purpose parallel mechanism rather than a second authentication system, and `PROJECT_ROADMAP.md` isolates it in its own phase precisely so the security review can be focused.

---

## Decisions taken before any code was written

Presented as a design review and explicitly approved, with adjustments:

1. **No documentation-reconciliation slice.** Documentation reconciliation is a **phase completion criterion**, not an implementation slice: the repository is kept current as each slice lands, and the final current-state reconciliation happens in the last slice before publication. The criteria are enumerated as a checklist below so this cannot degrade into "we'll remember later" — **including Phase 5's own unreconciled documentation, which Phase 6 inherits and must close.**
2. **`AddTokenLinks` migration approved without a new ADR** — it implements schema `SRS.md` §4.1, `ERD.md` and `Architecture.md` §7.2 already specify. Implementing documented schema is not an architecture decision.
3. **BR-4 governs the public view.** Viewing stays available after a decision; `UsedAt` blocks decision actions only, never the read endpoint. Two documents conflict with this and are reconciled within this phase (see below).
4. **The rejection reason is deferred to its own ADR.** It is **not** stored in `AuditLog` (audit is best-effort instrumentation by D50 — business data must never depend on it) and it is **not** accepted-then-discarded (if the API accepts a value, users may reasonably expect it preserved). Consequence, recorded plainly rather than glossed: **Slice 4's decision endpoint will not accept a `reason` field at all**, which is a known, deliberate gap against SRS FR-6.3 and Wireframe A3 pending that ADR.

## Document conflicts found during the design review

Each is reconciled inside this phase, per `CLAUDE.md` §15.

| # | Conflict | Resolution |
|---|---|---|
| C1 | `PermissionMatrix.md` §7 says token viewing is allowed "until decision made"; BR-4 says viewing remains allowed; `Architecture.md` §7.2 hedges ("or can be cut off too, per OQ resolution") | BR-4 wins — a numbered business rule outranks a matrix cell and an explicit hedge. Both other documents corrected |
| C2 | `Sequence Diagram.md` §6 sets `Angebot.DecisionResult` | Stale — that property was removed from the Domain by D16 and from `ERD.md` by D41. Outcome is carried by `Status` |
| C3 | `Sequence Diagram.md` §6 says the Lead becomes "Won-pending / Lost" | No such `LeadStatus` value exists. `StateMachine.md` §2.3/§5 both say plain `Won` |
| C4 | `Sequence Diagram.md` §7 *also* sets `Lead.Status = Won` at project conversion | A second path to `Won`, contradicting `StateMachine.md` §5's "only inside the Angebot decision handler". Phase 6 owns the transition; §7's line is stale |

## Phase completion criteria — Phase 6 is NOT complete until every one of these is done

Documentation reconciliation is a completion criterion of this phase, not an implementation slice
and not a note to remember later. **Phase 6 must not be proposed for publication with any box below
unticked**, regardless of how complete the four code slices are.

- [ ] **C1–C4 corrected in the source documents themselves** (see the conflict table below), not merely recorded here.
- [ ] **`PermissionMatrix.md` §7** — "until decision made" corrected to match BR-4.
- [ ] **`Architecture.md` §7.2** — the "or can be cut off too, per OQ resolution" hedge resolved to BR-4's answer.
- [ ] **`Sequence Diagram.md` §6** — stale `DecisionResult` removed; "Won-pending" corrected to `Won`.
- [ ] **`Sequence Diagram.md` §7** — the duplicate `Lead.Status = Won` removed (StateMachine §5 puts it in the decision handler only).
- [ ] **`Architecture.md` §5.2 / `ERD.md`** — updated for whatever Phase 6 actually built.
- [ ] **`PHASE6_PROGRESS.md`** — complete through Slice 4.
- [ ] **`PROJECT_STATE.md`** — current-state reconciliation.
- [ ] **`NEXT_STEPS.md`** — open items carried out of Phase 6.
- [ ] **`ARCHITECTURE_DECISIONS.md`** — Phase 6's own decisions recorded.
- [ ] **The rejection-reason ADR** — either decided and recorded, or explicitly carried forward as a named open item with its reason. It must not simply be absent.

### Inherited debt: Phase 5's documentation was never reconciled, and Phase 6 closes it

Found during Phase 6's opening verification, confirmed against the repository rather than inferred.
Phase 5 (PR #11, merge `18243ec`) touched only 22 documentation lines across 5 files. **This is
Phase 6's responsibility to close, and it is a completion criterion, not a courtesy:**

- [ ] **`PROJECT_STATE.md` §1** says "Phase 5 — … **Not started**". It is merged.
- [ ] **`PROJECT_STATE.md` §2** says "`main` is the current branch, at `e1a4d9e`". `origin/main` is `18243ec`.
- [ ] **`PROJECT_STATE.md` §9** says slices 1–4 are "done on the branch, not yet merged". They are merged.
- [ ] **`NEXT_STEPS.md` §6 step 4** names the Development-bootstrap slice as "the next deliverable". It merged as PR #10.
- [ ] **`NEXT_STEPS.md`** has no Phase 5 section at all (§1c, Phase 4, is the last).
- [ ] **`PHASE5_PROGRESS.md` does not exist**, though every phase from 2 onward has one.
- [ ] **`ARCHITECTURE_DECISIONS.md` stops at D64** — Phase 5's four business slices recorded no decisions.
- [ ] **`HANDOFF_PROMPT.md`** — its `origin/main` SHA predates PR #11 and its task section directs an already-merged slice.

## Slice plan

| # | Slice | Status |
|---|---|---|
| 1 | `TokenLink` Domain + Infrastructure (aggregate, repository, generator, migration) | ✅ done |
| 2 | `POST /api/v1/angebote/{id}/send` (Admin `F`) | ✅ done |
| 3 | `GET /api/v1/public/angebote/{token}` (anonymous read) | ✅ done |
| 4 | `POST /api/v1/public/angebote/{token}/decision` + public-route rate limiting + final reconciliation | not started |

---

## Slice 1 — `TokenLink` Domain + Infrastructure

**Why a Domain entity and not an Infrastructure-only persistence model.** `AuditLog` (D49), `NumberSequence` (D51) and `RefreshToken` were all classified Infrastructure-only on one stated ground: no business invariant referenced them. `TokenLink` fails that test in three independent ways — BR-4 is a numbered business rule about its `UsedAt`, `StateMachine.md` §2.3 guards two Angebot transitions on "TokenLink valid, unused, not expired", and **`Architecture.md` §6's aggregate list names it outright: "TokenLink (root) — polymorphic reference to Angebot or Invoice"**. So this was a documented answer to look up, not a judgment call to make.

**Files added**

| File | Purpose |
|---|---|
| `src/RenoTrack.Domain/Enums/TokenLinkEntityType.cs` | `Angebot`/`Invoice` |
| `src/RenoTrack.Domain/Entities/TokenLink.cs` | The aggregate: `Create`, `IsExpired(asOf)`, `MarkUsed()` |
| `src/RenoTrack.Application/Common/Interfaces/ITokenLinkRepository.cs` | `AddAsync`, `FindByTokenAsync` |
| `src/RenoTrack.Application/Common/Interfaces/ITokenLinkService.cs` | `Generate()` → `GeneratedToken(Token, ExpiresAt)` |
| `src/RenoTrack.Infrastructure/Persistence/Configurations/TokenLinkConfiguration.cs` | Table, string-stored enum, unique `Token` index, **no FK** |
| `src/RenoTrack.Infrastructure/Persistence/Repositories/TokenLinkRepository.cs` | Tracking reads (no `UpdateAsync` exists anywhere) |
| `src/RenoTrack.Infrastructure/TokenLinks/TokenLinkOptions.cs` | `TokenLink:LifetimeDays`, eagerly validated |
| `src/RenoTrack.Infrastructure/TokenLinks/TokenLinkService.cs` | 32-byte `RandomNumberGenerator`, base64url |
| `src/RenoTrack.Infrastructure/Persistence/Migrations/20260805180941_AddTokenLinks.cs` | Migration #6 |

**Design points worth recording**

- **`Invoice` is declared in the enum from the start**, though nothing produces it until Phase 8 — matching how `AngebotStatus`/`LeadSource` shipped complete in Phase 1 with values unreachable for phases. A single-valued enum would misrepresent the documented domain of the column, and Sequence §12's entity-type check is only meaningful once two values exist.
- **`ITokenLinkService` is a value provider, not a factory returning a `TokenLink`** — the `INumberGeneratorService` shape. Infrastructure supplies what the Domain cannot produce (randomness, configuration); the handler still constructs the aggregate through its own factory, keeping step 4 of `CLAUDE.md` §6's handler shape visible at the call site. Sequence §6 draws it as `GenerateToken(entityType, entityId, expiresIn)`; none of those three arguments affects the value produced, so they are passed to `TokenLink.Create` instead.
- **`IsExpired(asOf)` is a public read, which looks like the `Inspection.IsEditable` property D29 rejected — but is not the same case.** D29's property existed only so a handler could avoid calling a mutator that would have thrown anyway. Here the public **read** endpoint mutates nothing, so there is no guard to reject through, and Sequence §12 requires expiry to be checked on that path. The `asOf` parameter also makes the rule testable by moving the clock reading rather than by reflecting into `ExpiresAt`.
- **`MarkUsed()` guards expiry as well as prior use.** Both are facts about the aggregate's own state, so §2 puts enforcement there. The Application layer still checks both first, because it must distinguish them to produce Sequence §12's distinct statuses — presentation, not a duplicated invariant.
- **The constructor refuses an expiry that is not in the future.** A link dead on arrival can never serve any purpose.
- **`TokenLinks` has no foreign key at all** — the only table in this schema without one. Not a deferral like D44's: `EntityId` points at `Angebote` or `Invoices` depending on `EntityType`, and no column can reference two tables. `CLAUDE.md` §21 now records this as a permanent, documented exception, pinned by a test that a dangling `EntityId` is accepted.
- **Reads are tracking, deliberately.** There is no `UpdateAsync` anywhere in this project, so an `AsNoTracking` read here would make Slice 4's `MarkUsed()` silently never persist.
- **`TokenLink:LifetimeDays` has no compiled-in fallback.** SRS FR-6.4 requires the period to be configurable; `30` is a tracked default in `appsettings.json`, and an absent or zero value fails startup naming the key — the same shape as the connection string, JWT and file-storage checks. For a credential-shaped value, "longer than intended" is the dangerous direction of a silent default.

**Migration review** (`CLAUDE.md` §21 requires both the pre-generation three-way comparison and the post-generation read):
- *Three-way comparison* — `ERD.md`'s `TOKENLINK` block lists exactly `Id, EntityType, EntityId, Token (UK), ExpiresAt, UsedAt (nullable), CreatedAt`; the Domain entity has exactly those seven; the configuration maps exactly those seven. Unique `Token` index matches `ERD.md` §3. No FK matches `ERD.md`'s own note.
- *Generated-migration read* — one `CreateTable`, seven columns, `UsedAt` the only nullable one, one unique index, no FK, no cascade, no unexpected table. `has-pending-model-changes` reports none.

**Adversarial verification** — each safeguard broken, the failure observed, then restored and the full suite re-run green:

| Broken implementation | Observed failure |
|---|---|
| BR-4's `UsedAt is not null` guard removed from `MarkUsed()` | 2 Domain failures (`MarkUsed_ASecondTime_Throws`, `..._LeavesTheOriginalTimestampIntact`) |
| `FindByTokenAsync` changed to `AsNoTracking()` | `FindByTokenAsync_ReturnsATrackedInstance_SoMutationsPersist` failed — confirming Slice 4's `MarkUsed()` would silently never persist |
| `IsUnique()` dropped from the `Token` index | `TwoTokenLinksCannotShareAToken` failed — the constraint is real SQL, not model decoration |

**Test delta: 724 → 763 passing, 0 failing** (Domain 165 → 185, Application 219 unchanged, Infrastructure 140 → 159, Api 200 unchanged). Build 0 Warnings / 0 Errors; `has-pending-model-changes` reports none; six migrations.

**Documentation updated in this slice** (not deferred to the end): `README.md`'s configuration table gained `TokenLink:LifetimeDays`; `CLAUDE.md` §21 gained the `TokenLinks` no-FK exception.

**Five hand-composed test configurations needed the new setting** (`Api.Tests`/`Infrastructure.Tests` `DependencyInjectionTests`, `IdentityTestServices`, `DevelopmentBootstrapTests`, `DatabaseInitializerTests`) — the predictable cost of eager validation, and the same thing `FileStorage:RootPath` required in Phase 4 Slice 8. `RenoTrackApiFactory` needed none: it boots the real application, which reads the tracked `appsettings.json` default.

---

## Slice 2 — `POST /api/v1/angebote/{id}/send`

SRS FR-6.1 / Sequence Diagram §6 / StateMachine.md §2.3. The point where an internally approved
Angebot becomes a customer-facing document.

### The "Lead has a valid email address" guard — investigated, not assumed

StateMachine.md §2.3 guards `ApprovedInternally → Sent` on "Lead has a valid email address". The
conclusion is **split**, and the guard was neither re-implemented nor deleted:

| Reading of "valid" | Already guaranteed elsewhere? | Evidence |
|---|---|---|
| **Present / non-empty** | **Yes, structurally** | `Lead.Create` throws `ArgumentException` on null-or-whitespace email (`Lead.cs:62`); `Email` has `private set` and **no mutator anywhere** — `grep` finds it assigned only in the private constructor, so it cannot change after creation; and `LeadConfiguration` maps it `IsRequired().HasMaxLength(320)`, so the column is `NOT NULL` |
| **Syntactically valid address** | **No — not at the Domain level** | `Lead.Create` checks non-empty only. The **only** format check in the entire codebase is `CreateLeadCommandValidator`'s `.EmailAddress()` (confirmed by grepping `EmailAddress()` across `src/` — one hit) |
| **Deliverable** | No, and unknowable before Phase 9 | Nothing can establish this without actually sending |

**Conclusion: the guard still represents a real business invariant, but there is nothing for this
handler to add.** A presence check would be unreachable code. A format check inside the handler
would be shape validation in a handler, which `CLAUDE.md` §5 and §6 both forbid. So the handler
implements no new check and says so explicitly in its own doc comment, rather than staying silent
and letting a later reader assume the guard was overlooked.

**Residual risk, recorded rather than hidden:** the format guarantee rests on one validator at one
call site. `Lead.Create` currently has exactly one caller (`CreateLeadCommandHandler`), but its own
doc comment anticipates a second creation path (Sequence Diagram §2, Admin manual entry). A future
command that omits `.EmailAddress()` would produce a Lead whose email was never format-checked, and
nothing downstream would catch it. **Candidate fix, deliberately not taken unilaterally:** move the
format check into `Lead.Create` itself. That changes the Phase 1 Domain baseline and would need its
own explicit decision, so it is raised here rather than done quietly.

### The second flagged question: is `LeadStatus.AngebotInProgress` actually reachable at send time?

**Yes, verified by call-site inspection rather than assumed.** `Lead.MarkAngebotSent()` self-guards
`Status == AngebotInProgress`. That status is set by `MarkAngebotInProgress()`, whose only callers
are `CreateAngebotCommandHandler` and `DuplicateAngebotCommandHandler` — both of which run before an
Angebot can be submitted, reviewed or approved. Nothing between creation and send touches
`Lead.Status`: StateMachine.md §1.3 states the internal review transitions cause no Lead-level
change, and the handlers confirm it. **This slice is what finally makes `LeadStatus.AngebotSent`
reachable at all** — it was the unreachable state that made `Won`/`Lost` impossible to reach in
Phase 4, and Slice 4 now has a real path to them.

### Design points

- **No `IOwnershipValidator`** — PermissionMatrix.md §4 marks "Send Angebot to Lead (generate token link)" Admin `F`. Same reasoning as D31.
- **No request record** — every value is server-derived (D61): the id from the route, the Admin from the JWT.
- **All three writes share one `SaveChangesAsync`.** Angebot status, Lead status and the TokenLink row commit together. This matters more here than almost anywhere: a committed token link for an Angebot that never reached `Sent` is a live customer credential for a document nobody thinks was sent; a `Sent` Angebot with no token link is a customer who can never respond.
- **The audit row targets `Lead`, not `Angebot`** (`CLAUDE.md` §10) — this command's business-meaningful transition is the Lead reaching `AngebotSent`, exactly as `AngebotCreated` is logged against Lead for driving `MarkAngebotInProgress`. New `AuditAction.AngebotSent`.
- **`AngebotReadyNotification` carries the raw token, not a finished URL.** Composing `https://…/angebot/{token}` needs the public website's base address, which is deployment configuration — Application deliberately takes no `IConfiguration` at all (§22). Phase 9 owns the template and the base URL. This is the first customer-facing notification in the system; every earlier one (FR-9.2) goes to staff.
- **`LoggingNoOpEmailSender` logs the AngebotId but never the token**, and a test pins it. The token is the credential; logging it would defeat both the CSPRNG and D60's hash-only refresh-token stance.
- **The response carries no token and no `Location` header**, also pinned by a test asserting the raw JSON does not contain the persisted token. Returning it would put a live customer credential in response headers, proxy logs and browser history.

### Adversarial verification

| Broken implementation | Observed failure |
|---|---|
| `[Authorize(Roles = Roles.Admin)]` removed from the action | The Inspector **actually sent the Angebot** — the failure was on the status code (200 vs 403), not merely the body, i.e. a real fail-open, not a cosmetic one |
| Token generation moved **before** `angebot.Send()`/`lead.MarkAngebotSent()` | 2 Application failures — a rejected send left a generated token behind, and a second send minted a second token for the same Angebot |

Both restored and the full suite re-run green.

**Test delta: 763 → 786 passing, 0 failing** (Domain 185 unchanged, Application 219 → 233, Infrastructure 159 → 160, Api 200 → 208). Build 0 Warnings / 0 Errors. No new migration — this slice adds no schema.

**Documentation:** `Architecture.md` §5.2 already carried the `/send` row, so no correction was needed there; `PHASE6_PROGRESS.md` (this section) is the slice record.

---

## Slice 3 — `GET /api/v1/public/angebote/{token}`

SRS FR-6.2 / Sequence Diagram §6 and §12. The first anonymous endpoint in the system.

### The approved public DTO

A **separate hierarchy** (`PublicAngebotDto`/`PublicSectionDto`/`PublicItemDto`/`PublicVatLineDto`),
never a projection of `AngebotDetailDto`: if the two shared a type, a field added later for the
Dashboard would silently appear on the one endpoint any holder of a forwarded email can reach. The
duplication is the safety property.

**Included:** `AngebotNumber`, `Decision`, `DecisionAt`, section `Title`/`Subtotal`, item
`Description`/`Specification`/`Quantity`/`Unit`/`UnitPrice`/`LineTotal`, `NetTotal`, VAT
`Rate`/`VatAmount`, `GrossTotal`.

**Excluded:** every internal id (Angebot, section, item), `LeadId`, `InspectionId`,
`CreatedByInspectorId`, `ReviewedByAdminId` (staff identities — a forwarded link must not disclose
which employee priced the job or which manager approved it), `CatalogItemId` (BR-8 trace link;
would disclose that pricing comes from a reusable catalogue and which template), `CreatedAt`,
`SentAt`, `SortOrder`, per-item `VatRate`, per-rate net amounts, and every Lead field.

`Decision` is a dedicated `PublicAngebotDecision { Pending, Approved, Rejected }`. `AngebotStatus`
is **never** exposed publicly, so a future internal state cannot become part of the public contract
by accident. `Rate` is the printable percentage, not an enum member name — "zzgl. Standard MwSt"
would be nonsense on Wireframe A3's page.

### Two real defects found during implementation, both by tests rather than by inspection

**1. A time-dependent Domain guard made every expired row unreadable.** `TokenLink`'s constructor
rejected an expiry that was not in the future. EF Core materialises persisted rows **through that
same private constructor**, binding parameters to properties by name — so the guard ran on *reading*
as well as creating, and any lapsed link threw `ArgumentException` on load. It surfaced as **400
instead of the 410 the endpoint owes**, and the row was effectively unreadable forever. Fixed by
moving every guard into `Create`, matching `Lead`'s shape rather than `CatalogItem`'s. The
distinction is load-bearing: `CatalogItem`'s constructor guards (non-empty title, non-negative
price) hold forever once true; a time-dependent one does not. Pinned by
`AnExpiredTokenLinkCanStillBeLoaded`.

**2. Diagnostic surfaces carried live customer tokens — and the first fix for it silently did not
work.** The exception handler logged `httpContext.Request.Path`, and this is the first URL in the
system whose path segment *is* a secret, so every 404/410 on a token link put a working credential
into the application log. ProblemDetails `instance` echoed the same path.

The first attempt read the matched route template from `HttpContext.GetEndpoint()` inside the
exception handler. **It redacted nothing**, because ASP.NET's exception middleware calls
`ClearHttpContext` before invoking any `IExceptionHandler` — the endpoint and route values are
already gone, so the code fell straight through to the raw path. This went unnoticed at first
because the tests inspected only response bodies, never the log. It was found by probe, and the
Slice 3 closeout report that claimed the logging was fixed was wrong.

**Final behaviour.** `RouteDiagnostics.Capture` runs as middleware right after `UseRouting`, while
routing metadata still exists, and stashes the route template plus whether the route has a
parameter named `token` in `HttpContext.Items` (which `ClearHttpContext` does not touch). Both
surfaces then read from there:

- **Logs** use the route template for every route — uniform, aggregatable names are worth more than
  exact paths in a log, and id-bearing exceptions still carry their key in the message.
- **ProblemDetails `instance`** uses the template **only for credential-bearing routes**
  (`/api/v1/public/angebote/{token}`); every other route still reports its real path, ids included.
  That narrowness is itself pinned by a test.

Keyed on a route *parameter named `token`* rather than a URL prefix or segment position, so it
keeps holding for Slice 4's `{token}/decision` route — where the credential is not the last
segment — and for Phase 8's invoice links.

`NotFoundException` gained a **message-only constructor** for the same reason — the "id" here is the
token, and the id-based constructor would have written it into both the ProblemDetails `detail`
*and* the Warning log (D59).

**The property now held, and tested on both failure paths (404 and 410):** the raw token appears
nowhere in the response body, nowhere in `detail`, nowhere in `instance`, and nowhere in the
application log — while `detail` still states what went wrong and `instance` still names the
endpoint. The log assertion captures real `ILogger` output rather than arguing from the code,
precisely because arguing from the code is what produced the silent failure above.

This rule is recorded in `CLAUDE.md` §22 (public token credentials must not reach diagnostic
surfaces; capture route metadata before the exception middleware clears it; assert log content when
a route carries a secret), and the constructor-guard rule in §2.

### Design points

- **`GoneException` → 410 is a new Application exception type**, added because Sequence Diagram §6 names the status explicitly ("404 / 410 Gone") and §12 requires a specific reason. One new arm in the single `switch` (D59), nothing else. **Distinguishing "expired" from "unknown" leaks nothing here**, unlike D60's deliberately-identical login 401s: an email address is guessable, so distinguishing turns login into an enumeration oracle, whereas a 256-bit CSPRNG token cannot be produced to probe with. The only person who can see a 410 is someone genuinely holding a real link, and for them "this link expired" beats a 404 that sends them hunting for a typo.
- **A wrong-entity-type token is a 404, identical to an unknown one** — one combined condition, so the two cannot drift into distinguishable responses. Confirming "that token is real, it just belongs to an Invoice" helps nobody legitimate.
- **`UsedAt` is deliberately not checked here.** BR-4 restricts single use to state-changing actions and says viewing remains allowed; §12 scopes the check to "decision-type actions only". A customer who already approved must still be able to re-read what they agreed to.
- **A separate `PublicController`**, not more actions on `AngeboteController`. That controller is `[Authorize]` at class level, so an anonymous action there would sit one careless copy-paste away from every authenticated action around it. `[AllowAnonymous]` is declared at class level — the one deliberate inversion of CLAUDE.md §22's default, because on this controller every action is anonymous by definition, so the fail-safe direction is reversed.
- **The validator checks presence only.** A length or character-class rule would be a second, quieter definition of what a token looks like, competing with the generator's — changing the token length later would silently reject every link already in the field.

### Adversarial verification

| Broken implementation | Observed failure |
|---|---|
| `UsedAt` check added to the read path | 2 failures (Application + Api) — a used link stopped being viewable, contradicting BR-4 |
| `CreatedByInspectorId` added to the public DTO | `The_public_response_exposes_no_internal_field` failed — the exclusion list is enforced against the raw JSON, so a typed read cannot ignore an extra field |
| Expiry guard moved back into the constructor | 2 failures — the expired row became unloadable and the endpoint returned 400 instead of 410, reproducing defect 1 exactly |
| `RouteDiagnostics.Capture` middleware disabled | 3 failures — the token reappeared in `instance` on both 404 and 410, and in the captured application log, reproducing defect 2 exactly |

All restored and the full suite re-run green.

**Test delta: 786 → 813 passing, 0 failing** (Domain 185 unchanged, Application 233 → 246, Infrastructure 160 → 161, Api 208 → 221). Build 0 Warnings / 0 Errors. No new migration, no model drift.

**Still not rate-limited.** `Architecture.md` §12 requires abuse protection on `/api/v1/public/*`; it lands in Slice 4 so the limiter is configured once for the whole route group rather than twice.
