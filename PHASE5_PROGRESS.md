# PHASE5_PROGRESS.md — API: Angebot Builder + Internal Review Workflow

**Branch:** `feature/phase-5-angebot-builder-review`. **Merged to `main` via PR #11** (merge commit `18243ec`).
**Roadmap entry:** `PROJECT_ROADMAP.md` Phase 5. **Status: complete and merged.**

> **This file was written during Phase 6, not during Phase 5.** Phase 5 shipped without a progress
> document — every phase from 2 onward has one — and Phase 6 inherited closing that gap. It is
> reconstructed from the four slice commits (`44f9560`, `ece0ded`, `6684ffe`, `a74802e`) and the
> code they landed, **not** from memory of the conversation that produced them. Where a commit
> recorded reasoning, that reasoning is reproduced; nothing has been invented to fill a silence. It
> is therefore a faithful summary of the commit record rather than a contemporaneous slice log, and
> the commits themselves remain the primary source.

---

## Scope

Per `PROJECT_ROADMAP.md`: the Angebot builder endpoints (sections, items, from Catalog or custom),
the Catalog surface, duplication (FR-4.11), and the complete internal review loop
(submit → approve / request changes). Sending to the customer and the token-link mechanism were
**not** in scope — they are Phase 6.

A Development bootstrap slice preceded these four and merged separately as PR #10 (`7ce9774`,
D64); it is recorded in `ARCHITECTURE_DECISIONS.md` D64 and `PROJECT_STATE.md` §9, not here.

## Slice 1 — Angebot builder core endpoints and reads (`44f9560`)

`POST /leads/{leadId}/angebote`, `GET /angebote/{id}`, `GET /leads/{leadId}/angebote`,
`POST /angebote/{id}/sections`, `POST /angebote/{id}/items`, and the two `DELETE` routes. Six
handlers that already existed but were unreachable over HTTP, plus the read side the review loop
needs. No migration, no model change.

- **Removal is new Domain behaviour, justified by evidence that had been missed.** `PermissionMatrix.md` §3 says "Add/**remove** Sections & Items — Inspector S", which is exactly the documented evidence `CLAUDE.md` §2 requires before a child remover may exist. The earlier conclusion that no such evidence existed had been drawn from `Architecture.md` §5.2's endpoint table alone. §2 was corrected in the same commit, including why removing a draft line item does not contradict BR-12 — the edit-lock separates working material from a record. `Angebot.RemoveSection`/`RemoveItem` both go through `EnsureEditable` and recalculate totals; `AngebotSection.RemoveItem` stays `internal`.
- **The two reads were absent from `Architecture.md` §5.2**, which listed only writes — an Admin could not have fetched what they were being asked to approve. §5.2 gained those rows plus the `DELETE` rows.
- **`GET /angebote/{id}` reads through `IAngebotRepository`, not a projection.** §22 wants the entity for `IOwnershipValidator`; D36 wants a projection for `Include`-heavy aggregates. They reconcile here because the endpoint returns the whole tree anyway, and a projection would have to re-derive `VatBreakdown`/`Subtotal`/`LineTotal` in SQL — duplicating `Architecture.md` §6.1's calculations in a second language.
- **Unexpected finding:** `AngebotQueriesTests` initially used invented inspector ids and was rejected by the real FK to `AspNetUsers` (D44). Fixed by seeding real `ApplicationUser` rows. Only LocalDB catches this — the D40 rule earning its keep again.

## Slice 2 — Internal review loop endpoints and comment history (`ece0ded`)

`POST /angebote/{id}/submit-for-review`, `/approve`, `/request-changes`, and
`GET /angebote/{id}/review-comments`. No migration, no model change, no Domain change.

- **The authorization asymmetry is the point of the slice**, pinned in both directions: submitting is ownership-scoped (`S`), while approve and request-changes carry no ownership check at all, because `PermissionMatrix.md` §4 marks them `F`. Calling `IOwnershipValidator` there would be a semantic error, not redundancy (§16, D31).
- **FR-5.3's loop needs no endpoint.** `StateMachine.md` §2.3 says `ChangesRequested` returns to `Draft` "the moment editing resumes", which `Angebot.EnsureEditable` already implements. An API test drives the full round trip — submit, request changes, edit, observe `Draft`, resubmit, approve — proving it over HTTP rather than only in the aggregate.
- **`IAngebotReviewCommentQueries` is a separate interface from `IAngebotQueries`**, not another method on it: `AngebotReviewComment` is its own aggregate linked by id alone, with a structural test asserting neither type references the other.
- **The comments handler loads the parent Angebot before reading.** Access is governed by the parent's ownership, so loading it is what makes `IOwnershipValidator` usable — and it yields 404 for an unknown Angebot where a bare comment query would return an empty list.

## Slice 3 — Catalog endpoints and save-as-Catalog-item (`6684ffe`)

`GET`/`POST /catalog-items`, `PUT /catalog-items/{id}`, `POST /catalog-items/{id}/retire`, and
`POST /angebot-items/{id}/save-as-catalog-item` (FR-4.10). No migration, no model change.

- **Nothing here is ownership-scoped, and that is the correct reading of `PermissionMatrix.md` §6** rather than an omission: the Catalog is shared company-wide, so no row belongs to a caller. An API test asserts a *non-owning* Inspector can save an item from someone else's Angebot — the test that fails if anyone later adds a scope check.
- **Retirement is `POST /retire`, not `DELETE`.** BR-12 keeps the row so trace links stay valid (BR-8) and it remains usable as a direct reference (BR-14); a `DELETE` verb would advertise a physical removal that must never happen.
- **`SearchCatalogItemsQuery` gained a search term and paging** — both documented, not anticipated: Wireframe D2 shows a "Search Catalog" box and `Architecture.md` §5.1 makes `?page=`/`?pageSize=` the list convention. **D37 is untouched**, since it was specifically about an `includeRetired` flag, which still does not exist. Matching uses `EF.Functions.Like` so it runs through the column's collation.
- **`SaveAngebotItemAsCatalogItemCommand` is its own command**, not an overload of `CreateCatalogItem`: §6 splits Admin curation from the Inspector save-as path, and only this one records `CreatedFromAngebotItemId`. Quantity and VAT rate are deliberately not copied — facts about one job, not properties of a reusable template, and `ERD.md` gives `CatalogItem` no column for either. This resolves the D39 deferral.

## Slice 4 — Duplicate an Angebot (FR-4.11) + documentation reconciliation (`a74802e`)

`POST /angebote/{id}/duplicate`. No migration, no model change, no Domain change.

- **Whole Angebot only**, producing a new `Draft` on a target Lead with a fresh `AngebotNumber`; section-level duplication deferred until a real caller needs it. **Two ownership rules apply and both are tested** — the Inspector must own the source and the target Lead — and `StateMachine.md` §2.4's one-active-Angebot rule applies to the target, so duplication cannot become a second route around it.
- **`CatalogItemId` is preserved, after inspection rather than assumption.** BR-8 defines it as "a traceability link only, not a live reference" — every item holds its own copy of description, specification, unit and price, so a copied line depends on nothing in the Catalog's current state. BR-12 guarantees the row is never physically deleted, so the FK cannot dangle, and BR-14 keeps a retired item a valid reference. Detaching would erase a true fact about the line's origin.
- **`InspectionId` is deliberately not copied** — it points at an Inspection belonging to the *source's* Lead, and carrying it over would attach the new Angebot to another Lead's site visit.
- **The tree is rebuilt through the aggregate's own `AddSection`/`AddItemToSection`**, not cloned, so totals are recalculated by the same code path as any other edit and never assigned.
- **Unexpected finding:** the first fresh-number test passed for the wrong reason — the fake generator's default happened to equal the source's seeded number. Repointed at a distinct value, with the requested year asserted.

---

## Known documentation debt from this phase, and how it was closed

Phase 5 merged having touched only 22 documentation lines across five files. Phase 6's opening
verification found the following, and **all of it was closed inside Phase 6** (see
`PHASE6_PROGRESS.md`'s completion checklist):

- `PROJECT_STATE.md` §1/§2/§9 still described Phase 5 as not started, unmerged, and `main` as being at the Phase 4 merge commit.
- `NEXT_STEPS.md` §6 still named the already-merged Development-bootstrap slice as the next deliverable, and had no Phase 5 section at all.
- `HANDOFF_PROMPT.md`'s `origin/main` SHA predated PR #11 and its task section directed an already-merged slice.
- This file did not exist.
- `ARCHITECTURE_DECISIONS.md` stopped at D64.

**On the missing `D65`-era entries for Phase 5 specifically:** the four commits above reference
"approved decision D2 / D3 / D4", which are **slice-local numbers from the Phase 5 design review,
not `ARCHITECTURE_DECISIONS.md` numbers** — in that file D2, D3 and D4 belong to Phase 0/1 subjects
entirely. Those slice decisions were recorded in the commit messages and, where they changed a
standing rule, in `CLAUDE.md` §2 — which is where the corrected "no update/remove methods without
documented evidence" rule now lives. **No `ARCHITECTURE_DECISIONS.md` entries have been
back-invented for them**, because doing so would mean assigning numbers and rationale after the
fact to decisions whose contemporaneous record already exists elsewhere. The commit messages are the
primary source, and this file is the index to them. Future phases should avoid reusing bare `D<n>`
numbering for slice-local decisions, since it collides with the architecture-decision log.
