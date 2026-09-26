# F-01 — группы поиска и сохранённые фильтры

Разрешён прямым запросом владельца 2026-09-26.
Ветка: `codex/f-01-search-groups-filters`, база `243a61f` (main и origin/main проверены).

## Scope

Групповой отбор на /incoming; общие для организации сохранённые фильтры;
редактирование названия, группы и существующих условий с optimistic concurrency;
отдельное сохранение копии. Разовое применение не пишет сохранённые условия.
Неоднозначный V1 не преобразуется и не перезаписывается до решения владельца.
Новые поиски охватываются динамическим серверным условием группы.

## Required reading

README → START_HERE → ACTIVE_TASK → AGENTS; UI-инструкция,
screen 01-incoming-listings, его reference HTML и общие tokens.
Затрагиваются только Catalog contracts/services и IncomingCatalogV2.
Схема не меняется: версия 2 хранится в существующем criteria_json.

## Файлы и проверки

- IncomingCatalogReadContracts.cs, IncomingFilterPresetService.cs,
  IncomingCatalogReadService.cs, IncomingCatalogV2.razor.
- IncomingFilterPresetTests.cs: update/copy/version, group predicate/new search,
  isolation/permissions, legacy compatibility. Только штатный PostgresSandbox.
- Сборка Server и необходимых зависимостей, адресный запуск этих тестов.
- ACTIVE_TASK, этот Gate и reports/F_01_REPORT.md.

Нет browser/Live/Parser suite, Server startup, существующих/production DB writes,
commit/push/merge/rebase; последующие задачи не активируются.

## Отчёт

Файлы, решения, фактические команды/результаты, ограничения и ручная приёмка.
