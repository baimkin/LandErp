# Stage 1 — checkpoint C

Работающий Collector сохранён самостоятельным. Внутренний BCL-only проект
`LandErp.Collector.Contracts/V1` задаёт HTTPS wire contract; Collector не ссылается
на Application/Infrastructure/EF/Npgsql и не подключается к PostgreSQL.
Server не имеет browser/Playwright/WPF/SQLite зависимостей.

Machine identity: Server генерирует высокоэнтропийный token, показывает один раз,
хранит SHA-256 verifier и metadata/audit. Owner/Admin может отозвать и перевыпустить
credential; старый token запрещён. Agent имеет только registration/capabilities,
heartbeat, свои разрешённые work/lease и results/errors. ERP/admin API ему недоступны.
Runtime certificate validation не отключается, auth headers не журналируются.

Работа закрепляется конкретному AgentId. Короткие PG transactions, row/advisory locks
и случайный fencing token защищают claim, natural identity и повторную доставку.
Heartbeat продлевает действующий lease; старый/истёкший token не принимается.
ResultId idempotency и observation dedup раздельны. Количество и source outcome
сохраняются, CAPTCHA/rate limiting/auth/error не превращаются в пустой успех.

Catalog: Organization+Source+ExternalId identity, реляционное current state,
append-only observation/delivery, Raw/Parsed/Presence и provenance/version/time.
Missing/ParseFailed не очищают known; late observations не перезаписывают newest.
Money decimal+RUB; площадь decimal numeric(19,4), rounding ToEven; существенные
изменения имеют отдельную DataRevision и понятную причину появления в очереди.

Collector Server Adapter использует прежний QueueRunner/LocalStore для одной
разрешённой ссылки, не меняет Local selection/settings. Отдельный SQLite outbox
атомарно сохраняет все chunks до HTTP и помечает ack только после квитанции.
После outage/restart повторяет тот же ResultId; expired work можно перевыдать,
локальные наблюдения не теряются. Source adapters не изменены.

Фактические проверки SDK 10.0.112:

- locked restore, Release solution build — 0 warnings/errors;
- `dotnet test tests/LandErp.Foundation.Tests -c Release --no-build` — 8/9,
  оставшаяся metadata/backup проверка после устранения старого migration count
  прошла отдельно; C integration/architecture/metadata — 3/3;
- real HTTPS control Collector → PostgreSQL; machine API negatives, revoke/rotate,
  idempotent retry/conflict, duplicate observations, missing fields, late delivery,
  lease renew/fencing, Server outage/local usability и restart, backup/restore rows;
- real Chrome admin UI: create Collector/search/job, online/offline state,
  secret скрыт перед screenshot; Identity/organization regression проходит;
- `dotnet test tests/LandErp.ParserSpike.Tests -c Release --no-build --filter
  'TestCategory!=Live'` — 85/85. Live требует явно заданные URLs и не включён в CI;
- scoped format и `git diff --check`; local data/credentials игнорируются.

Первый полный Collector запуск без фильтра обнаружил live test без настроенных
URLs и два UI regression из-за нового первого tab. Server tab перемещён после
существующих Local tabs, все offline tests после исправления зелёные.
Недостающий COLUMN comment добавлен новой migration, применённая не переписана.

Ограничения: сбор инициируется пользователем; heartbeat/retry требуют открытого
Collector. Реальный marketplace smoke дополнительный и здесь не выполнялся;
parser regressions используют offline fixtures. Catalog contract пока передаёт
основные публичные поля закупки; полные локальные diagnostics остаются Collector.
Нет broker/Redis/ESB или обхода защиты источников. Следующий срез — Procurement D.
