# MEDIA-GALLERY

03.11.2026. Владелец разрешил реализовать четыре пункта: сетка фото/видео,
большой просмотр/листание/увеличение/видео, отдельные скачивание и мягкое удаление,
компактные незавершённые загрузки с повтором в существующую запись.
Ветка codex/inspection-file-improvements. Предыдущие изменения сохранить.

## Required reading

README → START_HERE → ACTIVE_TASK → этот Gate; уже прочитанные FP-004 §§3,9,14–15,
ADR-005, media Gates, AGENT_UI_INSTRUCTIONS/GATE-UI-CONSISTENCY/tokens;
screens 03/04 и reference composition; affected contracts/storage/access/forms/tests.
Неизменные документы повторно не перечитывать.

## Scope / файлы

Общий AttachmentGallery.razor/.css и изолированный gallery JS; интеграция
CaseWorkspace/SiteInspectionPage, CaseFileUpload failed block. Авторизованный
inline media endpoint, streaming/range без полной byte[] буферизации видео;
FileStorage/ProcurementContracts, Routed/FileSystem/Yandex adapters,
Procurement media auth/read helper, ProcurementEndpoints. Прежние права/soft-delete
и история сохраняются. Video codec fallback через скачивание; SVG/HTML/links
inline не исполняются, Deleted не попадает в галерею. Миниатюры lazy без новых
пакетов/перекодирования; UI использует токены. Секреты/провайдерские URL не выдаются.
Документы: ACTIVE_TASK, этот Gate, screens03/04, media report. Tests: storage/range/
auth/deletion/retry/component/browser и адресные регрессии. Без migrations/ролей/
пакетов, live cloud, production, Collector и commit/push/merge.

## Проверки / отчёт

Release build; range 200/206/416, bounds/no buffering, metadata integrity, tenant/
inspector/Deleted access; real component navigation/delete/retry; real browser
photos/video/zoom/keyboard/mobile/error/empty, screenshots. Визуальная проверка
этого Gate разрешена текущим запросом удобного UI и обязательной приёмкой новых
сценариев; историческое исключение browser из UI Gate не относится к этой задаче.
Отчёт reports/MEDIA_GALLERY_REPORT.md с файлами, решениями, командами, результатами
и ограничениями. Следующий Gate не начинать.

## Результат

Реализовано. Release build 0 warnings/errors; 28 core Passed, исправленный endpoint
и UI suite 22/22 Passed (пересекаются); финальный Edge/Blazor flow 1/1 Passed,
1 мин 9 с, desktop/mobile screenshots просмотрены. Новое видео не буферизуется
целиком; gallery retry не пересоздаёт picker. Production/Яндекс live/iPhone/commit/push
не выполнялись. Итоговый отчёт обновлён.

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