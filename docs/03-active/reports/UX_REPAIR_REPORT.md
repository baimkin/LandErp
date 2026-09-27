# UX_REPAIR_REPORT — 27 сентября 2026

## Границы и состояние

Одна утверждённая задача GATE-UX-REPAIR, R-01–R-06. База проверена:
`42d6e67cbd052e038894893e5fc109bb9d63f4ee`; исходное дерево чистое.
Работа только в `C:/Users/user/.codex/worktrees/c346/LandErp`, ветка
`codex/ux-repair-current-screens`. Без субагентов.

Программные исправления выполнены; адресные проверки ниже прошли.
Визуальная приёмка НЕ выполнена. Исходный пользовательский дефект задачи
не воспроизведён; его первопричина не объявляется исправленной.

Все восемь указанных PNG открыты через view_image. Они использованы как
свидетельства плохого результата, не как эталон. Прочитаны согласованные
specs Incoming/ProcurementQueue/PropertyCase, композиция соответствующих
reference HTML, UI-инструкции, tokens и применимые разделы FP-004.

## Что изменилось

### R-01 — общение и задача

Прослежен путь TaskTitle → RecordCaseCommunication.NextTask → транзакция →
WorkTask → ReadTasksAsync → обновление полной карточки/панели и key по версии.
Существующие transaction, replay, права и серверное создание задачи сохранены.

Точечное чтение `landerp_local` с `default_transaction_read_only=on` нашло
PC-000004 и общение с результатом «Какое то удобное время»: effectiveAt
27.09.2026 17:00 МСК, recordedAt 17:00:40.805962 МСК. В audit именно этой
команды `Task: null`. Отдельной новой WorkTask нет; присутствует прежняя
«Рассмотреть первичный анализ». Это исключает версию о скрытой созданной задаче
для этой записи. Не доказывает, что пользователь оставил название пустым:
исходное состояние браузера/введённое название недоступно. Уточнение запрошено.

Название теперь связывается с моделью на вводе; кнопка до отправки различает
«Сохранить общение» и «Сохранить общение и задачу». Точное сообщение успеха
передаётся в карточку и панель. Backend не изменён на основании гипотезы.

### R-02 — входящие

- Именованные colgroup: выбор 48 px, широкое объявление, отдельные цена,
  цена за сотку и площадь. Удалена сломанная нумерация ширин th.
- Сортировка только заголовками; направление и aria-sort. Серверная сортировка
  и расположение неизвестных значений не менялись.
- «В медиане» — переключатель; причина недоступности раскрывается у проблемной
  строки. Выбор и участие независимы. Постоянная техническая полоса убрана.
- Выбор страницы, mixed-состояние, отдельное «Выбрать все N», снятие строк в
  общем режиме, сохранение выбора между страницами, подсветка и счётчик.
  Смена условий сбрасывает выбор. Новая серверная ExcludedIds применяется
  одинаково при preview/apply; IDs всей выдачи в браузер не передаются.
  Версии, stamp, права и транзакционная проверка сохранены. Повторное чтение
  общего выбора проверяет состав/версии, а не молча принимает изменения.
- Имя, группа и единственная автонастройка перенесены в раскрытые условия.
  Разовое применение отдельно от сохранения общего фильтра. Явное включение
  уже найденных по применённым условиям объявлений содержит число и preview.
  Автодобавление новых и сохранение ручного исключения не переписаны.

### R-03/R-04 — текст, файлы и проверки

Один CaseTextInput: настоящий textarea, нативная вставка текста, Ctrl+V
изображения, скрепка, миниатюры/имена и удаление из черновика. Нет
contenteditable, toolbar, таблиц или форматирования при новом вводе.
Применён к рабочим заметкам, результату проверки, общим заметкам проверок
и общению. Полная карточка и боковая панель используют те же компоненты.

Файлы до «Сохранить» остаются в браузерном черновике. Заметки сохраняют
защищённые IDs существующего CaseAttachment в ограниченном JSON-документе;
добавлен только узел attachment для обычного файла. Миграции и новое хранилище
не нужны. Backend проверяет организацию, объект, владельца, доступность файла;
image дополнительно требует допустимый тип изображения. URL/HTML клиента не
становятся доверенными. Общение использует прежнюю связь NegotiationId.

Если загрузка сорвалась, черновик остаётся; уже подтверждённые IDs используются
повторно. После записи общения повтор загрузки материалов не отправляет
общение/задачу повторно. При ошибке после частичной загрузки заметки уже
загруженные файлы могут оставаться во вложениях объекта, но не публикуются как
сохранённый текст заметки. Пользовательские файлы не удаляются.

Старые форматированные документы, фото и таблицы остаются видимыми и сохраняют
структуру; новый текст дополняет их. Полноценное редактирование старой таблицы
через textarea намеренно не имитируется. Простые тексты редактируются напрямую.
Tiptap больше не используется этим вводом; пакеты/лицензии массово не удалялись.

CaseChecks переиспользуется в полной карточке и панели. Проверки остаются
разделёнными на базовые/глубокие; результат не требует назначения, срока или
стоимости. Убраны объяснения технического blocker/legacy. Реальный блокирующий
вопрос и права его изменения сохранены. Общие заметки не удалены. Файл,
связанный с результатом, не дублируется второй ссылкой под той же проверкой.

### R-05/R-06 — карточка, ленты, рынок

Рост формы задач больше не растягивает соседнее фото/описание. Дата/время
задачи оформлены общими токенами. Общение сохраняет видимые способы связи,
компактную дату/время, три цены и необязательную следующую задачу без сворачивания.
Файлы доступны одинаково в карточке и панели.

Ленты используют text-ui/text-sm: дата слева, содержимое в середине, тип/автор
справа; длинный текст переносится. Форматирование бизнес-событий U-01 и его
legacy fallback не переписывались. Рыночные подписи приведены к токенам,
повтор числа участников в Home убран, название группы переносится. Сохранены
выбор нескольких групп, отсутствие группы, единицы ₽/сот., ссылки участников,
права теста спроса, формулы, Sold и dedup.

## Фактические проверки

1. SDK `10.0.401`. Первый build остановился на NU1900: недоступен сервис
   NuGet vulnerability audit. Затем locked restore Server и тестового проекта
   с `-p:NuGetAudit=false` прошёл. Версии/пакеты/репозиторные настройки не менялись;
   сетевой аудит уязвимостей не выполнен.
2. `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore --nologo`:
   финально 0 warnings, 0 errors. Промежуточные механические ошибки Razor и
   сигнатуры тестового конструктора исправлены до итоговой сборки.
3. Адресный `dotnet test ... -c Release --no-restore --filter ...`:
   **5/5** — AllResultsMinusExclusionsAcrossPagesUsesSamePreviewAndApplyCohort;
   PlainNoteFileReferencePreservesProtectionAndRejectsNonImageInImageNode;
   CommunicationAndIndependentTaskCommitOnceAndReplayNeverChangesTask (2 варианта);
   NotesReuseDossierVisibilityAndProtectedAttachments.
4. Дополнительно **2/2 PostgreSQL-сценария**:
   LegacyStatesBlockerAssignmentAndPastDueSurviveRichEditWithAuditAndConcurrency;
   RichResultsKeepCheckImageOwnershipPermissionsAndClosedStageGuards.
5. ServerRebuildsAllowedDocumentAndEncodesText первоначально упал на сравнении
   record-коллекции по ссылке после добавления ImageIds. Проверка заменена на
   равенство содержимого JSON/HTML и обоих наборов IDs. Адресный повтор **1/1 passed**.
   Всего 8 успешно проверенных вариантов; full suite не запускался.
6. `node scripts/Test-CaseTextInput.mjs`: PASS для текста, нативной вставки,
   изображения из буфера, удаления файла, отложенной загрузки, ошибки/retry,
   сохранения таблицы и отмены. Это тест адаптера с минимальной имитацией DOM,
   **не браузерный тест и не визуальная приёмка**.
7. `git diff --check`: без ошибок. Отдельно проверены отсутствие migrations,
   изменений package/lock-файлов, секретов и build artifacts в составе изменений.

Тестовые PostgreSQL-сценарии создавали только disposable БД. `landerp_local`
не изменялась; её существующие M/C/T migrations не применялись повторно.
Сайт в worktree 11de не останавливался и не использовался как проверка нового кода.
Server/Worker приложения, CUA, Playwright, browser tests и публикация не запускались.

## Короткая ручная приёмка после отдельно разрешённого запуска

При 100% zoom: 1280/1440/1920, normal/compact, узкая панель и read-only.

1. Incoming: выбор страницы → все N → снять строки на двух страницах →
   проверить счётчик/partial → preview/apply. Изменить условия: выбор сбрасывается.
   Колонка выбора узкая, название читаемо, прокрутка локальна, сортировка в трёх заголовках.
2. Раскрыть фильтры: имя/группа/автодобавление видимы. «Применить» не сохраняет
   общую настройку. Старые объявления включаются отдельным действием с числом.
3. В заметке/проверке/общении: текст, Ctrl+V фото, файл, удалить из черновика,
   отменить; повторить с сохранением. Старую таблицу открыть и дополнить:
   таблица/фото остаются. Повторить в панели.
4. Сохранить общение с непустым названием задачи: видна точная кнопка и сообщение
   успеха; задача появляется и остаётся после обновления. Пустое название — без задачи.
   Проверить ошибку файла и повтор загрузки без дубля общения.
5. Открыть форму задачи: фото/описание не тянутся. Проверить две независимые задачи,
   изменение/выполнение/удаление, длинный заголовок и дату/время.
6. Длинная лента, старые записи и файлы читаемы; рынок без группы/с несколькими
   группами/без данных, переход к участникам и изменение теста спроса работают.

Следующий шаг — ручная приёмка этой сборки после отдельного разрешения запуска.
Следующие функции, main, push, merge, rebase, reset и production не активированы.

## Изменённые файлы

- `docs/03-active/ACTIVE_TASK.md`
- `docs/03-active/GATE-UX-REPAIR.md`
- `docs/03-active/reports/UX_REPAIR_REPORT.md`
- `scripts/Test-CaseTextInput.mjs`
- `src/LandErp.Application/Modules/Catalog/Public/CatalogCalculationContracts.cs`
- `src/LandErp.Application/Modules/Procurement/Public/CaseNoteDocument.cs`
- `src/LandErp.Infrastructure/Modules/Catalog/CatalogCalculationService.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementRichNotes.cs`
- `src/LandErp.Server/ClientAssets/case-notes.js`
- `src/LandErp.Server/Components/Pages/Home.razor`
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor`
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor`
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor.css`
- `src/LandErp.Server/Components/Primitives/DemandTestPrice.razor.css`
- `src/LandErp.Server/Components/Primitives/GroupMarketPrices.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseActivityFeed.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseCheckEntry.razor`
- `src/LandErp.Server/Components/Procurement/CaseChecks.razor`
- `src/LandErp.Server/Components/Procurement/CaseChecks.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseCommunication.razor`
- `src/LandErp.Server/Components/Procurement/CaseCommunication.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseNextAction.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseRichNoteBlock.razor`
- `src/LandErp.Server/Components/Procurement/CaseRichNoteBlock.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseTextInput.razor`
- `src/LandErp.Server/Components/Procurement/CaseTextInput.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor`
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor.css`
- `src/LandErp.Server/wwwroot/css/incoming-v2-workflow.css`
- `src/LandErp.Server/wwwroot/js/case-notes.js`
- `tests/LandErp.Foundation.Tests/CaseRichNoteTests.cs`
- `tests/LandErp.Foundation.Tests/MedianParticipationTests.cs`
