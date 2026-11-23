# PARSER-MAP-DIAGNOSTICS

Прямой запрос владельца 03.11.2026: «давай подготовь все» для сравнения
Avito карты с видимым контуром и без него. Scope: пассивная диагностика,
изолированный запуск без автоработы, отдельная папка каждого запуска,
санитизированный DOM, screenshot, формы JSON и геометрия. Наличие canvas,
drawId или кнопки сброса не считать доказательством видимости контура.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ADR-007
(ранее прочитан) → затронутые Collector/Desktop/tests. Текущая ветка сохраняется.
Без изменений Server/контрактов, новых пакетов, live marketplace, Git publication.
Не исправлять эвристики обычного сбора до получения сравнимых доказательств.
Файлы: MapDiagnosticCapture.cs, Models.cs, DomSourcePage.cs,
WorkspaceController.cs, WorkspaceWindow.xaml/.cs, MapDiagnosticTests.cs,
этот Gate и reports/PARSER_MAP_DIAGNOSTICS_REPORT.md.
Проверки: небраузерные tests, Release build, hash-preserving обновление
artifacts/LandErp-Parser с backup; Parser автоматически не запускать.
