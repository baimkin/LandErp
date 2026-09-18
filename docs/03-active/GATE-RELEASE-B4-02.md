# GATE-RELEASE-B4-02 — Minimal operational health

**Package:** Release Package 04 — Parser Reliability & Minimum Production Contour  
**Finding:** минимально необходимая часть LR-18  
**Branch:** `codex/release-package-04`  
**Base:** `6543e91569b053e611899aa7d8b49935ed3f6fe5`

## Бизнес-цель

Перед первым запуском оператор должен без разработчика понять:
- Server отвечает;
- PostgreSQL доступна;
- Worker выполняет scheduler cycle;
- Scheduler успешен или сломан;
- сколько Parser online / stale / offline;
- есть ли backlog;
- есть ли expired leases;
- доступно ли файловое хранилище.

Не строить Prometheus/Grafana/tracing/PagerDuty.

## Реализация

### Один существующий экран

Новая ops/admin страница не создаётся.

`/collectors` уже:
- доступен пользователю с `collection.read`;
- содержит parser/scheduler контекст;
- автоматически обновляется каждые 15 секунд.

Сверху добавлен компактный `Состояние системы`.

### Server / Database

- Server = live, если интерактивная страница выполняется;
- DB проверяется существующим `IDatabaseStatus.IsReadyAsync`;
- существующие внешние endpoints `/health/live` и `/health/ready` не меняются.

При недоступной DB health-контур остаётся виден и показывает проблему вместо попытки отрисовать устаревшие business data.

### Worker / Scheduler

Новая heartbeat-table не нужна.

`CollectionSchedulerStatus.LastStartedAt` обновляется каждым scheduler cycle Worker. Поэтому:
- Worker `Работает`: последний cycle не старше 2 минут;
- `Не запускался`: cycle ещё не было;
- `Нет связи`: LastStartedAt старше 2 минут.

Scheduler сохраняет существующий state `Работает / Ошибка / Нет связи / Не запускался`.

### Parser

Существующий online threshold остаётся 3 минуты.

UI дополнительно показывает:
- online;
- stale — enabled Parser без heartbeat 3–10 минут;
- offline — старше 10 минут или без heartbeat.

Это только operational presentation, protocol/lease semantics не меняются.

### Backlog / expired lease

`CollectionAdminView` теперь содержит точный DB count `ExpiredLeases`.

Health block показывает:
- Pending;
- expired/reclaimable;
- общий backlog = Pending + expired leases.

Expired lease также входит в attention summary.

### Storage

Добавлен отдельный `IFileStorageHealth`, чтобы не расширять `IFileStorage` и не ломать test/fake providers.

- Local: read-only проверка доступности volume;
- YandexDisk: один read-only metadata GET по configured root;
- 404 root до первого upload считается доступным provider/auth состоянием;
- никакой probe-file не создаётся;
- provider exceptions наружу не выходят — UI получает safe code.

Storage probe на `/collectors` кэшируется на 1 минуту, чтобы 15-секундный UI refresh не создавал лишний внешний трафик.

## Tests as code

`ReleasePackageB402Tests`:
1. expired lease виден отдельно от Pending и перестаёт считаться Busy;
2. Yandex health probe read-only, без upload;
3. auth failure превращается в safe `STORAGE_AUTH`.

## Вне scope

- historical health metrics;
- alerts/notifications;
- Prometheus/Grafana;
- distributed tracing;
- HA;
- deployment/restart/HTTPS;
- backup/restore;
- B4-04 UAT.

## Проверки

По решению владельца:
- restore/build/tests/browser: **Not run**.
