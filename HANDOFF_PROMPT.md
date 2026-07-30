# HANDOFF_PROMPT.md

Copy everything in the code block below into the first message of a brand-new conversation.

---

```
You are continuing work on RenoTrack (a renovation company's project-tracking system —
public website + admin/inspector dashboard), an existing, actively-developed project. This
is not a new project. A prior conversation ran out of context and prepared a full handoff
package so you can continue with zero loss of architectural context. Do not treat anything
below as optional reading.

BEFORE YOU DO ANYTHING ELSE, IN THIS ORDER:

1. Read CLAUDE.md in full. This is the permanent engineering-rules document for this
   repository — every convention in it (Clean Architecture, DDD, CQRS without a mediator
   library, rich domain model, thin handlers, repository-growth-on-demand, ownership vs.
   role-based authorization, audit policy, notification policy, exception strategy, and
   more) is an established, binding convention, not a suggestion you're free to deviate from.

2. Read PROJECT_STATE.md in full. This tells you exactly what exists right now: every
   aggregate, every repository/service interface, every command, every DTO, every test
   count, every deferred/incomplete piece of work, and the immediate next task.

3. Read ARCHITECTURE_DECISIONS.md in full. This is a chronological log of every significant
   decision made on this project, including the alternatives that were considered and
   rejected, and why. Several entries record real bugs that were caught and fixed (not
   hypothetical concerns) — read those carefully so you don't reintroduce the same mistakes
   (e.g. D29's file-upload ordering bug, D14's aggregate-identity-before-persistence bug).

4. Read PHASE2_PROGRESS.md in full. This is the detailed, non-summarized log of every
   vertical slice built in Phase 2 so far (CreateLeadCommand through
   RequestAngebotChangesCommand) — goals, design discussions, what was introduced, what
   documentation was updated, what tests were added, and the final outcome of each. It also
   explains, in detail, why AddAngebotItemCommand was deliberately postponed.

5. Read NEXT_STEPS.md in full. This tells you precisely what to do next, what NOT to change,
   which decisions are considered final, and which questions genuinely remain open for
   discussion.

6. Run `dotnet build RenoTrack.slnx` and `dotnet test RenoTrack.slnx` yourself. Confirm the
   test counts match what PROJECT_STATE.md states (as of the handoff: 255 tests passing —
   153 in RenoTrack.Domain.Tests, 102 in RenoTrack.Application.Tests — 0 warnings, 0 errors).
   If they don't match, stop and investigate before writing any code — something changed
   since the handoff was written, and the discrepancy itself is information you need before
   proceeding.

7. Run `git status`, `git branch --show-current`, and `git log --oneline -15`. Confirm you
   are on `feature/phase-2-application-layer` with the commit history PROJECT_STATE.md §2
   describes, and that this branch has not yet been pushed or opened as a PR.

CRITICAL WORKING RULES — THESE ARE NOT OPTIONAL:

- Never re-open an architectural decision recorded in ARCHITECTURE_DECISIONS.md or listed as
  "final" in NEXT_STEPS.md §5 unless you have discovered genuinely new evidence (a real bug,
  a newly-noticed documentation contradiction, an explicit new instruction from the user) —
  not a fresh stylistic opinion arrived at by re-reading the same documents.
- Never force-push to `main`. Always `git fetch origin` before any push and verify actual
  remote state — do not assume your local view of `origin/main` is current. This rule exists
  because of a real incident (ARCHITECTURE_DECISIONS.md D5) where exactly this assumption
  destroyed a merge the user had just performed in parallel.
- Follow the same process every prior slice in this project used: for anything touching new
  architectural territory, do analysis and get explicit user sign-off on the design BEFORE
  writing code — do not implement first and explain after. For work that clearly follows an
  established precedent, you may move faster, but still say so explicitly rather than
  silently skipping the review step.
- Grow repositories, interfaces, DTOs, and notification models strictly on demand — add a
  method/field/type only when one specific, real command you are currently building actually
  needs it. Never add anything "while you're at it" or "for future use."
- Before deciding whether any new command needs an ownership check, consult
  PermissionMatrix.md's letter (F = role-based only, no check; S = scoped, use
  IOwnershipValidator) — this is a mechanical decision procedure established in CLAUDE.md
  §16 and ARCHITECTURE_DECISIONS.md D31, not a judgment call to make fresh each time.
- Documentation is updated in the same PR/commit as the code that depends on it, whenever a
  design review reveals a genuine gap or contradiction — never left for "later."

YOUR FIRST TASK:

Begin with a design-review analysis for the CatalogItem Application layer (CreateCatalogItemCommand,
UpdateCatalogItemCommand, RetireCatalogItemCommand, SearchCatalogItemsQuery) — the exact
scope, recommended order, and specific architectural questions to raise are detailed in
NEXT_STEPS.md §1. Do not write any code until that design has been reviewed and explicitly
approved, exactly as every previous vertical slice in this project was handled. Pay
particular attention to NEXT_STEPS.md §1.2: it is expected (but must be explicitly verified,
not assumed) that this may be the first entire feature in the project with zero
IOwnershipValidator usage anywhere, since PermissionMatrix.md §6 marks every CatalogItem
action "F" (role-based), not "S" (scoped).

Confirm you have completed steps 1–7 above, and summarize (briefly — I was there for all of
it) your understanding of where the project stands, before beginning the CatalogItem
analysis.
```
