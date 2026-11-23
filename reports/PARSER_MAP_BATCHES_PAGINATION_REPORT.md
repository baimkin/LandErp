# PARSER-MAP-BATCHES-PAGINATION — 05.11.2026

## Результат и причины

- Долгопрудный: AvitoMapCapture молча прекращал обработку items после числа
  ответов, равного пределу страниц (обычно 10). Удалён этот предел ответов:
  координаты поздних карточек теперь могут поступать в течение всего сбора.
  Завершение прокрутки по стабильности/концу выдачи осталось в QueueRunner.
- Явные source exclusions isSimilarToSearch/isOutGeo больше не возвращаются
  через DOM fallback; ранее ожидавшие координат карточки с этими признаками
  исключаются из карантина.
- Архангельское: распознаётся pagination-button; переход выбирается по реальному
  адресу n+1, а не только подписи. Перескок n→n+2 запрещён. Некорректный next
  после полностью проверенной последней разрешённой страницы даёт LimitReached,
  а не ошибку перехода за предел сбора.
- Предыдущее исправление Крекшино (обычный список с drawId) сохранено.

## Изменённые файлы

- src/LandErp.ParserSpike/LocalCollection/AvitoMapCapture.cs
- src/LandErp.ParserSpike/LocalCollection/DomSourcePage.cs
- src/LandErp.ParserSpike/LocalCollection/Models.cs
- src/LandErp.ParserSpike/LocalCollection/SearchAreaGuard.cs
- src/LandErp.ParserSpike/LocalCollection/QueueRunner.cs
- tests/LandErp.ParserSpike.Tests/MapBatchesPaginationTests.cs
- tests/LandErp.ParserSpike.Tests/Fixtures/avito-pagination-saved.json
- README.md, docs/03-active/ACTIVE_TASK.md и GATE-PARSER-MAP-BATCHES-PAGINATION.md

Fixture содержит только публичные признаки пагинации из сохранённых ошибок
Долгопрудного и Архангельского; закрытое состояние страницы не переносилось.

## Проверки

Команды из корня репозитория:

```powershell
dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
powershell -File artifacts/parser-map-batches-pagination/Test-Acceptance.ps1
node artifacts/parser-map-batches-pagination/Test-PaginationScript.cjs
dotnet build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false -o artifacts/parser-map-batches-pagination/local-update/ready
```

- Release builds: 0 warnings, 0 errors.
- Небраузерные unit/contract проверки: 185 Passed, 0 skipped, включая 5 новых.
- Реальный pagination script на сохранённых/моделируемых DOM: 11/11 Passed.
- TRX: artifacts/parser-map-batches-pagination/tests/map-batches-pagination-acceptance.trx.
- Проверено совпадение трёх собственных DLL между протестированным и staged output.

## Рабочая сборка

Обновлена artifacts/LandErp-Parser, приложение автоматически не запускалось.
Updater подтвердил 155 файлов, заменил 4 с резервной копией; 125 защищённых
файлов данных/настроек/диагностики не изменились. Подтверждение:
artifacts/parser-map-batches-pagination/local-update/verification.json.
Backup: local-update/backup-a44d14c70e5340a98c79c7e9e5af8865 в той же папке этапа.

## Ограничения и следующий шаг

Live Avito и браузерные тесты не запускались. Это подтверждение offline-поведения,
а не фактической полноты конкретной выдачи. Если реальные координаты не поступят,
карточки остаются неподтверждёнными и результат может остаться Partial.
Геометрия, состояние выделения и фильтры карты продолжают проверяться;
пиксельная видимость canvas отдельно не доказывается. Некорректная пагинация
до лимита безопасно останавливает сбор, URL не достраивается догадкой.
Server, схема БД, пакеты и Git publication не изменялись.

Ручной контроль владельцем: Крекшино Avito, затем Долгопрудный Avito,
затем Архангельское Avito — по одному запуску с разбором результата каждого.
