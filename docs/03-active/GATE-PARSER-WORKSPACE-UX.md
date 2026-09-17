# Parser — удобный Local/Server workspace

Разрешено владельцем 17 сентября 2026 года после аудита. Ветка `codex/parser-workspace-ux`.

Scope: четыре раздела, общая форма поиска/группы/расписания, прямое создание server search, явное копирование одной local ссылки, редактирование групп/поисков, сохраняемая остановка автоматической работы, reconnect, читаемые результаты, удаление старого исследовательского UI. Browser pipeline и пользовательские данные сохраняются.

Required reading: README, START_HERE, ACTIVE_TASK, PARSER_UNIVERSAL_PRODUCT_PLAN, UNIVERSAL_PARSER_REPORT, ADR-007, UI-kit instructions. Server изменения ограничены machine search management; права, optimistic concurrency и аудит обязательны.

Проверки: Release build, offline runtime/storage tests, PostgreSQL gateway tests на disposable DB, WPF smoke при доступности среды. Отчёт: reports/PARSER_WORKSPACE_UX_REPORT.md. Production apply/merge/push не входят.
