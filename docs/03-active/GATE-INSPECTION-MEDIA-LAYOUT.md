# INSPECTION-MEDIA-LAYOUT

03.11.2026. Владелец отклонил разросшуюся сетку и поручил компактный современный
осмотр: слева крупный кадр + горизонтальные миниатюры, справа исходное описание
с сохранением переносов и ограниченной высотой. На mobile блоки последовательно.
Ветка codex/inspection-file-improvements. Накопленные изменения сохранить.

## Required reading

README → START_HERE → ACTIVE_TASK → этот Gate; ранее прочитанные UI instructions,
UI consistency/tokens/screens03/04, FP-004 §§3,9,14–15, ADR-005 и media gallery Gate;
только Gallery/CaseWorkspace/SiteInspectionPage, JS и адресные UI tests.
Неизменные документы повторно не читать.

## Scope / файлы

AttachmentGallery.razor/.css — opt-in Featured режим для общих материалов осмотра;
InspectionMediaOverview.razor/.css — два блока и plain-text formatting;
CaseWorkspace.razor, SiteInspectionPage.razor — интеграция без бизнес-изменений;
attachment-gallery.js — выбор кадра/лента/остановка сменённого видео;
screens03/04, этот Gate, ACTIVE_TASK, reports/INSPECTION_MEDIA_LAYOUT_REPORT.md;
адресные component/browser tests. Права, uploads, streaming, soft delete/history,
документы и backend не менять. Новых пакетов/migrations нет.
Без production/commit/push и изменений Collector.

## Проверки

Release build; реальный desktop/mobile UI с 20 файлами и многострочным текстом;
высота не растёт от количества миниатюр, горизонтальная прокрутка, выбор кадра,
видео, полноэкранный просмотр/zoom/keyboard, download/delete/retry прежние.
Проверить переносы текста как plain text, без интерпретации HTML/Markdown;
длинный текст ограничен по высоте с осознанным раскрытием. Скриншоты фактического
ERP и визуальная проверка обязательны. Следующий Gate не начинать.

## Результат

Реализовано локально. Последняя Release build 0 warnings/errors; component 2/2,
финальный real browser 1/1 Passed. Desktop/mobile снимки работающего ERP просмотрены.
Отчёт обновлён; визуальное решение подготовлено владельцу. Без commit/push/production.

## Публикация — 03.11.2026

Владелец прямо разрешил commit/push текущей переделки в main.
Публикуется ровно UI scope INSPECTION-MEDIA-LAYOUT одним коммитом после
0608fbdc1ea6dd39d3f64fba233972a00d9859fb, fast-forward без force.
Другие локальные Parser/README/диагностика и общий ACTIVE_TASK другого чата
сохраняются. В published ACTIVE_TASK — только новый media этап и история main.
Проверяется отдельная сборка точного main-based snapshot, scope, blob SHA и remote head.
Production/конфигурация/БД агентом не обновляются.
Evidence artifacts/inspection-media-layout/publication/verification.json.