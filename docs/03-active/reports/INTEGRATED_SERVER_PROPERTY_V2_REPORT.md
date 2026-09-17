# Отчёт — интеграция Collector Server Management и PropertyCase V2

**Ветка:** `codex/integrate-server-property-v2`
**База:** `main` `b5139e0`
**Дата:** 17 сентября 2026 года

## Результат

В актуальную основу объединены Universal Parser, Collector Server Management и
PropertyCase V2. Server остаётся владельцем и валидатором server groups,
searches, jobs, queue и данных. Parser может читать workspace и создавать группы
и поиски только при выданном `CanManageSearches`; разрешение по умолчанию
выключено и управляется пользователем с `agents.manage`.

Сохранены одноразовая активация Parser, heartbeat/progress, один активный Job,
scheduler health, read model, recovery codes, аудит, rate limiting и
идемпотентность. Организация machine-команд определяется только по
аутентифицированному Parser.

PropertyCase V2 сохраняет полномочия подтверждения покупки, исправление с
причиной и аудитом, next action/readiness/risks, source observations и чек-лист
документов со связью с вложениями.

## Конфликты

- Collector runtime contract объединён с additive полями Universal Parser.
- `/collectors` оставлен технической страницей без редизайна; сохранены
  одноразовое подключение и управление `CanManageSearches`.
- Создание PropertyCase сведено к одному helper, который теперь всегда создаёт
  базовый document checklist и для ручного, и для входящего сценария.
- Authorization policies содержат одновременно `collection.read` и
  `procurement_purchase.confirm`.
- Migration tests используют фактическое число migrations вместо старого
  захардкоженного значения.

## Миграции

Старые feature migration IDs не перенесены. После
`20260917121644_UniversalParserSearchManagement` создана одна forward migration:

- `20260917144244_IntegratedCollectorPropertyCaseV2`.

Она добавляет Collector activation/runtime/scheduler state, permission
`collection.read`, purchase authority, document checklist, связь вложений и
backfill существующих PropertyCase.

## Проверки

- Release build: успешно, 0 warnings, 0 errors.
- Server foundation unit tests: 4/4 успешно.
- Не-браузерные PostgreSQL tests: 37/37 успешно.
- Migration/model test включает `HasPendingModelChanges() == false`, upgrade
  существующей схемы, полный rollback до `0` и повторное применение — успешно.

Широкий PostgreSQL-фильтр был остановлен после обнаружения включённых в него
Playwright-сценариев; эта попытка не является приёмочной проверкой. Финальный
серверный прогон использовал явное исключение `Ui`, `Browser` и `Responsive`.
Parser является отдельным продуктом и не входит в итоговую проверку этой
серверной интеграции. Визуальная и браузерная приёмка остаётся за владельцем.

## Ограничения

После отдельной реализации и приёмки экрана `/collectors` по макету «Поиски и
парсинг v1.3» интеграционная ветка слита в локальный `main` 17 сентября 2026
года. Push и production apply не выполнялись.
