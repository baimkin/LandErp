# План ускорения Incoming после независимого аудита

Дата: 23.11.2026. После аудита владелец отдельно разрешил пакет A:
[INCOMING-PACKAGE-A](GATE-INCOMING-PACKAGE-A.md). Пакет B не активирован.
Ниже сохранён исходный план; фактические изменения и проверки фиксируются в
[отчёте A](../../reports/INCOMING_PACKAGE_A_REPORT.md).
Основание: [Gate аудита](GATE-INCOMING-FILTER-PLAN-AUDIT.md),
[аудит](../../reports/INCOMING_FILTER_PLAN_AUDIT_REPORT.md),
[предыдущие измерения](../../reports/INCOMING_LOAD_TIMEOUT_REPORT.md).

## 1. Решение и граница результата

Выбран минимальный вариант: **сначала ускорить чтение без постоянного хранения
попадания в пользовательские фильтры**. Основной пакет A: отдельные страница и
exact Total, ограниченная metadata, пакетный resolver пресетов, SQL-наборы без
многократного раскрытия истории и независимые информационные числа/рынок.
В A нет schema migration, backfill, новых пакетов, очереди и новых обязанностей
Collector при записи. Это полный первый выпуск, если он проходит приёмку.

Условный пакет B — одна монотонная проекция listing↔search, если A не даёт
достаточного запаса из-за истории. Её schema, запись и backfill описаны ниже,
но она требует отдельного решения после измерений A. Она ускоряет установление
исторической принадлежности, а не хранит финальные результаты фильтров.

`catalog.incoming_filter_memberships`, `criteria_revision`, `calculated_revision`,
`calculated_at`, поколения фильтров и фоновые rebuild **не включены** в выбранную
реализацию. Причины: изменяемые данные и время всё равно требуют свежих predicates;
инвалидация всех путей усложняет бизнес-команды; корректный concurrent rebuild
добавляет протокол, которого нет в исходной гипотезе. Название этого файла
сохранено по поручению, а не означает одобрения полного precompute.

Production SQL и EXPLAIN отсутствуют. Существующие синтетические результаты
11.031 с service read / 8.890 с count / 1.009 с window без Total — baseline,
а не замеры исправления, SLA или доказательство единственной причины incident.
Таймаут отдельной EF-команды остаётся 10 с.

## 2. Неподвижные правила состава

Источником семантики остаются IncomingCatalogSelection/Query и адресные тесты.
Оптимизация меняет способ выполнения, но не смысл predicates.

| Режим/число | Обязательное поведение |
|---|---|
| SavedFilters без выбранного пресета | Уникальный union всех совместимых активных фильтров, затем текущие transient условия и slice; при нуле применимых — пусто |
| Выбранный пресет | Заменяет прошлые group/conditions; текст остаётся отдельным ограничением |
| UseDraft | Полная замена условий выбранного фильтра; расширенный draft не ограничивается его сохранённым набором |
| AllListings | Не ограничивается saved union; по умолчанию Disposition=Incoming сохраняется |
| Archive | Только архивные состояния; старые price/type/age/preset условия сбрасываются; текст, зона, сортировка сохраняются |
| Participants | Сначала точные участники рынка по зоне и подтверждённым группам источников; только text/order дополнительно сужают режим |
| Summary tiles | Каждый tile использует ContextFor(tile): заменяется slice, сохраняется предусмотренный кодом контекст; нельзя считать только текущую страницу |
| PresetCounts | Отбор соответствующего альтернативного пресета + text + active slice, без пересечения с ранее выбранным пресетом/зоной |
| GroupCounts/Ungrouped | DISTINCT ListingId внутри union альтернатив данной группы, а не сумма PresetCounts |
| AllPresetsCount | Отдельное выражение ReadService:75–77: saved union + text + visible group + disposition при отсутствии slice + slice |
| Total | COUNT уникальных IDs после **всех** условий effective отбора, до пагинации |

Одно listing может входить в несколько поисков/зон; union не превращается в
сумму или в выбор одного «главного» поиска. Search.Enabled и URL не являются
условиями удаления исторической принадлежности. Зона определяется текущим
SearchConfiguration.SearchGroupId и Active группы; перенос search переносит
исторические результаты. Метаданные V1 без search не превращаются в ограничение
зоны; неоднозначные V1 и архивные группы остаются fail-closed, без read-side writes.

Рынок сохраняет IncludeInCalculation, eligibility и выбор представителя object
group по последнему наблюдению в зоне, затем ListingId. Объявления не сливаются
физически. Права владельца зоны и состав команд не являются кешируемыми числами.

## 3. Полная классификация критериев

Здесь «статическое» означает только историческую связь, неизменяемую без удаления
истории. «По данным» означает изменяемое состояние, которое читается свежим.
В выбранном варианте **все бизнес-критерии проверяются при чтении**; никакая цена,
тип, состояние или событие не входит в постоянную membership фильтра.

| Поле criteria / transient | Класс и точная обработка |
|---|---|
| SchemaVersion | Версия формата; resolver V1→V2 в памяти с прежними проверками; не критерий listing |
| Source | По данным: Listing.Source; не search.Source; повторная проверка в SQL |
| SearchConfigurationId | Legacy resolver сохранённого фильтра; для прямого transient запроса — наличие принятого observation данного search, без требования Active группы |
| SearchGroupId | Историческая listing↔search связь + **текущие** search.group и group.Active/org; в B статична только первая часть |
| Disposition | По данным; сохраняется исключение при Preset=PossibleDuplicate и нормализация Archive/Participants |
| Age=Any | Нет возрастного ограничения |
| Age=Today/ThreeDays/Week | По времени: ReceivedAt >= capturedNow минус 1/3/7 суток; Today здесь не календарный день |
| Age=OlderThanWeek | ReceivedAt < capturedNow минус 7 суток; точная открытая граница |
| MinTotalPrice/MaxTotalPrice, Base.MinPrice/MaxPrice | По данным, включительные сравнения decimal; SQL null не совпадает с заданной границей |
| MinPricePerSotka / MaxPricePerSotka | По данным: Price != null и Area > 0, Price*100/Area; фильтр не округляется до отображаемого значения |
| MinAreaSquareMeters / MaxAreaSquareMeters | По данным, включительные границы, null semantics прежние |
| LandTypes | По данным: OR выбранных типов; источник DeclaredLandTypes **или** текущие Title/Description по IncomingLandTypeClassifier.ApplyFilter; не только подпись Classify в строке |
| AttentionOnly | По данным: AttentionRequired; false не означает AttentionRequired=false |
| IncludedInMedian | По данным: nullable условие на общий IncludeInCalculation; eligibility рынка остаётся отдельным правилом |
| Preset=New | События ReviewStarted/Classified/MonitoringStarted/CaseResumed и Confirmed case links; не ViewedDataRevision и не персональное unread |
| Preset=PriceChanged | Наличие SourceChanged со structured изменением цены либо прежним Message.Contains("цена"); не любое изменение источника |
| Preset=PossibleDuplicate | Pending duplicate candidate + Disposition Incoming/Duplicate; не совпадение текстового типа и не ObjectGroupId сам по себе |
| Preset=Incomplete | Price/Area/Location IS NULL; не общая оценка CompletenessPercent |
| Preset=ReturnedFromMonitoring | Наличие MonitoringTriggered; сохранённое событие, не только текущее состояние Monitoring |
| Preset=ProcessedToday | По времени и данным: календарный день Europe/Moscow, Classified/MonitoringStarted/CaseResumed либо Confirmed link по RecordedAt; простой просмотр не входит |
| Preset=null | Нет дополнительного saved preset predicate |
| AutoIncludeNewInCalculation | **Не read predicate.** Существующее явное бизнес-действие на сохранении opted-in фильтра и действительно новых listing; rebuild/backfill никогда его не повторяют |
| SortField/SortDirection | Порядок свежих результатов; не membership. ChangedAt/Price/Area/PricePerSotka, nulls last и ListingId tie-break сохраняются |
| Base.Text | Только transient: AND по SearchTerms; внутри терма OR Title/Location/ExternalId/CadastralNumber/SellerName ILIKE и актуальные comments через simple tsquery; Description не добавлять в text-поиск |
| Base.Offset/Size | Только окно после фильтрации; 25/50/100 в UI; не ключ информационного расчёта |
| WorkingScope.Mode/SelectedPresetId/UseDraft | Выбор алгоритма и полного effective predicate по таблице §2; draft не получает persisted membership |
| WorkingScope.Slice | ContextFor/Slice: New/PriceChanged/Incomplete/Returned добавляют Incoming; PossibleDuplicate — Incoming/Duplicate; ProcessedToday не ограничен Incoming |
| WorkingScope.ArchiveState | Только разрешённые архивные состояния или все States; не произвольный disposition |

Текущие модели не имеют отдельного универсального UpdateManualListing контракта:
есть CreateManual, комментарии/классификация/monitoring, case facts, связи и
collector source updates. Изменение рабочего факта PropertyCase не подменяет
Listing.Price/Area/Description. Новую команду ручного редактирования ради ускорения
не создавать. Её будущая реализация обязана следовать этой таблице.

Один capturedNow используется в Total, rows и page metadata одной операции.
Информационный расчёт имеет свой capturedNow/freshAt. На полуночи и на границах
1/3/7 суток состав меняется без записи: читающий запрос пересчитывает его всегда.
Периодический фоновой «ремонт» membership для времени не нужен.

## 4. Пакет A: чтение без постоянного precompute

### 4.1 Контракты и согласованный снимок страницы

В existing IncomingCatalogReadContracts ввести отдельные результаты основного
чтения страницы (Items, exact Total, Rows, безопасные справочники, effective offset,
ParticipantTotal при соответствующем режиме) и информационного чтения
(Summary, FilterCounts, freshAt, scope fingerprint). Итоговые имена выбрать при
реализации; это расширение существующего сервиса, не новый модуль/универсальный engine.
ReadWindow/ReadItems/ReadChangeScope сохраняют ограниченные контракты.

Public read каждый раз разрешает права. Внутренний resolver пресетов принимает
уже открытую connection/transaction; не должен открывать отдельное соединение
для criteria внутри снимка. Метаданные, Count(current), окно и metadata читать
последовательно в короткой READ ONLY REPEATABLE READ транзакции одной connection.
Она завершается до rendering/рынка/ожидания пользователя; без FOR UPDATE и без
advisory locks. Это выбранный способ получить согласованные criteria/rows/Total
без единого гигантского условного агрегата. Не переводить бизнес-записи в этот
read-only режим. Обоснование снимка: [PostgreSQL 18 isolation](https://www.postgresql.org/docs/18/transaction-iso.html).

Сначала точный COUNT текущего effective набора, затем вычисление last-page clamp
и одно окно на том же снимке. При Total=0 вернуть offset=0/пустые rows. UI не
вызывает второй полный ReadAsync из-за неверного offset. Между двумя отдельными
загрузками страницы данные могут закономерно измениться; exact означает точность
на снимке, а не замораживание каталога для пользователя навсегда.

### 4.2 Форма SQL: вычислить множества один раз

1. Подготовить нормализованный effective запрос: mode, selected/draft, text,
   criteria, slice, нужные group/search IDs. Для выбранного фильтра и All/Archive
   не строить predicates всех альтернатив на критическом пути.
2. До агрегации выделить DISTINCT listing/search либо listing/group для нужных
   групп из observations→jobs→searches→groups с проверками организации на всех
   owning joins. В A это запросные CTE, а не сохраняемые таблицы. Зональный
   фильтр получает сначала уникальные IDs своей активной зоны, затем остальные
   условия; unbounded работает по listings своей организации.
3. Для SavedFilters union получить уникальные пары (PresetId, ListingId), затем
   DISTINCT ListingId. Нормализованные фильтры передать типизированными параметрами
   (VALUES/массивы через Npgsql), со строго фиксированными SQL-выражениями enums.
   Не подставлять criteria как произвольный SQL. Отдельно ветви zonal/unbounded,
   чтобы не создавать безусловный cross product всей истории на каждый фильтр.
4. По listing один раз вычислить нужные event/confirmed-link/pending flags из
   scoped SQL-наборов. Не получать Message/history в приложение для булевых флагов.
5. EffectiveIds после dynamic/transient условий дают COUNT(current). Page query
   использует то же определение в той же транзакции, сортировку и Skip/Take.
   Допустимы два SQL с одинаковым небольшим CTE; недопустимо повторять union
   ещё в 12 условных метриках внутри Count(current).
6. Для общего informational read вычислить совпадения фильтра один раз, затем
   переиспользовать их в PresetCounts/GroupCounts/AllPresetsCount. Summary отдельно
   по своему контексту; slice override требует базу до текущего slice/disposition,
   иначе ProcessedToday/архивные состояния потеряются. В приложение возвращаются
   агрегаты, а не все пары Listing×Preset.

CTE сам по себе не гарантирует однократного исполнения: для повторно используемых
scoped history/flags/matches предусмотреть явный MATERIALIZED и проверить plan.
Не материализовать лишние descriptions/payloads/весь каталог без нужды. Сравнить
планы и buffers с inline-вариантом, если материализация создаёт temp spill;
это проверка конкретного SQL, не разрешение бесконечного поиска архитектур.
Основание: [PostgreSQL 18 CTE materialization](https://www.postgresql.org/docs/18/queries-with.html).

Форма SQL — часть реализации, её быстродействие ещё не доказано. Критический
SavedFilters union в холодном чтении всё ещё требует проверить применимые фильтры:
невозможно обещать нулевую зависимость от их числа без дополнительного состояния.
Убирается повтор одной работы во многих метриках; page/sort/size не пересчитывают
все **информационные** альтернативы. Если одного точного union недостаточно быстро,
измерения A определяют переход к B, а не маскируются отсутствующим Total.

Не заводить второй независимый набор бизнес-формул в SQL: семантический builder
IncomingCatalogQuery/Selection остаётся единым описанием нормализации/условий.
SQL-проекция должна проходить полное differential сравнение с существующим
predicate. Не переписывать read всех ERP-модулей и не менять рынок/команды на новый
query path одновременно с первым пакетом. Bulk сохраняет свежий прямой predicate;
его равенство новому page path — обязательная проверка, не обещание из сходства кода.

### 4.3 Пакетные метаданные

IncomingFilterPresetService: один SELECT пресетов, один batch SELECT нужных
searches/groups; resolution в памяти с прежними CompatibilityIssue. Не 1+P EXISTS,
без кеша прав и без записи legacy criteria. Разрешено повторное внутреннее
использование только в пределах одной операции/транзакции.

Page metadata: последнее observation/search выбирать PostgreSQL top-1 по listing,
event flags — EXISTS/агрегат; объём материала не растёт с history depth.
При равных ObservedAt старый код не задаёт tie-break: зафиксировать детерминированный
Observation.Id как вторичный ключ и отметить, что меняется лишь ранее неопределённая
подпись search, не состав. Для business representative рынка tie-break не менять.
Не читать Raw payload, личные данные или историю сообщений ради metadata.

### 4.4 UI и информационные числа

После основного результата список смонтирован и доступен; Summary/alternatives
и рынок имеют собственные loading/error/freshAt. Ошибка рынка либо counts не
очищает Items/Total и не переводит страницу в общий PageState error.
Один активный informational read на scope в пределах компонента; поздний ответ
проверяет operation revision, fingerprint и локальный mutation revision.
Read отменяется при смене scope/уходе/dispose; cancellation не повторяет команду.
Один DbContext не используется параллельно; при независимых reads — отдельные
контексты с ограниченной конкуренцией. После готовности страницы вторичные
чтения выполнять последовательно, без Task.WhenAll по фильтрам.

По page/sort/size сохранять последнее informational значение того же scope до
60 секунд, затем обновлять один раз после основного чтения. Это локальный снимок
компонента, без общего серверного кеша. Scope включает org + access identity/scope
(права проверяются свежими) + mode + normalized effective criteria + text + selected
id/draft + slice/archive state + fingerprint Id/Version/CompatibilityIssue пресетов
и group/search Id/Version/Active. Page/size/sort в ключ состава не входят;
при нормализации criteria явно исключить SortField/SortDirection, не включать
сериализованный criteria JSON целиком в ключ состава.
Version на Rename может вызвать дешёвый refresh metadata/чисел, но не rebuild.
После собственного acknowledged write помечать числа устаревшими и сохранять
готовый fast-update/Total; не запускать общее чтение на каждый просмотр/отказ.

Внешнее изменение данных может делать число старым до следующего чтения/TTL;
показывается время снимка, по явному «Обновить» выполняется свежий расчёт.
Время жизни снимка истекает раньше в полночь ProcessedToday; для Age число всё
равно является снимком, текущий Total всегда свежий. Клик по старой альтернативе
запускает свежие rows+Total и может дать другое число. Отсутствие значения —
«обновляется/не удалось обновить», никогда не синтетический 0.

Shared IMemoryCache/Redis и cross-process single-flight исключены из первого
пакета. Локальный снимок не создаёт межпользовательских гарантий. Если после A
доказан шторм одинаковых informational запросов, bounded cache 30–60 с может стать
отдельным маленьким этапом с org/scope/version key, size limit, отказом от кеширования
ошибок и одним вычислением на ключ. Он не является условием выпуска A.

## 5. Зависимости и точки изменений

| Фактический путь | Что меняет отбор | Обязательство A / условного B |
|---|---|---|
| CollectorGateway.AcceptResult:308–358 | Receipt replay и два вида duplicate skip | A ничего не добавляет; B не создаёт новую связь по текущему search для пропущенного observation |
| AcceptResult:360–403 | Новые/старые/неизменные принятые observations; source/monitoring/duplicate changes | A читает текущие данные; B upsert только пар действительно добавленных observations после SaveChanges:453, до automation:454, в той же транзакции |
| CatalogCalculationAutomation.IncludeNew | Автовключение новых IDs по opted-in фильтрам | Не расширять на reobservation; ни B-backfill, ни повтор чтения/перезапуск его не вызывают |
| ProcurementWorkspace.CreateManual:205 | Данные ручного listing, new AutoInclude | Нет observation/search; в B нет fake membership; unbounded фильтры продолжают работать |
| RegisterView:183 | ReviewStarted; New меняется даже без изменения Listing.Version | Свежий flags query; локальный fast refresh; постоянная filter invalidation не нужна |
| SetDisposition/ApplyCatalogDisposition:260, SetMonitoring:298, CatalogMonitoringEvaluator | Disposition/Attention/Include/events | Свежий read и прежние атомарные бизнес-записи; в B search projection не изменяется |
| ReviewDuplicateCandidate:330, Link/Unlink/LinkSameObject:384–544 | Pending candidates, disposition, object groups/median, confirmed links | Свежие flags и market participants; исторические observations не переносятся на другой listing |
| CorrectCaseLink:574, Take/Transfer:720–859, Resume:876 | Все затронутые sources, confirmed links, events/disposition | Fresh predicate/права; сохранить ReadChangeScope и локальное обновление всей source group |
| ListingCommentService.Save:66 / types:149,203 | Актуальный текст поиска, preview комментариев | Перечитать затронутую строку/отбор; не индексировать историю комментариев в membership |
| CatalogCalculationService.Set/Apply; CatalogArchiveService.Apply | IncludeInCalculation и archive/classification по набору | Проверка по текущим данным; не брать IDs/permissions из informational snapshot |
| CollectionAdministration.UpdateSearch:236 / ArchiveGroup:304 | Текущая зона/Active, URL/source/Enabled | B не копирует group; move/archive сразу учитывается JOIN при следующем снимке; backfill/rebuild фильтров не нужны |
| CollectorGateway.UpdateSearch:22 / UpdateGroup:53 | Те же group/Active, включая восстановление группы | Та же семантика, не забывать machine API; без отдельного filter handler |
| IncomingFilterPresetService.Create/Update/Rename/Delete | Новые условия, совместимость, version/active | Новое чтение использует committed criteria; Rename/sort не вычисляют наборы; IncludeExisting — отдельная существующая команда |
| GroupMarketService / Worker duplicate maintenance | Цена рынка/settings/eligibility и pending photo matching | Рынок/flags свежие; search projection не хранит это состояние |
| Время без записи | Age/ProcessedToday | CapturedNow в каждом основном чтении; никакой очереди инвалидизации |

В открытом коде «merge duplicates» означает объединение CatalogObjectGroup,
а не удаление/перезапись ListingId в observations. Если появится физический merge
или purge, это новый контракт: B потребует транзакционного обслуживания проекции.
Пока FKs RESTRICT запрещают молча удалить owning данные; deactivate поисков или
пресетов не удаляет историю. Операции вне этой карты перед изменениями сверить с
текущим кодом, но не расширять Gate на их переделку.

## 6. Конкуренция и почему нет rebuild фильтров

В A запись price/event/group/criteria и чтение конкурируют через обычный MVCC:
основное чтение видит один committed снимок до или после изменения; никаких
копий совпадений, которые могли бы потерять обновление. Две правки фильтра защищены
существующим ExpectedVersion/WHERE version; Rename сохраняет этот контракт.
Ошибку/отмену read можно повторить без повторения бизнес-команд. Readiness означает
успешный результат чтения, а не `calculated_revision=criteria_revision`.

| Гонка исходного precompute-проекта | Решение в выбранном варианте |
|---|---|
| Price update после scan, до publish | Membership фильтра нет; новый read проверяет current price |
| Новое observation во время rebuild | Rebuild нет; в B observation и search upsert коммитятся вместе |
| Два edit фильтра и поздний rebuild первой ревизии | Rebuild нет; optimistic version не даёт затереть второй edit |
| Move/archive/restore во время расчёта | В A/B group/search JOIN на снимке; не публикуется скопированная зона |
| Crash/error/restart | A ничего не публикует; B transaction rollback или монотонный повтор upsert/backfill |
| AutoInclude и повтор вычислений | Read/backfill без бизнес-side effects; прежняя команда остаётся на исходной границе |

Три предложенных столбца пресета **не достаточны**, если всё же выбрать persisted
filter membership: одинаковая criteria revision не отслеживает изменение listing
или group. Простая атомарная замена DELETE+INSERT не устраняет потерянные updates.
Самый простой корректный транзакционный вариант потребовал бы общего lock до
изменения всех зависимостей и полного rebuild под ним; unbounded filter способен
заблокировать ingress организации на длительное время. Это против главного приоритета.
Порядок preset→listing из IncludeExisting также конфликтует с попыткой добавлять
listing→preset locks в ingress; нынешние пути не имеют общего такого протокола.

Поколения сами по себе решают только частичную публикацию. Для online rebuild
понадобятся target/applied criteria + active/build generation, durable changed-ID
journal с корректным порядком commit/ack и replay, fencing старого builder, короткий
publish barrier и обработка move/archive/clock. Нельзя использовать max UUID или
sequence allocation как доказанный commit watermark. После edit old matches нельзя
выдавать под new criteria; нужно отдельное pending/error поведение либо явно
ограниченный fresh read. Это существенно больше двух таблиц/трёх столбцов.

Поэтому ни очередь, ни transactional full-filter rebuild не выбраны. Если A+B
не пройдут нагрузку, потребуется отдельный Gate проектирования такого протокола;
этот документ не объявляет его готовым. В A не нужны перечисленные дополнительные
handler/lock/generation сущности; в B блокировки описаны в §7.3.

Bulk остаётся на свежем predicate и ExpectedVersion/stamp, независимо от read
снимков/TTL. В текущем Apply после selection и row locks сверяется stamp выбранных
listing, но predicate целиком после locks не повторяется: изменение search/group,
ReviewStarted и время могут не менять Listing.Version. Строгая сериализация
bulk с этими изменениями не доказана. Это существующая граница, не следствие A.
В тестах будущего этапа воспроизвести гонки; при подтверждении требуемого более
строгого контракта зафиксировать отдельное ограниченное исправление, не считать
один stamp доказательством отсутствия всех races и не кешировать bulk IDs.

## 7. Условный B: точная проекция listing↔search

### 7.1 Proposed schema и conventions

A: **0 таблиц / 0 столбцов / 0 migrations / grants без изменений**.
B: одна таблица, два parent alternate keys, comments и grants. Не две таблицы и
не три столбца в incoming_filter_presets. Предложение DDL описывает контракт,
не является созданной migration и сейчас не выполняется.

| Объект | Точное предложение B |
|---|---|
| catalog.listing_search_memberships | organization_id uuid NOT NULL; listing_id uuid NOT NULL; search_id uuid NOT NULL; last_observed_at timestamptz NOT NULL, UTC из accepted observation, не clock now |
| PK | pk_listing_search_memberships (organization_id, listing_id, search_id) — без surrogate id/version, ключ естественной технической проекции |
| Обратный btree | ix_listing_search_memberships_organization_id_search_id_listing_id (organization_id, search_id, listing_id) INCLUDE(last_observed_at) |
| Parent alternate keys | ak_listings_organization_id_id UNIQUE(organization_id,id); ak_search_configurations_organization_id_id UNIQUE(organization_id,id); существующий PK(id) сохраняется |
| FK listing | fk_listing_search_memberships_organization_id_listing_id → catalog.listings(organization_id,id), ON DELETE RESTRICT |
| FK search | fk_listing_search_memberships_organization_id_search_id → collection.search_configurations(organization_id,id), ON DELETE RESTRICT |
| Organization | Граница обеспечена обоими composite FK; отдельный org FK из проекции избыточен, у listing уже есть org FK |
| Не хранить | group_id, filter_id, criteria, цены/тексты/фото/права, Active и Enabled searches |
| Runtime role | GRANT SELECT, INSERT, UPDATE ON catalog.listing_search_memberships; DELETE и DDL не давать; grant исполняет штатный migrator/helper с проверенным именем роли |
| Backfill role | Runtime DML достаточно; checkpoint операторский, secrets вне аргументов/репозитория; schema changes только migrator |

Таблица и каждая колонка получают русские comments в ModelConventions: это
восстанавливаемая техническая проекция **сохранённых** observations; max ObservedAt
пары; organization isolation; отличие от бизнес-истории. Composite PK допустим
по имеющимся conventions (ModelConventions:302–315), новая UUID-сущность не нужна.
Если таблица отображается EF, явно задать key/relations/precision и concurrency
не вводить искусственно; DbSet + mapping + snapshot согласовать. Пресеты сейчас
обслуживаются raw SQL и не присутствуют как EF entity — не создавать mapping
пресета только ради оптимизации.

Parent unique indexes могут потребовать отдельного онлайн-подготовительного
DDL при больших production таблицах: точное число migrations определить после
schema review и disposable проверки lock durations, включая выбранную EF/Npgsql
транзакционность CREATE INDEX CONCURRENTLY. Не обещать «одну безопасную migration»
по одному количеству таблиц. Никаких data rebuild в Up/Server/Worker startup,
переписывания applied migrations и автоматического Down production.

### 7.2 Минимальная запись и duplicate semantics

Собирать пары только из действительно добавленных db.ListingObservations данного
batch. После SaveChanges:453 все owning строки существуют; одним параметризованным
INSERT…SELECT/GROUP BY на пакет upsert (max ObservedAt), перед IncludeNew и commit.
Никаких SELECT/SaveChanges на listing×filter. Для conflict key обновлять время
только если excluded.last_observed_at > saved.last_observed_at; старый out-of-order
пакет может добавить новую пару, но не уменьшить время и не перезаписать price.
Основание upsert/WHERE/привилегий: [PostgreSQL 18 INSERT](https://www.postgresql.org/docs/18/sql-insert.html).

| Ветка | Поведение B |
|---|---|
| ResultId receipt replay | Вернуть прежнюю receipt; новых пар/наблюдений/AutoInclude нет; восстановление старых пропусков — backfill, не бизнес-replay |
| AgentId+ObservationKey duplicate | Использовать ранее сохранённую историю; не связывать listing с текущим job.SearchId |
| ListingId+ObservedAt+ContentHash duplicate | Даже если текущий job другого search — не создавать новую пару: такого observation в истории нет |
| Принятое older observation | Пара появляется, max времени пары монотонен; текущие listing values изменяются только по существующему isLatest |
| Принятое неизменное повторное observation | Число пар не растёт для того же search; только max timestamp; ни одного filter rebuild |
| Accepted одинакового listing в другом search | Новая пара один раз, если observation реально принят; group выводится текущая |
| Ошибка до commit | Observation, projection и receipt откатываются вместе; никакого acknowledged partial success |

Писать связь на duplicate skip ради «более правильной принадлежности» — отдельное
изменение бизнес-правила, которое этот план запрещает. Не исправлять это скрыто.
Не использовать Listing.LastObservedAt в качестве времени всех его search pairs.

### 7.3 Lock order и ограничение конкуренции

Существующий AcceptResult берёт agent, job, сортированные natural-identity advisory
locks и затем сохраняет listing/observations. B не берёт preset/group/org lock и
не меняет порядок этих существующих блокировок. Новые projection keys после
owning insert/updating обрабатываются в одном порядке org/listing/search.
Backfill берёт только projection keys в том же порядке; не берёт FOR UPDATE
на listing/search и после upsert не вызывает бизнес-сервисы. Это исключает новую
обратную зависимость projection→listing/preset/job. Не добавлять FK KEY SHARE
parent-зависимости через новые изменяемые parent keys: org/id не изменяются.

B не гарантирует отсутствия уже существующих deadlock во всех business paths.
Локальный lock_timeout для backfill начать с 1 с, statement_timeout не выше 10 с;
при ожидании/ошибке откатить только пакет, уменьшить размер и продолжить с прежнего
checkpoint. Live ingress не ждёт полного backfill/rebuild; при ошибке его атомарной
записи receipt не подтверждается, штатная повторная доставка остаётся прежней.
Retry обёртку вокруг бизнес-команд не добавлять. Если тесты обнаружат общий цикл
locks со старым кодом, B не выпускать до устранения конкретного цикла.

Начальные размеры: не более 250 listing IDs на backfill chunk, один backfill
writer. Это параметры для измерения, а не гарантии длительности: listing с большой
историей может требовать меньшего chunk. Write cost B зависит от числа принятых
пар/изменений времени, не от числа пресетов; O(accepted pairs), без P-фактора.
Даже no-op conflict может ждать lock: нагрузочные замеры обязательны.

### 7.4 Backfill, readiness, online ingress

1. Аддитивная schema применена владельцем отдельно; read path пока A. Сначала
   развернуть dual-write B на **всех** ingress writers, завершить/дождаться старых
   транзакций. Зафиксировать deployment/schema fingerprint. Старый writer после
   этого не должен вернуться незамеченным. Без этого backfill-ready не доказывается.
2. Один maintenance CLI по org, без запуска приложения. Checkpoint — отдельный
   атомарно заменяемый файл под C:\ProgramData\LandErp\state\incoming-memberships\,
   с DB identity без пароля, org, schema/projection version, deployment fingerprint,
   last_listing_id, завершённостью и validation summary. Это операторское состояние,
   не DB-таблица готовности. Log — только безопасные counts/timing в существующей
   C:\ProgramData\LandErp\logs; scripts на сервере C:\LandErp\scripts.
3. Cursor — (organization_id, listing_id), keyset по listings ORDER BY id > cursor,
   без OFFSET, без ObservedAt/RecordedAt/UUID как commit watermark. Для chunk
   агрегировать все **сохранённые** observations этих IDs через jobs/searches,
   проверяя одинаковый org владельцев, max observed_at по паре. Не требовать
   active/enabled/group: это последующие динамические условия чтения.
4. Chunk upsert и commit; только после подтверждения commit продвинуть checkpoint.
   Crash между commit/checkpoint повторяет безопасный chunk. Перемещение cursor
   перед commit запрещено. Нестыковки org/orphan зафиксировать и остановить readiness,
   не «чинить» production данные. Ни одна business command не вызывается.
5. Новая запись за cursor/до cursor или поздний commit не теряется: после полного
   dual-write каждый новый accepted observation создаёт свою пару атомарно. Уже
   открытые старые writer transactions были drained на шаге 1. Сортировка UUID сама
   по себе этой гарантии не даёт. Повторный запуск с иным DB/deployment fingerprint
   отказывается продолжать чужой checkpoint и требует новую проверку writers.
6. При завершении traversal — chunked equality validation по каждой организации:
   old distinct(org,listing,search) EXCEPT new и обратно, плюс max ObservedAt;
   оба направления и timestamp discrepancies = 0 на одном снимке соответствующего
   chunk. Сохранить итоговый cursor/zero mismatches, сравнить также group predicates
   после move/archive и representative рынка. Не ограничиваться общим COUNT(*)
   или checksum, маскирующим разные ID. Validation не держит многочасовую транзакцию.
7. Read feature switch B — явный allowlist организаций в настройках развёртывания,
   default Off. Включать оператором после equality и проверки всех writers.
   Read не читает checkpoint-файл на каждый запрос; нет автоматического «готов»
   по наличию хоть одной строки. Пустая организация может быть корректно готовой.
   На разных экземплярах A/B дают одинаковый состав; разные latency допустимы.
8. До готовности пользователи работают на **оптимизированном A**, а не получают
   пустую проекцию. Можно включать организации по мере готовности, без общего
   ожидания. Внутри одной организации не смешивать projection и историю по строкам:
   иначе сложность readiness протекает в каждый запрос.

Состояния оператора: NotStarted → Writing/Backfilling → Validating → Ready →
Enabled. Error сохраняет последнюю committed позицию, не включает reads. Это не
новая серверная очередь jobs. Повреждённый/утраченный checkpoint допускает полный
идемпотентный проход; readiness заново доказывается. Generation для монотонной
проекции не нужен: всё время строится одно и то же множество фактов.

B читает JOIN memberships→searches→active groups. Market/bulk/permission проверки
в первом B-выпуске остаются на authoritative истории и свежих данных; их перенос
не обязателен для ускорения обычного Incoming. Даже после equality не заменять
механически permissions JOIN условием Active: сейчас некоторые median provenance
проверки не требуют group.Active. Это отдельный контракт.

### 7.5 Откат

A — pinned предыдущий reviewed build/config в отдельном действии владельца;
schema нет. Старый read остаётся известным медленным путём, не silent fallback
при каждой ошибке нового. Ошибка основного read показывается с возможностью
повторить чтение; ошибка secondary read локальна. Не переключаться автоматически
на тяжёлый SQL, который удвоит нагрузку при incident.

B — выключить allowlist, остаться на A; аддитивную таблицу/историю не удалять.
Dual-write по возможности продолжить. Если откат приложения отключил dual-write,
Ready недействителен: перед повторным включением полный backfill+equality после
восстановления всех writers. Не пытаться «догнать только по last ObservedAt».
Отмена backfill оставляет безопасный неполный слой, который не используется read.
Не запускать Down migrations, purge, переписывание receipt/history или automatic
production schema repair. Все production операции — отдельное разрешение владельца.

## 8. Этапы будущей реализации и файлы

Это последовательность предложения. Следующий этап не начинается автоматически.
Рабочая ветка/dirty checkout сохраняются; перед каждым этапом сверить scope и
перечень файлов, не включать параллельные Parser/media/logging изменения.

| Этап | Proposed файлы | Изменение и критерий завершения |
|---|---|---|
| A1. Основной read | src/LandErp.Application/Modules/Catalog/Public/IncomingCatalogReadContracts.cs; src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs, IncomingCatalogQuery.cs, IncomingCatalogSelection.cs, IncomingFilterPresetService.cs; адресные tests ниже | Разделены main/info контракты; batch metadata; short shared snapshot; COUNT только current + clamp + window; SQL scoped sets. Differential совпадение всех IDs/Total; plan не содержит повторной истории на каждую summary metric; ни одной новой обязанности ingress/schema |
| A2. Информационные числа и UI | те же read contracts/services; src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor, .Dismiss.cs, .Updates.cs; WorkspaceComponent.cs только если локальной границы недостаточно; существующие CSS только для индикаторов | Страница видна до secondary reads, ошибки изолированы, поздний ответ не затирает новое scope/mutation, page/sort/size не запускают повтор всех counts до TTL. Сохранены fast updates/scroll/25–100/выбор/unknown handling |
| A3. Приёмка и решение остановиться | tests/LandErp.Foundation.Tests/IncomingLoadTimeoutTests.cs, IncomingWorkingSelectionTests.cs, IncomingFilterPresetTests.cs, IncomingArchiveSortingTests.cs, IncomingMonitoringTests.cs, CatalogBulkArchiveUiTests.cs, IncomingQuickDismissTests.cs, IncomingFastOpenTransferTests.cs и browser counterparts; локальные ignored measurement artifacts; отчёт отдельного разрешённого Gate | Одинаковые ID/count/права, обязательные тесты Passed, no N+1, cold/warm/concurrent measurements. При выполнении целей выпуск ограничен A; B не нужен |
| B1. Только при доказанной history bottleneck | src/LandErp.Application/Modules/Catalog/Domain/CatalogModels.cs; src/LandErp.Infrastructure/Modules/Collection/CollectionMappings.cs, CollectorGateway.cs; src/LandErp.Infrastructure/Persistence/LandErpDbContext.cs, ModelConventions.cs, ProductionDatabaseInitializer.cs; новая additive migration(и) и snapshot в src/LandErp.Infrastructure/Migrations/; proposed src/LandErp.Infrastructure/Modules/Catalog/IncomingSearchMembershipProjection.cs | Точная схема §7, tenant FK и grants, batch atomic upsert только accepted observations; нет filter table/revisions; ingress/replay/out-of-order/concurrency tests Passed |
| B2. Maintenance и shadow | proposed src/LandErp.Maintenance/ (только CLI, существующие зависимости), solution registration; scripts/Backfill-IncomingSearchMemberships.ps1; dedicated projection/backfill tests; no startup hook | Resumable checked cursor и dry validation режим; rollback/error/restart/dual-writer tests; zero two-way equality. Отдельное решение о новом CLI-host вместо разрастания LocalSetup оформить в Gate B, без новых пакетов |
| B3. Read allowlist и приёмка | IncomingCatalogQuery/Selection/ReadService; existing configuration registration; projection tests/measurements; отдельный отчёт | Projection заменяет только historical membership часть Incoming, остальные predicates свежие. Read Off до готовности, rollback A, gate performance+ingress, all equality Passed |

Пути таблицы относительно корня C:\.Projects\LandErp. Имена новых B-файлов —
proposed, они сейчас не созданы. Точный список тестов зон/медианы взять адресно
по зависимостям конкретного этапа; не запускать полный suite ради этого аудита.
Если A требует индекса, это отдельное явно согласованное дополнение этапа,
а не незаметная migration: сначала EXPLAIN/BUFFERS на disposable.

Кандидаты для такой проверки: observations(job_id,listing_id) INCLUDE(observed_at),
events(organization_id,catalog_item_id,kind,recorded_at) либо более узкий индекс
по фактическому плану, listings(organization_id,disposition,changed_at,id).
Это **не утверждённые indexes**: уже существуют observations(job_id), unique
(listing_id,observed_at,content_hash), listings(org,changed_at), events(listing,time).
Не добавлять все кандидаты, GIN/trigram extensions или вычисляемые колонки заранее.

## 9. Проверки будущей реализации

### 9.1 Эквивалентность и пользовательские действия

- Differential harness: прежний predicate против нового пути, одинаковый snapshot
  и capturedNow; сравниваются ID множества в обе стороны, exact Total, порядок
  каждого окна, все Summary/alternative counts, не только число первой страницы.
- Overlaps фильтров и зон, один listing в нескольких searches одной/разных зон,
  повторная глубокая история, зональный и unbounded, manual listing без observations,
  пустая/foreign org, empty/all-incompatible presets, V1 ambiguous/metadata-only,
  active/archived/restore/move/Enabled=false/URL/source edit.
- Saved union/selected/draft с расширением и сужением; selected заменяет прошлую
  зону; text/comments AND terms, seller name matching; All/Archive/Participants;
  каждый enum criteria и null/0/границы decimal/area, source-declared + text LandTypes,
  current IncludeInCalculation и неизменная median eligibility.
- Frozen clock: Age ровно 1/3/7 суток ± тик; календарная полночь Москвы;
  ProcessedToday links/events, простой view не processed; при отсутствии writes
  следующий read даёт правильный новый состав. Проверить разницу saved Preset и Slice.
- CreateManual, source changes/неизменное/older observation, monitoring return,
  view/classify/duplicate accept-reject/photo refresh, transfer/take/correct link,
  object-group merge/unlink, median и bulk archive; по изменению рабочей цены
  PropertyCase Listing criteria не меняются сами.
- 25/50/100, null-last/tie sort/PricePerSotka до pagination, empty/last-page clamp,
  выбранные IDs/все с exclusions, fresh stamp/права/foreign org и zone ownership.
  Ошибки counts/market не скрывают rows; отказ/unknown write не удаляют строку;
  после committed write + read failure повторяется только read, не команда.
- Late/cancelled main/info read, смена scope/dispose, mutation во время secondary
  read, scroll/DOM continuity fast-dismiss/fast-open/transfer и скрытые sources.
  Отдельно проверить известные bulk races §6; не выдавать тест version-change за
  покрытие group/time/view races.

### 9.2 Конкуренция и B

- Две правки/rename/delete фильтра; rename/sort без rebuild; crash/retry read;
  snapshot до/после price/criteria/group commit, отсутствие частичного результата.
- B: accepted/replay/keyed duplicate/content-time duplicate в другом search,
  older/newer/out-of-order, один ключ несколько раз в batch; max timestamp;
  conflict на tenant FK; rollback observation+projection+receipt при любой ошибке.
- B: concurrent ingress двух agents/searches и backfill одного ключа; insert
  listing перед/за cursor, отложенный commit; crash до/после chunk commit/checkpoint,
  повтор полного backfill, потеря/несовпадение checkpoint, переход на старый writer.
- B: read Off при NotStarted/Partial/Error, true empty org ready, per-org enable;
  move/archive/restore во время backfill и после readiness, rollback/re-enable;
  обе EXCEPT и max ObservedAt equality, никакого AutoInclude/audit бизнес-команд
  при техническом заполнении. Измерить lock waits и проверить общий lock order.

### 9.3 Производительность и цели

Повторить baseline 25k listings / 500k observations / 50 filters с настоящим
preset service; отдельный диагностический прогон FixedPresets допустим только как
изоляция стоимости. Добавить overlaps, multi-zone, unbounded/dynamic/text и запас
роста 100k/2m/100 filters, несколько параллельных читателей с ingress. Сначала
зафиксировать характеристики машины/PG/options и actual cardinalities.

Измерять cold/warm раздельно, несколько повторов (не один best run), p50/p95/max;
под холодным в disposable явно указать метод, не очищать production buffers.
EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) — только synthetic disposable read queries;
server tag timings для main Count/Rows/Metadata/Info/Market отдельно. Не собирать
production SQL с текстами/параметрами или raw logs.

Считать все DB round trips, строки прочитанные PostgreSQL и materialized клиентом,
SQL/plan size, SubPlan/loops, shared/temp buffers, lock wait, CPU/IO; EF preparation
и authorization/preset resolver тоже входят в full main latency. Число сетевых
команд и узлов плана — разные метрики. Не разрешать P queries на metadata/counts,
N+1 по listing или materialization всех observations/Listing×Preset в приложение.

Начальные **проверяемые цели, не гарантии** на baseline: p95 main rows+exact Total
≤3 с, каждая команда ≤5 с (запас до 10), ни одного DB_TIMEOUT; page navigation
не запускает весь informational read при свежем снимке; bounded metadata не
зависит по числу returned rows от глубины observations. При нагрузке ≥4 readers +
ingress отсутствие timeout и длительных locks; допустимый p95 main согласуется
по измеренному пределу машины, а не объявляется выполненным по одному запросу.

Для B отдельно: дополнительный ingress p95 не более 10% baseline того же batch
как начальный бюджет; rows проекции после N повторов равны distinct пары, без
роста на run; не более одного дополнительного set-based upsert на ingress batch.
Backfill chunk целевой p95 <1 с и ограниченное lock wait; при неуспехе уменьшить
chunk/приостановить backfill, не замедлять live прием. Проверить сохранение
throughput ingress во время 4 concurrent readers и backfill.

Полный filter rebuild zonal/unbounded в A/B отсутствует. Вместо фиктивного замера
«rebuild=0 мс» измерять первое чтение после create/edit такого фильтра, холодный
union/info и существующий IncludeExisting отдельно при AutoInclude=true.
Его бизнес-стоимость не приписывать projection; полная filter membership не
выбрана. Если к ней вернутся, zonal/unbounded rebuild/дельты/publish/ingress
измеряются отдельно в новом Gate до утверждения реализации.

## 10. Условия завершения и решения владельца

A готов к отдельному разрешению реализации: конкретные контракты/состав, файлы,
SQL-подход, проверки и критерии остановки заданы. Положительный итог A3 отменяет
необходимость B; следующие этапы не активируются ради «полноты плана».
B спроектирован как условное расширение, но стартует лишь по доказанному
history bottleneck и отдельному согласованию schema/maintenance/rollout.
Полный per-filter precompute отклонён для первого решения; прежний черновик
нельзя реализовать как уже проверенную схему.

Блокирующие неизвестные перед production: результаты A на disposable/concurrent
нагрузке, актуальный deployed SHA/schema/наличие всех writers, безопасные реальные
stage timings и требования к строгой bulk concurrency из §6. Числовые цели выше
пока инженерные ориентиры. Это не повод запрашивать повторное разрешение на
текущий аудит; production проверка/выкатка и реализация не входят в него.

На момент завершения аудита были выполнены только чтение источников, проверка
существующих artifacts и подготовка документов. Последующая реализация A
разрешена отдельным запросом владельца; её результат — в отдельном отчёте выше.
