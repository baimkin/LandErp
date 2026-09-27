# C-02 — Упрощение структурированных проверок участка

Дата: 2026-09-27. Ветка остаётся `codex/c-01-rich-case-notes`, HEAD
`1c9c7c73410adbc83b65f75f6e543854706c39aa`.
Worktree: `C:/Users/user/.codex/worktrees/6d82/LandErp`.
Все изменения C-01 сохранены. Новой ветки/копии, commit/push/merge/rebase не было.

## Результат C-02

- В базовой и глубокой секциях собственное «＋ Проверка». Вместо прежней большой
  модальной формы — встроенная запись: одно название, состояние и сразу результат.
- Название можно набрать или выбрать из подсказок действующих шаблонов своей
  секции. При создании сохраняется прежний snapshot типового пункта/подсказки.
  Редактирование названия существующей записи не перепривязывает её старый шаблон.
  Поля «Что проверить» и отдельного выбора глубины больше нет.
- Результат использует общий CaseRichNoteBlock/Tiptap C-01: форматирование,
  таблицы, вставка/загрузка картинок, отмена, состояние черновика, конфликты.
  Дополнительные поля записи подключены к тому же editor lifecycle и тому же
  предупреждению об уходе, включая синхронный browser-side dirty guard.
  Второго редактора/хранилища/системы черновиков и новых пакетов нет.
- В чтении — название, состояние, результат с изображениями и явное «Изменить».
  Исполнитель/срок/стоимость показываются только при наличии. Связанные материалы
  остаются доступны, включая штатное восстановление через «Документы».
- Новая запись не требует исполнителя, срока или стоимости. При правке прежние
  значения передаются неизменёнными; UpdateResponsible=false сохраняет назначение.
  Старый неизменённый просроченный срок допускается прежним серверным правилом.
- Три свободных документа C-01 остаются отдельными от результатов отдельных
  проверок. Переговоры, ленты, медиана, фильтры и сценарии покупки не переделывались.

## Состояния и серверные границы

| Хранимое состояние | Подпись в компактной записи | Поведение |
|---|---|---|
| Planned | Не проверено | Начальное; результат может быть пустым |
| Passed | В порядке | Прежняя семантика Passed |
| Issue | Есть вопросы | Не включает Blocker автоматически |
| InProgress | В работе | Отдельный вариант у такой существующей записи |
| Blocked | Заблокирована | Отдельный вариант у такой существующей записи |

Legacy состояние не меняется от открытия/редактирования текста. Сотрудник может
явно выбрать другой результат. Подсчёты completed/risks и бизнес-решения не менялись.
Blocker хранится отдельно, изменение доступно только прежнему Head permission.
Менеджер может сохранить новый текст с существующим Blocker=true, но не снять его.

Сохранены title 3–512 символов и правило обязательного содержательного результата
для не-Planned; результатом может быть и защищённое изображение. Общая исходная
форма назначения больше не нужна, чтобы сразу записать Passed/Issue.

Сохранение использует существующие ProcurementWorkContext/organization/visibility,
active employee/organization lock, case lock, ExpectedCaseVersion и ExpectedCheckVersion.
`SaveCheckAsync` сохранён; новый `SaveCheckWithIdAsync` возвращает ID той же операции
для корректного завершения inline создания. Новых ролей/сервисов runtime нет.

Уточнены относящиеся к форме серверные guards:
- acquired явно запрещён к изменению проверки (раньше этот запрет был в UI);
- rejected/monitor сохраняют прежний отказ до возобновления case;
- Deep сохраняет ограничение approved/negotiation;
- шаблон другой секции отклоняется, а не переопределяет глубину после stage check;
- изменение результата существующей записи не перемещает её между секциями.

## Хранение и безопасность

Новая forward migration `20260927010000_CaseCheckRichResults` добавляет только
nullable `procurement.case_checks.result_document_json` (`jsonb`) с русским COMMENT.
UPDATE старых результатов, backfill, новые таблицы и изменение applied migrations
не нужны. Model snapshot согласован. Существующие grants на case_checks достаточны;
запись проверена через runtime роль тестовой fixture.

Если JSON отсутствует, старый Result читается буквально как текст, включая
строки, похожие на HTML; переносы строк сохраняются. Конвертация в редакторе
не принимает HTML из legacy поля за разметку. Полный новый rich result хранится
в JSON; Result остаётся текстовым представлением до прежних 4000 символов для
старых consumers/timeline. Полный текст и форматирование не теряются из JSON.
Старый API при неизменённом Result сохраняет rich JSON; явная plain-text правка
обновляет Result и очищает rich поле, оставляя предыдущий документ в аудите.

Reuse CaseNoteDocument: сервер заново строит allowlist JSON, безопасно кодирует
HTML, отклоняет опасные ссылки/узлы, сохраняет ограничения C-01.
Общий validator изображений принимает Case-вложения этого объекта и, только
для результата существующей проверки, её собственные Check-вложения.
Чужие check/case/org и недоступные файлы не принимаются. Свободные секции C-01
по-прежнему не получают доступ к Check-вложениям через новые правила.

Картинка до первого сохранения новой записи принадлежит case; после сохранения
JSON результата ссылается на неё. Для существующей записи новые изображения
загружаются как Check-вложения. Отмена файлы не удаляет, storage/retry прежние.
Audit CaseCheckSaved расширен before/after текста/состояния/блокера и полными
PreviousDocument/CurrentDocument. Отдельной истории документов нет.

## Изменённые файлы именно C-02

- Application/Modules/Procurement/Domain/PropertyCaseDossier.cs — nullable rich result.
- Application/Modules/Procurement/Public/ProcurementContracts.cs — чтение/команда/ID записи;
  CaseNoteDocument.cs — буквальное преобразование legacy текста и plain projection.
- Infrastructure/Modules/Procurement/ProcurementWorkspace.cs — read/save/check guards/audit;
  ProcurementRichNotes.cs — общий image-reference validator; ProcurementMappings.cs.
- Infrastructure/Migrations/20260927010000_CaseCheckRichResults.cs,
  LandErpDbContextModelSnapshot.cs. Предыдущие migrations сохранены.
- Server/Components/Procurement/CaseCheckEntry.razor(.css) — новая компактная запись;
  CaseWorkspace.razor — секции, удаление прежней check modal и её локальной модели;
  CaseRichNoteBlock.razor(.css) — callbacks/fields/owner context, общий dirty guard.
- Server/ClientAssets/case-notes.js и пересобранный Server/wwwroot/js/case-notes.js.
  Версии зависимостей/lockfile C-01 не менялись, notices сохранены.
- tests/LandErp.Foundation.Tests/CaseCheckEntryTests.cs.
- docs/03-active/ACTIVE_TASK.md, GATE-C-02.md, этот отчёт.

CaseFormValues изучен адресно и не менялся: новые поля даты не вводились, старые
значения срока передаются напрямую без повторного parsing/округления времени.

## Фактические проверки

1. `npm run build` в Server/ClientAssets — успешно. После окончательной правки
   синхронной защиты заголовка/состояния bundle пересобран успешно.
2. `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~CaseCheckEntryTests --logger 'console;verbosity=normal'`:
   **4/4 passed**, 47.1583 s, Release Server/test dependencies построены.
   - ReadyResultsNeedNoAssignmentAndSectionsAndTemplatesRemainIndependent:
     Quick/Deep ready results без назначения/срока/стоимости, snapshot шаблона,
     независимость свободной секции, deep guard и запрет template-depth bypass,
     отсутствие pending model changes и реальный COMMENT.
   - LegacyStatesBlockerAssignmentAndPastDueSurviveRichEditWithAuditAndConcurrency:
     InProgress/Blocked, Blocker=true, назначение, стоимость, неизменённый прошлый
     срок, прежний plain text, полный 5000-символьный rich документ и 4000-символьное
     совместимое представление, аудит, stale version, запрет снятия блокера,
     обратная совместимость старых plain commands.
   - RichResultsKeepCheckImageOwnershipPermissionsAndClosedStageGuards:
     own-check image, запрет картинки другой проверки и расширения C-01 секций,
     другой отдел/org/read-only, unsafe document, case version, acquired/rejected/monitor.
   - LegacyPlainTextIsNeverHtmlAndKeepsLineBreaksWithoutNodeExplosion:
     строки с HTML/script остаются текстом; переносы/многострочный legacy текст.
3. После финальной UI-правки защиты полей и блокировки ввода до загрузки редактора:
   `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore` —
   **успешно, 0 warnings / 0 errors**, 6.31 s. Backend после прошедших tests не менялся.
4. SQL только новой migration получен и просмотрен:
   `dotnet ef migrations script 20260926220749_CaseRichNotes 20260927010000_CaseCheckRichResults --project src/LandErp.Infrastructure/LandErp.Infrastructure.csproj --startup-project src/LandErp.Infrastructure/LandErp.Infrastructure.csproj --configuration Release --no-build`.
   Фиктивный design-time connection, без apply. Только ADD nullable jsonb,
   COMMENT и migration_history; массового изменения старых данных нет.
5. `git diff --check` — без замечаний; ветка и исходный HEAD подтверждены.

Миграции применялись только штатной fixture к своим disposable PostgreSQL DB.
Не запускались Server, браузер, Playwright/CUA/CDP, визуальные прогоны, весь C-01
набор, широкие suites/F/M/Parser/Live и foundation-скрипт. Сборка Parser как test
dependency не означает запуск Parser tests. Рабочие/production БД не менялись.

## Неприменённые миграции и ручная приёмка

К рабочей БД не применены:
- `20260926120000_CatalogCalculationParticipation` (M-01);
- `20260926140000_GroupDemandTestPrice` (M-03);
- `20260926220749_CaseRichNotes` (C-01);
- `20260927010000_CaseCheckRichResults` (C-02).

Владелец после отдельной подготовки окружения проверяет вручную:
1. Базовая «＋ Проверка»: выбрать типовой пункт/ввести своё название, сразу записать
   результат и «В порядке» без назначения и срока. Проверить ссылку, таблицу,
   вставку скриншота/файла и читаемый результат после сохранения.
2. Глубокая секция: до одобрения чтение/подсказка, после одобрения — создание
   только в этой секции. Общие свободные поля C-01 не меняются.
3. Старая InProgress/Blocked с blocker, исполнителем, прошлым сроком и стоимостью:
   изменить только текст. Состояние/блокер/дополнительные значения сохраняются;
   менеджер не снимает blocker. Проверить старые материалы и plain text с HTML-символами.
4. Изменить только название/состояние, отменить или уйти — предупреждение;
   при смене вкладки, upload и refresh текущий черновик остаётся. Отмена возвращает
   сохранённое, файлы не удаляются.
5. Две вкладки одной проверки: первая сохраняет, вторая получает конфликт с
   сохранением черновика. Загрузка актуальной версии — после подтверждения сброса.
6. Сверить C-01 в трёх прежних местах после адаптации общего компонента,
   клавиатуру/focus, компактность и узкую ширину.

Ручная и визуальная приёмка **не выполнены**. Clipboard/DOM lifecycle/подсказки
браузера/навигация проверяются владельцем; сборка не заменяет эти сценарии.
Автосохранения и восстановления после аварийного закрытия нет, как в C-01.
Работа остановлена на C-02; T/U/K и другие Gates не начинались.
