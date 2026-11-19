# Архивные зоны: текущие ошибки и допуск к запуску

19.11.2026. Gate PARSING-ARCHIVE-GUARD. Прямой запрос владельца.

## Поведение

Архивные SearchGroup исключены из «Требуют внимания», счётчика вкладки главной
и счётчиков категорий ошибок. Действующие зоны и поиски без группы сохраняют
свои ошибки, включая приостановленный поиск в действующей зоне. История всех
запусков остаётся доступной. Фильтр «Включая архив» сохраняет архивные поиски;
у их кнопки написано «В архиве», рядом — причина невозможности запуска.

Сервер прямо проверяет действующую группу той же организации:

- ручной enqueue пользователя и enqueue из авторизованного Parser;
- расписание due и автоматический retry;
- выдача Pending и повторная выдача просроченного Leased;
- возврат уже выданного lease после повторного Claim/reconnect.

Проверка не полагается только на SearchConfiguration.Enabled: тесты намеренно
оставляют Enabled=true при SearchGroup.Active=false. Поиски без группы допустимы,
чужая/отсутствующая группа не даёт допуск. Права и защита от активных дублей
сохранены. Код SEARCH_GROUP_ARCHIVED сообщает agent-команде причину отклонения;
web получает понятную причину архива/недоступности.

Уже выданная работа не прерывается принудительно: действующий lease может слать
heartbeat и итоговый результат по прежней проверке полномочий. Повторный Claim
архивной работы возвращает null и не назначает второе задание той же машине,
пока текущий lease не закончится. После окончания обычная очередь доступна.

Строки jobs и прежние результаты не удаляются и не переписываются. Старые Pending
и expired Leased остаются в истории с прежними состояниями, но не выдаются.
У просроченного автоматического повтора архивного поиска снимается RetryAt,
как и у выключенного поиска в прежней логике. Отдельная отмена/перевод старых jobs
в терминальный статус и изменение общих backlog/expired counters не входят в эту
правку; такие строки могут оставаться в этих операционных счётчиках.

## Файлы

- src/LandErp.Infrastructure/Modules/Collection/CollectionSearchAvailability.cs — общая проверка группы и организации.
- CollectionAdministration.cs, CollectionScheduler.cs, CollectorGateway.cs в той же папке — точки допуска.
- src/LandErp.Server/Components/Pages/Collectors.Workspace.cs, Collectors.Retry.cs, Collectors.razor — ошибки, кнопки, причина архива.
- tests/LandErp.Foundation.Tests/ParsingArchiveTests.cs, ParsingArchiveUiTests.cs — новые сценарии.
- ParsingRetryComponentTests.cs в той же папке — общая тестовая оснастка для групп/фильтра архива.
- docs/14-ui-kit/screens/07-parsing-management.md, Gate, ограниченная запись ACTIVE_TASK, этот отчёт.

Предыдущая независимая отправка повторов сохранена. Новых packages/migrations,
переписывания Parser, изменения production/config не было.

## Проверки

Locked restore и Release в artifacts/parsing-archive-guard/build, без остановки
работающего старого preview. Финальная сборка: 0 warnings/errors. PostgreSQL
использует одноразовые БД через существующее private окружение.

Новые PostgreSQL сценарии: оба ручных enqueue; чужой сотрудник; включённый поиск
в архиве; расписание и retry; старые pending/expired; активная и безгрупповая зона;
история и неизменные ID/результаты; недопуск повторной выдачи текущего архивного lease;
heartbeat/завершение уже полученной работы; некорректная ссылка на группу чужой
организации. Новые UI/HtmlRenderer проверки: архивной строки нет в текущих ошибках,
оба счётчика исключают архив, история/архивный список сохраняются, кнопка с причиной
и блокировка запроса даже при Enabled=true. HTML проверки учитывают scoped CSS атрибуты.

```powershell
dotnet restore tests/LandErp.Foundation.Tests --locked-mode --artifacts-path artifacts/parsing-archive-guard/build
dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/parsing-archive-guard/build --disable-build-servers -p:UseSharedCompilation=false
$env:LANDERP_DOTNET = (Get-Command dotnet).Source
$env:LANDERP_TEST_BUILD_ROOT = (Resolve-Path artifacts/parsing-archive-guard/build).Path
dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --no-restore --artifacts-path artifacts/parsing-archive-guard/build --filter 'FullyQualifiedName~ParsingArchive|FullyQualifiedName~ParsingRetryComponent|FullyQualifiedName~ParsingRetryState|FullyQualifiedName~CollectionPoolTests|FullyQualifiedName~CollectionSchedulingTests' --logger 'trx;LogFileName=archive-regression.trx' --results-directory artifacts/parsing-archive-guard/results
dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --no-restore --artifacts-path artifacts/parsing-archive-guard/build --filter 'FullyQualifiedName~ParsingWorkspace|FullyQualifiedName~ParsingRetryBrowser|FullyQualifiedName~ParsingLargeListsTests' --logger 'trx;LogFileName=browser-regression.trx' --results-directory artifacts/parsing-archive-guard/results
node scripts/Test-ParsingLists.mjs
```

Browser/HTTPS запускается с доступом Windows к временному ключу сертификата,
только localhost/синтетическая БД. Проверка JS storage — 11 случаев Passed.
Финальные результаты: archive-regression.trx — 26/26 Passed,
browser-regression.trx — 11/11 Passed, всего 37 разных .NET случаев, 0 Failed.
В browser-проверках ошибок JS и внешних запросов — 0. Регрессионные desktop/mobile
снимки предыдущего retry flow и истории просмотрены. Архивная фильтрация/счётчики
и причина недоступности проверены на фактическом HTML страницы (HtmlRenderer).
git diff --check для затронутых tracked файлов прошёл. Итоговые hashes/evidence —
artifacts/parsing-archive-guard/verification.json.
Это адресные проверки, не весь foundation suite и не production acceptance.

## Границы

Работающий сервер и настоящие источники не вызывались. Остальные локальные задачи,
ветка и накопленные изменения сохранены. Commit/push/deploy не выполнялись.
Следующий шаг — отдельная публикация после проверки владельцем.
