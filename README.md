# LandErp

LandErp — ERP для поиска, оценки и ведения инвестиционных проектов с земельными участками.

Исследовательская фаза SPIKE-001 закрыта документационно по ERP-00.
Collector остаётся самостоятельным локальным Windows-приложением.
Production ERP ещё не реализована; следующий Gate — ERP-01, подготовлен без разрешения реализации.

## Начать отсюда

README → [START_HERE](docs/START_HERE.md) → [ACTIVE_TASK](docs/03-active/ACTIVE_TASK.md)
→ только Required reading → затрагиваемый код.

Архитектурные планы и последовательность этапов не разрешают писать весь production-контур.

## Фактический статус

- Gate 01 принят владельцем по коммиту `8607e8c60f23ea40177ab8742fadc51f867663e3`.
- Collector Avito/Cian, локальная очередь, SQLite и карта Avito реализованы в
  `spike/001-avito-viability`, checkpoint `3994644d5a00413bb53513b78c5492c7e0b70692`.
- Последний полный контроль: 85 успешных офлайн-тестов; есть сверка 43 карточек
  карты Avito владельцем. Это не общий Go или production-приёмка Collector.
- [Итог Spike](docs/03-active/SPIKE-001_RESULT.md) фиксирует ограничения и evidence.
- [ERP-00](docs/04-foundation/ERP-00_Переход_от_Spike_к_production_ERP.md) выполнен;
  [отчёт](docs/03-active/reports/ERP-00_REPORT.md).
- [ERP-01 — техническое основание и БД](docs/03-active/GATE-ERP-01_Техническое_основание_и_БД.md)
  подготовлен. Код и production migrations в ERP-00 не создаются.

## Ветки и Collector

Текущая документационная ветка `docs/erp-core-foundation` создана от ранней
плановой базы и пока не содержит исходники Collector. Работающий Collector
сохранён в своей ветке; merge/rebase и перенос кода в этой задаче не выполнялись.
Отсутствие его исходников в документационной ветке не означает, что исследование
не выполнено. Перед ERP-01 необходимо явно согласовать базу implementation-ветки
и сохранение уже проверенных SDK/CPM/lock/test настроек, не пересоздавая Collector.

Collector не зависит от ERP для локального запуска. Server не содержит
Playwright/source-specific browser logic; Collector не подключается к PostgreSQL.
Будущий server mode реализуется отдельным адаптером по
[ADR-007](docs/02-decisions/ADR-007_Collector_как_самостоятельный_продукт_и_граница_с_Server.md).

Исторический `CODEX_FIRST_TASK.md` относится к Gate 01 и не является текущим заданием.
