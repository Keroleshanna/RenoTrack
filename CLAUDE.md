# CLAUDE.md — Permanent Engineering Rules for RenoTrack

**Status:** Living document. These are not suggestions — they are the conventions this codebase has already committed to, established through explicit design review across Phase 0, Phase 1, Phase 1b, Phase 2, and Phase 3 (all merged to `main`). Any code that violates a rule here without a documented, agreed exception is a bug in the review process, not a stylistic choice.

**How to use this file:** Read it in full before writing any code in this repository. When a new decision is made that changes or adds to a rule here, update this file in the same PR that makes the change — do not let this file drift out of sync with the code. If you are an AI assistant picking up this project, treat every rule below as settled unless the user explicitly reopens it with new evidence.

---

## 1. Overall Architecture

- **Clean Architecture**, strictly layered: `RenoTrack.Domain` → `RenoTrack.Application` → `RenoTrack.Infrastructure`/`RenoTrack.Api`. Dependency direction is enforced by actual `ProjectReference` entries, not convention — `Domain` has zero project references; `Application` references only `Domain`; `Infrastructure` references `Application` **and** `Domain` explicitly (not transitively — see §11).
- **Domain-Driven Design**, with a **rich domain model**: entities own their invariants and expose behavior (named methods), never public setters or anemic property bags manipulated externally.
- **CQRS-lite without a mediator library.** No MediatR. See §3.
- Front-end projects (`RenoTrack.Website`, `RenoTrack.Dashboard`) never reference any backend project — they talk to the API over HTTP only.
- Test projects mirror the same isolation: `RenoTrack.Domain.Tests` references only `RenoTrack.Domain`; `RenoTrack.Application.Tests` references `RenoTrack.Application` **and** `RenoTrack.Domain` explicitly (tests assert on resulting Domain state, so this reference is real, not transitive-only). `RenoTrack.Infrastructure.Tests` (added Phase 3) references `RenoTrack.Infrastructure` **and** `RenoTrack.Domain`, and is the one test project that talks to a real database (LocalDB) rather than in-memory fakes — see §14.

---

## 2. Rich Domain Model — Aggregate Rules

- **Constructors are private; construction happens through named static factory methods** (`Lead.Create(...)`, `Inspection.Schedule(...)`, `Angebot.Create(...)`, `CatalogItem.Create(...)`). This makes it structurally impossible to construct an entity in an invalid initial state.
- **Every state transition is a named method**, never a public status setter. E.g. `Lead.MarkInspectionScheduled()`, `Angebot.SubmitForReview()`. This is required by BR-7 for Lead and is applied as a general Domain convention everywhere else too.
- **Self-guards only.** An aggregate enforces exactly the invariants it can determine from its own current state. It never validates anything that requires knowledge of another aggregate, a repository query, or "who is calling." See §9 for where those checks actually live.
  - Example: `Lead.MarkInspectionScheduled()` checks `Status == New` (its own state) but does **not** check "does this Inspection actually belong to this Lead" (that requires loading a different aggregate — Application's job).
  - Example: `Angebot.SubmitForReview()` checks both `Status == Draft` **and** "at least one section has at least one item" — both are fully determinable from Angebot's own already-loaded object graph (its Sections/Items), so both stay inside the aggregate, not split between Domain and Application.
- **Aggregate roots are the only public entry point for mutating their own children.** Child entity constructors and mutating methods are `internal`, never `public`:
  - `InspectionPhoto`'s constructor is `internal`; only reachable via `Inspection.AddPhoto(...)`.
  - `AngebotSection`'s constructor and `AddItem` are `internal`; only reachable via `Angebot.AddSection(...)` / `Angebot.AddItemToSection(...)`.
  - `AngebotItem`'s constructor is `internal`; only reachable via `AngebotSection.AddItem(...)`.
  - This is enforced at compile time for every consumer **except** `RenoTrack.Domain.Tests`, which is granted access via a single `[assembly: InternalsVisibleTo("RenoTrack.Domain.Tests")]` in `RenoTrack.Domain.csproj` — Application/Infrastructure/Api still cannot bypass the aggregate root.
  - Verify this boundary with reflection-based tests where meaningful (see §14) — e.g. asserting a child type has zero public constructors, or that a mutator method is not resolvable via `GetMethod(..., BindingFlags.Public)`.
- **A child entity passed across an aggregate boundary is passed by reference, not by an as-yet-unassigned id**, when identity isn't stable yet. `Angebot.AddItemToSection(AngebotSection section, ...)` takes the actual `AngebotSection` object, not an `int sectionId` — because before EF Core assigns real database ids (Phase 3), every freshly-created child in a given parent shares `Id == 0`, making an id-based lookup within the aggregate ambiguous. The method still verifies the passed object actually belongs to the aggregate it's called on. Once Phase 3 assigns real ids, the Application layer resolves which child a request targets (e.g. from a route id) by reading the already-loaded parent's child collection, then passes the resolved instance in — this signature does not need to change then.
- **Independent aggregates relate to each other only by id, never by object reference or navigation property**, even when one is conceptually "about" another:
  - `AngebotItem.CatalogItemId` (nullable) — traceability only (BR-8); no behavior branches on whether it's set.
  - `AngebotReviewComment.AngebotId` — the only link; neither type has a property or field whose type is the other (verified by reflection-based structural tests).
  - `Lead.AssignedInspectorId`, `Inspection.LeadId`/`InspectorId`, `Angebot.LeadId`/`InspectionId`/`CreatedByInspectorId`/`ReviewedByAdminId` — all plain `int`/`int?`, never navigation properties to `User`/`Lead`/etc. as types. This keeps every aggregate's compile-time dependency graph limited to its own children.
- **Computed vs. stored fields — decide based on the actual ERD-documented reason for storage, not by default:**
  - `AngebotItem.LineTotal` and `AngebotSection.Subtotal` are pure computed properties (`=>` expressions), never stored fields — recomputed from live child data on every access, so they can never drift out of sync. No ERD-stated performance reason applies at that granularity (items/sections are never displayed independently of their parent Angebot).
  - `Angebot.NetTotal`/`GrossTotal` **are** stored fields (`{ get; private set; }`), kept current by a `private` `RecalculateTotals()` called at the end of every mutating method. This mirrors real ERD columns whose stated purpose is fast list-page rendering (Wireframes.md B2) — a reason that applies at the Angebot level but not to individual items/sections. The consistency guarantee holds because `RecalculateTotals()` is `private`: there is no public way to mutate the Sections/Items tree without also triggering it.
  - `Angebot.VatBreakdown` is always computed on demand — no ERD column exists for it at all (nothing to denormalize), and it's a variable-shaped collection (one row per distinct VAT rate present), not a single scalar.
  - **Do not add a read-only state-exposing property (e.g. `IsEditable`) purely to let the Application layer "check before acting."** If the Application layer needs to avoid an expensive/irreversible side effect before a Domain guard would reject it, restructure the *workflow* instead (see §12) — do not grow the Domain's public surface just to answer a question the aggregate's own mutator already answers by throwing.
- **No update/remove methods exist on a child entity unless there is documented evidence supporting editability.** `AngebotItem` has no `Update`/`Remove` method: no endpoint for it is documented anywhere (Architecture §5.2's endpoint list has no PATCH/DELETE for items), so building one would be inventing behavior, not encoding it. This is **not** documented as a permanent business rule — it is left as an open question, revisited only when real evidence (a documented endpoint, an explicit business decision) appears. Contrast this with `CatalogItem.Update(...)`, which **is** built, because PermissionMatrix.md §6 explicitly documents "Edit an existing Catalog item — Admin F."
- **Never truly delete a historical record.** This is a recurring, explicit pattern across the whole domain: Leads are never deleted (PermissionMatrix §1), Invoices are voided not deleted (BR-9), a completed Inspection becomes immutable rather than deletable content (BR-10), and Catalog items are retired via `IsRetired`, never physically deleted (BR-12). When a new "delete" requirement appears anywhere in this system, the default assumption is **retire/void, not delete**, unless a rule explicitly says otherwise.

---

## 3. CQRS-lite Without a Mediator

- **No MediatR, no pipeline behaviors, no reflection-based dispatch.** Every command has one hand-written handler, called directly.
- The only shared abstraction is:
  ```csharp
  public interface ICommandHandler<in TCommand, TResult>
  {
      Task<TResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
  }
  ```
  Defined in `RenoTrack.Application.Common`.
- **Why:** This project is explicitly educational — every execution path must be a plain, traceable method call a reader can jump straight to via "Go to Definition," with no hidden pipeline magic. MediatR's pipeline behaviors (validation, logging, etc. auto-injected) would hide exactly the orchestration steps this project exists to teach. If the project ever outgrows this (many more cross-cutting concerns needing injection), introducing MediatR later is a contained, reversible change — but that bar has not been reached as of Phase 2.
- **Read/write split is real, not just naming:** commands mutate aggregates via repository `GetByIdAsync`/`AddAsync`-style methods; queries (not yet built as of this writing) are expected to return DTOs directly via purpose-built query interfaces, bypassing full aggregate hydration, per the same reasoning as repository growth (§4).

---

## 4. Repository Growth on Demand — Never Speculative

- **Every repository interface starts with the absolute minimum a single, real command needs — usually just `AddAsync`.** `GetByIdAsync`, `HasActiveAngebotForLeadAsync`, etc. are added only when a specific, currently-being-built command actually needs them, never "while we're at it" or "we'll need this eventually."
  - `ILeadRepository` started with `AddAsync` only (for `CreateLeadCommand`); gained `GetByIdAsync` only when `ScheduleInspectionCommand` needed to load an existing Lead.
  - `IInspectionRepository` started with `AddAsync` only; gained `GetByIdAsync` only when `CompleteInspectionCommand` needed it.
  - `IAngebotRepository` started with `AddAsync` + `HasActiveAngebotForLeadAsync` (both needed by `CreateAngebotCommand`); gained `GetByIdAsync` only when `AddAngebotSectionCommand` needed to load and mutate an existing Angebot.
  - `IAngebotReviewCommentRepository` has only `AddAsync` as of this writing — no query method exists because no current command needs to read comments back.
- **A repository method name should express the exact business question it answers, not a generic CRUD verb.** `HasActiveAngebotForLeadAsync(int leadId)` (StateMachine §2.4's "only one non-terminal Angebot per Lead" check) is preferred over a generic `GetByLeadIdAsync` that returns entities the caller then filters — the repository, not the caller, should know what "active" means for that aggregate.
- **A repository loaded via `GetByIdAsync` always returns the full aggregate — there is no partial-load contract.** In DDD, the aggregate's full object graph *is* the consistency boundary; there is no legitimate "shallow" load of a root. Whether Infrastructure achieves that via EF Core's `Include`/`ThenInclude` is an implementation detail invisible to this interface. (Sequence Diagram.md's own wording, `GetByIdWithDetailsAsync`, is EF-flavored jargon from the diagram's authors, not a second contract Application needs to expose.)
- **No `UpdateAsync` method exists on any repository.** Mutation happens by loading the aggregate, calling its own methods, and committing via `IUnitOfWork.SaveChangesAsync()` — the change-tracking mechanism (EF Core, arriving in Phase 3) picks up the mutation automatically. Adding an explicit `UpdateAsync` would imply a second, redundant way to persist a change.

---

## 5. FluentValidation — Shape Only, Never Business Rules

- **Validators check only the shape of the request:** required fields present, plausible formats (e.g. `.EmailAddress()`), numeric ranges that are obviously nonsensical (`GreaterThan(0)` on an id). They never check anything that requires loading an aggregate or another entity.
- **Domain's own guards remain the real backstop**, not a redundant duplicate to be trusted blindly. E.g. `CreateLeadCommandValidator` checks `Name`/`Phone` aren't empty; `Lead.Create(...)` also checks this itself. The validator exists to give a friendly, field-level 400 before the Domain is even touched — it is not the only line of defense.
- **A validator never queries a repository.** "Does this Lead exist?", "Is this the right owner?" are handled in the handler (see §8, §9), never inside an `AbstractValidator<T>`.

---

## 6. Thin Handlers — The Canonical Shape

Every command handler follows this shape, with no deviation unless explicitly justified in review:

```
1. Validate the command (FluentValidation — shape only)
2. Load the aggregate(s) needed, throwing NotFoundException if missing
3. Enforce any resource-ownership rule (IOwnershipValidator) or note explicitly why none applies (role-based only)
4. Invoke exactly the Domain method(s) needed — no branching business logic beyond
   "which side effect to trigger" (e.g. only notify if Source == Website)
5. Persist via IUnitOfWork.SaveChangesAsync()
6. Record an audit entry, if this is a business milestone (see §10)
7. Send a notification, if SRS/Sequence Diagrams document one (see §11) — always after step 5, never before
8. Map the resulting aggregate to a DTO and return it
```

- **A handler never contains an `if` statement that encodes a business rule.** It may contain an `if` that decides *which side effect to trigger* (e.g. "only send this notification when Source is Website") — that is orchestration, not business logic. If a handler is making a decision about whether an *aggregate transition is allowed*, that logic has leaked out of the Domain and must be moved back in.
- **A handler never checks `Status`, `CompletedAt`, or any other aggregate-state field itself before calling a Domain method.** It calls the Domain method and lets it throw. The one narrow exception is when the *order* of operations matters for a non-Domain reason (see §12 — file storage ordering), never to duplicate a state check.
- When a handler's growing complexity tempts a helper method or extracted class, treat that as a signal that the responsibility belongs in the Domain, not as license to keep growing the handler with more helper methods of its own.

---

## 7. DTO Strategy

- **DTOs are `sealed record` types, one per response shape**, living in `<Feature>/Dtos/`.
- **Start with only the fields the current command's response actually needs — a "header-level" DTO, not the full nested tree.** `AngebotDto` was introduced with scalar/header fields only (`Id, LeadId, InspectionId, AngebotNumber, Status, CreatedByInspectorId, ReviewedByAdminId, SentAt, DecisionAt, CreatedAt, NetTotal, GrossTotal`) — no `Sections` list. `SectionDto` was introduced later, when `AddAngebotSectionCommand` actually needed to return one, with no nested `Items` list of its own. The same discipline as repository growth (§4) applies to DTOs: add a field or a nested DTO only when a real use case returns it.
- **Domain value objects never appear directly in a DTO.** `Money` is unwrapped to a plain `decimal` (`angebot.NetTotal.Amount`); enums (`LeadStatus`, `AngebotStatus`, `LeadSource`, `VatRate`) are passed through as-is since they serialize cleanly and carry no Domain behavior worth hiding.
- **Mapping is a `public static class <Entity>MappingExtensions` with a `ToDto()` extension method**, hand-written, living in the same file as the DTO it maps to (e.g. `LeadDto.cs` contains both `LeadDto` and `LeadMappingExtensions`).

---

## 8. Mapping Strategy — No AutoMapper

- **Manual, explicit mapping only.** No AutoMapper or any reflection-based mapping library.
- **Why:** at this project's scale, the boilerplate AutoMapper saves is small, and the cost — hidden mapping logic that isn't visible via "Go to Definition," configuration profiles that must be learned separately from the C# they affect — is not worth it for a project whose explicit goal is that every step be traceable by a junior developer reading the code. If the DTO surface grows dramatically in a later phase, this can be revisited, but as of Phase 2 it has not needed to be.

---

## 9. Ownership Strategy — `IOwnershipValidator`

- **Resource-ownership rules are Application-layer business invariants, not authorization attributes**, because they require the *loaded* aggregate to evaluate (see §16 for the full role-vs-ownership distinction).
- **One interface, `IOwnershipValidator`, growing by named method — never a generic `EnsureOwnedBy(int, int, string, int)` id-comparison helper.** Each method names the exact business relationship it checks, even when the underlying comparison is trivially the same shape as another method:
  ```csharp
  public interface IOwnershipValidator
  {
      void EnsureInspectionOwnership(Inspection inspection, int inspectorId);
      void EnsureLeadOwnership(Lead lead, int inspectorId);
      void EnsureAngebotOwnership(Angebot angebot, int inspectorId);
      // future: EnsureXxxOwnership(...) for any new ownership relationship
  }
  ```
- **Why named methods over one generic helper:** reading `ownershipValidator.EnsureAngebotOwnership(angebot, inspectorId)` at a call site tells you *which business rule* is being enforced without cross-referencing a `resourceName` string parameter. A little duplication behind expressive, intention-revealing method names is preferred over maximizing code reuse through a generic comparison utility that would gradually accumulate unrelated ownership rules with no distinguishing names.
- **The extraction threshold was three occurrences, decided explicitly, not on the first duplicate.** The check first appeared in `CompleteInspectionCommandHandler`, was duplicated verbatim in `UploadInspectionPhotoCommandHandler`, and only extracted into the shared service once `UpdateInspectionNotesCommandHandler` needed it a third time — with the *recurrence in a genuinely different aggregate relationship* (Angebot ownership, arriving one command later) confirming the abstraction was worth generalizing to an interface rather than just de-duplicating within one entity type.
- **`OwnershipValidator` (the concrete implementation) lives directly in `RenoTrack.Application`, not `RenoTrack.Infrastructure`** — unlike every other service interface (`IFileStorage`, `IEmailSender`, `IAuditService`, `INumberGeneratorService`), it has zero external dependency (no EF Core, no disk, no network, no SMTP) to justify an Infrastructure-side implementation or a future swappable alternative. It is still expressed as an interface (rather than a concrete class injected directly) for two reasons: consistent DI registration alongside every other service, and so a handler test could substitute a fake if that ever became useful — not because a second implementation is expected.
- **Not every command needs `IOwnershipValidator`.** See §16 — when PermissionMatrix.md marks an action `F` (full access) rather than `S` (scoped), no ownership check exists at all, and using `IOwnershipValidator` there would be a semantic error (mixing "who owns this specific record" with "does this role have blanket authority").

---

## 10. Audit Strategy

- **`IAuditService.LogAsync(entityType, entityId, action, performedByUserId, details, cancellationToken)`** is the only interface. `action` is a strongly-typed `AuditAction` enum (see below), never a free string.
- **`AuditAction` is one shared enum in `RenoTrack.Application.Common`, growing by one value per new use case, never pre-populated speculatively.** As of this writing: `LeadCreated, InspectionScheduled, InspectionDone, AngebotCreated, AngebotSubmittedForReview, AngebotApproved, AngebotChangesRequested`.
  - **Why one shared enum, not per-entity enums (`LeadAuditAction`, `AngebotAuditAction`, ...):** audit actions are a cross-cutting concern (just a label), not domain-specific business logic; one enum gives a single place to see every possible action across the system, useful for whoever eventually builds the Audit Log UI (PROJECT_ROADMAP.md Phase 15).
  - **Why enum values are entity-prefixed (`AngebotSubmittedForReview`, not just `SubmittedForReview`) even though `entityType` is already a separate string parameter:** a self-descriptive value reads correctly on its own when scanning a raw list of audit actions in code or logs, without needing to cross-reference the `entityType` alongside it. This mild redundancy is a deliberate, repeated choice throughout the project (see also `Money.RoundedPerBR11`, `Lead.MarkInspectionScheduled`) — explicit naming wins over terseness.
- **Audit is reserved for business milestones — explicit, meaningful workflow events — never for every mutating action.** The deciding question for any new command is: *"does this represent a transition someone reviewing the entity's history would want to see, or is it incidental, in-progress editing?"*
  - Logged: Lead creation, Inspection scheduled/completed, Angebot created/submitted-for-review/approved/changes-requested — all genuine milestones.
  - **Not** logged: `UploadInspectionPhotoCommand`, `UpdateInspectionNotesCommand` (attaching evidence / editing notes — operational activity, not a milestone; Sequence Diagram §3 itself omits an audit step for these, reinforcing the classification), `AddAngebotSectionCommand` (editing an existing draft — even on the rare occasion it triggers the internal `ChangesRequested → Draft` auto-transition, that transition is "making the draft editable again," not a new business event from the user's perspective).
- **The audit target is the aggregate whose state the business actually cares about — not necessarily the aggregate that was created or directly mutated by this specific command.** `AuditLog` (per ERD.md) has **no cross-entity linkage column** (only `EntityType`/`EntityId`, with a recommended index on exactly that pair "for fetching an entity's full history efficiently") — there is no way to recover "everything that happened around this Lead" from a child-entity-typed audit row. Combined with Wireframe C1's per-Lead "Activity Timeline" being the only documented audit UI, the rule is:
  - If a command's real side effect is a business-meaningful transition on **Lead** (even though a *different* aggregate got created), log against **Lead**. Example: `ScheduleInspectionCommandHandler` and `CompleteInspectionCommandHandler` both log against `Lead`, not `Inspection`, because scheduling/completing an Inspection is what actually drives `Lead.MarkInspectionScheduled()`/`MarkInspectionDone()` — Lead-level business milestones.
  - If a command's transition is purely internal to **Angebot** and never touches `Lead.Status` (StateMachine.md §1.3 explicitly states Angebot's internal review states cause "no Lead-level change"), log against **Angebot**. Example: `SubmitAngebotForReviewCommandHandler`, `ApproveAngebotCommandHandler`, `RequestAngebotChangesCommandHandler` all log against `Angebot`.
  - `CreateAngebotCommandHandler` logs against `Lead` (not the newly-created `Angebot`) because creating the draft is what drives `Lead.MarkAngebotInProgress()` — this was a real inconsistency found in Sequence Diagram.md §4 (it omitted an audit step entirely for Angebot creation) and corrected in both the diagram and the implementation, once this general principle made the omission look like an oversight rather than an intentional choice.
  - The comment/support-entity itself (`AngebotReviewComment`) is never the audit target — it is supporting business data, not the workflow milestone.

---

## 11. Notification Strategy

- **`IEmailSender` methods are one explicitly-named method per notification type**, never a generic `SendAsync(string template, string to, object data)`. This matches the project's consistent preference for named operations over string-keyed ones (see also `Money`, `ItemUnit`, `AuditAction`).
- **Every notification takes a dedicated notification model from `RenoTrack.Application.Common.Notifications`, never a feature DTO.** This was a deliberate correction: `IEmailSender.SendNewWebsiteLeadNotificationAsync` originally took a `LeadDto` directly, which made `RenoTrack.Application.Common` (meant to be the lowest-level part of Application) depend on the `Leads` feature folder — backwards from the intended dependency direction (feature folders depend on Common, never the reverse). The fix: `NewWebsiteLeadNotification` is its own record with only the fields the email template actually needs (`LeadId, LeadName, LeadPhone, LeadEmail` — deliberately narrower than the full `LeadDto`, since a notification email has no use for `Address`/`Notes`/`Status`/`AssignedInspectorId`/`CreatedAt`).
- **A notification is sent only if SRS.md/Sequence Diagram.md actually document one for that action — never added speculatively "to be safe."** SRS FR-9.2 names exactly three Admin-facing triggers (new website Lead, Angebot submitted for review, Lead decision received); Sequence Diagram §5 additionally depicts an Inspector-facing notification ("Notify Inspector with comment") not covered by FR-9.2's own enumeration (which is Admin-notifications only) — this is a minor SRS completeness gap, not a contradiction, and the diagram was followed since it's explicit and the behavior is obviously sensible.
- **A notification is always sent after `IUnitOfWork.SaveChangesAsync()` succeeds, never before.** No handler sends a notification about a state that hasn't actually been committed yet.
- **Notifications have no implementation as of Phase 2** — `IEmailSender` is defined in Application only; Infrastructure implements it for real in Phase 9 (SRS OQ-3 must be resolved first: which email provider). Until then, a no-op/logging Infrastructure implementation lets handlers run end-to-end in Phases 3–8 without a real mail provider.

---

## 12. Side-Effect Ordering — Stable External Resource Identifiers

- **General principle (recorded in Architecture.md §9):** *the Application layer is responsible for generating stable external resource identifiers before invoking external infrastructure, whenever doing so lets a Domain guard reject before an irreversible side effect.*
- **Origin of this rule — a real bug caught and fixed during Phase 2:** `UploadInspectionPhotoCommandHandler`'s first draft called `IFileStorage.SaveAsync(...)` (an actual file write) **before** calling `Inspection.AddPhoto(fileUrl, caption)` (which enforces BR-10 — no photos after completion). If the Inspection was already completed, the file got written to storage anyway, and only then did `AddPhoto` throw — leaving an orphaned file with no corresponding `InspectionPhoto` row, on every attempt to upload to a completed Inspection.
- **The fix was a workflow reordering, not a new Domain API.** The rejected alternative was adding an `Inspection.IsEditable` read-only property so the handler could check before uploading — rejected because it would expose Domain state primarily to optimize an Application workflow, introducing a second way to ask the same question BR-10's guard already answers by throwing. The actual fix: the handler **computes the `FileUrl` itself** (a GUID-based key, pure string computation, no I/O) **before** calling `IFileStorage.SaveAsync`, and calls `Inspection.AddPhoto(fileUrl, caption)` — which enforces BR-10 — *before* the actual upload. If Domain rejects it, `IFileStorage.SaveAsync` is never called at all.
  ```csharp
  var fileUrl = $"inspections/{inspection.Id}/{Guid.NewGuid()}{Path.GetExtension(command.FileName)}";
  var photo = inspection.AddPhoto(fileUrl, command.Caption); // BR-10 guard fires here — before any I/O
  await fileStorage.SaveAsync(command.FileContent, fileUrl, cancellationToken);
  await unitOfWork.SaveChangesAsync(cancellationToken);
  ```
- **Consequence for `IFileStorage`'s shape:** `SaveAsync(Stream content, string fileUrl, CancellationToken ct)` takes the caller-supplied key; it does not invent and return one. This is expected to recur for invoice PDFs or any other generated/stored document — whenever a Domain aggregate needs to reference a not-yet-created external resource by a value it can validate up front, compute that value in Application before the I/O, not after.

---

## 13. File Storage Principles

- **`IFileStorage` lives in `RenoTrack.Application.Common.Interfaces`**, implemented by `LocalDiskFileStorage` in `RenoTrack.Infrastructure` (Phase 4 — `PROJECT_ROADMAP.md`'s Phase 4 deliverable list, not Phase 3; corrected during Phase 3's design review, see `ARCHITECTURE_DECISIONS.md`), swappable later for Azure Blob/S3 with zero change to calling code (Architecture.md §9). Phase 3 registers only a minimal placeholder so DI composition succeeds.
- **Starts with only `SaveAsync(Stream content, string fileUrl, CancellationToken ct)`.** `GetAsync`/`DeleteAsync` (both named in Architecture §9's original description) are deliberately not built yet — no current command/query needs them. Add them only when one does, per the general repository-growth discipline (§4) applied to this interface too.
- **The caller determines the `fileUrl`/key up front** — see §12. `IFileStorage` never invents an identifier.

---

## 14. Testing Philosophy

- **Every Domain entity gets its own `RenoTrack.Domain.Tests` test class**, testing only through the entity's public API — no reflection to bypass a guard, no test-only public setters added to production code.
- **Application handler tests use hand-written in-memory fakes**, never a mocking framework (no Moq/NSubstitute). Each fake implements exactly one repository/service interface and exposes simple public collections/counters (`AddedLeads`, `SaveChangesCallCount`, `Calls`) for assertions. Fakes live in `tests/RenoTrack.Application.Tests/Fakes/`.
- **A "Seed" test helper simulating database-assigned identity uses reflection, and only in test code, never production code.** Since aggregate `Id` properties have `private set` and are normally assigned by EF Core (not yet built as of Phase 2), a fake repository's `Seed(entity)` method uses `typeof(T).GetProperty(nameof(T.Id))!.SetValue(entity, id)` to simulate what persistence will eventually do. This is explicitly sanctioned as test infrastructure only — the moment any production code needs reflection to assign an id, that is a bug, not a pattern to follow.
- **Aggregate-boundary claims are verified by reflection-based tests, not just by convention/comment.** E.g. `InspectionPhoto`/`AngebotSection` have tests asserting `GetConstructors(BindingFlags.Public | BindingFlags.Instance)` is empty, and that a would-be public mutator method cannot be resolved via `GetMethod(name, BindingFlags.Public | BindingFlags.Instance)`. Independent-aggregate separation (e.g. `AngebotReviewComment` vs. `Angebot`) is verified by asserting neither type's properties/fields (including generic type arguments, to catch a hidden `List<T>`) reference the other's type.
- **A guarded state machine is tested exhaustively, not just for the happy path.** The established pattern (`Lead`, `Angebot`) is: drive the aggregate to every possible state via its own real transition methods (never a backdoor), then assert a given transition method succeeds from exactly its documented "from" state and throws `InvalidOperationException` (naming both the actual and expected state in the message) from every other state.
- **A failing test that reveals a mistake in the test's own expectation (not the code) is still valuable — fix the assertion, don't discard the test.** Example: `AngebotItemTests`'s first realistic `LineTotal` example had an arithmetic error in the expected value (`255.5112` instead of the correct `255.5712`); the test caught it immediately, proving the production code was already correct.
- **`RenoTrack.Infrastructure.Tests` (Phase 3) runs real integration tests against SQL Server LocalDB — never the EF Core InMemory provider.** InMemory doesn't enforce real SQL constraints/types (unique indexes, foreign keys, `decimal(18,2)` precision), which is exactly what this layer exists to verify. Tests share one LocalDB database per test run via an `ICollectionFixture`, forcing xUnit to run them serially (never in parallel against the same database) rather than isolating per-class.
- **`dotnet build` must show 0 Warnings, 0 Errors and `dotnet test` must show 100% passing before any slice is considered complete or committed.** `TreatWarningsAsErrors` is enabled solution-wide (`Directory.Build.props`), with a narrow, explicit `WarningsNotAsErrors` escape hatch for specific NuGet advisory IDs only when consciously accepted.
- **A concurrency test passing once is not proof it's correct — run it several times before trusting it.** A real bug (D55) hid behind a concurrency test (`IdentityRoleSeederTests`'s 10-concurrent-instance race proof) that had genuinely passed when first written, then failed ~2/3 of the time on repeated local runs during final pre-merge verification. The root cause (shared `DbContext` tracking state poisoning a second, unrelated unit of work after the first one failed) was real and would have shipped undetected behind a single green run. Whenever a test exercises a race condition, rerun it several times (not just once) before treating a pass as confirmation — a single pass proves the race *can* succeed, not that it always does.
- **CI must exercise the same real dependencies a local run does — never substitute a weaker one just to make a pipeline green.** `RenoTrack.Infrastructure.Tests` requires real SQL Server LocalDB (D40), which only exists on Windows; CI is split into two jobs by OS specifically so this project's LocalDB-only-tests rule doesn't get quietly compromised by Linux-runner convenience (`.github/workflows/ci.yml`; `build-and-test` on `ubuntu-latest`, `infrastructure-tests` gated `needs: build-and-test` on `windows-latest`) — see `ARCHITECTURE_DECISIONS.md` D56.

---

## 15. Documentation-First Workflow

- **When a design review reveals a genuine business rule, contradiction, or gap that isn't yet documented, the documentation is updated *before* the code that depends on it is written**, in the same PR. This has happened repeatedly and is treated as the default, not an exception:
  - BR-10 (Inspection immutability after completion), BR-11 (monetary rounding strategy), BR-12 (Catalog retirement, not deletion), BR-13 (scheduling an Inspection assigns its Inspector to the Lead) were all added to `BusinessRules.md` mid-implementation, each with a Changelog row recording when and why.
  - `ERD.md` gained `CatalogItem.IsRetired` before `CatalogItem.Retire()` was written.
  - `StateMachine.md` and `PermissionMatrix.md` were both corrected (not just Architecture.md) when BR-13 was discovered.
  - `Sequence Diagram.md` §4 was corrected to add a previously-missing `AuditLog` step for Angebot creation, and to fix a stale `Angebot.CreateDraft(...)` reference (renamed to `Angebot.Create(...)` during Phase 1 review) — once the general audit principle (§10) made the omission look like an oversight in the diagram, not a deliberate choice.
- **A new architectural principle discovered while solving one problem is written down as a general rule in `Architecture.md`, not left implicit in one command's implementation.** E.g. §12's "stable external resource identifiers" principle, §7.3's role-vs-ownership distinction (§16 below), §11's "audit target" principle (§10 above) were all extracted as reusable, explicitly-worded rules the moment a second (or anticipated future) use case would need the same reasoning.
- **When a contradiction is found between two documents** (e.g. PermissionMatrix.md granting Admin a "delete" action on Catalog items while ERD.md had no field to represent it — resolved as BR-12), the resolution updates **all** affected documents, cross-referencing the new BR/decision, not just the one document that happened to be open at the time.

---

## 16. Role-Based Authorization vs. Resource Ownership

Recorded formally in `Architecture.md` §7.3. Two different concerns, both loosely called "authorization," belong in different layers:

- **Role-based authorization** ("is this caller an Admin/Inspector at all?") is an **API-layer** concern: `[Authorize(Roles = "...")]` attributes (Architecture §7.1), enforced before a request ever reaches a handler. It needs no Domain data — the JWT's role claim alone is enough.
- **Resource-ownership rules** ("is this caller *the specific* Inspector/Admin this record is scoped to, not just *any* holder of that role?") are an **Application-layer** concern, enforced via `IOwnershipValidator` (§9) — because they require the *loaded* aggregate to evaluate, and the handler already loads it to do its real work.
- **How to tell which applies, mechanically: check PermissionMatrix.md's letter for that exact action.**
  - **`S` (scoped)** → resource-ownership rule → use `IOwnershipValidator`. E.g. "Mark Inspection complete — Inspector S, assigned Inspector only" → `EnsureInspectionOwnership`. "Add/remove Sections & Items — Inspector S, owning Inspector only" → `EnsureAngebotOwnership`.
  - **`F` (full access)** → role-based only → **no** `IOwnershipValidator` call in the handler at all. E.g. "Approve Angebot — Admin F" and "Request changes — Admin F" → `ApproveAngebotCommandHandler`/`RequestAngebotChangesCommandHandler` have zero ownership-check code; any authenticated Admin may act on any Angebot, full stop.
- **Using `IOwnershipValidator` for an `F`-marked action would be a semantic error**, not just unnecessary code — it would mix "does this specific person own this specific record" with "does this role have blanket authority," weakening what the abstraction means everywhere else it's used. When a command's authorization model is `F`, that absence is not an inconsistency to be reconciled with other handlers; it is the correct reflection of a different business rule.

---

## 17. Exception Strategy

Three Application-layer exception types exist, added one at a time exactly when first needed (never speculatively), each intended for a specific future HTTP status mapping in Phase 4's API middleware (Architecture §5.3, RFC 7807 ProblemDetails):

| Exception | Meaning | Introduced in | Expected HTTP mapping |
|---|---|---|---|
| `NotFoundException` | The requested aggregate id does not exist | `ScheduleInspectionCommand` (first command that loads an existing aggregate) | 404 |
| `ForbiddenException` | A resource-ownership rule was violated (§9, §16) | `CompleteInspectionCommand` (first `IOwnershipValidator` use) | 403 |
| `ConflictException` | The request conflicts with the aggregate's own current business state (not ownership, not missing) | `CreateAngebotCommand` ("Lead already has an active Angebot", StateMachine §2.4) | 409 |

- FluentValidation's own `ValidationException` (thrown by `ValidateAndThrowAsync`) is used as-is for shape-validation failures — expected mapping: 400.
- Domain's own `ArgumentException`/`InvalidOperationException` (guard failures inside aggregate methods) are allowed to propagate unwrapped out of handlers — Application does not catch and re-wrap them into a different exception type. Their exact HTTP mapping (likely 400 for `ArgumentException`, 409 for `InvalidOperationException`) is deferred to Phase 4's middleware design, not resolved now.
- **No generic base "AppException" class exists or is planned.** Each exception type is added because a specific, real scenario needed a specific, named exception — not to build out a taxonomy in advance.

---

## 18. Number Generation Principles

- **`INumberGeneratorService`** (`RenoTrack.Application.Common.Interfaces`) is intentionally minimal: `Task<string> NextAngebotNumberAsync(int year, CancellationToken ct)`, returning a fully-formatted string (e.g. `"ANG-2026-00042"`).
- **The Infrastructure implementation (`NumberGeneratorService`, Phase 3 Slice 11) guarantees uniqueness under concurrent requests, but not via the literal "same database transaction as the entity being numbered" wording this section originally specified.** `CreateAngebotCommandHandler` calls `NextAngebotNumberAsync` before the `Angebot` entity even exists in memory, so true same-transaction participation was never achievable without restructuring that handler. Instead, a single independently-committed atomic SQL statement (`UPDATE ... OUTPUT`, raw SQL — a deliberate, narrowly-scoped exception to this project's EF-Core-only convention) guarantees uniqueness with a row-level lock held only for that one statement. See `ARCHITECTURE_DECISIONS.md` D52 for the full reasoning, including why EF Core's read/track/write model cannot express atomic increment-and-return as one round trip. **This was the single highest-risk unverified assumption carried out of Phase 2 — it is now verified**, proven by a 50-parallel-caller concurrency integration test, not just a code review.

---

## 19. Git Workflow (established in Phase 0, still binding)

- **Never force-push to `main`.** Established after an incident in Phase 0 where a force-push overwrote a merge the user had just performed on GitHub in parallel — recoverable only because the old commit objects hadn't been garbage-collected. See `ARCHITECTURE_DECISIONS.md` for the full incident record.
- **Every phase (or, within Phase 2, every meaningfully-sized group of vertical slices) is developed on its own branch and merged to `main` only via Pull Request.** No direct commits to `main` after Phase 0's initial bootstrap.
- Before starting a new branch, always `git fetch origin` and reconcile with the actual remote state — never assume the local view of `origin/main` is current, especially right after handing the user a PR link (they may merge it before you act again).
- Within Phase 2, multiple vertical slices accumulate as separate commits on one long-lived feature branch (`feature/phase-2-application-layer`), pushed and opened as a PR once a natural milestone is reached — not one PR per slice, mirroring how Phase 1 accumulated multiple entities into one PR.

---

## 20. Project-Specific Terminology Reminders

- **Angebot** = quote/offer (German commercial document). **Rechnung/Invoice** = invoice. **Zwischensumme** = section subtotal. **Gesamtsumme** = grand total. **MwSt** = Mehrwertsteuer (German VAT).
- **"Inspector" and "Admin"** are the only two internal dashboard roles (ASP.NET Core Identity + JWT). **Lead/Customer** never has a dashboard account — token links only (Architecture §7.2).
- Money amounts are always `decimal(18,2)` in the database and always exact to 2 decimal places in `Money` (Domain value object) — see `ARCHITECTURE_DECISIONS.md` entry on BR-11 for the full rounding policy.

---

## 21. Infrastructure Layer — EF Core Conventions (Phase 3, complete — all 15 slices)

- **One `IEntityTypeConfiguration<T>` class per entity**, in `RenoTrack.Infrastructure/Persistence/Configurations/`, picked up via `modelBuilder.ApplyConfigurationsFromAssembly(...)` — never configured inline in `OnModelCreating`.
- **`RenoTrackDbContext` exposes a `DbSet<T>` per aggregate root only.** Child entities (`AngebotSection`, `AngebotItem`, `InspectionPhoto`) have no `DbSet` — reachable only through their aggregate root's navigation, extending `CLAUDE.md` §2's "aggregate roots are the only public entry point" rule into how the persistence layer is queried.
- **Only entities that exist in the Domain today get a `DbSet`/configuration.** `NumberSequence`/`AuditLog`/Identity (`AspNetUsers`/`AspNetRoles`/etc.) were added exactly when Slices 10, 11, and 15 respectively needed them — not speculatively ahead of that. No speculative schema still exists for `Customer`/`Project`/`Invoice`/`InvoiceLine`/`Payment`/`TokenLink` — each is added in whichever future phase actually introduces the Domain concept that needs it, mirroring the repository/DTO growth-on-demand discipline (§4) applied to the schema itself.
- **Value converters** (`Persistence/ValueConverters/`): `MoneyConverter` (`Money` ↔ `decimal`, via `.Amount`/`Money.FromExact`), `ItemUnitConverter` (`ItemUnit` ↔ `string`, via `.Code`/`ItemUnit.FromCode`). `VatRate` needs no converter — its underlying enum values (`0/7/16/19`) already are the percentages, so EF's default enum-to-int mapping is correct as-is.
- **Computed Domain properties are `.Ignore()`d explicitly**, never silently left unmapped: `AngebotSection.Subtotal`, `AngebotItem.LineTotal`, `Angebot.VatBreakdown`. `ERD.md` was corrected to match this (D41) — it had stale columns for the first two, and a `DecisionResult` column for a property removed from the Domain entirely (D16).
- **Encapsulated child collections require two explicit configuration steps, not one:** `.HasMany(x => x.Children).WithOne().HasForeignKey("ShadowFkName")` binds the shadow FK, **and** `.Navigation(x => x.Children).UsePropertyAccessMode(PropertyAccessMode.Field)` is required because the collection property is `IReadOnlyList<T>` with no public setter over a private `List<T>` field. **`.IsRequired()` must also be called explicitly on the relationship** — without it, EF Core defaults the shadow FK column to nullable, which is wrong for a required composition (a photo/section/item always belongs to exactly one parent). This was found as a real bug in the first generated migration (D46), not designed in from the start — verify with an actual migration/integration test, don't assume the convention does the right thing.
- **FK constraints are added for any relationship where both tables already exist**, even between independent aggregates related "by id only" at the Domain level (e.g. `AngebotItem.CatalogItemId → CatalogItems`, `CatalogItem.CreatedFromAngebotItemId → AngebotItems`, `AngebotReviewComment.AngebotId → Angebote`, `Inspection.LeadId → Leads`, `Angebot.LeadId → Leads`, `Angebot.InspectionId → Inspections`) — `DeleteBehavior.Restrict` in every case (nothing in this schema is ever hard-deleted, so cascade behavior would never trigger, but `Restrict` is the correct safe default regardless). **User-referencing FK constraints were the one deliberate exception, deferred until the Identity slice (Slice 15) added the `AspNetUsers` table — now resolved.** `Lead.AssignedInspectorId`, `Inspection.InspectorId`, `Angebot.CreatedByInspectorId`/`ReviewedByAdminId`, `AngebotReviewComment.AdminUserId` all have real `Restrict` FK constraints as of Slice 15 (D44's deferral, D53/D54 for the Identity slice itself).
- **No generic `Repository<TEntity>` base class.** Hand-written, per-aggregate repository classes implementing the already-defined narrow Application-layer interfaces — same anti-generic-abstraction stance as `IOwnershipValidator` (D28).
- **Before generating any migration, perform a three-way comparison** — Domain code ↔ EF configurations ↔ `ERD.md` — explicitly, not just trusting the configuration code compiles. This caught three missing FKs before Slice 2's migration was ever generated (D45).
- **Migrations are a product of the model, never hand-edited.** If a configuration was wrong after a migration was already generated, the fix is: correct the configuration, `dotnet ef migrations remove` (only safe pre-apply — never remove a migration already applied to a shared database), then regenerate from scratch. Never patch the generated `Migration` class's `Up`/`Down` methods by hand.
- **Every generated migration is manually reviewed before being considered complete** — check every operation is expected, no accidental cascade deletes, no unnecessary columns, no unexpected tables, no missing Domain concept. This caught a real nullability bug in Slice 2 that the pre-migration schema review had not (D46) — the two reviews catch different classes of mistake, neither is redundant with the other.
- **`RenoTrack.Infrastructure.Tests` (a deliberate addition beyond `Architecture.md`'s originally-documented three-test-project structure, D40) uses real SQL Server LocalDB, never the EF Core InMemory provider** — InMemory doesn't enforce the unique constraints, FKs, or `decimal(18,2)` precision this layer exists to verify. All tests share one LocalDB database per test run via an `ICollectionFixture`, forcing xUnit to run them serially against it (never in parallel).
- **`IUnitOfWork`'s Infrastructure implementation is an intentionally thin, one-line wrapper** over `DbContext.SaveChangesAsync` — no transaction API (EF Core's own implicit per-`SaveChanges` transaction already covers every handler's needs), no `IDisposable` (it doesn't own the injected, DI-scoped `DbContext`) (D48).
- **Design-time migration tooling uses `IDesignTimeDbContextFactory<RenoTrackDbContext>`** (`RenoTrackDbContextFactory`), not the running application's DI composition — that composition (`AddInfrastructure()` + `Program.cs` wiring) was deliberately built as a separate, later slice (Slice 14, D47), not bundled with the factory.
- **A component that must perform several independent units of scoped work outside a single request scope (e.g. startup-time seeding across multiple items, each needing its own `DbContext`/`RoleManager` lifetime) is a dedicated class registered in DI with `IServiceScopeFactory` injected through its constructor — never a static utility method, and never `IServiceScopeFactory`/`IServiceProvider` accepted as a per-call method parameter.** `IdentityRoleSeeder` (`src/RenoTrack.Infrastructure/Identity/IdentityRoleSeeder.cs`) is the established example: `AddScoped<IdentityRoleSeeder>()`, constructor-injected `IServiceScopeFactory`, a parameterless public `SeedRolesAsync()` that opens one fresh `IServiceScope` per independent unit of work internally. This is Microsoft's own documented pattern for `BackgroundService`/`IHostedService`-style orchestration and is not a service-locator anti-pattern *as long as* the component resolves a small, fixed, visible set of named types from each scope — never an arbitrary type chosen at runtime. See `ARCHITECTURE_DECISIONS.md` D55 for the full case study (two independent concurrency bugs found empirically, and why a `DbContext` parameter, a method-parameter `IServiceScopeFactory`, and a static-utility shape were each considered and rejected first).

---

## 22. API Layer — Conventions (Phase 4, in progress)

- **Routes are versioned by literal URL segment: `[Route("api/v1/[controller]")]`. No versioning library.** `Architecture.md` §5.1 mandates `/api/v1/...` from day one; there is exactly one version and no documented plan for a second, so `Asp.Versioning.Mvc` would be version-negotiation infrastructure for something that doesn't exist — the same speculative-abstraction failure §4 forbids for repositories/DTOs/schema, applied to routing (D57). A future v2 is added as v2 controllers alongside v1 under `api/v2/...`, so v1's behavior never silently changes underneath existing clients; a library is reconsidered only if content negotiation or deprecation headers become a real recurring need. Sub-resource routes (`POST /api/v1/leads/{leadId}/inspections`) are explicit route templates on the owning controller.
- **Controllers are `[Authorize]` by default; `[AllowAnonymous]` is opted into per action.** A forgotten `[Authorize]` silently exposes an endpoint; a forgotten `[AllowAnonymous]` merely fails closed. The known anonymous actions are `POST /api/v1/leads` (website contact form) and `POST /api/v1/auth/login`.
- **Role checks live on the controller (`[Authorize(Roles = "...")]`); ownership checks stay in the handler via `IOwnershipValidator`.** This is §16 unchanged — Phase 4 is where the API half finally exists, not a new rule. Decide mechanically from `PermissionMatrix.md`'s letter: `F` → role attribute only, no ownership call anywhere; `S` → role attribute *and* the handler's existing `IOwnershipValidator` call.
- **A controller never accepts, from the request body or query string, any value representing *who the caller is* or *what context they are acting in* (D61).** Those come from the authenticated principal, the route, or the endpoint's own fixed meaning. A request record is therefore normally a strict subset of its command's parameters, and introducing one is justified exactly when that subset differs — not one per endpoint by reflex. Concretely: the inspector id always comes from the JWT's `sub` claim, never the body; the aggregate id comes from the route. `CreateLeadRequest` omitting `Source` is the worked example, and it is not cosmetic — `Source` gates the Admin notification (FR-9.2), so a caller controlling it could suppress that notification.
- **Enums serialize as names, not ordinals** (`JsonStringEnumConverter`, D61). An ordinal contract silently changes meaning if anyone reorders an enum, the database already stores these enums as strings, and every project document names statuses rather than numbering them.
- **Controllers are thin: validate nothing, decide nothing, map nothing beyond request → command.** Every business rule already lives in a Domain aggregate or an Application handler. A controller that contains an `if` about business state has the same defect §6 describes for handlers, one layer further out.
- **`RenoTrack.Api.Tests` boots the real application via `WebApplicationFactory<Program>` against real SQL Server LocalDB — never a mocking framework, never a fake Identity store, never the EF Core InMemory provider** (D58, extending D40's stance). It tests what the API layer adds over the layers beneath it — routing, model binding, role/ownership enforcement reaching from a real JWT to a real 403, and ProblemDetails shape — with one happy path plus one representative guard failure per endpoint. It does **not** re-verify business rules that Domain/Application tests already cover exhaustively.
- **`RenoTrack.Api.Tests` creates its schema with `Database.MigrateAsync()`, not `EnsureCreatedAsync()`** — unlike `RenoTrack.Infrastructure.Tests`, whose fixture uses `EnsureCreated`. The two projects have genuinely different responsibilities: `Infrastructure.Tests` constructs a `DbContext` directly and never runs `Program.cs`, so it has no production startup path to stay faithful to; `Api.Tests` boots the real application, which in production always runs against a migrated database. `EnsureCreated` never writes `__EFMigrationsHistory`, so it would break outright if startup-time migration is chosen later. Do not "unify" these two fixtures for consistency's sake — the difference is deliberate (D58).
- **Because `Api.Tests` needs LocalDB, it runs in CI's Windows job (`database-backed-tests`), not the Linux `build-and-test` job** — the same reasoning as D56: the OS split exists so real-database tests keep using a real database, never a weaker substitute chosen for pipeline convenience.
- **Every error leaves the API as RFC 7807 `ProblemDetails`, produced by one `IExceptionHandler` with a single explicit `switch` — never one handler per exception type, never a `try`/`catch` in a controller.** The mapping (D59): `NotFoundException`→404, `ForbiddenException`→403, `ConflictException`→409, FluentValidation's `ValidationException`→400 (field-keyed `errors` dictionary), `ArgumentException`→400, `InvalidOperationException`→409, everything else→500. Adding a new Application exception type means adding one arm to that switch, nothing else.
- **Mapped exceptions surface their message as `detail`; unmapped ones never do.** Every mapped exception is authored in this codebase and phrased for a caller; an unmapped one may carry connection strings or schema names, so the 500 branch emits a fixed generic title and no `detail` member at all. This asymmetry is deliberate — do not "simplify" the fallback into the shared path.
- **`ArgumentException`→400 / `InvalidOperationException`→409 is a knowingly-accepted risk, not an oversight.** Both are BCL-wide types, so an EF Core fault could in principle surface as 409 rather than 500. It was accepted because every request-path occurrence of either type in this codebase originates in a Domain guard (Infrastructure's two `InvalidOperationException` throws are startup-only). The mitigation is that every mapped exception is logged at `Warning` **with its full stack trace**, so a mis-mapped infrastructure fault stays discoverable. Do not remove that logging. Reopen the mapping only with evidence of a real masking incident (D59).
- **`traceId` is added in `AddProblemDetails`'s `CustomizeProblemDetails`, not in the exception handler** — so it appears on every `ProblemDetails` response, including ones ASP.NET produces itself with no exception involved (e.g. model-binding 400s).
- **`AddApplication()` (`src/RenoTrack.Application/DependencyInjection.cs`) is the Application layer's composition root, and registers Application services only.** It takes no `IConfiguration`, reads no environment, and references nothing from ASP.NET Core or the generic host — that is what keeps `RenoTrack.Application.Tests` runnable against hand-written fakes with no framework involved. Application's only package dependencies are FluentValidation and `Microsoft.Extensions.DependencyInjection.Abstractions`; adding a hosting or configuration package here is a layering violation, not a convenience.
- **Every registration in both composition roots is explicit — no assembly scanning, no Scrutor, no `AddValidatorsFromAssembly`.** The list doubles as a readable inventory of every use case the application supports, and it keeps reflective magic out of production code (same stance as D22/D28). Registrations are grouped by category — validators, command handlers, query handlers, then services — not alphabetically or by creation order.
- **The "forgot to register a new handler" risk is covered by a reflection-based test, not by scanning.** `RenoTrack.Api.Tests`' `DependencyInjectionTests` reflects over the Application assembly to discover every `ICommandHandler<,>`/`IQueryHandler<,>`/`IValidator<>` implementation that exists and asserts each resolves. Reflection in the test, explicit registration in production. Note `ValidateOnBuild` alone would **not** catch a missing handler registration while no controller depends on it yet — this test is the only thing that does, so do not weaken it into a plain container-build check.
- **Handlers are registered by their interface**, not as concrete types, so controllers depend on the Application abstraction and `ICommandHandler<,>` stays load-bearing rather than becoming a decorative marker (§3).
- **Authentication is the one deliberate exception to §3's command/handler convention (D60).** `AuthController` calls `UserManager` and `ITokenService` directly, with no Application-layer command, because authentication has no aggregate, no Domain invariant, no state transition, and no audit milestone. **Do not "fix" this inconsistency** — routing it through CQRS would require an `IIdentityService` abstraction existing purely so a layer with no business rules about authentication could appear to own it. `ITokenService` correspondingly lives in `RenoTrack.Infrastructure.Identity`, not `Application.Common.Interfaces`, since Application neither consumes nor could consume it. The boundary is "does this have business rules," not "is it authentication" — if an auth concern ever acquires a real business rule, that concern becomes a command.
- **Every login failure returns an identical 401**: unknown email, wrong password, inactive account, and lockout are indistinguishable to the caller. This is a deliberate, narrow exception to the "mapped exceptions carry a useful message" policy (§22's ProblemDetails rules) — distinguishing them would make login an account-enumeration oracle. The real reason is logged server-side. Do not make these messages more helpful.
- **Refresh tokens are persisted, stored only as a SHA-256 hash, rotated on every use, and revoked as a whole chain on reuse.** Presenting an already-revoked token revokes every outstanding token for that user (stolen-token detection). `RefreshToken` is Infrastructure-only, like `AuditLog` and `NumberSequence`. Retention is until `ExpiresAt` — revoked-but-unexpired rows must be kept, because they are what makes reuse detection work. There is deliberately no cleanup job (steady state is a few hundred rows) and deliberately no logout endpoint (no requirement documents one).
- **`UserManager.CheckPasswordAsync` does not touch lockout counters** — only `SignInManager` does, and `AddIdentityCore` deliberately doesn't register it (D54). SRS FR-10.3's rate-limiting therefore depends on `AuthController` calling `IsLockedOutAsync`/`AccessFailedAsync`/`ResetAccessFailedCountAsync` explicitly. Removing those calls silently removes a documented security requirement.
- **JWT configuration is validated eagerly at startup**, failing with the exact configuration key at fault — the same fail-fast shape as the connection-string check. The signing key is never committed. `ClockSkew` is `TimeSpan.Zero`, because the 5-minute default would silently extend a 15-minute access token to twenty.
- **API documentation (OpenAPI document + Scalar UI) is Development-only**, matching the guard `MapOpenApi` already carried — the docs are a developer tool, not a public surface. The JWT bearer scheme is declared in the generated document by an `IOpenApiDocumentTransformer` so protected endpoints are exercisable by hand from the moment they exist, not only through `Api.Tests`.
