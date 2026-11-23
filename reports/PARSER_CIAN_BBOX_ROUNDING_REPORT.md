# PARSER-CIAN-BBOX-ROUNDING — результат

23.10.2026. Ветка `codex/parser-reliability`. Прямой запрос: исправить сравнение
координат с сохранением контроля фильтров, проверить и обновить рабочую сборку.

## Исправление

В последних 19 ошибках Циана единственное диагностированное отличие — bbox:
ссылка второй страницы округляет координаты до 10 десятичных знаков.
Сохранённый HTML подтвердил наличие соответствующей ссылки пагинации.

`SearchUrls.CianSemanticTerms` теперь канонизирует только bbox: четыре корректные
decimal-координаты в порядке широта/долгота/широта/долгота, invariant culture,
проверка диапазонов ±90/±180, округление до 10 знаков (AwayFromZero).
Это точность сравнения, а не изменение сохранённой/переходной ссылки.
Диагностика использует те же термы и больше не показывает ложное отличие bbox.

Каждая граница по-прежнему значима на выбранной точности. in_polygon сравнивается
строго без округления; region, цена, тип сделки/объекта и неизвестные фильтры
также остаются строгими. Отсутствующий/добавленный bbox не игнорируется.
Malformed/вне диапазона bbox не получает числовой канонизации, сравнивается
прежним строгим способом. Изменения на уровне меньше точности сериализации bbox
не различаются; контроль самого polygon от этого не ослабляется.
Avito, Normalize/SafePage, идентификаторы SQLite и Server не менялись.

## Файлы

- `src/LandErp.ParserSpike/LocalCollection/SearchUrls.cs`.
- `tests/LandErp.ParserSpike.Tests/CianCoordinateIdentityTests.cs` — 26 новых
  небраузерных сценариев на округление, знаки, midpoint, формат, обе стороны,
  сохранность исходной ссылки, диагностику, все четыре координаты, polygon,
  фильтры/добавление/удаление, диапазоны/NaN/overflow, культуру ru-RU/en-US.
- ACTIVE_TASK, Gate и этот отчёт.

Предыдущие локальные изменения сохранены. Рабочий HTML не копировался в fixtures/Git.

## Проверки

`dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-restore`
— 0 warnings/errors.

`dotnet test tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~CianCoordinateIdentityTests|FullyQualifiedName~QueueRunnerTests|FullyQualifiedName~ParserReliabilityTests|FullyQualifiedName~LocalCollectionTests|FullyQualifiedName~PolygonPaginationUsesSemanticSearchIdentity|FullyQualifiedName~CianPaginationIgnoresPresentationButRejectsUnknownChangedFilters|FullyQualifiedName~CianSeoRouteCanContinueOnCatPhpWhenSemanticIdentityMatches"`
— финально 66/66 Passed. Evidence:
`artifacts/parser-cian-bbox/tests/cian-bbox-final.trx`.
Предварительный прогон 65/65 Passed, после добавления защиты крайнего значения
decimal повторно проверены все 66 сценариев. Браузер/live не запускались.
`git diff --check` — успешно.

## Рабочая сборка

`dotnet build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -o artifacts/parser-cian-bbox/local-update/ready`
— 0 warnings/errors. Собственные DLL staging по SHA-256 совпали с DLL
финальной тестированной сборки. Исполнена процедура
`artifacts/parser-cian-bbox/local-update/Update-WorkingParser.ps1`.

Цель: `C:\.Projects\LandErp\artifacts\LandErp-Parser`.
Процессов Parser перед копированием не было. Первичное обновление заменило
4 файла с backup `backup-4ffeef3a23bc41edb7e8485828c5abdc`; после последней
защиты overflow заменены 2 файла с backup
`backup-205909f2559247dd8e5f67ebb2a25250` (оба каталога в local-update).
Проверены все 155 программных файлов. Хеши 9 файлов базы/настроек/outbox/последних
HTML и JSON совпали — ProtectedChanges=0. Дополнительные файлы не удалялись,
браузерные профили не изменялись. Evidence: manifest.json, protected-before.json,
verification.json в `artifacts/parser-cian-bbox/local-update`.

Parser автоматически не запускался; реальный сбор остаётся ручной проверкой
владельца на обновлённом EXE. Commit/push/merge/production не выполнялись.
Следующий Gate не активирован.
