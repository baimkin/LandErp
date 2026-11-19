# Публикация доработок 19.11.2026

По прямому запросу владельца опубликовать одним fast-forward коммитом три завершённые локальные доработки: INCOMING-QUICK-DISMISS, PARSING-ARCHIVE-GUARD, PRODUCTION-LOGGING.

Required reading: README → START_HERE → ACTIVE_TASK → три Gate и отчёта, FP-004 §§3, 8–9, 12, 14–15, PRODUCTION_RUNBOOK §§5–6, 9 → затронутые файлы.

Проверки: точный снимок main, locked restore, Release build, адресные тесты .NET/PostgreSQL и PowerShell 5.1, git diff --check, SHA опубликованных файлов, head main.

Проверку UI архива перенести на опубликованный способ запуска сборов. Независимая обработка повторов, Parser, preview, миграции и production-конфигурация вне scope. Рабочий сервер владелец обновляет отдельным скриптом, привязанным к опубликованному SHA.

Отчёт: `reports/NOV19_PUBLICATION_REPORT.md`.
