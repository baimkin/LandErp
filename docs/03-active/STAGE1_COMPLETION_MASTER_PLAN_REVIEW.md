# Stage 1 Completion Master Plan — Independent Architecture & Logic Review

**Review mode:** REVIEW-ONLY  
**Reviewed branch:** `codex/stage-1-procurement-core`  
**Reviewed plan commit:** `733b7e49fb878fd4d633942ececf4272f177509a`  
**Reviewed document:** `docs/03-active/STAGE1_COMPLETION_MASTER_PLAN.md`  
**Production code changed:** no  
**Master plan changed:** no  
**Local Parser/Collector Agent internals inspected:** no

This review treats `STAGE1_COMPLETION_MASTER_PLAN.md` as the intended single execution document and checks whether an implementation agent can execute Stage 1 sequentially without reopening the architecture.

---

## Verdict

**READY WITH CHANGES**

The core architecture is sound and should **not** be replaced with an alternative design.

The following main boundaries are correct:

- `External sources -> Catalog -> PropertyCase -> Acquired`;
- Catalog remains the universal incoming contour and does not become a Procurement aggregate;
- `PropertyCase` becomes the independent Procurement root and may have `0..N` source links;
- external source data remains provenance and does not become system-of-record for confirmed Procurement facts;
- `Acquired` is a valid Stage 1 terminal boundary without prematurely introducing `LandAsset`;
- `Search -> Schedule -> Pending Job -> shared Agent pool -> Claim/Lease -> Catalog` is compatible with the existing Collector V1 wire contract;
- Position, Role, AccessScope, Department/Team and employee responsibility remain separate concepts.

However, the plan still has several execution ambiguities that are too important for a document that declares itself the only execution source. Most are ordering/migration/DoD defects rather than architecture defects. They should be amended before an implementation agent starts Phase 1.

---

## Critical findings

**No architectural blocker requiring redesign was found.**

There is no reason to discard the current master-plan architecture or run another full architecture audit.

The Major findings below are nevertheless mandatory amendments because they affect migration safety, phase independence, permissions, or approved business flows.

---

## Major findings

### M1. Phase 1 does not fully close the approved P0 boundary

`P0_CATALOG_PROCUREMENT_BOUNDARY.md` and `ACTIVE_TASK.md` explicitly require the next P0 implementation to prove both automatic and non-Collector ingress:

- server-side manual Catalog creation;
- server-owned source classification;
- `TakeToWork` / link-to-existing-case boundary;
- Avito flow still working;
- manual/Telegram-like flow working without fake Agent/Job/Observation.

The master-plan Phase 1 correctly moves Procurement to `CaseId`, but leaves manual ingestion / full `TakeToWork` boundary ambiguous between Phase 1 and Phase 3. Phase 2 Collection is then scheduled in between.

That makes Phase 1 technically buildable, but not sufficient to prove the most important P0 invariant: **Catalog is truly universal and PropertyCase can be created without a marketplace Listing**.

**Required amendment:** move only the minimum P0 ingestion slice into Phase 1:

- server-owned Catalog source code abstraction;
- nullable external identity/URL for non-marketplace items;
- server command for manual Catalog item creation with explicit provenance;
- idempotent `TakeToWork` / link-to-existing-case command;
- minimal server/UI path sufficient to run the manual/Telegram-like acceptance scenario.

Keep the complete Incoming screen, filters, statuses, monitoring UI and richer read model in Phase 3.

This is a move of a small boundary-proof slice, not a merge of Phase 3 into Phase 1.

### M2. Phase 2 job migration is under-specified and can destroy executor history if implemented literally

Current `ServerCollectionJob.AgentId` is mandatory. It is used for two different meanings:

1. preassigned Agent before execution;
2. the actual Agent for leased/completed work.

Current `ClaimAsync` filters by that preassignment. Existing completed jobs therefore already carry a historically meaningful executor.

The approved parsing screen is more precise than the master-plan migration text: old pending jobs may be released from Agent assignment, while completed jobs must retain their Agent as historical fact.

The phrase “migrate or drain depending on environment” is too open-ended for a sole execution document.

**Required amendment:** make the status-by-status migration deterministic:

- `Pending`: clear legacy preassignment and enter the shared pool;
- valid `Leased`: do not steal/reassign an active lease; drain it or wait for expiry during cutover, then re-pool only expired unfinished work;
- terminal jobs (`Completed`, `LimitReached`, `AwaitingManualAction`, `Failed`, `Interrupted`): preserve their current Agent as actual historical executor;
- the job Agent/executor field becomes nullable only for unclaimed work and is set atomically at Claim;
- existing V1 heartbeat/result paths continue validating `JobId + Agent + LeaseId` after claim;
- old Search `AgentId/DepartmentId/TeamId` columns are not physically removed until new code no longer reads them and compatibility has been verified.

No separate execution-history aggregate is required for Stage 1 unless implementation proves the existing job record cannot preserve this fact.

### M3. Scope enforcement is scheduled too late, and Catalog scope semantics are not explicit

Current Procurement scope is derived partly from `Listing.DepartmentId/TeamId`. Phase 1 correctly removes that dependency. After the cutover, Team/Department visibility for PropertyCase must be derived from internal responsibility/assignment, not source metadata.

The plan then postpones much of cross-surface scope verification to Phase 6. That is unsafe as an execution sequence: every phase must already be deployable before Phase 6 exists.

**Required amendment:** every phase that introduces a read model or command must add its own server-side access predicate and tests in that same phase.

Recommended Stage 1 semantics:

- **Procurement:** `Own/AssignedObjects/Team/Department/Organization` resolve through the current PropertyCase responsibility/Assignment and the assigned employee's organization assignment, never through Listing metadata;
- **Collection management:** organization-admin surface; non-organization scopes are rejected rather than partially filtered;
- **Organization administration:** organization-admin surface for mutations; employee read views obey the existing permitted scope;
- **Audit:** `audit.read` plus organization-level scope as specified by the approved screen;
- **Overview:** each aggregate must be computed from the same scope-aware queries/policies as its source module;
- **Catalog:** explicitly document that the approved “общий входящий Catalog” is an organization-shared intake pool. Do **not** reintroduce Search -> Department/Team routing only to satisfy scope filtering. If the product later requires partitioned incoming triage, add a human-owned triage assignment as a separate feature rather than source-owned routing.

Phase 6 should remain the Organization/Identity UX phase plus a final cross-module permission matrix verification, not the first place where security semantics become correct.

### M4. Confirmed source-link uniqueness and TakeToWork idempotency need a hard invariant

The P0 document states that one confirmed Catalog item must not become the canonical source of two PropertyCases. The master-plan describes a confirmed source link but does not make the database/transaction invariant explicit enough.

This matters for concurrent `TakeToWork` clicks and for duplicate matching.

**Required amendment:** define and test the following invariant:

- one PropertyCase may have `0..N` confirmed Catalog source links;
- one Catalog item may belong to at most one confirmed PropertyCase;
- `TakeToWork` is serialized/idempotent: if a confirmed link already exists, return/open the existing Case instead of creating another one;
- “possible duplicate/match” is not represented as a second confirmed link.

Use a database uniqueness constraint/index plus transactional command logic; do not rely only on UI checks.

### M5. Monitoring/re-activation is not complete enough for the approved Incoming business flow

The approved Incoming screen defines `Мониторинг цены` with a target total price and/or price-per-sotka condition; when a condition is met, the item returns to attention/incoming.

The master-plan mentions a `monitoring` state but does not explicitly plan the threshold data, re-evaluation, or re-activation rule. The same gap appears in the scenario “manager rejected/monitored -> source price changed -> object became interesting again”.

The current Listing-centric implementation already has a useful behavior where changed source revision can make a rejected/approved item actionable again. That behavior must not disappear when the model becomes multi-source.

**Required amendment:** Phase 3 must define:

- monitoring thresholds and the rule for returning a Catalog item to attention;
- `RemovedAtSource/Sold/Fake/Duplicate` as classifications distinct from generic workflow rejection where required by screen 01;
- source change creates an attention signal and never overwrites PropertyCase working facts;
- if a source is already linked to an existing rejected/paused case, renewed interest reopens/resumes the same PropertyCase by explicit command rather than silently creating a duplicate case;
- head return -> manager rework -> resubmit remains the same PropertyCase and preserves prior decisions.

No new generic workflow engine is required.

### M6. Phase 2 is too broad unless it has an internal cut line

Phase 2 currently combines:

- destructive semantic migration from preassigned jobs to shared pool;
- new claim behavior and concurrency rules;
- compatibility with existing V1 Collector;
- SearchGroup;
- server scheduling;
- result counters/history;
- full approved parsing-management UI.

All belong to the same product area, but the migration-critical part should become green before scheduling/UI expansion begins.

**Required amendment:** keep Phase 2 as one numbered phase, but split its execution/acceptance into two mandatory checkpoints:

- **2A — shared pool cutover:** additive schema, deterministic old-job migration, Claim/Lease change, V1 compatibility, ingestion no longer copies Search Department/Team, concurrency/idempotency tests;
- **2B — orchestration UX:** SearchGroup, typed schedules, scheduler, counters/history read model, parsing-management UI.

Phase 2A must be independently buildable and testable before 2B starts.

### M7. Attachments/media and offline inspection draft are hidden prerequisites of Phases 4–5

Approved screens require:

- negotiation file/photo/audio;
- check/DD documents and attachments;
- inspection photo/video/audio at whole-inspection and checklist-item level;
- locally preserved inspection draft under poor connectivity.

The reviewed production module tree does not expose an existing Stage 1 attachment persistence module that these phases can simply consume. The master-plan mentions document references/media but does not explicitly establish the minimum storage/upload contract.

**Required amendment:** add a small attachment foundation at the beginning of Phase 4 (or explicitly at the start of Phase 5 before inspection implementation):

- stable attachment reference owned by Organization and business object;
- upload/read metadata contract;
- authorization through the owning object;
- no secrets/public raw storage identifiers in staff UI;
- no generic document-management system.

For Phase 5, define the minimum offline rule: local draft of inspection answers/notes must survive connectivity loss and reconnect safely. A full general-purpose offline sync engine is not required for Stage 1. Media can use an explicit “not yet synchronized / retry upload” state if full offline media synchronization is deferred.

### M8. Legacy route/API compatibility must be concrete, not just conceptual

The current production route is `/procurement/listings/{ListingId}` and the public Procurement contracts address card/commands/notes by ListingId. Phase 1 changes the canonical identity to CaseId.

A generic statement that a compatibility adapter “can map ListingId to CaseId” is not sufficient to prevent broken saved links/tests during cutover.

**Required amendment:** explicitly keep a temporary server-side legacy route/API adapter:

- canonical new route is CaseId-based;
- old ListingId route resolves the single confirmed Case link and redirects/forwards to the canonical Case route;
- if no linked Case exists, the legacy route does not implicitly create a case;
- adapters are read/compatibility-only where possible;
- remove them only in Phase 9 after all server UI/tests and known consumers use CaseId.

---

## Minor findings

### m1. Catalog external identity migration should distinguish `null` from fake empty IDs

The current Catalog natural identity is `Organization + Source + ExternalId`. Manual sources must not receive fake IDs merely to satisfy the old index/contract.

When `ExternalId` becomes optional, preserve uniqueness only when an external identity exists. Do not use `""` as a pseudo-ID for manual items.

### m2. Backfilled PropertyCase facts need migration provenance, not fake human confirmation

When Phase 1 copies working values from existing Listing into case-owned facts, record that the initial value came from migration/source snapshot. Preserve the original Listing/Observation history and do not manufacture a user “confirmed” action.

A single migration/system audit/timeline record plus source-link provenance is enough; a per-field event-sourcing system is unnecessary.

### m3. Source removal must not hard-delete provenance

A linked external item that disappears from Avito/Cian should become unavailable/removed-at-source while the last known snapshot and observations remain readable. The PropertyCase must stay alive and the source link/history must remain auditably intact.

### m4. SearchGroup is justified but should remain deliberately thin

The approved parsing screen needs groups for 30–50+ searches. Use an existing suitable organization dictionary if it already provides stable ID/sort/archive; otherwise a small `collection.search_groups` table is enough. Do not expand a generic reference-data framework just for this.

### m5. Configurable inspection template is required; a template designer is not

The approved inspection screen explicitly requires server-configurable checklist items. Stage 1 needs a versioned/template-backed definition consumed by the UI, but does not require a universal rules engine or a full administrative template editor unless separately approved.

### m6. Audit should stay a read-layer projection

The approved Audit UI can be delivered from the immutable current audit store plus explicit action formatters/read models. Do not normalize or event-source the entire application merely to get human-readable audit.

For new events, include enough non-secret display snapshot data in payloads that archived entities remain understandable later.

### m7. Overview should remain query-time aggregates first

The master-plan is correct not to introduce BI/materialized projections by default. Use bounded server queries/read models first; add materialization only after measured performance evidence.

---

## Phase-by-phase review

| Phase | Review | Explanation |
|---|---|---|
| **Phase 1 — Catalog / Procurement P0** | **CHANGE** | Core direction is correct, but Phase 1 must also contain the minimum manual/non-Collector ingress + idempotent `TakeToWork` proof required by P0/ACTIVE_TASK, explicit confirmed-link uniqueness, CaseId route compatibility, case-owned scope and migration provenance. |
| **Phase 2 — Collection shared pool** | **SPLIT** | Keep one numbered phase but enforce 2A shared-pool migration/compatibility before 2B groups/schedules/read models/UI. Specify exact migration of Pending/Leased/terminal jobs. |
| **Phase 3 — Incoming Catalog** | **CHANGE** | After the minimal Phase 1 ingestion slice is moved earlier, Phase 3 owns the full Incoming UX/read model. Add monitoring thresholds/reactivation and screen-01 classifications; avoid duplicating the P0 command logic. |
| **Phase 4 — PropertyCase dossier** | **CHANGE** | Domain direction is correct. Add explicit reopen/resume semantics for previously rejected/paused cases and establish the minimal attachment contract required by Negotiations/Checks/Documents. |
| **Phase 5 — Inspection + Acquired** | **CHANGE** | `Acquired` boundary is correct and should not create LandAsset. Add local draft/offline minimum and media synchronization state; keep purchase command idempotent and terminal. |
| **Phase 6 — Organization / Identity** | **CHANGE** | Organization UX/direct account work is correctly placed here, but server scope enforcement cannot wait until Phase 6. Move per-module enforcement/tests into the phases that create those modules; use Phase 6 for admin UX plus final permission-matrix verification. |
| **Phase 7 — Audit** | **OK** | Semantic read model, paging/filtering and hidden technical details are appropriately scoped. Keep immutable storage and explicit formatters; no event-sourcing rewrite. |
| **Phase 8 — Overview** | **OK** | Correctly late enough to depend on stable Catalog/Procurement/Collection semantics. Ensure aggregates reuse the same access policies and do not recompute scope differently. |
| **Phase 9 — Hardening / cleanup** | **CHANGE** | Correct location for destructive cleanup, but list concrete removal gates for ListingId compatibility route/contracts and legacy Search/Job columns. Physical removal happens only after compatibility tests and no remaining server consumer. |

---

## Missing scenarios / invariants

The following should be added to the master-plan acceptance matrix so an implementation agent does not have to invent behavior:

1. **Concurrent TakeToWork:** two requests for the same Catalog item result in one confirmed PropertyCase link and one case.
2. **One source / two cases:** a second confirmed PropertyCase link for the same Catalog item is rejected; possible-match data stays non-confirmed.
3. **Three sources / one case:** Telegram + Avito + Cian retain independent source values/history while one case owns confirmed facts.
4. **Manual item with no URL/ExternalId:** it can be created, screened, monitored and taken to work without fake Collector entities.
5. **External source removed:** Catalog history remains; source marked unavailable; case and case facts remain operational.
6. **Source changed after case creation:** creates an attention/discrepancy signal; no automatic overwrite of confirmed case facts.
7. **Rejected/monitored becomes interesting again:** threshold/source change returns attention and resumes the existing case where already linked; no duplicate case.
8. **Head return and resubmit:** manager edits/adds data and forwards the same Case again; prior approval/return history remains append-only.
9. **Repeated negotiation:** ask/seller offers/buyer offers/agreed price remain ordered history; Acquired price may differ from last agreed price and is stored separately.
10. **Acquired idempotency:** retry cannot create duplicate purchase events or reactivate work; negotiations/checks/inspection/documents/sources remain readable.
11. **Cross-organization CaseId/source link attempt:** denied even if IDs are valid.
12. **Scope after source decoupling:** Team/Department Procurement visibility follows responsible employee/assignment, not historical Listing fields.
13. **Old Pending Collection jobs:** become unassigned pool work exactly once.
14. **Old leased job during deploy:** active lease is not stolen; expired lease can be reclaimed safely.
15. **Old terminal Collection job:** original Agent remains visible as historical executor.
16. **Collector duplicate delivery:** remains idempotent after shared-pool migration.
17. **Catalog source mapping:** Collector V1 `Avito/Cian` maps to server-owned Catalog source codes without leaking the Collector enum into Catalog domain contracts.
18. **Audit preservation during P0 migration:** existing timeline/approvals/tasks/audit rows remain attached to the same PropertyCase; migration adds provenance but does not rebuild history.
19. **Archived organization references:** disabling/archiving employee/team/position does not erase historical business/audit references.
20. **Approved UI terminology:** staff-facing DTOs/pages never require raw `PropertyCase`, `CollectionJob`, `AccessScope.*`, enum codes or GUIDs to explain a normal business state.

---

## Migration review

### Catalog / Procurement migration

The planned `expand -> backfill -> compatibility -> cutover -> verify -> contract` strategy is correct. It should be made more executable as follows.

#### Expand

Add, without deleting old storage:

- server-owned Catalog source representation;
- nullable external identity/URL required for manual/non-marketplace input;
- PropertyCase source-link table/entity;
- minimal case-owned facts needed to stop reading Listing as Procurement truth;
- any compatibility fields required for safe dual-read/cutover.

Do not rewrite old migrations.

#### Backfill

For every existing PropertyCase:

- create exactly one confirmed source link to its legacy Listing;
- copy the minimum working facts needed by the Case-centric read model;
- preserve `PropertyCase.Id`, BusinessNumber, AssignmentId, WorkTaskId, approvals, transitions, timeline and audit;
- add migration/system provenance without impersonating a human confirmation;
- verify organization IDs match across case/source/assignment.

Migration verification should fail if a PropertyCase cannot be deterministically backfilled.

#### Dual compatibility

Before deleting any legacy contract:

- canonical new reads/writes use CaseId;
- old ListingId card route/API resolves an existing confirmed link and forwards to CaseId;
- old compatibility path never creates a PropertyCase as a side effect of a read;
- `TakeToWork` owns creation/linking and is idempotent;
- migration and integration tests run against a database containing legacy-shaped data.

#### Cutover

Only after all Phase 1 server reads/writes are Case-centric:

- queue starts from PropertyCases;
- card, notes, decisions, workflow and assignments use CaseId;
- access predicates no longer read Listing Department/Team;
- manual ingress + manual -> TakeToWork scenario is green;
- source data remains available only as source/provenance input to the case view.

#### Contract

Within Phase 1 it is safe to remove the **required semantic relationship** between PropertyCase and Listing after cutover/backfill verification.

It is not necessary to physically delete every legacy column immediately. A nullable/unused compatibility column can survive until Phase 9 if that reduces rollout risk. Physical deletion occurs only after no application path, test, route, report or compatibility adapter uses it.

### Catalog external identity

Current automatic Catalog data uses a natural identity equivalent to `Organization + Source + ExternalId`. Preserve this for sources that provide a stable external ID.

For manual/referral/Telegram-like input where no such ID exists:

- allow `ExternalId = null`;
- do not generate a fake empty/string ID merely to satisfy uniqueness;
- if a unique index remains, make its semantics compatible with missing external IDs.

### Collection migration

Use an additive migration first:

1. make job executor/Agent nullable for unclaimed work;
2. add SearchGroup/schedule fields without deleting legacy Search routing columns;
3. deploy code capable of reading old Search rows and claiming shared jobs;
4. set old searches to safe `Manual` schedule and `SearchGroupId = null` unless explicit data says otherwise;
5. clear preassignment only for old Pending jobs;
6. preserve active lease ownership until finish/expiry;
7. preserve Agent on terminal jobs;
8. stop writing Search Agent/Department/Team into new jobs/Listings;
9. after verification, stop reading legacy Search routing fields;
10. delete obsolete columns only in Phase 9.

This preserves completed job records as historical facts and avoids forcing a coordinated Parser Agent release.

---

## Parser Agent compatibility review

**Result: compatible with keeping the local Parser/Collector Agent untouched during server Stage 1, after the Phase 2 clarifications above.**

The reviewed server-side V1 contract already supports this boundary:

- Agent identity is established by credential/authentication, not embedded in `CollectionWork` as an assignment contract;
- Agent registration reports source capabilities;
- `CollectionWork` contains `JobId`, `LeaseId`, lease expiry and search execution data;
- heartbeat/result already carry job/lease information;
- the server can therefore select any compatible Pending job, then atomically store the actual Agent at claim, without changing the V1 payload shape;
- Collector V1 `ListingSource` can remain `Avito/Cian`; the server ingestion layer maps it to the broader server-owned Catalog source code;
- current idempotent delivery, lease fencing and `SKIP LOCKED` pattern can be preserved.

Compatibility requirements to write explicitly into Phase 2/9:

- do not rename/remove current V1 register/heartbeat/claim/result endpoints during Stage 1;
- do not change required V1 JSON fields or enum values;
- keep server adapter/mapping between V1 source enum and Catalog source code;
- `Claim` sets actual Agent before returning work so existing heartbeat/result authorization still works;
- a new optional server command/endpoint allowing an authorized local app to create SearchConfiguration may be added for approved screen-07 behavior, but the old local Agent does not need to start calling it now;
- Phase 9 may remove only server-internal legacy Search/Agent assignment compatibility, not break V1 wire compatibility without a separately planned Agent release.

No local Parser Agent implementation inspection is required to start Phase 1 or Phase 2A.

---

## UX -> backend consistency

The approved screens are broadly consistent with the target backend, with these required corrections:

- **01 Incoming:** add backend semantics for manual create, monitoring thresholds/reactivation, duplicate/fake/removed/sold classifications and idempotent TakeToWork/link-existing-case;
- **02 Procurement queue:** backend must start from PropertyCase, expose current case-owned facts, linked sources and next action; source changes are signals, not overwrites;
- **03 PropertyCase:** Negotiation/PriceStatement history and structured checks are justified; attachment support is a real backend prerequisite; Acquired remains a small terminal command, not a Deal module;
- **04 Inspection:** template/configuration must be server-owned; backend needs inspection/checklist/material model, while the client also needs a defined local draft strategy;
- **07 Parsing management:** Search must not own Agent/Department/Team; completed job executor and server-side schedule/read model are required; optional local-app search creation is a server contract and does not require immediate Parser refactor;
- **08 Organization:** existing EmployeeAssignment/Role/Scope model is a good base; Phase 6 adds CRUD/archive/direct-account UX without merging Position and permissions;
- **09 Audit:** separate semantic read model is enough; raw technical data stays available but hidden by default;
- **10 Overview:** a single scope-aware server read model is appropriate; do not make Blazor infer business state from raw lists.

No approved screen requires exposing `PropertyCase`, `CollectionJob`, `AccessScope.*`, raw GUIDs or internal enum names in normal staff UI.

---

## Overengineering / underengineering assessment

### Correctly necessary for Stage 1

- PropertyCase source-link relation;
- case-owned confirmed working facts;
- negotiation/price history;
- structured quick/deep check records;
- configurable inspection template + inspection instance;
- thin SearchGroup;
- typed server scheduling (`Manual / Interval / FixedTimes`);
- shared job pool with claim/lease;
- semantic audit read layer;
- Overview query/read model;
- short-lived compatibility adapters during migration.

### Keep intentionally simple

- do not physically rename every Listing table/class in Phase 1 merely for purity;
- do not build generic workflow/rules engines for checks/inspection;
- do not build a universal reference-data framework for SearchGroup if a tiny table is sufficient;
- do not build a template designer in Stage 1 unless separately approved;
- do not build event sourcing for Audit;
- do not materialize Overview aggregates before performance evidence;
- do not build a general offline synchronization platform for the inspection screen;
- do not introduce LandAsset/InvestmentProject or a Deal state machine before the Stage 1 `Acquired` boundary.

---

## Recommended amendments to `STAGE1_COMPLETION_MASTER_PLAN.md`

These are point edits, not a replacement plan.

1. **Phase 1 — add the minimum universal-Catalog proof:** server-owned Catalog source abstraction, nullable external ID/URL, manual create command, idempotent TakeToWork/link-existing-case, minimal manual acceptance UI/path and manual/Telegram-like integration test.
2. **Phase 1 — make source-link invariants explicit:** `Case 0..N sources`, `Catalog item <=1 confirmed Case`, transactional uniqueness and concurrent TakeToWork test.
3. **Phase 1 — specify CaseId compatibility:** canonical Case route/API + temporary legacy ListingId route adapter; old read route must not create cases.
4. **Phase 1 — specify access cutover:** Procurement Team/Department scope derives from internal case responsibility/assigned employee, never Listing Department/Team.
5. **Phase 1 migration — add provenance rule:** backfilled case facts are migration/source-derived, existing history is preserved, no fake human confirmation.
6. **Phase 2 — split internally into 2A shared-pool cutover and 2B scheduling/groups/UI.**
7. **Phase 2 migration — replace “migrate or drain depending on environment” with status-specific rules:** Pending unassign, valid Leased drain/expire, terminal preserve Agent.
8. **Phase 2 — explicitly preserve V1 endpoint/payload compatibility and map V1 source enum at the server Catalog boundary.**
9. **Phase 2 — include a server application command for Search creation usable by web UI and, later, authorized local Agent without making the search Agent-owned.**
10. **Phase 3 — add price-monitoring thresholds/reactivation and the approved Incoming classifications (`Duplicate`, `Fake`, `Removed`, confirmed `Sold`) with their distinct semantics.**
11. **Phase 3/4 — add explicit “resume/reopen existing case” behavior for a previously rejected/paused linked case; do not create a duplicate case because a source changed.**
12. **Phase 4 — establish the minimal attachment/file-reference contract needed by negotiations, DD and documents.**
13. **Phase 5 — add minimum offline draft behavior for inspection and a visible unsynchronized/retry state for media if full offline media sync is deferred.**
14. **All phases — move scope enforcement/tests to the phase where each read model/command is introduced.** Keep Phase 6 as Organization/Identity implementation plus final permission-matrix verification.
15. **Permissions section — explicitly state Catalog policy:** Incoming is an organization-shared intake pool by approved product design; do not restore Search-owned Department/Team routing. Procurement scope begins from internal responsibility after TakeToWork.
16. **Phase 7 — keep Audit physical storage immutable; add explicit semantic mappings/read model and non-secret display snapshots for newly written events where needed.**
17. **Phase 8 — require Overview aggregates to reuse module access policies and count one PropertyCase once regardless of number of sources.**
18. **Phase 9 — list destructive cleanup gates:** no remaining ListingId Procurement consumer/route except tested adapter, no legacy Search routing reads/writes, migration verification green, V1 Collector server compatibility green; only then remove obsolete columns/adapters.
19. **Verification matrix — add the 20 missing scenarios/invariants from this review, especially concurrency, old job state migration, monitoring reactivation, source disappearance and migration audit preservation.**

---

## Final execution answer

**Yes.** After the amendments above are incorporated into `STAGE1_COMPLETION_MASTER_PLAN.md`, Phase 1 can be started immediately in a separate implementation agent **without another architecture analysis**.

The implementation agent should treat the amended master-plan plus its already-declared higher-priority P0/screen specs as final architecture, begin with Phase 1 tests/migration boundaries, and only stop for genuinely new product decisions or evidence that contradicts the reviewed baseline.
