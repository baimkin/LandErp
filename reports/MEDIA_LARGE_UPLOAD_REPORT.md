# MEDIA-LARGE-UPLOAD — итог реализации

02.11.2026. Прямым запросом владельца возобновлена и завершена реализация direct
upload; production обновляет владелец. Ветка codex/inspection-file-improvements.
Предыдущие изменения Collector/поиска/вложений сохранены. Commit/push не выполнялись.

## Результат

Браузер пользователя проверяет файл порциями по 1 МиБ, получает временную ссылку
через авторизованный ERP и отправляет байты напрямую на Яндекс Диск. ERP-сервер
не принимает файл, не хранит промежуточную копию и не передаёт OAuth браузеру.
Server подтверждает размер и SHA-256 через API провайдера; только затем Available,
чек-лист документа и событие истории фиксируются одной транзакцией.
Браузер на сервере для этой функции не требуется.

Лимит direct — 500 МиБ = 524288000 байт. Старый byte[]/JSON путь и локальный
файловый backend остаются до 50 МиБ; JSON envelope 68 МиБ не увеличивается.
Файлы пакета идут последовательно. Есть реальные проценты проверки/передачи,
отмена, стабильный UploadId, повтор незагруженных без нового вложения и восстановление
после потери ответа PUT. Подтверждение на сервере повторно проверяет текущие права,
организацию/объект и назначенного осмотрщика. Отзыв доступа не обходится выданной
ранее ссылкой: ERP не публикует файл без повторной проверки.

JS-вызовы большой передачи имеют явный deadline 31 минута (XHR — 30 минут),
вместо стандартного одноминутного ожидания межпроцессного вызова Blazor.
Неудача не объявляется успехом. Безопасная диагностика содержит operation ID,
Code/Retryable, без URL, токена, provider response или exception payload.
Имеющиеся физические файлы при удалении сохраняются, история/права прежние.

## Изменённые файлы текущего этапа

- Application: Foundation/Files/FileStorage.cs; Modules/Procurement/Public/ProcurementContracts.cs.
- Infrastructure: Foundation/Files/YandexDiskFileStorage.cs, YandexDiskDirectUploads.cs,
  RoutedFileStorage.cs; Modules/Procurement/ProcurementWorkspace.cs,
  ProcurementDirectUploads.cs; ProcurementAttachments.cs (понятный отказ удаления
  несохранённой записи вместо нарушения существующего DB constraint).
- Server: Components/WorkspaceComponent.cs; Foundation/ProcurementEndpoints.cs;
  Components/Procurement/CaseFileUpload.razor/.css, CaseUploadBatch.cs,
  CaseWorkspace.razor; Components/Pages/SiteInspectionPage.razor;
  wwwroot/js/case-upload.js, file-hash-worker.js, sha256.mjs.
- Tests: DirectAttachmentUploadTests.cs, DirectUploadComponentTests.cs,
  DirectUploadEndpointTests.cs, DirectUploadBrowserTests.cs; ReleasePackageB302Tests.cs
  (разделение buffered/direct лимитов), WorkspaceOperationTests.cs (safe diagnostics).
- scripts/Test-UploadHash.mjs; ранее созданные Test-YandexDiskDirectUpload.ps1 и
  tools/LandErp.StorageProbe (только отдельная диагностика, не зависимость ERP).
- deploy/windows/production.config.example.json; ACTIVE_TASK,
  GATE-MEDIA-LARGE-UPLOAD, YANDEX_DISK_SETUP и этот отчёт.

Метаданные Begin/Retry/Complete/Cancel имеют действующую авторизацию/CSRF,
no-store для capability и предел JSON 64 КиБ, включая chunked запросы.
Новых пакетов, migrations и ролей нет. Использован уже существующий Playwright
1.62.0; отдельная probe-утилита не устанавливается на ERP-сервер.

## Evidence и фактические проверки

1. Пользовательский production probe: INCONCLUSIVE / BrowserStart /
   PROBE_PLAYWRIGHTEXCEPTION. Этот результат не доказывает CORS-отказ.
2. Отдельная browser self-test: 2/2 Passed, разрешённый междоменный PUT проходит,
   запрещённый не отправляется; защита браузера не отключалась.
3. Live development-подключение Яндекс Диска: origin https://land.easy-sell.pro,
   preflight 200, browser PUT 201, реальные progress и provider SHA-256/size.
   Синтетический файл 60 байт оставлен в Checks. Evidence:
   artifacts/media-large-upload/direct-live-result.json. Это проверка механизма,
   не рабочего токена/CSP/устройств пользователей.
4. dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore:
   Passed, 0 warnings/errors (последняя сборка после финального изменения UI).
5. Адресный core suite: direct storage/DB/idempotency/rights/revocation,
   endpoint cookie+CSRF+foreign tenant+413/415+chunked64КиБ, safe logging,
   buffered bounds и прежние inspection/storage проверки — 39/40 в исходном
   прогоне. Единственный сбой — финальное отображение состояния загрузчика.
   Добавлен явный render после Busy=false; повтор реального компонента — 1/1 Passed.
   Остальные 39 не повторялись: их код не менялся. Evidence:
   tests/direct-upload-core-final.trx и tests/direct-upload-component-final.trx.
   Component test передаёт только метаданные 500 МиБ и запрещает OpenReadStream
   на Server; потерянный ответ PUT и повтор сохраняют ID/hash.
6. Реальный Edge headless — 2/2 Passed, 1 мин 9 с:
   - native 500 МиБ, настоящий worker и XHR между двумя local origins, приёмник
     читает по 64 КиБ; byte count и SHA-256 совпали, hash/upload progress,
     отмена до PUT и отказ 500 МиБ+1 проверены;
   - прежняя реальная форма: пакет документов, осмотр с видео 30 МиБ,
     мягкое удаление и авторизованное скачивание через историю.
   Command: LANDERP_DOTNET=<dotnet>; dotnet test tests/LandErp.Foundation.Tests
   -c Release --no-build --filter "FullyQualifiedName~DirectUploadBrowserTests|FullyQualifiedName~ActualFormsUploadMultipleFilesAndThirtyMiBVideoThenDeleteAndDownloadThroughHistory".
   Evidence: tests/direct-upload-browser-final.trx.
   500 МиБ тестировались на локальном приёмнике, не передавались в облако.
7. scripts/Test-UploadHash.mjs: 46 Passed, включая полные 524288000 байт,
   сравнение с Node crypto и повторное использование одного чанка 1 МиБ.
8. git diff --check: Passed; только существующие уведомления LF/CRLF.

Пути тестов выше относительны artifacts/media-large-upload/.
Locked cached restore probe выполнен ранее с NuGetAudit=false: свежие vulnerability
advisories были недоступны в среде. Новые версии зависимостей не добавлялись.

## Ограничения и обновление владельцем

- Production не обновлялся; iPhone/Safari и рабочий токен проверяются владельцем
  после установки через обычную форму ERP. Дополнительный серверный браузер не нужен.
- В существующем приватном production.json требуется Storage.YandexDisk.MaxFileBytes
  = 524288000. Пример обновлён, существующий приватный конфиг автоматически не меняется.
  Рабочие Token/Root/ConnectionId сохраняются. Новых migrations нет.
- При внешнем CSP разрешить HTTPS upload host провайдера в connect-src и workers
  своего сайта. В репозитории такого запрета CSP нет; внешний reverse proxy не проверялся.
- Причина исходного FileStorageException на видео 45 МиБ по старому логу не доказана:
  там отсутствует provider Code. Новый путь и безопасный Code упрощают проверку,
  но успешная локальная сборка не доказывает устранение любого серверного ограничения.
- Существующий DB constraint запрещает Deleted без storage_key/external_url:
  запись никогда не сохранённого файла сначала восстанавливается загрузкой.
  Вместо сырой DB ошибки выдаётся понятное сообщение. Schema не менялась.
- Скачивание остаётся прежним buffered read с прежними таймаутами; этот этап
  устраняет буферизацию загрузки. Галерея и отдельный потоковый download не реализованы.
- Сжатие/галерея и fallback через Server не реализуются: direct механизм подтверждён.

Следующий шаг: публикация исходников по отдельному запросу, обновление владельцем,
проверка небольшого файла и проблемного/большого видео с реального устройства.
При CORS/сетевом отказе на production зафиксировать безопасный Code и решать fallback
по фактической ошибке. Следующий Gate не активирован.
## Публикация исходников — 02.11.2026

Владелец прямо разрешил commit/push в main и запросил скрипт обновления.
Снимок текущего Gate публикуется одним коммитом после проверенного
31c0c88b0a675f021afdfcb4b9c7a3fc2a123be2, fast-forward без force.
Предыдущие локальные изменения Collector/README и его история исключены.
ACTIVE_TASK для публикации сохраняет историю удалённого main, добавляя этот этап.
Локальные ветка/индекс сохраняются; рабочий сервер агентом не обновляется.
Добавлен scripts/Update-ProductionMedia.ps1: pin reviewed SHA, clean tracked tree,
ff-only main, versioned publish, private config backup, MaxFileBytes=524288000,
штатный deploy. Секреты/подключение не выводятся и не меняются. Миграций нет.
Синтаксис PowerShell проверен; production-выполнение скрипта выполняет владелец.
Итоговый SHA и совпадение snapshot подтверждаются отдельно в
artifacts/media-large-upload/publication/verification.json.