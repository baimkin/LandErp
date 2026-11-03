# MEDIA-PARALLEL-UPLOAD — отчёт

03.11.2026. Владелец разрешил до пяти параллельных direct uploads, скорость,
понятные этапы и время; проверка файлов строго последовательная.
Ветка codex/inspection-file-improvements. Накопленные изменения сохранены.

## Реализовано

- До пяти активных передач на открытый загрузчик. Один hash worker, чтение
  порциями по 1 МиБ. Проверка следующего файла может идти рядом с передачей,
  но два расчёта SHA-256 одновременно не запускаются. Лимит остаётся 500 МиБ.
- Слот передачи освобождается после HTTP-ответа на PUT, до server confirmation.
  Подтверждение предыдущих файлов не останавливает отправку следующих.
- Статусы: очередь → проверка с процентами → получение ссылки → передача
  с байтами/скоростью → ожидание ответа Яндекса → проверка сохранения → загружен.
  После отправки тела нет misleading «100% = готово»: ожидание отдельное.
- Скорость — оценка по browser upload progress, окно 3 секунды. После отсутствия
  обновлений 3 секунды скорость отображается нулевой. Она не измеряет скорость
  записи на стороне провайдера. Прогресс ограничен примерно двумя сообщениями
  в секунду на файл; UI таймер один на пакет, раз в секунду.
- Для каждого файла видны время очереди, проверки, ссылки, передачи, ответа,
  подтверждения и всего. Browser timestamps разделяют send/response.
  Логи UPLOAD_TIMINGS / EventId 1103 содержат только UploadId и пять длительностей
  в миллисекундах. Имена файлов, токены, URL и hash в них не передаются.
- Ошибки независимы. Повторяет только незавершённые, с прежними ID и hash,
  если файл выбран в той же форме. Отмена останавливает текущий worker/XHR
  и очередь; уже подтверждаемые файлы завершают свою проверку.
- Native inputs остаются в DOM до конца пакета. Server не читает IBrowserFile.
  Сбой загрузки JS module можно повторить; двойное нажатие не запускает второй пакет.
- Buffered/local путь остаётся последовательным и ограниченным 50 МиБ.

## Серверные границы

Per-file metadata operations имеют собственную актуальную аутентификацию и
correlation ID, не разделяют Busy/OperationId/result страницы. Карточка/осмотр
обновляются один раз после пакета, не после каждого Begin/Complete.

Provider confirmation вынесен из DB transaction: сначала авторизация/снимок,
затем ожидание хранилища без case/file locks, затем повторная проверка текущих
прав, tenant/case/owner/hash/size и атомарный finalize. Отзыв доступа во время
ожидания не обходится. Конкурентный успешный finalize не понижается поздней ошибкой.
Новые роли, migrations, физическое удаление и изменения download не добавлены.

## Файлы текущего Gate

- docs/03-active/ACTIVE_TASK.md, GATE-MEDIA-PARALLEL-UPLOAD.md,
  YANDEX_DISK_SETUP.md; этот отчёт.
- src/LandErp.Server/Components/Procurement/CaseUploadBatch.cs,
  CaseFileUpload.razor/.css, CaseWorkspace.razor;
  Components/Pages/SiteInspectionPage.razor; Components/WorkspaceComponent.cs.
- src/LandErp.Server/wwwroot/js/case-upload.js, upload-metrics.mjs.
- src/LandErp.Infrastructure/Modules/Procurement/ProcurementDirectUploads.cs.
- tests/LandErp.Foundation.Tests/ParallelUploadTests.cs,
  DirectAttachmentUploadTests.cs, DirectUploadBrowserTests.cs,
  WorkspaceOperationTests.cs; scripts/Test-UploadMetrics.mjs.

## Фактические проверки

- dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore:
  последняя сборка Passed, 0 warnings/errors.
- Core regression — 46/46 Passed, 1 мин 42 с. Фильтр: ParallelUploadTests,
  DirectAttachmentUploadTests, DirectUploadComponentTests, DirectUploadEndpointTests,
  WorkspaceOperationTests, YandexDiskStorageTests, ReleasePackageB302Tests,
  InspectionFileImprovementsTests; исключены Browser/LiveStorage.
  Evidence: artifacts/media-parallel-upload/tests/parallel-core-final.trx.
  Проверено: пять слотов/один hash, шестой/седьмой продолжают при зависшем confirm,
  независимая ошибка/повтор/hash cache, Stop не стартует очередь, metadata-only
  actual component и независимый прогресс, cookie/CSRF/body bounds, soft deletion,
  idempotency и права. Новый PostgreSQL test задерживает provider response,
  начинает другое вложение того же case и отзывает доступ до finalize.
- После финальной защиты module load/double click и подписей — адресный UI suite
  21/21 Passed (5 pipeline/component, 1 прежний component, 15 workspace tests).
  Evidence: artifacts/media-parallel-upload/tests/parallel-ui-final.trx.
  Другие прошедшие серверные проверки повторно не запускались: серверная логика
  после core suite не менялась (только перенос строки).
- Browser — 2/2 Passed, 1 мин 12 с, Edge headless с включённой защитой:
  native 500 МиБ через настоящий worker/XHR и два loopback origins; hash/size,
  progress, положительная скорость, отдельный response stage, искусственная
  задержка ответа 650 мс отражена в responseMilliseconds; oversize/cancel.
  Плюс прежний настоящий Blazor flow: документы/осмотр 30 МиБ/удаление/скачивание
  через историю на disposable PostgreSQL. Evidence:
  artifacts/media-parallel-upload/tests/parallel-browser-final.trx.
  Пять слотов проверены actual Razor component с fake JS и очередью; пять
  одновременных реальных облачных PUT этим прогоном не проверялись.
- Node: scripts/Test-UploadMetrics.mjs — 11 Passed: скорость/пауза/невалидные
  события/независимость файлов/ограниченное окно; существующий Playwright Node.
- git diff --check и whitespace новых файлов — Passed.

## Ограничения и следующий шаг

Production, рабочее подключение Яндекс Диска и устройства пользователей не трогались.
Новых пакетов нет. API квота провайдера/общая пропускная способность не изменены:
пять передач не означают пятикратное ускорение, максимум относится к одному
загрузчику, не ко всем пользователям системы. Файлы по-прежнему идут напрямую
на Диск. Source SHA-256 один раз до отправки, затем сравнение с provider metadata;
повторного чтения файла устройством для проверки после загрузки нет.

Галерея/сжатие/новые версии/commit/push/развёртывание не выполнялись. Исходная
причина многоминутного production ожидания не доказана; новые этапы/замеры
покажут её при реальном повторе. Следующий шаг — публикация по отдельному
запросу, затем обновление владельцем и пакет из 6+ видео с просмотром этапов/скорости.
Следующий Gate не активирован.
## Совместная публикация — 03.11.2026

Владелец прямо поручил commit/push обеих доработок в main и скрипт обновления.
Публикуются MEDIA-PARALLEL-UPLOAD и MEDIA-GALLERY одним коммитом после
b0a18a9af7469f00756ba81ce5da8052f1f81897, fast-forward без force.
Предыдущие локальные изменения Parser/README/диагностики и их история исключены.
ACTIVE_TASK снимка сохраняет историю remote main и только два разрешённых media Gate;
общий локальный ACTIVE_TASK другого чата не перезаписывается.
Проверка отдельной сборки точного main-based snapshot, SHA всех blob и итогового main.
Рабочая ветка/индекс и production сохраняются. Скрипт — прежний проверенный
Update-ProductionMedia.ps1 с закреплённым новым SHA в отдельной копии для владельца;
ff-only, unique version, private config backup, 500 МиБ, без migrations.
Evidence: artifacts/media-gallery/publication/verification.json.