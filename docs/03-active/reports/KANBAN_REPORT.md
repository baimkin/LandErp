# Канбан K-A–K-D — отчёт внедрения, 28.09.2026

## Результат

Реализованы последовательные части согласованного KANBAN_PLAN: воронки и стадии, рабочая доска и таблица, туннели, миграция и обновление Local. Визуальная приёмка владельцем ещё не выполнена.

- **K-A:** отдельные Pipeline/Stage/Membership/Transition/Tunnel в procurement; составные внешние ключи, уникальность участия, одной основной воронки и начальной стадии. Общий редактор в `Справочники → Канбан` (`/directories/kanban`) и модальном окне доски. Названия, пояснения заказчика, порядок, цвет, начальная/конечные стадии, общая видимость. Удаление — деактивация с проверкой занятости/туннелей. Серверная проверка действующей роли Owner/Administrator/ProcurementHead; для Owner/Administrator сохраняется требование MFA. CanManageTemplates не даёт доступа менеджеру.
- **K-B:** `/procurement?view=kanban&pipeline=<id>` и `view=table` читают одинаковый состав выбранной воронки; старые ссылки/фильтры обычной очереди сохраняют прежний режим. Доска использует сохранённый HTML как образец композиции, 278px колонки с горизонтальной прокруткой, без верхних показателей и постоянных фильтров. Существующая таблица и правая панель сохранены; добавлен редактируемый «Статус работы». Задача выбирается среди незавершённых/неудалённых по сроку, затем RecordedAt/Id; отсутствие — «Нет задач». Ответственный берётся из текущего Assignment. Возраст — полные сутки UTC, точная дата в подсказке по Москве. Неподтверждённые демонстрационные бейджи не перенесены.
- **K-C:** синхронные атомарные Transfer/Parallel одного PropertyCase без копирования объекта, задач или файлов. Конфликт существующего целевого участия отклоняет всё движение с названием реальной воронки/стадии; переданное участие возобновляется только явным действием. Циклы, собственная воронка, нерабочая начальная и неверная конечная блокируются. Настройка туннеля не обрабатывает уже стоящие карточки. Повтор выполненной команды возвращает сохранённый результат, в том числе после смены конфигурации.
- **K-D:** адресные PostgreSQL-тесты, Release-сборки, миграция, заполнение и рабочий Server. BusinessTimeline и append-only переходы сохраняют названия на момент события; перемещение не изменяет версию/состояние PropertyCase, назначение, задачи и бизнес-решения.

Новая воронка наполняется явным добавлением доступных объектов. Создание нового объекта из любой воронки создаёт основное участие и открывает основную воронку. Черновик настроек записывается только общей командой сохранения. При неопределённом результате перемещения UI повторяет прежнюю команду, не создаёт новый ID.

## Точная база и отделение изменений

Рабочая копия: `C:/Users/user/.codex/worktrees/af92/LandErp`, ветка `codex/kanban-ka-kd`.

Исходник: полная ux02 из задачи `01a0e349-b6ae-7843-abe9-4761aeb05c2f`, HEAD `656df0e3a870c4088a6c450078908e9976d5f358`, включая незакоммиченные UX02/U-02/U-03/O-01/UX17/UX18. Автоматический worktree первоначально был на более старом `243a61f`; реализация на нём не начиналась.

Перенесён и по SHA-256 проверен **761 файл** из tracked + untracked, неигнорируемых Git. `.git`, секреты, локальные БД и каталоги сборки не копировались. Финальная сверка исходной ux02: **0 изменений** относительно сохранённого manifest. Её HEAD, индекс и файлы не изменялись этой задачей.

Evidence (локальные ignored artifacts):
- `artifacts/kanban/baseline-sha256.json` — исходные SHA-256 всех 761 файлов;
- `source-status.txt`, `baseline-status.txt` — накопленный исходный diff;
- `ACTIVE_TASK-before.md` — прежний указатель с историей UX18;
- `change-manifest.json` — отдельный список изменений канбана относительно этой базы;
- `test-results/kanban.trx` — фактический результат тестов;
- `server.pid`, `server.out.log`, `server.err.log` — текущий локальный запуск.

Общий `git diff HEAD` включает накопленные до канбана исправления и не является списком только этой задачи. Оригиналы внешних плана и HTML сохранены; копии находятся в `docs/03-active/KANBAN_PLAN.md` и `docs/14-ui-kit/prototypes/landerp-kanban.html`.

## Фактические проверки

1. Locked restore существующих зависимостей Server, LocalSetup и Foundation.Tests с `--ignore-failed-sources -p:NuGetAudit=false`. Новых пакетов нет. Аудит уязвимостей NuGet в ограниченной сетевой среде недоступен; автоматическая попытка restore из EF получила NU1900. Использована восстановленная локальная база пакетов и EF `--no-build`.
2. `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore -p:UseAppHost=false -o artifacts/kanban/server -v:minimal` — **0 ошибок, 0 предупреждений**.
3. `dotnet build src/LandErp.LocalSetup/LandErp.LocalSetup.csproj -c Release --no-restore -p:UseAppHost=false -v:minimal` — **0 ошибок, 0 предупреждений**.
4. `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~KanbanTests --logger "trx;LogFileName=kanban.trx" --results-directory artifacts/kanban/test-results -v:minimal` — **5 пройдено, 0 ошибок, 0 пропущено**. Существующая инфраструктура создаёт и убирает только свои disposable PostgreSQL-базы. Полный набор не запускался.
5. Чтение diff относительно ux02, проверка прав/блокировок/повторяемости, сверка сохранности базы. Whitespace-замечания в унаследованных IncomingCatalogV2/CaseWorkspace относятся к исходному UX-пакету и не исправлялись ради канбана.

| Адресная проверка | Подтверждено |
|---|---|
| Настройки и права | 12 начальных стадий; Owner/Head разрешены, Manager с CanManageTemplates отклонён; Owner без MFA отклонён; чужая организация не читает доску |
| Удаление/скрытие/порядок | Занятая конечная не удаляется; скрытая сохраняет членство, дату и версию; таблица содержит её; порядок меняется без смены начальной; stale конфигурация отклонена |
| Начальное заполнение | Повтор заполнения не дублирует и не сбрасывает текущую стадию/время |
| Команды | No-op сохраняет дату; два конкурентных перемещения дают один успех; повтор возвращает прежний результат после изменения конфигурации; тот же ID с другим payload отклонён |
| Независимость | Перемещение не меняет PropertyCase.Version/StageId/AssignmentId/WorkTaskId; бизнес-отказ не меняет участие/дату, объект остаётся на доске и в таблице |
| Transfer и Parallel (2 запуска) | Одно целевое участие; исходное соответственно передано или сохранено; replay без дубля; существующая цель полностью откатывает движение и историю, сообщение называет реальную цель |
| Туннели | Обратный цикл/самопереход и удаление занятой цели отклонены |
| Выборка и гонка удаления | 52 объекта: страницы 50+2 без повторов, сумма всей выборки 5100 и одна неизвестная цена; ранняя активная задача вместо задачи без срока/удалённой; гонка удаления стадии с переносом не оставляет участия в удалённой стадии |

Первый запуск тестов остановился на ошибке тестовых данных: обязательный контекст ручного объекта был пустым. Исправлен fixture; повтор прошёл. Замечания компилятора в новых файлах исправлены адресно. Проверки не подменяют ручной сценарий пользователя.

## Миграция и Local

Создана **`20260928095409_KanbanPipelines`**: пять новых таблиц, ограничения/индексы/FK канбана и составной alternate key `(OrganizationId, Id)` существующего PropertyCase для tenant-safe FK. Значения существующих бизнес-полей не переписываются. Прежние миграции, включая UX18 `CheckNoteEntries`, сохранены.

Миграция применена штатным LocalSetup из этой рабочей копии к существующей **landerp_local**. Настройки читаются на месте из `C:/.Projects/LandErp/local-data/stage1/settings.json`, секреты не выводились и не копировались. Заполнение создаёт основную воронку и стартовые стадии с пояснениями, помещает только ранее активные объекты на начальную, без угадывания бизнес-этапа. Новые организации получают набор при provisioning, новые case — в транзакции создания. Runtime grants добавлены явно: настройки/участия SELECT/INSERT/UPDATE, история SELECT/INSERT.

После применения:
- PropertyCase: **8 → 8**, StoredFile: **5 → 5**;
- активных case: **7**, участников основной воронки: **7**;
- воронок: **1**, стадий: **12**;
- последняя миграция: `20260928095409_KanbanPipelines`.

Перед заменой заново проверены слушатель 7240 и путь DLL: это была текущая UX18. Новый Server: **PID 20000**, сборка `artifacts/kanban/server/LandErp.Server.dll`, адрес **https://localhost:7240**. PID приведён на момент проверки, перед будущим перезапуском требуется новая сверка.

Сохранены существующий Яндекс Диск и путь локальных вложений `C:/.Projects/LandErp/src/LandErp.Server/local-data/stage1/files`: сверка с БД подтвердила наличие единственного локального файла именно там. В ux02 этот файл отсутствовал, поэтому её временный каталог не использован. Остальные вложения остаются в прежнем облачном хранилище; сами файлы не перемещались.

Обычные HTTP-запросы `/health/live`, `/health/ready`, `/js/kanban.js` — **200**. Worker не запускался. Его проект собирался только как существующая ссылка Foundation.Tests.

## Границы и следующий шаг

Браузер, CUA, Playwright, CDP, скриншоты и обходные визуальные проверки **не использовались**. Ручная проверка мыши/клавиатуры, узких экранов, внешнего вида плиток/панели и реальной работы владельца пока не выполнена. HTML сверялся только как исходный текст. Сборка и health не являются визуальным принятием.

Доска рассчитана на малую рабочую очередь: выдача по 50 карточек на колонку со стабильным курсором, общие итоги по всей разрешённой выборке. Текущая реализация пакетно загружает видимые данные/задачи для расчётов на сервере; нагрузочная проверка большого числа объектов не проводилась.

Owner/Head/Manager и межорганизационная изоляция проверены серверными сценариями; отдельный живой вход Administrator, покупка через UI и все варианты ручного возврата прежнего участия не заявлены проверенными. Их серверные пути реализованы в тех же ограниченных командах.

Следующий шаг: владелец открывает `/procurement?view=kanban`, проверяет доску, таблицу, общий редактор и туннели. Main, merge, rebase, push, production, соседние справочники и новые агенты не выполнялись. Коммит не создавался; накопленный исходный diff сохранён отдельно от списка канбана.

## Файлы канбана относительно ux02
- `docs/03-active/ACTIVE_TASK.md`
- `src/LandErp.Application/Modules/Procurement/Public/ProcurementQueueV2ReadContracts.cs`
- `src/LandErp.Infrastructure/Migrations/LandErpDbContextModelSnapshot.cs`
- `src/LandErp.Infrastructure/Modules/IdentityAccess/LocalBootstrap.cs`
- `src/LandErp.Infrastructure/Modules/Organization/AuditReadService.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementServices.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs`
- `src/LandErp.Infrastructure/Persistence/LandErpDbContext.cs`
- `src/LandErp.Infrastructure/Persistence/ModelConventions.cs`
- `src/LandErp.Infrastructure/Persistence/ProductionDatabaseInitializer.cs`
- `src/LandErp.LocalSetup/Program.cs`
- `src/LandErp.Server/Components/App.razor`
- `src/LandErp.Server/Components/Layout/AppShell.razor`
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor`
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.razor.css`
- `src/LandErp.Server/Components/Procurement/CaseModal.razor.css`
- `docs/03-active/KANBAN_PLAN.md`
- `docs/14-ui-kit/prototypes/landerp-kanban.html`
- `src/LandErp.Application/Modules/Procurement/Domain/KanbanModels.cs`
- `src/LandErp.Application/Modules/Procurement/Public/KanbanContracts.cs`
- `src/LandErp.Infrastructure/Migrations/20260928095409_KanbanPipelines.Designer.cs`
- `src/LandErp.Infrastructure/Migrations/20260928095409_KanbanPipelines.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanMappings.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanProvisioning.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanReadService.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanTunnelService.cs`
- `src/LandErp.Infrastructure/Modules/Procurement/KanbanWorkspace.cs`
- `src/LandErp.Server/Components/Pages/KanbanDirectory.razor`
- `src/LandErp.Server/Components/Pages/ProcurementQueueV2.Kanban.cs`
- `src/LandErp.Server/Components/Procurement/KanbanBoard.razor`
- `src/LandErp.Server/Components/Procurement/KanbanBoard.razor.css`
- `src/LandErp.Server/Components/Procurement/KanbanCard.razor`
- `src/LandErp.Server/Components/Procurement/KanbanCard.razor.css`
- `src/LandErp.Server/Components/Procurement/KanbanSettings.razor`
- `src/LandErp.Server/Components/Procurement/KanbanSettings.razor.css`
- `src/LandErp.Server/Components/Procurement/KanbanStagePicker.razor`
- `src/LandErp.Server/wwwroot/js/kanban.js`
- `tests/LandErp.Foundation.Tests/KanbanTests.cs`
- `docs/03-active/reports/KANBAN_REPORT.md`

## Исправления по замечаниям владельца 28.09.2026

Первый вопрос владельца был ошибочно воспринят как немедленное разрешение правок; продолжение затем явно разрешено владельцем. Дополнительно запрошены общий стиль списков и сохранение вида по пользователям.

- `KanbanCard.razor`: явное строковое HTML `draggable="true"/"false"`. `KanbanBoard.razor` и `wwwroot/js/kanban.js`: синхронная нативная обработка жеста/DataTransfer и подсветки без ожидания Blazor; сервер вызывается один раз на drop. Повторный перенос во время ожидания блокируется, обработчики снимаются при закрытии доски. Серверная команда/права/версии/replay не меняются; идентификаторы из DOM проверяются по текущему разрешённому DTO. Ошибка связи отображается без утверждения, что перенос отменён.
- `KanbanBoard.razor.css`, `KanbanCard.razor.css`, `ProcurementQueueV2.razor`, `wwwroot/css/procurement-v2-polish.css`: высота канбана занимает оставшуюся часть окна с учётом фактической высоты панели; единый внутренний scroll по двум осям, sticky-заголовки. Горизонтальная полоса находится у нижней границы области доски.
- `wwwroot/css/components.css`, `ProcurementQueueV2.razor`, `KanbanStagePicker.razor`, `KanbanSettings.razor`: выпадающие списки используют общий `ui-select` и токены стандартных полей, включая состояния фокуса/disabled. Стиль подключён к фильтрам, выбору воронки, этапу в таблице/карточке и настройкам.
- `ProcurementQueueV2.Kanban.cs`, `kanban.js`: выбор таблицы/канбана и воронки сохраняется в localStorage под ключом с UserId. Настройки разных аккаунтов разделены; между браузерами/устройствами не синхронизируются. Прямые ссылки с query/фильтрами имеют приоритет; обычный вход `/procurement` восстанавливает последний выбор. Недоступная сохранённая воронка заменяется доступной default; повреждённое/заблокированное хранилище не блокирует страницу.
- «Обычная очередь» переименована в «Все воронки» с пояснением: это общий список доступных объектов закупки, включая объекты без участия в воронке, по умолчанию кроме купленных/отклонённых. Это не отдельная воронка и не очередь только объектов без воронки. Существующая серверная выборка сохранена.
- Scope дополнения записан в ACTIVE_TASK; схема БД и данные не изменены.

Проверки: `node --check src/LandErp.Server/wwwroot/js/kanban.js` — успешно; `node scripts/Test-KanbanInteraction.mjs` — успешно (изоляция аккаунтов, валидация/недоступность хранилища, синхронный dragstart, единичный drop во время ожидания, busy, detach). Это проверки логики с имитацией событий, не браузерный тест. `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore -p:UseAppHost=false -o artifacts/kanban/drag-scroll-server -v:minimal` — 0 ошибок, 0 предупреждений. Проверка пробелов изменённых отслеживаемых файлов прошла. Серверные тесты заново не запускались: серверные команды/схема не изменены.

Local обновлён: PID 23408, `artifacts/kanban/drag-scroll-server/LandErp.Server.dll`, https://localhost:7240. HTTP live/ready/JS — 200; выдача нового drag/preference JS и CSS ограничения высоты подтверждена. Существующие Database/Storage/Yandex настройки сохранены, Worker не запускался. Браузерные и визуальные проверки не выполнялись; фактический жест, внешний вид, прокрутка и восстановление после повторного входа требуют ручной приёмки владельца.
