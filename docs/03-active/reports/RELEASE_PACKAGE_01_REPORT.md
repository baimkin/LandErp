# Release Package 01 — журнал реализации и проверки

**Дата записи B1-01:** 18 сентября 2026 года.
**Ветка:** `codex/release-package-01`.
**План:** `../RELEASE_PACKAGE_01_PLAN.md`.
**Gate B1-01:** `../GATE-RELEASE-B1-01.md`.
**База B1-01:** `5b53c0b2f1dd3e12cfb9ec5721f38f590b69b16c` (подготовительный коммит плана; production-база `dbbfc7bb2197266186d7ac47860ba5cd07a326c4`).

## Состояние пакета

| Задача | Реализация | Проверка исполнением | Приёмка |
|---|---|---|---|
| B1-01 — LR-09 / LR-19 | Реализовано | PostgreSQL/targeted suite passed на `59181519a` | Browser/manual acceptance pending |
| B1-02 — жизненный цикл и просроченные проверки | Реализовано | PostgreSQL/targeted suite passed на `59181519a` | Browser/manual acceptance pending |
| B1-03 — назначения, Owner, политика шаблонов | Реализовано; LR-23 остаётся decision item | PostgreSQL/targeted suite passed на `59181519a` | LR-23 + browser/manual acceptance pending |
| B1-04 — передача работы и проверка пакета | Реализовано | Release build + PostgreSQL 40/40 passed на `59181519a` | Independent review + browser/manual acceptance pending |

Публикация B1-01 подтверждается SHA коммита и удалённым ref в итоговом сообщении задачи. Собственный SHA не встраивается в содержимое файла, входящего в тот же коммит. Точный commit, впервые добавивший отчёт, определяется без зависимости от текущего HEAD:

```text
git log --reverse --diff-filter=A --format=%H 5b53c0b2f1dd3e12cfb9ec5721f38f590b69b16c..HEAD -- docs/03-active/reports/RELEASE_PACKAGE_01_REPORT.md
```

Это команда идентификации для дальнейшей рабочей среды, а не заявление о её запуске в чате. Следующие реализации продолжают опубликованную цепочку пакета, а не начинают независимо от main.

## B1-01: что изменено

### 1. Подтверждение записи отделено от обновления экрана

`WorkspaceComponent` сохраняет прежний `Task ExecuteAsync(...)` для совместимости и добавляет `Task<bool> ExecuteConfirmedAsync(...)` для обработчиков, которым нужно явное подтверждение.

Результаты различаются как `NotSent`, `Rejected`, `Unknown`, `Committed` (и исходный `None`). Ошибка получения сеанса не отправляет команду. Валидация, запрет доступа и конфликт версии не показываются как успех. Общая ошибка или ошибка файлового провайдера не трактуется как доказательство отсутствия уже записанных данных.

После успешного возврата команды выставляется `Committed`. Если последующий `ReadAsync` упал, подтверждение сохраняется: «Изменения сохранены, но экран не обновлён». `RefreshRequired` показывает необходимость повторить именно чтение. `Error` для такого post-commit сбоя не устанавливается, чтобы существующие формы, ориентирующиеся на `Success`/`Error`, не предлагали повторное создание.

Начальная загрузка и уже выполняемая команда защищены от параллельной отправки; refresh не заменяет состояние во время записи. Новые targeted handlers закрываются по фактическому подтверждению, а не по предположению `Error == null`.

### 2. Устойчивый повтор ограничен тремя добавлениями

| Команда | Защищённый результат | UI |
|---|---|---|
| `CreateManualCaseAsync` | Тот же CaseId и BusinessNumber; один кейс, assignment, task, начальные документы и история | Создание в Procurement V2 |
| `AddNoteAsync` | Одна заметка или старый contact-вариант; без повторного timeline/audit и прироста версии | Рабочая заметка полной карточки |
| `AddNegotiationWithIdAsync` / `AddNegotiationAsync` | Тот же ID переговоров; без повторного контакта и истории | Полная карточка и Procurement V2 |

В соответствующие DTO добавлен необязательный `CommandId`. Существующие endpoint adapters продолжают использовать те же DTO; новые routes и миграции не нужны.

Новый `ProcurementCommandReplay` использует существующий append-only `AuditEvent`, сохраняемый в одной транзакции с бизнес-фактом. Первичный ключ события равен переданному CommandId. Технические данные `CommandReplay` содержат версию формата, SHA-256 представления команды и ID результата. Обычные action names и прежние бизнес-поля аудита сохранены.

Перед проверкой квитанции берётся PostgreSQL advisory transaction lock на идентификатор команды, до блокировки кейса. Один ключ сериализуется и между разными субъектами, поскольку PK аудита общий. Другой actor/organization получает отказ; другой payload/action — конфликт. Повтор не обходит текущие permission/visibility predicates.

`ExpectedCaseVersion` исключён из fingerprint для заметки/переговоров: это предусловие первой записи, а не новое бизнес-содержимое её повтора. Первая запись заметки по-прежнему проверяет версию. Остальное содержимое сравнивается точно, без автоматической подмены изменившихся значений. Ранее существовавшая семантика добавления переговоров не расширяется до нового правила workflow.

Права и роли не расширены. Новая таблица квитанций и универсальная платформа идемпотентности не вводились.

### 3. Изменения в формах

В черновике формы создаётся один CommandId; при повторной отправке текущего черновика используется тот же ID. После подтверждённого сохранения или явного начала нового добавления создаётся новый идентификатор. Фактическое время контакта, используемое как fallback, также фиксируется в черновике, а не меняется при каждой попытке.

Procurement V2 отображает сообщения в основном экране, drawer и затронутых модальных формах. В полной карточке добавлена кнопка повторного чтения при подтверждённой записи и неудачном обновлении. Закрытие/смена объекта в ходе затронутых добавлений ограничены.

Контакт с материалами разделён на подтверждение контакта и отдельные загрузки. Файлы считываются до отправки контакта; затем подтверждённая форма контакта закрывается. При проблеме материала показывается, что контакт сохранён, сколько загрузок подтверждено и что нужно проверить вложения, а не повторять контакт.

### 4. Безопасная диагностика

Событие `WORKSPACE_OPERATION_FAILED` / EventId 1101 содержит ID операции, фазу, компонент, метод и тип исключения. Пользовательское сообщение содержит тот же ID. Фазы различают получение сеанса, команду, валидацию, хранилище, чтение и refresh после commit.

Сам объект исключения, InnerException, его диагностическое сообщение, тело команды, формы, адреса передачи, файлы и credentials в этот лог не передаются. Сбой логгера не меняет результат бизнес-команды. Это безопасная диагностика границы вызова, а не полная трассировка всего приложения.

`AuditReadService` исключает новый технический `CommandReplay` из обычных изменений и CSV. Квитанция остаётся доступной в прежнем защищённом чтении технических деталей.

### 5. Защита совместимости осмотра

Поскольку общий обработчик теперь сохраняет Success даже при сбое refresh, адресно изменена `SiteInspectionPage`: локальный черновик удаляется только после подтверждённой записи и успешного чтения новой версии. До такого чтения повторный старт/сохранение осмотра блокируются; отображается подтверждение и действие обновления серверной версии. Убрано прежнее безусловное утверждение «данные не перезаписаны» при любой ошибке.

Это защита от новой регрессии общего обработчика. Существующие LR-06/07 о полноценной сохранности полевого черновика и взаимодействии с материалами НЕ закрыты.

## Файлы

Всего 15 путей: 8 production source files, 4 новых тестовых файла, 3 документа. Полная карта — в Gate. CSS, пакеты, EF model, migrations, Server/Worker startup и Parser не изменялись.

## Тесты подготовлены, НЕ запускались

| Файл | TestMethod | Сценарии |
|---|---:|---|
| `WorkspaceOperationTests.cs` | 14 | commit + failed refresh, read-only retry, совместимый Task caller, валидация/конфликт/права, неизвестный результат, storage/auth errors, параллельная отправка и чтение, безопасные логи и отказ логгера |
| `ProcurementCommandReplayTests.cs` | 9 | параллельные/повторные добавления, новый service instance, другой payload/actor/org/action, rollback первой попытки, прежняя/обновлённая версия, изменение scope и отключение сотрудника, legacy-вызовы |
| `ProcurementCommandContractTests.cs` | 3 | JSON roundtrip для трёх DTO и совместимость без CommandId |
| `ProcurementReplayAuditViewTests.cs` | 1 | квитанция скрыта в обычном аудите/CSV, сохранена в защищённых технических деталях |
| **Всего** | **27** | **Ни одного результата выполнения в этой задаче нет** |

PostgreSQL-тесты используют существующий `Phase1Fixture` / disposable sandbox проекта. SQLite/InMemory вместо PostgreSQL не добавлялись. Статическая сверка не доказывает зелёную сборку, отсутствие analyzer warnings или прохождение конкурентных сценариев.

### Фактически проверено в чате

- исходные контракты, affected call sites и существующая тестовая fixture;
- diff подготовленных изменений относительно базы, в том числе отсутствие случайных переписываний больших файлов;
- сохранение action names/бизнес-полей аудита и области серверных проверок;
- раздельные пути записи/обновления/загрузки материала;
- отсутствие изменений EF model, migrations, packages и Parser;
- публикация проверяется отдельным чтением удалённого ref после обновления ветки.

**Не выполнялись:** restore, build, test, browser/WPF automation, SQL, миграции, старт Server/Worker, git diff --check, нагрузка, backup/restore и production deployment.

## Ограничения, которые нельзя потерять при продолжении

1. **Это не глобальная идемпотентность.** Вызов без CommandId совместим, но не получает гарантию повтора. Ручное создание входящего Catalog item, проверки, файлы и другие добавления не переводились на этот механизм.
2. **ID текущей формы живёт в компоненте.** Автоматическое восстановление CommandId/ввода после полного reload страницы или потери Blazor circuit не реализовано. Сохранённая на сервере квитанция переживает новый service instance; вызывающая сторона должна повторно предоставить исходный ID. Не обещать persistent browser outbox.
3. **Аудит является частью корректности повтора.** Пока допускается повтор команды, её квитанцию нельзя удалить политикой retention. Будущая архивация аудита должна сохранять возможность поиска квитанций либо вводить явный совместимый срок/механизм повторов.
4. **Fingerprint имеет контракт v1.** При изменении состава/сериализации DTO нужно сохранить проверку ранее записанных команд, а не молча считать их другим payload. Неизменённая бизнес-команда сравнивается точно; автоматический перенос на другой формат здесь не добавлялся.
5. **Полный recovery файлов остаётся LR-08.** После неопределённого результата загрузки нельзя автоматически повторять AddAttachment как новое вложение. Сначала проверяется состояние сохранённых метаданных. Контакт повторять не нужно.
6. **Осмотр ещё требует своей задачи.** Эта правка предотвращает очистку черновика из-за новой семантики Success, но не реализует весь offline/conflict workflow.
7. **Общие вызовы требуют регрессии.** Старые страницы с собственной логикой обработки Success/Error и многошаговыми action delegates нужно пройти на B1-04. Наличие нового boolean API не делает произвольный delegate атомарной транзакцией.
8. **LR-23 пока не решён.** Политику изменения общих шаблонов не утверждали и не меняли. B1-02–04 не активированы.

## Передача в режим работы — B1-04

До реализации четвёртой задачи получить опубликованную ветку и проверить накопленное состояние. Убедиться, что чужие изменения не потеряны. Использовать SDK из `global.json` и действующие правила локальной среды; не заменять PostgreSQL sandbox общей рабочей БД, не сбрасывать `landerp_local`, не применять production migrations и не запускать второй общий сервер.

Пример целевых команд ниже — **план, не выполненный evidence**. `$dotnet` должен указывать на согласованный SDK; секреты sandbox остаются в локальном защищённом окружении.

```powershell
& $dotnet restore LandErp.slnx --locked-mode
& $dotnet build LandErp.slnx -c Release --no-restore
& $dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --filter "FullyQualifiedName~WorkspaceOperationTests|FullyQualifiedName~ProcurementCommandReplayTests|FullyQualifiedName~ProcurementCommandContractTests|FullyQualifiedName~ProcurementReplayAuditViewTests"
```

Дополнительно выполнить согласованные регрессии procurement/organization/audit и остальных использующих общий обработчик страниц. Конкретный browser scope согласуется с ограничениями окружения B1-04; отсутствие разрешённого браузерного прогона не отмечать как прохождение UI-сценария.

### Обязательная пользовательская проверка

- Создать ручной кейс с искусственным сбоем последующего чтения: форма закрылась, есть подтверждение, повтор чтения не создал второй кейс.
- Повторить заметку/контакт с тем же ID после потерянного подтверждения: один факт. Изменить payload под тем же ID: понятный конфликт.
- Проверить отрицательные права, stale version и валидацию: нет успеха и самопроизвольной очистки ввода.
- Сохранить контакт с материалами, вызвать отказ провайдера: контакт один, сообщение различает контакт и вложения.
- Сохранить осмотр с отказом refresh: локальный черновик не удалён только из-за успешной команды.
- Сопоставить номер ошибки UI с безопасной записью лога; убедиться в отсутствии чувствительного содержимого.
- Проверить обычный аудит/CSV и защищённые технические детали после защищённого добавления.
- Проверить исходные и обновлённые формы в обычном успешном сценарии; не ограничиваться fault injection.

В B1-04 исправления ошибок этого пакета входят в scope. Записать точный проверенный commit, команды, количество passed/failed/skipped и ограничения. Только затем менять статус с «исполнение не проверено» на «проверено»; публикация в ветку не является production-релизом.


## B1-02 — terminal acquired и просроченные проверки

**Исходно зафиксированная база:** `14fa6064567fed4bb1a511ffc2c5576c82559dc2`.
**Фактический parent публикации:** `9e1ffd5e23d176be253126b184bef3943851e99a` (`fix: satisfy replay analyzer`), появившийся во время подготовки B1-02 и не пересекающийся с её файлами.
**Scope:** LR-01, LR-05.
**Статус:** реализовано в коде и тестах; выполнение отложено до B1-04.

### Изменённое поведение

- `DecideAsync` после проверки актуальной версии явно отклоняет обычные Procurement decisions для `acquired`. Отказ происходит до изменения stage, assignment, task, transitions, timeline, notifications или audit.
- `SaveNextActionAsync` также отклоняет изменение/повторное открытие следующей задачи у купленного объекта.
- Обычная карточка и Procurement V2 больше не выставляют `CanManagerDecide` для `acquired`; V2 не предлагает изменение следующего действия, новый контакт или старт осмотра из drawer купленного объекта. Полная карточка уже имела stage guards для dossier actions.
- `CorrectAcquisitionAsync` не менялся и остаётся текущим способом исправить подтверждённые данные покупки без снятия terminal stage.
- `SaveCheckAsync` сначала получает существующую проверку и её версию. Неизменённый `DueAt`, который к моменту сохранения уже оказался в прошлом, разрешён. Новый или изменённый прошлый срок запрещён. UTC-инвариант сохранён.

### Файлы B1-02

- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.cs`
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor`
- `tests/LandErp.Foundation.Tests/ReleasePackageB102Tests.cs` — 2 новых PostgreSQL TestMethod
- `docs/03-active/GATE-RELEASE-B1-02.md`
- `docs/03-active/ACTIVE_TASK.md`
- этот отчёт

Миграций, EF model changes, новых packages и Parser/Collector изменений нет.

### Подготовленные проверки, НЕ запущенные в B1-02

1. Купленный case: `CanManagerDecide=false` в двух read models; обычное решение и SaveNextAction отклоняются; task остаётся completed, stage/acquisition/transition/timeline/audit не меняются.
2. После этих отказов `CorrectAcquisitionAsync` успешно меняет только данные покупки и сохраняет stage=`acquired`.
3. Проверка создаётся с будущим сроком через fixed TimeProvider, затем после наступления срока закрывается с тем же DueAt.
4. Другое прошлое значение срока и новая проверка с прошлым дедлайном отклоняются; после отказов история и аудит содержат только две подтверждённые операции.

**Не выполнялись:** restore, build, tests, browser, PostgreSQL execution, Server/Worker, migrations и production operations. Фактический запуск и регрессия acquisition/check/UI входят в B1-04.


## B1-03 — назначения, Owner и политика шаблонов

**Исходная база:** `017136fb67f1e0b379a69251f97fd705032c93dc`.
**Scope:** LR-02, LR-04 и техническое отделение LR-23.
**Статус:** реализация и targeted tests подготовлены; исполнение отложено до B1-04.

### LR-02

Добавлен единый расчёт eligibility получателя относительно фактической видимости case. Он различает текущее чтение, получение case assignment и получение manager+assignment. Семантика scope не расширяется: task/check сами по себе не превращают `AssignedObjects` или `Own` в доступ.

Decision targets требуют `QueueRead` плюс нужное decision permission. Forward допускает AssignedObjects recipient, потому что после операции он становится case assignee; Own recipient без ownership отклоняется. Return может сделать manager новым manager+assignee, поэтому Own/AssignedObjects становятся корректными после операции.

Для следующего действия и ответственного проверки используются только recipients, которые уже видят текущий case. В полной карточке появился отдельный `Assignees`, а Procurement V2 строит `AvailableAssignees` по той же модели.

### LR-04

Отключение сотрудника и изменение assignment/role получают общий organization-level advisory transaction lock до проверки последнего Owner. Проверка и изменение выполняются внутри транзакции. Два параллельных deactivate или смешанные deactivate/demote сериализуются и не могут оба удалить последние active Owner grants.

### LR-23

Эффективные права не менялись. Введены отдельные `CanManageTemplates` и `RequireTemplateManagementPermissionAsync`, которые сейчас повторяют прежнюю Manager/Head политику.

**LR-23 остаётся открытым:** требуется продуктовая матрица read/use/edit/archive shared templates. B1-03 не присваивает себе это решение.

### Файлы B1-03

- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementVisibility.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.cs`
- `src/LandErp.Application/Modules/Procurement/Public/ProcurementContracts.cs`
- `src/LandErp.Server/Components/Procurement/CaseWorkspace.razor`
- `src/LandErp.Infrastructure/Modules/Organization/OrganizationWorkspace.cs`
- `tests/LandErp.Foundation.Tests/ReleasePackageB103Tests.cs` — 3 новых PostgreSQL TestMethod
- `docs/03-active/GATE-RELEASE-B1-03.md`
- `docs/03-active/ACTIVE_TASK.md`
- этот отчёт

Миграций, EF model changes, новых packages и Parser/Collector изменений нет.

### Новые tests, НЕ запущенные

1. post-assignment visibility для Own/AssignedObjects, stale Forward/task/check payload и совпадение UI recipient lists с сервером;
2. concurrent Owner deactivation и смешанный deactivate + role removal;
3. сохранение текущей effective template policy через отдельный capability.

**Не выполнялись:** restore, build, tests, browser, PostgreSQL execution, Server/Worker, migrations и production operations. Все фактические проверки — B1-04.


## B1-04 — передача работы и проверка пакета

**Исходный SHA:** `54fbb73a233bbfe6a085423f0ab5fd8668b916aa`.  
**Scope:** LR-03 и integration validation B1-01–B1-04.

### Реализация

Добавляется `EmployeeWorkImpact` с counts активных case responsibilities, task/check и pending approvals, а также списком recipients, которые способны принять весь набор работы.

`SetEmployeeActiveAsync` при отключении:
- сериализуется общим employee-work invariant;
- проверяет last Owner;
- при наличии активной работы требует recipient либо явный `EmergencyRevoke`;
- при recipient атомарно передаёт текущие responsibilities и отзывает доступ;
- при emergency revoke немедленно блокирует login/session через прежний Identity lifecycle, оставляет responsibility pointers на отключённом сотруднике и пишет timeline/audit о незавершённом handover.

`TransferEmployeeWorkAsync` позволяет позднее завершить передачу от уже отключённого сотрудника.

Procurement-команды, которые создают или меняют текущую responsibility, получают тот же organization-level advisory lock. Это закрывает гонку assignment ↔ deactivation.

Исторические Author/Actor поля не меняются.

### Проверки B1-04

Новый `ReleasePackageB104Tests.cs` содержит сценарии:
1. preview + обязательный explicit handover + атомарная передача manager/assignment/task/check;
2. handover pending Head approval и последующее решение новым руководителем;
3. emergency revoke с немедленным отказом доступа и поздним explicit handover;
4. конкурентное новое назначение против deactivation — финально у disabled employee нет новой активной responsibility.

Для исполнения добавлен branch-scoped GitHub Actions workflow с PostgreSQL 18. После публикации implementation commit фактические build/test результаты и SHA будут дописаны отдельным evidence/fix commit, если потребуется.

**До запуска workflow эти проверки считаются Not run.**


## B1-04 — фактическое evidence после исправлений

### Проверенный SHA

`59181519a89608e84d0c360ef8dae8c7708eefcc` — **исполняемо проверенный кодовый SHA первого release package**.

История B1-04:
- `cdb6d3d26a5e18eb4836b7eaf9ffd929f882bbab` — основная реализация handover + branch-scoped validation workflow;
- `7023bc9b29899eae7e6743522559eb8394e9c476` — исправления build/analyzer проблем, найденных первым реальным CI;
- `59181519a89608e84d0c360ef8dae8c7708eefcc` — исправление смешанной Owner concurrency после фактического PostgreSQL test failure.

### Реальные прогоны

| Run | SHA | Restore | Release build | PostgreSQL tests | Итог |
|---|---|---|---|---|---|
| `35337163531` | `cdb6d3d26` | Passed | **Failed** | Not run | Выявлены реальные compile/analyzer blockers |
| `35337366714` | `7023bc9b2` | Passed | **Passed, 0 warnings / 0 errors** | **39 passed / 1 failed / 0 skipped** | Выявлена смешанная гонка deactivate + remove Owner |
| `35337618940` | `59181519a` | **Passed** | **Passed, 0 warnings / 0 errors** | **40 passed / 0 failed / 0 skipped** | **Success** |

Финальный run `35337618940` завершён со статусом `completed / success`. Использован PostgreSQL 18 service. Test result сохранён как artifact `release-package-01-test-results`, artifact id `10544155488`.

### Что реально доказано этим прогоном

**B1-01**
- save outcome / post-commit refresh semantics;
- command replay и конфликт replay payload;
- safe diagnostics и audit representation.

**B1-02**
- terminal `acquired`;
- запрет обычного решения/next action после покупки;
- исторический просроченный DueAt существующей проверки;
- отдельная корректировка покупки.

**B1-03**
- recipient visibility / stale target rejection;
- Own / AssignedObjects semantics в targeted scenarios;
- last Owner concurrent deactivation;
- смешанная гонка deactivate + role removal;
- текущая эффективная template policy сохранена за отдельным capability.

**B1-04**
- impact preview;
- обязательный explicit handover при обычном отключении;
- transfer manager / assignment / open task / open checks без переписывания исторических authors;
- handover pending Head approval;
- emergency access revoke + поздний explicit transfer;
- assignment ↔ deactivation race;
- отзыв доступа отключённого сотрудника.

В targeted run также включены существующие Identity/Organization tests, выборочная acquisition/inspection regression и case-scope regression.

### Две регрессии, найденные именно исполнением

1. Первый build обнаружил expression-tree incompatibility, отсутствующие namespace imports и analyzer errors. Они не были замаскированы и исправлены в `7023bc9b2`.
2. Первый успешный runtime suite обнаружил, что `Serializable` snapshot мог фиксироваться при ожидании advisory lock, поэтому смешанные операции с Owner обе проходили. `ChangeAssignmentAsync` переведён на `ReadCommitted`, а advisory lock остаётся сериализатором инварианта. После этого тот же suite прошёл 40/40.

### Что НЕ проверено этим evidence

- dedicated Playwright/browser сценарий новых handover modal и emergency UI;
- ручная визуальная приёмка владельцем;
- независимый implementation review итогового diff;
- production deployment, backup/restore release drill и остальные G-01–G-06;
- продуктовая политика LR-23: read/use/edit/archive shared templates.

Эти пункты нельзя интерпретировать как Passed. **Release Package 01 имеет зелёную build/PostgreSQL integration-проверку, но ещё не является production release acceptance.**
