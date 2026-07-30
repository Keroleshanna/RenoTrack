# NEXT_STEPS.md — What Happens Next

**Read this after `PROJECT_STATE.md` and before writing any code.** This file is specifically about *what to do next*, not what has already happened (that's `PHASE2_PROGRESS.md`) or why past decisions were made (that's `ARCHITECTURE_DECISIONS.md`).

---

## 1. Phase 2 — Complete, Pending PR

All of Phase 2's roadmap-defined scope (`PROJECT_ROADMAP.md`'s Phase 2 command list: `CreateLeadCommand`, `ScheduleInspectionCommand`, `CompleteInspectionCommand`, `CreateAngebotCommand`, `AddAngebotSectionCommand`, `AddAngebotItemCommand`, `SubmitAngebotForReviewCommand`, `RequestAngebotChangesCommand`, `ApproveAngebotCommand`) is done — 15 vertical slices, full record in `PHASE2_PROGRESS.md`. `CatalogItem`'s Application layer (`CreateCatalogItemCommand`, `UpdateCatalogItemCommand`, `RetireCatalogItemCommand`, `SearchCatalogItemsQuery` — Slices 11–14) was a deliberate, justified insertion into this branch, needed by `AddAngebotItemCommand`.

**Immediate next step:** open the Phase 2 PR (see `PROJECT_STATE.md` for the closeout review — build/test status, commit range, recommended PR title). After the PR, Phase 3 (Infrastructure — EF Core, repositories, Identity) begins.

## 2. Deferred Items — Explicitly Recorded, With Reasons

- **`SaveAngebotItemAsCatalogItemCommand` (SRS FR-4.10) — deferred out of Phase 2, not implemented.** Two independent reasons, both verified rather than assumed (see `ARCHITECTURE_DECISIONS.md` D39 for the full record):
  1. It was never actually in Phase 2's roadmap-defined scope — `PROJECT_ROADMAP.md`'s Phase 2 command list doesn't include it or any CatalogItem command; Phase 1b's title names the "save as catalog item" concept but its own deliverable list only required the Domain-level `CatalogItem.Create(..., createdFromAngebotItemId)`, already built.
  2. Implementing it today would force a new Application-layer lookup capability — resolving an `AngebotItem`'s owning `Angebot`/`Section` from the item's id alone (its documented route, `POST /api/v1/angebot-items/{itemId}/save-as-catalog-item`, carries no `AngebotId`) — that no other command needs and that isn't justified by anything else in current scope.
  - **Revisit only when a phase that actually needs it arrives** — most naturally Phase 3, since real EF Core ids would trivially resolve the lookup problem. Do not build a one-off lookup mechanism just to unblock this single command.
- **`IFileStorage.GetAsync`/`DeleteAsync`** — not built; no current command needs them (`CLAUDE.md` §4).
- **`Angebot.Send()`, `RecordCustomerApproval()`, `RecordCustomerRejection()`** — Domain methods exist (Phase 1) but have no Application-layer commands yet; deliberately deferred to Phase 6 (Token-link mechanism), since they depend on `ITokenLinkService`, which doesn't exist.
- **`AngebotItem` update/remove methods** — open question, not a rule (`ARCHITECTURE_DECISIONS.md` D12/`CLAUDE.md` §2). Revisit only with real evidence (a documented endpoint, an explicit business decision).
- **HTTP status-code mapping** for Domain's own `ArgumentException`/`InvalidOperationException` — deferred to Phase 4's API middleware design.

## 3. What Should NOT Be Changed

- **Do not modify `Lead`, `Inspection`, `Angebot`, `CatalogItem`, or `AngebotReviewComment`'s existing public API** without a genuine bug or a new, explicitly-documented business rule. "Treat Phase 1/1b/the-Domain-so-far as a stable baseline" is a standing instruction, repeated explicitly by the user at the close of both Phase 1 and Phase 1b.
- **Do not add `IOwnershipValidator` calls to Admin-`F` commands** (`ApproveAngebotCommand`, `RequestAngebotChangesCommand`, every CatalogItem command). Their absence is correct, not an oversight to "fix" for consistency.
- **Do not introduce MediatR, AutoMapper, or a mocking framework (Moq/NSubstitute)** — all three were explicitly considered and explicitly rejected for this project (see `ARCHITECTURE_DECISIONS.md` D22, `CLAUDE.md` §8, `CLAUDE.md` §14).
- **Do not add repository methods, DTO fields, or notification models speculatively** — every abstraction in this codebase was added because one specific, real command needed it at that exact moment. Continue that discipline; do not "future-proof" ahead of an actual requirement. `SaveAngebotItemAsCatalogItemCommand`'s deferral (§2 above) is this exact principle applied to a whole command, not just a field.
- **Do not re-litigate whether `AngebotReviewComment` should be an Angebot child** — this was verified carefully (multi-cycle review support, ERD/Wireframe/PermissionMatrix evidence) and confirmed correct in `ARCHITECTURE_DECISIONS.md` D32. Reopen only with genuinely new evidence, not a fresh read of the same documents.
- **Do not force-push to `main`, ever** (`CLAUDE.md` §19, `ARCHITECTURE_DECISIONS.md` D5). Always `git fetch origin` before any push and re-verify remote state.
- **Do not assume a command belongs to the current phase just because it appears in the SRS/Sequence Diagrams** — always check the phase's own roadmap-defined scope first (`ARCHITECTURE_DECISIONS.md` D39 is the concrete example: `SaveAngebotItemAsCatalogItemCommand` is real, documented, SRS-backed work, and still doesn't belong in Phase 2).

## 4. Decisions Considered Final (Do Not Reopen Without New Evidence)

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
- A retired `CatalogItem` remains a valid direct `CatalogItemId` reference — retirement only affects discovery, never a direct reference (BR-14, `ARCHITECTURE_DECISIONS.md` D38).
- `AddAngebotItemCommand`'s `Quantity`/`UnitPrice`/`VatRate` are always caller-supplied for both paths; only `Description`/`Specification`/`Unit` are copied from a `CatalogItem` in the Catalog-sourced path.
- `SaveAngebotItemAsCatalogItemCommand` is out of Phase 2's scope and deferred — not a rejection of the feature, just a scope/sequencing decision (BR-14 is unrelated and stands; `ARCHITECTURE_DECISIONS.md` D39).

## 5. What Still Requires Future Discussion (Not Yet Decided — Do Not Assume an Answer)

- **`SaveAngebotItemAsCatalogItemCommand`'s lookup design** — how the Application layer resolves an `AngebotItem`'s owning `Angebot`/`Section` from the item's id alone, once this command is actually built (§2 above). Not yet designed; do not pre-decide a repository shape for it now.
- Whether `AngebotItem` should ever gain update/remove methods (currently an open question, not a rule — `ARCHITECTURE_DECISIONS.md` D12). Revisit only with real evidence (a documented endpoint, an explicit business decision).
- The exact HTTP status-code mapping for Domain's own `ArgumentException`/`InvalidOperationException` (likely 400/409 respectively) — deferred to Phase 4's API middleware design.
- OQ-1 through OQ-4 from `SRS.md` §10 remain open at the SRS level (Admin managing Inspector accounts; website language; email provider choice — needed before Phase 9; "revise and resend" after rejection) — none of these block current work, but do not assume an answer to any of them without checking `SRS.md` first.

---

## 6. How to Start Your First Message in a Resumed Conversation

1. Read `CLAUDE.md`, `PROJECT_STATE.md`, `ARCHITECTURE_DECISIONS.md`, `PHASE2_PROGRESS.md`, and this file, in that order, in full.
2. Run `dotnet build RenoTrack.slnx` and `dotnet test RenoTrack.slnx` yourself and confirm the counts in `PROJECT_STATE.md` §3 still hold. If they don't, something changed since this handoff was written — investigate before proceeding, don't just trust the stale numbers.
3. Run `git status`, `git branch --show-current`, and `git log --oneline -15` to confirm you're on `feature/phase-2-application-layer` with the expected commits, and that `main` matches what `PROJECT_STATE.md` §2 describes.
4. Phase 2 is complete and pending its PR (§1 above). If the PR hasn't been opened yet, that's the next action. If it has been merged, begin Phase 3 (Infrastructure) — read `PROJECT_ROADMAP.md`'s Phase 3 section first, and remember `INumberGeneratorService`'s atomic-transaction requirement is the single highest-risk unverified assumption carried into it (`CLAUDE.md` §18).
