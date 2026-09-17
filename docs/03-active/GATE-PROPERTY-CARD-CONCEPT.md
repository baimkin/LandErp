# Карточка объекта по утверждённому концепту

Статус: Implemented; ожидает ручной визуальной приёмки. Ветка: codex/property-card-concept.
Основание: прямое разрешение владельца перенести artifacts/ui-prototypes/property-card-concept.html.

## Required reading
README, START_HERE, ACTIVE_TASK; UI-kit AGENT_UI_INSTRUCTIONS и tokens;
docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md;
утверждённый концепт, CaseWorkspace, SiteInspectionPage и вызываемые ими контракты.

## Scope
Карточка, её окна, галерея, уведомления, документы и осмотр. Изолированные
компоненты и стили; общий shell и остальные страницы не меняются. Существующие
права, история, optimistic concurrency, реальные файлы сохраняются. Без миграций,
новых пакетов, commit/merge/push. Формы работают с существующими контрактами.

## Файлы и проверки
CaseWorkspace.razor/css, SiteInspectionPage.razor/css, новые компоненты Procurement,
изолированный обработчик операций и тесты. Release Server build, небраузерные
unit tests форм/операций, diff check. Browser/visual tests исключены владельцем.
Отчёт: reports/PROPERTY_CARD_CONCEPT_REPORT.md. Ручная визуальная приёмка владельцем.

В ходе работы параллельная задача переключила общую папку на codex/map-scroll-completion. Её изменения сохранены; повторное переключение не выполнялось. Дополнительно LandErp.Server.csproj исключает вложенные artifacts из исходников и публикации, чтобы временная сборка другой задачи не дублировала generated attributes.
