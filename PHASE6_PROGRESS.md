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
| 2 | `POST /api/v1/angebote/{id}/send` (Admin `F`) | not started |
| 3 | `GET /api/v1/public/angebote/{token}` (anonymous read) | not started |
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
