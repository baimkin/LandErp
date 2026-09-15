# Stage 1 Phase 1 — решение по database transition

**Статус:** утверждено владельцем  
**Дата:** 2026-09-15  
**Ветка:** `codex/stage-1-procurement-core`

## Решение владельца

До начала Phase 2 существующие pre-Phase-1 базы LandErp не являются сохраняемым production state. Данные в них считаются disposable development/test data и могут быть удалены с последующим созданием базы заново.

Для Phase 1 это означает:

- in-place upgrade legacy Stage 1 database **не является acceptance contract**;
- canonical deployment Phase 1 — **clean database rebuild** с применением migration chain к пустой БД;
- migration `20260915160713_Phase1CatalogProcurementBoundary` должна быть schema-only и не обязана выполнять legacy backfill `PropertyCase.ListingId -> PropertyCaseSourceLink`, переносить snapshots или сохранять pre-Phase-1 workflow/history;
- legacy migration regression test с искусственным старым dataset исключается из Phase 1 gate и заменяется явной проверкой clean DB migration + `Database.HasPendingModelChanges() == false`;
- существующие локальные/dev БД после этой коррекции следует удалить и создать заново, а не пытаться «долечивать» вручную;
- production data migration rules для будущих фаз этим решением **не ослабляются**.

## ListingId compatibility

Nullable `PropertyCase.ListingId` пока разрешено оставить в физической схеме как deprecated compatibility residue до Phase 9, потому что оно больше не является business dependency и не используется новыми Procurement operations.

При этом:

- новые `PropertyCase` не требуют `ListingId`;
- canonical ownership источников существует только через `PropertyCaseSourceLink`;
- canonical Procurement identity — `CaseId`;
- `/procurement/listings/{ListingId}` остаётся только application-level resolver по confirmed `PropertyCaseSourceLink` и **не создаёт Case**;
- runtime не должен backfill/восстанавливать ownership через `PropertyCase.ListingId`;
- физическое удаление compatibility field остаётся cleanup Phase 9, если к тому моменту оно ещё существует.

## Приоритет документа

Это более позднее owner-approved уточнение к `P0_CATALOG_PROCUREMENT_BOUNDARY.md` и `STAGE1_COMPLETION_MASTER_PLAN.md`.

Для **Phase 1 database transition** оно supersede-ит старые требования о сохранении pre-Phase-1 dataset, legacy backfill и migration-history preservation. Все остальные архитектурные инварианты Phase 1 остаются без изменений: независимый `PropertyCase`, `0..N` sources, DB-enforced confirmed uniqueness, CaseId cutover, safe scopes, manual/Telegram ingress и legacy HTTP resolver.

Phase 2 этим решением не начинается и не меняется.
