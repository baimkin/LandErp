# Отчёт — пользовательские доработки 5–8 октября 2026 года

Дата начала: 09.10.2026. Ветка: `main`.

Production, production-БД и рабочий сервер не изменялись. Браузерные проверки
не выполняются; визуальная приёмка остаётся владельцу.

## Этап 1. Очередь закупки — фильтры и сортировка

Реализовано:

- выбранный ответственный сохраняется в серверном запросе конкретной воронки;
- фильтр, список значений и колонка «Ответственный» используют ответственность
  PropertyCase (`Assignment.EmployeeId`), а не исполнителя ближайшей задачи;
- таблица выбранной воронки и канбан используют один и тот же смысл фильтра;
- добавлена серверная сортировка по имени ответственного в обе стороны;
- равные имена получают стабильный порядок по номеру PropertyCase и `CaseId`,
  поэтому разбиение на страницы не меняет состав;
- заголовок колонки управляет сортировкой и сообщает направление через
  `aria-sort`;
- исправлен существовавший пропуск `CancellationToken` в тесте экономики,
  который ранее блокировал компиляцию профильного тестового проекта.

Изменённые файлы:

- `docs/03-active/ACTIVE_TASK.md`;
- `src/LandErp.Application/Modules/Procurement/Public/ProcurementQueueV2ReadContracts.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.Queries.cs`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.Projection.cs`;
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor`;
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor.css`;
- `tests/LandErp.Foundation.Tests/KanbanTests.cs`;
- `tests/LandErp.Foundation.Tests/MarketDemandTests.cs`.

Проверки:

- `dotnet --version` → `10.0.401`;
- `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore`
  → успешно, 0 ошибок, 0 предупреждений;
- `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --filter "FullyQualifiedName~KanbanTests.BoardManagerFilterKeepsCountsCardsAndPaginationInOneScope" --no-restore`
  → 1/1 Passed.

Миграции: не требовались.

## Этапы 2–6

Не начаты на момент фиксации этапа 1.
