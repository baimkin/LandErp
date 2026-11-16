# PARSING-WORKSPACE

16.11.2026 владелец утвердил v2 и разрешил подключить его к реальной странице.
Scope: четыре вкладки /collectors, состояние системы, проблемы последних запусков
с доказанными причинами, правка ссылки и повтор, зоны, поиски и история с inline
сведениями и карандашом. Сохраняются реальные формы, расписания, права и аудит.
Предыдущий PARSING-WORKSPACE-PROTOTYPE завершён и принят. HTML v2 неизменен.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate →
GATE-PARSING-WORKSPACE-PROTOTYPE и его отчёт → AGENT_UI_INSTRUCTIONS,
GATE-UI-CONSISTENCY, screen07, v2/reference v1.3 и tokens → FP-004 §§3,9,12–15
→ затрагиваемые Collection contracts/read-model/Collectors/tests.

Текущая ветка codex/inspection-file-improvements сохраняется; накопленное и
параллельный Parser не изменяются. Без migrations, пакетов, смены прав,
изменений Parser/Collector протокола, commit/push и production.
Read-model может добавить текущий активный запуск для корректного disabled UI,
не меняя LastRun (последний завершённый результат). Partial не доказывает смену
области; неизвестная причина явно остаётся неизвестной.

Файлы: Collectors.razor/.razor.cs/.razor.css и partial workspace helpers,
CollectionResultPresentation.cs, CollectionServices.cs, CollectionAdministration.cs,
адресные тесты ParsingWorkspaceTests/ParsingWorkspaceBrowserTests, screen07,
этот Gate, ограниченная запись ACTIVE_TASK и reports/PARSING_WORKSPACE_REPORT.md.
Проверки: Release Server/tests, адресные .NET и PostgreSQL/browser сценарии на
disposable БД, desktop/tablet/mobile render, вкладки/фильтры/пагинация,
реальное сохранение/повтор, права и автоперечитывание без потери открытой формы.

Отчёт фиксирует фактические проверки, ограничения и следующий шаг владельца.

Результат: реализовано локально. Release 0 warnings/errors. Финальные 13/13
адресных проверок Passed, включая настоящий Server/PostgreSQL/браузер, и ранее
18 проверок расписаний/пула. Проверены 1720/1440/1024/390 px, права, сохранение,
повтор, обновление и история. Отчёт reports/PARSING_WORKSPACE_REPORT.md.
Публикация и production не выполнялись, следующий Gate не начинался.
