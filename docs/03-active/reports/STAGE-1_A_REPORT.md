# Stage 1 — Checkpoint A

Дата: 2026-09-14. Ветка `codex/stage-1-procurement-core`.
База docs `def070e` + Collector `3994644`, merge `3e30755`.

## Реализовано

Application/Infrastructure/Server/Worker, один основной DbContext, design-time
factory, schema-only migration с русскими PostgreSQL comments, независимые hosts,
live/ready, bounded correlation ID, Problem Details, конфигурация без secrets.
CPM/locks, stable SDK 10.0.112, EF 10.0.12, Npgsql/provider 10.0.3.
Runtime не выполняет migrations; отдельный local-only migration path.
Review conventions до migration: `STAGE-1_DATA_CONVENTIONS.md`.

Файлы: solution/global/CPM, новые src/LandErp.Application/Infrastructure/Server/Worker,
tests/LandErp.Foundation.Tests, scripts/Test-Foundation.ps1 и Invoke-Migrations.ps1,
dotnet-tools.json, новые package locks, README/START_HERE/ACTIVE_TASK/checkpoints/conventions.
Код, locks и настройки проектов Collector сохранены без изменения.

## Фактические команды и результаты

Команды выполнялись SDK `artifacts/stage1/dotnet/dotnet.exe` (официальный архив
Microsoft, SHA512 проверен по release metadata). Установленная PostgreSQL 18.6.

- `dotnet restore LandErp.slnx --locked-mode`: Passed, audit включён.
- `dotnet build LandErp.slnx -c Release --no-restore`: Passed, 0 warnings/errors.
- `scripts/Test-Foundation.ps1 -DotnetPath ./artifacts/stage1/dotnet/dotnet.exe -NoBuild`:
  Passed, 6 tests. Реальная PostgreSQL через разрешённую admin env variable;
  disposable БД/роли созданы и удалены только по owned exact names.
- Schema SQL сгенерирован `scripts/Invoke-Migrations.ps1 -Action Script`; inspected:
  только foundation schema + migration_history; TABLE/COLUMN comments присутствуют.
- Clean apply, repeated apply, model drift, rollback-to-zero/reapply: Passed.
- `pg_catalog` TABLE/COLUMN comments на русском проверены: Passed.
- Runtime CREATE TABLE/SCHEMA, ALTER TABLE и DELETE migration history: отказ 42501.
- `pg_dump`/`pg_restore` в отдельную disposable restore DB: Passed; EF history сохранена.
- Server/Worker independently started: Passed. Worker остановлен, Server ready.
- Недоступный PostgreSQL endpoint: readiness 503, liveness 200.
- HTTP 404 Problem Details, correlation propagation и oversized header: Passed.
- `dotnet list LandErp.slnx package --vulnerable --include-transitive`: уязвимых
  пакетов для текущих источников не обнаружено; audit не отключался.
- `git diff --check`: Passed для изменений A; tracked local-data/secrets/DB отсутствуют.

## Найденное и ограничения

Первый Collector browser test запуск в файловой песочнице встретил TargetClosed;
повтор вне ограничений прошёл все 85 tests. Код Collector не менялся.
Первый recovery smoke выявил отсутствие явного target database для pg_restore;
исправлен runner, повтор всех foundation tests зелёный.
Whole-solution format verification обнаруживает pre-existing whitespace в Collector.
Глобальная переформатировка Collector запрещена scope; проверяется форматирование
новых/затронутых foundation src/tests. Это не скрывается как whole-solution Passed.
В imported старом отчёте Gate01 есть Markdown trailing spaces; исторический отчёт
не переписан. Production apply не выполнялся. Бизнес-сценарий ещё не реализован.

Следующий checkpoint B — Identity/Organization и реальный Blazor UI Kit shell.
