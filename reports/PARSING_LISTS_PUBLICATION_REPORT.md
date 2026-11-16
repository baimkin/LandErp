# Публикация больших списков поисков и парсинга

16.11.2026. По прямому запросу владельца подготовлен отдельный main-based снимок
PARSING-LARGE-LISTS. Base: 9bf4ed95ebb52e61daf1043436000f97ca1741ec.

Включены зоны/поиски без страниц с внутренней прокруткой, история по 50 с
серверными фильтрами и курсором, сохранение фильтров/видимой строки. Прежние
галерея, передача менеджеру, бизнес-команды и права сохраняются из main.
Другие локальные Parser/inspection/media работы не включаются. Локальные
branch/index и preview сохраняются. ACTIVE_TASK содержит только текущую
доработку/публикацию поверх истории удалённого main.

Состав: 16 файлов реализации и документации, Gate публикации и этот отчёт.
Проверки точного снимка и фактический SHA фиксируются в ignored
artifacts/parsing-lists-publication/verification.json, manifest.json и TRX.
Отдельный commit имеет единственного parent текущий main; обновление ветки
fast-forward с expected SHA, без force. После публикации сверяются remote/tree/blobs.

Точный main-based снимок: locked restore Passed; Release 0 warnings/errors;
37/37 адресных .NET проверок Passed, включая настоящие browser/PostgreSQL
сценарии обеих функций, чтение media/ranges/soft delete и права. JS storage:
11 случаев Passed. Состав diff проверен: 18 файлов, вне scope отличий нет.
Галерея и IncomingCatalogV2 совпадают с base main по blob SHA. Синтаксис
PowerShell update template проверен. Это не полный foundation suite и не
production acceptance. TRX: snapshot/artifacts/publication-tests/publication.trx.

Скрипт обновления привязывается к опубликованному SHA и использует штатные
Publish-Production/Deploy-ProductionRelease. Пакет создаётся в
C:\LandErp\artifacts\production\releases\<версия>, установленная версия — в
C:\Program Files\LandErp\releases\<версия>. Существующие private config и БД
не меняются; новых migrations/packages нет. Production агентом не запускается.
