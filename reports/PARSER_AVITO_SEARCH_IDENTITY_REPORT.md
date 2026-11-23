# PARSER-AVITO-SEARCH-IDENTITY — итоговое поведение списка

Дата: 22.10.2026. Ветка `codex/parser-reliability`, HEAD
`c6a79c725d57064732a6676e66cfa7d862fe6bff`. По прямому уточнению владельца
обычная выдача должна парситься по сохранённой ссылке без открытия карты.
Предыдущие незакоммиченные изменения сохранены; commit/push не выполняются.

## Исправленная причина

Ручной запуск в 23:25 МСК остановился до нажатия карты. В очищенном HTML
обнаружены пустые native select с соседними span `categoryId/select-text` и
`params[203]/select-text`, а также штатный `geoFilters/select`. Предыдущий код
читал текст пустого select и считал географический контрол неизвестным.
Тестовая форма из div не воспроизводила фактическую структуру.

Автоматический переход в карту удалён по уточнению владельца. Проверка зоны
больше не меняет режим сохранённого поиска.

## Итоговое решение

- Обычный список остаётся списком: чтение DOM-карточек, прокрутка страницы,
  контроль конца выдачи и штатная пагинация. Кнопка карты не нажимается,
  дополнительные запросы к map API не создаются. Режим определяется реальным
  DOM, а не наличием drawId/map в URL.
- Для списка с drawId текущий URL и объявленная страницей ссылка карты должны
  сохранять исходный drawId и остальные фильтры. Ссылка карты только читается.
  Это проверка сохранности ссылки на зону, а не подтверждение её геометрии.
  В БД такой список не получает фиктивный MapScope/ZoneConfirmed.
- Отсутствующий f может стать только точным наблюдавшимся токеном
  `ASgBAgICAUSq3xUC`, только для земельных участков с исходной зоной и нейтральной
  формой. Реальные цена/тип сделки/продавец/фото/текст/unknown и существующий
  непрозрачный f сохраняются строго. Общее SearchUrls.SameSearch не ослаблено.
- Подпись select читается из соседнего select-text; если есть текст native
  selectedOption, он должен совпадать. Неоднозначная разметка отклоняется.
  Штатный geoFilters/select учтён отдельно; неизвестные контролы не разрешены.
- Проверка списка выполняется при каждом чтении и перед завершением. Сброс
  зоны/изменение фильтров не создаёт успешную полную выдачу; прежние полезные
  результаты сохраняются. HTML и metadata ошибки продолжают сохраняться.
- Реальный режим карты остаётся отдельным адаптером с прежней проверкой
  полигона и координат; его наличие не отменяет проверку исходных фильтров.
- Схемы БД, Server API, пакеты, настройки и рабочий UI не меняются.

## Файлы

`src/LandErp.ParserSpike/LocalCollection/AvitoSearchIdentity.cs`,
`DomSourcePage.cs`, `Models.cs`, `QueueRunner.cs`;
`tests/LandErp.ParserSpike.Tests/AvitoSearchIdentityTests.cs`, `AvitoMapTests.cs`;
ACTIVE_TASK, Gate, этот отчёт. История предыдущих накопленных изменений
Parser Reliability / Last Error HTML сохранена.

## Проверки

1. На новой синтетической структуре native-select/select-text до исправления
   воспроизведён сбой: 4 ожидаемых падения из 12 сценариев. Evidence:
   `artifacts/parser-avito-identity/select-labels/tests/select-label-baseline.trx`.
2. Адресные проверки окончательного варианта списка: 29/29 Passed, включая
   реальный Chrome, две страницы/два сохранённых объявления, отсутствие
   нажатий карты и map-запросов, изменения зоны/фильтров, неизвестный контрол,
   несовпадение native/display подписи и контрольную проверку Runner.
3. Очищенный рабочий last-error.html открыт только локально в отдельном Chrome:
   скрипты источника отключены, все внешние запросы блокированы. Исправленный
   скрипт распознал нейтральную форму и ссылку зоны, найдено 35 DOM-карточек.
   Это воспроизведение структуры очищенного снимка, не live-проверка состояния
   значений формы или работоспособности источника. Личный HTML в fixtures/Git
   не копировался. Evidence: `artifacts/parser-avito-identity/list-mode/saved-html-replay.json`.
4. Финальный полный офлайн-прогон после учёта geoFilters/select:
   **194 Passed, 0 Failed, 0 Skipped**, 1 минута 23 секунды.
   Evidence: `artifacts/parser-avito-identity/list-mode/tests/list-mode-final.trx`.
5. Release build tests и отдельной Desktop-сборки: 0 warnings/errors.
   Личный Cian HTML из Downloads явно исключён, категории Live не выполняются.

```powershell
dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-restore
dotnet test tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-build --no-restore --filter "TestCategory!=Live&FullyQualifiedName!~ProvidedCianSnapshotParsesTwentyEightCardsWithScriptsDisabled" --logger "console;verbosity=minimal" --logger "trx;LogFileName=list-mode-final.trx" --results-directory artifacts/parser-avito-identity/list-mode/tests --blame-hang-timeout 45s --blame-hang-dump-type none
dotnet build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -o artifacts/parser-avito-identity/list-mode/local-update/ready
git diff --check
```

## Данные, установка и ограничения

До проверок сняты SHA-256 двух рабочих баз, пяти настроек и обоих последних
диагностических снимков: `artifacts/parser-avito-identity/select-labels/protected-before.json`.
Тесты используют временные базы/профили и локальные route-fixtures; рабочие
LocalStore/профили в тестах не открываются. После финального прогона штатно
закрыт только рабочий Parser (активных заданий не было). Обновлена папка
`C:\.Projects\LandErp\artifacts\LandErp-Parser`: заменены 4 программных файла,
прежние версии заранее сохранены и проверены в
`artifacts/parser-avito-identity/list-mode/local-update/backup-b6663c62972c44fd93a6d4b50de68d0c`.
Все 155 файлов установки совпали со staged-сборкой по SHA-256; три собственные
DLL совпадают с прошедшим тесты runtime. Все 9 защищённых файлов неизменны,
ProtectedChanges=0. Дополнительные файлы не удалялись, Parser после обновления
не запускался. Evidence: `artifacts/parser-avito-identity/list-mode/local-update/manifest.json`,
`verification.json`, `protected-verification.json`. `git diff --check` — Passed.

Список подтверждает сохранность исходной ссылки на зону и условий. Без
геометрии нельзя независимо доказать, что сайт корректно применил drawId,
если он одновременно сохранил ссылку и молча проигнорировал её смысл.
Такое геометрическое доказательство не симулируется и не требует от списка
открытия карты. Проверка на площадке остаётся ручной приёмкой владельца.
Production, live marketplace, commit/push/merge и следующий Gate не выполняются.
