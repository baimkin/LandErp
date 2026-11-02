# PROCUREMENT-PIPELINE-SEARCH

02.11.2026. Владелец утвердил поиск в выбранной воронке закупки: канбан и
таблица. Работа в текущей ветке `codex/inspection-file-improvements`, без
переключения и изменения накопленного scope вложений/Collector.

## Scope

- Компактный поиск по тем же полям, что в общей очереди; ввод с задержкой,
  Enter и отдельный сброс. Сохранять запрос при переключении таблица/канбан.
- Единый серверный предикат поиска для обоих представлений; фильтровать до
  пагинации и подсчётов, совместно с выбранным менеджером и текущей видимостью.
- При смене запроса сбрасывать подгруженные части колонок; при «Ещё» сохранять
  запрос. Пустой запрос восстанавливает доступный набор выбранной воронки.
- Без схемы/миграций, новых ролей/пакетов, production и публикации исходников.

## Required reading

README → START_HERE → ACTIVE_TASK → этот Gate;
FP-004 §§3, 9, 14–15; AGENT_UI_INSTRUCTIONS; GATE-UI-CONSISTENCY;
UI-kit tokens; screen 02 и его reference HTML; KANBAN_PLAN (границы контекста
воронки и пагинации; прежнее скрытие поиска отменено текущим запросом);
затрагиваемые contracts/read services/page/tests.

## Файлы и проверки

ProcurementQueueV2.razor/.css/.Kanban.cs, KanbanContracts.cs,
KanbanReadService.cs, ProcurementQueueV2ReadService.cs, общий
ProcurementCaseSearch.cs; ProcurementPipelineSearchTests.cs; ACTIVE_TASK,
KANBAN_PLAN, screen 02, этот Gate и отчёт.

Release build; адресные небраузерные проверки на disposable PostgreSQL:
поля/регистр/пробелы, одинаковый набор таблицы/канбана, менеджер, scope и чужая
организация, пустой результат/сброс, >50 карточек и продолжение, суммы колонок;
проверка действующей Razor-страницы через HtmlRenderer; git diff --check.
Визуальная приёмка отдельно, браузер и рабочая БД не запускаются.

## Отчёт

`reports/PROCUREMENT_PIPELINE_SEARCH_REPORT.md`: файлы, решения, команды,
результаты, ограничения и следующий рекомендуемый шаг. Следующий Gate не начинать.

## Результат

Реализовано. Release build — 0 warnings/errors; итоговый прогон — 7/7 Passed
(4 новых PostgreSQL/HtmlRenderer сценария и 3 прежние проверки).
Evidence: `artifacts/procurement-pipeline-search/tests/pipeline-search-verified.trx`.
Отчёт: `reports/PROCUREMENT_PIPELINE_SEARCH_REPORT.md`.
Визуальная приёмка, production и commit/push не выполнялись; следующий Gate
не активирован.
## Публикация исходников — 02.11.2026

Прямым запросом владелец поручил commit/push поиска в `origin/main`.
Публикуется только 13 файлов этого Gate: поиск, адресные тесты, спецификация и
документы этапа. Накопленные изменения Collector/README и локальная история
его неопубликованных Gate исключены. Публикация — один коммит поверх проверенного
head `main`, fast-forward с expected SHA без force через подключённый GitHub.
Локальная ветка/индекс и production не изменяются. Итоговый SHA подтверждается
отдельным чтением `main`; evidence — `artifacts/procurement-pipeline-search/publication/verification.json`.