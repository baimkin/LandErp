# MEDIA-PARALLEL-UPLOAD

03.11.2026. Владелец разрешил до пяти параллельных прямых загрузок, скорость,
понятные этапы и замеры времени; SHA-256 на устройстве строго последовательно.
Ветка codex/inspection-file-improvements, предыдущие изменения сохранить.

## Scope

- До пяти активных upload pipelines на открытый загрузчик, один hash worker.
  Переданные файлы подтверждаются независимо, не занимая слот следующей передачи.
- Скорость по upload progress с усреднением, переданные байты, отдельные ожидание
  ответа/проверка сохранения, длительности этапов/всего; без misleading 100% успеха.
- Отмена активных передач/проверки, остановка очереди, стабильные ID/hash при повторе,
  независимые ошибки; локальный buffered backend остаётся последовательным.
- Provider confirmation вне DB locks, затем актуальные права и атомарный finalize.
  Метаданные параллельных файлов не используют общий Busy/result/operation ID страницы.
- No secrets in timings/logs. Не менять роли, migrations, 500 МиБ и soft deletion.
- Без галереи/сжатия, live cloud, production, commit/push/merge и Collector.

## Required reading

README → START_HERE → ACTIVE_TASK → этот Gate; уже прочитанные FP-004 §§3,9,14–15,
ADR-005, YANDEX_DISK_SETUP, GATE-MEDIA-LARGE-UPLOAD, UI instructions/consistency/tokens;
текущие batch/uploader/JS, WorkspaceComponent, parent forms, direct workspace/tests.
Документы неизменных прежних этапов повторно не читать.

## Файлы и проверки

ACTIVE_TASK, этот Gate, YANDEX_DISK_SETUP, reports/MEDIA_PARALLEL_UPLOAD_REPORT.md;
CaseUploadBatch.cs, CaseFileUpload.razor/.css, case-upload.js, upload-metrics.mjs;
WorkspaceComponent.cs, CaseWorkspace.razor, SiteInspectionPage.razor,
ProcurementDirectUploads.cs; адресные queue/component/browser/storage tests,
JS assertions. Новых зависимостей нет.

Release build; five-slot ceiling, sequential hashing, confirmation frees transfer slot,
independent failures/retry/cancel, rights revoked during confirmation, DB lock release,
safe timing diagnostics, real browser progress/wait-response/timing and cancellation;
прежние storage/inspection regressions. Без реальных пользовательских данных.
Итоговый отчёт: решения, файлы, команды, результаты, ограничения и следующий шаг.

## Результат

Реализовано. Последняя Release build: 0 warnings/errors; core 46/46, после последней
UI-защиты UI 21/21, browser 2/2, JS metrics 11/11 Passed. Отчёт обновлён.
Production/пять реальных облачных PUT/iPhone не проверялись; commit/push не выполнялись.

## Совместная публикация — 03.11.2026

Владелец прямо поручил commit/push обеих доработок в main и скрипт обновления.
Публикуются MEDIA-PARALLEL-UPLOAD и MEDIA-GALLERY одним коммитом после
b0a18a9af7469f00756ba81ce5da8052f1f81897, fast-forward без force.
Предыдущие локальные изменения Parser/README/диагностики и их история исключены.
ACTIVE_TASK снимка сохраняет историю remote main и только два разрешённых media Gate;
общий локальный ACTIVE_TASK другого чата не перезаписывается.
Проверка отдельной сборки точного main-based snapshot, SHA всех blob и итогового main.
Рабочая ветка/индекс и production сохраняются. Скрипт — прежний проверенный
Update-ProductionMedia.ps1 с закреплённым новым SHA в отдельной копии для владельца;
ff-only, unique version, private config backup, 500 МиБ, без migrations.
Evidence: artifacts/media-gallery/publication/verification.json.