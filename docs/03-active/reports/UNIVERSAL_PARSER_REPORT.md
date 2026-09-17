# Universal Parser — отчёт реализации

**Дата:** 17 сентября 2026 года
**Ветка:** `codex/universal-parser`
**Статус:** основной Local/Server сценарий реализован; production migration не применялась.

## Что реализовано

- В Parser появился сохраняемый переключатель `Локально / Через Server` с общей блокировкой параллельного запуска.
- Локальный workspace получил независимые группы и расписания `Manual / Interval / FixedTimes`; старые ссылки автоматически получили ручное расписание при миграции SQLite v3.
- Текущий URL можно взять из открытого браузера после настройки фильтров или области и сохранить как локальную ссылку.
- Server выдаёт единый секретный код подключения. Parser проверяет его, регистрируется и хранит token через Windows DPAPI CurrentUser.
- Parser читает server groups/searches, создаёт server group и по явной кнопке добавляет одну выбранную локальную ссылку с выбранной server group и расписанием.
- Перед добавлением показывается подтверждение: локальная группа, расписание, история, задания и результаты не передаются.
- Server work links хранятся в скрытом служебном разделе SQLite и не появляются в Local workspace.
- Server loop автоматически восстанавливает сохранённое подключение, получает задания с bounded backoff/jitter, отправляет heartbeat/progress и доставляет результаты через durable outbox.
- Outbox привязан к Server origin + AgentId. Смена подключения при незавершённых данных блокируется; permanent failure одного Job не блокирует другие Jobs; superseded lease отмечается отдельно.
- Server получил ограниченный machine API groups/searches, отдельное право `CanManageSearches`, аудит действий Parser и возможность включить это право у уже созданного Parser.
- Повторный claim действующего lease возвращает ту же работу. Heartbeat поддерживает явный переход защиты источника в `Ready`.
- Добавлена forward migration `UniversalParserSearchManagement` для `collection.agents.can_manage_searches` с русским schema comment.

## Основные границы

- Local данные не синхронизируются автоматически и не удаляются после явного добавления ссылки на Server.
- Server не содержит browser/Playwright, Parser не подключается к PostgreSQL.
- Код подключения и machine token не пишутся в Git или обычные логи.
- Production apply, live Avito/Cian и merge не выполнялись.

## Проверки

- `dotnet build LandErp.slnx --configuration Release` через изолированный artifacts path: успешно, 0 warnings, 0 errors.
- Parser targeted offline suite: 29/29 — protocol compatibility, connection code, Local groups/schedules, hidden server links, QueueRunner, outbox partition/error isolation.
- PostgreSQL `CollectionPoolTests`: 3/3 — migrations/model snapshot, permission boundary, idempotent create, audit, one active lease, CAPTCHA → Ready и concurrency.
- `git diff --check`: ошибок whitespace нет; присутствуют только штатные предупреждения Git о будущем CRLF.

Полный старый Parser suite не использован как критерий: один тест открывает видимый Chrome и падает/зависает в текущей неинтерактивной среде. Отдельный end-to-end Collector host test также не стартовал из изолированного artifacts path; его database/gateway сценарии покрыты прошедшими PostgreSQL tests. Production схема не изменялась.

## Ограничение первой версии

Parser создаёт server groups/searches и задаёт расписание при добавлении. Редактирование уже существующего server search из Parser UI оставлено зарезервированным DTO и не включено в первый сценарий. Сохранение нарисованной области опирается на текущий URL источника; отдельная переносимая геометрия карты в server contract не добавлялась.

## Следующий рекомендуемый шаг

Провести ручную приёмку на тестовом Server: создать/разрешить Parser, вставить код, переключить режим, открыть карту, настроить URL, добавить одну ссылку в server group и убедиться, что scheduler выдаёт её после перезапуска приложения. После приёмки отдельно разрешить применение migration к выбранной непроизводственной базе.

## Интеграция с параллельной Procurement V2

После завершения реализации Parser его изменения зафиксированы отдельным коммитом
`9328365`. Верхняя проверенная ветка Procurement V2
`codex/procurement-v2-negotiations-inspection` @ `41c6fea` объединена с Parser без
текстовых конфликтов. Интеграционный commit — `a0d9025` в
`codex/universal-parser`. Отдельные устаревшие queue, diagnostic и `tmp-*` ветки
повторно не объединялись: их необходимые изменения уже входят в актуальную
линейную цепочку либо были заменены последующей реализацией.

Проверки объединённого состояния:

- locked restore — успешно после разрешения доступа к NuGet;
- Release build всего `LandErp.slnx` — успешно, 0 warnings / 0 errors;
- целевой offline Parser suite — 29/29;
- PostgreSQL `CollectionPoolTests` — 3/3;
- PostgreSQL `OverviewTests` — 2/2;
- PostgreSQL `ProcurementQueueV2ReadTests` — 5/5;
- пять отдельно запущенных `ProcurementTests` для manual/marketplace flow,
  direct creation, concurrent take-to-work, scope и clean migration — 5/5;
- `git diff --check` — успешно.

Общий Parser suite не является green evidence: реальный visible-Chrome test падает
при закрытии browser process, а Desktop UI tests удерживают testhost в текущей
неинтерактивной среде. Старый restart scenario
`IncomingUiCreatesCaseUsesCanonicalRouteAndSurvivesRestart` также не вернул итог
за две минуты и был остановлен; интеграция меняет в его fixture только новый
аргумент права Parser и не меняет restart/runtime path. Эти проверки не заменены
предположением и остаются в ручном/интерактивном checklist.
