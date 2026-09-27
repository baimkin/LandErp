# GATE-UX-REPAIR — исправление существующих экранов

Прямой запрос владельца 27.09.2026 разрешает R-01–R-06 одной задачей.
База: `42d6e67cbd052e038894893e5fc109bb9d63f4ee`.
Ветка: `codex/ux-repair-current-screens`.

Scope: диагностика задачи после общения; Incoming, выбор с исключениями и
сохранённые фильтры; обычный текст и вложения с сохранением legacy; проверки;
карточка/задачи/общение; ленты и рынок. Новые функции не активируются.

Required reading: UX_REPAIR_PLAN_2026-09-27.md; AGENT_UI_INSTRUCTIONS.md;
GATE-UI-CONSISTENCY.md; tokens.css; specs и references Incoming,
ProcurementQueue, PropertyCase; FP-004 в части прав, задач и истории.
Последние решения владельца из запроса выше прежних требований rich-text.

Файлы: перечисленные в repair plan компоненты и стили, CatalogCalculation
contracts/service, адаптер CaseNoteDocument и его проверка вложений, адресные тесты.
Проверки: исключения preview/apply, задача и повтор команды, права/legacy/вложения;
Release build Server, небраузерная проверка JS и diff.
Server, browser automation, рабочая БД, migrations apply, main/push/merge запрещены.
Отчёт: reports/UX_REPAIR_REPORT.md. Ручная визуальная приёмка отдельно.
