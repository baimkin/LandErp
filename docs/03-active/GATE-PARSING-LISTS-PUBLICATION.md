# PARSING-LISTS-PUBLICATION

16.11.2026 прямой запрос владельца: commit/push в main завершённой доработки
PARSING-LARGE-LISTS и скрипт обновления сервера. Прежний запрет публикации этого
Gate снят запросом. Production выполняет владелец, агент готовит скрипт.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
GATE-PARSING-LARGE-LISTS / reports/PARSING_LARGE_LISTS_REPORT.md →
PRODUCTION_RUNBOOK §§5–6 / Publish-Production / Deploy-ProductionRelease.
Предыдущая WORKSPACE-TRANSFER-PUBLICATION — reference безопасного scoped snapshot.

Base main проверен: 9bf4ed95ebb52e61daf1043436000f97ca1741ec. Подготовить отдельный
снимок main, наложить только 16 файлов реализации (ACTIVE_TASK — только scoped
секция поверх main), этот Gate и отчёт публикации. Сохранить галерею, передачу
менеджеру и все прочие файлы main. Локальные branch/index/накопленное/preview не менять.

Проверки: состав diff и hashes; locked restore/Release точного снимка, адресные
.NET/JS/browser/PostgreSQL проверки; синтаксис скрипта обновления. Один commit,
один parent текущий main, fast-forward без force с проверкой expected SHA.
После публикации сверить remote SHA, tree и blobs. Без миграций/packages/config,
credentials/local-data/artifacts в Git. Другие Gate не активировать.

Скрипт привязан к новому reviewed SHA; существующий versioned publish
C:\LandErp\artifacts\production\releases\<версия> и штатный deploy. БД и private
config не меняются. Фактический SHA и evidence — ignored
artifacts/parsing-lists-publication/verification.json, отчёт
reports/PARSING_LISTS_PUBLICATION_REPORT.md.

Подготовка проверена: 18 scoped файлов, незапланированных изменений 0,
locked restore/Release Passed (0 warnings/errors), 37/37 .NET и 11 JS случаев
Passed на точном main-based снимке. Галерея и передача менеджеру сохранены.
Синтаксис update template Passed; факт отправки и SHA — в verification.json.
