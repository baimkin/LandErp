# MEDIA-GALLERY — отчёт

03.11.2026. По прямому запросу владельца реализованы четыре пункта галереи.
Ветка codex/inspection-file-improvements. Предыдущие изменения сохранены.

## Результат и UX

- Общий AttachmentGallery для осмотра, пунктов чек-листа и карточки/досье.
  Адаптивная сетка фото/видео, имя, размер, метка типа, видеокадр/кнопка play.
  Изображения lazy, видео метаданные/первый кадр — только рядом с viewport.
- Модальный большой просмотр: листание стрелками/клавиатурой, фото 100–300%,
  double click, прокрутка увеличенного снимка, нативный видеоплеер/перемотка.
  Escape закрывает окно, фокус возвращается к вызвавшей кнопке, видео останавливается.
- Отдельные Скачать и Удалить в карточке миниатюры и просмотре. Подтверждение
  удаления прежним механизмом. Файл и история сохраняются; Deleted исключён
  из inline preview даже для редактора, скачивание через историю остаётся прежним.
- Pending/UploadFailed вынесены в компактный раскрываемый блок. Понятно, что
  загрузка не подтверждена; повтор выбирает исходный файл и существующий ID.
  Неподтверждённые ошибки текущего пакета также отдельным блоком в загрузчике.
  Точная причина старой ошибки не выдумывается: она не сохранялась в прежней модели.
- Стандартные design tokens и системный шрифт, без новых UI/медиа-пакетов/CDN.
  Mobile — две колонки, отдельные кнопки с размером для касания, viewer по ширине экрана.

## Доступ и транспорт

Новый GET /api/procurement/attachments/{id}/media требует авторизацию и ту же
серверную проверку tenant/case/inspector, что download. Общий private auth helper
используют старый download и новый preview. Inline только JPEG/PNG/GIF/WebP/AVIF,
MP4/WebM/MOV; SVG/HTML/PDF/внешние ссылки не исполняются в просмотре.

Available, immutable size/SHA/key проверяются до provider read. 200 full,
206 single byte range (включая suffix), 416 invalid/multi-range, no-store/nosniff,
If-Range/ETag. Yandex metadata SHA проверяется, затем только запрошенный диапазон
из downloader через ResponseHeadersRead. Проверяется Content-Range/length, OAuth
не передаётся download host, redirects ограничены прежним allowlist. Выданный
браузеру URL всегда endpoint ERP; provider capabilities/токены не раскрываются.

Stream ограничен ожидаемым количеством байт и владеет provider response.
Большое видео не буферизуется целиком и не сохраняется на диске Server.
Локальный adapter сверяет файл потоковым SHA-256 и отдаёт seeked bounded stream.
Range integrity в облаке опирается на metadata SHA immutable объекта, не на
повторное скачивание/хеширование всего видео при каждой перемотке.

CanManageAttachments в inspection read model следует из существующего workAccess,
не из назначения осмотрщика; новых ролей/прав нет. Повтор/удаление на standalone
странице повторно проверяются существующими server commands. Выявленное при UI
проверке пересоздание загрузчика после успешного повтора исправлено через
RefreshAfterUploadAsync: обновление метаданных без общего Loading/remount.
Применено и к прежним direct upload callbacks карточки/осмотра.

## Файлы

- Application/Foundation/Files: AttachmentMediaTypes.cs, FileReadStream.cs;
  Modules/Procurement/Public/ProcurementContracts.cs.
- Infrastructure/Foundation/Files: RoutedFileStorage.cs, FileSystemFileStorage.cs,
  YandexDiskFileStorage.cs, YandexDiskMediaRead.cs.
- Infrastructure/Modules/Procurement: ProcurementWorkspace.cs, ProcurementMedia.cs.
- Server/Components/Procurement: AttachmentGallery.razor/.css, CaseWorkspace.razor,
  CaseFileUpload.razor/.css; Components/Pages/SiteInspectionPage.razor;
  Components/WorkspaceComponent.cs; wwwroot/js/attachment-gallery.js.
- Server/Foundation: ProcurementEndpoints.cs, AttachmentMediaEndpoint.cs.
- Tests: MediaGalleryTests.cs; YandexDiskStorageTests.cs (range-aware mock),
  InspectionFileImprovementsTests.cs (реальный новый UX и mobile screenshots).
- ACTIVE_TASK, GATE-MEDIA-GALLERY, screens03/04, YANDEX_DISK_SETUP и этот отчёт.

## Фактические проверки

- Последняя Release build Foundation.Tests/Server: Passed, 0 warnings/errors.
- Исходный адресный core suite: 28 Passed / 1 Failed из 29. Прошли новые stream
  contract tests, Yandex/range/security, прежние inspection, parallel и direct
  upload tests. Единственный endpoint test изначально ошибочно использовал старый
  MemoryFileStorage без streaming; исправлен на настоящий FileSystem adapter.
  Дополнительно исправлено построение намеренно невалидного HTTP Range в тесте:
  TryAddWithoutValidation вместо client-side parser exception.
- Финальный endpoint + UI/workspace/parallel suite: 22/22 Passed, 20 секунд.
  Проверены 200/206/suffix/416, no-store/inline MIME, anonymous/foreign tenant,
  soft deletion: preview denied, history download bytes retained. Core suites
  пересекаются, их количества не суммируются. Evidence:
  artifacts/media-gallery/tests/gallery-core-initial.trx и gallery-endpoint-ui-final.trx.
- Реальный Edge/Blazor browser flow после исправления remount: Passed, 1 мин 10 с.
  Фото PNG и WebM созданы синтетически в canvas/MediaRecorder; пользовательские
  данные и внешнее облако не использованы. Проверены пакет документов/30 МиБ видео,
  история/скачивание после удаления, фото/zoom/Arrow/Escape, настоящее WebM playback,
  fallback невалидного MP4, failed block и восстановление того же ID без дубля,
  mobile viewer и удаление из viewer с сохранением исходного файла.
  После визуальной проверки увеличены mobile touch targets/раскладка действий;
  финальный скриншот дожидается завершения старой анимации sidebar.
  Итоговый повтор: artifacts/media-gallery/tests/gallery-browser-mobile-verified.trx.
- Desktop/mobile screenshots в artifacts/media-gallery: gallery-grid-desktop.png,
  gallery-grid-mobile.png, gallery-photo-desktop.png, gallery-photo-mobile.png,
  gallery-video-desktop.png. Скриншоты просмотрены, источник — работающий тестовый ERP.
- git diff --check и whitespace новых исходников: Passed.

## Ограничения и следующий шаг

Новых packages/migrations/ролей нет; рабочие данные, production, Collector,
commit/push не изменялись. Галерея использует уже сохранённые файлы. Отдельных
сжатых thumbnail-файлов/перекодирования нет: фото lazy читаются из оригинала,
видео first-frame/metadata требуют поддержки контейнера/кодека браузером.
MOV/HEVC не гарантированы на всех устройствах; при ошибке есть Скачать оригинал.
Скачивание остаётся прежним buffered/integrity path, новый streaming — для просмотра.

Реальное Range-поведение рабочего Яндекс Диска и физический iPhone/Safari не
проверялись: локальный HTTP/file backend и Yandex HTTP contract mock — evidence
реализации, не production acceptance. На сервере потребуется опубликовать также
предыдущий MEDIA-PARALLEL-UPLOAD, от которого зависят текущие upload callbacks.
Следующий шаг — публикация по запросу владельца, обновление им сервера и проверка
реальных фото/видео на своих устройствах. Следующий Gate не активирован.
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