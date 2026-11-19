# PRODUCTION-LOGGING

19.11.2026 владелец разрешил исправить технические логи и диагностику, чтобы
следующий сбой сохранения общения можно было найти по номеру обращения.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate →
PRODUCTION_RUNBOOK разделы запуска/диагностики, FP-004 §§9,12,14,15 →
Run/Deploy-ProductionProcess, Server/Worker logging, WorkspaceComponent,
CaseCommunication и соответствующие тесты. Неизменившиеся документы повторно
не читать. Параллельные Parser, ранее завершённые gates и preview сохранить.

Scope: logs в прежнем StateRoot/logs, папки по московским месяцам, файлы по дням,
предел 10 МиБ, отдельные Server/Worker stdout/stderr; ротация без перезапуска.
Retention 7 суток, очистка при запуске и периодически, включая старые закрытые
штатные .out/.err. Не удалять активные/посторонние файлы, не проходить junctions.
Потоковый поиск incident ID во всех текущих/старых файлах без tail-ограничения.
UTC timestamp в JSON, безопасные структурные коды сбоев, вложенный тип,
SQLSTATE/ограничение/таблица/колонка, длительность и стабильный CommandId общения.
Без сообщений/Detail/SQL/параметров/секретов/содержимого форм и exception dump.
Причина прежнего production incident без его данных не считается установленной.

Файлы: scripts/Run-ProductionProcess.ps1, Deploy-ProductionRelease.ps1;
scripts/ProductionLogRunner.cs (Add-Type helper для Windows PowerShell 5.1,
без пакетов/отдельной службы), Find-ProductionIncident.ps1, Test-ProductionLogging.ps1;
Server/Worker Program.cs, WorkspaceComponent.cs, безопасный диагностический helper,
CaseCommunication.razor, адресные tests; PRODUCTION_RUNBOOK, Gate/ACTIVE_TASK/report.
Новых пакетов/схемы, переписывания общения, Parser, git publication, запуска
production и удаления реальных серверных логов в этой задаче нет.

Проверки: Release в отдельном artifacts output, реальный helper в Windows
PowerShell 5.1: день/месяц, предел UTF-8/большая запись, параллельные потоки,
семь суток/legacy/active/reparse/посторонние; stdout+stderr и код выхода child,
поиск по новым/старым файлам. .NET tests типов/SQLSTATE/CommandId/тайминга и
отсутствия secrets, сохранение Committed/Unknown. Документировать пределы и
production activation отдельно. Отчёт reports/PRODUCTION_LOGGING_REPORT.md.

Локально завершён 19.11.2026. Release clean; 35 Windows PowerShell 5.1 checks и
26 .NET tests Passed. Production/Git/история ERP не изменялись. Активация — после
отдельно разрешённого обновления сервера; прежняя причина ошибки не установлена.
