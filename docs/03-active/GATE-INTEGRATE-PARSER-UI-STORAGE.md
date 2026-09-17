# Gate — совместная проверка Parser, UI и файлового хранилища

Дата: 17 сентября 2026 года. Ветка: `codex/integrate-parser-ui-storage`.
Владелец явно разрешил локальные коммиты завершённых работ и их слияние для
совместной проверки. Main и push не входят. Production apply не разрешён.

## Required reading

- README → START_HERE → ACTIVE_TASK → этот Gate.
- GATE-PARSER-WORKSPACE-UX, PARSER_UNIVERSAL_PRODUCT_PLAN, UNIVERSAL_PARSER_REPORT,
  PARSER_WORKSPACE_UX_REPORT.
- GATE-UI-CONSISTENCY, UI_CONSISTENCY_REPORT, PROPERTY_CARD_VISUAL_REPORT,
  UI-kit instructions/tokens.
- GATE-YANDEX-DISK-STORAGE, YANDEX_DISK_STORAGE_REPORT, YANDEX_DISK_SETUP.
- ADR-005, ADR-007, FP-004 (границы прочитаны в текущей задаче).

## Объединение

1. Сохранить все завершённые Parser/UI изменения основной рабочей копии.
2. Зафиксировать и слить Yandex Disk из отдельной рабочей копии.
3. Слить `codex/fix-property-case-runtime-grant` (право runtime на документы).
4. Остальные старые ветки не повторять: ancestry и patch equivalence проверяются.
5. Разрешить только integration conflicts, сохранить обе функциональности.

Файлы: только перечисленные в исходных отчётах; дополнительно этот Gate,
ACTIVE_TASK, интеграционный отчёт и необходимые локальные launch scripts.
Секрет Яндекс Диска остаётся в ignored local-data, не входит в коммиты.

## Проверки

Locked restore и Release build всех проектов в отдельном artifacts path;
89 offline/WPF Parser tests с прежними исключениями реального браузера;
Foundation unit tests, CollectionPoolTests (5), attachment/storage tests на
настоящей disposable PostgreSQL; 9 file adapter tests, live Disk opt-in smoke;
5 JS preference tests, JS syntax и git diff --check.

Server запускается для ручной приёмки из объединённой сборки с прежней локальной
конфигурацией; без применения migrations и без изменения пользовательских
данных. Визуальная приёмка владельцем сохранена, браузерные UI tests не запускать.
Старые процессы идентифицировать точно; чужие процессы не останавливать.

Отчёт: `reports/INTEGRATED_PARSER_UI_STORAGE_REPORT.md` — коммиты, конфликты,
файлы, проверки, запуск, ограничения, следующий шаг. Другие Gate не активируются.
