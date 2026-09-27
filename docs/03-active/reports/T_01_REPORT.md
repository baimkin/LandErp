# T-01 — несколько задач участка

Дата: 2026-09-27. Ветка `codex/t-01-case-tasks`.
База и неизменённый HEAD: `76cda4a7139febeb83bffca07b7896262153a74f`.
Worktree: `C:/Users/user/.codex/worktrees/11de/LandErp`.

**Статус: программная часть T-01 реализована и проверена.**
Владелец согласовал сохранение пользовательских задач при покупке/отказе
с исключением из рабочих списков закрытого объекта. Правило реализовано;
ручная и визуальная приёмка остаются за владельцем.
Commit/push/merge/rebase, рабочая БД, Server/браузер не выполнялись.

## Реализованная часть

- Несколько WorkTask одного PropertyCase; создание второй не затрагивает первую.
- Общий компактный блок задач в полной карточке и боковом просмотре очереди.
  Название обязательно; тип/срок/подробности необязательны. Типы видны кнопками,
  выбор типа не заменяет название. Выполненные свёрнуты.
- Ответственный по умолчанию — текущий Assignment объекта, а не автор/Owner.
  Assignment обязателен в существующем aggregate. При недоступном исполнителе
  предлагается явный выбор допустимого сотрудника без молчаливой подмены.
- Отдельная дата, быстрые Сегодня/Завтра, текстовое HH:mm и Без срока.
  Старый точный срок, в том числе секунды, сохраняется при неизменённых полях срока.
- Редактирование на месте; подробности сохраняются и доступны под раскрытием.
  Выполнение и логическое удаление адресованы TaskId внутри CaseId.
  Удаление не удаляет строку WorkTask, audit или timeline.
- Case version + task version, организация, текущая видимость, действующий сотрудник,
  допустимые назначения; назначение задачи не расширяет доступ к case.
  Сохранён запрет изменения задач купленного объекта.
- Повтор команды использует существующий ProcurementCommandReplay в audit.
  Неизвестный результат UI повторяет тем же ключом; конфликт не затирает черновик.
- Legacy SaveNextAction направлен в тот же write boundary, редакторы единственного
  next action удалены из двух экранов.
- Передача руководителю, возврат и возобновление используют системную WorkTask;
  пользовательская/сохранённая legacy задача не перезаписывается переходом.

## Consumers и счётчики

1. ProcurementWorkspace.ReadQueueAsync/ReadCardAsync: ближайшая активная задача
   вместо WorkTaskId; при её отсутствии «Нет задач».
2. ProcurementQueueV2ReadService (Queries/Projection): ближайшая задача,
   сроки/подсветка, фильтр исполнителя по любой активной задаче.
3. Today/Overdue queue summary и slices: EXISTS по всем активным задачам,
   count объектов, одна строка case. Подписи явно говорят об объектах с задачами.
4. OverviewService: attention и team overdue — объекты; MyWork — задачи плюс
   существующие уведомления. Удалённые/выполненные задачи исключены.
   Дизайн Overview и уведомления не переделывались.
5. CaseWorkspace/ProcurementQueueV2: формат срока учитывает наличие времени;
   компактный блок задач общий.

Порядок: активные, просроченные первыми, затем назначенный срок, RecordedAt, Id.
Без срока после сроков. Date-only не скрывает уже просроченную точную задачу сегодня.
Объект может одновременно попадать в Today и Overdue по разным задачам.

## Хранение

Новая forward migration `20260927020000_CaseTasks`:

- `workflow.work_tasks.deleted` — логическое удаление;
- `is_user_task` — защита пользовательской/legacy задачи от workflow-перезаписи;
- `due_has_time` — явное различение даты и точного времени;
- индекс `(organization_id, object_type, object_id)`.

`DueAt` остаётся UTC. При `DueHasTime=false` это начало даты Europe/Moscow;
просрочка определяется сравнением с началом текущего московского дня.
При true — сравнение с текущим instant; равенство времени ещё не просрочка.
Legacy сроки получают true без изменения значений, исполнителей, текста и версий.
Все прежние PropertyCase-задачи помечаются защищёнными от перезаписи.
Русские column comments и snapshot согласованы; существующих grants достаточно,
команды проверены runtime ролью. Applied migrations не редактировались.

Новый case сохраняет обязательную внутреннюю workflow-ссылку на пустую завершённую
WorkTask. Она не показывается и не участвует в счётчиках. Фиктивный активный
«Первичный анализ» больше не создаётся; старые записи не удаляются.

К рабочей БД НЕ применены M-01 CatalogCalculationParticipation,
M-03 GroupDemandTestPrice, C-01 CaseRichNotes, C-02 CaseCheckRichResults и T-01 CaseTasks.
Новая схема применялась только к disposable `landerp_test_*` через существующую fixture.

## Файлы

- Application: Workflow/Domain/WorkModels.cs, WorkTaskDeadline.cs;
  Procurement/Public/CaseTaskContracts.cs, ProcurementContracts.cs,
  ProcurementQueueV2ReadContracts.cs.
- Infrastructure: ProcurementCaseTasks.cs, ProcurementWorkspace.cs,
  ProcurementMappings.cs, ProcurementQueueV2ReadService.cs/.Queries.cs/.Projection.cs;
  OverviewService.cs; новая migration и LandErpDbContextModelSnapshot.cs.
- Server: CaseNextAction.razor/.razor.css, CaseWorkspace.razor,
  Pages/ProcurementQueueV2.razor.
- tests/LandErp.Foundation.Tests/CaseTasksTests.cs; GATE-T-01, ACTIVE_TASK, этот отчёт.

## Фактические проверки

- SDK 10.0.401; locked restore тестового проекта с его существующими references.
  Первый restore остановлен NU1900 из-за недоступности NuGet в sandbox;
  повтор с сетевым разрешением успешен, версии/lockfiles не менялись.
- `dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --verbosity quiet`:
  успешен, 0 warnings/errors, включая Server. Первая попытка выявила CA1861
  в новой migration; механическая правка проверена повтором.
- `dotnet test ... -c Release --no-build --no-restore --filter FullyQualifiedName~CaseTasksTests`:
  первоначальные 4/4 passed — независимость/CRUD/history/replay/version;
  сроки МСК/nearest/Today/Overdue/Overview; доступ/организация/исполнитель/закрытый case;
  legacy задача и forward/return.
- Дополнительный тест `MigrationPreservesLegacyValuesAndCommentsAndAssigneeFilterSeesNonNearestTask`:
  1/1 passed. Настоящий down/up последней migration в disposable DB,
  сохранность legacy значений, комментарии, отсутствие pending model changes,
  фильтр исполнителя второй задачи и его MyWork.
- `git diff --check`: успешно.
- После последних правок фильтра/отображения срока и guard удалённых задач:
  финальная Release-сборка снова 0 warnings/errors; повторены только затронутые
  IndependentTasks, MoscowDeadlines и OrganizationVisibility — 3/3 passed.
- После согласования правила закрытия: Release-сборка 0 warnings/errors;
  `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~ClosingPreservesUserTasksAndExcludesThemFromWorkUntilResume --logger "console;verbosity=normal"`:
  **2/2 passed** (отказ/возобновление и покупка/повтор покупки). Проверены
  неизменность обеих пользовательских задач, системная задача, история,
  default queue, Today/Overdue включая архивный stage, Overview и возвращение задач.
  Первая попытка покупки выявила отсутствие CanConfirmPurchase в общей fixture;
  право явно выдано только тестовому руководителю через существующий helper,
  после чего сборка и оба варианта целевого теста прошли.
  Всего T-01 покрыт семью сценариями; прежние зелёные suites повторно не запускались.

Full suite, F/M/C suites, Live/Parser tests, foundation-скрипт, браузерные тесты
и визуальная приёмка не запускались.

## Согласованное закрытие и возобновление

Владелец выбрал сохранение незавершённых задач. При покупке и отказе
EnsureSystemTask защищает старую WorkTaskId, если она указывает на пользовательскую
задачу. Завершается только системная задача; название, подробности, срок,
исполнитель, состояние и версия пользовательских задач остаются неизменными.
Ложных событий выполнения/удаления не добавляется; событие закрытия объекта
остаётся в существующих workflow/history/audit.

Задачи видны в карточке. Default queue, Today/Overdue, task list Overview
и task-derived counters закрытые объекты исключают. Явный архивный stage
не возвращает задачи в Today/Overdue. При штатном ResumeCase после отказа задачи
снова учитываются без отмены/восстановления записей. Новый механизм возобновления
купленного объекта не добавлялся; прежний запрет его редактирования сохранён.

Дополнительной схемы для этого правила не потребовалось.
Следующий шаг — ручная приёмка владельца. T-02/U-01 не начинать.

## Ручная приёмка

1. Добавить две задачи с разными исполнителями; тип не должен менять название.
2. Проверить Сегодня/Завтра/Без срока, дату без времени и точное HH:mm по МСК.
3. Изменить название/тип существующей задачи, убедиться в сохранности подробностей.
4. Выполнить одну и удалить другую: независимость, свёрнутые выполненные, история.
5. Проверить передачу/возврат, затем отказ и покупку: задачи остаются в карточке,
   но исчезают из рабочих срезов; после возобновления отклонённого объекта возвращаются.

Визуальная приёмка остаётся за владельцем.
