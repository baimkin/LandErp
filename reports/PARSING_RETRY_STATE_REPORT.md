# Независимые повторы и статусы кнопок

16.11.2026. Gate PARSING-RETRY-STATE. Прямой запрос владельца после сообщения
о серых кнопках, восстанавливающихся после reload.

## Изменения

Повтор использовал общий WorkspaceComponent.Busy до завершения команды и полного
чтения страницы (включая health и историю). Он блокировал все действия поиска.
Кнопка сохраняла название «Повторить» даже после постановки; активное задание
было видно только мелкой строкой возле старого результата. Конкретную длительность
ожидания на production не измеряли, production-логи не читались.

Новый обработчик хранит попытку по SearchId, независимо от других строк и общего
Busy. Состояние разделяется между главной и списком поисков. Кнопка показывает
«Отправляем…» до ответа, «В очереди» после подтверждения, «Выполняется» по серверной
выдаче задания. Соседние строки и остальные действия страницы не блокируются
отправкой повтора. Повторный клик того же поиска отсекается до первого await.

На паузе видны кнопка «На паузе» и причина с указанием «Поиск активен» в настройках.
Штатная правка доступна. Пауза не включается автоматически; ограничения запуска
и права не расширены. Старая ошибка остаётся до результата нового запуска.

Каждая отправка проверяет текущую аутентификацию и вызывает прежнюю команду с
проверками разрешений/организации и защитой от активного дубля. Ответ подтверждения
показывается до долгого чтения. Ошибка обновления после подтверждения сохраняет
подтверждённую очередь, не предлагает слепую повторную отправку. Потерянный ответ
показывает «Не подтверждено» и «Обновить статус». Свежий успешный read уточняет
активное задание или подтверждает его отсутствие. Старый read, начатый до ответа,
не стирает локальное подтверждение/неопределённость; старые ответы не заменяют
более новый снимок страницы. Ошибки команды и чтения хранятся раздельно.

Диагностика COLLECTION_ENQUEUE_FAILED/EventId 1110 содержит OperationId, SearchId,
фазу Command/Refresh и тип исключения; без provider message, токенов и payload.
Код и БД production не менялись. Backend/schema/packages/Parser без изменений.

## Файлы

- src/LandErp.Server/Components/Pages/Collectors.Retry.cs — обработчик и представление кнопки.
- Collectors.razor, .razor.cs, .Workspace.cs, .razor.css в той же папке — разметка, чтение и ограничения действий.
- tests/LandErp.Foundation.Tests/ParsingRetryStateTests.cs, ParsingRetryComponentTests.cs, ParsingRetryBrowserTests.cs.
- ParsingWorkspaceBrowserTests.cs — прежний сценарий проверяет новое имя кнопки очереди.
- docs/14-ui-kit/screens/07-parsing-management.md, Gate, ограниченная запись ACTIVE_TASK, этот отчёт.

## Проверки

Locked restore и Release в artifacts/parsing-retry-state/build, без остановки
старого local preview. Сборка: 0 warnings/errors. Адресные component/state проверки:
4/4 Passed. Настоящий HtmlRenderer вызывает фактический обработчик страницы:
два независимых запроса с задержанным первым, подавление дубля, отсутствие общего
Busy, сохранённое подтверждение при failed read, потерянный ответ, запрет слепого
повтора, отсутствие private provider text, сохранение validation error при failed read,
уточнение неизвестного результата только новым read.

Настоящий browser/PostgreSQL: транзакция удерживает FOR UPDATE первого поиска,
второй в это время успешно ставится в очередь; затем первый завершается. По одному
Pending job на поиск. Серверное Leased показывает «Выполняется», пауза видна в
обоих списках. После завершения задания кнопка восстанавливается обычным таймером
без browser reload. Desktop 1720/mobile 390, snapshots в artifacts/parsing-retry-state/browser/.
Финальный запуск: 15/15 Passed, 0 Failed, включая прежний browser flow больших
списков и серверную историю. Ошибок JS и внешних запросов — 0 в обоих браузерных
сценариях. Снимки independent-sending-1720.png, queued-running-paused-1720.png,
retry-statuses-390.png просмотрены. Переполнения mobile документа нет.
При fault injection исправлено замещение validation error ошибкой последующего
чтения: теперь оба результата сохраняются раздельно. Evidence — retry-final.trx
в artifacts/parsing-retry-state/results/ и verification.json в той же папке задачи.

```powershell
dotnet restore tests/LandErp.Foundation.Tests --locked-mode --artifacts-path artifacts/parsing-retry-state/build
dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/parsing-retry-state/build --disable-build-servers -p:UseSharedCompilation=false
$env:LANDERP_DOTNET = (Get-Command dotnet).Source
$env:LANDERP_TEST_BUILD_ROOT = (Resolve-Path artifacts/parsing-retry-state/build).Path
dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --no-restore --artifacts-path artifacts/parsing-retry-state/build --filter 'FullyQualifiedName~ParsingRetry|FullyQualifiedName~ParsingWorkspace|FullyQualifiedName~ParsingLargeListsTests' --logger 'trx;LogFileName=retry-final.trx' --results-directory artifacts/parsing-retry-state/results
```

Проверки выполняются на одноразовой PostgreSQL через существующее private окружение;
HTTPS browser требует доступа Windows к временному тестовому сертификату. Реальные
источники и production не вызываются. Это адресные проверки, не весь foundation suite.

## Границы

Состояние попытки живёт в странице, reload получает серверную правду. Новых
таблиц/сервисов/пакетов нет. Пользовательские файлы и другие незавершённые работы
сохранены, ветка не менялась. Commit/push/deploy не выполнялись. Следующий шаг —
отдельный запрос публикации исправления после локальной проверки.
