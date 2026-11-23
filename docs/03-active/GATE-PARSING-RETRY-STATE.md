# PARSING-RETRY-STATE

16.11.2026 владелец разрешил исправить блокировку остальных поисков при повторе,
статусы кнопки «Отправляем…» / «В очереди» / «Выполняется» и видимую причину паузы.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
PARSING-WORKSPACE/LARGE-LISTS Gate/report, UI instructions/tokens/screen07,
FP-004 §§3,9,12–15 → Collectors/WorkspaceComponent/EnqueueAsync/tests.
Неизменившиеся документы повторно не читать.

Scope: независимое состояние отправки по SearchId, подтверждение только после
успешного ответа команды, видимые состояния/причины в проблемах и списке поисков.
Защита от повторного клика, проверка сессии, server permissions/organization scope,
неопределённый результат без слепой повторной отправки. Последняя ошибка сохраняется
до результата нового запуска. Остальные строки работают во время отправки.
Не разрешать ручной запуск выключенного поиска; показать причину и штатную правку.

Файлы: Collectors.razor/.razor.cs/.Workspace.cs/.razor.css и partial Retry.cs;
адресные UI/browser/PostgreSQL tests, screen07, этот Gate, запись ACTIVE_TASK,
reports/PARSING_RETRY_STATE_REPORT.md. При необходимости небольшое выделение
проверяемого состояния кнопки. Backend/schema/права/packages/Parser не менять.
Сохранить текущую ветку, накопленное и preview. Commit/push/production не разрешены
этим запросом и не выполняются.

Проверки: отдельный Release output; независимые два enqueue с задержкой первого,
повторный клик без дубля, состояния sending/pending/leased, пауза и пояснение,
ошибка отправки/неопределённый результат и ошибка refresh без ложного успеха,
обновление и снятие блокировки без reload, desktop/mobile render. Отчёт с фактами
и ограничениями; следующий Gate не начинать.

Результат: реализовано локально. Locked restore/Release Passed, 0 warnings/errors.
15/15 адресных проверок Passed, включая actual page/HtmlRenderer с fault injection,
две независимые отправки в реальном browser/PostgreSQL и прежний flow больших списков.
Desktop/mobile snapshots просмотрены. Публикация/production не выполнялись.
Отчёт reports/PARSING_RETRY_STATE_REPORT.md, evidence artifacts/parsing-retry-state/.
