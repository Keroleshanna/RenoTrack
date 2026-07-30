# ARCHITECTURE_DECISIONS.md — Chronological Decision Log

**Purpose:** every significant architectural decision made on RenoTrack, in the order it was made, with the problem it solved, what else was considered, what was chosen, why, and what it costs. This is not a summary — treat every entry as authoritative and complete. If you are tempted to revisit a decision here, first re-read its "Why Chosen" and "Consequences" sections; only reopen it if you have genuinely new evidence (a real bug, a new requirement, an actual documented contradiction), not a stylistic preference.

Numbering is chronological across the whole project, not per-phase.

---

## D1 — Clean Architecture Dependency Rule Enforced by Real Project References

**Problem:** A layering convention stated only in a document (Architecture.md) is easy to violate accidentally — nothing stops a future contributor from adding `using RenoTrack.Infrastructure` inside `RenoTrack.Domain`.

**Alternatives considered:** (a) Document the rule and rely on code review discipline. (b) Enforce it via architecture-testing tools (e.g. NetArchTest). (c) Enforce it via actual `ProjectReference` entries, so a violating `using` simply fails to compile.

**Final decision:** (c). `RenoTrack.Domain.csproj` has zero `<ProjectReference>`. `RenoTrack.Application` references only `Domain`. `RenoTrack.Infrastructure` references both `Application` and `Domain` **explicitly** (see D-Infra-Explicit below). `RenoTrack.Api` references `Application` and `Infrastructure`. `Website`/`Dashboard` reference nothing backend.

**Why chosen:** A missing project reference produces an immediate compiler error, not a lint warning someone can ignore. This is the cheapest, most reliable enforcement available and requires no extra tooling/NuGet package.

**Consequences:** Any new cross-layer dependency requires an explicit, visible `dotnet add reference` command — a deliberate friction point that surfaces layering violations at the moment they'd be introduced, not later in review.

---

## D2 — Infrastructure References Domain Explicitly, Not Just Transitively

**Problem:** SDK-style .NET projects make `ProjectReference`s transitive by default — `Infrastructure → Application → Domain` means `Infrastructure` can already use `Domain` types without its own direct reference, purely because C# project references propagate.

**Alternatives considered:** (a) Rely on the transitive reference (compiles fine, one less line in the `.csproj`). (b) Add an explicit `Infrastructure → Domain` reference even though it's redundant for compilation.

**Final decision:** (b) — added explicitly.

**Why chosen:** `Infrastructure` will directly consume `Domain` entities as a first-class concern (EF Core `DbContext` with `DbSet<Lead>`, `DbSet<Angebot>`, etc., and repository implementations) — not an incidental usage of something `Application` happens to expose. The `.csproj` should honestly declare what a project actually uses; hiding a real, direct dependency behind transitive resolution is fragile (if `Application`'s own reference to `Domain` were ever marked `PrivateAssets="all"`, `Infrastructure` would silently break with no local explanation) and misleading to anyone reading the project file.

**Consequences:** One extra `<ProjectReference>` line with no functional difference today, but a clearer, more resilient dependency graph. This same reasoning (declare real usage explicitly, don't rely on what happens to compile) recurs — see D9 (`Domain.Tests`) and the general principle in `CLAUDE.md` §1.

---

## D3 — `TreatWarningsAsErrors` Solution-Wide, With a Narrow Escape Hatch

**Problem:** Compiler/analyzer warnings accumulate silently if not enforced; a growing pile of ignored warnings erodes trust in what "clean build" means.

**Alternatives considered:** (a) Leave warnings as warnings (default). (b) `TreatWarningsAsErrors=true` with no exceptions. (c) `TreatWarningsAsErrors=true` plus a `WarningsNotAsErrors` list for specific, consciously-accepted advisory IDs.

**Final decision:** (c), set in `Directory.Build.props` (applies to every project via MSBuild import order).

**Why chosen:** Full enforcement (b) risks blocking the whole solution's build over something as narrow as a single NuGet security-advisory warning that's already been triaged and deliberately accepted (or, more commonly, patched — see D4). A per-ID escape hatch keeps the default strict while allowing a specific, reviewed exception.

**Consequences:** As of this writing, `WarningsNotAsErrors` contains `NU1903` (defensive, in case a future transitive package flags a vulnerability warning that needs triage time rather than an instant build break) — not because an unresolved vulnerability currently exists (the one found in Phase 0 was fixed outright, see D4).

---

## D4 — `Microsoft.OpenApi` Pinned to 2.7.5 (Security Fix, Not Suppression)

**Problem:** The default ASP.NET Core Web API template pulled in `Microsoft.OpenApi` 2.0.0 transitively via `Microsoft.AspNetCore.OpenApi` 10.0.10, triggering NU1903 for GHSA-v5pm-xwqc-g5wc (a high-severity stack-overflow-via-circular-schema-reference vulnerability, CVE-2026-49451).

**Alternatives considered:** (a) Suppress the warning via `NoWarn`/`WarningsNotAsErrors` and move on. (b) Pin the transitive package to a version above the advisory's fixed threshold.

**Final decision:** (b). Explicit `<PackageReference Include="Microsoft.OpenApi" Version="2.7.5" />` added directly to `RenoTrack.Api.csproj` (the advisory's fix version for the 2.x line; 3.5.4+ fixes the 3.x line, not applicable here).

**Why chosen:** Suppressing a warning about a real, confirmed vulnerability (not a false positive) is never the right default — the underlying risk still exists, just hidden. An explicit override to a patched version costs one line and actually closes the gap.

**Consequences:** Restoring the project no longer emits NU1903 at all for this package; verified by rerunning `dotnet restore` and observing the warning disappear.

---

## D5 — Never Force-Push to `main` (Incident-Driven)

**Problem (the actual incident):** During Phase 0, while both branches (`main` and `chore/phase-0-solution-bootstrap`) were being worked on in parallel by the AI assistant and the user, the assistant force-pushed a locally-rewritten `main` history (created via an orphan-branch restructuring to satisfy a different request — putting an empty first commit on `main`) without first fetching to check whether the user had already merged their own PR on GitHub. The user had, in fact, already merged PR #1 in the same window. The force-push silently overwrote that merge. It was only caught because the user noticed a subsequent "unrelated histories" error from GitHub when comparing a follow-up branch against `main` — the follow-up branch had been created from the assistant's *local*, stale view of `main`, which no longer matched what was really on GitHub.

**Root cause:** The assistant performed a destructive git operation (`push --force`) based on an assumption about remote state that was never re-verified immediately before the destructive action.

**Recovery:** Possible only because the overwritten commit objects had not yet been garbage-collected locally — `git log --oneline --all` after a fresh `git fetch` revealed the orphaned merge commit, which was then used to reconstruct the correct history via `git reset --hard` to that commit, followed by re-applying the newer (also-legitimate) work as a new commit on top, avoiding a second force-push by structuring the recovery as a fast-forward-compatible history instead.

**Final decision:** No force-push to `main`, ever, from this point forward. Every phase (or slice-group) develops on its own branch, merged only via Pull Request. Before any push, `git fetch origin` first and diff against what's actually there.

**Why chosen:** The user explicitly stated this as a durable rule after the incident, and it is now recorded as a memory/feedback entry outside this repo as well (`feedback_git_workflow_rules.md` in the assistant's persistent memory). The cost of an extra `git fetch` before any push is negligible compared to the cost of silently destroying a collaborator's just-completed work.

**Consequences:** All subsequent phases accumulate commits on a long-lived feature branch and open exactly one PR per phase (or per meaningfully-sized slice-group within Phase 2), never touching `main` directly. See `CLAUDE.md` §19.

---

## D6 — Full Solution Structure (Website, Dashboard) Built in Phase 0, Not Deferred

**Problem:** Architecture.md §3 describes six projects (`Domain, Application, Infrastructure, Api, Dashboard, Website`), but the original Phase 0 plan only scaffolded the four backend projects, deferring `Website`/`Dashboard` to Phases 10/13.

**Alternatives considered:** (a) Scaffold only backend projects now, add front-ends later exactly when their phase arrives. (b) Scaffold the full six-project structure immediately, even with `Dashboard`/`Website` empty, so the solution matches Architecture.md from day one.

**Final decision:** (b), per explicit user request during Phase 0 review.

**Why chosen:** Matching the documented full structure from day one avoids retrofitting it later and lets the solution's shape be reviewed as a whole immediately, rather than growing piecemeal in a way that could reveal structural surprises mid-project.

**Consequences:** `RenoTrack.Website` (Razor Pages) and `RenoTrack.Dashboard` (Angular 20) exist as empty/default-template skeletons. Neither references any backend project (Architecture §3: front-ends talk to the API over HTTP only) — verified structurally (zero `ProjectReference`/backend imports in either). Default template boilerplate (Angular's welcome page, ASP.NET's WeatherForecast sample) was stripped since it added no value and would only be replaced later anyway.

---

## D7 — `RenoTrack.Domain.Tests` as Its Own Project, Not Folded Into `Application.Tests`

**Problem:** Architecture.md §3 originally documented only two test projects (`Application.Tests`, `Api.Tests`). Domain-layer unit tests needed a home, and the two-project structure would have forced them into `Application.Tests`, even though they test Domain entities directly with no Application-layer involvement at all.

**Alternatives considered:** (a) Put Domain tests inside `Application.Tests` (matches the documented structure exactly, adds a direct `Domain` reference to that project). (b) Create a dedicated `RenoTrack.Domain.Tests` project referencing only `Domain`.

**Final decision:** (b).

**Why chosen:** The same "explicit dependency, structurally enforced" reasoning as D2: a dedicated project whose only reference is `Domain` makes it *impossible*, not just discouraged, for a "Domain test" to accidentally depend on Application-layer concerns (handlers, validators, DTOs). Naming a test file "Application.Tests" while testing pure Domain logic would also be misleading to a future reader. Architecture.md §3 was updated to document this as the real structure (see D26/`CLAUDE.md` §15 for the general "keep docs in sync with reality" discipline).

**Consequences:** `tests/RenoTrack.Domain.Tests/` exists, referencing only `RenoTrack.Domain`. `RenoTrack.Domain.csproj` has `<InternalsVisibleTo Include="RenoTrack.Domain.Tests" />` so this project alone can construct/test `internal`-constructor child entities (`InspectionPhoto`, `AngebotSection`, `AngebotItem`) directly, while every other assembly (`Application`, `Infrastructure`, `Api`) remains unable to bypass the aggregate root.

---

## D8 — Lead: Self-Guarded Transitions, Cross-Aggregate Coordination Lives in Application

**Problem:** StateMachine.md §1.3's guard column for Lead's transitions mixes two fundamentally different kinds of condition: some are checkable from Lead's own current state alone (e.g. "is Status currently New?"), others require knowledge of a different aggregate entirely (e.g. "Inspection belongs to this Lead," "no other open Angebot exists," "TokenLink valid & unused"). A naive reading might try to make `Lead` itself somehow aware of `Inspection`/`Angebot`/`TokenLink` to check all of these.

**Alternatives considered:** (a) Give `Lead` references/dependencies to `Inspection`, `Angebot`, `TokenLink` so it could check everything itself. (b) `Lead` enforces only what it can determine from its own state; everything else becomes the Application layer's job, performed by executing the operation on the *other* aggregate first, then calling the matching `Lead` transition method only once that succeeds.

**Final decision:** (b).

**Why chosen:** Giving `Lead` type-level knowledge of three other aggregates would create a tangled dependency graph inside the Domain layer and violate the whole point of separate aggregates (each owns its own consistency boundary). The self-guard-only design keeps `Lead` genuinely independent — it has zero compile-time references to `Inspection`, `Angebot`, or `TokenLink` as types.

**Consequences:** Every `Lead` transition method (`MarkInspectionScheduled`, `MarkInspectionDone`, `MarkAngebotInProgress`, `MarkAngebotSent`, `MarkWon`, `MarkLost`) checks only `Status`. Application-layer handlers are responsible for the cross-aggregate half of each guard, and — critically — for calling the other aggregate's operation *first*, only calling the `Lead` method once that operation has already succeeded (e.g. `ScheduleInspectionCommandHandler` creates the `Inspection` first, then calls `lead.MarkInspectionScheduled()`).

---

## D9 — `Lead.AssignInspector` Has No Status Guard (Deliberately)

**Problem:** Should assigning/reassigning a Lead's Inspector be restricted to certain `LeadStatus` values?

**Alternatives considered:** (a) Restrict it (e.g. disallow reassignment once `Won`/`Lost`). (b) No restriction at all.

**Final decision:** (b) — no `LeadStatus` guard, and the method never changes `Status`.

**Why chosen:** Neither StateMachine.md nor PermissionMatrix.md places any restriction on when this administrative action may happen. Inventing a restriction here would mean encoding a new business rule that doesn't exist in the source documents, not implementing one that does. Recorded explicitly in `Architecture.md` §6.2 so a future reader understands this absence is intentional, not an oversight — and the recommendation, if a restriction is ever wanted, is to add it as a new numbered `BR-n` in `BusinessRules.md` first, not silently add a guard.

**Consequences:** `AssignInspector(int inspectorId)` is a plain setter-shaped method with input validation only implicit (an `int`, no null check needed); callable from any `LeadStatus`, any number of times.

---

## D10 — `ItemUnit` as a Value Object, Not a Plain Enum, Not an Enum+String Pair

**Problem:** SRS FR-4.3 lists five standard units (`m², Stk, lfm, pauschal, m`) but explicitly says "etc." — unlike `LeadStatus`/`AngebotStatus`, which have exhaustive, closed transition tables, this list is *deliberately open*. A plain closed enum would contradict the SRS; a naive open representation (an enum plus a separate nullable `string CustomLabel`) can represent contradictory states (e.g. `Kind=SquareMeter` with a non-null `CustomLabel`, or `Kind=Custom` with no label at all).

**Alternatives considered:** (a) Closed enum with only the five named units (rejected — contradicts SRS's own "etc."). (b) Plain string, fully free-form (rejected — no type safety for the five common cases, no defense against near-duplicate values). (c) Enum + nullable string as two independent public fields (rejected — allows contradictory states). (d) A Value Object (`ItemUnit`) with a private constructor and validating static factories, making contradictory states unconstructable.

**Final decision:** (d).

**Why chosen:** The same "make invalid states impossible to construct, not just checked" philosophy applied throughout — matches D8's reasoning that the type itself should protect its own invariants rather than relying on callers to remember validation.

**Consequences (including a subtle edge case resolved during design):** `ItemUnit.Custom(label)` rejects — case-insensitively — any label matching one of the five reserved standard codes (`m2, Stk, lfm, pauschal, m`). This was a deliberate resolution to a real ambiguity: if `Custom("m2")` were allowed, a value round-tripped from storage as the string `"m2"` could not be distinguished from the genuine standard `SquareMeter` unit. Rejecting the collision **at construction time** (fail fast, loud, immediate) was chosen over trying to guess the right interpretation at deserialization time (silent, ambiguous, surprising to whoever entered the custom value). `Code`/`FromCode` are the single round-trip surface Infrastructure will use in Phase 3 (a `ValueConverter<ItemUnit, string>` mapping to one string column, matching ERD.md's single-column design) — Domain has zero EF Core awareness or reference.

---

## D11 — `Money` Value Object, BR-11 (Rounding Policy), and the `FromExact`/`RoundedPerBR11` Split

**Problem 1 (the underlying gap):** No source document (SRS, Architecture, BusinessRules, ERD) specified any rounding strategy for monetary calculations, yet different reasonable choices (round-at-each-step vs. round-only-at-the-end; away-from-zero vs. banker's rounding) produce different final Euro-cent totals on a legally significant document (BR-5, §14 UStG).

**Problem 1 — Final decision (this became BR-11):** Round every calculated value (line totals, then per-VAT-rate amounts) to 2 decimal places immediately upon computation, using `MidpointRounding.AwayFromZero`. Section/net totals are plain sums of already-rounded values — no re-rounding at the sum level. Gross total is the sum of the (already-rounded) net total and the (already-rounded) per-rate VAT amounts.

**Why chosen (Problem 1):** Rounding immediately and always summing already-rounded values guarantees every number shown anywhere always adds up exactly to the numbers displayed above it, avoiding an apparent "off by one cent" discrepancy that would look like an error to a customer manually checking a document. `AwayFromZero` matches standard German commercial rounding convention, more so than .NET's default `ToEven` (banker's rounding).

**Problem 2 (should this be centralized in a type?):** Once codified as BR-11, should the rounding logic live as a repeated `Math.Round(...)` call at every place a monetary value is derived, or be centralized?

**Alternatives considered:** (a) Repeat `Math.Round(x, 2, MidpointRounding.AwayFromZero)` at each call site. (b) A `Money` Value Object centralizing the rule.

**Final decision:** (b) — introduced specifically because BR-11 is now a *named, numbered business rule* needing one enforcement point, and because Architecture §6.1 already states the same calculation is reused for Invoices (a second aggregate, not yet built) — reuse across at least two aggregates plus real legal consequences of getting it wrong were the two conditions that tipped this from "would be over-engineering for a one-off" to "clearly justified."

**Problem 3 (is the rounding policy intrinsic to `Money`, or does baking it into the only construction path over-couple `Money` to today's specific policy?):** Raised explicitly during design review — is `Money`'s core concept "an amount, always exact to 2 decimals" (a fact about currency, permanent) separable from "how a raw calculation gets rounded down to that representation" (a policy, potentially subject to change)?

**Alternatives considered:** (a) One factory (`Money.Of(decimal)`) that always silently applies BR-11's rounding — rejected, because it conflates a permanent invariant with a named, specific, potentially-future-pluggable policy, and a generically-named factory hides that a business rule is being invoked. (b) Split into two factories: `FromExact(decimal)` (validates the input is *already* exact to 2dp, throws otherwise — for trusted/already-correct values like a user-entered price) and `RoundedPerBR11(decimal)` (explicitly named after the rule it implements, for raw multi-decimal calculation results).

**Final decision:** (b).

**Why chosen:** `Money`'s own private-constructor invariant ("must already be exact to 2dp") is a currency fact, not a policy — it stays permanent regardless of any future rounding-policy change. `RoundedPerBR11`'s explicit naming means a hypothetical future second policy for a different financial workflow becomes a second, equally explicit, equally named factory (e.g. `RoundedPerSomeOtherRule`) — not a silent change to behavior every existing call site already depends on.

**Problem 4 (should `Money` have a multiplication operator, e.g. `Money * decimal`?):** Raised alongside Problem 3 — is `operator *` genuinely useful given most calculations (`Quantity × UnitPrice`) originate as raw decimals before becoming `Money`?

**Final decision:** No multiplication operator on `Money`. Only `+` (safe — adding two already-rounded values never produces new precision, so no re-rounding needed) and `Sum(IEnumerable<Money>)`.

**Why chosen:** A `Money * decimal` operator returning `Money` would have to silently apply rounding internally to stay valid — exactly the "hidden policy application behind an innocuous-looking operator" problem D11 already rejected for the factory design. Removing it forces the rounding boundary (`RoundedPerBR11`) to stay visibly present at the exact point in the code where BR-11 actually applies (e.g. `AngebotItem.LineTotal => Money.RoundedPerBR11(Quantity * UnitPrice.Amount)`), rather than hidden inside `UnitPrice * Quantity`.

**Consequences:** `Quantity` stays a plain `decimal` on `AngebotItem` (never wrapped in a Money-like type) — only genuinely-monetary fields (`UnitPrice`, `LineTotal`, `Subtotal`, `NetTotal`, `GrossTotal`, `VatBreakdownLine.NetAmount/VatAmount`) are `Money`. Verified with tests covering real midpoint-rounding edge cases (`1.005 → 1.01`, `-1.005 → -1.01`, `0.125 → 0.13`) using `decimal` literals specifically to avoid `double`-precision artifacts.

---

## D12 — `AngebotItem`: No Update/Remove Method (Left Open, Not a Rule)

**Problem:** Should `AngebotItem` support in-place editing after creation (e.g. fixing a mis-typed quantity)?

**Alternatives considered:** (a) Assume editing should be supported and build `Update(...)` (reasoning: FR-4.7 mentions saving a draft and returning to it later, implying some form of ongoing editing). (b) Note that Architecture §5.2's representative endpoint list has no PATCH/DELETE for individual items (only `POST .../items` to add one) and treat this as evidence that in-place editing may not be intended — build no update method, but explicitly do **not** document this absence as a permanent rule, since "no endpoint happens to be documented yet" is not the same as "editing is forbidden."

**Final decision:** (b), with the explicit distinction preserved: this is a deliberately open question, revisited only with real evidence, not treated the same way as `BR-10`'s Inspection immutability (which *is* backed by explicit business reasoning about evidentiary integrity).

**Why chosen:** The user specifically pushed back on an earlier draft of this reasoning that risked conflating "no evidence either way" with "editing is forbidden" — the correct position is narrower: build nothing now, document nothing as a permanent decision, and leave the door open.

**Consequences:** Corrections to an existing `AngebotItem` currently require removing and re-adding an item via the owning `AngebotSection`/`Angebot` (no remove method exists yet either, for the same reason). No `Architecture.md` note or `BusinessRules.md` entry claims this is final.

---

## D13 — `Angebot` Is the Sole Public Entry Point for Its Sections/Items

**Problem:** Sequence Diagram §4's literal pseudocode shows `section.AddItem(...)` and `angebot.RecalculateTotals()` as two separate calls — read literally, this would let the Application layer add an item to a section without ever triggering the Angebot-level totals recalculation, if a handler forgot the second call.

**Alternatives considered:** (a) Make `AngebotSection.AddItem` public, matching the diagram's literal shape, and trust every handler to also call `Angebot.RecalculateTotals()` afterward. (b) Make `AngebotSection`'s constructor and `AddItem` method `internal`, reachable only through `Angebot.AddSection(...)`/`Angebot.AddItemToSection(...)`, which wrap the child mutation and the resulting recalculation inside one atomic public operation.

**Final decision:** (b). The Sequence Diagram is treated as a conceptual description of *what* happens, not a literal specification of *which methods are public* — consistent with Architecture §6's own explicit statement that "child entities are only ever modified through their aggregate root."

**Why chosen:** (a) reintroduces exactly the kind of "caller must remember an extra step" footgun this whole project has consistently designed against (see D8, D10, D11's rejected alternatives). (b) makes it structurally impossible to leave `NetTotal`/`GrossTotal` stale from outside the aggregate.

**Consequences:** `AngebotSection`'s constructor and `AddItem` are `internal`; verified by reflection tests (`GetConstructors(Public)` empty, `GetMethod("AddItem", Public)` returns null). Same pattern applied to `AngebotItem`'s constructor.

---

## D14 — `Angebot.AddItemToSection` Takes the `AngebotSection` Object, Not a `sectionId`

**Problem:** The originally-agreed public API used `AddItemToSection(int sectionId, ...)`. While implementing it, a real bug was discovered: before EF Core assigns real database ids (not yet built as of Phase 2), every freshly-created `AngebotSection` within a given `Angebot` shares `Id == 0` — an id-based lookup inside `_sections` cannot reliably distinguish between multiple sections added in the same session, and would silently always resolve to whichever section happens to match first (`Id == 0`), a genuine correctness bug, not a style concern.

**Alternatives considered:** (a) Keep the `int sectionId` signature and accept the bug will surface once real persistence exists (rejected — this is a genuine, not hypothetical, defect). (b) Change the signature to accept the actual `AngebotSection` instance.

**Final decision:** (b). `AddItemToSection(AngebotSection section, ...)`, with an internal check (`_sections.Contains(section)`) verifying the passed object actually belongs to this aggregate, so the aggregate boundary is still enforced even though identity is now reference-based rather than id-based.

**Why chosen:** Reference equality doesn't depend on persistence having happened, sidestepping the zero-id ambiguity entirely, and works correctly both before and after Phase 3 assigns real ids — the Application layer will simply resolve which section a request targets (e.g. from a route id, once ids are real) by reading the already-loaded `Sections` collection, then pass the resolved instance in. This signature does not need to change once Phase 3 arrives.

**Consequences:** This entity-identity-instability problem recurred later in `RenoTrack.Application`'s test fakes too (see D-Fakes below) for the exact same underlying reason, confirming it as a general pattern to watch for, not a one-off.

---

## D15 — `Angebot.NetTotal`/`GrossTotal`: Stored + Privately Recalculated, Not Computed Properties (Reconsidered Mid-Design)

**Problem:** Following the same "computed, never stored" reasoning already applied to `AngebotItem.LineTotal`/`AngebotSection.Subtotal` (no ERD-stated performance reason applies at that granularity), an initial proposal made `Angebot.NetTotal`/`GrossTotal`/`VatBreakdown` all pure computed properties too, eliminating the need for any `RecalculateTotals()` method at all.

**Pushback (from the user, during design review):** ERD.md explicitly documents `NetTotal`/`GrossTotal` as *cached/denormalized* columns, for a stated reason (fast list-page rendering, Wireframes.md B2) — a reason that specifically *does* apply at the Angebot level (unlike item/section granularity). The user also argued that an explicit, named `RecalculateTotals()` domain operation better expresses the ubiquitous language of the business process (Sequence Diagram §4 literally narrates "add item → recalculate totals" as a distinct conceptual step) than a silently-computed property, and that repeatedly recomputing a full tree walk on every property access is a real (if modest) inefficiency when a value is read multiple times within one request.

**Final decision:** Reverted the initial computed-property proposal. `NetTotal`/`GrossTotal` are stored fields (`{ get; private set; }`), kept current by a `private void RecalculateTotals()` called at the end of every public method that mutates the Sections/Items tree (`AddSection`, `AddItemToSection`).

**Why chosen:** Both of the user's objections were substantive, not stylistic — the ERD's stated caching rationale genuinely does apply at this level (unlike items/sections), and a private-only recalculation method still delivers the *same* structural guarantee the computed-property proposal was trying to achieve (no public way to leave totals stale), just via a different mechanism. The AI assistant's original position was not "wrong," but the user's counter-argument was strictly better once weighed, and the assistant explicitly conceded rather than defending the original proposal past the point its rationale held up.

**Consequences:** `VatBreakdown` remains a pure computed property regardless of this reversal, since it has no ERD column at all (nothing to denormalize) and is a variable-shaped collection, not a scalar — the reasoning for it was never in question, only `NetTotal`/`GrossTotal`. `Architecture.md` §6.1/§6.2 documents both the "why stored" reasoning for Net/Gross and the "why computed" reasoning for LineTotal/Subtotal/VatBreakdown side by side, explicitly contrasting them so a future reader sees this was a deliberate distinction, not an inconsistency.

---

## D16 — `Angebot.DecisionResult` Removed From the Domain Entirely (Not Even a Computed Property)

**Problem:** ERD.md documents a `DecisionResult` string column (`"Approved" | "Rejected"`) on `Angebot`, seemingly redundant with `Status` (which already reaches `CustomerApproved`/`CustomerRejected` — the same information, differently encoded).

**Alternatives considered:** (a) Keep it as a genuinely separate stored field, matching ERD literally. (b) Make it a computed property deriving the string from `Status` (the same "derive, don't duplicate" pattern used for `LineTotal`/`Subtotal`/`NetTotal`). (c) Remove it from the Domain model entirely — `Status` is the one and only authoritative fact; a presentation-friendly string is an Application/DTO/UI concern, not a Domain concept.

**Final decision:** (c).

**Why chosen:** The user specifically distinguished this from the `LineTotal`/`Subtotal` computed-property precedent: those are genuine *financial/domain* facts worth exposing as Domain concepts, whereas mapping an enum to a display string ("Approved"/"Rejected") is presentation/DTO-mapping work, not a Domain responsibility at all — adding even a computed property for it would still be Domain reaching into a concern that belongs one layer up.

**Consequences:** `Angebot` has no `DecisionResult` member of any kind. If a `DecisionResult`-shaped field is ever wanted at the persistence layer (e.g. for reporting convenience), that is an Infrastructure/Application-layer mapping decision made later, independently, not something Domain carries.

---

## D17 — `Angebot.CreateDraft(...)` Renamed to `Angebot.Create(...)`

**Problem:** The originally-agreed factory name mirrored Sequence Diagram §4's literal pseudocode (`Angebot.CreateDraft(...)`), but the aggregate is *always* created in `Draft` status — there is no other initial state, making the "Draft" qualifier redundant noise.

**Final decision:** Renamed to `Angebot.Create(...)`, matching the naming convention already used by `Lead.Create(...)`/`Inspection.Schedule(...)`/`CatalogItem.Create(...)`.

**Why chosen:** Simple, uncontroversial simplification — the qualifier added no information a reader didn't already know.

**Consequences:** Sequence Diagram §4 originally still referenced the old name (`Angebot.CreateDraft(...)`) in its pseudocode; this was a stale-documentation bug caught and fixed during Phase 2 (see D26/PHASE2_PROGRESS.md, `CreateAngebotCommand` slice) — a reminder that renaming Domain members requires a documentation sweep, not just a code change.

---

## D18 — BR-10: A Completed Inspection Is Immutable

**Problem:** No document explicitly stated whether an Inspector could keep adding photos or editing notes after marking an Inspection complete.

**Alternatives considered:** (a) Allow continued editing after completion (no restriction). (b) Forbid any further `AddPhoto`/`UpdateNotes` once `CompletedAt` is set, requiring a distinct future "reopen" action instead of silent post-completion editing.

**Final decision:** (b), formalized as BR-10 in `BusinessRules.md`.

**Why chosen:** A completed Inspection is the evidentiary basis the Angebot gets built from (SRS FR-3.4) — allowing silent edits afterward would blur exactly what evidence a subsequent Angebot was actually based on, creating audit ambiguity. This mirrors the same "lock after a workflow gate" pattern StateMachine.md §2.4 already applies to Angebot editing (locked after `InReview`), and the same "don't allow silent correction, require an explicit action" philosophy behind BR-7 (Lead) and, later, BR-12 (Catalog retirement).

**Consequences:** `Inspection.AddPhoto`/`UpdateNotes` both call a shared `EnsureNotCompleted(actionName)` guard, throwing `InvalidOperationException` naming both the action and the completion timestamp. No "reopen" use case exists yet — deliberately deferred as a distinct future feature, not built speculatively now.

---

## D19 — BR-12: Catalog Items Are Retired, Never Deleted

**Problem:** A direct contradiction was found between two documents while designing `CatalogItem`: `PermissionMatrix.md` §6 explicitly grants Admin a "Delete/retire a Catalog item" action, but `ERD.md`'s `CatalogItem` schema had no field at all to represent a deleted/retired state (no `IsActive`, `IsRetired`, or `Status` column).

**Alternatives considered:** (a) Implement a true hard delete (row removed from the database). (b) Introduce an `IsRetired` boolean flag; "delete" means setting it `true`, never removing the row.

**Final decision:** (b), formalized as BR-12, with `ERD.md` updated to add the `IsRetired` column and `PermissionMatrix.md`'s "Delete/retire" row clarified to state what "delete" actually means.

**Why chosen:** A hard delete would destroy the very traceability BR-8 depends on — every `AngebotItem.CatalogItemId` pointing at a deleted `CatalogItem` would become a dangling reference, breaking the "trace back to origin" capability even though BR-8 already ensures the *copied data* on the `AngebotItem` itself would remain intact. This also matches the project's now-consistent "never truly delete a historical record" philosophy (Leads never deleted, Invoices voided not deleted per BR-9, completed Inspections immutable per BR-10) — retiring CatalogItem fits that existing pattern far better than physical deletion would.

**Consequences:** `CatalogItem.Retire()` sets `IsRetired = true`; it is idempotent (retiring an already-retired item is a no-op, not an error — there is no meaningful "from" state to guard here, unlike an `AngebotStatus` transition). No `Delete`/`Remove` method or repository operation exists that removes a `CatalogItem` row. The eventual Catalog picker query (not yet built) is expected to filter out retired items.

---

## D20 — `CatalogItem.Create(...)`: Single Factory, Not Two (Admin vs. "Save As")

**Problem:** `CatalogItem` can originate two ways — Admin curating directly (SRS FR-4.8) or an Inspector's one-click "save as Catalog item" action from a custom Angebot line item (SRS FR-4.10, PermissionMatrix §6). Should these be two differently-named factory methods (`CreateByAdmin`/`CreateFromAngebotItem`) or one?

**Final decision:** One factory: `Create(title, defaultUnit, suggestedUnitPrice, defaultSpecification = null, createdFromAngebotItemId = null)`.

**Why chosen:** The two paths produce an identical Domain shape — the only difference (`CreatedFromAngebotItemId` set or not) is already expressed as an optional parameter, not a structural difference. *Who* is allowed to call this factory (Admin directly vs. any Inspector via "save as") is an authorization concern for the Application/API layer, not a Domain-shape distinction — consistent with the project's repeated position that Domain never encodes "who is calling" (see D8, D9, and the ownership-vs-authorization split in D31).

**Consequences:** `CatalogItem.Create(...)` has no notion of "who" created it beyond the optional traceability field; enforcing that only Admins can omit `createdFromAngebotItemId` (direct curation) while Inspectors must supply it (save-as) is left entirely to the not-yet-built Application-layer commands.

---

## D21 — `CatalogItem.Update(...)` Exists (Contrast With `AngebotItem`'s Deliberate Absence)

**Problem:** Should `CatalogItem` support in-place editing, given `AngebotItem` deliberately does not (D12)?

**Final decision:** Yes — `Update(title, defaultUnit, suggestedUnitPrice, defaultSpecification = null)` exists, changing every field except `CreatedFromAngebotItemId`/`CreatedAt` (both immutable historical facts about origin).

**Why chosen:** Unlike `AngebotItem` (no documented evidence either way), `PermissionMatrix.md` §6 **explicitly** documents "Edit an existing Catalog item — Admin F" as a real, granted capability. This is the clean, deliberate contrast the project draws between "build only what's evidenced" (D12) and "build what's evidenced, even if a sibling entity doesn't get the same treatment" — the two entities are not being treated inconsistently; they are being treated according to what their respective documents actually say.

**Consequences:** `Update(...)` duplicates the same two validation checks as `Create(...)` (title non-empty, price non-negative) — flagged by the user as a known, accepted minor duplication worth revisiting only if the pattern needs to change in the future, not urgent enough to refactor now.

---

## D22 — No MediatR; Hand-Rolled `ICommandHandler<TCommand, TResult>`

**Problem:** Phase 2 needed a CQRS-lite dispatch mechanism (Architecture §5.1 mentions the pattern by name but names no specific library).

**Alternatives considered:** (a) MediatR — industry-standard, gives pipeline behaviors (validation, logging, etc.) for free via decorators. (b) A single hand-written generic interface, no framework.

**Final decision:** (b).

**Why chosen:** This project is explicitly educational (the user's own framing from the very first message of the project: train a junior developer by making every step visible). MediatR's pipeline-behavior model is powerful but adds indirection that would hide exactly the orchestration steps the project exists to make explicit and traceable via "Go to Definition." SRS §8's own "favor the straightforward version" philosophy reinforces this. Adding MediatR later, if the project ever outgrows hand-rolled dispatch, is a contained, reversible change.

**Consequences:** Every handler calls its own `IValidator<T>.ValidateAndThrowAsync(...)` explicitly at the top of `HandleAsync` — there is no automatic validation pipeline step. Cross-cutting concerns (audit, notifications) are written out explicitly in each handler, not injected via a shared behavior.

---

## D23 — `IEmailSender` Uses Dedicated Notification Models, Never Feature DTOs

**Problem (a real dependency-direction bug caught mid-review, not a hypothetical):** `IEmailSender.SendNewWebsiteLeadNotificationAsync` was initially designed to take a `LeadDto` parameter directly. `IEmailSender` itself lives in `RenoTrack.Application.Common` — meant to be the *lowest-level* part of Application, depended upon by every feature folder, never depending on one. Taking `LeadDto` as a parameter made `Common` import `RenoTrack.Application.Leads.Dtos`, a feature-folder namespace — the dependency arrow pointing backwards from how the rest of the project is organized. Every future notification method (Angebot review, decision, etc.) would have compounded this, making `Common` depend on every feature in sequence.

**Alternatives considered:** (a) Leave it as-is (accept `Common` depending on `Leads`). (b) Make notification methods take only primitive parameters (no DTO at all) — avoids the dependency issue but produces long, unexpressive parameter lists as more fields are needed. (c) Introduce dedicated notification request models, living in `Application.Common.Notifications`, expressing exactly what each email template needs — narrower than the full feature DTO, and owned by `Common` itself rather than any feature.

**Final decision:** (c).

**Why chosen:** (c) keeps `Common` genuinely at the bottom of the dependency graph (it now depends only on its own `Notifications` sub-namespace) while keeping method signatures expressive and typed, rather than degrading to primitive soup. It also naturally produces narrower models than the full feature DTO would (e.g. `NewWebsiteLeadNotification` has only `LeadId, LeadName, LeadPhone, LeadEmail` — no `Address`/`Notes`/`Status`/`CreatedAt`, since a notification email genuinely has no use for those).

**Consequences:** Every subsequent notification (`AngebotSubmittedForReviewNotification`, `AngebotChangesRequestedNotification`) follows this same pattern from the start, with no repeat of the original mistake. This is treated as a permanent convention (`CLAUDE.md` §11), not a one-off fix.

---

## D24 — `AuditAction` as One Centralized, Growing Enum (Not Free Strings, Not Per-Entity Enums)

**Problem:** `IAuditService.LogAsync`'s `action` parameter was originally a plain `string`. The user flagged, before a second call site could drift, that free strings risk inconsistent naming across handlers over time (`"Created"` vs. `"Create"` vs. `"LeadCreated"` vs. `"Added"`, depending on who wrote which handler).

**Alternatives considered:** (a) Leave it as a `string`, rely on developer discipline. (b) Constants. (c) One shared enum for the whole system, entity-prefixed value names (`LeadCreated`, `AngebotSubmittedForReview`, ...). (d) Separate per-entity enums (`LeadAuditAction`, `AngebotAuditAction`, ...).

**Final decision:** (c).

**Why chosen over (d):** A single enum gives one place to see every possible audit action across the whole system — useful for whoever eventually builds the Audit Log UI (PROJECT_ROADMAP.md Phase 15) — without the proliferation of many small, thinly-populated per-entity enums. Entity-prefixed naming (`AngebotSubmittedForReview`, not just `SubmittedForReview`) was chosen even though `entityType` is already passed as a separate string parameter to `LogAsync`, because a self-descriptive value reads correctly on its own when scanning a raw list of actions (e.g. in a database query result or log), without needing to cross-reference `entityType` alongside it.

**Consequences:** `IAuditService.LogAsync`'s `action` parameter type changed from `string` to `AuditAction`. The enum started with a single value (`LeadCreated`) and grows by exactly one value per new command that needs to log something — never pre-populated with anticipated future values (same discipline as repository growth, D-Repo-Growth).

---

## D25 — BR-13: Scheduling an Inspection Automatically Assigns Its Inspector to the Lead

**Problem:** While designing `ScheduleInspectionCommand`, a real gap was found: `PermissionMatrix.md` §1 scopes an Inspector's own pipeline view by `Lead.AssignedInspectorId`, but no document explicitly stated that scheduling an Inspection for a given Inspector also assigns that Inspector to the Lead. Without this, a scheduled Inspector would never see the Lead in their own pipeline unless a separate, entirely undocumented "assign inspector" action happened to precede or follow scheduling every single time.

**Alternatives considered:** (a) Treat assignment and scheduling as two genuinely independent actions, requiring the Admin to call both separately every time (matches the absence of any explicit documented link). (b) Treat scheduling as implicitly performing the assignment too, formalized as a new numbered business rule.

**Final decision:** (b), formalized as BR-13.

**Why chosen:** The Inspector being scheduled is, self-evidently, the one performing work for that Lead — no scenario exists in current requirements where an Inspection would be scheduled for one Inspector while the Lead remains assigned to someone else (or no one). Requiring a redundant, always-paired second action for every scheduling event would add friction with no documented business value, and every other "implicit" behavior in this project's Domain (e.g. `ChangesRequested → Draft`) is one that's explicitly evidenced in the docs — this is the same category of "obviously-intended, just not spelled out" gap BR-10/BR-12 also addressed.

**Consequences:** `ScheduleInspectionCommandHandler` calls both `Inspection.Schedule(...)` and `Lead.AssignInspector(inspectorId)` in the same operation. `StateMachine.md`'s `New → InspectionScheduled` row and `PermissionMatrix.md`'s "Assign/reassign Inspector" row were both updated to cross-reference BR-13.

---

## D26 — Audit Target Principle: Log Against the Aggregate the Business Cares About, Not Necessarily the One That Changed Directly

**Problem:** While implementing `ScheduleInspectionCommand`, a question arose: an `Inspection` is created, but the real "business-meaningful" side effect is `Lead.MarkInspectionScheduled()`. Which entity should the audit entry target?

**Investigation:** `ERD.md`'s `AuditLog` table has **only** `EntityType`/`EntityId` — no cross-reference column that would let a query recover "everything that happened around this Lead" from a child-entity-typed row (e.g. no `LeadId` column on an `Inspection`-typed audit row). Combined with Wireframe C1's "Activity Timeline" being a **per-Lead** view (the only documented audit UI in the whole project), logging against `Inspection` would mean this event could never surface on the one screen designed to show it.

**Final decision:** Log against the aggregate whose state the *business* cares about (usually the one with the user-facing history screen), not necessarily the aggregate a command's side effect happens to create or touch most directly.

**Consequences and later reuse of this principle:**
- `ScheduleInspectionCommandHandler`/`CompleteInspectionCommandHandler` both log against `Lead`.
- `CreateAngebotCommandHandler` also logs against `Lead` (not the newly-created `Angebot`) — and this specifically revealed that `Sequence Diagram.md` §4 had **omitted** an audit step for Angebot creation entirely. Once this general principle existed, that omission looked like an oversight rather than an intentional choice, so both the diagram and the implementation were corrected together (documentation-first, per `CLAUDE.md` §15).
- `SubmitAngebotForReviewCommandHandler`/`ApproveAngebotCommandHandler`/`RequestAngebotChangesCommandHandler` all log against `Angebot`, **not** `Lead` — because `StateMachine.md` §1.3 explicitly states Angebot's internal review states cause "no Lead-level change." This is the same principle correctly producing the *opposite* target in a different situation — proof the rule is a genuine "which aggregate does the business care about" test, not a blanket "always log against Lead."
- Formalized as a general rule in `Architecture.md` §11, not left implicit in any one handler.

---

## D27 — `NotFoundException` Introduced (First Application Exception Type)

**Context:** First needed when `ScheduleInspectionCommand` became the first command to *load* an existing aggregate (`CreateLeadCommand` only ever created one, with nothing to "not find"). `ILeadRepository.GetByIdAsync` returns `Lead?`; the handler needed a way to signal "no such Lead" distinctly from a validation failure or a business-rule violation.

**Final decision:** `NotFoundException(string entityName, object key)` in `Application.Common.Exceptions`, thrown as `?? throw new NotFoundException(nameof(Lead), command.LeadId)` immediately after a `GetByIdAsync` call returns null.

**Why chosen:** A distinct, purpose-named exception type (rather than reusing `InvalidOperationException` or a generic exception) gives the not-yet-built Phase 4 API middleware an unambiguous signal to map to HTTP 404, separate from Domain guard failures (which propagate as `ArgumentException`/`InvalidOperationException` unwrapped) and from validation failures (FluentValidation's own `ValidationException`).

**Consequences:** This exact pattern (`?? throw new NotFoundException(...)`) repeats identically at the top of every subsequent handler that loads an aggregate by id.

---

## D28 — `IOwnershipValidator` Introduced After the Third Occurrence, as a Named-Business-Intent Service (Not a Generic Comparison Helper)

**Problem (first two occurrences, duplicated verbatim):** `CompleteInspectionCommandHandler` needed to verify "is the caller the Inspector this Inspection is assigned to?" (`inspection.InspectorId != command.CompletedByInspectorId → throw ForbiddenException(...)`). `UploadInspectionPhotoCommandHandler` needed the *exact same check*, duplicated inline.

**Decision point (explicitly deferred at the second occurrence):** The user was asked whether two occurrences justified extraction, and explicitly said no — "one occurrence is not a pattern," wait for a third, and specifically flagged that the third occurrence should also confirm the check would recur in a *genuinely different* aggregate relationship (not just the same `Inspection.InspectorId` check a third time), to justify an interface abstraction rather than just a de-duplicated private method.

**Third occurrence:** `UpdateInspectionNotesCommandHandler` needed the same `Inspection.InspectorId` check a third time — confirming the extraction threshold — and the design review for that slice explicitly anticipated the check would recur again for `Angebot` ownership (`Angebot.CreatedByInspectorId`) in the very next command (`AddAngebotSectionCommand`), which it did.

**Alternatives considered for the shape of the extraction:** (a) A fully generic helper: `OwnershipGuard.EnsureOwnedBy(int resourceOwnerId, int callerId, string resourceName, int resourceId)`, reusable across any entity via primitive parameters. (b) A named-business-intent service, `IOwnershipValidator`, with one method per specific relationship (`EnsureInspectionOwnership(Inspection, int)`, `EnsureLeadOwnership(Lead, int)`, `EnsureAngebotOwnership(Angebot, int)`), even though each method's internal comparison is the same shape.

**Final decision:** (b) — the user explicitly rejected (a) even after initially discussing it as the natural generalization, specifically because a generic helper "loses business intent" at the call site: reading `OwnershipGuard.EnsureOwnedBy(x, y, "Inspection", z)` requires cross-referencing a string parameter to know what's being checked, whereas `ownershipValidator.EnsureInspectionOwnership(inspection, inspectorId)` is self-explanatory. The user was explicit that "a little duplication behind expressive APIs is preferable to one generic helper that gradually accumulates unrelated ownership rules."

**Why `OwnershipValidator` (the implementation) lives in `RenoTrack.Application`, not `RenoTrack.Infrastructure`:** Unlike every other service interface (`IFileStorage`, `IEmailSender`, `IAuditService`, `INumberGeneratorService`), it has zero external dependency (no EF Core, disk, network, or SMTP) to justify an Infrastructure-side implementation — there is no "swap this for a different backend" scenario the way there is for file storage. It remains an interface anyway, purely for DI-registration consistency and so a test could substitute a fake if ever useful, not because a second implementation is expected.

**Consequences:** This decision directly established the mechanical rule now used to decide whether *any* future command needs an ownership check at all — see D31.

---

## D29 — File-Upload Ordering Bug: Discovered, Diagnosed, and Fixed by Restructuring the Workflow (Not by Adding Domain State)

**Problem (a genuine bug caught before commit, not a hypothetical):** `UploadInspectionPhotoCommandHandler`'s first draft called `IFileStorage.SaveAsync(...)` (writing bytes to storage) **before** calling `Inspection.AddPhoto(fileUrl, caption)` (which enforces BR-10 — no photos after `CompletedAt` is set). If the targeted Inspection was already completed, the file got physically written to storage anyway, and only *then* did `AddPhoto` throw, leaving an orphaned file in storage with no corresponding `InspectionPhoto` row — on every single attempt to upload to an already-completed Inspection, not just a rare edge case.

**First proposed fix (rejected by the user):** Add a read-only `Inspection.IsEditable` property (`=> CompletedAt is null`), letting the handler check *before* calling `IFileStorage.SaveAsync`. The user explicitly rejected this: "I don't want us to expose internal domain state primarily to optimize an application workflow... the business invariant already exists: `inspection.AddPhoto(...)` is the authoritative rule. I don't want to introduce a second way of asking the same question." The user asked instead for the *workflow itself* to be re-examined: could the storage operation be delayed, or could the key/identifier be decided before the upload happens?

**Actual fix (accepted):** Invert which value is "produced by" which step. Instead of `IFileStorage.SaveAsync` inventing and returning a `FileUrl` after the upload succeeds, the **handler computes the `FileUrl` itself up front** (`$"inspections/{id}/{Guid.NewGuid()}{ext}"` — a pure string computation, no I/O) and calls `Inspection.AddPhoto(fileUrl, caption)` — BR-10's *existing* guard — **before** calling `IFileStorage.SaveAsync` at all. If Domain rejects it, the upload never happens.

**Why chosen over the rejected `IsEditable` alternative:** The fix uses the exact same single existing invariant (`AddPhoto`'s `CompletedAt` check) — it just runs earlier in the sequence, rather than adding a second, redundant way to ask the same question. No Domain surface area grows at all; `Inspection`'s public API is completely unchanged.

**Consequences:** `IFileStorage.SaveAsync`'s signature changed from "invents and returns a `FileUrl`" to `SaveAsync(Stream content, string fileUrl, CancellationToken ct)` — the caller supplies the key. This was generalized into a permanent principle recorded in `Architecture.md` §9 ("the Application layer is responsible for generating stable external resource identifiers before invoking external infrastructure, when doing so improves workflow consistency"), explicitly anticipated to recur for invoice PDFs or other generated/stored documents later in the project.

---

## D30 — `AddAngebotItemCommand` Deliberately Postponed Until `CatalogItem` Exists

**Problem:** Per `PROJECT_ROADMAP.md`'s original ordering, the Angebot workflow's commands were to be implemented roughly in business order, and `AddAngebotItemCommand` is next in Sequence Diagram §4's flow after `AddAngebotSectionCommand`. However, `AddAngebotItemCommand` genuinely supports two paths per BR-8/SRS FR-4.9: adding an item from an existing `CatalogItem` (copying its fields) or a fully custom item — and `CatalogItem`'s own Application layer (Create/Update/Retire/Search) had not been built yet at that point in Phase 2.

**Alternatives considered:** (a) Implement `AddAngebotItemCommand` now, but only its custom-item path, deferring the Catalog-sourced path until `CatalogItem`'s Application layer exists later. (b) Postpone `AddAngebotItemCommand` entirely — implement the rest of the Angebot review workflow (`SubmitForReview`, `Approve`, `RequestChanges`) first, then build `CatalogItem`'s Application layer, then return to `AddAngebotItemCommand` and implement both paths together from the start.

**Final decision:** (b), explicit user decision.

**Why chosen:** `AddAngebotItemCommand` represents *one* business use case with two supported entry mechanisms, not two separate use cases — implementing only one path now would mean reopening and modifying the same command shortly afterward once Catalog support arrives, producing a temporary, incomplete vertical slice rather than a genuinely finished one. The user explicitly said: "I'd rather implement the complete use case once than introduce a temporary implementation that we'll immediately revisit."

**Consequences:** Phase 2's implementation order became: Lead → Inspection (all four commands) → Angebot workflow *except* `AddAngebotItemCommand` (Create, AddSection, SubmitForReview, Approve, RequestChanges) → **CatalogItem** (not yet started as of this document) → finally `AddAngebotItemCommand` with both paths. `ItemDto`/`AngebotSummaryDto` remain unbuilt until that final step.

---

## D31 — Role-Based Authorization vs. Resource-Ownership: Formal Split (Discovered via `ApproveAngebotCommand`)

**Problem:** `ApproveAngebotCommand` is the first command performed by an **Admin** rather than an Inspector. A design-review question was raised explicitly: does `IOwnershipValidator` apply here, the same way it applied to every Inspector-scoped command so far?

**Investigation:** `PermissionMatrix.md` §4 marks "Approve Angebot" as Admin-**`F`** (full access) — a fundamentally different marking than every prior ownership-checked action, all of which were Inspector-**`S`** (scoped). `F` means *any* authenticated Admin may act on *any* Angebot; there is no "which specific Admin" question to ask at all, unlike `S` actions where "which specific Inspector" is exactly the question being asked.

**Final decision:** No `IOwnershipValidator` call exists in `ApproveAngebotCommandHandler` (nor, later, `RequestAngebotChangesCommandHandler` — also Admin-`F`). The rule, formalized in `Architecture.md` §7.3: role-based authorization (checking a JWT role claim, needing no Domain data at all) is resolved entirely at the API layer (`[Authorize(Roles="Admin")]`, not yet built); resource-ownership rules (`IOwnershipValidator`) apply only when PermissionMatrix marks an action `S`.

**Why chosen:** The user was explicit that using `IOwnershipValidator` for an `F`-marked action "would weaken the semantic meaning of the abstraction by mixing ownership with authority" — `IOwnershipValidator`'s entire value comes from expressing "is this the *specific* owner," and applying it to a case with no ownership concept at all would blur that meaning for every other call site using it correctly.

**Consequences:** This became a mechanical, checkable rule for every future command: consult PermissionMatrix.md's letter (`F` or `S`) for the specific action before deciding whether `IOwnershipValidator` participates — not a judgment call repeated from scratch each time. `ApproveAngebotCommandHandler`'s absence of an ownership step was explicitly confirmed by the user as *not* an inconsistency with other handlers, but the correct reflection of a genuinely different business rule.

---

## D32 — `AngebotReviewComment`: A Genuine Domain Gap Discovered and Filled Mid-Phase-2, Not a New Feature

**Problem:** While designing `RequestAngebotChangesCommand` (which includes an Admin-entered comment per Sequence Diagram §5), a question was raised: does this comment belong inside the `Angebot` aggregate (reopening Phase 1's decision that `AngebotReviewComment` is *not* an Angebot child, per Architecture §6's aggregate diagram), live as transient data used only for the notification email, or represent an entirely separate, persisted Domain concept that was simply never built?

**Investigation (verifying, not assuming, the prior Phase 1 decision still held):**
- `ERD.md` models `AngebotReviewComment` as its own table (`Id, AngebotId, AdminUserId, Comment, CreatedAt`), explicitly noted as an "append-only log of the review loop" (SRS FR-5.4).
- Wireframe D3 (Admin's review screen) displays "Previous review comments (if any), **threaded**" — a real, persistent, queryable history, not one-shot notification content that could be discarded after sending an email.
- `PermissionMatrix.md` §4 grants **both** roles read access to "View review comment history" (Admin F, Inspector R) — further confirming this is data meant to be queried back later, not transient.
- `Architecture.md` §6's aggregate list, re-checked, still does **not** list `AngebotReviewComment` as a child of `Angebot` — the original Phase 1 decision to keep it outside the aggregate remains correct and unchanged.
- **Before finalizing**, the user specifically asked for one additional verification: does the documented workflow actually support *multiple* review cycles (Draft → InReview → ChangesRequested → Draft → InReview → ... repeated)? If not, a single-comment or transient-notification model might have sufficed. Verified explicitly: SRS FR-5.3 states "this loop may repeat as many times as needed," and Sequence Diagram §5 states, verbatim, "Loop repeats until Admin approves." Multiple cycles are not just possible but explicitly expected.

**Final decision:** `AngebotReviewComment` is built as a new, independent Domain aggregate root (same "related by id only, no navigation reference" pattern as every other independent aggregate) — a genuine gap in Phase 1's original scope (which only covered `Lead`/`Inspection`/`Angebot`/`CatalogItem` and simply never mentioned this entity), not a new feature and not a reversal of the Phase 1 aggregate-boundary decision.

**Why chosen:** With multi-cycle review confirmed, an append-only, independently-queryable aggregate is the only model consistent with all four pieces of evidence above — each review cycle produces its own comment, accumulating independently of `Angebot.Status`, which simply oscillates between `Draft`/`InReview`/`ChangesRequested` across cycles.

**Consequences:** `AngebotReviewComment.Create(angebotId, adminUserId, comment)` — no update/delete method (matches ERD's own "append-only" description; not a new numbered BR, since ERD already documents this behavior, it just hadn't been implemented). `RequestAngebotChangesCommandHandler` composes two fully independent Domain operations — `angebot.RequestChanges(reviewedByAdminId)` (workflow transition only, no comment persistence) and `AngebotReviewComment.Create(...)` (created independently in the Application layer) — with neither aggregate's type referencing the other at all, verified by reflection-based structural tests. This is the reason `Angebot.RequestChanges(...)` was deliberately designed back in Phase 1 to take *no* comment parameter at all — the separation was anticipated even before this entity existed.

---

## D33 — `Inspection.AddPhoto` Changed to Return the Created `InspectionPhoto` (Domain Fix, Found During Application-Layer Work)

**Problem:** While building `UploadInspectionPhotoCommandHandler` (Phase 2, Slice 4), the handler needed to return a `PhotoDto` built from the photo it had just caused `Inspection` to create — but `Inspection.AddPhoto(string fileUrl, string? caption)`, built in Phase 1, returned `void`. The only way to get the created `InspectionPhoto` back was `inspection.Photos.Last()` — fragile, and dependent on collection ordering never changing (true today, not guaranteed forever).

**Context that made this look like a bug, not a style preference:** `AngebotSection.AddItem(...)`, built later in the *same* Phase 1 session, already returns the `AngebotItem` it creates — the exact same shape of operation (add a child to a collection, caller needs a reference to exactly the one just added). Two entities with an identical structural pattern behaving differently was flagged as a genuine inconsistency, not a deliberate design choice with its own rationale.

**Alternatives considered:** (a) Use `inspection.Photos.Last()` in the handler and leave `Inspection.AddPhoto`'s signature unchanged (rejected — couples the caller to collection-ordering behavior that has no reason to be guaranteed). (b) Change `Inspection.AddPhoto` to return the created `InspectionPhoto`, matching `AngebotSection.AddItem`'s established shape.

**Final decision:** (b). Confirmed explicitly with the user first that this qualified as a genuine bug/inconsistency-fix (permitted under the standing "don't change existing Domain behavior without a genuine bug or documented new rule" instruction) rather than a reopening of Phase 1's design, before touching already-merged code.

**Why chosen:** Consistency of pattern across structurally-identical operations reduces surprise for any future reader who has already internalized `AngebotSection.AddItem`'s shape; `Photos.Last()` is exactly the kind of fragile, implicit-ordering dependency this project has consistently avoided elsewhere (see D14's identical concern about not relying on incidental object-collection behavior).

**Consequences:** `Inspection.AddPhoto` now returns `InspectionPhoto`. Existing Phase 1 Domain tests for `AddPhoto` continued to pass unchanged (they never asserted on the return value, only on `inspection.Photos`); one new test (`AddPhoto_ReturnsTheCreatedPhoto`) was added asserting the returned object is reference-equal to `inspection.Photos[0]`. No other Domain behavior changed. This is now the established, permanent convention (`CLAUDE.md` §2): any aggregate method that creates a child returns that child.

---

## D34 — `INumberGeneratorService`: Minimal Interface, Transactional Guarantee Deferred to Infrastructure (Unverified Risk, Flagged Explicitly)

**Problem:** Architecture.md §8 describes Angebot numbering (`ANG-{YYYY}-{sequence:D5}`) as needing an atomic increment "inside the same DB transaction as the Angebot creation" to prevent duplicate numbers under concurrent requests — a real correctness requirement for a legally-significant, must-be-unique document number. `CreateAngebotCommand` (Phase 2, Slice 6) was the first command needing to generate one.

**Alternatives considered:** (a) Have the Application-layer interface's method signature somehow encode the transactional requirement (e.g. taking a transaction/unit-of-work parameter explicitly). (b) Keep the Application-layer interface deliberately simple (`Task<string> NextAngebotNumberAsync(int year, CancellationToken ct)`, no transaction parameter, no numeric sequence exposed) and treat the atomicity guarantee as a *requirement on whatever implements the interface*, not something the interface's shape needs to express.

**Final decision:** (b).

**Why chosen:** Consistent with this project's general preference (see D22, D23) for interfaces that express *intent* ("give me the next Angebot number for this year") rather than mechanism (how the underlying counter is stored, locked, or incremented) — Application should not need to know or care that Infrastructure achieves atomicity via a database transaction, a `SELECT ... FOR UPDATE`, or any other specific technique. The interface stays swappable and simple; the hard requirement is documented in prose (Architecture §8, `CLAUDE.md` §18) rather than encoded in C# types that would leak Infrastructure concerns into Application.

**Consequences — explicitly flagged as the single highest-risk unverified assumption in the whole project as of this handoff:** No implementation of `INumberGeneratorService` exists yet (Phase 3 is not started). The interface's simplicity means it is entirely possible to write a naive, non-atomic implementation that compiles, passes any test that doesn't specifically exercise concurrency, and silently produces duplicate Angebot numbers under real concurrent load. `CLAUDE.md` §18 and `NEXT_STEPS.md` §3 both explicitly call for a genuine concurrency test (not just a code review) once Phase 3 builds the real implementation — this is not optional diligence, it is the direct consequence of choosing interface simplicity over encoding the guarantee in types.

---

## D35 — `ConflictException`: The Third and (So Far) Final Application Exception Type

**Problem:** `CreateAngebotCommand` (Phase 2, Slice 6) needed to reject a request when a Lead already has a non-terminal Angebot (StateMachine §2.4). Neither existing exception type fit: the Lead exists (`NotFoundException` doesn't apply), and this isn't a question of *who* is allowed to act (`ForbiddenException` doesn't apply either) — it's a conflict between the request and the aggregate's own current business state, discovered via a repository query (`HasActiveAngebotForLeadAsync`), not a Domain guard.

**Alternatives considered:** (a) Reuse `InvalidOperationException` directly (the same type Domain guards throw), relying on handlers to distinguish "this came from a repository-backed Application check" vs. "this came from a Domain aggregate guard" some other way later. (b) Introduce a third, distinct, purpose-named Application exception type.

**Final decision:** (b) — `ConflictException(string message)`, following the exact same shape and reasoning as `NotFoundException` (D27) and `ForbiddenException` (D28).

**Why chosen:** A distinctly-named, distinctly-typed exception gives the not-yet-built Phase 4 API middleware an unambiguous signal to map to HTTP 409 — separate from both Domain-thrown guard failures (which propagate unwrapped as `ArgumentException`/`InvalidOperationException`, expected to map differently, likely 400/409 depending on further Phase 4 design) and from `NotFoundException`/`ForbiddenException`'s own distinct mappings (404/403). Keeping three narrow, purpose-built types (rather than one generic "AppException" with a status-code property, or reusing Domain's own exception types for an Application-layer concern) keeps each type's meaning unambiguous at both the throw site and the catch site.

**Consequences:** Three Application exception types exist as of this writing, each introduced exactly when a real scenario first needed it (D27 → `NotFoundException`, first needed in `ScheduleInspectionCommand`; the `ForbiddenException` need first arose in `CompleteInspectionCommand`, formalized alongside D28's `IOwnershipValidator` extraction; D35 → `ConflictException`, first needed in `CreateAngebotCommand`). No fourth type has been needed since. See `CLAUDE.md` §17 for the full current table and expected HTTP mappings.

---

## D36 — `IQueryHandler<TQuery, TResult>`: A Deliberate Second Dispatch Abstraction, Not Reuse of `ICommandHandler`

**Problem:** Design review for the CatalogItem Application layer reached `SearchCatalogItemsQuery` — the first query in the entire codebase. It needs a handler shape, and `ICommandHandler<TCommand, TResult>` already has the identical method signature (`Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken)`).

**Alternatives considered:** (a) Reuse `ICommandHandler<TQuery, TResult>` for queries too, since introducing a second interface with an identical signature could be read as speculative duplication. (b) Introduce a distinct `IQueryHandler<TQuery, TResult>`, even though its shape is currently identical to `ICommandHandler`.

**Final decision:** (b), by explicit user instruction, overriding the initial recommendation of (a).

**Why chosen:** `CLAUDE.md` §3's CQRS-lite split is a real, not just nominal, distinction — commands mutate aggregates via repositories; queries return DTOs directly, bypassing full aggregate hydration entirely. The user's reasoning: commands and queries are different architectural concepts even when today's method signature happens to coincide, and the dispatch abstraction should name that difference explicitly rather than let one interface quietly represent two mutually exclusive things. An identical signature today is not evidence the two concepts are the same — it just means neither side has yet needed a concern (e.g. a caching hint on queries, an idempotency key on commands) that the other doesn't.

**Consequences:** `RenoTrack.Application.Common` gains a second interface, `IQueryHandler<TQuery, TResult>`, structurally identical to `ICommandHandler<TCommand, TResult>` but named and reasoned about separately. Its first consumer will be `SearchCatalogItemsQuery`, not yet implemented as of this writing.

---

## D37 — `SearchCatalogItemsQuery` Will Start With No `includeRetired` Parameter

**Problem:** BR-12 requires retired `CatalogItem`s to be excluded from the Catalog picker (Wireframes.md D2). During design review, the question was raised whether the query's first version should also accept an `includeRetired` flag, anticipating a possible future Admin "manage all items, including retired" screen.

**Alternatives considered:** (a) Add the flag now, defaulting to excluding retired items, on the reasoning that a management screen showing retired items is a foreseeable future need. (b) Return only non-retired items unconditionally, with no parameter at all, and add one later only once a real, documented caller needs it.

**Final decision:** (b), by explicit user instruction.

**Why chosen:** No documented use case anywhere (`Wireframes.md`, `SRS.md`, `PermissionMatrix.md`) currently shows retired items being surfaced anywhere. Adding the parameter now would be exactly the kind of "we'll need this eventually" speculative growth `CLAUDE.md` §4 and `NEXT_STEPS.md` §4 explicitly reject for repositories, DTOs, and (by the same reasoning) query interfaces.

**Consequences:** `SearchCatalogItemsQuery` (not yet implemented as of this writing) will take no filter parameter when it is built, and its query implementation will always exclude `IsRetired` items. Revisit only when a real, documented use case needs to see retired items.

---

## Decisions Explicitly Rejected (Collected for Quick Reference)

| Rejected approach | Where | Why rejected |
|---|---|---|
| Enum + nullable string as two independent fields for `Unit` | D10 | Allows contradictory states (e.g. `Custom` kind with no label) |
| `Money.Of(decimal)` single factory silently applying rounding | D11 | Conflates a permanent invariant with a specific, named, potentially-future-pluggable policy |
| `Money` multiplication operator | D11 | Would hide BR-11's rounding application behind an innocuous-looking operator |
| `Angebot.NetTotal`/`GrossTotal` as pure computed properties | D15 | ERD's caching rationale genuinely applies at this level; reverted after user pushback |
| `Angebot.DecisionResult` as a computed property (rather than removed) | D16 | Presentation-mapping concern, not a Domain concept at all — even deriving it is out of scope for Domain |
| `AngebotSection.AddItem` public, matching Sequence Diagram's literal pseudocode | D13 | Reopens the "caller must remember an extra step" footgun |
| `Angebot.AddItemToSection(int sectionId, ...)` | D14 | Ambiguous before real ids exist — a genuine bug, not a style issue |
| `Inspection.IsEditable` read-only property | D29 | Exposes Domain state solely to optimize an Application workflow; a second way to ask a question BR-10 already answers |
| Generic `OwnershipGuard.EnsureOwnedBy(int, int, string, int)` helper | D28 | Loses business intent at the call site; user explicitly preferred named methods over maximal reuse |
| `IOwnershipValidator` used for `ApproveAngebotCommand`/`RequestAngebotChangesCommand` | D31 | These are Admin-`F` (role-based only) actions with no ownership concept — using it would blur the abstraction's meaning |
| Implementing `AddAngebotItemCommand`'s custom-item path only, deferring Catalog-sourced path | D30 | Would produce an incomplete slice requiring immediate rework once Catalog exists |
| Two separate `CatalogItem` factories (`CreateByAdmin`/`CreateFromAngebotItem`) | D20 | Same Domain shape either way; the difference is an authorization concern, not a Domain one |
| MediatR | D22 | Adds indirection that hides orchestration steps this educational project needs to keep visible |
| AutoMapper | `CLAUDE.md` §8 | Hidden mapping logic not worth the boilerplate savings at this project's scale |
| Reusing `ICommandHandler<TQuery, TResult>` for `SearchCatalogItemsQuery` | D36 | Commands and queries are different concepts even with an identical signature today; user preferred a distinct `IQueryHandler` |
| `SearchCatalogItemsQuery` with an `includeRetired` flag from the start | D37 | No documented use case surfaces retired items anywhere yet; add only when one does |
