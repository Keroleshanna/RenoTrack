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

## D38 — BR-14: A Retired `CatalogItem` Remains a Valid Direct Reference

**Problem:** During `AddAngebotItemCommand`'s design review, `NEXT_STEPS.md` §2 had flagged as an open question whether the command should reject an attempt to add an item sourced from a retired `CatalogItem`. Before answering either way, the full documentation set (`BusinessRules.md`, `PermissionMatrix.md`, `ERD.md`, `SRS.md`, `Wireframes.md`) was searched explicitly for "retired"/"retire" — at the user's specific request not to infer an answer without first confirming the documentation was genuinely silent.

**Findings:** Every existing mention ties retirement's effect specifically to **discovery** — `BR-12` says a retired item "is excluded from the Catalog picker... but the row itself is kept"; `PermissionMatrix.md` §6 says it "stops appearing in the Catalog picker (D2) but is kept so any AngebotItem previously created from it... keeps a valid `CatalogItemId` trace link." Nothing anywhere states that a retired item becomes an invalid *target* for a **new** reference — the documentation set was confirmed genuinely silent on this specific question, not merely unread.

**Alternatives considered:** (a) Reject `AddAngebotItemCommand` when the referenced `CatalogItem.IsRetired == true`, inferring this from BR-12's general "retired items are excluded from use" spirit. (b) Allow it — retirement only ever affects discovery (`SearchCatalogItemsQuery`), never direct reference by id.

**Final decision:** (b), formalized as a new rule, **BR-14** (`BusinessRules.md`), rather than left as an implicit implementation choice.

**Why chosen:** BR-8's copy-on-create semantics already make the resulting `AngebotItem` functionally independent of a `CatalogItem`'s current state (including `IsRetired`) the instant it's created — `CatalogItemTests.UpdatingACatalogItem_DoesNotAffectAnAngebotItemAlreadyCreatedFromIt_BR8` already proves this at the Domain level for edits, and retirement is no different in kind. Rejecting on a retired source would be inventing a new restriction with no documented basis, purely by extrapolating BR-12's picker-exclusion rule further than it actually states.

**Consequences:** `AddAngebotItemCommandHandler`'s call to `ICatalogItemRepository.GetByIdAsync` is deliberately **not** filtered by `IsRetired` — unlike `ICatalogItemQueries.SearchAsync`, which does filter (BR-12). `PermissionMatrix.md` §6's "Delete/retire" row was updated to cross-reference BR-14 alongside its existing BR-8/BR-12 references, per `CLAUDE.md` §15's "update all affected documents" rule.

---

## D39 — `SaveAngebotItemAsCatalogItemCommand` Deferred Out of Phase 2 (Scope Correction)

**Problem:** `NEXT_STEPS.md` had characterized `SaveAngebotItemAsCatalogItemCommand` as "the one remaining piece of Phase 2," based on following the SRS/Sequence-Diagram narrative (FR-4.10 sits in the same note block as `AddAngebotItemCommand`). Before implementing it, the user asked whether it actually belonged in Phase 2's scope, rather than assuming so because it appears in the SRS.

**Investigation:** Re-checked `PROJECT_ROADMAP.md` directly rather than trusting the prior framing. Phase 2's own title scopes it explicitly to "Lead/Inspection/Angebot" only; its explicit nine-command list (`CreateLeadCommand` through `ApproveAngebotCommand`) does not include `SaveAngebotItemAsCatalogItemCommand`, nor any CatalogItem command at all. (`CatalogItem`'s CRUD/Search feature, Slices 11–14, was a separate, deliberate insertion into Phase 2's branch because `AddAngebotItemCommand` — which *is* on Phase 2's list — needed it; that justification does not extend to this command, which nothing in Phase 2's actual scope depends on.) Separately, Phase 1b's own title already names "save as catalog item" as part of its concept, and Phase 1b already delivered the Domain-level requirement (`CatalogItem.Create(..., createdFromAngebotItemId)`).

**A second, independent finding reinforced the deferral, not just the scope check:** implementing this command per its documented route (`POST /api/v1/angebot-items/{itemId}/save-as-catalog-item` — item id only, no `AngebotId`) would require a new Application-layer lookup capability — resolving an `AngebotItem`'s owning `Angebot`/`Section` from the item's id alone — that no other command needs and that doesn't exist today. `AngebotItem` has no back-reference by design (Phase 3 EF shadow property only); `IAngebotRepository` has only `GetByIdAsync(angebotId)`; there is no `IAngebotItemRepository`, nor should there be one (`AngebotItem` is a grandchild, not an aggregate root). Building new repository surface to serve exactly one command is the kind of premature, single-purpose architecture this project has consistently avoided (`CLAUDE.md` §4's repository-growth-on-demand discipline; see also D30's earlier rejection of a similarly premature partial implementation).

**Alternatives considered:** (a) Implement it now as Phase 2's closing slice, since it was already documented as expected in `NEXT_STEPS.md`. (b) Defer it — correct `NEXT_STEPS.md`/`PROJECT_STATE.md` to reflect that it was never actually in Phase 2's roadmap-defined scope, and revisit its repository/lookup design only when a phase that actually needs it arrives.

**Final decision:** (b).

**Why chosen:** Two independent reasons converge on the same answer: it isn't roadmap-scoped work, and implementing it today would force a repository capability whose only justification would be this one command — exactly the "add it because we might need it" pressure this project has structurally avoided everywhere else (repository growth, DTO growth, `IOwnershipValidator`, query parameters).

**Consequences:** `SaveAngebotItemAsCatalogItemCommand` remains unimplemented as Phase 2 closes. Deferred, not abandoned — flagged explicitly in `PROJECT_STATE.md`/`NEXT_STEPS.md` as a future item, to be designed (including its lookup mechanism) only once a phase that depends on it actually arrives — most naturally once Phase 3's EF Core ids exist, which would trivially resolve the lookup problem this analysis surfaced.

---

## D40 — `RenoTrack.Infrastructure.Tests`: A New, Deliberate Test Project (Phase 3)

**Problem:** `Architecture.md` §3's solution structure named exactly three test projects (`Domain.Tests`, `Application.Tests`, `Api.Tests`). None can exercise real EF Core/repository behavior: `Domain.Tests`/`Application.Tests` are deliberately isolated from any database (in-memory fakes only), and `Api.Tests` only references `RenoTrack.Api` — Phase 4 hasn't built any endpoints yet, so there's nothing to test through.

**Alternatives considered:** (a) temporarily add a direct `RenoTrack.Infrastructure` reference to `RenoTrack.Api.Tests` ahead of Phase 4. (b) introduce a new `RenoTrack.Infrastructure.Tests` project, referencing `RenoTrack.Infrastructure` and `RenoTrack.Domain` only, mirroring `Domain.Tests`'/`Application.Tests`' own "reference only what's needed" discipline.

**Final decision:** (b), with the user's explicit sign-off (this is a real deviation from `Architecture.md`'s documented structure, not something to add silently).

**Why chosen:** (a) would make `Api.Tests` depend on Infrastructure before `Api` itself does — backwards from the intended dependency direction, and confusing for anyone reading the project reference graph. A dedicated project keeps the graph honest and gives Infrastructure integration tests a home that doesn't presuppose Phase 4 exists yet.

**Consequences:** `Architecture.md` §3 updated to list the new project and explain why it's an addition, not part of the original Phase 0 structure. Tests use real SQL Server LocalDB, never the EF Core InMemory provider (D-established in the Phase 3 design review — InMemory doesn't enforce the constraints/types this layer exists to verify). All tests in the project share one `ICollectionFixture`-backed LocalDB database and run serially against it (xUnit collections never parallelize against each other), avoiding interference between tests that share real, persistent state.

---

## D41 — `ERD.md` Corrected to Match Confirmed Domain State (`Subtotal`/`LineTotal`/`DecisionResult`)

**Problem:** While designing Phase 3's entity configurations, `ERD.md` was found to still list `AngebotSection.Subtotal`, `AngebotItem.LineTotal` as physical columns ("Subtotal is cached, recalculated whenever a child item changes"), and `Angebot.DecisionResult` as a nullable column — none of which exist in the actual Domain code. `AngebotSection.Subtotal`/`AngebotItem.LineTotal` are pure `=>` computed properties with no backing field (`CLAUDE.md` §2 already states this explicitly and deliberately); `Angebot.DecisionResult` was removed from the Domain entirely back in Phase 1 (D16 — "a presentation-mapping concern, not a Domain concept, out of scope for Domain").

**Investigation:** Confirmed directly against the live C# source (not just documentation) that all three properties either don't exist (`DecisionResult`) or have no backing field to persist (`Subtotal`/`LineTotal`). `ERD.md` predates these Phase 1 decisions and was never updated to match — a real, load-bearing documentation gap, since Phase 3's entity configurations literally cannot proceed without deciding whether these get columns.

**Alternatives considered:** (a) Add columns for all three anyway, matching `ERD.md` literally, treating the Domain as the thing that needs to catch up. (b) Trust the Domain code and `CLAUDE.md` (both more recent and more authoritative per `PROJECT_STATE.md`'s own stated precedence rule) and correct `ERD.md` to match.

**Final decision:** (b), confirmed explicitly by the user rather than assumed unilaterally.

**Why chosen:** `CLAUDE.md` §2 doesn't just fail to mention these fields being stored — it explicitly and reasonedly states they're computed-only ("No ERD-stated performance reason applies at that granularity"), and D16 explicitly removed `DecisionResult` as a deliberate decision, not an oversight. Reintroducing columns for either would resurrect settled Phase 1 decisions without new evidence, which `NEXT_STEPS.md`/`CLAUDE.md` both prohibit.

**Consequences:** `ERD.md`'s diagram and Physical Schema Notes updated in the same commit as the entity configurations that implement this (`CLAUDE.md` §15's documentation-first discipline). `AngebotSectionConfiguration`/`AngebotItemConfiguration`/`AngebotConfiguration` explicitly `.Ignore()` these three properties rather than silently omitting a mapping for them — makes the exclusion visible in code, not just absent.

---

## D42 — `LocalDiskFileStorage` Reassigned to Phase 4 (`CLAUDE.md` Corrected)

**Problem:** `CLAUDE.md` §13 stated `LocalDiskFileStorage` is implemented "in `RenoTrack.Infrastructure` (Phase 3)." `PROJECT_ROADMAP.md`'s Phase 4 deliverable list explicitly includes it; Phase 3's own deliverable list does not mention it at all. A direct contradiction between two documents, surfaced during Phase 3's design review.

**Investigation:** `PROJECT_ROADMAP.md` has been the authoritative, execution-driving document for every phase so far — branch names, PR titles, and (immediately prior to this) the entire basis for confirming `SaveAngebotItemAsCatalogItemCommand` was out of Phase 2's scope (D39). `CLAUDE.md` §13's "(Phase 3)" reads like a forward-reference written speculatively during Phase 2, never re-verified against the roadmap once Phase 3 was actually scoped.

**Alternatives considered:** (a) Build `LocalDiskFileStorage` for real in Phase 3, per `CLAUDE.md`'s stated (but unverified) claim. (b) Defer it to Phase 4 per `PROJECT_ROADMAP.md`, registering only a minimal placeholder in Phase 3 so DI composition succeeds for `UploadInspectionPhotoCommand`.

**Final decision:** (b), confirmed explicitly by the user.

**Why chosen:** Same reasoning as D39 — `PROJECT_ROADMAP.md` is the authoritative scope document; a stale forward-reference in `CLAUDE.md` doesn't override it. Building the real disk implementation now would also be premature relative to Phase 4's own stated ownership of it.

**Consequences:** `CLAUDE.md` §13 corrected to say "(Phase 4)" with a note explaining the correction. Phase 3 (Slice 12, per the dependency map) registers a placeholder `IFileStorage` implementation only.

---

## D43 — Encapsulated Child Collections Mapped via Backing-Field EF Core Navigation

**Problem:** `Inspection.Photos`, `Angebot.Sections`, and `AngebotSection.Items` are all exposed as `IReadOnlyList<T>` over a `private readonly List<T>` field, with no public setter — by design, so nothing outside the aggregate root can mutate a child collection directly (`CLAUDE.md` §2). EF Core's default navigation convention typically expects a settable `ICollection<T>`-shaped property.

**Investigation:** EF Core does support binding a navigation directly to a private backing field (`PropertyAccessMode.Field`), and has since EF Core 3+, but this needed to be proven with an actual round-trip test against real LocalDB, not assumed to work by convention — exactly the risk flagged in the Phase 3 design review.

**Final decision:** Explicitly configure `builder.Navigation(x => x.Photos).UsePropertyAccessMode(PropertyAccessMode.Field)` (and the equivalent for `Sections`/`Items`) in each entity configuration, rather than relying on EF's implicit field-discovery convention.

**Why chosen:** Being explicit here costs nothing and removes any ambiguity about why the mapping works, versus leaving a future reader to wonder whether it's convention-based magic. `RenoTrack.Infrastructure.Tests`' `InspectionPersistenceTests`/`AngebotPersistenceTests` prove this actually round-trips through a real database (add via the aggregate root → save → reload via a fresh `DbContext` → assert the collection is populated), not just that the code compiles.

**Consequences:** All three encapsulated collections materialize correctly on reload, confirmed by integration tests. This is the concrete resolution of the risk flagged in the Phase 3 design review — verified, not assumed.

---

## D44 — User-Referencing Foreign Keys Deferred Until the Identity Slice

**Problem:** `Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, and `AngebotReviewComment.AdminUserId` all conceptually reference a `User`, and `ERD.md` lists each as a "Notable Foreign Key." But Identity (which owns the `Users`/`AspNetUsers` table) is Slice 15 in the revised Phase 3 order — deliberately sequenced after every repository slice, per the user's explicit request to keep repository work independent of Identity.

**Final decision:** These columns are mapped as plain `int`/`int?` properties with no FK constraint in Slice 1 (and will remain so through Slice 14). The Identity slice can add the constraints retroactively via a follow-up migration once the `Users` table exists — this doesn't require revisiting any configuration written in Slice 1.

**Why chosen:** Directly follows from the user's ordering decision — a repository slice can't reference a table that doesn't exist yet, and there's no reason to block Slices 1–14 on Identity landing first just to satisfy a documentation line's phrasing.

**Consequences:** `ERD.md`'s physical notes for `Leads`, `Inspections`, `Angebote`, and `AngebotReviewComments` each note explicitly which FK is deferred and why, so this isn't a silent gap. Meanwhile, `AngebotItem.CatalogItemId` and `CatalogItem.CreatedFromAngebotItemId` **do** get real FK constraints in Slice 1, since both `CatalogItems` and `AngebotItems` tables already exist today — the deferral is specific to the Identity dependency, not a general policy against FKs.

---

## D45 — Three Missing FKs Found During Slice 2's Pre-Migration Schema Review

**Problem:** Before generating `InitialCreate`, a deliberate three-way comparison (Domain code ↔ EF configurations ↔ `ERD.md`) was performed, per the user's explicit request, rather than generating the migration straight from Slice 1's configurations. It found `Inspection.LeadId`, `Angebot.LeadId`, and `Angebot.InspectionId` had no FK constraint configured at all — a real gap, not a deliberate deferral. All three reference tables (`Leads`, `Inspections`) that already exist today, so none of them fall under the "Users table doesn't exist yet" reasoning correctly applied to the Identity-referencing columns (D44). This was a straightforward oversight in Slice 1: only the cross-aggregate `CatalogItem`↔`AngebotItem` FKs were added; these three same-table-exists-today relationships were missed.

**Final decision:** Add all three as real FKs, `DeleteBehavior.Restrict` (consistent with every other cross-aggregate FK in the model — `Lead`/`Inspection` are never deleted per the project's "never truly delete a historical record" philosophy, so cascade behavior would never trigger anyway, but `Restrict` is the correct, safe default regardless).

**Why this matters as a process point, not just a bug fix:** this is the concrete payoff of doing a schema review *before* generating a migration rather than after — the gap was caught by comparing three sources deliberately, not by re-reading the same code that already had the bug in it.

**Consequences:** `InspectionConfiguration` and `AngebotConfiguration` updated. `RenoTrack.Infrastructure.Tests` gained explicit FK-rejection tests for all three (`LeadIdForeignKey_RejectsANonExistentLead` in both `InspectionPersistenceTests` and `AngebotPersistenceTests`, `InspectionIdForeignKey_RejectsANonExistentInspection` in `AngebotPersistenceTests`) — and three existing tests that had been using a hardcoded, never-actually-inserted `leadId: 1` were fixed to seed a real `Lead` row first, since they had only been passing by accident (test-class execution order happened to let a different test class create a real `Lead` with `Id == 1` first).

---

## D46 — Owned-Child Shadow FK Columns Were Nullable by Default (Found in the Generated Migration)

**Problem:** Manual review of the first generated `InitialCreate` migration (before applying it anywhere, per the user's explicit "review the migration manually" instruction) found `InspectionPhotos.InspectionId`, `AngebotSections.AngebotId`, and `AngebotItems.SectionId` — the three shadow FK columns for encapsulated child collections — were all generated as `nullable: true`. This is wrong: by Domain design, a photo/section/item always belongs to exactly one parent; the relationship is a required composition, not optional.

**Investigation:** `HasMany(x => x.Children).WithOne()` (no back-navigation, since the child has no reference back to its parent — matching the Domain's own "no FK back-reference" design) defaults EF Core's convention to an *optional* relationship, because there's no non-nullable navigation property anywhere telling EF the relationship is required. `.IsRequired()` must be called explicitly on the relationship builder to get a `NOT NULL` shadow FK column.

**Final decision:** Add `.IsRequired()` to all three `HasMany(...).WithOne()` calls (`InspectionConfiguration.Photos`, `AngebotConfiguration.Sections`, `AngebotSectionConfiguration.Items`), then regenerate `InitialCreate` from scratch (the first, incorrect migration was removed via `dotnet ef migrations remove` before ever being applied to a database — nothing to undo).

**Why chosen:** This is a genuine correctness bug, not a style preference — a nullable FK here would have let a database-level `InspectionPhoto`/`AngebotSection`/`AngebotItem` row exist with no parent at all, silently contradicting the Domain's own invariant that these are always created through their aggregate root.

**Consequences:** All 15 (now 17, with the two new migration tests) `RenoTrack.Infrastructure.Tests` still pass with `.IsRequired()` added — confirming the fix didn't change any tested behavior, only tightened the schema to match what was always true in practice. This is exactly the kind of issue the user's insistence on manually reviewing the generated migration (not just trusting the configuration code) was designed to catch.

---

## D47 — `IDesignTimeDbContextFactory` Added for Migration Tooling, DI Composition Still Deferred

**Problem:** `dotnet ef migrations add` needs to construct `RenoTrackDbContext` at design time, but the real DI composition (`AddInfrastructure()` + `Program.cs` wiring) is deliberately Slice 14 — much later in the approved dependency map. Generating a migration in Slice 2 needed some way to build the context without jumping ahead to Slice 14's work.

**Final decision:** Add `RenoTrackDbContextFactory : IDesignTimeDbContextFactory<RenoTrackDbContext>` in `RenoTrack.Infrastructure` — the standard EF Core pattern for exactly this situation. It hardcodes a LocalDB connection string used only by `dotnet ef` tooling (never by the running application), consistent with `RenoTrack.Infrastructure.Tests`' own fixture. `Microsoft.EntityFrameworkCore.Design` added to both `RenoTrack.Infrastructure` (`PrivateAssets="all"` — dev-time only, never a runtime dependency for consumers) and `RenoTrack.Api` (as the migrations' eventual startup project once Slice 14 lands).

**Why chosen:** Keeps Slice 2 self-contained — generating a migration doesn't require jumping ahead to DI wiring that hasn't been designed yet, and doesn't leave a half-built Slice 14 in place just to unblock tooling.

**Consequences:** `dotnet ef migrations add`/`remove` work today via this factory. Slice 14 will still do the real DI registration for the running application; this factory is never consulted at runtime, only by `dotnet ef` itself.

---

## D48 — `UnitOfWork` Confirmed as an Intentionally Thin Abstraction

**Problem:** Before implementing `IUnitOfWork`'s Infrastructure side (Phase 3, Slice 3), the user asked for a short design review covering whether it should contain any logic beyond `SaveChangesAsync()`, transaction ownership, `DbContext`/repository lifetime, disposal responsibility, cancellation propagation, and whether the interface should stay minimal — explicitly asking for the "why," not just the conclusion, if the answer was "keep it thin."

**Findings, each confirmed rather than assumed:**
- **No logic beyond `SaveChangesAsync()`.** Every Phase 2 handler calls it exactly once, after all repository/Domain work; EF Core's own `SaveChangesAsync()` already wraps everything tracked by that call in an implicit transaction, so no explicit `BeginTransaction`/`Commit`/`Rollback` is needed for anything built so far.
- **Transaction ownership** stays with EF Core's implicit per-`SaveChanges` transaction. The one case that looks like it needs more — `INumberGeneratorService`'s atomic requirement (Slice 11) — is deliberately *not* solved through `IUnitOfWork`'s public contract; it's the number-generator service's own internal concern (its own explicit `BeginTransactionAsync()` wrapping a raw SQL increment and the entity's save).
- **`DbContext` and every repository share one Scoped lifetime** — this is what makes "repository adds an entity, `UnitOfWork.SaveChangesAsync()` commits it" work at all; a different lifetime pairing would silently break this.
- **`UnitOfWork` does not implement `IDisposable`** — it doesn't own the `DbContext` (constructor-injected), so disposal belongs to the DI container's scope, not to this class.
- **Cancellation token passes straight through**, no additional handling.

**Final decision:** `UnitOfWork : IUnitOfWork` is a one-line wrapper: `SaveChangesAsync(ct) => dbContext.SaveChangesAsync(ct)`. Nothing else.

**Why chosen:** The same repository/interface growth-on-demand discipline already applied everywhere else in this project (`CLAUDE.md` §4) — grow the interface only when a real, currently-being-built command needs more, never speculatively. Exposing `DbContext` or a generic `ExecuteInTransactionAsync` wrapper through the interface would leak an Infrastructure mechanism into the Application layer's contract for no current benefit.

**Consequences:** `UnitOfWorkTests` proves three things directly: `SaveChangesAsync` persists pending changes tracked by the same `DbContext`, calling it with nothing pending doesn't throw, and an already-cancelled token *does* throw — but only when there's a real pending change to save, since EF Core short-circuits `SaveChangesAsync()` entirely (skipping the cancellation check) when nothing is tracked. This was found empirically (a first draft of the cancellation test had no pending change and failed, since EF returned immediately without ever consulting the token) — a small, concrete confirmation that EF's own behavior, not just this wrapper's code, needed to be verified rather than assumed.

### Amendment (Phase 7, Slice 3) — `IUnitOfWork` gains an explicit transaction boundary

D48's **rule** is unchanged and is in fact what triggered this: *"grow the interface only when a real, currently-being-built command needs more, never speculatively."* `ConvertAngebotToProjectCommand` is the first command that needs more, so the trigger fired. Only D48's **finding** — that EF Core's implicit per-`SaveChanges` transaction covers every handler's needs — has expired.

**Why the finding expired.** Conversion is the first case where a brand-new aggregate (`Project`) requires the database-generated identity of another brand-new aggregate (`Customer`) before it can itself be validly constructed. Because the aggregates deliberately have no navigation-property relationship, EF cannot use relationship fix-up to defer that foreign-key assignment until persistence. Verified rather than inferred: a probe against real LocalDB confirmed `Customer.Id` is `0` after `Add()` and before `SaveChanges`.

**Rejected, each for a stated reason:**
- **A `Project.Customer` navigation property** — breaks CLAUDE.md §2's by-id-only aggregate separation and drags Customer's object graph into every Project load.
- **Weakening `Project.Create`'s `customerId > 0` guard** — would silently write `CustomerId = 0` and fail later at the foreign key as an unmapped 500. That guard is precisely what made this defect immediate and obvious instead of latent.
- **Two un-transacted `SaveChanges`** — orphans a Customer, which the unique index on `Customers.LeadId` then makes un-retryable without manual cleanup.
- **Compensating deletion of the Customer** — compensation is not atomicity (§22's own wording), and it leaves the crash-between-steps hole open.
- **Client-generated keys for `Customer.Id`** — changes the PK strategy for one table out of nine against `ERD.md`'s `int Id PK` convention, and reopens an already-committed migration.
- **`ExecuteInTransactionAsync<T>(Func<Task<T>>, …)`** — hides the boundary inside a lambda. The transaction boundary is part of the Application use case and should stay legible in the handler.

**Shape.** `IUnitOfWork.BeginTransactionAsync` returns an `IUnitOfWorkTransaction : IAsyncDisposable` carrying a single `CommitAsync`. **No `RollbackAsync`:** disposing an uncommitted transaction rolls it back, so `await using` covers every escape path — exception, early return, cancellation — and an explicit method would be a redundant second way to do one thing. `IAsyncDisposable` is BCL, so EF Core's `IDbContextTransaction` never reaches the Application layer's contract. `UnitOfWork` still does not implement `IDisposable`: it still does not own the injected `DbContext`, and the caller owns only the transaction it opened.

**Scope.** The transaction is opened **only** on the create-new-Customer path. Reusing an existing Customer needs one `SaveChangesAsync` and opens none — symmetry is not a reason to take a lock.

**Two standing constraints this creates.**
- A `DbContext` must never be reused after a rolled-back transaction: the change tracker still holds entities as persisted with ids the database no longer has (the D55 family of hazard). In practice the failure propagates and the request scope is disposed.
- **`EnableRetryOnFailure` must not be added to `UseSqlServer` without revisiting every caller of `BeginTransactionAsync`** — a retrying execution strategy forbids user-initiated transactions and would break this handler at runtime. Not configured today; checked, not assumed.

**A weak test, found by adversarial verification rather than inspection.** The first version of `ConversionTransactionTests.AFailedSecondWriteRollsBackTheCustomerInsert` wrapped its `DbContext` in `await using` and verified through a fresh context. It passed even when `IUnitOfWorkTransaction.DisposeAsync` was gutted to a no-op — because disposing the context tears down the connection, which rolls back any open transaction as a side effect. It proved the business outcome while proving nothing about the mechanism. The test now disposes the transaction explicitly while its context is still alive and re-reads through that same context, which fails as it should when disposal is gutted. **A rollback test that lets its own context disposal do the work is not a rollback test.**

**Correction to the original entry above:** D48's findings state that `INumberGeneratorService` uses "its own explicit `BeginTransactionAsync()` wrapping a raw SQL increment and the entity's save". That has not been true since **D52** replaced the mechanism with a single atomic `UPDATE … OUTPUT` statement — `grep -rn "BeginTransaction" src/` returned nothing at all prior to this amendment.

---

## D49 — `AuditLog` Is an Infrastructure Persistence Model, Not a Domain Entity

**Problem:** Every persisted type built so far (`Lead`, `Inspection`, `InspectionPhoto`, `Angebot`, `AngebotSection`, `AngebotItem`, `CatalogItem`, `AngebotReviewComment`) is a Domain entity, following the project's rich-domain-model convention: private constructor, static factory, self-guarded invariants (`CLAUDE.md` §2). `IAuditService` (Phase 2, `Application.Common.Interfaces`) needs a real persisted record in Phase 3, Slice 10 — raising the question of whether that record should be built the same way.

**Investigation:** `AuditLog` has no business invariant discussed anywhere in `BusinessRules.md`/`StateMachine.md` — no `BR-n` references it. `Architecture.md` §11 and `CLAUDE.md` §10 both describe it purely as cross-cutting instrumentation ("written by a small `IAuditService` called from handlers at key transition points"), not as a business concept with Domain behavior. Its only Application-layer contact point, `IAuditService`, is a plain logging-shaped interface (`LogAsync(entityType, entityId, action, performedByUserId, details, ct)`), not a repository over an aggregate — structurally closer to the notification models in `Application.Common.Notifications` (D23: pure data conveying a fact, no Domain behavior) than to any existing aggregate root, except that it is persisted rather than transient.

**Alternatives considered:** (a) Build `AuditLog` as a Domain entity, matching every other persisted type's convention, for structural consistency. (b) Build `AuditLog` as an Infrastructure-only persistence model with no Domain counterpart at all — the first EF-mapped type in this project without one.

**Final decision:** (b), by explicit user instruction.

**Why chosen:** `AuditLog` represents technical instrumentation, not business behavior — it protects no invariant beyond having its fields set at construction (there is no meaningful "invalid state" the way `Lead.Create` prevents an empty name), never transitions, and is never read back through any Domain rule. Applying the rich-domain-model machinery (private constructor, static factory, self-guards, `CLAUDE.md` §2) to a type with no actual invariants to protect would be ceremony without purpose, and would incorrectly imply `AuditLog` is a business concept participating in the ubiquitous language the way `Lead`/`Angebot`/`CatalogItem` do.

**Consequences:** `AuditLog` lives in `RenoTrack.Infrastructure/Persistence/Entities/AuditLog.cs` as a plain sealed class with a normal (not private) constructor — `RenoTrack.Application`/`RenoTrack.Domain` never reference this type at all, only `IAuditService`'s interface (primitives + `AuditAction` in, `Task` out). This is a precedent: if a future Infrastructure-only technical record is ever needed (a cache entry, a rate-limit counter, etc.), the same reasoning — no Domain business rule references it, purely cross-cutting — determines whether it belongs in `Domain` or stays Infrastructure-only, rather than defaulting to the rich-domain-model pattern for every persisted row.

---

## D50 — `IAuditService`: Best-Effort Audit Strategy — Business Consistency Never Depends on Audit Persistence

**Problem:** Every handler already follows `CLAUDE.md` §6's canonical shape: `IUnitOfWork.SaveChangesAsync()` (step 5, the business commit) happens *before* `auditService.LogAsync(...)` (step 6), and no handler calls `SaveChangesAsync` again afterward. This is pre-existing Phase 2 behavior, not reopened here — but it has a real, previously-unaddressed consequence: since audit logging happens strictly after the business transaction has already committed, it cannot participate in that transaction, and the Infrastructure implementation of `LogAsync` must independently persist its own write (calling `SaveChangesAsync` on the shared `DbContext` itself, since nothing else will). Every handler `await`s `LogAsync(...)` directly with no `try/catch` — so if that independent write throws (e.g. a transient DB fault), the exception propagates out of an already-successful handler, and the caller would receive an error response for a business operation that in fact already succeeded and is durably saved.

**Alternatives considered:** (a) Let audit-write exceptions propagate normally — simplest, but produces a false "failed" response for data that was actually committed, and makes the reliability of every business command hostage to the audit table's own health. (b) Adopt an explicit **Best-Effort Audit** strategy: catch any exception inside the Infrastructure `AuditService.LogAsync`, log it as a warning (`ILogger<AuditService>`), and never rethrow — audit failures are recorded for operational visibility but never surface to the caller or affect the business result.

**Final decision:** (b), by explicit user instruction, named and documented as the **Best-Effort Audit strategy**:
- Business consistency never depends on audit persistence.
- The business transaction (`IUnitOfWork.SaveChangesAsync()`) is always committed first, independently of audit logging.
- Audit logging executes afterward, as a separate, best-effort write.
- Audit failures are logged as warnings (`ILogger<AuditService>`), not thrown.
- Audit failures never invalidate an already-committed business operation — `LogAsync` never lets an exception propagate to its caller.

**Why chosen:** `CLAUDE.md` §10 already frames audit as being for "business milestones a reviewer would want to see" — valuable operational/observability data, not a correctness-critical invariant on the level of `Money`/BR-11. A transient failure writing one audit row is an acceptable, recoverable loss; reporting an already-persisted business operation as failed because of it is a strictly worse outcome, and would make every command's reliability depend on a secondary, non-essential write path. This requires no change to `IAuditService`'s existing signature (`Task`, no result) — the swallow-and-log behavior is entirely internal to the Infrastructure implementation, invisible to callers by design.

**Consequences:** `AuditService.LogAsync` wraps its own `DbContext.AuditLogs.Add(...)` + `SaveChangesAsync(...)` in a `try/catch`, logging any exception via `ILogger<AuditService>.LogWarning(...)` and returning normally either way. No handler code changes — this is purely an Infrastructure-side guarantee. A future reader should not expect `LogAsync` to ever throw, and should not add `try/catch` around it in handler code — that defensive layer already exists inside the implementation itself.

---

## D51 — `NumberSequence` Is an Infrastructure Persistence Model, Not a Domain Entity

**Problem:** `INumberGeneratorService` (Phase 2) needs a real persisted counter in Phase 3, Slice 11 — raising the same Domain-vs-Infrastructure question already resolved once for `AuditLog` (D49).

**Investigation:** `NumberSequence` has no business invariant referenced anywhere in `BusinessRules.md`/`StateMachine.md` — no `BR-n` discusses it. It is a technical counter (`SequenceType`, `Year`, `LastValue`) whose sole purpose is guaranteeing unique, formatted numbers for other aggregates; nothing about it participates in any Domain rule or ubiquitous-language concept the way `Lead`/`Angebot`/`CatalogItem` do.

**Final decision:** Same reasoning and outcome as D49, applied to a second technical/cross-cutting record: `NumberSequence` is an Infrastructure-only persistence model — no Domain entity, no rich-domain-model machinery (no private constructor/static factory/self-guards), living in `RenoTrack.Infrastructure/Persistence/Entities/`.

**Why chosen:** Confirmed as a genuine, direct precedent match to D49's own reasoning — technical instrumentation, not business behavior — rather than reflexively applying the rich-domain-model convention to every persisted row regardless of whether it protects a real invariant.

**Consequences:** `NumberSequence` joins `AuditLog` as the second EF-mapped type with no Domain-entity counterpart. `RenoTrack.Application`/`RenoTrack.Domain` never reference this type directly — only through `INumberGeneratorService`'s interface (`int year` in, `Task<string>` out).

---

## D52 — `INumberGeneratorService`: Atomic Single-Statement Increment, Decoupled From the Angebot's Own Transaction; Raw SQL Deliberately Introduced for This One Case

**Problem 1 (a real documentation/reality mismatch, found during Slice 11's review, not assumed away):** `Architecture.md` §8 and `ERD.md`:239 both state the sequence increment happens "inside the same DB transaction as the Angebot creation." Re-checking the *actual*, already-built `CreateAngebotCommandHandler` (`CreateAngebotCommandHandler.cs:43-50`) shows this is not achievable as written: `numberGenerator.NextAngebotNumberAsync(...)` is awaited and returns a plain `string` **before** the `Angebot` object is even constructed in memory (line 45), let alone added to the `DbContext` (line 49) or committed via `IUnitOfWork.SaveChangesAsync()` (line 50). There is no way for the sequence increment to share one physical database transaction with the Angebot's own `INSERT` without either (a) reopening and restructuring this already-approved Phase 2 handler (not permitted without a genuine bug — `NEXT_STEPS.md` §3), or (b) leaving the increment merely *tracked*, not committed, until the later `SaveChangesAsync` — which reintroduces the exact concurrent read-then-write race the "same transaction" language was meant to prevent in the first place (two concurrent requests could both read the same `LastValue`, both stage `+1` to the same target value, and neither would detect the other without a concurrency-token retry loop that has nowhere sane to live given `INumberGeneratorService`'s existing `Task<string>` signature, already fixed in Phase 2).

**Problem 2 (why EF Core alone cannot express the actual requirement):** The real, load-bearing requirement is **atomic increment-and-return of a single counter row**, safe under concurrent callers, in as small a locking window as possible. EF Core's change-tracking model is fundamentally a *read-then-track-then-write-on-SaveChanges* pattern — even loading a `NumberSequence` entity, incrementing a property in C#, and calling `SaveChangesAsync()` is still two separate round trips (a `SELECT` followed by an `UPDATE`), with the increment computed in application memory in between. That gap is exactly the race window: two concurrent callers can both `SELECT` the same `LastValue`, both compute the same `LastValue + 1` in memory, and whichever `SaveChangesAsync()` commits second will simply overwrite the first's row with the *same* incremented value (no built-in conflict detection, since no concurrency token is configured — and adding one would only convert the race into a `DbUpdateConcurrencyException` that still has no retry loop to run in). EF Core has no API to express "atomically increment this row and return the new value in one database round trip" as a single statement — that requires a database-level `UPDATE ... OUTPUT` (or equivalent), which is inherently provider-specific SQL, not an ORM abstraction EF Core exposes.

**Alternatives considered:** (a) Accept the read-then-write race and rely on a unique constraint on `Angebot.AngebotNumber` to reject duplicates after the fact — rejected: this would cause `CreateAngebotCommand` to fail with a confusing `DbUpdateException` under concurrent load, for a case the system should instead handle transparently by simply generating a different, still-unique number. (b) Add an EF Core concurrency token (`[Timestamp]`/`RowVersion`) to `NumberSequence` and retry the whole increment-and-return operation on `DbUpdateConcurrencyException` — rejected: still requires two round trips minimum per attempt, and under real contention could require multiple retries, all for a problem a single atomic statement solves in one round trip with no retry logic needed at all. (c) A single atomic `UPDATE ... OUTPUT INSERTED.LastValue` raw SQL statement, executed via `DbContext.Database.SqlQueryRaw<int>(...)` with no ambient/explicit EF transaction open — the entire increment-and-return happens as one SQL Server auto-commit unit, taking a row-level exclusive lock for the duration of that single statement only (sub-millisecond), then releasing it.

**Final decision:** (c). This is a **deliberate, narrowly-scoped exception** to the project's otherwise-consistent "EF Core only, no raw SQL" Infrastructure convention — introduced *only* inside `NumberGeneratorService`, for exactly this one atomic-increment-and-return requirement, and nowhere else in the Infrastructure layer. A first-of-year fallback (`INSERT ... OUTPUT INSERTED.LastValue` with `LastValue = 1`) handles the case where no row exists yet for a given `(SequenceType, Year)`; if that `INSERT` loses a race against a concurrent first-of-year request (caught via the table's own unique constraint violation), the `UPDATE` is retried exactly once — bounded, not an unbounded retry loop, and guaranteed to succeed since the racing request's row now exists.

**Why chosen:** EF Core cannot express "atomically increment and return in one statement" — that gap is a real limitation of the ORM's read/track/write model, not a design preference to route around. Reaching for a small amount of provider-specific SQL for exactly this one, precisely-bounded requirement is the correct, minimal response: it solves the actual concurrency problem in a single round trip with no retry-loop machinery needed for the common case, and is far simpler to reason about than either accepting a real duplicate-number race or bolting a concurrency-token retry loop onto an ORM operation that doesn't need one. This exception must not be read as license to reach for raw SQL elsewhere in Infrastructure — every other repository/query in this project (Slices 4–9) uses EF Core's LINQ surface exclusively, and should continue to.

**Documentation correction (`CLAUDE.md` §15's documentation-first discipline applied):** `Architecture.md` §8 and `ERD.md`'s `NumberSequences` row are both corrected in this same commit to describe the actual, provably-safe design — an independently-committed atomic statement, not literal same-transaction participation with the Angebot's own `SaveChangesAsync`.

**Gaps in Angebot numbering:** confirmed acceptable — searched `BusinessRules.md`/`SRS.md` directly rather than assuming: **BR-9** explicitly requires Invoice numbers to "never skip or reuse numbers, even if an Invoice is later voided" (a stated §14 UStG legal requirement), but no equivalent rule exists anywhere for Angebot numbers. If the sequence is incremented but the rest of `CreateAngebotCommand` later fails for an unrelated reason, that reserved number is simply never reused — a harmless, explicitly-permitted gap. This tolerance does **not** extend to a future Invoice-numbering implementation, where BR-9 remains fully binding and would need its own review.

**Consequences:** `NumberGeneratorService.NextAngebotNumberAsync` never throws for a genuine concurrency conflict (the atomic statement + bounded first-of-year retry structurally prevents one) — any exception that does propagate represents a real infrastructure failure (e.g. connection loss), and is allowed to surface unmodified, unlike `AuditService`'s best-effort swallow (D50): number generation is correctness-critical (an `Angebot` cannot exist without a valid, unique number), so failures here must be visible, not absorbed. Proven, not just asserted, by a real concurrency integration test issuing many parallel calls (each with its own `DbContext`) against the same year and asserting every returned number is distinct.

---

## D53 — `ApplicationUser`/Identity Roles Are Infrastructure-Only — Forced by D1, Not a Judgment Call

**Problem:** Phase 3 Slice 15 needs a real ASP.NET Core Identity user/role schema (Architecture.md §7.1). Every prior Infrastructure-only persistence model (`AuditLog` D49, `NumberSequence` D51) required weighing whether a Domain placement was *possible but not worthwhile* versus *genuinely excluded*.

**Investigation:** `RenoTrack.Domain.csproj` has zero `<ProjectReference>` entries (D1) — a structural, compiler-enforced rule, not a convention. `ApplicationUser` must inherit `IdentityUser<TKey>` (from `Microsoft.AspNetCore.Identity`, an ASP.NET Core-specific framework package) to work with `UserManager`/`SignInManager`/`RoleManager` and the built-in `[Authorize(Roles = "...")]` machinery Architecture.md §7.1 commits to. `Domain` cannot reference that package at all without violating D1.

**Final decision:** `ApplicationUser : IdentityUser<int>` and Identity's own tables live entirely in `RenoTrack.Infrastructure` (`Identity/ApplicationUser.cs`), using the framework's built-in `IdentityRole<int>` directly (no custom subclass — nothing today requires an extra property on Role).

**Why chosen — the key distinction from D49/D51:** those two were genuine judgment calls (the types *could* have been modeled as pure C# with no framework dependency, but weren't, because they represent technical instrumentation, not business behavior). `ApplicationUser` has no such choice — it structurally cannot compile in `Domain` given D1, independent of any judgment about whether it "deserves" rich-domain-model treatment. This is a precedent for future framework-mandated types: check whether Domain's own zero-dependency rule makes Domain placement literally impossible before applying D49/D51-style reasoning about whether it's merely undesirable.

**Consequences:** `ERD.md`'s simplified single-table `USER` sketch (a plain `Role` string column) is corrected in this same commit to describe the real schema — `AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`, plus the framework's own `AspNetUserClaims`/`AspNetUserLogins`/`AspNetUserTokens`/`AspNetRoleClaims` — matching the same "trust the more specific, more authoritative document, correct the simplified one" precedent D41 already established. Table names stay the framework defaults (not renamed to `Users`/`Roles`) — every ASP.NET Core Identity guide/tool assumes these exact names, and renaming would be friction for zero benefit.

---

## D54 — `AddIdentityCore`, Not `AddIdentity`; Role Seeding Is Best-Effort-Safe Under Concurrent Startup

**Problem 1:** `RenoTrack.Api` will authenticate via JWT bearer tokens (Architecture.md §7.1), not cookies. ASP.NET Core offers two entry points: `AddIdentity<TUser, TRole>()` (the full package, which also wires cookie-authentication-scheme defaults meant for server-rendered web apps) and `AddIdentityCore<TUser>()` (the minimal building block, with no assumption about the authentication scheme).

**Final decision (Problem 1):** `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<int>>().AddEntityFrameworkStores<RenoTrackDbContext>()` — no `AddDefaultTokenProviders()` either, since nothing yet needs password-reset/email-confirmation tokens (`CLAUDE.md` §4's growth-on-demand discipline applied to Identity's own optional pieces, not just this project's own abstractions). Every one of these is a pure `IServiceCollection` extension, so it integrates directly inside the existing `AddInfrastructure()` (Slice 14) with no new composition root.

**Why chosen:** `AddIdentity` registering cookie-scheme defaults for an API that will never use cookies would be dead configuration at best, and a confusing footgun at worst (a future reader wondering why cookie auth exists when Architecture.md says JWT).

**Problem 2 (raised in review — a real correctness question, not assumed away):** The naive role-seeding shape (`if (!await roleManager.RoleExistsAsync(name)) await roleManager.CreateAsync(...)`) is a classic check-then-act race. Two application instances starting simultaneously could both observe a role missing and both call `CreateAsync`. `AspNetRoles`' unique index on `NormalizedName` (`RoleNameIndex`, a framework default from `IdentityDbContext`'s own base model — not something this project configures) means the loser's `INSERT` fails. Critically, `RoleStore`/`RoleManager` do **not** catch a `SaveChanges` failure into a graceful `IdentityResult.Failed(...)` the way in-memory validation errors are — the `DbUpdateException` propagates unhandled, which would crash startup for the losing instance if left unmitigated.

**Alternatives considered:** (a) Leave the race unmitigated and document it as acceptable, on the reasoning that v1's deployment model (Architecture.md §13: a single Azure App Service/VPS) makes concurrent-instance startup unlikely — rejected: the mitigation is cheap and the assumption is fragile (deployment topology can change, and even a single instance can restart into an overlapping window during a deploy). (b) Catch the failure and re-verify existence before deciding whether it's a genuine problem — the same shape as D52's first-of-year `INSERT` race, applied to a second, independent scenario.

**Final decision (Problem 2):** (b). `IdentityRoleSeeder.SeedRolesAsync` catches `DbUpdateException` from a failed `CreateAsync` and re-checks `RoleExistsAsync`; if the role now exists, a concurrent instance won the race — benign, not rethrown. Any other cause (the role genuinely still missing) rethrows unchanged. `await` isn't permitted directly in a `catch` filter (`CS7094`), so the re-check happens in the catch body, not the filter expression.

**Why chosen:** Directly mirrors D52's already-established pattern for exactly this class of problem — a check-then-act race resolved by a framework-provided uniqueness guarantee, caught and re-verified rather than either ignored or defended against with a heavier mechanism (a distributed lock would be real over-engineering for two role rows). Proven, not assumed: a concurrency test runs 10 simultaneous seeding calls (each its own scope, mirroring real per-request/per-host lifetime) against the same database and asserts none throw and exactly the two expected roles exist afterward.

**Consequences:** This mitigation does not change `IdentityRoleSeeder`'s public shape or `AddInfrastructure()`'s registration — it's entirely internal to the seeder's own implementation, same as D50's audit-failure handling being invisible to callers.

**Update (D55):** this "does not change the public shape" claim held for the `DbUpdateException` manifestation alone, but a second, independent bug was found empirically after this decision — see D55 for the full story and the resulting (justified) shape change.

---

## D55 — `IdentityRoleSeeder` Becomes a Dedicated DI Service; `IServiceScopeFactory` Isolates Each Role's Seeding Attempt

**Problem 1 (a second real bug, found empirically during pre-merge CI verification, not assumed):** D54's `DbUpdateException` mitigation was verified by a 10-concurrent-instance test at the time — but re-running that same test repeatedly during final CI verification (prompted by an unrelated environment fix) surfaced a ~66% failure rate under both Debug and Release configurations. The actual failure: `RoleStore.CreateAsync` does `Context.Add(role); await Context.SaveChangesAsync();` — when `SaveChangesAsync` fails (the race D54 already anticipated), EF Core does **not** discard the failed entity; it remains tracked with state `Added` on that `DbContext`. `IdentityRoleSeeder.SeedRolesAsync`'s loop reused the **same** `RoleManager`/`DbContext` across both roles, so a failed "Admin" attempt left a poisoned entity that rode along into "Inspector"'s `SaveChangesAsync()` call — EF batches all pending changes into one transaction, so the whole batch (including "Inspector", which had no conflict of its own) rolled back together, surfacing as a confusingly-attributed failure on an entirely unrelated role.

**Problem 2 (a related, independently-found gap in the same D54 mitigation):** `RoleManager<TRole>.CreateAsync` calls `ValidateRoleAsync` (which re-checks uniqueness via `FindByNameAsync`) **before** ever touching the store/`DbContext`. If a concurrent instance wins the race in the window between our own `RoleExistsAsync` check and this internal re-validation, `CreateAsync` returns a graceful `IdentityResult.Failed` ("Role name 'X' is already taken") instead of throwing at all — a second manifestation of the exact same race that D54's `catch (DbUpdateException)`-only handling didn't cover.

**Investigation — is recovering the poisoned tracked entity `IdentityRoleSeeder`'s responsibility at all?** Explicitly asked before reaching for a fix, rather than defaulting to patching in place. Microsoft's own EF Core guidance is that a `DbContext` represents one unit of work, and the recommended response to a failed `SaveChanges()` is to discard that context, not attempt to recover and reuse it — manually detaching the specific failed entity (via `Entry(role).State = EntityState.Detached`) is the *non-idiomatic* path, and would require the DbContext reference in the first place, which `RoleManager.Store` doesn't expose publicly (a fix through `Store` would need reflection into a `protected` member — rejected outright, against this project's consistent no-reflection-in-production-code stance). This reframed the diagnosis: the bug isn't a missing catch clause, it's that the loop was treating two **independent** units of work (seeding "Admin", seeding "Inspector") as one, sharing mutable state between them that should never have been shared.

**Alternatives considered for the fix's shape:**
- (a) Add a `DbContext` parameter to `SeedRolesAsync`, detach the failed entity manually in the catch block — rejected: leaks an EF-specific type into what should be an Identity-domain-focused utility, and only manages the symptom (still relies on remembering to detach correctly) rather than removing the shared state that causes it.
- (b) Change `SeedRolesAsync` to accept `IServiceScopeFactory` directly as a **method parameter** — rejected: pushes "how do I get scoped resources" onto every caller (`Program.cs`, every test call site), which is a worse contract than what existed before, and is inconsistent with how every other Infrastructure service in this project takes its dependencies via constructor, not per-call parameters.
- (c) Keep `IdentityRoleSeeder` as a static utility class — rejected on a second, independent ground: it was already the one structural outlier in the whole Infrastructure layer. `IAuditService`, `INumberGeneratorService`, `IEmailSender`, `IFileStorage`, and every repository are all interface-or-class + DI-registered, constructor-injected with exactly what they need. A bare static method with no DI registration didn't match that shape, independent of this bug.
- (d) A dedicated `IdentityRoleSeeder` class, constructed via normal DI (`AddScoped<IdentityRoleSeeder>()`), with `IServiceScopeFactory` injected through its **constructor**, and a **parameterless** public `SeedRolesAsync()` method. Internally, it creates one fresh `IServiceScope` per role, resolving a correctly-wired `RoleManager<IdentityRole<int>>` from each.

**Final decision:** (d).

**Verifying this doesn't reintroduce a service-locator smell, explicitly, before accepting it:** the anti-pattern this project has consistently avoided elsewhere (`DependencyInjection.cs`'s own stated rule: no service takes `IConfiguration`/`IServiceProvider` as a dependency) is specifically about a component holding a *general* resolver and pulling *arbitrary, dynamic* types at runtime, hiding its real dependency graph. `IdentityRoleSeeder` doesn't do that: its constructor declares exactly one narrow capability ("create an isolated scope"), and the code always resolves exactly one fixed, named type from each scope (`RoleManager<IdentityRole<int>>`) — visible directly in the method body, not dynamic, not hidden. This is also Microsoft's own documented pattern for components that outlive a single scope and must perform multiple independent pieces of scoped work — the same shape recommended for `BackgroundService`/`IHostedService` implementations needing scoped dependencies. `IDbContextFactory<RenoTrackDbContext>` was reconsidered as an alternative and rejected again for the same reason as when it first came up: it would supply a fresh `DbContext` but not a correctly-validator-wired `RoleManager`, forcing either hand-construction of Identity's object graph (duplicating what `AddIdentityCore()` already wires) or exposing `RoleManager`'s several constituent dependencies directly — both worse than reusing the existing, correct DI registration via a fresh scope.

**Why chosen:** removes the shared mutable state instead of managing it — no cleanup code is needed at all, which is a net reduction in complexity, not an addition. The public contract also gets simpler as a side effect: `SeedRolesAsync()` takes no parameters, so callers (`Program.cs`, tests) don't need to know or construct anything beyond resolving the service itself.

**Consequences:** `IdentityRoleSeeder` is registered `AddScoped` in `AddInfrastructure()`, alongside every other Infrastructure service — consistent lifetime, no special-casing. `Program.cs` and the three call sites in `IdentityRoleSeederTests.cs` were updated to resolve `IdentityRoleSeeder` and call the now-parameterless `SeedRolesAsync()`, instead of resolving `RoleManager<IdentityRole<int>>` themselves and passing it in — a mechanical call-site change, not a change to any test's assertions or intent. Verified empirically, not just by re-reading the fix: 32 consecutive runs of the concurrency test (22 Debug, 10 Release) all passed, versus roughly a 2-in-3 failure rate before this fix.

---

## D56 — CI Splits Into Two Jobs by OS, So `RenoTrack.Infrastructure.Tests` Can Keep Using Real LocalDB

**Problem:** The first CI run on `feature/phase-3-infrastructure-efcore` failed — not architecturally, environmentally. The single `ci.yml` job ran on `ubuntu-latest`, and `RenoTrack.Infrastructure.Tests` requires real SQL Server LocalDB (D40), which does not exist on Linux at all (not a missing-install problem — LocalDB is a Windows-only SQL Server edition).

**Alternatives considered:** (a) Replace LocalDB with the EF Core InMemory provider or SQLite for CI only, keeping LocalDB for local runs — rejected outright, explicitly, by the user: this would let CI pass without ever exercising the real unique indexes/FKs/`decimal(18,2)` precision D40 exists specifically to verify, silently reintroducing the exact class of gap D40 closed. (b) Drop `RenoTrack.Infrastructure.Tests` from CI entirely, run it only locally — rejected: a whole test project (74 tests) going unverified by CI is a worse regression risk than a slightly more complex workflow file. (c) Run the entire workflow on `windows-latest` — rejected: slower and more expensive for the 297 non-Infrastructure tests that have no LocalDB dependency and gain nothing from a Windows runner. (d) Split `ci.yml` into two jobs by OS: `build-and-test` on `ubuntu-latest` (build + `Domain`/`Application`/`Api` tests), `infrastructure-tests` on `windows-latest` (`needs: build-and-test`, starts `sqllocaldb start MSSQLLocalDB` before running `RenoTrack.Infrastructure.Tests`).

**Final decision:** (d).

**Why chosen:** Preserves D40 exactly as decided — CI now exercises the same real LocalDB behavior a local run does, not a stand-in. The `needs:` gate means the more expensive Windows job only runs after the cheap Linux job (build + 297 tests) already passed, so a broken build fails fast without ever spinning up the Windows runner. This mirrors the project's general instinct (see D50/D52/D54's "fix the real problem, don't paper over it with a weaker substitute") applied to CI infrastructure rather than application code.

**Consequences:** `.github/workflows/ci.yml` now has two jobs instead of one; a PR's "all checks passed" status now depends on both. No test file changed — the fix is entirely in the workflow definition, matching the user's explicit constraint not to touch test code to make CI green. Verified directly via the GitHub Actions API, not assumed: both jobs passed (`build-and-test` in ~26s, `infrastructure-tests` in ~1m39s including a successful LocalDB start) on the commit that introduced the split.

---

## D57 — API Versioning by URL Segment, With No Versioning Library

**Problem:** `Architecture.md` §5.1 mandates routes versioned from day one (`/api/v1/...`) but names no mechanism. ASP.NET Core offers a dedicated package (`Asp.Versioning.Mvc`) providing version negotiation across URL segments, query strings, headers, and media types, plus deprecation metadata and per-version API explorer grouping. Phase 4 builds the first controllers, so the convention must be settled before any route exists.

**Alternatives considered:** (a) Adopt `Asp.Versioning.Mvc` now, configuring URL-segment versioning through it, so a future v2 is a configuration change rather than a structural one. (b) Header or media-type versioning — rejected immediately: contradicts `Architecture.md` §5.2's own literal endpoint table, which spells out `/api/v1/...` paths throughout. (c) Literal `api/v1` route-template prefix (`[Route("api/v1/[controller]")]`) with no library at all.

**Final decision:** (c).

**Why chosen:** There is exactly one version, and no documented plan for a second anywhere in the source documents. Installing version-negotiation infrastructure for a version that does not exist is precisely the speculative-abstraction failure mode `CLAUDE.md` §4 already forbids for repositories, DTOs, and schema — applied here to routing. The literal prefix satisfies `Architecture.md` §5.1 completely and is readable without knowing any library's conventions, which matters for a project whose stated goal is that every step be traceable by a junior developer.

**Consequences:** Every controller carries an explicit `[Route("api/v1/[controller]")]`; sub-resource routes (e.g. `POST /api/v1/leads/{leadId}/inspections`) are explicit route templates on the owning controller. When a genuine v2 is needed — a real breaking change, not a hypothetical one — v2 controllers are added alongside v1 under `api/v2/...` so v1's behavior never silently changes underneath existing clients; a versioning library is reconsidered only if content negotiation or deprecation headers become an actual recurring need. Also settled here: controllers are `[Authorize]` by default with `[AllowAnonymous]` opted into per action (e.g. `POST /api/v1/leads` for the website contact form), since a forgotten `[Authorize]` silently exposes an endpoint while a forgotten `[AllowAnonymous]` merely fails closed.

---

## D58 — `RenoTrack.Api.Tests` Runs the Real Pipeline Against Real LocalDB, Migrated Not `EnsureCreated`

**Problem:** `RenoTrack.Api.Tests` existed as an empty project through Phases 0–3. Phase 4 gives it its first real job, and its testing strategy had to be settled before the first endpoint was written — including what "real" means for its backing store, given `RenoTrack.Infrastructure.Tests` had already established one answer (real LocalDB, D40) via a different mechanism (`EnsureCreatedAsync`).

**Alternatives considered — what to test:** (a) Re-verify business rules over HTTP — rejected: Domain and Application tests already cover those exhaustively; duplicating them per endpoint would be expensive and would drift. (b) Test only what the API layer adds over the layers beneath it: routing, model binding, role/ownership enforcement reaching from a JWT to a real 403, and ProblemDetails shape — one happy path plus one representative guard failure per endpoint.

**Alternatives considered — backing store:** (c) A hand-rolled fake `IUserStore`/in-memory Identity so tests need no database — rejected: it builds a second, parallel Identity mechanism that exists only in tests, and login is meaningless without a real `UserManager` and real password hashing. (d) Real LocalDB via `WebApplicationFactory<Program>`, matching D40's stance.

**Alternatives considered — schema creation:** (e) `EnsureCreatedAsync()`, mirroring `RenoTrackDbContextFixture`. (f) `Database.MigrateAsync()`.

**Final decision:** (b) + (d) + (f). No mocking framework, consistent with `CLAUDE.md` §14.

**Why chosen:** (f) was chosen over (e) after the user challenged an initial proposal that had defaulted to (e) purely on the strength of `Infrastructure.Tests`' precedent. That precedent does not transfer: `Infrastructure.Tests` constructs a `DbContext` directly and never executes `Program.cs`, so it has no production startup path to stay faithful to; `Api.Tests` boots the real application, which in production always runs against a migrated database. Three further points settled it. **Schema fidelity:** the two are provably equivalent *today* (no migration in this repo contains `migrationBuilder.Sql`, `InsertData`, or `HasData`, and `InitialCreateMigrationTests` proves migrations match the model) — but that equivalence is a property of the current migrations, not a guarantee, and `EnsureCreated` would silently diverge the moment raw SQL or seed data entered a migration. **Migration coverage:** `EnsureCreated` would leave `InitialCreateMigrationTests` as the only place migrations are ever executed. **Forward-compatibility, the decisive point:** `EnsureCreated` never writes `__EFMigrationsHistory`, so if the still-open migration-application decision (Phase 4's final slice) lands on startup-time `Database.MigrateAsync()`, that call — which `WebApplicationFactory` executes via the real `Program.cs` — would find zero applied migrations against existing tables and fail on `CREATE TABLE`. `MigrateAsync` in the fixture is correct under *both* outcomes of that pending decision: a no-op if startup migrates, a faithful stand-in if CI/CD does.

**Consequences:** `RenoTrackApiFactory` (a `WebApplicationFactory<Program>` + `IAsyncLifetime`) owns schema creation and teardown against its own database (`RenoTrackApiTests`, deliberately separate from `RenoTrackInfrastructureTests` so the two suites cannot interfere even when run as concurrent processes), overrides `ConnectionStrings:RenoTrackDb` via `UseSetting`, and forces `UseEnvironment("Development")` because `WebApplicationFactory` otherwise defaults to `Production`, where `Program.cs` does not map the OpenAPI document. All API tests share it through a `[CollectionDefinition("Api")]` `ICollectionFixture`, so they run serially against one database — the same shape as `Infrastructure.Tests`. `RenoTrack.Api.csproj` gained `<InternalsVisibleTo Include="RenoTrack.Api.Tests" />` because top-level statements compile `Program` as `internal` (granting one named assembly access, rather than making `Program` public to every consumer, matches the D7 precedent). CI: `Api.Tests` moved off the Linux `build-and-test` job onto the Windows job, renamed `database-backed-tests`, for exactly D56's reason — the OS split exists so real-database tests keep using a real database.

The mechanism by which the fixture creates its schema was considered for a decision entry of its own and deliberately rejected as one: it is a test-harness implementation detail, not a cross-cutting architectural rule, and padding this log with implementation minutiae dilutes it. The reasoning lives in `RenoTrackApiFactory`'s own XML doc comment and in `PHASE4_PROGRESS.md`'s Slice 1 entry.

---

## D59 — One Exception Handler With an Explicit Mapping Table; `ArgumentException`→400 / `InvalidOperationException`→409 as a Knowingly-Accepted Risk

**Problem:** `Architecture.md` §5.3 requires every error to funnel through one middleware producing RFC 7807 `ProblemDetails`. Two things needed settling: the mechanism, and the exact status code for Domain's own `ArgumentException`/`InvalidOperationException` — deferred since Phase 2 (`CLAUDE.md` §17 recorded a lean toward 400/409 but explicitly left it open for Phase 4's middleware design).

**Alternatives considered — mechanism:** (a) One `IExceptionHandler` implementation per exception type, registered in sequence, with ASP.NET trying each until one returns `true` — rejected: registration order silently determines behavior, and the full mapping ends up spread across six files with no single place to read it, the same "hidden pipeline" property D22 rejected MediatR for. (b) A hand-written `IMiddleware` with a `try`/`catch` — rejected: `IExceptionHandler` + `AddProblemDetails()` is the framework's own supported seam and gets content negotiation and the `application/problem+json` content type for free. (c) One `IExceptionHandler` containing a single explicit `switch` expression over exception type.

**Final decision — mechanism:** (c). The whole mapping table is one readable block reachable by "Go to Definition" from one place — the same reasoning D24 used to choose one shared `AuditAction` enum over per-entity ones.

**Alternatives considered — the `ArgumentException`/`InvalidOperationException` mapping:** (d) Leave both unmapped, falling through to 500 — rejected: plainly wrong for the 24 Domain guards that exist today; BR-10's "Cannot add a photo to a completed Inspection" is a client error, and reporting it as a server fault is a worse inaccuracy than the one being avoided. (e) Change Domain to throw dedicated exception types — rejected for this slice: it reopens `CLAUDE.md` §17's "no generic base exception type" decision and modifies the Domain baseline `NEXT_STEPS.md` §3 marks stable, on a risk that is real but so far hypothetical. (f) Map `InvalidOperationException` only when it originated in the Domain assembly, via `ex.TargetSite?.DeclaringType?.Assembly` — rejected despite targeting the risk precisely: it is reflective, degrades silently when `TargetSite` is null, and would force `RenoTrack.Api` to take an explicit Domain project reference purely to run an assembly comparison. Clever-but-fragile is what this codebase consistently avoids. (g) Map as documented (400/409), and make the masking observable through logging.

**Final decision — mapping:** (g), with the risk recorded rather than inherited silently.

**Why chosen:** the risk is genuine — `ArgumentException` and `InvalidOperationException` are BCL-wide types, and EF Core throws `InvalidOperationException` for tracking conflicts and untranslatable queries, so a real infrastructure fault could surface as an intelligible-looking 409 instead of a 500. What made (g) acceptable was checking the actual exposure rather than reasoning abstractly: every request-path occurrence of both types in this codebase today originates in a Domain guard. Infrastructure throws `InvalidOperationException` in exactly two places (`DependencyInjection`'s missing-connection-string check and `IdentityRoleSeeder`'s failure path), both startup-only, neither reachable during a request. The mitigation is that **every** mapped exception is logged at `Warning` *with its full stack trace* (unmapped ones at `Error`), so a mis-mapped infrastructure fault remains discoverable in logs rather than invisible. Reopen only with concrete evidence of a real masking incident, not on principle.

**Consequences:** `ProblemDetailsExceptionHandler` (`src/RenoTrack.Api/ErrorHandling/`) maps `NotFoundException`→404, `ForbiddenException`→403, `ConflictException`→409, FluentValidation's `ValidationException`→400, `ArgumentException`→400, `InvalidOperationException`→409, everything else→500. **Message-leakage policy:** mapped exceptions carry their message outward as `detail`, because each is authored here and phrased for a caller; anything unmapped gets a fixed generic title and *no* `detail` member at all, so an unexpected `SqlException` can never surface connection strings or schema names — this is why the fallback is a separate branch rather than shared code, and it is covered by a test that throws a message containing a fake password and asserts it never reaches the client. `ValidationException` produces a field-keyed `errors` dictionary (multiple failures on one property group under one key) rather than a flat string, matching what ASP.NET already emits for model-binding failures, because the Angular Dashboard (Phase 10) needs to highlight individual inputs. `traceId` is added via `AddProblemDetails(options => options.CustomizeProblemDetails = ...)` in `Program.cs` rather than inside the handler, so it appears on *every* `ProblemDetails` response including ones ASP.NET produces itself with no exception involved — a handler-only approach would have missed those. `app.UseExceptionHandler()` is first in the pipeline. `RenoTrack.Api` gained an explicit `FluentValidation` package reference (it catches `ValidationException` by type; the transitive reference through `RenoTrack.Application` would have compiled, but D2's discipline is to declare what a project actually uses).

`OperationCanceledException` (thrown on client disconnect, currently falling through to 500 and logging at `Error`) was raised during review and deliberately left out of scope: it belongs to the hosting/runtime layer rather than to Domain/Application exception mapping, and is to be addressed with real evidence of log noise rather than speculation.

---

## D60 — Authentication Sits Outside the CQRS Pipeline, With Persisted, Rotating, Hash-Stored Refresh Tokens

**Problem:** Phase 4 Slice 4 had to answer several authentication questions at once, and the answers interact: where login lives architecturally, what the refresh-token model is, and how both fit the conventions every other operation in this system follows.

### Part 1 — Login is not an Application-layer command. This is deliberate; do not "fix" it.

**The most important thing to understand about this decision:** every other operation in RenoTrack is a `Command` + hand-written `Handler` in `RenoTrack.Application` (`CLAUDE.md` §3). `AuthController` is the single exception, and a future contributor noticing the inconsistency should read this section before attempting to make it uniform.

**Alternatives considered:** (a) `LoginCommand` + `LoginCommandHandler` in `RenoTrack.Application`, requiring a new `IIdentityService` abstraction over `UserManager`/`ApplicationUser` (both Infrastructure types, forced there by D53 and D1's zero-project-reference rule) plus an `ITokenService`, so the Application layer could participate. (b) Login lives entirely in `RenoTrack.Api`, calling `UserManager` and `ITokenService` directly.

**Final decision:** (b).

**Why chosen — the reasoning that matters:** the CQRS-lite convention exists to keep *business use cases* traceable and testable, and authentication is not one. It has **no aggregate**, **no Domain invariant**, **no state machine transition**, and **no audit milestone** (§10's audit rule covers business milestones; a login is not one). Routing it through a command handler would produce an abstraction (`IIdentityService`) whose sole purpose is to let a layer with no business rules about authentication appear to own it — ceremony with no business value, exactly the failure D49 rejected when it declined to make `AuditLog` a rich Domain entity. The uniformity gained would be cosmetic, and the cost is two interfaces plus an indirection layer that no reader benefits from following.

**Consequences:** `AuthController` (`src/RenoTrack.Api/Controllers/AuthController.cs`) injects `UserManager<ApplicationUser>` and `ITokenService` directly. `ITokenService` is declared in `RenoTrack.Infrastructure.Identity`, **not** in `RenoTrack.Application.Common.Interfaces` where every repository/service interface lives — because the Application layer neither consumes nor could consume it, and declaring an abstraction a layer never uses would be worse than the inconsistency it papers over. This does not weaken §3: it narrows it to what it was always about. **If a future authentication concern acquires a genuine business rule** (e.g. an audited "user account locked" milestone that must appear on a Lead timeline), that specific concern becomes a command — the boundary is "does this have business rules," not "is it authentication."

### Part 2 — Refresh tokens are persisted, hashed, and rotated

**Alternatives considered:** (c) No refresh tokens at all, just a longer-lived access token — rejected: contradicts `Architecture.md` §7.1, which names the pattern explicitly, and a long-lived bearer token cannot be revoked at all. (d) Stateless refresh tokens (a second signed JWT) — no storage needed, but **cannot be revoked**, which defeats most of the reason to have a refresh token rather than a longer access token. (e) A persisted `RefreshToken` table.

**Final decision:** (e), with three specific properties:

- **Only a SHA-256 hash is stored, never the plaintext.** The client receives the plaintext once; every later lookup hashes the incoming value. A database read yields no usable credential — the same reasoning already applied to passwords. SHA-256 rather than a password hash (PBKDF2/bcrypt) is correct because the input is already 32 bytes of cryptographic randomness: there is no entropy to stretch and nothing to brute-force.
- **Rotation on every use.** The presented token is revoked (`RevokedAt` + `ReplacedByTokenHash`) and a new pair issued. **`RevokedAt` is an EF concurrency token, which is what makes "a token transitions from active to revoked exactly once" a guarantee the database enforces rather than one that depends on request timing.** Added during the Phase 4 closeout review after the PR review identified the gap; it is not a refinement but a real fix. Without it the check-then-revoke sequence is a plain read-modify-write race, and **that race was fully reproducible, not theoretical: eight concurrent refreshes of one token all succeeded, producing eight live chains from a single token and bypassing reuse detection entirely** (measured, 3/3 runs). With the concurrency token the losing `UPDATE` matches zero rows and raises `DbUpdateConcurrencyException`; because EF wraps `SaveChanges` in one transaction, the loser's replacement `INSERT` rolls back with it, so revocation and its successor are always committed together. The loser returns the same 401 as every other refresh failure and is deliberately **not** treated as reuse — it is a legitimate concurrent request, not a replay, and revoking the chain there would let a client's double-submit log itself out. Its tracked entities are detached, because the request-scoped `DbContext` is shared and a failed `INSERT` left in `Added` state could otherwise be committed by a later `SaveChangesAsync` on the same request. **No migration was required:** a concurrency token on a non-`rowversion` column is model metadata only, confirmed by `has-pending-model-changes` reporting no drift.
- **Reuse detection.** Presenting an *already-revoked* token revokes **every** outstanding token for that user. A revoked token arriving means either a stolen token is being replayed or a client bug; in the theft case the attacker and the legitimate user both hold live tokens, and breaking the whole chain is the only way to end the attacker's access. Forcing one re-login is the correct trade against leaving a compromised session alive. This is why `ReplacedByTokenHash` exists rather than rows simply being deleted on rotation.

`RefreshToken` is **Infrastructure-only, not a Domain entity** — same reasoning as `AuditLog` (D49) and `NumberSequence` (D51): no business invariant references it, and authentication is a mechanism rather than a business concept.

**Deliberately not built:** a logout endpoint. Revocation is a *capability* this model enables, but no requirement documents logout, and building it now would be speculative (`CLAUDE.md` §4). The storage supports it the moment a real requirement appears.

### Part 3 — Retention: chosen consciously, not left to accumulate

A row carries information only until `ExpiresAt`. Revoked-but-unexpired rows **must** be kept — they are exactly what makes reuse detection possible — but once expired, a token is rejected on expiry grounds regardless of revocation state, so the row is dead weight. **Retention is therefore until `ExpiresAt`; anything older can be deleted at any time with zero behavioural change.**

**No cleanup mechanism is built, and that is a decision rather than an omission.** With 15-minute access tokens an active user produces roughly 32 rows per working day; at a 7-day window, steady state is about `users × 32 × 7` — a few hundred rows for this company's real staff count, a few thousand even at twenty users. Building a background job for that would be solving a non-problem. **Revisit when** the table reaches a size that actually matters (tens of thousands of rows, or an order-of-magnitude increase in users); the fix is then a background job deleting rows past `ExpiresAt`. Note `CLAUDE.md` §2's "never truly delete a historical record" does **not** apply here — that rule governs business records, not authentication mechanisms.

### Part 4 — Lockout, lifetimes, and configuration

- **SRS FR-10.3 (rate-limit failed logins) is honoured in this slice, not deferred.** `AddIdentityCore` deliberately does not register `SignInManager` (D54, avoiding cookie-auth defaults a JWT API never uses), and `UserManager.CheckPasswordAsync` does **not** touch lockout counters by itself. `AuthController` therefore calls `IsLockedOutAsync` / `AccessFailedAsync` / `ResetAccessFailedCountAsync` explicitly. Without those three calls the documented requirement would silently not exist.
- **Every login failure returns an identical 401** — unknown email, wrong password, inactive account, and locked-out account are indistinguishable, because distinguishing them turns the endpoint into an account-enumeration oracle. This is a deliberate, narrow exception to D59's "mapped exceptions carry a useful message" policy: here unhelpfulness is the feature. Failures are logged server-side with the real reason, so operators lose nothing.
- **15-minute access tokens, 7-day refresh tokens, both from configuration** (`Jwt` section), not constants — they are operational knobs.
- **`ClockSkew = TimeSpan.Zero`.** The framework default of five minutes would keep a 15-minute access token usable for twenty — a third longer than configured, silently.
- **Configuration is validated eagerly at startup** (`JwtOptions.Validate()`): issuer, audience, and signing key must be present, and the key at least 32 characters (HMAC-SHA256's own floor). Failure throws naming the exact configuration key, matching `AddInfrastructure`'s connection-string check. The signing key is never committed — `appsettings.Development.json` locally, environment/secrets elsewhere (Architecture.md §13).
- **`AddJwtAuthentication` is a separate extension from `AddInfrastructure`**, and registers `ITokenService` itself despite that service touching persistence: `TokenService` depends on `JwtOptions`, which only this method supplies, so splitting them would leave `AddInfrastructure` advertising a service that cannot be constructed — caught immediately by the `ValidateOnBuild` DI test when it was first written the other way round.

---

## D61 — An Endpoint's Request Contract Is Narrower Than Its Command; Identity and Context Are Always Server-Derived

**Problem:** `CreateLeadCommand` has seven parameters. Two of them — `Source` and `CreatedByUserId` — must not come from an HTTP request body on the public contact-form endpoint. The general question this raised, which recurs on every remaining Phase 4 slice: when a controller translates a request into an existing command, does it pass the request straight through, or is the wire contract deliberately smaller?

**Alternatives considered:** (a) Bind `CreateLeadCommand` directly as the action parameter — zero mapping code, no new type, and it looks like the purest expression of "the endpoint contract is driven by the existing use case." Rejected: it would let an anonymous caller set `CreatedByUserId` to any user id (mis-attributing the Lead and its audit entry) and set `Source` to `Phone`, which is not cosmetic — `CreateLeadCommandHandler` notifies the Admin **only** when `Source == Website` (SRS FR-9.2), so a caller controlling that field can suppress the notification for Leads they create. (b) Keep the direct binding but overwrite the sensitive fields in the controller after binding — rejected: the fields still appear in the generated OpenAPI document as inputs, inviting clients to send them, and "we bind it then ignore it" is a convention a future edit can quietly break. (c) A dedicated request record containing only the fields a caller may legitimately supply, with the controller constructing the command and filling the rest.

**Final decision:** (c), generalized into a standing rule:

> **A controller never accepts, from the request body or query string, any value that represents *who the caller is* or *what context they are acting in*. Those are derived server-side — from the authenticated principal, from the route, or from the endpoint's own fixed meaning. The request contract is therefore normally a strict subset of the command's parameters, and a new request record is justified exactly when that subset differs.**

**Why chosen:** the alternative fails closed nowhere — every field a caller can set is a field an attacker can set, and the damage is not always obvious from the field's name (`Source` reads like a harmless label until you notice it gates a notification). Making the wire contract structurally unable to express those values is stronger than any amount of controller-side sanitisation, and it keeps the OpenAPI document honest about what the endpoint actually accepts. This does **not** contradict the standing instruction to let the existing use case drive the endpoint: the *handler* is unchanged, and remains correct for both the website and the future Admin-entry path. The endpoint is narrower than the use case, which is the right relationship — one use case can have several endpoints exposing different subsets of it.

**Consequences:** `CreateLeadRequest` (`src/RenoTrack.Api/Leads/Dtos/`) has five fields to the command's seven; `LeadsController.Create` supplies `Source: LeadSource.Website` and `CreatedByUserId: null`. A test posts `source` and `createdByUserId` anyway and asserts the created Lead is still `Website` with no assigned inspector — the rule is verified, not just documented. **This rule governs the remaining Phase 4 slices directly**: the aggregate id comes from the route, and the acting user's id comes from the JWT's `sub` claim.

> **Correction, made in Slice 7.** This paragraph originally read *"the inspector id for scheduling (Slice 7), photo upload (Slice 8), and completion (Slice 9) all come from the JWT's `sub` claim, never from the request."* That is wrong for scheduling, and the error was over-broad in a way that would have caused real harm: `ScheduleInspectionCommand` carries **two** user ids of opposite kinds. `ScheduledByAdminId` is who is acting and is server-derived, as the rule says — but `InspectorId` is *who the work is assigned to*, a third party the Admin deliberately chooses (`PermissionMatrix.md` §2 marks scheduling Admin-`F`). Taking it from the token would have made it impossible for an Admin to schedule anyone but themselves, which is the entire operation. The rule is about **the caller's own identity and context**, not about every user id that appears in a request. In Slices 8 and 9 the inspector id *is* server-derived, because there the Inspector acts on their own Inspection — but that is a property of those use cases, not of the field's name. A new request record is *not* introduced when the subset happens to be the whole command — this rule justifies the DTO, it does not mandate one per endpoint.

**Related, decided in the same slice (no separate entry):** enums serialize as names, not ordinals (`JsonStringEnumConverter` in `Program.cs`). An ordinal contract silently changes meaning if anyone reorders an enum — an invisible breaking change for every client — while the database already stores these same enums as strings for the readability reason `ERD.md` gives, and every project document refers to statuses by name. Also: `POST /api/v1/leads` is deliberately **not** idempotent (two identical submissions create two Leads), because silently de-duplicating would be an invented business rule that discards a genuine second enquiry; a duplicate row is something an Admin can close, a swallowed enquiry is a lost customer.

---

## D62 — Business Rules About Staff Accounts Go Through `IUserQueries`; a Database FK Is Not a Business Rule

**Problem:** `ScheduleInspectionCommand` takes an `InspectorId` chosen by an Admin, and nothing checked it. `Inspection.InspectorId` has a real FK to `AspNetUsers` (D53), so a mistyped id failed at `SaveChangesAsync` with a `DbUpdateException` — unmapped by D59's middleware, and therefore **500 on an ordinary client mistake**. Worse, the FK could not catch the other two ways the value can be wrong: a real user who is an **Admin** rather than an Inspector, or an Inspector whose account has been **deactivated** (`ApplicationUser.IsActive`). Both would have been accepted by the database and produced invalid business data — site work assigned to someone who cannot or should not do it.

**Alternatives considered:** (a) Accept the 500 and defer to a later hardening pass — rejected by the user on the grounds that this is not hardening but a business rule: an Inspection assigned to a non-existent, non-Inspector, or deactivated account is invalid data, and the Application layer should refuse it before persistence rather than relying on a storage constraint. (b) Map `DbUpdateException` FK violations to 400 in the exception middleware — rejected: it fixes only the status code, still permits the Admin-as-assignee and inactive-assignee cases, and requires sniffing provider-specific SQL error numbers, which is fragile. (c) Verify in the handler through a new Application-layer abstraction over Identity.

**Final decision:** (c). `IUserQueries.IsActiveInspectorAsync(int userId, CancellationToken)` in `Application.Common.Interfaces`, implemented by `UserQueries` in `Infrastructure/Identity/`.

**Why chosen, and why this does not contradict D60:** D60 kept *authentication* out of the Application layer because logging in has no aggregate, no invariant, and no business rule — it is a mechanism. This is the mirror image: "an Inspection may only be assigned to an active Inspector" **is** a business rule, so the Application layer must be able to enforce it, and it cannot reach `UserManager` directly because Identity types are Infrastructure-only (D53, forced by D1). An abstraction here with an Infrastructure implementation is the standard resolution, and the two decisions state one consistent principle: **business rules live in Application, mechanisms live in Infrastructure.**

**Consequences:** One method, not three, deliberately — every caller wants the same conjunction of "exists, active, is an Inspector", and splitting it would invite a caller to check two of the three and quietly permit the case the third would have caught. The name states the business question rather than the lookup (CLAUDE.md §4). It lives in `Common.Interfaces` rather than a feature folder because it returns no feature DTO, so D23's constraint does not apply. The handler throws `NotFoundException` (→404) for all three cases: from the caller's point of view the resource they named — an assignable Inspector with that id — does not exist, which is honest for all three and, as a side benefit, does not disclose whether the id belongs to some other kind of account. The check runs **before** the Lead is mutated, so a rejected assignee leaves no partial in-memory state; a test asserts the Lead's status and assignment are untouched.

**Verified by reproducing the defect**, not by argument: disabling the check made `Scheduling_to_a_non_existent_user_...` return `InternalServerError`, exactly the failure the check exists to prevent.

**Related cleanup made in the same slice:** role names became named constants (`IdentityRoleSeeder.AdminRole`/`InspectorRole`), with the API's `Roles` class forwarding to them rather than repeating the literals. The names are now referenced from three places — the seeder, `UserQueries`' role predicate, and `[Authorize(Roles = ...)]` attributes — and a mismatch between any two fails **open** for an `IsInRole` scope check, which is precisely the defect shape found in Slice 6.

---

## D63 — Production Applies Migrations Outside Application Startup; Startup Verifies Migration History and Required Roles

**Problem:** nothing in `src/` had ever applied a migration. `grep` for `Migrate`/`EnsureCreated` across the whole of `src/` returned no match outside the migration files themselves, while `Program.cs` unconditionally ran `IdentityRoleSeeder.SeedRolesAsync()` at startup — whose first action is a `SELECT` against `AspNetRoles`. A fresh production deployment therefore could not start at all, and had never been able to.

**Verified by reproduction, not inference.** Running the real application against a brand-new database produced:

```
Unhandled exception. Microsoft.Data.SqlClient.SqlException: Invalid object name 'AspNetRoles'.
   at Microsoft.AspNetCore.Identity.RoleManager`1.RoleExistsAsync(String roleName)
   at RenoTrack.Infrastructure.Identity.IdentityRoleSeeder.SeedRolesAsync() ... line 64
```

Two further findings came out of that reproduction:

1. **There are two distinct fresh-deploy failures, not one.** A database that does not *exist* fails with SQL error 4060, which EF Core wraps as *"likely due to a transient failure. Consider enabling transient error resiliency by adding 'EnableRetryOnFailure'"* — actively misleading, pointing an operator at retry policies rather than a missing database. Only once the database exists but is empty does the error 208 above appear.
2. **`RenoTrackApiFactory` already documented the bug.** Its comment reads *"The schema must exist before the host is first created: Program.cs seeds Identity roles during startup, which fails against a database with no tables."* The test harness had been working around, in test code, the exact step production lacked.

**Alternatives considered:** (a) `Database.MigrateAsync()` at startup — rejected on three grounds: it requires the application's *runtime* login to hold DDL permission permanently, turning any future application-level compromise into a schema-destruction capability; it applies schema changes on every unplanned restart with no review, dry run, or backup window; and EF Core takes no cross-process lock, so two instances starting together race — which Azure App Service (Architecture §13's target) does on every overlapped-restart deploy, *even at one configured instance*. That last hazard is the one D54/D55 already refused to hand-wave for role seeding, so relying on "we only run one instance" here would contradict a decision this codebase has made. (b) A separate migrator process/init-container — has (a)'s permission profile without its convenience, and earns its keep with orchestration this project does not use. (c) Explicit deployment step, application verifies only.

**Final decision:** (c), as a durable policy:

> **Production applies schema changes through an explicit deployment step and the application never mutates schema at startup; startup instead performs a read-only readiness check — migration-history compatibility plus required roles — and refuses to serve if it fails. Development may opt into migrating, and must say so in configuration. `DatabaseInitializer` never provisions users, in any environment.**

*(The final clause originally read "Startup never provisions users, in any environment." **Amended by D64**, which added `DevelopmentBootstrap` as a separate startup step that provisions accounts in the Development environment only, and is refused everywhere else. `DatabaseInitializer` itself is unchanged and still provisions no users anywhere; **no code path reachable in Production creates a user**, so this decision's Production position is untouched.)*

**Why chosen:** least-privilege is the decisive factor. Verification is read-only, so the runtime login needs no DDL rights at all, while a short-lived deployment credential holds them. The verification half is what makes this safe rather than merely correct: without it, "someone forgot to run migrations" surfaces as scattered runtime 500s instead of one unmissable startup failure.

**Mechanism.** `DatabaseInitializationOptions` (`Database:Mode`) with exactly two modes, `Verify` and `Migrate`. **`Verify` is the default when the key is absent**, so an unconfigured deployment fails safe rather than silently mutating. An unrecognised value fails eagerly, naming the key and the allowed values. `Migrate` is **refused outright in Production** — a warning was explicitly rejected, since a setting that logs disapproval and then does the dangerous thing anyway provides none of the least-privilege benefit that motivates the policy.

**A "None"/"Skip" mode was considered and deliberately not built.** Every environment that might seem to want one — including a DBA-managed database the application must not touch — is better served by `Verify`, which is already read-only. An escape hatch would exist only to disable the check that catches the exact failure this decision exists to prevent. If a real environment is ever found that genuinely cannot perform a read-only verification, that evidence should reopen this decision rather than a mode being added pre-emptively.

**Migration-history compatibility is checked in both directions**, deliberately, and is *not* a schema diff:
- *Known but not applied* → the database is behind this build → refuse, naming the pending migration ids.
- *Applied but not known* → the schema is newer than this build, the realistic cause being a rollback to an older release → refuse, naming the unknown ids. **`GetPendingMigrationsAsync` alone cannot detect this direction**, which is why applied-vs-known sets are compared instead.

Anything beyond that — a general schema-comparison engine, expand/contract compatibility checking — is explicitly out of scope.

**Role seeding moved out of normal startup** into the same explicit initialization step, on operational grounds rather than symmetry: it shares migrations' precondition and failure mode, and it *writes*, which conflicts with a read-only startup posture. Startup verifies the roles exist instead, because a missing role otherwise presents as a fleet-wide permissions outage with no obvious cause — every `[Authorize(Roles = ...)]` silently denying. **`IdentityRoleSeeder` itself is unchanged**; D54/D55's race-tolerant design is orthogonal and only its caller moved.

**User provisioning stays out.** Schema initialization, role reference data, and user provisioning are three separate concerns. SRS **OQ-1** remains unresolved, and a database having roles must never imply a privileged user silently exists. A fresh production database therefore has schema and roles and nobody able to log in — the known OQ-1 gap, sharpened rather than closed.

**Amended in part by D64**, for Development only: the "nobody able to log in" consequence proved to make the API undrivable by hand, so a separate startup step now provisions development accounts behind three stacked guards. The separation of the three concerns is *preserved* by that change rather than weakened — provisioning went into its own component, not into this one — and **Production is unchanged: no code path there creates a user, and OQ-1 remains open.**

**Deployment artifacts:** an **EF migration bundle** (`dotnet ef migrations bundle`) is the primary recommended mechanism — a self-contained executable needing no SDK on the target. An **idempotent SQL script** (`dotnet ef migrations script --idempotent`) is the supported alternative where a DBA reviews and applies changes. Building an actual deployment pipeline was explicitly out of this slice's scope.

**Concurrency, stated honestly:** the Development `Migrate` path is *not* concurrency-safe, and deliberately so. Production never migrates at startup, so concurrent migration is not a production scenario, and adding distributed locking to make a developer convenience horizontally scalable would be architecture built for a case that does not exist.

**Consequences:** `AddInfrastructure()` now depends on `IHostEnvironment` in addition to `ILogger<T>` — both supplied free by the generic host, both absent in a hand-composed container. Three test helpers add it, exactly as they already add `AddLogging()`; the requirement is documented on `AddInfrastructure` itself. **The Slice 3 DI safety net caught this within minutes**, failing with *"Unable to resolve service for type 'IHostEnvironment' while attempting to activate 'DatabaseInitializer'"* — the second time that test has caught a later slice's mistake (the first was `TokenService` in Slice 4). D40 and D58 are untouched: `Api.Tests` still migrates its own database and `Infrastructure.Tests` still uses `EnsureCreated`; neither test lifecycle is routed through the initializer.

---

## D64 — Development Login Accounts Are Provisioned by a Separate `DevelopmentBootstrap` Step, Allowlisted to Development, Create-Only, With No Compiled-In Credential

**Problem:** D63 deliberately left a fresh database with schema and roles and **nobody able to log in**. Every endpoint except `POST /api/v1/leads` and `POST /api/v1/auth/login` is `[Authorize]`, and login needs an account that exists — so after a clone, or any `EnsureDeleted`, the API cannot be driven by hand at all. Scalar is wired up and unusable. Phase 5 (Angebot builder + review) makes this acute: the flow is two-actor by definition — an Inspector builds and submits (ownership-scoped, `S`), an Admin approves or requests changes (`F`) — so neither role alone can exercise it.

Automated coverage was never the gap: `RenoTrack.Api.Tests` already provisions seven users through the real `UserManager`. This is exclusively about a human driving the application.

**This does not answer SRS OQ-1.** Whether Admins manage Inspector accounts from the dashboard, or v1 provisions them directly in the database, remains open and untouched. **No code path reachable in Production creates a user**, so D63's closing position — "a database having roles must never imply a privileged user silently exists" — is unchanged where it matters. What this decision adds is a Development-only carve-out, and the guard is what makes that claim true rather than aspirational.

**Alternatives considered:** (a) no seeding, documented manual SQL — rejected: hand-writing an Identity `PasswordHash` is error-prone, and every database drop re-blocks manual work. (b) A separate console tool run out-of-band — the strongest alternative, since the API binary would then contain no user-creating code at all and no guard could fail open; rejected for this phase on cost (a new project, a second composition root, CI wiring) and **recorded as the escalation path**: if this system ever deploys to a real production environment with real customers, the correct move is to extract the seeder into a tool, not to harden the guard further. (c) An Admin "create user" endpoint — that *is* OQ-1, needs a business decision, and is chicken-and-egg anyway since calling it requires a first Admin. (d) A startup step, gated.

**Final decision:** (d), as `DevelopmentBootstrap` — **its own startup step, not a fourth responsibility of `DatabaseInitializer`.**

**Why a separate component.** `DatabaseInitializer` exists to make exactly one statement — *this database is ready to serve* — and to refuse startup when it is not. Whether a convenience account exists says nothing about readiness. Folding it in would mean the component whose entire justification is a least-privilege, read-only Production posture also owning the one operation that mints a privileged credential. They run as two `await`ed steps in separate scopes in `Program.cs`.

**When something earns a `Program.cs` startup orchestrator — the four-condition test.** Two distinct things are easy to conflate here. The *component shape* (dedicated DI service, constructor-injected `IServiceScopeFactory`, parameterless public method, one fresh scope per independent unit of work) is an established pattern with three instances: `IdentityRoleSeeder` (D55), `DatabaseInitializer` (D63), `DevelopmentBootstrap` (D64). The *number of orchestrators resolved directly from `Program.cs`* is two, and is expected to stay two — `IdentityRoleSeeder` already shows the difference, since it has the shape but is invoked by `DatabaseInitializer` rather than by `Program.cs`.

A new startup orchestrator is justified only when **all four** hold:

1. It must run **once per process, before the first request** — not per request, and not after the server binds.
2. Failure must be **fail-closed**: the correct response is refusing to serve, not logging and continuing.
3. It needs **scoped services outside a request scope** (a `DbContext`, a `UserManager`), which is what forces the `IServiceScopeFactory` shape in the first place.
4. Its claim is **genuinely distinct** from what an existing orchestrator already asserts.

Condition 4 is the one that keeps the count at two, and it is what this decision turned on: user provisioning fails it against `DatabaseInitializer` — "ready to serve" and "a login exists" are different claims — so it became a second call site rather than a fourth step. Anything failing 1 or 2 is configuration validation (eager, in `AddInfrastructure`) or middleware; anything failing 3 needs no orchestrator at all; anything failing 4 belongs *inside* an existing one, exactly as `IdentityRoleSeeder` sits inside `DatabaseInitializer`.

Checked against the remaining roadmap, nothing in Phases 5–15 qualifies: Phases 5–8 are request-path CQRS; Phase 9's email provider is a DI registration plus eager config validation; Phase 14's asset checks belong in an eager `Validate()`; Phase 15's rate limiting and CORS are middleware. Catalog reference data is explicitly *not* a seeding candidate — `PermissionMatrix.md` §6 makes `CatalogItem` Admin-managed and no document specifies starter content, so inventing some would be the speculative move §4 forbids.

**The rejection of an `await app.InitializeStartupAsync()` extraction is therefore conditional, not permanent.** At two orchestrators, naming both explicitly in `Program.cs` is clearer than hiding the two-step structure D63/D64 exist to make visible. A genuine third that satisfies all four conditions flips that judgement — three near-identical `using (var scope = …)` blocks are worth extracting — and would also deserve its own decision entry, because a third would mean this test is either being applied loosely or genuinely needs revising.

The first draft *did* fold it in, ordering user seeding after `Verify()` so that role existence was guaranteed by the step before. That crutch was removed: **`DevelopmentBootstrap` now checks its own precondition** (required roles present) and throws naming the missing role. The `Program.cs` ordering still means a database that fails verification is never reached, but nothing *relies* on it — which is what makes the separation real rather than cosmetic.

**The environment guard is a positive allowlist, deliberately stricter than D63's.** `Migrate` is refused with `!IsProduction()`; this is permitted only on `IsDevelopment()`. The asymmetry is the point: migrating a Staging database is recoverable, silently minting a known-credential Admin on any reachable non-development host is not. This is `CLAUDE.md` §22's fail-secure rule applied literally — the privileged outcome is reached only by positively establishing the condition — so a new environment name, a typo, or an unset `ASPNETCORE_ENVIRONMENT` all land on refusal. Enabled-but-not-permitted **throws**, never silently skips, for D63's own reason: a deployment that skipped would run while its operator believed the opposite.

**Three independent conditions must all hold:** `DevelopmentBootstrap:Enabled` is explicitly `true` (absent ⇒ `false`, matching `Database:Mode` ⇒ `Verify`); the environment is Development; and a password is present in configuration.

**Credentials come from configuration with no default, and User Secrets is the recommended source.** The reason is stronger than tidiness: `WebApplication.CreateBuilder` registers the user-secrets provider **only when the environment is Development**, so a credential stored there cannot reach a Production host at all — a second gate, independent of the guard above. `appsettings.Development.json` (gitignored) and environment variables remain supported, with standard precedence. Rejected: a constant in source (a committed credential whose only protection is the guard) and generating a random password and logging it (writes a live credential to every log sink, and idempotency means it is printed only on the run that created the account).

**Guard ordering is itself a decision.** `Enabled` is checked first and silently, because absent-or-false is the normal state of every environment including Production. The environment check comes next, and account validation only after it — so a Production host enabled without a password is told the feature is refused in Production, not asked to supply a credential it must never supply. Both orderings are pinned by tests.

**Create-only.** An existing account is left completely untouched: no password reset, no role re-assignment, no reactivation. Three reasons, in order of weight: a developer who deliberately deactivated the seeded Inspector to observe the rejected-login path must not have it silently reactivated by the next restart; a "repair" path is precisely the shape that turns dangerous if the guard ever fails, since the worst case of a guard bug becomes *resetting* an existing privileged account rather than adding one; and it keeps the contract identical to `IdentityRoleSeeder`'s. Accepted cost, documented in the README: repairing a broken development account means deleting the row, not restarting.

**Create-only means never *modified*, not never *inspected* — a distinction added during review.** `CreateAsync` and `AddToRoleAsync` are two operations with no transaction spanning them, so a fault or a process kill between them leaves an account with no role; every subsequent start then skips it as "already exists" and **succeeds**, leaving an account that logs in but is refused by every `[Authorize(Roles = ...)]` endpoint, over a startup log saying nothing is wrong. The fix is a **read-only** role-membership check on the already-exists path that logs a `Warning` naming the account, the expected role, and the remedy (delete the account). Reading is not mutating, so create-only is fully preserved; repairing automatically was rejected for the same reason a password reset was. The check is deliberately **not** applied to the two concurrent-creation paths, where another instance may simply not have reached its own `AddToRoleAsync` yet — there it would report a race in progress as a defect.

**Two accounts configured with the same address are rejected before anything is created — also added during review.** Left unchecked, create-only would create the first account, find the address taken for the second, leave it untouched exactly as designed, and log a benign-sounding message — silently producing one account holding only the first role. Validation names both colliding keys and compares addresses **case-insensitively**, matching Identity's upper-invariant `NormalizedEmail`: two addresses differing only in case are one account to `UserManager`, so an ordinal comparison would admit precisely the configuration the check exists to catch.

**Concurrency is handled exactly as D54/D55 handle roles**, because the hazard is identical in shape — `AspNetUsers` carries a unique index on `NormalizedUserName`, and check-then-create races under overlapped restarts. One fresh `IServiceScope` per account (so a failed `CreateAsync` cannot leave an entity tracked as `Added` and ride into the next account's `SaveChangesAsync` — the D55 bug verbatim), and both manifestations of the loss are tolerated: a graceful `IdentityResult.Failed` from Identity's own `UserValidator`, and a `DbUpdateException` from `SaveChangesAsync`. Per `CLAUDE.md` §14, the 10-instance concurrency test was re-run repeatedly, not trusted on one green.

**Two accounts, not one.** An Admin alone cannot exercise a single `S`-scoped path in `PermissionMatrix.md`, so the first thing anyone would do is hand-create an Inspector — the exact manual step this removes. Nothing further is provisioned: `Api.Tests`' deactivated, role-less and dual-role accounts exist because tests must *prove* edge behaviour, and every extra account is more privileged surface behind the same guard. Growth-on-demand (`CLAUDE.md` §4) applied to seed data. The **role is fixed in code per account, never read from configuration** — configuration choosing the role would turn a development convenience into a way to mint an arbitrary privileged account from a settings file.

**Consequences:** `RenoTrackApiFactory` sets `DevelopmentBootstrap:Enabled=false` **explicitly** rather than leaving it unconfigured. Its host is Development, which is exactly the environment where the API project's user secrets load — so a developer with bootstrap passwords set on their own machine would otherwise have accounts provisioned into the test database, and the suite would pass in CI's fresh clone while failing locally. Same reasoning as the JWT settings it already supplies explicitly. `RenoTrack.Api.csproj` gains a `UserSecretsId` (it had none, so `dotnet user-secrets` previously did nothing). `DatabaseInitializerTests` gains a regression pin asserting the initializer creates no users, so this responsibility cannot creep back.

---

## D65 — Public-Surface Rate Limiting: Fixed Window, 30/Minute per Client IP, `ForwardedHeaders` Deliberately Not Configured

**Context.** `Architecture.md` §12 requires "rate limiting / basic abuse protection on public endpoints (`/api/v1/public/...` and the contact form) to prevent scraping or brute-forcing token guesses", and `PROJECT_ROADMAP.md` assigns basic limiting of `/api/v1/public/*` to Phase 6. **A threat is documented; no limit, window, algorithm, partition key, queue behaviour or rejection shape is documented anywhere** — searched across all nine source documents. Every one of those is therefore a policy decision made here, not a requirement implemented.

**Decision.**

| Aspect | Choice |
|---|---|
| Partition key | **Client IP**, from the connection's `RemoteIpAddress` |
| Algorithm | **Fixed window** |
| Limit | **30 requests per 60 seconds** |
| Scope | **One shared policy** across all of `/api/v1/public/*`; GET and POST share the allowance |
| Queue | **None** — reject immediately |
| Rejection | **429 + RFC 7807 ProblemDetails**, with `Retry-After` |
| Application | Opt-in **named policy** via `[EnableRateLimiting]`, never a global limiter |

**Why per-IP and not the alternatives.** Partitioning **per token** was rejected outright: it does not address the documented threat at all, because every guessed token would open a fresh partition with a full allowance, so enumeration would be unlimited. A **global** limiter was rejected because one abusive client could then consume the entire allowance and deny service to every genuine customer. Per-IP is the only partition that makes token guessing expensive while leaving an unrelated customer unaffected.

**Why 30/minute.** A real customer opens one link and clicks one button; 30 requests in a minute is far beyond that and far below anything that makes enumerating a 256-bit secret worth attempting. The number is arbitrary within a wide band — that is precisely why it is recorded here and named in `PublicRateLimitOptions` rather than left as a literal in `Program.cs`.

**Why fixed window.** The requirement says "basic". A sliding window or token bucket would smooth the boundary burst a fixed window allows (up to 2× across a window edge), but nothing documents a need for that, and this project does not build for hypothetical load (`CLAUDE.md` §4 applied to middleware).

**`ForwardedHeaders` is deliberately NOT configured, and `X-Forwarded-For` is never read.** Correct configuration requires knowing which proxies are trusted, how many hops, and which networks — deployment facts not knowable in Phase 6. Guessing is worse than absence: a wrongly-trusted forwarded header lets any caller spoof a fresh partition per request and defeat the limiter completely, converting a security control into decoration. **Known and accepted consequence:** behind a reverse proxy, clients collapse into the proxy's address and share one bucket until trusted `ForwardedHeaders` is configured at deployment with real `KnownProxies`/`KnownNetworks`. Recorded as an operational prerequisite in `NEXT_STEPS.md`, not as a code gap.

**Compiled-in defaults, unlike `TokenLinkOptions`.** A token lifetime has no safe default — silently guessing "longer than intended" on a credential is dangerous, so absence must fail startup. A throttle's default *is* the policy, and a deployment expressing no opinion should get the policy rather than a startup failure. Configuration overrides it, which is also what lets tests exercise rejection without waiting out a real minute.

**Scope, stated precisely so §12 is not read as closed.** This covers `/api/v1/public/*` only. **`POST /api/v1/leads` — the contact form §12 names in the same sentence — remains unthrottled**, deferred by explicit decision since Phase 4 Slice 5 and still tracked in `NEXT_STEPS.md`. CORS likewise remains unconfigured. §12 is partially, not fully, satisfied.

**Consequences.** `PublicRateLimitOptions`/`PublicRateLimitPartition`/`RateLimitingRegistration` live in `RenoTrack.Api/RateLimiting/`. `UseRateLimiter()` sits after `UseRouting()` and after `RouteDiagnostics.Capture` — so a 429 on a token route is redacted exactly like every other response — and before `UseAuthentication()`, since the protected surface is anonymous and an abusive caller should not be able to make the server do authentication work either. Rejections go through `IProblemDetailsService`, so `CustomizeProblemDetails` adds `traceId` and the token-safe `instance`. Partitioning is proven by unit tests against real `HttpContext` instances, because `TestServer` supplies no `RemoteIpAddress`; the API tests state explicitly what they can and cannot prove rather than simulating the framework behaviour under test.

---

## D66 — Invoice Numbers Are Unique and Never Reused, But Not Gapless; Reserved as Late as Possible

**Phase 8, Slice 3.**

### The conflict, found while reconstructing the slice

`Architecture.md` §8 said invoice numbering "must never skip or reuse numbers". The mechanism this project actually has cannot deliver the first half of that. `NumberGeneratorService` reserves a number with a single `UPDATE … OUTPUT` statement that commits **independently** of the caller's unit of work — that independence is exactly what D52 chose, because EF Core cannot express atomic increment-and-return inside the caller's transaction. So if anything fails after the reservation and before the Invoice row commits, the number is consumed and never appears on any document: a gap.

§8 also attributed the requirement to **BR-5**. BR-5 is the mandatory §14 UStG *field list*; the numbering rule is **BR-9** ("An Invoice number, once issued, is never reused or reassigned — even if that Invoice is later Voided"). `StateMachine.md` §3.1 and §3.4 carried the same mis-citation and were corrected in Slice 1.

Read literally, **BR-9 requires uniqueness and non-reuse — not gaplessness.** The stricter claim existed only in §8's own prose.

### The decision

1. **Guarantee uniqueness and non-reuse.** Both hold absolutely: the sequence only ever increments, a voided Invoice keeps its row and number, and the unique index on `Invoices.InvoiceNumber` is the backstop. Proven by a 50-parallel-caller integration test on the invoice sequence specifically, not inherited from the Angebot one.
2. **Do not claim gaplessness.** `Architecture.md` §8 now states the real guarantee and names the accepted failure window.
3. **Reserve as late as practical.** `CreateInvoiceCommandHandler` takes the number only after every guard that can be evaluated beforehand has passed — the Project exists, the Project is not `Completed`, the originating Angebot exists, and the VAT allocation has been computed. Four Application tests assert `ReservationCount == 0` on each rejection path, so a future reordering that burns a number on an ordinary bad request fails visibly.

**Accepted failure window:** between the reservation statement committing and the Invoice's own `SaveChangesAsync` committing. In practice that is one insert. A gap can still occur if the database connection drops, the process dies, or the insert violates a constraint at that instant.

**No claim is made about German legal requirements.** The documents this project owns require non-reuse; whether the law additionally requires an unbroken sequence is not something this decision asserts in either direction. If it is confirmed as a requirement, it needs its own design — reserving at send time rather than creation, or a compensating reservation table — not a re-reading of this entry.

### Alternatives rejected

- **Reserve inside the caller's transaction.** D52 established that EF Core cannot express this atomically; achieving it would need raw SQL participating in an explicit transaction held open across the whole handler, taking a row lock on the sequence for the duration of unrelated work. It would narrow the window, not close it — a rollback still discards the number — while adding contention to every concurrent invoice creation.
- **Allocate the number after the commit and update the row.** Moves the gap into a worse place: an Invoice would briefly exist with no number, and a failure between the two writes leaves a permanently unnumbered legal document.
- **Detect and reuse gaps.** Directly forbidden by BR-9's "never reused or reassigned".
- **A gapless-at-read-time renumbering view.** Would make the number on a sent document differ from the number in the database — the one thing invoice numbering exists to prevent.
- **Leave §8's wording alone.** Rejected outright: a document asserting a guarantee the code does not provide is worse than no document, and this is a legally-adjacent claim.

## D67 — A Project's Completion Guard: Two Clauses, One Override, and a Reason That Lives Only in the Audit Log

**Date:** 2026-08-09 (Phase 8 Slice 6). **Status:** Accepted. **Supersedes nothing.**

### Context

`StateMachine.md` §5 assigns "a Project cannot silently become `Completed` with unpaid Invoices" to
`CompleteProjectCommand` by name, and SRS FR-8.6 grants an Admin override "with a reason". Building
it required answering three questions the documents either contradicted themselves on or never
asked.

**1. Which Invoice statuses block — contradicted three ways.** `StateMachine.md` §4.3's guard read
"All Invoices.Status == `Paid` (or `Void`)"; §3.4's invariant, four sections away in the *same
file*, read "while any of its Invoices are in `Sent` or `Overdue`"; `Sequence Diagram.md` §10's
`alt Any invoice not Paid` would have blocked on a `Void`, contradicting both. FR-8.6's "remain
unpaid" is ambiguous between all three.

**2. A Project with no Invoices at all — never asked.** "All Invoices are `Paid` or `Void`" is
vacuously true over an empty set, so the guard as written would let a Project that was never
invoiced close silently. FR-7.3's "once its final invoice has been paid" presupposes one exists.

**3. Where the override reason is stored — no column exists.** `ERD.md`'s `PROJECT` defines none.
§4.3 says the AuditLog entry records it.

### Decision

- **Blocking predicate:** a Project is blocked when it has **no Invoices at all**, **or** at least
  one Invoice is `Draft`, `Sent` or `Overdue`. `Paid` and `Void` never block. §4.3 wins over §3.4
  and over Sequence §10; both losing documents were **corrected**, and `StateMachine.md` §4.4 now
  records the reconciliation in full rather than leaving the winner to be inferred from the code.
- **The zero-Invoice clause is a new rule**, decided with the Product Owner, not a reading of an
  existing one. Such a Project is completable only through the override.
- **The override reaches the invoice precondition and nothing else.** `Project.Complete()`'s
  `Active`-only invariant lives inside the aggregate and no request field can reach it, so an
  `OnHold` or already-`Completed` Project is refused regardless of `forceOverride`.
- **Nothing is mutated until every precondition has passed, and the resulting error precedence is
  accepted.** The invoice predicate and the override rules are evaluated first; `Complete()` is
  called last. For a non-`Active` Project this means an invoice-derived refusal can be reported
  ahead of the Project's own state refusal — a blocking-Invoice 409 in one cell, and the 400
  "nothing to override" in another. **No combination of inputs completes a non-`Active` Project**;
  only the reported reason differs, and all eight cells are enumerated in tests.
- **An override with nothing to override is a 400**, thrown as FluentValidation's own
  `ValidationException` so both of the endpoint's 400s share one field-keyed body, and **no audit
  entry is written** for the refused attempt.
- **A reason without `forceOverride` is also a 400**, never silently dropped.
- **Every successful completion is audited** (`ProjectCompleted` against the Project); `details`
  carries the override reason and is `null` otherwise.
- **The AuditLog row is the reason's only storage.** No `Projects` column, no migration.

### Why

- **§4.3 over §3.4 because a `Draft` Invoice is money the company still intends to collect.**
  Closing a Project over one produces a bill nobody will ever chase, and the dashboard would show
  the Project settled while its own balance read still counted the draft toward `alreadyInvoiced`
  (Slice 3 excludes `Void` and nothing else). §3.4's narrower wording would have made those two
  screens disagree.
- **§4.3 over Sequence §10 because BR-9 makes `Void` a settled outcome, not an unpaid one.** A
  voided Invoice is explicitly excluded from remaining-balance math (§3.3); blocking on it would
  make voiding an Invoice permanently un-completable work.
- **The zero-Invoice clause exists because vacuous truth is not consent.** The likeliest way to
  reach it is forgetting to invoice at all, and a guard whose entire purpose is "do not close over
  unbilled work" should not be silent in exactly that case. It is not made impossible — the
  override is there, and it costs one typed sentence.
- **The 400 for an empty override protects the audit trail, which is the whole point of requiring a
  reason.** Accepting it would write "override: customer waived the final instalment" against a
  Project that was fully paid — a false justification, permanently, in the one record FR-8.6 exists
  to produce.
- **No `Projects.CompletionOverrideReason` column, deliberately, and this is the uncomfortable
  half of the decision.** `ERD.md` defines none, and §4.3 assigns the reason to the AuditLog — so
  following the documents means accepting **D50's best-effort semantics**: `AuditService` swallows
  its own failures, so a failed audit write loses the reason silently while the Project stays
  completed. This is close to the FR-6.3 rejection-reason problem, which was resolved *against*
  AuditLog storage — the difference is that no document assigned FR-6.3's reason anywhere, while a
  document assigns this one here. **Recorded as a known limitation in `NEXT_STEPS.md`, not solved
  by inventing schema.** Revisit trigger: any requirement that reads an override reason back, or
  any audit of completed Projects for compliance.

### Alternatives rejected

- **Following §3.4 (only `Sent`/`Overdue` block).** See above — it lets a Project close over an
  unsent bill and puts the completion screen at odds with the balance read.
- **Following Sequence §10 literally (`Void` blocks).** Contradicts BR-9 and §3.3, and makes a
  voided Invoice a permanent obstacle.
- **Letting a zero-Invoice Project complete normally.** Vacuous truth, in the one case the guard
  most needs to speak up.
- **Accepting a pointless override silently.** Puts a false reason in the permanent record.
- **A `Projects.CompletionOverrideReason` column + migration #9.** Durable, but it invents schema no
  document defines, on a table `ERD.md` specifies exactly. Recorded as the alternative to reach for
  if the D50 limitation ever bites.
- **Letting the override bypass the `Active` guard.** FR-8.6 is about Invoices; nothing suggests an
  override should reopen a closed Project or skip `Resume`.
- **Re-checking `Project.Status` in the handler before calling `Complete()`.** CLAUDE.md §6 forbids
  it — its only exception is ordering "for a non-Domain reason… never to duplicate a state check",
  and this would be exactly a duplicated state check.
- **A public `Project.EnsureCanComplete()` throwing precondition method**, so the state guard could
  run before the invoice predicate. §2 says "do not grow the Domain's public surface just to answer
  a question the aggregate's own mutator already answers by throwing" — and that sentence is
  general; the `IsEditable` parenthetical is an example, not the scope. Reading it as limited to
  *properties* in order to make this ordering implementable was considered and **rejected by the
  Product Owner**: a rule is not reinterpreted to fit an implementation. Note the codebase agrees
  independently — `ProjectTests.ExposesExactlyTheDocumentedTransitions` pins the aggregate's public
  mutating surface to `Complete`/`PutOnHold`/`Resume`, so such a method fails a Domain test.
- **Calling `Complete()` first as the state check, letting scope disposal discard the mutation when
  a later guard throws.** Implemented, then **rejected**: it uses a Domain state transition as a
  validation probe, and it makes correctness depend on the request-scoped `DbContext` never being
  committed between the mutation and the refusal — a property no guard enforces. (The comparable
  Phase 4 Slice 9 ordering is tolerated there because reordering would cost error quality on a
  double-submit; here a clean alternative existed.)
- **Amending §6 to permit a handler-level state check for guard precedence.** Strictly worse than
  amending §2: it puts a second copy of the `Active`-only rule in Application, which is the drift
  §6 exists to prevent.
- **`ArgumentException` for the empty-override 400.** Maps to 400 (D59) but produces a different
  body shape from the validator's own 400 on the same endpoint, for the same class of caller error.
- **A new Application exception type for it.** CLAUDE.md §17 adds one when a real scenario needs a
  *distinct* status; this one needs 400, which two existing types already produce.

## D68 — Email Transport Is SMTP via MailKit; OQ-3 Splits Into a Code Decision and a Deployment Decision

**Date:** 2026-08-09 (Phase 9 design review, before any implementation). **Status:** Accepted. **Supersedes nothing.**

### Context

SRS OQ-3 asked one question — "existing company mailbox via SMTP, or a transactional provider such as SendGrid/Postmark?" — and `PROJECT_ROADMAP.md` made it a hard prerequisite for Phase 9. It could not be answered as asked: **no company mailbox exists yet**, because the product is still being built, and the only address available belongs to the developer personally. Picking a provider to unblock development would have recorded a vendor choice on no evidence; using a personal address would have baked one person's mailbox into a product intended for a company.

The question turned out to contain two independent decisions with different owners and different deadlines.

### Decision

**OQ-3a — transport mechanism. Resolved: SMTP, via MailKit, behind the unchanged `IEmailSender`.** The dependency direction is Application → `IEmailSender` → Infrastructure → MailKit/SMTP. Application references no SMTP type, no MailKit type, no host and no credential; its package list stays FluentValidation + DI.Abstractions + Logging.Abstractions.

**OQ-3b — production mailbox and sender identity. Deferred to deployment, and blocking nothing.** SMTP host, port, security mode, username, password, sender address, sender display name, optional `Reply-To`, and the Admin notification recipients are per-deployment configuration. **No value is compiled in and none has a default**; an absent required value fails startup naming the exact key, matching `TokenLinkOptions`/`JwtOptions`. Secrets follow D64's precedent: `dotnet user-secrets` in Development (registered only in Development, so a credential cannot reach Production), environment variables in Production, never committed.

### Why

A company mailbox and a transactional provider's **SMTP relay** are the same implementation with different settings, so the vendor question moves out of code entirely. Only an API/SDK client would have made it sticky. That matters beyond convenience: the product is intended to be deployable for other companies later (each with its own mailbox), so a vendor recorded in code would be wrong for the second deployment and would have to be reopened.

Two further reasons specific to this codebase. First, **token secrecy**: `AngebotReadyNotification`/`InvoiceReadyNotification` carry the raw token that *is* the customer's credential, and this project has spent real effort keeping it out of logs and diagnostics (`CLAUDE.md` §22, `RouteDiagnostics`, `LoggingNoOpEmailSender`'s deliberate omission of the token). Routing every token through a third party's retained message store is a decision deserving an explicit processor agreement, not a library choice made in passing. Second, **testability**: MailKit's transport can be exercised over a real socket against an in-process listener, whereas an HTTP SDK would leave a boundary this project's no-mocking rule (`CLAUDE.md` §14) cannot exercise for real.

### Consequences

- Phase 9 can be built, tested and completed **without any real mailbox existing**, because no value is compiled in and non-production refuses to deliver by default.
- Deliverability, DNS/SPF/DKIM alignment, and any Microsoft 365 SMTP-AUTH/OAuth2 obstacle surface **at deployment, not during development**. This defers that risk rather than eliminating it, and is accepted knowingly.
- Switching to a transactional provider later is a configuration change, not a code change — which is exactly what `Architecture.md` §4's "swappable without touching business logic" claims and, until now, had not been tested by a real decision.

### Alternatives rejected

- **Answer OQ-3 as one question by choosing a provider now.** Would record a vendor decision on no evidence, and would be re-made per deployment anyway.
- **Choose an arbitrary company address to unblock development.** Invents a business fact; explicitly refused by the Product Owner.
- **Use the developer's personal address as a default.** Would place one person's mailbox into a product built for a company.
- **A provider HTTP API/SDK.** Its advantages — delivery telemetry, bounce webhooks, suppression lists — serve requirements no document states, at the cost of vendor lock-in in code and a third-party processor holding every token link.

---

## D69 — Notification Delivery State Is Persisted in One Infrastructure-Owned `NotificationDeliveries` Table, Written After the Business Commit

**Date:** 2026-08-09 (Phase 9 design review). **Status:** Accepted. **Supersedes nothing.**

### Context

Every one of the six `IEmailSender` call sites `await`s the sender **uncaught**, after `SaveChangesAsync` and after `auditService.LogAsync`. With the placeholder that is harmless — `LoggingNoOpEmailSender` cannot throw. With a real SMTP client it means a transport fault becomes **HTTP 500 on already-committed work**: the Angebot is sent, the token link exists, the audit row is written, and the caller is told the operation failed.

The Product Owner set two requirements beyond merely not failing: the failure must be **visible to an Admin**, and it must be **retryable**. That rules out logging alone, and for a concrete reason rather than a stylistic one — **two of the six senders are anonymous public endpoints** (`CreateLeadCommandHandler`, the website contact form; `RecordAngebotDecisionCommandHandler`, the customer's token-link decision). In those flows no Admin is present in the request, so a failure cannot be reported to the caller in any actionable way.

No existing durable surface can hold this. `AuditLog` is best-effort and swallows its own failures (D50), and `CLAUDE.md` §10 restricts audit to *business milestones* — a transport failure is not one.

### Decision

**The business operation and email delivery are separate concerns. A committed business operation is a success even when delivery fails**, on every flow including the anonymous ones. The API never converts one into the other.

**Delivery state is persisted in one Infrastructure-owned table, `NotificationDeliveries`**, with the lifecycle `Pending → Sending → Sent` or `Pending → Sending → Failed → (retry) → Sending → Sent`. The row is created **after** the business commit — *not* inside it.

The record must answer: which notification this is, which business object it belongs to, who the recipient was, its status, when it was created, when it was last attempted, when it was successfully sent, what the last failure was, and how many attempts have occurred. It must **not** store SMTP credentials, a rendered email body, or any customer/business data reloadable from the source aggregate.

**Ownership: Infrastructure, not Domain** — alongside `AuditLog` (D49), `NumberSequence` (D51) and `RefreshToken` (D60). No Domain `Notification` aggregate is created, and no notification state is added to an existing aggregate. The Domain stays unaware of email, SMTP, delivery attempts and retry state.

### Why

**Why persistence at all** — the requirement forces it. "An Admin can see the failure and retry it" is an in-product read and an in-product action; with two anonymous senders there is no caller to return it to, and no existing table may carry it.

**Why no payload is stored.** All six notifications are reconstructible from already-persisted state: Angebot-ready from `Angebot` + `TokenLink` (whose `Token` is stored raw, deliberately, because the public read looks it up), invoice-ready from `Invoice` + `Customer` + `TokenLink`, decision from `Angebot` + `Lead`, submitted-for-review from `Angebot`, new-lead from `Lead`, changes-requested from `Angebot` + `AngebotReviewComment`. The record therefore needs identity, not content — and storing no second copy of the token means this table adds no new class of credential exposure.

**Why Infrastructure-owned.** D49's test applied unchanged: no Domain aggregate has an invariant that references delivery state, no business rule branches on it, and no state transition depends on it. A `Notification` aggregate would put an operational concern inside the consistency boundary and would drag email semantics into a Domain that is deliberately transport-ignorant.

**Why written after the commit, not inside it.** Writing it inside the business transaction is the Outbox pattern (option C3 of the design review). That would change the transaction shape of six handlers and require a dispatcher to send — the first `IHostedService` in the codebase.

### Consequences

- **An accepted, documented crash window.** If the process dies between the business commit and the notification-row insert, the record is lost: the business operation stands, and nobody learns an email was owed. This is a deliberate trade-off at single-company scale, where email is a notification side effect rather than business evidence (`LoggingNoOpEmailSender`'s own doc comment makes the same distinction against BR-10). **Stated here rather than hidden.** It is the one thing an Outbox would fix, and the reason to revisit is a real incident, not principle.
- One new table and one migration (**#9**), whose exact columns are derived from this repository's conventions and reviewed before implementation.
- No message broker, no queue, no `BackgroundService`, no `IHostedService`, no dispatcher — none exists in `src/` today and none is added.

### Alternatives rejected

- **Direct send + catch-and-log only.** Satisfies "don't fail the business operation" and nothing else. A log line is not Admin-visible, and the two anonymous flows make that gap concrete rather than theoretical.
- **Outbox (row inside the business transaction).** Correct where a lost notification is a business failure; here it is not. Changes six handlers' transaction semantics and needs a dispatcher.
- **In-memory queue + background worker.** Would be the first hosted service in the codebase, and is *worse* than catch-and-log for durability — an in-memory queue loses everything on restart, invisibly. It solves latency, not the stated requirements.
- **Recording failures in `AuditLog`.** Forbidden twice over: D50 makes audit writes best-effort and swallowed, so business-visible data must never depend on them; and `CLAUDE.md` §10 reserves audit for business milestones.
- **Per-aggregate delivery columns** (e.g. `Angebote.NotificationFailed`). Six notification types would mean columns scattered across five tables, and would put an operational concern into Domain aggregates.

---

## D70 — Notification Retry Is Manual, Synchronous, and Retries Only the Notification

**Date:** 2026-08-09 (Phase 9 design review). **Status:** Accepted. **Supersedes nothing.**

### Decision

Retry is **manual only**, triggered by an Admin, executed **synchronously** within that Admin's API request. There is **no automatic retry, no background retry, no delay/backoff, and no maximum attempt count**.

A retry re-sends **only the notification**. It loads the persisted record, reconstructs the message from persisted business data, sends, and updates the status. **It never re-executes the underlying business operation** — retrying a decision notification must not re-record the decision; retrying an Angebot link must not re-send the Angebot as a business action.

### Why

The Admin is present and is the rate limiter, so backoff and attempt caps solve nothing a human clicking does not already solve. Automatic retry would need a scheduler this codebase deliberately does not have (D69). Reconstruction rather than replay is what makes "retry the notification, not the operation" structural rather than a matter of care: the retry path loads an aggregate and calls `IEmailSender`, and has no access to a command handler at all.

### Duplicate sends — explicitly acknowledged, not eliminated

- **Definite failure** (connection refused, authentication rejected): retry is safe.
- **Ambiguous failure** (e.g. an SMTP timeout after `DATA`): the message may already have been delivered, so a retry can produce a duplicate. **Accepted.** No message-ID deduplication or distributed idempotency infrastructure is introduced to eliminate this edge case.
- **Admin double-click**: prevented by the status transition `Failed → Sending → Sent`, not by any distributed mechanism. The exact concurrency mechanism is designed before implementation.

The harm profile is what makes this acceptable: every notification is **content-idempotent** — the same link, the same facts — so a duplicate is a second identical email, never a second business effect.

### Consequences

- No retention or archival policy is defined; notification rows remain until a real requirement exists. No 30/90-day deletion and no archival job is invented.
- A permanently-failing notification is retried by a human until it succeeds or the Admin stops. Nothing escalates it automatically.

---

## D71 — Admin Notification Recipients Are a Configured List, Deliberately Independent of the Identity Admin Role

**Date:** 2026-08-09 (Phase 9 design review). **Status:** Accepted. **Supersedes nothing.**

### Context

FR-9.2, FR-1.3, FR-6.5, SRS §2.2 and §2.3 all use the definite singular — "notify **the Admin**", "the Admin … owns the business relationship" — while `PermissionMatrix.md` opens with "There are two internal **roles** (Admin, Inspector)" and the Identity implementation places no limit on how many accounts hold the Admin role. **No document reconciles the two, and none states how a notification is addressed.** It was an open decision, not a settled rule to be inferred.

### Decision

Admin notifications go to a **configured list of company-level recipients** (e.g. `buero@firma.de`, `owner@firma.de`), supplied per deployment. Operational notifications are **not** sent to every Admin user's personal mailbox, and **no `IUserQueries` lookup is added for this purpose**.

The configured list is the **notification distribution policy**, deliberately independent of Dashboard authorization: who may *act* in the dashboard and who receives *operational mail* are different questions with different answers.

The **Inspector** recipient is unaffected and remains per-person — `AngebotChangesRequestedNotification` carries only `InspectorId`, so resolving it to an address needs a lookup regardless.

### Why

A single configured address satisfies FR-9.2's singular reading exactly; making it a **list** costs one line of validation and removes the most likely future change request ("also send to the owner"). Deriving recipients from the Admin role instead would invent a distribution policy from documentary silence, turn one notification into N sends and N notification rows, need a new interface method, and **fail open in the worst way — silently sending nothing when no active Admin exists.**

### Consequences

- Adding an Admin account does not subscribe that person to operational mail, and removing one does not unsubscribe them. The list is maintained as deployment configuration. **This is intended, and is the cost of keeping notification distribution independent of authorization.**
- The list is required configuration: absent or empty fails startup naming the key, since FR-9.2 would otherwise silently never fire.

---

## D72 — The Dashboard Is a Working Surface Built Around a Cockpit, Not a CRUD Skin Over the API

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Supersedes nothing.**

### Context

`PROJECT_ROADMAP.md`'s Phase 10 entry describes login, the Lead pipeline and the Inspection screens. `Architecture.md` §1 says something stronger: *"The dashboard is the primary deliverable."* A screen-per-endpoint dashboard satisfies the first sentence and not the second — it produces a competent table for every resource and no answer to "what should I do now", which is the question a Handwerksbetrieb's owner actually opens the application with.

### Decision

**`/cockpit` is the landing page**, not the Lead pipeline. It carries the headline figures, a queue of what is waiting on the signed-in user, the pipeline as a funnel, and the day's site visits. The Lead pipeline is one workspace among several, reached from the navigation like any other.

**Role changes priority, not product.** Verwaltung and Bauleitung see *the same screen with different content in the same slots*, never two dashboards. What differs is decided by `PermissionMatrix.md`, and it decides more than emphasis: §5 gives Bauleitung no Invoice permission, so their Cockpit **does not request** the money reads at all — a 403 avoided by not asking rather than handled after the fact.

**German is the primary language throughout** (with D79's runtime switch to English), because the users are a German company's office and site staff.

### Why

The commercial value SRS §1.1 claims — replacing a 3–4 hour manual Word/Excel job — is only visible if the Dashboard shows the state of the business and routes into the work. Every Cockpit tile and queue entry therefore navigates somewhere real, and an empty queue is omitted rather than rendered as a zero (D78).

### Consequences

- Two more read endpoints existed as permissions but not as routes before Phase 10 (D76), because a Cockpit needs cross-resource figures the per-Lead reads could not answer.
- The Lead pipeline lost its status as "the app", which is what makes room for Angebote, Rechnungen and Projekte to be first-class workspaces rather than sub-pages.

---

## D73 — The Access Token Lives in Memory; the Refresh Token Lives in `sessionStorage`; Exactly One Refresh Is Ever in Flight

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Supersedes nothing.**

### Context

D60 returns both tokens in the login response body, rotates refresh tokens on every use, and revokes the whole chain on reuse. Where the client keeps them was never decided, and there is deliberately no logout endpoint.

### Decision

- **The access token is held in memory only.** No script arriving later can read it back, and no restart resurrects it.
- **The refresh token is held in `sessionStorage`, never `localStorage`.** `localStorage` would leave a credential valid for `RefreshTokenDays` (default 7) readable by any injected script *and* surviving browser restarts — which would also make D60's client-side-only logout close to meaningless. `sessionStorage` dies with the tab.
- **Cookies were rejected for Phase 10**: they would change `AuthController`, add CSRF surface, and contradict D60's body-returned token. **No backend change was made for any of this.**
- **Concurrent 401s share one refresh** (`shareReplay({ refCount: false })`) and replay on success. **The refresh request is itself never retried** — presenting an already-revoked token *is* reuse, and reuse revokes the chain.

### Why

The shared in-flight refresh is not about protecting the server: `TokenService.RotateAsync` already treats a lost race as a race rather than as reuse. It exists so the losing 401 is not read by the user as "your session ended".

### Consequences

- Closing the tab ends the session. That is the intended trade, and it is what makes the absence of a logout endpoint acceptable.
- The role mapping (`Admin` → `admin`, anything else → `inspector`) **fails secure**, mirroring `LeadsController.RequestingInspectorId()`: the wider role is only ever reached by matching it explicitly.

---

## D74 — The Dashboard Calls Relative `/api/v1/...` Paths; the Port Gap Is a Development-Only Proxy

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Supersedes nothing.**

### Decision

Every call the Dashboard makes is a **relative path**. There is no configured API base URL, no environment file naming a host, and no build-time substitution. `proxy.conf.json` bridges `ng serve` (4200) and Kestrel (5294) **in development only**, and is not read by `ng build`.

### Why

`Architecture.md` §13 keeps same-origin hosting open — the Dashboard served by the API, or by a reverse proxy in front of it. Relative paths work unchanged under that arrangement, need no CORS configuration, and remove an entire class of "which environment am I pointed at" defect. A base-URL setting would have to be decided per deployment for a benefit no requirement asks for.

### Consequences

Cross-origin hosting would require a real decision (CORS policy, and where the origin is configured), rather than being reachable by editing a settings file. That is the intended cost.

---

## D75 — Angular Material Is the Component and Accessibility Foundation; Its Default Look Is Not Shipped

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Supersedes nothing.**

### Decision

`@angular/material` and `@angular/cdk` are dependencies and supply component behaviour and accessibility primitives (focus management, overlays, a11y utilities). **The visual layer is the project's own** — a token file (`styles/_tokens.scss`) and a base layer (`styles/_base.scss`) define the palette, the type ramp, the surfaces, the tables and the buttons, and Material's system tokens are pointed at ours so its components sit on our palette rather than introducing a second one.

**No second UI dependency is added without an explicit decision** — no chart library, no icon package, no component kit, no translation library, no date library. Where Phase 10 needed one of those it built the narrow thing it actually needed (the Cockpit's funnel and segmented bar; the typed i18n dictionary of D79).

### Why

A default Material dashboard looks like an admin template, and this is a commercial product for a company whose customers see its documents. Conversely, reimplementing focus trapping, overlays and keyboard semantics is exactly how an internal tool becomes unusable with a keyboard. Taking behaviour and refusing the look is the split that gets both.

### Consequences

Every new UI need is a build-or-adopt decision on the record, not an `npm install`. The one new-behaviour need in the second Phase 10 session (a modal) was met with the **native `<dialog>` element** plus `cdkTrapFocus` from the CDK already present — no new package.

---

## D76 — Phase 10's Backend Work Is Reads Only: New Query Endpoints Where a Documented Permission Had No Route, and No New Write Endpoint or Schema Change

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Supersedes nothing.**

### Context

Phases 4–9 built every command the workflow needs. Several **reads**, however, existed as permissions in `PermissionMatrix.md` with no endpoint to serve them: there was no cross-Lead Angebot list, no Invoice list at all (the only Invoice read in the system was the anonymous token-link page), no Project list, no Inspection schedule or single-Inspection read, and no way to list staff for an Inspector picker.

### Decision

Phase 10 adds **queries only**. Every screen is built on commands that already existed, and where a screen needed data no endpoint served, the gap was closed with a read that a documented permission already implied — never with a new write, never with a new column, and never with a migration. **The migration count is unchanged at nine.**

`IUserDirectoryQueries` is **Infrastructure-owned end to end**, applying D77's rule.

### Why

A UI phase that quietly grows the write surface is a UI phase that has stopped being reviewable against the documents. Holding the line at reads means every mutation the Dashboard performs is one whose rules were already agreed, tested and audited — and it is what makes "authorization is enforced by the backend, not by Angular" true rather than aspirational.

### Consequences

- Two capabilities the Dashboard could plausibly want do **not** exist and were not invented: a per-Lead Inspection list (so a new Angebot is created with `inspectionId: null` — D84) and an audit-log read (so no Activity Timeline is rendered — D85).
- `GET /api/v1/invoices/{id}` still does not exist. No document defines it, and the list plus the Project detail read cover every screen built.

---

## D77 — An Identity-Backed Read Is Infrastructure-Owned End to End, Like `INotificationDeliveryQueries`

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Extends D60's exception; applies D69's precedent.**

### Decision

`IUserDirectoryQueries` — the staff list behind the Inspector picker — lives in `RenoTrack.Infrastructure.Identity`, interface, DTO and implementation, and its controller consumes it directly rather than through the CQRS pipeline.

### Why

It is D60's own test applied a third time: no aggregate, no Domain invariant, no state transition, no audit milestone. The data lives in `AspNetUsers`, which is Identity's table and not part of this project's Domain at all. An Application-side query would exist purely so a layer with no business rules about user accounts could appear to own them.

**Do not "fix" this asymmetry with the other `*Queries` interfaces** — those return Domain-derived DTOs and correctly live in Application.

### Consequences

Because the reflection-driven registration safety net in `DependencyInjectionTests` only discovers types in the Application assembly, this registration is pinned by its own explicit resolution test — exactly as D69's is.

---

## D78 — The Cockpit Reports Exact Counts and Omits Empty Queues; It Never Computes a Figure the API Can Answer

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Supersedes nothing.**

### Decision

- **Every per-status count is a `pageSize=1` request read from `totalCount`**, never a client-side bucketing of a fetched page. A page-bucketed count is silently wrong past page one, and a cockpit's whole value is that its numbers are trustworthy.
- **An empty queue is not rendered as a zero.** A cockpit lists work; "0 overdue invoices" is the absence of work, not work.
- **No figure is invented.** The funnel attaches money only to the stage the API can actually attribute it to; the Angebote workspace labels its own sum as *this page's* value rather than presenting it as company quote volume; and the Angebot document reads its totals and VAT breakdown from the server rather than summing rows in the browser (BR-11's rounding is the Domain's).

### Why

Every one of these is the same rule seen three times: a number on a management screen is a claim, and a claim that is *nearly* right is worse than an absent one, because it will be acted on.

---

## D79 — Runtime Bilingual UI via a Typed Dictionary; German Is Primary and URLs Are Never Translated

**Date:** 2026-08-15 (Phase 10). **Status:** Accepted. **Supersedes nothing.**

### Decision

- **A typed dictionary (`de.ts` / `en.ts`), not Angular's `$localize`.** `$localize` compiles one bundle per locale and cannot switch at runtime, which is exactly what a `DE | EN` toggle in the header needs. A translation library would be a second UI dependency, which D75 forbids without an explicit decision.
- **German is the source of truth.** `Strings` is *derived* from the German dictionary, and `en.ts` is annotated with it — so **a German key with no English translation is a compile error**, not an English word appearing in a German UI.
- **URLs are never translated.** Route segments are German (`/angebote`, `/rechnungen`, `/besichtigungen`) in both languages: a URL is an address, not a string a user reads.
- **Date and time *patterns* live in the dictionary, not only the locale.** German writes `04.08.2026`, English `4 Aug 2026`; passing the active locale to a pipe translates the words but not the order or the separators.
- **Money is never in the dictionary.** The currency is always EUR and only its formatting changes with the language. The value is the same value.
- **A backend `ProblemDetails` `detail` is never rendered.** Server messages are English and phrased for an API caller; the Dashboard maps the *status code* (or a validation `errors` field key) onto its own wording.

### Consequences

A spec asserts that no English value is byte-identical to its German counterpart except on an explicit allowlist (language codes, format patterns, words German borrowed from English, and "Pos."), which is what catches a key copied across without being translated.

---

## D80 — The Angebot Builder and the Admin Review Are One Route; Capability Is Derived, Not Duplicated

**Date:** 2026-08-16 (Phase 10, commercial workflows). **Status:** Accepted. **Supersedes nothing.**

### Context

Wireframe D1 is the Inspector's builder; D3 is the Admin's review screen. D3 describes itself as *"the same read view as D1, but read-only for Admin"*.

### Decision

**One route, `/angebote/:id`, and one component.** What differs between the two wireframes is *capability*, and capability is a pure function of role and status in `angebot-capabilities.ts`, derived directly from `PermissionMatrix.md` §3–§4 and `StateMachine.md` §2.4.

### Why

Two components would mean two renderings of the same commercial document, each free to disagree with the other about what a subtotal is. Extracting the permission table instead makes it **exhaustively testable over every status** — which is where a permission rule actually goes wrong, and which a template scattered with `@if (role === …)` cannot be.

### Consequences

- `canEdit` is true only for Inspector in `Draft`/`ChangesRequested`; Admin is `R` on a draft and never edits one, so a change goes through Request Changes.
- `canSubmitForReview` is true only in `Draft`, because a `ChangesRequested` quote returns to `Draft` **by being edited** (`Angebot.EnsureEditable()`), and no reopen endpoint exists. The screen therefore *explains* that state rather than offering a disabled button (`awaitingRework`).
- This is presentation only. Deleting the file would make the screen ruder, not less secure.

---

## D81 — Every Angebot Mutation Re-Reads the Document; Command Responses Are Not Merged Into Client State

**Date:** 2026-08-16 (Phase 10, commercial workflows). **Status:** Accepted. **Supersedes nothing.**

### Decision

After any successful mutation the screen **re-reads `GET /api/v1/angebote/{id}`** and replaces its state wholesale. The response bodies — `AngebotDto`, `SectionDto`, `AddAngebotItemResult`, `AngebotSummaryDto` — are used only to know the call succeeded.

### Why

Those four shapes are all different, deliberately (CLAUDE.md §7), and **none carries the whole tree plus the VAT breakdown**. Merging partial shapes is how a screen begins to disagree with the server about what a document contains — and here the disagreement would be about money. Sequence Diagram §4 prescribes the same thing in its own words: totals are "recalculated on every change" by calling the API.

### Consequences

One extra round trip per edit, on a document a single user is building. That is the cheapest possible price for the totals on screen always being the totals in the database.

---

## D82 — The Invoice Write Workflow Lives on the Project Screen; the Invoice List Links to It

**Date:** 2026-08-16 (Phase 10, commercial workflows). **Status:** Accepted. **Supersedes nothing.**

### Context

Invoice creation is `POST /api/v1/projects/{projectId}/invoices` — Project-scoped. Send, mark-paid and void are id-scoped and could technically be driven from anywhere.

### Decision

**All four Invoice actions live on the Project detail screen** (Wireframes E1–E3). The Rechnungen workspace stays a receivables view and links each row to its Project.

### Why

BR-3's *remaining balance* — the one figure that tells an Admin what to invoice next — exists only in a Project's context. Creating an invoice from a flat list would mean choosing a Project first, with no balance in sight. And putting the three id-scoped actions in both places would be two implementations of one workflow, free to drift; the requirement was explicit that competing implementations are not wanted.

### Consequences

- The invoice list gains a link column rather than an action column.
- The Invoice action rules are extracted to `invoice-capabilities.ts` and tested exhaustively over every status, exactly as D80's are — including the assertion that Bauleitung, who may *read* a Project's invoices (§5 `R`), gets no action in any state.

---

## D83 — Actions With a Consequence Outside the Screen Are Confirmed; the Completion Override Is Offered Only After a Real Refusal

**Date:** 2026-08-16 (Phase 10, commercial workflows). **Status:** Accepted. **Supersedes nothing.**

### Decision

- **Submit for review, approve, send an Angebot, convert to a Project, send an Invoice, mark it paid, void it, and complete a Project each open a confirmation** naming what will happen. Adding a section or a line item does not — it is reversible from the same screen.
- **Every completed write raises a notice.** A screen that merely re-renders with different data tells the user that something is different, not that *their action happened*.
- **Project completion tries the ordinary call first.** The override dialog (FR-8.6's reason) opens **only** after the server has actually answered 409.

### Why

The confirmations track a real distinction: each confirmed action either emails a customer, burns a number, or moves a document out of its author's hands. The override rule is stricter than it looks — the API rejects `forceOverride` when nothing is blocking, precisely so a false justification cannot enter the audit trail (D67), so offering the override speculatively would be offering a 400.

---

## D84 — A New Angebot Is Created With `inspectionId: null`, Because No Per-Lead Inspection Read Exists and None Was Invented

**Date:** 2026-08-16 (Phase 10, commercial workflows). **Status:** Accepted. **Consistent with D76.**

### Context

`CreateAngebotRequest.InspectionId` is optional in both the request and the aggregate. The only Inspection reads are a **date-window** schedule and a single-Inspection read by id — neither answers "which Inspection belongs to this Lead".

### Decision

The Dashboard sends `inspectionId: null` when an Inspector starts a quote from a Lead.

### Why

The two alternatives were both worse than an honest null. Scanning a date window and guessing would attach the wrong site visit as readily as the right one, on a field whose entire purpose is traceability. Adding `GET /api/v1/leads/{id}/inspections` would be a new endpoint no document defines, in a phase that deliberately added no capability beyond serving a documented permission (D76).

### Consequences

`Angebot.InspectionId` is null for quotes created through the Dashboard. **Revisit trigger:** if the Inspection link is ever needed on a screen or a document, add the read endpoint deliberately and populate it — do not infer it.

---

## D85 — The Lead Detail Screen Renders No Activity Timeline

**Date:** 2026-08-16 (Phase 10, commercial workflows). **Status:** Accepted. **Consistent with D76.**

### Decision

Wireframe C1's "Activity Timeline (audit trail)" is **not** rendered. The Lead detail shows contact details, notes, and the real Angebot history for that Lead.

### Why

No audit-log read endpoint exists. `PermissionMatrix.md` §8 assigns the Audit Log to Admin and `PROJECT_ROADMAP.md` puts that screen in Phase 15. Rendering the section would require either inventing a read endpoint (D76) or assembling a plausible-looking history from the Lead's own fields — a fabricated audit trail, which is worse than an absent one on the one surface whose purpose is to be believed.

### Consequences

The gap is recorded here and in `NEXT_STEPS.md` rather than papered over. C1 is served in part, and the part that is missing is missing for a stated reason.

---

## D86 — Manual Lead Entry Is Its Own Admin-Only Route, and Its Source Is a Narrower Type Rather Than a Validated Field

**Date:** 2026-08-16 (Phase 10, operational completion). **Status:** Accepted. **Amends the depiction in Sequence Diagram §2.**

### Context

`PermissionMatrix.md` §1 grants "Create Lead manually (phone/email)" to Admin as `F`, and SRS FR-2.1 names the fields. `CreateLeadCommand` was built for both paths from the start — it carries `Source` and a nullable `CreatedByUserId`, and `CreateLeadRequest`'s own remarks anticipated the manual endpoint "when that endpoint is built". Only the API surface was missing.

**Sequence Diagram §2 depicts manual entry posting to `POST /api/v1/leads` under `[Authorize(Roles="Admin")]`.** That is not implementable: the same route and verb is the anonymous website contact form, and one action cannot be both `[AllowAnonymous]` and Admin-only.

### Decision

1. Manual entry is **`POST /api/v1/leads/manual`**, Admin-only. The anonymous contact form keeps `POST /api/v1/leads` unchanged.
2. Its request carries **`ManualLeadSource`**, an API-layer enum with exactly two members, `Phone` and `Email`.

### Why

**On the route split.** Serving both from one action would mean an `[AllowAnonymous]` action branching on `User.Identity.IsAuthenticated` to decide whether to trust a body-supplied `Source`. That puts a decision in a controller (CLAUDE.md §22), on precisely the field D61 identifies as a security boundary, behind the fail-closed default D57 exists to protect. A separate route keeps the anonymous surface exactly as small as it was.

**On the narrower enum.** `Source` gates the FR-9.2 Admin notification: `CreateLeadCommandHandler` sends it only when `Source == Website`. A manually-entered Lead must never be able to claim it arrived through a form nobody filled in. Expressing that as a type rather than a validator rule means no rule can be forgotten, no controller `if` is needed, and an invalid value fails at model binding as a 400 — CLAUDE.md §2's "structurally impossible to construct in an invalid state", applied at the API boundary.

### Consequences

`Sequence Diagram.md` §2 and `Architecture.md` §5.2 are corrected to name the real route. No notification is sent on this path, deliberately: an Admin typing a Lead in is already at the keyboard.

---

## D87 — `Lead.UpdateContactDetails` Covers Four Fields and Deliberately Excludes `Notes`

**Date:** 2026-08-16 (Phase 10, operational completion). **Status:** Accepted.

### Context

`PermissionMatrix.md` §1 grants "Edit Lead contact details" as Admin `F` / Inspector `S`, with the example of an Inspector correcting a wrong phone number found on-site. No Domain method and no endpoint existed. SRS defines no FR for editing a Lead at all, so §1 is the sole authority.

### Decision

`Lead.UpdateContactDetails(name, phone, email, address)` — four fields, replaced wholesale by a `PUT`. **`Notes` is not editable.** No `LeadStatus` guard applies.

### Why

§1 grants editing of *contact details*, and FR-2.1 lists `notes` separately from name/phone/email/address. Including it would widen a documented permission by assumption rather than evidence — the same discipline CLAUDE.md §2 applies to child-entity update methods, and the discipline whose absence caused the `AngebotItem.Remove` mistake recorded in that section.

No status guard, for the same reason `Lead.AssignInspector` has none: this is clerical correction, not a lifecycle transition. StateMachine.md defines no event for it and §1 places no timing restriction, so refusing it in a terminal state would invent a rule. A wrong phone number is worth fixing on a `Won` Lead too — the `Customer` record copied at conversion is unaffected either way.

### Consequences

Lead `Notes` remain writable only at creation. Recorded in `NEXT_STEPS.md` as a known gap rather than quietly closed. `PUT` semantics mean an omitted address clears it, which the screen states.

---

## D88 — Two Id Parameters Where a Command Is Both Scoped and Audited

**Date:** 2026-08-16 (Phase 10, operational completion). **Status:** Accepted. **Refines D61.**

### Context

`UpdateLeadContactDetailsCommand` is the first command whose permission is split `F`/`S` *and* which writes an audit entry. The established scoping shape (`GetLeadByIdQuery.RequestingInspectorId`) uses `null` to mean "Admin, unrestricted".

### Decision

Such a command carries **both** `RequestingInspectorId` (nullable — the scope) and `PerformedByUserId` (non-null — the actor).

### Why

They answer different questions and collapsing them breaks one. The scoping value's `null` is load-bearing: it is how Admin's `F` access is expressed, so it cannot also carry the Admin's identity. Deriving the audit actor from it would leave every Admin-made correction attributed to nobody — a silent hole in the trail, on exactly the surface Wireframe C1 exists to render. Both still come from the JWT and neither is accepted from the body, so D61 is unchanged; this only says which of the two a given parameter is.

---

## D89 — Reassignment Is Guarded by BR-10 on an Inspection and Unguarded on a Lead

**Date:** 2026-08-16 (Phase 10, operational completion). **Status:** Accepted.

### Context

`PermissionMatrix.md` §1 and §2 grant an Admin reassignment of a Lead and of an Inspection respectively. `Lead.AssignInspector` already existed, unguarded and unreachable; `Inspection` had no equivalent.

### Decision

`Inspection.Reassign(inspectorId)` is guarded by `EnsureNotCompleted` (BR-10). `Lead.AssignInspector` stays unguarded. Reassigning an Inspection also re-applies BR-13, moving the Lead's `AssignedInspectorId` in the same `SaveChangesAsync`.

### Why

The asymmetry is real, not an inconsistency. A Lead is an open-ended pipeline record that stays administratively editable for its whole life. A completed Inspection is explicitly immutable *evidence of who was on site and what they found* — rewriting the Inspector on a finished visit would falsify that record, which is the same reasoning that already stops notes and photos changing after completion.

BR-13 must follow the visit or the Lead keeps pointing at a colleague who is no longer going. That is not merely stale data: §1's pipeline is filtered server-side, so the outgoing Inspector would keep seeing the Lead while the incoming one could not.

---

## D90 — Project Hold and Resume Are Two Named Endpoints With No Reason Field

**Date:** 2026-08-16 (Phase 10, operational completion). **Status:** Accepted.

### Context

`PermissionMatrix.md` §5 grants "Put Project On Hold / Resume" to Admin. `Project.PutOnHold()`/`Resume()` existed with correct guards and were unreachable through the API. StateMachine.md §4.3's row notes "Reason optional/free text".

### Decision

`POST /api/v1/projects/{id}/hold` and `POST /api/v1/projects/{id}/resume`, Admin-only, **no request body**, audited as `ProjectPutOnHold`/`ProjectResumed`.

### Why

**Two named endpoints, not one status setter.** BR-7's reasoning for Leads applies here: this codebase names every transition, and Architecture.md §5.2 records the removal of `PATCH /leads/{id}/status` for exactly this reason. A status-setting endpoint would be the free-standing status edit the project has already refused once.

**No reason field**, despite §4.3's note. `ERD.md`'s `PROJECT` defines no column for one, and `Project.PutOnHold()` accepts none. Taking a reason would either discard it silently or force it into `AuditLog.details` as its only home — and D50 makes audit writes best-effort and swallowed, so that is not storage a caller can rely on. `CompleteProjectCommand`'s override reason lives there only because FR-8.6 makes it mandatory; nothing makes this one mandatory, so no storage is invented for it.

### Consequences

Pausing does not disturb invoicing: StateMachine §5 permits an Invoice against an `Active` *or* `OnHold` Project, and an API test pins it. A paused Project cannot be completed (§4.2 draws no such edge), which the screen explains rather than showing a disabled control.

---

## D91 — No Customers Workspace, and No Read Endpoint Invented for One

**Date:** 2026-08-16 (Phase 10, operational completion). **Status:** Accepted. **Consistent with D76.**

### Context

`Customer` is a real aggregate, created at Project conversion. A Customers workspace was considered as part of the operational scope.

### Decision

No Customers workspace and no `GET /api/v1/customers`. Customer identity surfaces where it is acted on — on the Project detail, the Invoice list, and the originating Lead.

### Why

`PermissionMatrix.md` has **no Customers section at all**, and SRS names no functional requirement for managing one — §3.7's only customer operation is FR-7.1's carry-over at conversion. The aggregate has **zero commands**: nothing in the system edits, retires or otherwise acts on a Customer after creation. A workspace would therefore be a read-only list existing because an entity exists, which is precisely what a workspace must not be, and it would require a new endpoint justified by nothing but its own screen.

### Consequences

Recorded so the absence reads as a decision rather than an oversight. **Revisit trigger:** the first documented requirement that lets someone *act* on a Customer.

---

## D92 — BR-10's Immutability Keeps a Named Escape Hatch: `Inspection.Reopen`

**Date:** 2026-08-16 (Phase 10, QA round 2). **Status:** Accepted. **Implements BR-10's own stated remedy; does not weaken it.**

### Context

QA found a completed site visit was a dead end: a typo in the notes, or a photo taken after the Inspector tapped "complete", could never be corrected. The screen said so plainly and that was the end of it.

The obvious reading is that BR-10 is too strict. It is not. Its changelog records it as **requested by the Product Owner** during Phase 1, and its rationale is real: a completed Inspection is the evidentiary basis the Angebot is built from (FR-3.4), so silent edits would blur exactly what evidence a quote rests on.

**BR-10's own text already names the way out:** "Any future need to change a completed Inspection's record requires a distinct, explicit action (e.g. a 'reopen' use case), not implicit editing."

### Decision

`Inspection.Reopen()` clears `CompletedAt`, and `POST /api/v1/inspections/{id}/reopen` exposes it to the **assigned Inspector only**. `AddPhoto`, `UpdateNotes` and `Reassign` still refuse outright while the visit is complete — nothing about the lock changed. Reopening is audited as `InspectionReopened`.

### Why this shape

**Inspector, not Admin.** `PermissionMatrix.md` §2 marks photos, notes and completion Inspector `S` / Admin `—`, deliberately, so the evidence chain-of-custody points at whoever was on site. The action that *re-enables* those edits belongs to the same person.

**The Lead does not rewind.** It stays at `InspectionDone`: the visit did happen, and any Angebot already built on it remains valid. Reopening corrects evidence; it does not reverse the pipeline.

**The completion is not erased.** Clearing `CompletedAt` is what unlocks the aggregate, but the audit trail is what preserves the history — it reads `InspectionDone → InspectionReopened → InspectionDone`. The field is the lock; the log is the record.

### Consequences

`BusinessRules.md` BR-10 now records that the anticipated action exists. A visit can be completed, corrected and completed again — which is what made D93 necessary.

---

## D93 — Completing a Reopened Visit Must Not Re-Drive the Lead Transition

**Date:** 2026-08-23 (Phase 10, browser QA). **Status:** Accepted. **Fixes a defect introduced by D92.**

### Context

D92 made a completed visit reopenable. Browser QA then found it could not be **closed again**: `POST /inspections/{id}/complete` returned 409 on the second attempt, leaving the visit permanently open and defeating the entire point of reopening.

`CompleteInspectionCommandHandler` drove `Lead.MarkInspectionDone()` unconditionally, and that transition runs only from `InspectionScheduled`. After the first completion the Lead has already moved, so the second call threw.

**Every test suite passed throughout.** The Domain test exercises the aggregate alone, which is perfectly happy to complete twice. The defect lived in the cross-aggregate step, which no unit test touched and no API test covered.

### Decision

The Lead transition fires only when the Lead has **not already moved past the visit**, expressed as an explicit status set: `InspectionDone`, `AngebotInProgress`, `AngebotSent`, `Won`, `Lost`.

### Why this shape

**An explicit set, not an ordinal comparison.** `Lost` sits last in `LeadStatus` but is a terminal branch, not a later stage — "greater than `InspectionScheduled`" would be true for the wrong reason.

**A Lead that has *not* reached the visit is deliberately absent from the set**, so `MarkInspectionDone()` still runs and still throws for it. That combination means the Inspection and its Lead genuinely disagree, which BR-13 makes unreachable in production and which should fail loudly rather than be skipped. An existing test pins exactly that case.

**This is CLAUDE.md §6's sanctioned "an `if` that decides which side effect to trigger", not a duplicated guard.** `Inspection.Complete()` still runs unconditionally and still refuses a second completion on its own; what is conditional is the *Lead* transition, a pipeline milestone that happens once.

### Consequences

Verified in the browser with the Lead at `Won` — well past the visit — where complete, reopen and complete again all succeed and the Lead does not move. Three Application-layer tests pin it, which is the layer the bug was actually in.

---

## D94 — `SubmitForReview` Accepts `ChangesRequested` as Well as `Draft`

**Date:** 2026-08-16 (Phase 10, QA round 1). **Status:** Accepted. **Amends StateMachine.md §2.3.**

### Context

`StateMachine.md` §2.3 routed a returned quote back to `Draft` only as an implicit side effect of editing it — "Inspector edits items — no explicit state event" — and `SubmitForReview` guarded on `Draft` alone.

That left a dead end QA hit immediately: an Inspector who read the Admin's comment and concluded **nothing needed changing** had no way to send the quote back. The UI offered no Submit button and the aggregate would have refused one. The only workaround was to add and delete a section purely to trip `EnsureEditable()` — a state change made solely to satisfy a guard.

### Decision

`Angebot.SubmitForReview()` accepts `Draft` **or** `ChangesRequested`. `StateMachine.md` §2.3 gains the `ChangesRequested → InReview` row.

### Why

Nothing is weakened. The "at least one section with at least one item" guard still applies, and §2.4 already treats `ChangesRequested` as an editable state — so a caller who may edit may equally resubmit. The rejected alternative, a separate reopen event on Angebot, would add a transition the documents do not define in order to reach a state the Inspector is already in.

### Consequences

The exhaustive state-machine test now pins a transition with **two** legal source states, which is why its helper takes a set rather than a single status. `angebot-capabilities.ts` mirrors it, so the screen and the aggregate still agree.

---

## D95 — `CatalogItems.CreatedFromAngebotItemId` Is `SetNull`, Not `Restrict`

**Date:** 2026-08-16 (Phase 10, QA round 1). **Status:** Accepted. **Migration #10 `RelaxCatalogItemOriginFkToSetNull`.**

### Context

Removing a draft line that had been contributed to the Catalog (FR-4.10) failed with a `DbUpdateException` on `FK_CatalogItems_AngebotItems_CreatedFromAngebotItemId`, surfacing to the user as an unmapped **500**.

That contradicted CLAUDE.md §2, which states plainly that sections and items in a `Draft`/`ChangesRequested` Angebot are unsent working material and may be removed — the edit-lock is what separates them from records.

### Decision

The relationship is `DeleteBehavior.SetNull`. The line is removed, the Catalog entry survives, and its `CreatedFromAngebotItemId` becomes `NULL`.

### Why

`CreatedFromAngebotItemId` is a **provenance trace** (BR-8) and nothing branches on it, so it must never be able to veto an unrelated operation. BR-12 keeps the *Catalog entry* alive; it says nothing about the draft line that entry was copied from, which BR-8's copy-on-create semantics make independent the instant it exists. A `NULL` is the honest record of an entry whose origin no longer exists.

**Cascade was never a candidate** — it would delete a shared library entry as collateral damage of a draft edit (BR-12). **Restrict's only other alternative** was refusing the removal, which invents a rule no document states.

### Consequences

Four LocalDB integration tests pin it against the real constraint, including that the surviving entry keeps its title and is not retired. This is the only schema change in Phase 10; the migration count moves from nine to **ten**.

---

## D96 — `TokenLinks.UsedAt` Is an Optimistic-Concurrency Token, and `UnitOfWork` Translates the Loss

**Date:** 2026-09-04 (Phase 11, Slice 1). **Status:** Accepted. **Migration #11 `AddTokenLinkConcurrencyToken` (empty `Up`/`Down` — snapshot only).**

### Context

BR-4 says a token link is single-use for decisions, and `TokenLink.MarkUsed()` enforces it — but only against the state that one `DbContext` happens to have loaded. `RecordAngebotDecisionCommandHandler` reads the link, checks `UsedAt`, mutates three aggregates and commits them in one `SaveChangesAsync`. Nothing made that read-then-write atomic across requests.

So two simultaneous decisions on the same link — a customer double-clicking, or answering from two tabs — both read `UsedAt` as null, both passed the in-memory guard, and **both committed**. `TokenLinks` had no concurrency token; only `RefreshToken.RevokedAt` did.

The visible damage was not merely a duplicate: `Angebot` and `Lead` carry no concurrency token either, and the two transactions wrote them independently. An Approve racing a Reject could therefore leave `Angebot = CustomerApproved` with `Lead = Lost` — the one pair of rows StateMachine.md §5 exists to keep in agreement — plus two audit rows and two Admin notification emails for a decision the customer made once.

This was found by reading the code during the Phase 11 assessment, not by a failing test. Every existing test drove the decision endpoint sequentially, where `MarkUsed()`'s guard is sufficient.

### Decision

**`TokenLinks.UsedAt` is an EF Core optimistic-concurrency token.** The decision `UPDATE` becomes `UPDATE TokenLinks SET UsedAt = @now WHERE Id = @id AND UsedAt IS NULL`. The loser matches no row, EF Core throws `DbUpdateConcurrencyException`, and **its entire batch rolls back** — so the `Angebot` and `Lead` writes it had queued never land either.

**`UnitOfWork.SaveChangesAsync` catches `DbUpdateConcurrencyException` and rethrows `ConflictException`**, which the API already maps to 409. `ConflictException` gained an inner-exception overload so the real cause survives for D59's Warning-with-stack-trace logging.

### Why this shape

**A token on one column protects all three aggregates**, because EF Core wraps the batch in a transaction — so `Angebot` and `Lead` need no tokens of their own, and none was added. The token link is the gate for this path; nothing else can reach those two transitions (StateMachine.md §5).

**`UsedAt` is the natural token because it is the only column anything ever mutates on this table** — `MarkUsed()` is the aggregate's one mutator. There is no cost anywhere else, and no `rowversion` column, and therefore **no schema change**: a non-`rowversion` concurrency token is client-side `WHERE`-clause behaviour. This is exactly the shape `RefreshToken.RevokedAt` already uses (D60).

**The translation had to live in `UnitOfWork`, not in the handler.** `DbUpdateConcurrencyException` is an EF Core type and `RenoTrack.Application` references FluentValidation and two Microsoft.Extensions abstractions packages and nothing else (CLAUDE.md §22) — a handler cannot name the type it would need to catch. Infrastructure referencing Application is the permitted direction, so Infrastructure is the only layer that can name both sides. This is D60's boundary — business rules in Application, mechanisms in Infrastructure — applied to a failure mode rather than to a service.

**The translation is blanket rather than token-specific.** A lost optimistic-concurrency race always means the same thing and 409 is always the honest answer, so there is nothing for a per-entity branch to decide — and any concurrency token added later is mapped correctly the day it is added, instead of surfacing as an unmapped 500 until someone notices. **Only `DbUpdateConcurrencyException` is caught, never its `DbUpdateException` base:** a unique-index or foreign-key violation is a defect, not a conflict a caller should be invited to retry, and must keep surfacing as a 500 with its stack trace intact.

**The message names nothing.** The first caller to reach this path is the anonymous public decision endpoint, where the loser is a real customer holding a token link, and a mapped exception's message becomes the ProblemDetails `detail` (D59). No row id, table, timestamp or token appears in it.

**No new arm was added to `ProblemDetailsExceptionHandler`.** Mapping `DbUpdateConcurrencyException` there would have pushed a persistence mechanism into the presentation layer's mapping table — the very thing §22 already records as an accepted *risk* for `ArgumentException`/`InvalidOperationException` rather than as a pattern to extend.

**`TokenService` is unaffected.** It saves through `DbContext` directly, never through `IUnitOfWork`, and already catches `DbUpdateConcurrencyException` itself for refresh-token reuse detection. The authentication path's behaviour is unchanged.

### Consequences

**The 409 is deterministic whichever way two decisions interleave**, and that is the property the tests assert rather than which guard fired. A genuine race loses at the database and surfaces as `ConflictException`; a serialized second request reloads a consumed link and loses to `MarkUsed()`'s `InvalidOperationException`. Both map to 409, so the caller-visible contract is identical — which is also why these tests have no flaky branch to tolerate.

Ten tests pin it: five repeated LocalDB races on the aggregate, four on `UnitOfWork`'s translation (including that the message discloses nothing and that a constraint violation is *not* translated), and three repeated end-to-end races through the real HTTP endpoint with the two requests deliberately disagreeing. **Repetition is not decoration** — D55 is the precedent: a race proof that passed when written then failed about two runs in three once it was actually repeated.

**Migration #11 has empty `Up`/`Down` and that is the correct product of the model.** The column, its type and its nullability are unchanged; the migration exists only because `RenoTrackDbContextModelSnapshot` records `.IsConcurrencyToken()`, and without it `dotnet ef migrations has-pending-model-changes` would report a pending change forever. It must still be applied, because startup verification compares migration history in both directions (D63). The migration count moves from ten to **eleven**.

**Known, deliberately unfixed in this slice:** the *sequential* second decision still surfaces `MarkUsed()`'s own message, which names the token link's row id and the first use's timestamp. That is a pre-existing disclosure on the anonymous surface, narrower than a credential but wider than it needs to be. It is recorded in `NEXT_STEPS.md` rather than fixed here, because changing a Domain guard's message is not what this slice is for.

---

## D97 — The Customer Page Is Server-Rendered by `RenoTrack.Website`, and the API's Rate Limiter Finally Gets a Trust Boundary

**Date:** 2026-09-04 (Phase 11, Slice 2). **Status:** Accepted. **No migration.**

### Context

`EmailMessageFactory.CreateAngebotReady` has been composing `{TokenLink:PublicBaseUrl}/angebot/{token}` since Phase 6 (D4.1), and `GET /api/v1/public/angebote/{token}` has answered it since then. Nothing served that URL: `RenoTrack.Website` was still the untouched Razor scaffold. Slice 2 builds the page.

Two questions had to be settled before it could exist: **how the page reaches the API**, and **what that does to D65's rate limiter**.

### Decision

**The page is server-rendered Razor Pages in `RenoTrack.Website`, calling the API through a typed `HttpClient` on the server.** No browser-JS-to-API flow, no SPA, no script on the page at all. The boundary is one interface, `IPublicAngebotClient`, and the Website references no backend project — it mirrors the API's JSON contract in its own types.

**`X-Forwarded-For` is now honoured on both sides, but only from an explicit allowlist.** `TrustedForwardersOptions` (one copy in each application) clears ASP.NET Core's pre-trusted loopback defaults and adds only what `TrustedForwarders:KnownProxies`/`KnownNetworks` name. **Empty is the default and means trust nothing**, in which case `UseForwardedHeaders` is not registered at all and behaviour is byte-for-byte what it was before D97. The Website sends the *connection's* address on each outgoing API call, never a header the customer supplied.

### Why this shape

**Server-side rendering keeps the token out of the browser's script context entirely**, needs no CORS surface (Architecture.md §12 records CORS as still unconfigured), and never discloses the API's origin to the customer. A browser-side flow would have required all three, on the one surface in this system any anonymous holder of a forwarded email can reach.

**Server-side rendering is also what forced the trust boundary.** D65 partitions `/api/v1/public/*` per client IP and deliberately never read `X-Forwarded-For`, because believing a forwarded header with no known proxy trust boundary lets any caller mint a fresh partition per request and defeat the limiter entirely. That was right, and Architecture.md §12 recorded the consequence as a deployment prerequisite. Once the Website calls the API on the customer's behalf, it stops being a prerequisite and becomes a live defect: **every customer would share one 30-per-minute bucket**, so one busy afternoon — or one abusive visitor — throttles everybody else's quote. D97 supplies the half D65 declined to invent, and only that half: a named forwarder, or nothing.

**Fail-closed when unconfigured is the whole design.** A trust list that silently defaults to trusting something is worse than none, because it looks configured. For the same reason a malformed entry **fails startup naming the key** rather than being skipped — a typo that quietly shrinks the list yields a limiter that appears to work and does not. This is the stance `LeadsController.RequestingInspectorId()` takes on role scope and `DevelopmentBootstrap` takes on provisioning: reach the permissive branch only by positively establishing it.

**A non-canonical network is refused, and that guard is ours because the framework's is documented but absent.** `System.Net.IPNetwork`'s type remark says "the constructor and the parsing methods will throw in case there are non-zero bits after the prefix", and its constructor declares an `ArgumentException` for exactly that. **Neither is true in the shipped implementation** — it calls `ClearNonZeroBitsAfterNetworkPrefix` and normalises silently, so `TryParse("10.0.0.1/8")` returns `true` with `BaseAddress` `10.0.0.0`. Established by reading `dotnet/runtime` at tag **`v10.0.11`** (the version CI installs), after an earlier revision of this decision asserted the documented rejection and a test proved it never happens.

Left alone, that is a **silent widening**: an operator who wrote a host address with a network prefix would trust all 16,777,216 addresses of `10.0.0.0/8` rather than the one host they meant. D97's principle is that a trust list must mean exactly what it says, and silently *widening* is the same failure as silently shrinking with a worse blast radius — so `TrustedForwardersOptions` compares the parsed `BaseAddress` against the address the operator supplied and **fails startup**, naming the key, the range that would have been trusted, and `KnownProxies` as the right home for a single host. `KnownProxies` itself is unchanged: exact-match, no CIDR, no normalisation.

**The containment semantics are pinned by tests, not by having read the source once.** `ForwardedHeadersMiddleware.CheckKnownAddress` iterates `KnownIPNetworks` calling `Contains`, so `10.0.0.0/8` trusts `10.0.0.1`, `10.0.0.50` and `10.255.255.255` and refuses `11.0.0.1` and `9.255.255.255` — asserted directly, along with loopback not being trusted despite the framework defaulting to it.

**Nothing the API says reaches the customer.** `CustomerAngebotResult` carries one of four outcomes and never a status code, a ProblemDetails `detail`, an exception message or an internal id. Every mapped exception in the API is authored for an API caller in English and may name an aggregate or an id (D59). This is `CLAUDE.md` §23's Dashboard rule — map the outcome, never render the backend's `detail` — and it matters more here, because the audience is not staff.

**An outage is not a bad link.** `Unavailable` is a distinct outcome from `NotFound` and answers **503**, not 404. Telling a customer their link is invalid when the API is merely unreachable sends them away permanently over a transient fault. Equally, a 200 whose body does not match the contract is reported as an outage, not as a missing quote — the quote may well exist.

**The strict response headers are keyed on a route parameter named `token`**, exactly as `RouteDiagnostics` keys the API's redaction. It covers Slice 4's `{token}/entscheidung` and a future invoice route without anyone maintaining a list of paths. `Referrer-Policy: no-referrer` is not hygiene here: without it every outbound click hands the full token URL to whatever the customer clicked.

**Two framework log sources had to be silenced, and one of them was found in review rather than by design.** `Microsoft.AspNetCore` stays at `Warning` because ASP.NET's hosting diagnostics log every request line at Information, and on `/angebot/{token}` the request line *is* the credential — `appsettings.json` now says so rather than leaving it looking like tidiness. The second was worse: **`IHttpClientFactory` attaches its own logging handlers** that write `Sending HTTP request GET {uri}` at Information, and every URI this Website requests contains the token. Those handlers log under `System.Net.Http.HttpClient.*`, *outside* the `Microsoft.AspNetCore` category, so the existing setting did not cover them and the `Default` level of Information would have written a live credential to every sink on every page view. It is removed **structurally**, with `RemoveAllLoggers()` on the client builder, so no configuration change can reintroduce it; the category is also pinned to `Warning` as defence in depth. **The general rule this produces: when a URL is a credential, enumerate every framework component that logs a URL, not just the obvious one.**

### Consequences

`Architecture.md` §12's "deployment prerequisite, not a code gap" bullet is superseded: the code half exists, and the deployment half is now naming the forwarder rather than accepting a degraded limiter. **A deployment that names none is unchanged, not broken.**

**`TrustedForwardersOptions` is deliberately duplicated** between `RenoTrack.Api` and `RenoTrack.Website` rather than shared. The Website references no backend project (`CLAUDE.md` §1), and inventing a shared library to hold two short lists would breach that boundary to save a few lines.

**Three defects in this decision's own implementation were found by CI rather than by review, and all three are recorded because each is a trap anyone would fall into.** The first was mechanical: `ForwardedHeadersOptions.KnownNetworks` is deprecated in .NET 10 in favour of `KnownIPNetworks`, and `TreatWarningsAsErrors` turned it into four build errors. The second was not: the `"//"` comment convention this codebase uses throughout `appsettings.json` is **only safe in a section bound with `.Get<T>()`**, which ignores unknown keys. `Logging:LogLevel` is enumerated as category-to-level pairs, so every key under it must parse as a `LogLevel` — a comment key there throws at `builder.Build()` and **the Website would not have started in any environment**. The notes now live in a sibling key outside `Logging`. The third was the canonicality claim above: this decision originally stated that `IPNetwork.TryParse` rejects a non-canonical network, which is what the framework documents and not what it does. All three were invisible to inspection and are exactly what a green-CI gate exists to catch — and the third is the one worth remembering, because the wrong belief came from trusting a framework's written contract over its implementation on a security boundary.

`RenoTrack.Website.Tests` is the fifth test project and joins CI's **Linux** job, because it deliberately needs no database — the API is stubbed at the `IPublicAngebotClient` boundary. That is a real gain: the customer-facing surface stays verifiable on any OS, unlike the LocalDB-bound suites (D40, D56).

**Not decided here, and deliberately still open:** the rendered quote (Slice 3), the decision buttons and rejection reason (Slices 4–5), link re-issue (Slice 6), and the company identity and legal pages (Slice 7 — `CompanyIdentityOptions` is the structure, and no value is invented).

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
| Adding `RenoTrack.Infrastructure` reference to `RenoTrack.Api.Tests` instead of a new test project | D40 | Would make `Api.Tests` depend on Infrastructure before `Api` itself does — backwards dependency direction |
| Keeping `ERD.md`'s `Subtotal`/`LineTotal`/`DecisionResult` columns and adding them to the schema | D41 | Would resurrect settled Phase 1 decisions (computed-only properties, D16) without new evidence |
| Building `LocalDiskFileStorage` for real in Phase 3 | D42 | `PROJECT_ROADMAP.md`'s Phase 4 deliverable list explicitly owns it; `CLAUDE.md`'s "(Phase 3)" was a stale forward-reference |
| `AuditLog` as a rich Domain entity (private constructor, static factory, self-guards) | D49 | No business invariant references it anywhere; ceremony without purpose for a type with nothing to protect |
| Letting `AuditService.LogAsync` exceptions propagate to the caller | D50 | Would report an already-committed business operation as failed; audit is best-effort instrumentation, not a correctness invariant |
| Read-then-write sequence increment via plain EF Core (`SELECT`, increment in memory, `SaveChangesAsync`) | D52 | Two round trips with an in-memory gap between them is a real concurrent-duplicate race; EF Core has no single-statement atomic increment-and-return |
| EF Core concurrency token + retry loop for the sequence increment | D52 | Still two-plus round trips per attempt, with no sane place for the retry loop given `INumberGeneratorService`'s existing signature; a single atomic statement needs no retry at all in the common case |
| `AddIdentity<TUser,TRole>()` for API/JWT authentication | D54 | Wires cookie-authentication-scheme defaults the API never uses — dead config at best, a confusing footgun at worst |
| Leaving the role-seeding concurrent-startup race unmitigated, citing v1's single-instance deployment | D54 | Cheap to fix properly; the "single instance" assumption is fragile against deploy topology changes and restart-overlap windows |
| `DbContext` parameter on `SeedRolesAsync`, manual `Entry(role).State = Detached` | D55 | Leaks an EF-specific type into an Identity-domain utility; manages the symptom instead of removing the shared state that causes it |
| `IServiceScopeFactory` as a `SeedRolesAsync` method parameter (not constructor-injected) | D55 | Pushes scope-creation concerns onto every caller; inconsistent with every other Infrastructure service taking dependencies via constructor |
| `IDbContextFactory<RenoTrackDbContext>` instead of `IServiceScopeFactory` | D55 | Supplies a fresh `DbContext` but not a correctly-wired `RoleManager` — would force hand-constructing Identity's object graph, duplicating `AddIdentityCore()`'s own registration |
| EF Core InMemory/SQLite for `RenoTrack.Infrastructure.Tests` in CI only | D56 | Would let CI pass without exercising the real constraints/precision D40 exists to verify — reintroduces the exact gap D40 closed |
| Running the entire CI workflow on `windows-latest` | D56 | Slower/costlier for 297 tests with no LocalDB dependency and nothing to gain from a Windows runner |
| `Asp.Versioning.Mvc` (or any versioning library) for `/api/v1` | D57 | Version-negotiation infrastructure for a second version that doesn't exist — speculative abstraction |
| Header/media-type API versioning | D57 | Contradicts `Architecture.md` §5.2's own literal `/api/v1/...` endpoint table |
| Hand-rolled fake `IUserStore`/in-memory Identity for `Api.Tests` | D58 | A second Identity mechanism existing only in tests; login is meaningless without real password hashing |
| `EnsureCreatedAsync()` for `RenoTrack.Api.Tests`' schema | D58 | Never writes `__EFMigrationsHistory`, so it breaks if startup-time migration is chosen later; also leaves migrations executed in only one test |
| Re-verifying business rules over HTTP in `Api.Tests` | D58 | Domain/Application tests already cover them exhaustively; duplication would be costly and would drift |
| Making `Program` public to enable `WebApplicationFactory<Program>` | D58 | Widens the public surface for one test project; `InternalsVisibleTo` to one named assembly matches the D7 precedent |
| A health/ping endpoint added solely to give Slice 1's smoke test something to call | D58 | Inventing an undocumented endpoint to serve a test; the OpenAPI document is already-intended behavior and serves the same purpose |
| One `IExceptionHandler` per exception type, chained | D59 | Registration order silently determines behavior and the mapping scatters across six files — the "hidden pipeline" property D22 rejected MediatR for |
| Leaving `ArgumentException`/`InvalidOperationException` unmapped (→500) | D59 | Plainly wrong for the 24 Domain guards that exist today; a BR-10 violation is a client error, not a server fault |
| Dedicated Domain exception types to make the mapping unambiguous | D59 | Reopens `CLAUDE.md` §17's "no base exception type" and modifies the stable Domain baseline, on a so-far-hypothetical risk |
| Mapping `InvalidOperationException` by originating assembly (`ex.TargetSite`) | D59 | Reflective, degrades silently when `TargetSite` is null, and forces a Domain project reference into `RenoTrack.Api` purely for an assembly comparison |
| Echoing an unmapped exception's message as ProblemDetails `detail` | D59 | An unexpected `SqlException` would surface connection strings or schema names to the client |
| Handling `OperationCanceledException` in Slice 2 | D59 | Hosting/runtime concern, not Domain/Application exception mapping; address with real evidence of log noise, not speculation |
| `LoginCommand` + `IIdentityService` to route authentication through CQRS | D60 | Authentication has no aggregate, invariant, transition, or audit milestone — an abstraction existing purely to preserve cosmetic uniformity |
| No refresh tokens (longer-lived access token instead) | D60 | Contradicts Architecture §7.1, and a long-lived bearer token cannot be revoked at all |
| Stateless refresh tokens (a second signed JWT) | D60 | Cannot be revoked, which defeats most of the reason to have a refresh token rather than a longer access token |
| Storing refresh-token plaintext | D60 | A database read would yield usable credentials; same reasoning that forbids plaintext passwords |
| A logout endpoint in Slice 4 | D60 | Revocation is a capability the model enables, but no requirement documents logout — speculative until one does |
| A background cleanup job for expired refresh tokens | D60 | Steady state is a few hundred rows; building it now solves a non-problem. Revisit at tens of thousands of rows |
| Distinguishing "unknown email" from "wrong password" in the 401 | D60 | Turns login into an account-enumeration oracle; the real reason is logged server-side instead |
| Default `ClockSkew` (5 minutes) on token validation | D60 | Would keep a 15-minute access token usable for twenty, silently overriding the configured lifetime |
| Binding `CreateLeadCommand` directly as the action parameter | D61 | Lets an anonymous caller set `CreatedByUserId` and `Source` — the latter gates the Admin notification (FR-9.2), so controlling it suppresses that notification |
| Binding the command directly and overwriting sensitive fields after binding | D61 | The fields still appear as inputs in the OpenAPI document, and "bind then ignore" is a convention a future edit can quietly break |
| Serializing enums as ordinals (the System.Text.Json default) | D61 | Reordering an enum would silently change the wire contract's meaning for every existing client |
| De-duplicating identical contact-form submissions | D61 | An invented business rule that discards a genuine second enquiry; a duplicate row can be closed, a swallowed enquiry is a lost customer |
| Rate limiting the public Lead endpoint in Slice 5 | Slice 5 review | Architecture §12 requires it, but it is public-endpoint hardening infrastructure, not this slice's purpose — deferred to a dedicated hardening slice once the public endpoints exist |
| Letting the `AspNetUsers` FK be the only check on a scheduled Inspector | D62 | Surfaces as an unmapped 500 for a mistyped id, and cannot catch an Admin or a deactivated account as assignee |
| Mapping `DbUpdateException` FK violations to 400 in the middleware | D62 | Fixes the status code only, still permits invalid assignees, and needs fragile provider-specific error-number sniffing |
| Splitting `IsActiveInspectorAsync` into separate exists/role/active checks | D62 | Every caller wants the same conjunction; splitting invites checking two of three and permitting the case the third would catch |
| Taking `InspectorId` from the JWT when scheduling | D61 correction | It is the assignment target chosen by an Admin, not the caller's identity — deriving it would make it impossible to schedule anyone but oneself |
| Validating that `ScheduledAt` is in the future | Slice 7 review | No document requires it, and back-dating a visit already carried out is plausible; it would need a numbered `BusinessRules.md` rule first |
| An Admin-driven `MarkWon`/`MarkLost` endpoint (`PATCH /api/v1/leads/{id}/status`) | Slice 10 review | `AngebotSent` is unreachable (`Lead.MarkAngebotSent()` is called by nothing), so it would 409 for every Lead; and StateMachine §5 states Lead reaches `Won` only inside the Angebot decision handler's transaction — an Admin path would be a second route to a decision BR-4 makes tamper-proof |
| `Database.MigrateAsync()` at application startup | D63 | Requires the runtime login to hold DDL permission permanently, applies schema changes on unplanned restarts with no review or backup window, and races across instances — which App Service does on every overlapped-restart deploy |
| A separate migrator process / init-container | D63 | Same DDL-permission profile as startup migration without the convenience; earns its keep only with orchestration this project does not use |
| A warning instead of a hard refusal for `Migrate` in Production | D63 | A setting that logs disapproval and then does the dangerous thing anyway provides none of the least-privilege benefit motivating the policy |
| A `None`/`Skip` database initialization mode | D63 | Every environment that might want one is better served by `Verify`, which is already read-only; it would exist only to disable the check that catches the failure the decision exists to prevent |
| Distributed locking for concurrent Development `Migrate` | D63 | Production never migrates at startup, so it is architecture for a case that does not exist |
| Adding development user provisioning as a fourth step of `DatabaseInitializer` | D64 | Makes the component whose entire justification is a read-only, least-privilege Production posture also own the one operation that mints a privileged credential |
| A separate console tool for seeding development accounts | D64 | Strongest isolation and the **recorded escalation path**, but a new project, second composition root and CI wiring for a phase that needs speed — revisit on a real production deployment |
| `!IsProduction()` for the development-bootstrap environment guard | D64 | Hands accounts to Staging, a typo'd environment name, or an unset `ASPNETCORE_ENVIRONMENT` — the fail-open shape CLAUDE.md §22 forbids |
| A compiled-in default development password | D64 | A committed credential whose only protection is the guard; the design's third condition exists so no such value is present to leak |
| Generating a random development password and logging it | D64 | Writes a live credential to every log sink, and create-only means it is printed on one run and unrecoverable after |
| Updating (repairing) an existing development account on re-run | D64 | Silently undoes a deliberately changed password or deactivation, and makes a guard failure able to reset an existing privileged account rather than merely add one |
| Reading each development account's role from configuration | D64 | Turns a development convenience into a way to mint an arbitrary privileged account from a settings file |
| A `MigrateAndSeedUsers` value on `DatabaseInitializationMode` | D64 | Conflates two of the three concerns D63 keeps separate, and would combinatorially require `VerifyAndSeedUsers` too |
| Leaving `DevelopmentBootstrap:Enabled` unconfigured in `RenoTrackApiFactory` | D64 | Its host is Development, where the API project's user secrets load — a developer's own passwords would provision accounts into the test database, passing in CI and failing locally |
| Extracting both startup steps into `await app.InitializeStartupAsync()` | D64 | **Conditional, not permanent.** At two orchestrators it hides the two-step structure D63/D64 exist to make visible, for a two-line saving. Revisit if a genuine third satisfies D64's four-condition test |
| Duplicating the environment guard at the `Program.cs` call site | D64 | Two guards that can drift, and it makes the component *look* unguarded — the authoritative check belongs where it cannot be bypassed by a second caller |
| An `IDevelopmentBootstrap` interface | D64 | One implementor, one caller, no test double — an abstraction for symmetry, which §9 and D28 reject by name |
| Registering `DevelopmentBootstrap` only when `IsDevelopment()` | D64 | Moves a runtime guard into composition, makes the guard untestable against a Production host, and turns a misconfiguration into "service not registered" instead of the message explaining the policy |
| Per-token rate-limit partitioning | D65 | Every guessed token opens a fresh partition with a full allowance, so it does not address the token-guessing threat §12 names at all |
| A single global rate-limit partition | D65 | One abusive client could consume the whole allowance and deny service to every genuine customer |
| Configuring `ForwardedHeaders` in Phase 6 to get the real client IP | D65 | Requires trust-boundary facts (which proxies, how many hops, which networks) not knowable yet; a wrongly-trusted forwarded header lets any caller spoof a fresh partition per request and defeats the limiter entirely — worse than the known proxy-collapse limitation |
| Reading `X-Forwarded-For` manually without a configured trust boundary | D65 | Same defeat, with none of the framework's validation |
| Sliding-window or token-bucket limiting | D65 | The requirement says "basic"; no documented evidence that boundary bursts matter |
| A global rate limiter instead of an opt-in named policy | D65 | Internal and authenticated routes would inherit the public allowance silently; tightening it would look like a Dashboard outage |
| Extending Phase 6's limiter to `POST /api/v1/leads` | Slice 4 review | Phase 6's approved scope is the token-link surface; the contact form stays tracked separately in `NEXT_STEPS.md` rather than being folded in without review |
| Faking `RemoteIpAddress` in `WebApplicationFactory` to test per-IP partitioning | Slice 4 review | Would simulate the framework behaviour under test and prove nothing; partitioning is tested at unit level against real `HttpContext` instances instead |
| Accepting an FR-6.3 rejection reason and discarding it | Slice 4 (approved earlier) | If the API accepts a value, users may reasonably expect it preserved; storing it is an open ADR, so the honest contract is not to accept one |
| Storing the FR-6.3 rejection reason in `AuditLog.Details` | Slice 4 (approved earlier) | Audit is best-effort by D50 and swallows its own failures — business data must never depend on it |
| Reusing `AngebotReviewComment` for the customer's rejection reason | Slice 4 (approved earlier) | `AdminUserId` is a required FK to `AspNetUsers` and the type is documented as the *internal* review loop; a customer's words would be misattributed as staff review and surface in the Inspector's comment history |
| A `Project.Customer` navigation property so EF could fix up the FK in one `SaveChanges` | D48 amendment | Breaks CLAUDE.md §2's by-id-only aggregate separation and drags Customer's graph into every Project load |
| Weakening `Project.Create`'s `customerId > 0` guard | D48 amendment | Would silently persist `CustomerId = 0` and fail later at the FK as an unmapped 500; the guard is what made the defect immediate |
| Two un-transacted `SaveChanges` for Customer then Project | D48 amendment | Orphans a Customer that `Customers.LeadId UK` then makes un-retryable without manual cleanup |
| Compensating deletion of the Customer after a failed Project write | D48 amendment | Compensation is not atomicity (§22), and it leaves the crash-between-steps hole open — a rollback has neither problem |
| Client-generated keys for `Customer.Id` | D48 amendment | Changes the PK strategy for one table of nine against `ERD.md`'s `int Id PK` convention, and reopens an already-committed migration |
| `ExecuteInTransactionAsync<T>(Func<Task<T>>, …)` on `IUnitOfWork` | D48 amendment | Hides the boundary inside a lambda; the transaction boundary is part of the use case and belongs in the handler where it can be read |
| An explicit `RollbackAsync` on `IUnitOfWorkTransaction` | D48 amendment | Disposing an uncommitted transaction already rolls back (verified against LocalDB), so `await using` covers every escape path; a second way to do one thing |
| Opening a transaction on the reuse-existing-Customer path for symmetry | D48 amendment | One `SaveChangesAsync` is already atomic through EF's implicit transaction — it would take a lock for nothing |
| Matching Customers by email, phone, name or address during conversion | Phase 7 Slice 3 | A customer-identity policy no document specifies; getting it wrong merges strangers or splits a genuine repeat customer. `ERD.md` records the `LeadId UK` consequence as a known limitation instead |
| Refreshing an existing Customer's copied details from the Lead at conversion | Phase 7 Slice 3 | Would let an unrelated Lead edit rewrite the party an earlier Project was agreed with — the drift BR-8 forbids for `AngebotItem`; no document asks for a refresh |
| Blocking an Invoice that exceeds the Project's agreed total | D66 / Phase 8 Slice 3 | BR-3 says the system "warns (does not hard-block)"; a 409 or validator maximum would convert a documented warning into a prohibition |
| Clamping `Remaining` at zero on the invoice-balance read | D66 / Phase 8 Slice 3 | The negative value *is* BR-3's warning — flooring it deletes the only signal the rule asks the system to produce |
| An `isOverInvoiced`/`warning` field on the balance DTO | Phase 8 Slice 3 | Sequence Diagram §8 defines three figures; a flag would invent a contract no document specifies, and the number already carries the information |
| Excluding `Draft` invoices from `AlreadyInvoiced` | Phase 8 Slice 3 | StateMachine §3.3 excludes `Void` and nothing else |
| Reserving the invoice number before the Project/Angebot guards | D66 | A reservation is irreversible (D52), so a number taken before an ordinary bad request is a number burned for nothing |
| Reserving the invoice number inside the caller's transaction | D66 | D52 established EF Core cannot express it; would hold a sequence row lock across unrelated work and still not close the gap |
| Inventing a VAT rate for a positive Invoice against a zero-gross Angebot | Phase 8 Slice 3 | No proportion exists to allocate by; assuming 0%, picking among zero-valued groups, or blending would fabricate a legally relevant figure. Rejected with a 409 instead, kept as narrow as the arithmetic problem |
| Rejecting a **zero-gross** Invoice against a zero-gross Angebot | Phase 8 Slice 3 | It needs no proportion, so the arithmetic problem does not arise — widening the rule would invalidate a Project the documents allow |
| A blended effective VAT rate derived from the Angebot's totals | Phase 8 Slice 3 | Collapses a legally mixed-rate document (BR-6) into a rate appearing on no document |
| Promoting the residual-cent rule into `BusinessRules.md` or an ADR of its own | Phase 8 Slice 3 | Deterministic rounding machinery, not business policy; no requirement specifies which rate group absorbs a cent, and the per-rate detail is neither stored nor returned |
| An explicit transaction in `CreateInvoiceCommandHandler` | Phase 8 Slice 3 | One insert is already atomic under EF Core's implicit transaction; D48's amendment exists for genuine multi-save identity problems, not for symmetry |
| An `IOwnershipValidator` call on Invoice creation or the balance read | Phase 8 Slice 3 | `PermissionMatrix.md` §5 marks them `F` and `R` respectively, never `S` — an ownership check would be a semantic error (CLAUDE.md §16) |
| A `GET /api/v1/invoices/{id}` invented so 201 could carry a `Location` | Phase 8 Slice 3 | No document defines an invoice read endpoint; `POST /leads/{id}/inspections` already returns 201 without one |
| Following StateMachine §3.4's narrower guard (only `Sent`/`Overdue` block) | D67 / Phase 8 Slice 6 | Lets a Project close over an unsent `Draft` — money still intended for collection — and puts the completion screen at odds with the balance read, which counts `Draft` toward `alreadyInvoiced` |
| Following Sequence §10 literally, so a `Void` Invoice blocks completion | D67 / Phase 8 Slice 6 | Contradicts BR-9 and StateMachine §3.3, which make `Void` a settled outcome excluded from balance math; voiding would become permanently un-completable work |
| Letting a Project with no Invoices at all complete normally | D67 / Phase 8 Slice 6 | "All Invoices are Paid or Void" is vacuously true over an empty set, so the guard would stay silent in the one case it most needs to speak — forgetting to invoice. Reachable through the override instead |
| Accepting `forceOverride` when nothing is blocking | D67 / Phase 8 Slice 6 | Writes a false justification permanently into the one record FR-8.6 exists to produce. Refused with 400, and no audit entry at all |
| Accepting a reason without `forceOverride` and ignoring it | D67 / Phase 8 Slice 6 | The same accept-and-discard pattern Phase 6 refused for the FR-6.3 rejection reason — a caller would believe they had recorded something that went nowhere |
| Letting the override bypass `Project.Complete()`'s `Active`-only guard | D67 / Phase 8 Slice 6 | FR-8.6 is about Invoices; nothing suggests an override should reopen a `Completed` Project or skip `Resume` on an `OnHold` one |
| Re-checking `Project.Status` in `CompleteProjectCommandHandler` | D67 / Phase 8 Slice 6 | CLAUDE.md §6 forbids a handler duplicating an aggregate's state check, and it would create a second place for the `Active`-only rule to drift |
| A `Projects.CompletionOverrideReason` column and migration #9 | D67 / Phase 8 Slice 6 | Invents schema on a table `ERD.md` specifies exactly, when §4.3 already assigns the reason to the AuditLog. The D50 best-effort consequence is recorded as a known limitation instead — and this is the alternative to reach for if it ever bites |
| `ArgumentException` for the empty-override 400 | D67 / Phase 8 Slice 6 | Maps to 400 (D59) but yields a different body shape from the validator's own 400 on the same endpoint, for the same class of caller error |
| A new Application exception type for the empty-override case | D67 / Phase 8 Slice 6 | CLAUDE.md §17 adds one when a scenario needs a *distinct* status; this needs 400, which two existing types already produce |
| Loading every Invoice of a Project to evaluate the completion guard | Phase 8 Slice 6 | The handler needs one boolean, not five aggregates with their `Payments` graphs; CLAUDE.md §4 wants the repository to answer the business question, and it does so with two indexed existence probes |
| A second SQL `SUM` for `alreadyInvoiced` on the Project detail read | Phase 8 Slice 6 | The invoice rows are already fetched and carry the same `GrossAmount`; a third round trip would recompute what is in hand, and would hit the value-converted-`Money`-inside-an-aggregate constraint the balance read documents |
| Hiding `Void` Invoices from the Project detail list | Phase 8 Slice 6 | BR-9 keeps a voided Invoice as a numbered, visible record; a list that dropped one would make the gap in the numbering look like a deleted document. Excluded from the figures only |
| Leaving the Project detail invoice list unordered | Phase 8 Slice 6 | Two identical requests could present rows in different orders; `IssueDate` alone is not unique, so the primary key is the tiebreaker (the same reasoning CLAUDE.md §22 applies to paged reads) |
| Choosing a transactional email provider (SendGrid/Postmark) for v1 | D68 / Phase 9 design | Records a vendor decision on no evidence, would be re-made per deployment, and puts every token-link credential in a third party's retained message store; an SMTP relay gives most of the benefit as pure configuration |
| An arbitrary or personal email address as a production default | D68 / Phase 9 design | Invents a business fact and bakes one person's mailbox into a product built for a company; explicitly refused by the Product Owner |
| Outbox (notification row written inside the business transaction) | D69 / Phase 9 design | Changes the transaction shape of six handlers and needs a dispatcher; email here is a notification side effect, not business evidence, so the crash window is cheaper than the machinery |
| In-memory queue + `BackgroundService` for email | D69 / Phase 9 design | Would be the first hosted service in `src/`, and is *worse* than catch-and-log for durability — an in-memory queue loses everything on restart, invisibly |
| Recording delivery failures in `AuditLog` | D69 / Phase 9 design | D50 makes audit writes best-effort and swallowed, so Admin-visible data must never depend on them; CLAUDE.md §10 also reserves audit for business milestones |
| A Domain `Notification` aggregate, or delivery columns on existing aggregates | D69 / Phase 9 design | No Domain invariant references delivery state (D49's test); it would drag transport semantics into a deliberately transport-ignorant Domain |
| Deriving Admin notification recipients from the Identity Admin role | D71 / Phase 9 design | Invents a distribution policy from documentary silence, needs a new lookup, fans one notification into N, and fails open by silently sending nothing when no active Admin exists |
| Automatic retry, backoff, or an attempt cap for failed notifications | D70 / Phase 9 design | The Admin triggering the retry is the rate limiter; automatic retry needs a scheduler the architecture deliberately does not have |
| Message-ID deduplication to eliminate ambiguous-failure duplicates | D70 / Phase 9 design | Every notification is content-idempotent, so a duplicate is a second identical email, never a second business effect |
| `localStorage` for the refresh token | D73 | Leaves a week-long credential readable by any injected script and surviving browser restarts, and makes the client-side-only logout close to meaningless |
| Cookie-based auth for the Dashboard | D73 | Would change `AuthController`, add CSRF surface, and contradict D60's body-returned token — a backend change for a phase that made none |
| Retrying the refresh request itself after a failure | D73 | Presenting an already-revoked token *is* reuse, and reuse revokes the entire chain |
| A configured API base URL / per-environment host setting | D74 | Relative paths already work under §13's same-origin option, need no CORS, and remove the "which environment am I pointed at" class of defect |
| A chart library, icon package, component kit or i18n library | D75 | Each is a second UI dependency for a need the project met with a narrow, owned implementation; adopting one is a decision on the record, not an `npm install` |
| Angular's `$localize` for the bilingual UI | D79 | Compiles one bundle per locale and cannot switch at runtime, which is exactly what the header toggle needs |
| Translating route segments into English | D79 | A URL is an address, not prose; two URL vocabularies would make every link language-dependent |
| Rendering the backend's `ProblemDetails` `detail` to the user | D79 / CLAUDE.md §23 | Server messages are English and phrased for an API caller — correct for a developer, useless to a Handwerksbetrieb's office |
| Client-side bucketing of a fetched page to produce Cockpit counts | D78 | Silently under-counts past page one, on the one screen whose value is that its numbers can be trusted |
| Showing an empty decision queue as a zero | D78 | A cockpit lists work; the absence of work is not work |
| Summing line items in the browser to render Angebot totals | D78 / D81 | Produces a second answer to a question with an authoritative one, and BR-11's rounding is the Domain's, not JavaScript's |
| Two components for Wireframes D1 and D3 | D80 | D3 *is* D1's read view; two renderings of one commercial document are free to disagree about what a subtotal is |
| A disabled "Submit" button on a `ChangesRequested` quote | D80 | Editing is what reopens the draft (`EnsureEditable`), and there is no reopen endpoint — the state needs an explanation, not a dead control |
| Merging command responses into the Angebot screen's state | D81 | The four response shapes differ deliberately and none carries the tree plus the VAT breakdown; merging them makes the screen disagree with the server about money |
| Invoice actions on the Rechnungen list as well as the Project screen | D82 | Two implementations of one workflow, free to drift — and creating an invoice needs BR-3's remaining balance, which exists only on a Project |
| Offering the project-completion override before the server refuses | D83 | The API rejects `forceOverride` when nothing is blocking (D67), so a speculative offer is an offer of a 400 |
| Guessing a Lead's Inspection from the date-window schedule read | D84 | Attaches the wrong site visit as readily as the right one, on a field whose whole purpose is traceability |
| Adding `GET /api/v1/leads/{id}/inspections` to populate `inspectionId` | D84 | A new endpoint no document defines, in a phase that deliberately added no capability beyond serving a documented permission (D76) |
| Rendering Wireframe C1's Activity Timeline from the Lead's own fields | D85 | A fabricated audit trail on the one surface whose entire purpose is to be believed; the real read is Phase 15 |
| Serving manual Lead entry from the anonymous `POST /api/v1/leads` | D86 | One route cannot be both `[AllowAnonymous]` and Admin-only; the alternative is a controller branching on `IsAuthenticated` to decide whether to trust the one field that gates a notification |
| A validator rule forbidding `Source = Website` on manual entry | D86 | A rule can be forgotten; a type with no such member cannot. Model binding rejects it before any handler runs |
| Letting the Lead contact edit also write `Notes` | D87 | §1 grants "contact details" and FR-2.1 lists notes separately — widening a documented permission by assumption is the mistake CLAUDE.md §2 already records once |
| A `LeadStatus` guard on contact correction | D87 | Clerical correction is not a lifecycle transition; StateMachine defines no event and §1 no timing restriction, so a guard would invent a rule |
| Deriving the audit actor from the nullable scoping id | D88 | `null` means "Admin, unrestricted" — reusing it for identity attributes every Admin-made change to nobody |
| Guarding `Lead.AssignInspector` with a status check, for symmetry with `Inspection.Reassign` | D89 | A Lead stays administratively editable for life; only the Inspection is immutable evidence, and only BR-10 says so |
| Leaving the Lead's `AssignedInspectorId` behind when an Inspection is reassigned | D89 | §1's pipeline is filtered server-side, so the outgoing Inspector keeps seeing the Lead while the incoming one cannot — a scoping bug, not stale data |
| One `PATCH /projects/{id}` taking the target status | D90 | The free-standing status edit BR-7 forbids, and the one Architecture §5.2 already removed for Leads |
| Accepting a hold reason because StateMachine §4.3 mentions one | D90 | No ERD column stores it; the only home would be a best-effort audit row (D50), which is not storage a caller can rely on |
| A Customers workspace and a `GET /api/v1/customers` to feed it | D91 | No PermissionMatrix section, no SRS requirement, and zero commands on the aggregate — a list existing because an entity exists |
| Relaxing BR-10 so a completed Inspection stays editable | D92 | BR-10 was Product-Owner-requested and protects the evidence a quote rests on; the rule already named an explicit reopen as the remedy |
| Letting an Admin reopen a completed visit | D92 | §2 gives Admin `—` on photos, notes and completion, so the action that re-enables those edits belongs to the Inspector who was on site |
| Rewinding the Lead to `InspectionScheduled` on reopen | D92 | The visit did happen and an Angebot may already exist on it; reopening corrects evidence, it does not reverse the pipeline |
| Skipping the Lead transition whenever it is not `InspectionScheduled` | D93 | Too broad — it would also silently skip for a Lead that never reached the visit, masking a genuinely inconsistent Inspection/Lead pair |
| An ordinal `> InspectionScheduled` comparison instead of a status set | D93 | `Lost` sits last in the enum but is a terminal branch, not a later stage, so the comparison would be true for the wrong reason |
| A separate reopen event on Angebot to reach `Draft` from `ChangesRequested` | D94 | Adds a transition the documents do not define, to reach a state the Inspector is already in |
| Cascade delete on `CreatedFromAngebotItemId` | D95 | Would delete a shared Catalog entry as collateral damage of a draft edit, which BR-12 exists to prevent |
| Concurrency tokens on `Angebot` and `Lead` as well as `TokenLink` | D96 | Redundant — EF Core rolls back the loser's whole batch, so a token on the one row that gates the path already protects all three |
| Mapping `DbUpdateConcurrencyException` in `ProblemDetailsExceptionHandler` | D96 | Pushes a persistence mechanism into the presentation layer's mapping table; §22 records that shape as an accepted risk, not a pattern to extend |
| Catching `DbUpdateException` (the base) in `UnitOfWork` | D96 | A constraint violation is a defect, not a conflict — dressing it up as a retryable 409 would hide it |
| A browser-side (JS) customer page calling the API directly | D97 | Puts the token in a script context, needs a CORS surface, and discloses the API origin — on the one endpoint any holder of a forwarded email can reach |
| Trusting `X-Forwarded-For` unconditionally so the limiter partitions correctly | D97 | Exactly the attack D65 refused to enable: any caller mints a fresh partition per request |
| Defaulting the trusted-forwarder list to loopback (ASP.NET Core's own default) | D97 | A trust list that trusts something by default is worse than none — it looks configured |
| Skipping a malformed proxy entry instead of failing startup | D97 | Yields a limiter that appears to work and does not |
| A shared library for `TrustedForwardersOptions` across Api and Website | D97 | The Website references no backend project (§1); a shared library to hold two short lists would breach that to save a few lines |
| Rendering the API's ProblemDetails `detail` on the customer page | D97 | Authored for an API caller, in English, and may name an aggregate or an id (D59) |
| Reporting an API outage as an invalid link | D97 | Sends a customer away permanently over a transient fault |
| Refusing to remove a line that had been saved to the Catalog | D95 | Invents a rule no document states, and contradicts CLAUDE.md §2's "a draft line is unsent working material" |

---

## D98 — The FR-6.3 Rejection Reason Lives on `Angebote`, and an Approval That Carries One Is a 400

**Problem:** SRS FR-6.3 permits an optional reason with a rejection and Wireframe A3 shows the field, but `POST /api/v1/public/angebote/{token}/decision` has never accepted one. Phase 6 deferred the question to its own ADR rather than guessing, recorded the gap in `NEXT_STEPS.md` with a revisit trigger — "any requirement that reads a reason back" — and **pinned the gap with a test** so it could not drift into accept-and-discard. Phase 11 Slice 5 is that trigger firing: the customer-facing decision UI now exists and the Product Owner requires the reason captured and readable by staff.

**Alternatives considered:**

(a) `AuditLog.Details`. (b) Reuse `AngebotReviewComment`. (c) Accept it and discard it. (d) A new `AngebotDecisionReason` table. (e) **A nullable `DecisionReason` column on `Angebote`, owned by the `Angebot` aggregate.**

**Final decision:** (e), with the reason **rejection-only**, **immutable once recorded**, **never echoed to the customer**, and **never placed in an email**.

**Why chosen:**

- **(a) is ruled out by D50, not by taste.** Audit writes are best-effort and swallow their own failures; business data must never depend on them. A reason that vanishes when the audit write fails is worse than no reason.
- **(b) misattributes authorship through the schema itself.** `AngebotReviewComment.AdminUserId` is a *required* FK to `AspNetUsers` and the type is documented as the internal review loop. A customer has no account, so the row could not even be written honestly — and if it were, a customer's words would surface in the Inspector's staff-review history as though a colleague had written them.
- **(c) was already refused in Phase 6** and refused again for the Project completion override (K-4/D67): if the API accepts a value, a caller may reasonably expect it kept.
- **(d) is a table for one nullable string on a 1:1 relationship.** The reason has no independent lifecycle, no identity of its own, and is written in the same transaction as the decision it belongs to. It is an attribute of the decision, and the decision is an attribute of the Angebot.
- **(e) puts the fact where the aggregate that owns the decision already lives.** `DecisionAt` is already there; the reason is the same event's second attribute.

**Four boundaries, each decided rather than defaulted:**

1. **Rejection-only.** `RecordCustomerRejection(string? reason)` takes it; `RecordCustomerApproval()` deliberately does not. An approval has nothing to justify, and giving the method the parameter would invite a caller to store one.
2. **An approval carrying a reason is a 400, never a silent drop.** This is not a new judgement — it is K-4/D67's existing rule applied unchanged: `POST /projects/{id}/complete` already answers 400 to a reason without an override, explicitly because accepting a value and ignoring it is the accept-and-discard pattern this project has now refused three times.
3. **Never in the public DTO.** The customer submits it; only staff read it back. The anonymous token is already a credential, and echoing customer-authored free text back through that credential widens the one contract in this system that any holder of a forwarded email can reach — for no benefit, since the Dashboard is where the reason is acted on. `PublicAngebotDto` stays a separate hierarchy from `AngebotDetailDto` precisely so a field added for staff cannot appear there by accident.
4. **Never in the notification email.** FR-9.2's trigger is "a decision was received", and the notification stays exactly that. Copying customer-authored free text into mailbox storage moves it into the least-controlled channel in the system, outside the Dashboard's access model and outside any retention this project controls. `AngebotDecisionNotification` is unchanged, which also means D70's manual retry keeps working untouched.

**Immutability.** Once recorded, the reason cannot be changed or cleared: no edit endpoint, no clear endpoint, no Domain method. The decision itself is already terminal and single-use under BR-4, and the reason is part of it. This is BR-12's "retire, never delete" stance applied to a field rather than a row.

**Validation is layered, and the Domain is the backstop** (§5). `maxlength` on the textarea is convenience; FluentValidation produces a friendly 400; `Angebot.RecordCustomerRejection` trims, maps whitespace-only to `null`, and throws above 1000 characters. The guard lives in the method rather than the constructor because EF Core materialises persisted rows through the constructor — a length guard there would throw on every read of a legitimately stored value (the `TokenLink` lesson in `CLAUDE.md` §2).

**Concurrency needs nothing new.** D96's `TokenLinks.UsedAt` token already serialises this path; the reason is written in the same EF batch as `MarkUsed()`, the Angebot transition and the Lead transition, so a loser's batch rolls back and takes the reason with it. `Angebote` gets no token of its own, for the same reason D96 gave for `Angebot` and `Lead`.

**Consequences:**

- Migration **#12**, `AddAngebotDecisionReason`: `Angebote.DecisionReason nvarchar(1000) NULL`. **No backfill** — `NULL` means "not given", which is the truth for every historical rejection and for future ones without a reason. No sentinel.
- `PublicAngebotDecisionEndpointTests.A_rejection_reason_is_not_part_of_the_contract` is **replaced, not deleted.** The gap it guarded is closed, so the executable guarantee moves to the new contract: the reason is accepted, persisted, returned to staff, refused with an approval, refused over-length, and absent from the public DTO. Deleting it would remove the only thing keeping any of that from drifting.
- `NEXT_STEPS.md`'s gap entry is removed rather than amended — its revisit trigger fired and the question is answered here.
- This is the **second** untrusted-text surface on the customer site and the first written by the customer themselves. It is HTML-encoded wherever it renders, proven by a test with a hostile payload rather than by inspection.

---

## D99 — Re-issuing an Angebot Token Link: Expiry Supersedes, and `ExpiresAt` Is the Gate That Serialises It

**Problem:** A customer loses the email, the link lapses before they answer, or the address needed correcting. Phase 11's approved **Q3** requires an Admin-triggered re-issue: only while the Angebot is `Sent`, the old credential invalidated **in the same transaction** that creates the new one, and **never more than one live credential per Angebot**. No requirement document named this capability — SRS had no FR, `PermissionMatrix.md` §4 no row, `Architecture.md` §5.2 no endpoint. It exists because the Product Owner required it, which is the same footing `Angebot.UpdateItem` stood on in Phase 10, so the documents move first (`CLAUDE.md` §15). SRS **OQ-4** ("revise and resend" after a rejection, which would need `Lost → AngebotInProgress`) is a different question and stays open.

### Part 1 — How the old link is invalidated

**Alternatives considered:** (a) Set `UsedAt`. (b) Add a `RevokedAt`/`IsCurrent` column. (c) Delete the old row. (d) **Set `ExpiresAt` to now.**

**Final decision:** (d).

**Why chosen:**

- **(a) would destroy two meanings at once.** `UsedAt` means "a decision was recorded through this link" (BR-4) *and* is the optimistic-concurrency token D96 added. Writing it for a re-issue would make the audit trail lie and would corrupt the decision race.
- **(b) is schema and a third "why is this link dead?" state** for an outcome that already has a correct customer-facing page.
- **(c) contradicts this codebase's standing position** that historical records are retired, never deleted (BR-12's stance, applied to a row).
- **(d) makes the customer-facing wording literally true.** The public page already answers an expired link with *"Dieser Link ist abgelaufen … senden wir Ihnen gerne einen neuen Link zu."* A superseded link **is** expired, so the honest message and the accurate message are the same message, with no new state to map.

**Consequence:** multiple `TokenLinks` rows per Angebot are now expected, with **at most one usable** (`UsedAt IS NULL` and not expired). Old rows are kept.

### Part 2 — No state transition, and the `Sent`-only check lives in the handler

Re-issuing changes no aggregate state: the Angebot stays `Sent`, the Lead stays `AngebotSent`, and **`SentAt` is not updated** — it records the original send, and each re-issue is recorded in the audit trail instead.

That leaves "only while `Sent`" with nowhere Domain-shaped to live. `CLAUDE.md` §6 forbids a handler inspecting an aggregate's state field, on the grounds that it should call the Domain method and let it throw — **but there is no mutator to call**, because nothing about the Angebot changes. The alternative, a public `Angebot.EnsureResendable()` probe, is precisely the read-only precondition method D29 rejected for `Inspection.IsEditable`: Domain surface grown to answer a question no mutator asks.

**Decision: the check lives in the handler and throws `ConflictException`, recorded here as a deliberate, reasoned exception to §6 rather than an oversight.** The rule §6 protects — *don't duplicate a guard the Domain already enforces* — is not in play, because no Domain guard exists to duplicate.

### Part 3 — Serialising concurrent re-issues, and a flaw caught in review

**The first design was wrong, and the way it was wrong is the lesson.** It claimed that D96's `UsedAt` concurrency token already serialised two simultaneous re-issues. It does not. EF Core puts a token's *original* value in the `WHERE` clause, and a re-issue never writes `UsedAt` — so both concurrent operations issue `WHERE Id = @id AND UsedAt IS NULL`, both match, both commit, and **two usable credentials exist**. The invariant Q3 states would have been violated by the very mechanism claimed to protect it.

`UsedAt` does gate *decision-versus-re-issue*, because the decision writes it. It gates nothing between two writers that both leave it alone. **The guarantee comes from the column the operation actually changes, never from the presence of a token on the row** — the sharpened form of the rule `CLAUDE.md` §21 already states as "the question is not 'does a guard exist' but 'what happens when two callers pass it at the same time'".

**Alternatives considered:**

- **A pessimistic row lock** on `Angebote` (`UPDLOCK, HOLDLOCK`) inside the existing transaction API. Satisfies the letter of the invariant and produces the wrong behaviour: re-issues do not conflict, they **chain** — the second waits, then supersedes the first's brand-new link. Exactly one credential survives, but the customer receives two emails and the first link is dead on arrival. It also adds blocking and a deadlock surface for a case that should simply be refused.
- **A unique filtered index**, the only mechanism where the schema itself forbids two live credentials. It cannot be built on today's columns: filtering on `UsedAt IS NULL` alone would forbid the unused-but-expired rows that accumulate legitimately, and expiry is time-relative, so `ExpiresAt > GETUTCDATE()` is non-deterministic and illegal in a filtered-index predicate. A real constraint needs the `IsCurrent`/`RevokedAt` column Part 1 rejected. **Deliberately not adopted in this slice**; the schema model stays as it is.
- **Serializable isolation.** Escalates locking for the whole transaction, invites deadlocks, and still chains rather than refuses unless combined with an optimistic check — so it buys nothing the chosen mechanism does not, at a higher cost.
- **Make `ExpiresAt` an optimistic-concurrency token as well.**

**Final decision:** the last one.

`ExpiresAt` is the column a re-issue writes, so it is the column that can gate one re-issue against another:

```sql
UPDATE TokenLinks SET ExpiresAt = @now
WHERE Id = @id AND UsedAt IS NULL AND ExpiresAt = @originalExpiresAt
```

Both readers see the same original value; the first commits; the second matches **zero rows**; EF throws `DbUpdateConcurrencyException`; `UnitOfWork` translates it to `ConflictException` (D96) and the API answers **409**. Exactly one re-issue wins, and the loser is refused rather than chained.

**The loser's new link is never persisted.** EF wraps the `UPDATE` and the `INSERT` in one batch inside one transaction, so a failed concurrency check rolls the whole batch back — the same property D96 recorded ("EF Core wraps the batch in a transaction and rolls the loser's whole batch back"). The only correction is *which column gates the path*.

**It strengthens the customer path rather than merely preserving it.** With `ExpiresAt` also a token, a decision arriving through a link superseded mid-flight conflicts deterministically instead of committing against a credential that has been replaced. All three interleavings stay correct: the re-issue commits first, so the decision handler's own expiry check answers **410**; the decision commits first, so the re-issue misses on `UsedAt` and answers **409**; genuinely simultaneous, and whoever commits second loses.

**Cost:** migration **#13** with legitimately empty `Up`/`Down`. A non-`rowversion` token is client-side `WHERE`-clause behaviour and changes no schema, but it is recorded in the model snapshot — the identical situation to migration #11, and the same precedent covers it.

### Part 4 — A trap this analysis surfaced

Q2 approves re-issuing a link that has already lapsed naturally. **`TokenLink.Expire()` must therefore always write, even when the link is already expired.** An implementation that "helpfully" skips the write when `IsExpired` is already true would cause EF to issue **no `UPDATE` at all**, the concurrency predicate would not exist, and the serialisation would silently vanish for exactly the case Q2 added. The write is what carries the guard. This is pinned by a test, not by this paragraph.

### Consequences

- `TokenLink` gains `Expire()`; `UsedAt` keeps its single meaning and is never written by a re-issue.
- `TokenLinks.ExpiresAt` becomes an optimistic-concurrency token (migration #13, empty `Up`/`Down`).
- `POST /api/v1/angebote/{id}/resend`, Admin-only, returning `AngebotDto` and **no token** — the credential goes only to the customer's email, exactly as `send` does.
- `PermissionMatrix.md` §4 gains "Resend Angebot link — Admin **F**"; `Architecture.md` §5.2 gains the endpoint; `ERD.md` records that multiple rows per entity are expected with at most one usable; `StateMachine.md` records that re-issue causes **no** transition, because silence there would read as an omission.
- The invariant is guaranteed by application-issued predicates rather than by a schema constraint. A future writer bypassing the aggregate could still violate it; closing that needs the filtered index above, which is a deliberate non-goal here.
- **`NotificationRetryExecutor` needs no change, and that is not luck.** It already orders token links `CreatedAt DESC, Id DESC` and takes the first, with a comment stating that one link per entity is true "in practice" but unenforced, and it already refuses an expired or used link. This slice makes that foresight load-bearing, so a test pins it.
