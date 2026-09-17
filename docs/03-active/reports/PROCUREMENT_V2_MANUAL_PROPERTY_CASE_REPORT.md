# Procurement V2 — ручное создание PropertyCase

**База этапа:** `codex/procurement-v2-next-action` @ `533440c`

**Исходная интеграционная база:** `origin/codex/stage1-phase8-overview` @ `0877129`

**Ветка:** `codex/procurement-v2-manual-property-case`

**Дата:** 2026-09-17

## Выполнено

- В `/procurement-v2` активирована команда `+ Создать объект вручную` для пользователей с `ManagerDecide`.
- Форма создаёт самостоятельный `PropertyCase` с названием, локацией, кадастровым номером, рабочей ценой, площадью и обязательным контекстом происхождения объекта.
- Прямой сценарий и `Incoming → Взять в работу` используют один конструктор `PropertyCase`, `Assignment`, начального `WorkTask` и workflow transition.
- Прямое создание не порождает фиктивные `Listing`, Collector Agent или Collection Job; `ListingId` остаётся `null`.
- Создание фиксируется в business timeline и audit. После сохранения новый кейс появляется в очереди и сразу открывается в drawer.
- Вручную созданный кейс может существовать без источников, а существующий механизм позволяет позже привязать к нему один или несколько Incoming-источников без смены идентичности кейса.
- Department, Team, manager и assignment берутся только из tenant-safe `AccessContext`; `ProcurementVisibility` остаётся общей границей чтения.
- `MineOnly`, `PriceChangedOnly`, `stage`/`mine` query params и существующие Phase 8 semantics не изменены. Переговорные/inspection modals и другие этапы не начинались.

## Migration

- Новая migration не требуется: этап использует уже существующие `PropertyCase`, `Assignment`, `WorkTask`, workflow, timeline и audit модели.
- Существующие migrations не изменялись.

## Проверки

- Release build `LandErp.Server.csproj`: green, 0 warnings / 0 errors.
- `DirectAndIncomingCreationConvergeOnOneScopedPropertyCaseModel`: green, 1/1. Проверены оба пути создания, отсутствие фиктивных источников/Collector/Job, scope isolation, assignment/workflow, timeline/audit и поздняя привязка источника.
- `ManualAndMarketplaceSourcesUseOneIndependentCaseAndCaseIdWorkflow`: green, 1/1; регрессия существующего Incoming и case-id workflow.
- `git diff --check`: green, кроме информационных notices о Windows line endings.

## Ручная визуальная приёмка

На `/procurement-v2` под менеджером проверить кнопку ручного создания, обязательные поля и сообщения валидации, сохранение карточки без входящего объявления, появление строки в очереди и автоматическое открытие drawer. Затем в Incoming привязать источник к созданной карточке и убедиться, что карточка не дублируется, а источник появляется в её drawer.
