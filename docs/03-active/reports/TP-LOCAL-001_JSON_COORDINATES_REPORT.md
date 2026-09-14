# TP-LOCAL-001 — уточнение диагностики карты

Дата: 2026-09-14. Ветка: spike/001-avito-viability.
Разрешение: прямое «да, давай делай» после разбора единственного сеанса
владельца с выделенной зоной. Работа ограничена диагностикой, не парсером карты.

## Основание и результат

Предыдущий файл browser-json-20260914-094650-53cd7b5c.jsonl показал:

- /js/1/map/items: 6 порций 10/10/10/10/10/9, totalCount=59;
- карточки имеют id, urlPath, coords.lat/lng как строки, precision;
- /web/1/map/markers: координаты меток, itemsCount, isOutGeo, drawAreaBase64.

Ранее значения ID, строковых координат и drawAreaBase64 маскировались.
Исходный файл остаётся неизменным; восстановить удалённые значения нельзя.

Новая запись для Avito принимает только эти два подтверждённых endpoint,
без фоновой аналитики. Общая диагностика Cian не менялась.
В JSONL добавлен раздел Map:

- Listings: все подтверждённые ID порции, Latitude/Longitude/Precision;
- TotalCount: счётчик ответа карточек;
- Markers: координаты, ItemsCount, IsOutGeo, безопасный числовой MarkerId,
  когда он имеется. ID кластера не объявляется ID объявления;
- DrawOutcome и DrawGeometry: результат безопасного декодирования контура.

ID объявления разрешён только при совпадении числового id с публичным
urlPath объявления Avito. Строковые координаты преобразуются с invariant culture,
диапазоны -90..90 / -180..180. Неполная/некорректная пара даёт null/null,
а не выдуманную точку. Единицы и смысл precision пока не подтверждены:
сохраняется исходное публичное число, оно не объявляется метрами.

Сводка охватывает до 5000 элементов порции, в отличие от прежних трёх образцов
общей схемы. Совпадение суммарного количества с totalCount ещё не доказывает
уникальность и полноту: это проверяется после нового реального сеанса.

drawAreaBase64: Base64/Base64URL с URL-decode, затем строгий UTF-8 и JSON.
Исходная закодированная строка не записывается. Геометрия экспортирует только
известные географические поля, числовые значения в диапазоне и типы GeoJSON;
посторонние поля, токены, личные сведения исключаются.
Результаты Absent/Empty/UnexpectedType/CannotDecode/DecodedJson фиксируются
явно. Лимиты: вход 512 KiB, глубина 16, до 12000 узлов геометрии.
Compressed/binary/иной формат может дать CannotDecode — это потребует анализа
нового результата, а не автоматического сохранения произвольного payload.
Успешное декодирование не доказывает применение зоны самим источником.

## Файлы

- src/LandErp.ParserSpike/LocalCollection/JsonResponseDiagnostics.cs;
- tests/LandErp.ParserSpike.Tests/JsonMapDiagnosticsTests.cs — четыре новых теста;
- README.md — новый exe;
- docs/03-active/ACTIVE_TASK.md — тот же диагностический scope, отчёт;
- этот отчёт.

SQLite, ссылки, профиль, очередь и UI-команды не изменялись.
Новых пакетов нет. Другие локальные изменения сохранены. Commit/push не выполнялись.

## Проверки

    dotnet restore LandErp.slnx --locked-mode --ignore-failed-sources -p:NuGetAudit=false -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-json-coords
    dotnet build LandErp.slnx --no-restore -c Release -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-json-coords
    dotnet test tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj --no-build --no-restore -c Release -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-json-coords --filter "FullyQualifiedName~JsonMapDiagnosticsTests" --logger "trx;LogFileName=map-diagnostics.trx" --results-directory artifacts/local-001-json-coords/test-results

Locked restore успешно; Release build 0 ошибок / 0 предупреждений.
Четыре адресных теста Passed: все ID сводки и преобразование строк;
отбрасывание несовпадающего ID/ссылки; некорректные координаты и endpoint;
геометрия без токенов/личных полей, отсутствие/ошибка decode, кластер не является
объявлением. Это тесты нового преобразования, не повторный обход всего проекта.
Полный набор, format/audit не повторялись согласно просьбе владельца экономить
контекст. Прежние 67 Passed не относятся к новой версии.

Новое сохранение координат и реальный формат drawAreaBase64 — Live NotVerified.
Точность координат не подтверждена, получение координат из объявления
не равно получению кадастровых границ участка.

## Следующий шаг владельца

Закрыть старую версию. Запустить
artifacts/local-001-json-coords/bin/LandErp.ParserSpike.Desktop/release/LandErp.ParserSpike.Desktop.exe.
«Диагностика» → «Начать запись JSON». Открыть сохранённую ссылку карты
через «Открыть поиск вручную», проверить выделение и прокрутить правый список.
«Остановить запись JSON», подождать завершения ответов, «Показать JSON».
Файл сохранится в прежней local-data/diagnostics/browser-json/.
Исполнитель прочитает его локально по сообщению владельца о завершении сеанса.
Сбор объявлений в SQLite и восстановление зон этим этапом не реализуются.
