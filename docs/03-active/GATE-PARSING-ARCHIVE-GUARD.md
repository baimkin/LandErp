# PARSING-ARCHIVE-GUARD

19.11.2026 владелец разрешил убрать архивные зоны из текущих ошибок и добавить
прямую проверку архива при запуске и выдаче заданий парсеру. Историю сохранять.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
PARSING-RETRY-STATE/LARGE-LISTS Gate/report, screen07/UI/tokens и
docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md §§3,9,12–15
→ CollectionAdministration/Scheduler/Gateway, Collectors.Workspace/Retry/tests.
Неизменившиеся документы не перечитывать.

Scope: ошибки/счётчики главной исключают SearchGroup.Active=false; серверные
manual enqueue (web/agent), расписание/retry и claim прямо проверяют группу,
включая ошибочно enabled поиск в архиве и ранее ожидающие/просроченные задания.
Поиски без группы сохраняют штатное поведение. Результаты и строки истории не
удалять и не скрывать в истории. Уже полученные результаты принимаются по прежней
проверке lease; отдельное принудительное прерывание работающего Parser не входит.
Права/tenant scope не расширять, существующую защиту от дублей сохранить.

Файлы: CollectionAdministration.cs, CollectionScheduler.cs, CollectorGateway.cs,
при необходимости общий внутренний archive guard; Collectors.Workspace.cs/Retry.cs;
новые адресные PostgreSQL/UI tests, уточнение существующих browser tests,
screen07, этот Gate, запись ACTIVE_TASK, reports/PARSING_ARCHIVE_GUARD_REPORT.md.
Без migrations/packages/переписывания Parser/commit/push/production.
Сохранить текущую ветку, накопленное, предыдущую доработку кнопок и local preview.

Проверки: Release в отдельном output; PostgreSQL — оба ручных enqueue, schedule,
retry, pending и expired claim в архиве, активные/безгрупповые поиски и история;
UI — архив не в ошибках/счётчиках, история доступна, причина архива у кнопки;
регрессия существующих Collection/Retry сценариев. Фактические команды/результаты
и ограничения в отчёте; следующий Gate не начинать.

Результат: реализовано локально. Release 0 warnings/errors; финальные 37/37 .NET
и 11 JS случаев Passed. PostgreSQL проверил архив независимо от Enabled, enqueue,
schedule/retry/claim, текущий lease, чужую группу и сохранённую историю. UI/HTML
проверил ошибки/счётчики/архивный список/кнопку; browser-регрессия и desktop/mobile
снимки пройдены. Отчёт reports/PARSING_ARCHIVE_GUARD_REPORT.md.
Commit/push/production не выполнялись. Следующий Gate не начат.
