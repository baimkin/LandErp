# C-01 — Свободный текст и изображения в карточке закупки

Дата: 2026-09-27. Ветка `codex/c-01-rich-case-notes`.
Проверенная база и неизменённый HEAD: `1c9c7c73410adbc83b65f75f6e543854706c39aa`.
Работа выполнена в отдельном worktree `C:/Users/user/.codex/worktrees/6d82/LandErp`.
Commit/push/merge/rebase не выполнялись. Main и рабочие БД не менялись.

## Результат только C-01

- «Основное»: самостоятельный блок «Рабочие заметки и расчёты» в левой колонке
  под фото/описанием; прежний «Ход работы» ниже, его разметка и действия сохранены.
  Рыночный блок M-03 справа не изменён.
- «Проверки»: независимый свободный текст внутри базовой и глубокой секций.
  Структурированные проверки, их блокеры, материалы и кнопки не менялись.
- Общий CaseRichNoteBlock: обычный читаемый документ, компактные действия,
  автор/время; при редактировании открытая панель заголовков, жирного/курсива,
  списков, ссылок, таблиц, изображений, undo/redo. Высота растёт по содержимому.
- Изображения из буфера, файла или уже загруженных вложений объекта.
  Это позволяет вставить и восстановленное штатным retry изображение.
- Состояние черновика обозначено; отмена/уход при изменениях требуют явного
  подтверждения. При смене вкладки редактор остаётся смонтированным в скрытой
  панели, при refresh/upload его содержимое и исходная версия не заменяются.
  На время сохранения ввод фиксируется, чтобы ответ не отбросил новый ввод.
- Конфликт версии оставляет черновик открытым. Загрузка сохранённой версии
  требует подтверждения сброса; automerge и молчаливого overwrite нет.

## Данные, права, история

`procurement.case_rich_notes`: одна строка на PropertyCase/секцию, собственная
bigint Version, серверные OrganizationId/автор/UTC-время. Версии трёх секций
независимы; сам case, Listing, исходное описание, проверки и старые заметки
timeline не перезаписываются. JSON — ограниченная документная схема C-01
поверх стандартного Tiptap JSON; не произвольный HTML и не финансовая модель.

Чтение включено в ReadCard после действующих visibility/org checks. Сохранение
использует Manager/Head и ProcurementWorkContext — тот же доступ, что CanManageDossier.
Транзакционная блокировка case защищает первую конкурентную вставку; проверяется
ожидаемая версия секции. Обновление и существующий append-only audit атомарны.
Аудит хранит полный предыдущий/новый JSON, actor, section, version, correlation ID;
его обычная проекция показывает читаемый текст до/после (до 2000 символов),
полные значения остаются в штатных технических деталях. Timeline получает
обычное событие изменения секции; отдельного хранилища версий нет.

Сервер заново строит документ из allowlist узлов/marks/атрибутов и кодирует
текст для HTML. Неизвестные узлы и опасные ссылки отклоняются, произвольные
атрибуты/обработчики удаляются. Ссылки только http/https без credentials.
Изображения хранятся как ID CaseAttachment: на сохранении проверяются case,
обе организации link/file, OwnerType.Case, Photo, Available, StorageKey,
отсутствие внешней ссылки и допустимый MIME. Публичных URL/base64 в документе нет.

Загрузка, размер и storage/retry остаются в существующем файловом контуре.
Inline endpoint `/api/procurement/attachments/{id}/image` вызывает тот же
ReadAttachmentAsync с проверкой доступа и hash/размера, допускает только
PNG/JPEG/WebP/GIF, не перенаправляет наружу, отвечает с nosniff/no-store.
Отмена/undo не удаляют вложения; неиспользованный файл остаётся в «Документах».
Новые провайдеры и общая уборка storage не добавлены.

Ограничения формата: JSON до 128 КиБ, 4000 узлов/16 уровней вложенности,
до 100 различных ID изображений; таблица до 100 строк × 20 столбцов без объединения ячеек.
Изображения до действующих 8 МиБ. SVG, внешние и base64-изображения не вставляются.

## Зависимости

Владелец отдельно согласовал только бесплатные open-source компоненты:
`@tiptap/core`, `@tiptap/pm`, `@tiptap/starter-kit`, `@tiptap/extension-image`,
`@tiptap/extension-table` — **3.31.3**, esbuild — **0.28.2**; все MIT.
Проверка package-lock.json: все Tiptap имеют одну версию 3.31.3, лицензии
всех перечисленных пакетов — MIT. npm install audit: **0 vulnerabilities**.
Проверены официальные docs/releases/license, ссылки в ClientAssets/README.md.

Bundle поставляется локально (446298 байт), license notices — отдельным
`case-notes.LICENSE.txt` (51878 байт). Сборщик проверяет license и сохраняет notices.
Прямые версии фиксированы, транзитивные закреплены lockfile. esbuild/Node нужны
только при пересборке assets; нового runtime-сервиса нет. Нет Pro/trial,
подписок, регистрации, ключей, облака и CDN. Инструкция сборки — ClientAssets/README.md.

## Изменённые файлы

- Application/Modules/Procurement: `Domain/CaseRichNote.cs`,
  `Public/CaseRichNoteContracts.cs`, `CaseNoteDocument.cs`, `ProcurementContracts.cs`.
- Infrastructure/Modules/Procurement: `ProcurementRichNotes.cs`,
  `ProcurementWorkspace.cs`, `ProcurementMappings.cs`.
- Infrastructure/Modules/Organization: `AuditReadService.cs` — только новое событие C-01.
- Infrastructure/Persistence: `LandErpDbContext.cs`, `ModelConventions.cs`,
  `ProductionDatabaseInitializer.cs` (общие runtime grants для setup/test).
- Infrastructure/Migrations: `20260926220749_CaseRichNotes.cs` и `.Designer.cs`,
  `LandErpDbContextModelSnapshot.cs` (только новые note entity/relations).
- Server/Components/Procurement: `CaseRichNoteBlock.razor(.css)`, `CaseWorkspace.razor(.css)`.
- Server/Foundation: `ProcurementEndpoints.cs`; Server: `LandErp.Server.csproj`.
- Server/ClientAssets: `package.json`, `package-lock.json`, `case-notes.js`,
  `build.mjs`, `README.md`; Server/wwwroot/js: `case-notes.js`, `case-notes.LICENSE.txt`.
- `.gitignore`, `tests/LandErp.Foundation.Tests/CaseRichNoteTests.cs`.
- Docs: `ACTIVE_TASK.md`, `GATE-C-01.md`, этот отчёт.

## Фактические проверки

1. `dotnet --version` — 10.0.401; Node 24.20.0, npm 11.19.0.
2. `npm install --ignore-scripts --no-fund --fetch-retries=0` в ClientAssets:
   зависимости установлены, lockfile создан, аудит 0 vulnerabilities.
   `npm run build` — локальный ESM bundle и полный набор MIT notices собраны.
   После добавления вставки существующего изображения bundle пересобран успешно.
3. `dotnet restore tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj --locked-mode`:
   успешно. Первый sandbox restore столкнулся с NU1900 из-за недоступного NuGet;
   повтор с доступом к сети прошёл без отключения аудита.
4. EF migration сгенерирована только локально, с фиктивным design-time
   подключением к имени `landerp_test_c01_design`; подключения/apply не было.
   Первоначально ошибочная опция `--no-connect` отклонена после успешной
   Infrastructure build; генерация повторена с `--no-build`.
5. `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CaseRichNoteTests --logger 'console;verbosity=normal'`:
   **10/10 passed**, 34.5881 s, включая Release Server build.
   До этого исправлены CA1861 в новой сгенерированной migration и raw-string
   синтаксис нового теста; production ошибки не скрывались подавлением warnings.
6. После финальных правок аудита/comments/snapshot, вставки восстановленного
   вложения и запрета объединённых ячеек повторены только три затронутых сценария:
   `SectionsSaveIndependentlyWithVersionAuditAndRuntimeSchema`,
   `NotesReuseDossierVisibilityAndProtectedAttachments`,
   `UnknownNodesExternalImagesAndOversizedDocumentsAreRejected`.
   Первый и третий прошли. Дополнение второго выявило ограничение MemoryFileStorage
   тестовой fixture (повторная успешная запись того же ID не поддерживалась).
   Сценарий исправлен на действительный сбой первой записи и штатный retry того
   же вложения. Повтор **только NotesReuseDossierVisibilityAndProtectedAttachments**:
   **1/1 passed**, 21.9937 s; Release build успешна.
7. Покрыто: независимость секций, сохранение/обновление, stale version,
   конкурентные первые/последующие записи, before/after/actor в аудите и его
   читаемая проекция; права read-only/другого отдела/организации; ID чужого case,
   организация StoredFile, pending/failed/quarantined/deleted/SVG; сохранение
   и защищённое чтение картинки, восстановление того же ID после сбоя storage;
   script-текст/неизвестные узлы/опасные ссылки/внешние и base64 картинки/лимиты.
8. Штатная fixture применяла миграции **только в своих disposable PostgreSQL DB**.
   Проверены отсутствие pending model changes, COMMENT через pg_attribute,
   реальная запись через runtime grants. SQL новой миграции получен и просмотрен:
   `dotnet ef migrations script 20260926140000_GroupDemandTestPrice 20260926220749_CaseRichNotes --project src/LandErp.Infrastructure/LandErp.Infrastructure.csproj --startup-project src/LandErp.Infrastructure/LandErp.Infrastructure.csproj --configuration Release --no-build`.
   SQL содержит только новую таблицу/FK/индексы/comments/history.
9. Финальная проверка перехода к другому CaseId уточнила loading guard:
   живые редакторы сохраняются только при refresh того же объекта; предыдущая
   карточка не показывается под новым адресом во время загрузки/ошибки.
   `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore`
   после этой правки — успешно, 0 warnings / 0 errors, 3.78 s.
10. `git diff --check` — без замечаний.

Не запускались полный solution suite, F/M suites, Parser/Live tests,
foundation-скрипт, Server, браузер, Playwright/CUA/CDP и визуальные прогоны.
Сборка зависимого Parser проекта в test project не является запуском его тестов.

## Миграции и ручная приёмка

На рабочую БД **не применены**:
- `20260926120000_CatalogCalculationParticipation` (M-01);
- `20260926140000_GroupDemandTestPrice` (M-03);
- новая `20260926220749_CaseRichNotes` (C-01).

После отдельной подготовки окружения владельцем проверить вручную:
1. Расположение трёх полей, прежние проверки и рынок M-03; desktop/узкая ширина.
2. Ввести заголовки, жирное/курсив, списки, ссылку и таблицу; сохранить,
   перечитать, изменить. Текст двух других секций остаётся прежним.
3. Вставить скриншот Ctrl+V и загрузить файл; продолжать печатать во время загрузки.
   После сохранения изображение читается внутри документа. Проверить oversized/SVG.
4. Изменить текст, переключить вкладки, обновить данные действием карточки,
   вернуться: черновик сохранён. Отмена с подтверждением возвращает сохранённое.
   При уходе по маршруту/закрытии вкладки появляется предупреждение.
5. Сбой загрузки: восстановить то же вложение в «Документах», вернуться к тексту,
   вставить через «Из вложений объекта». Отмена не удаляет файлы.
6. Две вкладки одного case/секции: после сохранения первой вторая сообщает конфликт
   и сохраняет черновик. Проверить автора/время, аудит и событие истории.

Интерактивные действия, clipboard, реальная HTTP inline-выдача изображений,
клавиатура/focus и визуальная/адаптивная приёмка **не проверялись в браузере**.
Автосохранения и долговременного восстановления после аварийного закрытия нет:
защита — сохранение живого редактора и предупреждение перед обычным уходом.
Работа остановлена на C-01; C-02/T/U/K и другие Gates не начинались.
