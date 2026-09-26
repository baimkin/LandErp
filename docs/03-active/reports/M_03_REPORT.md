# M-03 — Тест спроса и показатели рынка на экранах

Дата завершения: 2026-09-27. Ветка `codex/m-01-median-participation`, база
`18a6a1f26089317d91d13823183e720d95530199`. Локальные M-01/M-02 сохранены.
Изменения не опубликованы; рабочие БД не изменялись.

## Результат

Один общий ручной ориентир теста спроса RUB/сот. на организацию и активную группу
поиска. Это не измеренный спрос и не цена сделки. Пустое значение отображается
как «—»; редактор принимает положительную цену или очистку.

Сохранение требует существующего `CanHeadProcurement`. Названия должностей и
ролей не проверяются; Processor/Manager не получают право редактирования.
SystemOwner сохраняет полный доступ по существующему resolver; Administrator
подчиняется существующим явным настройкам. Read-доступ соответствует экрану:
Incoming.Read, Procurement.Read с ProcurementVisibility для карточки, прежние
границы Overview. Ссылка участников доступна только при Incoming.Read.

`GroupMarketService` — общий источник значений для всех четырёх экранов и обоих
блоков Overview. Формула перенесена из M-02 без изменений: та же выборка
CatalogMarketParticipants, точная цена на сотку, median/average с итоговым
округлением 4 знака ToEven. Тест спроса в формуле не участвует. Денежное
отображение унифицировано до двух знаков; расчётная точность не меняется.

## Экранные изменения

- Incoming: правая плитка показывает три цены для группы текущего фильтра.
  Сохранённый фильтр разрешается в его группу; черновик использует свою текущую
  группу. Дополнительные условия/текст не сужают рыночную выборку. Без группы —
  подсказка. «Обработано сегодня» сохранено отдельной кнопкой среза.
- ProcurementQueueV2: правая плитка заменена ценами. В текущих фильтрах добавлен
  выбор группы; реальные объекты фильтруются через confirmed source links,
  observations, jobs, searches и активные группы организации. EXISTS сохраняет
  одну строку объекта. ProcurementVisibility применяется до фильтра группы.
  «У руководителя» осталось в существующей строке срезов. Другие фильтры очереди
  не меняют общую рыночную выборку группы.
- CaseWorkspace, «Основное» / «Цена и торг»: блок справа от рабочей цены;
  на узкой ширине переносится ниже. Группы только подтверждённых источников,
  без использования прежнего ListingId карточки. 0 — «Группа поиска не указана»;
  1 — название; N — первая по SortOrder/Name/Id и раскрытие «Ещё N групп» с select.
  Переключение меняет только просмотр.
- Home: колонка «Тест спроса» между средней и выборкой, в основной таблице и
  «Все группы». Inline поле, сохранить/отмена, сообщения о конфликте/недоступности.
  Обновление после записи перечитывает экран; фоновых таймеров нет.

## Хранение и запись

`SearchGroupMarketSettings.DemandTestPricePerSotka`, nullable numeric(19,4),
общая существующая bigint Version. Денежная граница ввода RUB: 2 знака ToEven;
после округления требуется >0, проверяется вместимость numeric(19,4).
Проверяются организация, активность группы, право и ExpectedVersion. Блокировка
родительской группы в транзакции закрывает гонку первой записи без settings.
Изменение и append-only `DemandTestPriceChanged` сохраняются вместе: actor,
group, прежняя/новая цена, RUB, correlation ID. Источники цен, флаги участия и
PropertyCase не изменяются.

Forward migration `20260926140000_GroupDemandTestPrice` добавляет только nullable
столбец и русский COMMENT. Model snapshot согласован. Старые applied migrations
не менялись; существующие runtime grants достаточны, что проверено записью через
runtime fixture. Required reading data/comment conventions сверены до изменения схемы.

## Изменённые файлы M-03

- Application: `Modules/Collection/Domain/SearchGroupMarketSettings.cs`;
  `Modules/Overview/Public/GroupMarketContracts.cs`, `OverviewContracts.cs`;
  `Modules/Procurement/Public/ProcurementQueueV2ReadContracts.cs`.
- Infrastructure: `Modules/Overview/GroupMarketService.cs`, `OverviewService.cs`;
  `Modules/Collection/CollectionMappings.cs`;
  `Modules/Procurement/ProcurementQueueV2ReadService.cs`, `ProcurementServices.cs`;
  `Migrations/20260926140000_GroupDemandTestPrice.cs`, `LandErpDbContextModelSnapshot.cs`.
- Server: `Components/Primitives/GroupMarketPrices.razor(.css)`,
  `DemandTestPrice.razor(.css)`; `Components/Pages/IncomingCatalogV2.razor(.css)`,
  `ProcurementQueueV2.razor(.css)`, `Home.razor(.css)`;
  `Components/Procurement/CaseWorkspace.razor(.css)`.
- Tests: `tests/LandErp.Foundation.Tests/MarketDemandTests.cs`.
- Docs: `ACTIVE_TASK.md`, `GATE-M-03.md`, этот отчёт.

Остальные записи в git status относятся к сохранённым M-01/M-02; они не являются
новой работой M-03. В частности CollectorGateway и M-01 migration не менялись в M-03.

## Проверки и фактические результаты

1. `dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --filter 'FullyQualifiedName~MarketDemandTests|FullyQualifiedName~MarketParticipantsTests' --logger 'console;verbosity=normal'`
   - Первая сборка: CA1725 для имён cancellationToken; механически исправлено.
   - Повтор: Release-сборка прошла; 4/5 tests passed, 55.895 s.
   - Прошли права/org/null/валидация/версия/аудит и конкурентная первая запись M-03;
     обе проверки формулы и представителя M-02 прошли.
   - Один M-03 тест выявил EF translation ошибки constructor projection CaseGroup.
     Исправлен на member-init projection; запрос остаётся SQL, без client evaluation.
2. Повтор только
   `--filter 'FullyQualifiedName~MarketDemandTests.ContextsShareWholeGroupPricesAndCasesUseConfirmedVisibleSourceMembership'`:
   Release-сборка и **1/1 passed**, 31.324 s.
   Проверены Incoming selected preset / draft / ungrouped, одинаковые значения
   Overview/Incoming/queue/card, независимость от текста/ценового подфильтра;
   0/1/N групп карточки, неподтверждённые связи, повторные наблюдения, отсутствие
   дублей очереди, чужая организация и другая department visibility, скрытие
   ссылки участников без Incoming.Read.
3. Fixture применяла migrations только к одноразовым test DB; проверены
   `HasPendingModelChanges == false` и реальный COMMENT через pg_attribute.
4. Сгенерирован и просмотрен SQL только новой migration:
   `dotnet ef migrations script 20260926120000_CatalogCalculationParticipation 20260926140000_GroupDemandTestPrice --project src/LandErp.Infrastructure/LandErp.Infrastructure.csproj --startup-project src/LandErp.Infrastructure/LandErp.Infrastructure.csproj --configuration Release --no-build`.
   Первый запуск без design-time connection корректно отклонён. Повтор с локальной
   фиктивной конфигурацией `landerp_test_m03_script` сгенерировал ADD nullable
   numeric(19,4), COMMENT и запись migration history; подключение/применение не выполнялось.
5. После унификации денежного отображения — финальная Release-сборка Server
   `dotnet build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore`.
6. `git diff --check` — без замечаний.

Три сценария M-03 и два M-02 прошли в указанных запусках; полный suite не запускался.
Parser/Collector тесты не запускались (сборка зависимостей в test project не означает
их запуск). Данные подключения не выводились.

## Ограничения и следующий шаг

Server и браузер не запускались. Предоставленные четыре скриншота просмотрены,
но визуальная/ручная приёмка владельцем не заявляется выполненной. Следующий
рекомендуемый шаг — ручная проверка четырёх мест после отдельного разрешения
на подготовку локальной рабочей БД/запуск. На рабочую БД не применены ни M-01,
ни M-03 migration. Commit/push/merge/rebase/main и следующий Gate не выполнялись.
