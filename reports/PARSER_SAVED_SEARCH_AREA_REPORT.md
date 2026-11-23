# PARSER-SAVED-SEARCH-AREA — результат

03.11.2026. Ветка codex/inspection-file-improvements. Прямой запрос владельца:
собственный эталон области, проверка координат объявлений, отдельные фильтры,
неполный результат при недостаточном подтверждении, сохранение диагностики.

## Поведение

- Эталон хранится локально по SHA-256 нормализованного исходного поиска,
  с отдельными пространствами local/server. Цена/тип/неизвестные фильтры входят
  в ключ. Исходные URL и SQLite schema не переписываются. map/searchArea из URL
  не превращается в фиктивный polygon.
- При добавлении/сохранении поиска из открытого браузера доступная геометрия
  сохраняется только для точно совпадающего текущего адреса. Для существующих
  поисков кнопка «Сохранить область» явно подтверждает текущую видимую область:
  пользователь выбирает поиск, открывает его через «Открыть сайт», проверяет
  контур и нажимает кнопку. Допустим новый drawId при тех же остальных фильтрах.
  Геометрия должна реально поступить от source adapter, страница не должна
  менять адрес во время захвата. Имеющийся эталон автоматически не заменяется.
- По последующему прямому уточнению владельца 03.11.2026 автоматическое первое
  сохранение выполняется после загрузки, когда Avito отдал валидную геометрию
  выделения и остальные фильтры сохранены. Совпадение исходного ID или request
  correlation больше не требуются. Это явная политика доверия Avito при первом
  восстановлении области из сохранённой ссылки. Один drawId без геометрии или
  viewport не создают эталон. Ручное подтверждение старых ссылок не требуется.
- Для известного эталона достаточно эквивалентной геометрии при строгих фильтрах.
  Polygon/MultiPolygon, GeoJSON Feature/FeatureCollection, holes поддерживаются;
  направление/начало обхода, порядок holes и частей, округление до 7 знаков
  не меняют подпись. Замкнутость/диапазоны/вырождение/самопересечения колец
  проверяются с лимитами: до 64 polygons, 128 rings/polygon, 1024 points/ring,
  суммарно 12000 points. Выход за поддержанные границы — отсутствие подтверждения,
  а не разрешение собрать произвольную область.
- Декодер принимает старый drawAreaBase64 и явный GeoJSON drawArea на существующем
  публичном map/markers endpoint. Произвольный bbox/viewport не используется.
  Новые неизвестные endpoint/форматы остаются неподтверждёнными. Автоматических
  дополнительных запросов, обхода защиты или переключения списка в карту нет.
- В основной результат проходят только source observations с корректными
  координатами внутри эталона. Внешняя граница включается, отверстия исключаются.
  Данные за пределами не включаются; сведения без подтверждённых координат
  сохраняются отдельно, в основной Catalog/Server не выдаются за проверенные.
- Неизвестный эталон/неполученная геометрия/другая геометрия дают Partial
  (ReasonCode END_NOT_CONFIRMED), реальные изменённые фильтры — SourceError
  (INVALID_SEARCH_URL). Warnings AREA_REFERENCE_UNAVAILABLE,
  AREA_GEOMETRY_UNAVAILABLE, AREA_GEOMETRY_CHANGED, AREA_COORDINATES_MISSING,
  AREA_FILTERS_CHANGED дают точную локальную причину. UI показывает «Неполный сбор».
- Для Partial полезные подтверждённые observations сохраняются, включая снимок
  контрольного чтения и ожидание геометрии при исчерпании scroll budget.
  Incomplete page не помечается Completed; EndReached=false. Сервер получает
  сохранённые observations и финальный Partial. Не менялись Chunk(25), стабильные
  ResultId/outbox и JobId+LeaseId fencing. Повторами Server продолжает управлять сам.
- Ручная CAPTCHA/авторизация остаются ручными gate. После снятия CAPTCHA
  проверка защиты и проверка геометрии независимы: новый drawId не удерживает
  исправную страницу в состоянии CAPTCHA; область заново проверяет collection loop.
- Эталон, последнее evidence и quarantined observations расположены в
  local-data/search-areas/ под ключом local-/server-<hash>.json,
  *-evidence.json и *-unverified.json. Evidence содержит оба контура, draw references,
  job/time, статус, числа missing/outside; HTTP bodies/auth/cookies/storage не пишутся.
  Quarantine ограничен 5000 records, превышение помечается Truncated. Основные
  подтверждённые observations этим лимитом не обрезаются.

## Изменённые файлы текущего этапа

- Новые LocalCollection/SearchAreaGeometry.cs, SearchAreaStore.cs, SearchAreaGuard.cs.
- Models.cs, AvitoMapCapture.cs, JsonResponseDiagnostics.cs, AvitoSearchIdentity.cs,
  DomSourcePage.cs, QueueRunner.cs, LocalStore.Workspace.cs.
- Desktop/WorkspaceController.cs, WorkspaceWindow.xaml и .xaml.cs.
- Новый SavedSearchAreaTests.cs; актуализированы AvitoMapTests.cs,
  AvitoSearchIdentityTests.cs, WorkspaceUiTests.cs под подтверждение координат
  и Partial вместо прежней паузы/слепого доверия list URL.
- README, ACTIVE_TASK, Gate и этот отчёт.

Server API/schema/версии контрактов/пакеты не менялись. Все накопленные
доработки Media/Procurement и предыдущие Collector изменения сохранены.
HTML владельца не копировался в Git/fixtures. Пагинация и HTML retention per-link
не исправлялись: это отдельные обсуждённые задачи.

## Проверки

`dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-restore`
— 0 warnings/errors. Финальный прогон:
`./artifacts/parser-saved-area/Test-Acceptance.ps1` — 141/141 Passed.
Evidence: artifacts/parser-saved-area/tests/saved-area-final.trx.
Script содержит точный whitelist тестов и исключает все browser/live методы.

Проверены geometry identity, holes/boundaries/MultiPolygon, точность, malformed,
bootstrap/перезапуск/изоляция/коррупция/невозможность подмены эталона, смена ID,
фильтры, loading, missing/resolved coordinates, явный захват из браузера через
fake ISourcePage, Partial/coverage, сохранность useful points при нехватке scroll
budget, итоговый контроль, ручная защита, UI-кнопка/CompletionKind. Результаты
собраны в реальный contract classifier/outbox на temp SQLite: пакеты <=25,
тот же JobId/LeaseId, те же ResultId после повторного открытия outbox.
Существующие queue/Cian/classifier/диагностика/reliability tests включены.

Первоначальный тест поймал ошибочный Completed checkpoint перед Partial;
исправлено до итогового прогона. После финального аудита добавлена регрессия
ручного resume после нового drawId и повторно пройден весь whitelist.
git diff --check по изменяемым Parser/README/ACTIVE_TASK — без ошибок whitespace.
Полный diff даёт прежние CRLF normalization warnings других локальных изменений;
чужие файлы не переписывались.

## Установка и сохранность

Release staging:
`dotnet build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -o artifacts/parser-saved-area/local-update/ready`
— 0 warnings/errors. Три собственные DLL staging по SHA-256 совпали с итоговой
сборкой, прошедшей тесты.

Parser был запущен, одно задание PausedByUser. Process.CloseMainWindow не смог
найти главное окно из sandbox; Computer Use окно обнаружил, но доступ к desktop
не был одобрен. UI-действия не выполнялись; владелец сам закрыл приложение.
Принудительного завершения не было. После этого процессов Parser не было.

Рабочая папка C:\.Projects\LandErp\artifacts\LandErp-Parser обновлена.
Первое копирование: 4 файла с backup-bb6df18d17d74cb1aa114eb4942a08cf;
после последней регрессии resume — 2 файла с backup-f888e60829974d77ba6d2902a6d8c608.
Все backup и manifests лежат в artifacts/parser-saved-area/local-update/.
Финальная SHA-256 проверка всех 155 программных файлов успешна;
15 защищённых файлов БД/outbox/настроек/последнего HTML/JSON и двух архивов
неизменны. Проверка counts до/после штатного закрытия: 60101 observations,
7350 listings; активных задач после закрытия 0. Содержимое бизнес-данных
через LocalStore не открывалось/не мигрировалось, только read-only sqlite3.
Сборки и все тестовые базы/контуры — отдельные artifacts/temp.

Evidence: verification.json, manifest.json, protected-before.json,
initial-*.json, runtime-before.json/runtime-after.json, Update-WorkingParser.ps1
в local-update. Существующие extras не удалялись, browser profiles не менялись.
Автоматический запуск/live/production/Git commit/push/merge не выполнялись.

## Практические ограничения

Эталон хранится на этом компьютере, не синхронизируется через Server. Старые
ссылки автоматически получают первый валидный source contour даже при новом ID,
если загрузка завершена и фильтры не изменились. Если источник не
отдал поддерживаемую геометрию/координаты, будет Partial с локальным evidence;
реальные новые ответы Avito в этом offline-этапе не проверялись.
Контуры с произвольной иной аппроксимацией/добавленными вершинами не объявляются
тождественными по одному внешнему виду. Следующий шаг — ручной сбор владельца
на обновлённом EXE и проверка evidence для конкретной области. Новый Gate не начат.

## Дополнение: старые ссылки без ручного подтверждения

Следующим прямым запросом 03.11.2026 владелец уточнил: первый валидный контур
выделения, восстановленный Avito при открытии сохранённой ссылки, считается
достаточным для bootstrap независимо от нового drawId. Условия: SearchResults,
загрузка завершена, неизменные фильтры, валидная source geometry. Контур из
viewport/одной строки ID не создаётся. Существующий эталон не перезаписывается.
Это осознанное доверие Avito при первом восстановлении области; доказательство
тождественности старого/нового контура на самом первом запуске не требуется.

Изменены SearchAreaGuard.cs, SavedSearchAreaTests.cs, README и текущие документы.
Старый anchored bootstrap отменён. В UI ручная кнопка остаётся дополнительной,
для обычного запуска старых ссылок её нажимать не требуется.

Release build tests/Desktop — 0 warnings/errors. Test-Acceptance.ps1 повторно:
155/155 Passed, включая новый ID/исчезнувший ID, первый запуск и повторный запуск
в Local/Server, list/map, неизменность ранее сохранённого эталона, changed filters
с эталоном и без, незавершённую загрузку, отсутствие геометрии, все неуспешные
классы страниц. Итоговый TRX скопирован в artifacts/parser-auto-area/tests/auto-area.trx.
Новых browser/live запусков не было. git diff --check — успешно.

Staging: artifacts/parser-auto-area/local-update/ready. Собственные DLL по SHA-256
совпали с тестированной сборкой. Процессов рабочего Parser перед заменой не было.
В artifacts/LandErp-Parser заменены 2 программных файла, предыдущие версии в
artifacts/parser-auto-area/local-update/backup-bacf6b2e090f49139771abef86d71b63.
SHA-256 всех 155 программных файлов совпали со staging. 15 защищённых файлов
БД/outbox/настроек/последней диагностики и архивных HTML/JSON неизменны.
Evidence: manifest.json, protected-before.json, verification.json и процедура
Update-WorkingParser.ps1 в artifacts/parser-auto-area/local-update.
Автоматический запуск/production/commit/push/merge не выполнялись.
