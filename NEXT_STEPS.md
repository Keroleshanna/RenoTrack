# NEXT_STEPS.md — What Happens Next

**Read this after `PROJECT_STATE.md` and before writing any code.** This file is specifically about *what to do next*, not what has already happened (that's `PHASE2_PROGRESS.md`) or why past decisions were made (that's `ARCHITECTURE_DECISIONS.md`).

---

## 1. Immediate Next Task: CatalogItem Application Layer

The Domain entity `CatalogItem` already exists and is fully built and tested (Phase 1b — `Create`, `Update`, `Retire`, BR-12 retirement policy). **Nothing in the Domain needs to change.** The task is purely the Application layer: commands, a query, validators, handlers, DTOs, and tests — following the exact same process this whole project has used for every prior feature.

### 1.1 Recommended Implementation Order

Follow this order — it goes simplest-and-most-precedented first, saving the one genuinely novel piece (the query) for when the established conventions are freshly reinforced:

1. **`CreateCatalogItemCommand` — ✅ done (Slice 11).** Closest precedent: `CreateLeadCommand`/`CreateAngebotCommand`. Straightforward create-and-persist, no cross-aggregate concerns (CatalogItem is independent, per Architecture §6). `ICatalogItemRepository` introduced with `AddAsync` only; `CatalogItemDto` introduced; `AuditAction.CatalogItemCreated` added. See `PHASE2_PROGRESS.md` Slice 11 for the full record.
2. **`UpdateCatalogItemCommand` — ✅ done (Slice 12).** Closest precedent: any command loading then mutating an existing aggregate (`ScheduleInspectionCommand`, `AddAngebotSectionCommand`). Confirmed as the first `Update`-shaped command in the whole Application layer; `ICatalogItemRepository` gained `GetByIdAsync`; `AuditAction.CatalogItemUpdated` added; reuses the existing `CatalogItemDto`. See `PHASE2_PROGRESS.md` Slice 12 for the full record.
3. **`RetireCatalogItemCommand` — ✅ done (Slice 13).** Simplest of the three; `CatalogItem.Retire()` takes no parameters and is idempotent — re-verified explicitly before implementation (see `PHASE2_PROGRESS.md` Slice 13's four-question Domain check: idempotency confirmed, no "unretire" path exists or is planned anywhere in the docs, no rule should block retirement based on historical `AngebotItem` references per BR-8). Handler is the simplest of the three CatalogItem commands.
4. **`SearchCatalogItemsQuery` — next up (last piece of this feature).** **This is the first query in the entire codebase.** Every command so far has followed the same "load aggregate → mutate → return DTO of that same aggregate" shape; a query has no aggregate to mutate and, per `CLAUDE.md` §3, is expected to return DTOs directly without full aggregate hydration. Expect a genuine design discussion here with no established precedent to fall back on mechanically — see §2 below.

### 1.2 Authorization Model — Check PermissionMatrix.md §6 Before Assuming Anything

Do **not** assume every CatalogItem command needs `IOwnershipValidator`. Per `CLAUDE.md` §16, the deciding question is PermissionMatrix.md's letter for each specific action:

| Action | PermissionMatrix §6 | Expected authorization model |
|---|---|---|
| Create/curate Catalog item directly | Admin **F**, Inspector — | Role-based only (Admin), same pattern as `ApproveAngebotCommand`/`RequestAngebotChangesCommand` — **no** `IOwnershipValidator` call |
| Add via "save as Catalog item" | Admin —, Inspector **F** | Role-based only (any Inspector) — **no** `IOwnershipValidator` call; note this is `F` for Inspector too, not `S` — **any** Inspector may save any custom item as a Catalog entry, not just the one who created the Angebot it came from |
| Edit an existing Catalog item | Admin **F**, Inspector — | Role-based only (Admin) — **no** `IOwnershipValidator` call |
| Delete/retire a Catalog item | Admin **F**, Inspector — | Role-based only (Admin) — **no** `IOwnershipValidator` call |
| View Catalog | Admin **F**, Inspector **F** | Both roles, full access — no restriction to enforce at all beyond authentication |

**Confirmed during design review (Slice 11):** none of the CatalogItem commands need `IOwnershipValidator` — every row above is `F`, not `S`. This is confirmed as the **first entire feature** in the project with zero ownership checks anywhere in it. `CreateCatalogItemCommandHandler` has no ownership-check code, matching `ApproveAngebotCommandHandler`/`RequestAngebotChangesCommandHandler`'s precedent. Carry this same conclusion forward unchanged into `UpdateCatalogItemCommand`/`RetireCatalogItemCommand`.

### 1.3 Expected Architectural Concerns to Raise During Design Review

Work through these the same way every other slice in `PHASE2_PROGRESS.md` did — analysis and explicit user sign-off **before** writing code, per the process this whole project has used without exception:

- **Repository:** `ICatalogItemRepository` does not exist yet. It will need at least `AddAsync` (for Create) and `GetByIdAsync` (for Update/Retire). Does it need anything else for the query (§2 below), or does the query bypass the repository entirely?
- **DTO:** `CatalogItemDto` does not exist yet. Design it the same incremental way every other DTO was designed — header/scalar fields only unless a specific use case needs more.
- **Audit:** Is creating/updating/retiring a Catalog item a business milestone worth auditing (per `CLAUDE.md` §10's test: "would someone reviewing this entity's history want to see this?"), or operational activity like photo-upload/notes-update? There is no existing precedent for an *independent* aggregate's own audit policy (Lead/Angebot both had the "which aggregate does the business care about" question to resolve; CatalogItem has no such cross-aggregate ambiguity — the entity mutated *is* the entity that matters). Decide this explicitly, don't default silently either way.
- **Notification:** SRS FR-9.2 does not mention any Catalog-related notification. Expect **no** `IEmailSender` involvement in any CatalogItem command, but confirm this explicitly rather than assuming, the same way every prior slice explicitly checked FR-9.2/Sequence Diagrams before deciding to skip a notification.
- **"Save as Catalog item" is a distinct, separate command** (`SaveAngebotItemAsCatalogItemCommand`, SRS FR-4.10, `POST /api/v1/angebot-items/{itemId}/save-as-catalog-item`) — **not** the same as `CreateCatalogItemCommand`. It reads an existing `AngebotItem`'s fields and constructs a new `CatalogItem` from them (`CatalogItem.Create(..., createdFromAngebotItemId: angebotItem.Id)`). Decide during design review whether to build this now (as part of the CatalogItem feature) or explicitly defer it to the `AddAngebotItemCommand` slice that follows — either is defensible, but the decision should be made and recorded, not fallen into by accident.

### 1.4 Potential Risks / Things to Watch For

- **Do not build a "shallow" query result by manually constructing a `CatalogItemDto` list from full `CatalogItem` aggregates loaded via a repository's `GetByIdAsync`-style method** for the search/list query — that defeats the entire point of a read/write split (`CLAUDE.md` §3). If a query-specific interface is introduced (e.g. `ICatalogItemQueries.SearchAsync(...)` returning `IReadOnlyList<CatalogItemDto>` directly), that is the moment to actually implement the read/write split this project has talked about since Phase 2's initial design review but never yet had a concrete use case to build.
- **`SearchCatalogItemsQuery` will need to filter out retired items** (BR-12: retired items are excluded from the Catalog picker) — this is a genuine, real filter condition, not speculative; make sure it's actually implemented and tested, not just mentioned in a comment.
- **Do not introduce `IOwnershipValidator` calls "just in case."** If §1.2's expectation holds (no ownership concept anywhere in this feature), that absence should be treated the same way `ApproveAngebotCommandHandler`/`RequestAngebotChangesCommandHandler` treated it — a deliberate, confirmed reflection of the actual business rule, not an inconsistency to "fix" by adding a check that doesn't belong.

---

## 2. After CatalogItem: Return to `AddAngebotItemCommand`

Once CatalogItem's Application layer is complete, implement `AddAngebotItemCommand` **with both paths available from the start** — this was the entire reason it was postponed (`ARCHITECTURE_DECISIONS.md` D30). Do not implement one path first "to make progress."

**Expected shape, based on Sequence Diagram §4 and BR-8:**
- Command likely needs a discriminator or optional `CatalogItemId` parameter: if supplied, the handler loads the `CatalogItem`, copies its `Title`/`DefaultSpecification`/`DefaultUnit`/`SuggestedUnitPrice` into the new `AngebotItem`'s parameters (BR-8's copy-on-create semantics — the handler does the copying; `AngebotItem` itself has no knowledge of `CatalogItem` beyond the passive traceability `CatalogItemId` field); if not supplied, the caller provides `Description`/`Specification`/`Unit`/`UnitPrice` directly for a fully custom item.
- New DTOs expected: `ItemDto` and `AngebotSummaryDto` (both named explicitly in Sequence Diagram §4 — "APP-->>API: ItemDto + updated AngebotSummaryDto"). `AngebotSummaryDto` is likely a lighter-weight response (Id, AngebotNumber, Status, NetTotal, GrossTotal) distinct from the full `AngebotDto`, used specifically so a client adding items one at a time doesn't need the full DTO shape re-serialized on every add.
- Ownership: `IOwnershipValidator.EnsureAngebotOwnership` (already exists, same as `AddAngebotSectionCommand`).
- Retired `CatalogItem`s: decide explicitly whether `AddAngebotItemCommand` should reject an attempt to add an item sourced from a retired `CatalogItem`, or allow it (the item's fields are copied at creation time regardless per BR-8, so a retired source arguably doesn't matter functionally — but confirm this is genuinely inconsequential rather than assuming).
- **"Save as Catalog item"** (`SaveAngebotItemAsCatalogItemCommand`) — if not already built during the CatalogItem feature (§1.3), this is the natural place to build it, since it's most directly related to `AngebotItem`.

## 3. Remaining Phase 2 Scope After That

Per `PROJECT_ROADMAP.md`, once `AddAngebotItemCommand` (and "save as Catalog item," wherever it ends up) are done, Phase 2's originally-scoped Lead/Inspection/Angebot/CatalogItem Application-layer work is complete. At that point:

- Review whether Phase 2 should be considered closed and merged (one PR, per `CLAUDE.md` §19's "accumulate a phase's slices, open one PR at milestone" convention) before starting Phase 3.
- Phase 3 (Infrastructure — EF Core, repositories, Identity) is next per the roadmap. Every interface catalogued in `PROJECT_STATE.md` §5.2 needs a concrete implementation at that point. **`INumberGeneratorService`'s atomic-transaction requirement (Architecture §8) is the single highest-risk unverified assumption carried into Phase 3** — write an actual concurrency test for it, don't just implement and assume it's correct.

---

## 4. What Should NOT Be Changed

- **Do not modify `Lead`, `Inspection`, `Angebot`, `CatalogItem`, or `AngebotReviewComment`'s existing public API** without a genuine bug or a new, explicitly-documented business rule. "Treat Phase 1/1b/the-Domain-so-far as a stable baseline" is a standing instruction, repeated explicitly by the user at the close of both Phase 1 and Phase 1b.
- **Do not add `IOwnershipValidator` calls to Admin-`F` commands** (`ApproveAngebotCommand`, `RequestAngebotChangesCommand`, and — very likely — every CatalogItem command). Their absence is correct, not an oversight to "fix" for consistency.
- **Do not introduce MediatR, AutoMapper, or a mocking framework (Moq/NSubstitute)** — all three were explicitly considered and explicitly rejected for this project (see `ARCHITECTURE_DECISIONS.md` D22, `CLAUDE.md` §8, `CLAUDE.md` §14).
- **Do not add repository methods, DTO fields, or notification models speculatively** — every abstraction in this codebase was added because one specific, real command needed it at that exact moment. Continue that discipline; do not "future-proof" ahead of an actual requirement.
- **Do not re-litigate whether `AngebotReviewComment` should be an Angebot child** — this was verified carefully (multi-cycle review support, ERD/Wireframe/PermissionMatrix evidence) and confirmed correct in `ARCHITECTURE_DECISIONS.md` D32. Reopen only with genuinely new evidence, not a fresh read of the same documents.
- **Do not force-push to `main`, ever** (`CLAUDE.md` §19, `ARCHITECTURE_DECISIONS.md` D5). Always `git fetch origin` before any push and re-verify remote state.

## 5. Decisions Considered Final (Do Not Reopen Without New Evidence)

- Clean Architecture layering + explicit (not transitive) cross-project references.
- No MediatR; hand-rolled `ICommandHandler<TCommand, TResult>`.
- No AutoMapper; manual `ToDto()` extension methods.
- Rich domain model with private constructors, named transition methods, self-guards only.
- `IOwnershipValidator` as named-business-intent methods, never a generic id-comparison helper.
- Audit policy: business milestones only, targeting the aggregate the business cares about (not necessarily the one directly mutated).
- Notification models are dedicated types in `Common.Notifications`, never feature DTOs.
- Repository/interface/DTO growth strictly on-demand, never speculative.
- Role-based authorization (API layer) vs. resource ownership (`IOwnershipValidator`, Application layer) — decided mechanically by PermissionMatrix's `F`/`S` marking.
- The Application layer generates stable external identifiers before invoking external infrastructure, when doing so lets a Domain guard reject before an irreversible side effect.
- Never truly delete a historical record (retire/void instead) — applies project-wide by default for any future "delete" requirement, not just the specific entities where it's already been formalized (Lead, Invoice, Inspection, CatalogItem).
- No CatalogItem command uses `IOwnershipValidator` — every action is Admin/Inspector-`F` per `PermissionMatrix.md` §6, confirmed during Slice 11's design review.
- `IQueryHandler<TQuery, TResult>` is a distinct interface from `ICommandHandler<TCommand, TResult>`, even though their method signatures currently coincide — queries get their own dispatch abstraction, not a reuse of the command one (`ARCHITECTURE_DECISIONS.md` D36).
- `SearchCatalogItemsQuery` starts with no `includeRetired` parameter — always excludes retired items, no flag until a real documented use case needs one (`ARCHITECTURE_DECISIONS.md` D37).

## 6. What Still Requires Future Discussion (Not Yet Decided — Do Not Assume an Answer)

- Whether `AngebotItem` should ever gain update/remove methods (currently an open question, not a rule — `ARCHITECTURE_DECISIONS.md` D12). Revisit only with real evidence (a documented endpoint, an explicit business decision).
- The exact HTTP status-code mapping for Domain's own `ArgumentException`/`InvalidOperationException` (likely 400/409 respectively) — deferred to Phase 4's API middleware design.
- Whether `SaveAngebotItemAsCatalogItemCommand` belongs in the CatalogItem feature or the `AddAngebotItemCommand` feature (§1.3/§2 above) — to be decided during design review, not pre-decided by this document.
- The exact shape of `ICatalogItemQueries` (method name, return type) for `SearchCatalogItemsQuery` — the read/write split's principle itself is now decided (a dedicated query interface returning DTOs directly, no repository/aggregate hydration involved), but the interface hasn't been written yet.
- OQ-1 through OQ-4 from `SRS.md` §10 remain open at the SRS level (Admin managing Inspector accounts; website language; email provider choice — needed before Phase 9; "revise and resend" after rejection) — none of these block current work, but do not assume an answer to any of them without checking `SRS.md` first.

---

## 7. How to Start Your First Message in a Resumed Conversation

1. Read `CLAUDE.md`, `PROJECT_STATE.md`, `ARCHITECTURE_DECISIONS.md`, `PHASE2_PROGRESS.md`, and this file, in that order, in full.
2. Run `dotnet build RenoTrack.slnx` and `dotnet test RenoTrack.slnx` yourself and confirm the counts in `PROJECT_STATE.md` §3 still hold. If they don't, something changed since this handoff was written — investigate before proceeding, don't just trust the stale numbers.
3. Run `git status`, `git branch --show-current`, and `git log --oneline -15` to confirm you're on `feature/phase-2-application-layer` with the expected 10 commits, and that `main` matches what `PROJECT_STATE.md` §2 describes.
4. Begin with the CatalogItem design-review analysis (§1 above) — do not write code before that review is complete and the user has explicitly approved the design, exactly as every prior slice in `PHASE2_PROGRESS.md` was handled.
