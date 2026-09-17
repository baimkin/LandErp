# Procurement V2 — полноценное «Следующее действие»

**База:** `origin/codex/stage1-phase8-overview` @ `0877129`

**Ветка:** `codex/procurement-v2-next-action`

**Дата:** 2026-09-17

## Выполнено

- `WorkTask` расширен стабильным типом действия и описанием/целью; title, UTC-срок, исполнитель и version сохранены в общей workflow-модели.
- Добавлена tenant-safe команда изменения next action с optimistic concurrency, server-side проверкой видимости/исполнителя, audit, business timeline и уведомлением нового исполнителя.
- Исполнитель следующего действия не меняет `Assignment` кейса, поэтому `ProcurementVisibility` и семантика `MineOnly` не переопределяются.
- Queue V2 показывает тип, title, краткую цель, срок и исполнителя; drawer показывает полное действие и позволяет его изменить.
- `MineOnly`, `PriceChangedOnly`, `stage`/`mine` query params и общий `ProcurementVisibility` не изменены. Ручное создание PropertyCase, переговорные/inspection modals и Phase 9 не начинались.

## Migration

- `20260917112700_ProcurementV2NextAction` — forward-only добавление `workflow.work_tasks.type` и `description`.
- Существующие строки получают `General` для type и пустое description; старые migrations не изменялись.

## Проверки

- Release build `LandErp.Server.csproj`: green, 0 warnings / 0 errors.
- `ProcurementQueueV2ReadTests`: green, 4/4; включает persistence/projection, timeline/audit, stale command, tenant isolation и неизменность case assignment.
- `CleanDatabaseMigrationAppliesWithoutLegacyBackfillContract`: green, 1/1; полный migration chain и отсутствие pending model changes.
- `git diff --check`: green, только ожидаемые Windows line-ending notices.

## Ручная визуальная приёмка

На `/procurement-v2` проверить компактность новой колонки действия, карточку в drawer, форму редактирования, русские labels типов, отображение длинной цели, срока/просрочки и смену исполнителя без исчезновения кейса из его прежнего responsibility scope.
