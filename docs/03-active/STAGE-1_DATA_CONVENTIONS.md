# Stage 1 — review data conventions перед migration

Дата: 2026-09-14. Foundation review принят в рамках разрешения Stage 1.

- База: текущие ERP docs `def070e` + Collector `3994644`, safe merge `3e30755`.
- Stable SDK 10.0.112 (тот же feature band Collector, runtime 10.0.12);
  EF Core/design-time 10.0.12, Npgsql/provider 10.0.3. Без preview/RC.
  EF/Microsoft packages MIT, Npgsql PostgreSQL license; официальные поддерживаемые
  проекты. Только persistence/design-time; advisory audit обязателен.
- Один LandErpDbContext и migrations в Infrastructure. `foundation` владеет
  migration history; будущие `identity`, `organization`, `collection`, `catalog`,
  `workflow` владеют своим current state. snake_case, English names.
- A initial DDL — только `foundation` schema и EF `migration_history` с русскими
  TABLE/COLUMN comments. Без dummy entity и business tables. SQL review до apply.
- Business IDs application-side Guid.CreateVersion7(), PostgreSQL uuid.
  ExternalId и BusinessNumber отдельны; Source+ExternalId не идентичность земли.
- Instants DateTimeOffset offset 0 / timestamptz. Business timezone явно
  Europe/Moscow; Windows mapping проверяется платформой. RecordedAt/ObservedAt
  различаются; неизвестный EffectiveAt не выдумывается. DateOnly для business date.
- Money decimal + ISO currency, storage numeric(19,4). Валютная precision отдельно;
  RUB 2 знака, MidpointRounding.ToEven на явно обозначенной денежной границе.
  Площадь numeric(19,4), единица m²; float/double не для денег.
- Mutable aggregates: application-managed bigint Version, concurrency predicate,
  увеличение при изменении. Approval фиксирует version рассматриваемых данных.
- Current state relational; history/audit/approval facts append-only.
  Runtime не UPDATE/DELETE history; retention отдельным privileged процессом.
  Full Event Sourcing не применяется.
- Raw/Parsed/Presence раздельны. Missing не очищает known. Latest state обновляется
  более новым observation; idempotency delivery и observation раздельны.
- Runtime без ownership/CREATE/DDL; migrator connection отдельно из env.
  Startup schema не меняет. Applied migrations immutable; исправления новой.
- Test cluster: отдельный каталог и loopback порт из локальных PG18 binaries;
  disposable test databases автоматически именованы. Существующая БД не меняется.
- Clean/repeat apply, metadata, rollback/reapply только Test, backup/restore и outage
  checks обязательны. Production apply требует отдельного разрешения.

Business tables B–D проходят дополнительный review у первого use case;
foundation convention не разрешает будущие модули.
