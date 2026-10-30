# INSPECTION-FILE-IMPROVEMENTS — результат

29.10.2026. Ветка `codex/inspection-file-improvements`.
Основание: владелец утвердил пункты 1–4 и отдельный план реализации в текущем чате.
Предыдущие изменения Collector сохранены. Commit/push/merge и production не выполнялись.

## Решения и поведение

- Общий CaseFileUpload используется в карточке и отдельной форме осмотра.
  До 20 файлов в партии, последовательно; содержимое всей партии одновременно
  не считывается. Отображаются результаты каждого файла, частичный сбой не
  отменяет успешные загрузки. При повторе сохраняется UploadId, успешные файлы
  не отправляются заново. Назначение/тип/комментарий партии фиксируются до повтора.
- Лимит общего файлового механизма — 50 МиБ на файл. JSON envelope — 68 МиБ
  с учётом base64. Kestrel и YandexDisk default используют общие константы.
  Форма осмотра определяет тип каждого файла отдельно, включая смешанную партию.
- Галерея/файлы и камера — разные InputFile. Только камера имеет capture.
  InputFile остаётся в DOM во время загрузки и повторов, иначе IBrowserFile
  теряет доступ к содержимому. Существующая граница подтверждённых записей
  WorkspaceComponent переиспользуется для каждого файла.
- Мягкое удаление использует существующий StoredFileStatus.Deleted.
  CaseAttachment, StoredFile, storage key, hash и содержимое сохраняются.
  Схема, миграции, роли и права не добавлялись. Provider при удалении не вызывается.
- Сервер проверяет существующий доступ Manager/Head и work scope конкретной
  карточки (тот же CanManageDossier). Назначение осмотрщиком само по себе не
  разрешает удаление/скачивание удалённого. Активные файлы читаются по прежним правам.
- Audit и событие существующей BusinessTimeline фиксируются атомарно с Deleted.
  ID события удаления равен ID вложения в другой таблице: точная ссылка без
  угадывания по имени/времени и без дополнительной схемы. Повтор удаления безопасен.
  Лента показывает автора, дату, файл и ссылку для скачивания редактору карточки.
- Deleted исключается из текущих списков, переговоров и отчётов/счётчиков осмотра.
  После удаления последнего доступного файла Received/Verified обязательного
  документа становится Missing; при оставшемся файле статус сохраняется.
- Удаление и retry используют одинаковый порядок блокировок case/file;
  retry не возвращает удалённый файл в актуальные. PendingUpload сначала требуется
  завершить/восстановить. Quarantined/Rejected не переводятся удалением в доступные.
  Download endpoint возвращает no-store и проверяет право до обращения к storage.

## Файлы

- `src/LandErp.Application/Foundation/Files/FileStorage.cs`.
- `src/LandErp.Application/Modules/Procurement/Public/ProcurementContracts.cs`.
- `src/LandErp.Application/Modules/Procurement/Public/ProcurementQueueV2ReadContracts.cs`.
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementAttachments.cs` (новый).
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs`.
- `src/LandErp.Infrastructure/Modules/Procurement/CaseTimelineAttachment.cs`.
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.cs`,
  `.Modals.cs`, `.Projection.cs`.
- `src/LandErp.Server/Components/Pages/SiteInspectionPage.razor`.
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor`.
- `src/LandErp.Server/Components/Procurement/CaseFileUpload.razor`, `.razor.css`
  и `CaseUploadBatch.cs` (новые).
- `src/LandErp.Server/Components/Procurement/CaseActivityFeed.razor`, `CaseFeedFormat.cs`.
- `src/LandErp.Server/Foundation/ProcurementEndpoints.cs`.
- `tests/LandErp.Foundation.Tests/InspectionFileImprovementsTests.cs` (новый).
- `tests/LandErp.Foundation.Tests/ReleasePackageB302Tests.cs` (название проверки
  и пояснение теперь не привязаны к прежнему лимиту 8 МиБ).
- ACTIVE_TASK, Gate и этот отчёт.

## Фактические проверки

Финальная Release-сборка Foundation.Tests со всеми затронутыми проектами —
0 warnings/errors. Адресные небраузерные проверки — 15/15 Passed;
новый сквозной Edge headless — 1/1 Passed. Всего 16 адресных сценариев,
из них 8 новых (7 без браузера и один сквозной).

Первый расширенный запуск: 15 Passed / 2 Failed (17).
Один прежний тест `DocumentChecklistTracksRequestAttachmentVerificationAndHistory`
останавливается до загрузки: ожидает 5 обязательных документов, текущая и HEAD
реализации DefaultDocumentRequirements содержат 4. Не исправлялся вне scope.
Второй прежний Browser-тест не запустился из-за отсутствующего исторического
`artifacts/stage1/dotnet/dotnet.exe`.

Повтор с `TestCategory!=Browser` и исключением указанного старого ожидания:
15/15 Passed. Evidence: `artifacts/inspection-files/tests/inspection-files-offline-final.trx`.
Новые проверки включают последовательность, partial failure/retry с тем же ID,
реальную PostgreSQL, параллельное повторное удаление, сохранность metadata/bytes,
checklist, timeline и очередь, чужую организацию/карточку, read-only, узкий work
scope, inspector, 30 МиБ video и запрет восстановления Deleted через retry.

Новый сквозной Browser-сценарий использует отдельную disposable БД и папку
`artifacts/inspection-files/browser/<id>`, тестовый HTTPS-сертификат (без установки
в trust store) и Edge headless. В ограниченной среде сервер не стартовал из-за
CryptographicException/UnauthorizedAccessException. Повтор с системным доступом
прошёл старт сервера и пакетную загрузку документов; найдено и исправлено
неоднозначное обращение теста к двум кнопкам «Закрыть».

Сквозная проверка также выявила лишний ReloadAsync после завершения партии:
в форме осмотра он пересоздавал InputFile/компонент и стирал результаты загрузки.
Убран повторный reload; существующий ExecuteConfirmedAsync обновляет данные
после каждого подтверждённого файла и сохраняет статус партии на экране.
Финальный сквозной тест Passed за 50 с: два документа через настоящую форму,
мягкое удаление, ссылка из ленты и authenticated download с проверкой байтов;
смешанная партия фото + видео 30 МиБ в форме осмотра, отдельные gallery/capture
inputs, все четыре физических файла остаются в тестовой папке после удаления.

Финальные evidence:
`artifacts/inspection-files/tests/inspection-files-regression-final.trx` (15/15),
`artifacts/inspection-files/tests/inspection-files-browser-confirmed.trx` (1/1).

Команды (секрет подключения к тестовой БД наследуется из окружения, не выводился):

```powershell
dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore
dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --filter '(FullyQualifiedName~InspectionFileImprovementsTests|FullyQualifiedName~ReleasePackageB302Tests|FullyQualifiedName~YandexDiskAttachmentTests|FullyQualifiedName~InspectionAcquisitionTests)&TestCategory!=Browser&FullyQualifiedName!~DocumentChecklistTracksRequestAttachmentVerificationAndHistory' --logger 'trx;LogFileName=inspection-files-regression-final.trx' --results-directory artifacts/inspection-files/tests
$env:LANDERP_DOTNET=(Get-Command dotnet).Source
dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~ActualFormsUploadMultipleFilesAndThirtyMiBVideoThenDeleteAndDownloadThroughHistory' --logger 'trx;LogFileName=inspection-files-browser-confirmed.trx' --results-directory artifacts/inspection-files/tests
git diff --check
```

Сквозной тест выполнялся с системным доступом к Windows crypto/key storage.
Он не меняет trust store и не использует рабочую БД/файлы. `git diff --check`
пройден; полный suite не запускался. Fixture проверяет отсутствие pending model
changes; создание новых migrations не потребовалось.

## Публикация исходников — 30.10.2026

Прямой запрос владельца: выполнить commit/push в `main`. Публикуется только
реализация этого Gate, её тесты, Gate, отчёт и запись ACTIVE_TASK. Несвязанные
локальные изменения Collector, README, архивы пользователя и test artifacts
исключены. В ACTIVE_TASK публикуется только новый этап поверх исходной истории;
неопубликованные локальные записи о доработках Collector сохраняются в checkout.

Локальные metadata Git доступны только для чтения (`fetch` не может записать
FETCH_HEAD). Используется подключённый GitHub: проверка head `main`, создание
дерева/коммита поверх него и fast-forward обновление с expected SHA, без force.
Начальная база: `c6a79c725d57064732a6676e66cfa7d862fe6bff`.
Перед публикацией повторены Release build (0 warnings/errors) и
`git diff --check`; подтверждены сохранённые результаты 15/15 и 1/1.
Итоговый SHA проверяется чтением удалённого ref и сообщается в итоговом ответе.
Локальная ветка и индекс не обновляются из-за ограничения записи metadata.
Рабочий сервер и production-БД не обновляются.

## Ограничения и следующий шаг

- Физический iPhone/Safari не проверялся; галерея/камера требуют приёмки на устройстве.
- Рабочий сервер и production-БД не обновлялись. Проверить после отдельного
  развёртывания ограничения reverse proxy (для JSON API не менее 68 МиБ)
  и возможное явное Storage:YandexDisk:MaxFileBytes в конфигурации: явно заданное
  старое значение не переписывается автоматически. Профили Collector,
  пользовательские данные и рабочее хранилище не изменялись.
- Отдельные прежние лимиты файлов в редакторах общения/задач не расширялись:
  scope относится к осмотру и досье карточки.
- Следующий этап не активирован; рекомендуемый шаг — приёмка на iPhone/Safari
  и отдельное разрешение на обновление сервера.
