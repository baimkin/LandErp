# Collector ↔ Server protocol V1

**Статус:** активный контракт универсального Parser и server authority
**Дата:** 17 сентября 2026 года
**Transport:** HTTPS JSON
**Base path:** `/api/collector/v1`

## 1. Граница ответственности

Server является единственным владельцем и валидатором server groups, searches,
schedules, общей очереди Job, lease/fencing и данных Catalog. Организация всегда
определяется только по авторизованному Parser token; organization id из body не
принимается.

Локальный Parser владеет browser/source adapters, локальными группами и заданиями
Local mode, browser state, SQLite и durable outbox. Parser не подключается к
PostgreSQL и не создаёт ERP workflow, Catalog items или Job напрямую.

Локальному Parser разрешено явно создать server group или search через machine
API только если конкретному Parser выдано `CanManageSearches`. Разрешение по
умолчанию выключено; пользователь с `agents.manage` включает и отзывает его на
Server. Server выполняет валидацию, аудит, rate limiting и idempotency.

## 2. Режимы Parser и перенос

- **Локально** — группы, ссылки, расписания, очередь и результаты принадлежат
  компьютеру.
- **Через Server** — группы, поиски, расписания, очередь и результаты принадлежат
  Server; Parser только исполняет полученную работу и доставляет наблюдения.

Переключение режима не переносит данные автоматически. Пользователь может явно
добавить одну выбранную локальную ссылку на Server: проверить URL/источник/лимит,
выбрать или создать server group, задать server schedule и подтвердить команду.
Локальная группа, история, результаты и задания при этом не переносятся.

## 3. Одноразовое подключение

Пользователь с `agents.manage` создаёт Parser и получает код
`LDP1.<base64url>`. Payload содержит HTTPS origin Server, AgentId и одноразовый
64-hex activation secret. Код действует 15 минут и показывается один раз.

Parser вызывает `POST /activation` с AgentId, activation secret, machine name,
contract version, версией приложения и capabilities. Server хранит только
SHA-256 verifier и один раз возвращает постоянную 64-hex machine credential.
Parser атомарно сохраняет её через Windows DPAPI CurrentUser до запуска цикла.
Повторная активация тем же кодом запрещена; при потере ответа пользователь
отзывает identity и создаёт новый код.

Legacy AgentId + credential сохраняет совместимость через `/registration`.

## 4. Аутентификация

Все операции кроме `/activation` передают:

- `Authorization: Bearer <credential>`;
- `X-LandErp-Agent-Id: <AgentId>`.

Server находит Parser по этим данным и из записи Parser получает организацию и
права. Credential, activation secret, cookies и auth headers не попадают в логи,
audit, diagnostics или обычный UI.

## 5. Operations

| Операция | Назначение / условие |
|---|---|
| `POST /activation` | Одноразовый обмен кода на credential |
| `POST /registration` | Версия и capabilities |
| `POST /heartbeat` | Liveness, lease, runtime state и progress |
| `POST /work/claim` | Одна совместимая работа; `204` — работы нет |
| `POST /results` | Идемпотентная порционная и final доставка |
| `POST /workspace` | Server groups/searches; требуется `CanManageSearches` |
| `POST /workspace/groups` | Создать server group; требуется `CanManageSearches` |
| `POST /workspace/searches` | Создать server search/schedule; требуется `CanManageSearches` |

Control-команды содержат `CommandId`. Точный повтор возвращает ранее созданный
объект; несовместимый повтор даёт `IDEMPOTENCY_CONFLICT`. DTO находятся в
`LandErp.Collector.Contracts.V1`; внутренние ERP entities наружу не выдаются.

## 6. Claim и один активный Job

Один Parser имеет максимум один действующий lease. Повторный Claim возвращает ту
же работу. Назначение выполняет только Server с блокировкой Agent и очереди;
совместимость определяется зарегистрированными capabilities. Expired lease может
быть выдан другому Parser. Локальный и server режимы используют общий локальный
диспетчер, поэтому одновременно два сбора не запускаются.

## 7. Heartbeat и progress

Старые `JobId`, `LeaseId`, `SourceStatus` сохраняются. Additive optional поля:

- `RuntimeState`: Idle, Claiming, Parsing, AwaitingManualAction, Delivering,
  Paused, Recovering;
- `SourceState`: Ready, Captcha, AuthenticationRequired, RateLimited,
  SourceError, Unknown;
- `Progress`: page, maxPages, processedCount, totalCount, phase,
  lastUsefulActionAt (UTC).

`null` означает «значение не передано». `SourceState=Ready` явно очищает прежнее
attention state после повторной проверки страницы. Heartbeat продлевает только
действующий lease и не завершает Job.

## 8. Results, lease и recovery

- ResultId создаётся до отправки и не меняется при неоднозначном результате.
- Не более 25 observations передаётся за запрос.
- Точный повтор ResultId с тем же payload возвращает сохранённый receipt.
- JobId + LeaseId выполняют fencing.
- Final delivery завершает Job; `final=false` допустим только для успешной порции.
- `RESULT_SUPERSEDED` — единственное явное разрешение пометить локальную delivery
  terminal Superseded; generic 403/409 не разрешает удалять payload.
- Permanent failure одного Job не блокирует deliveries других Jobs.
- Outbox partition привязан к Server origin и AgentId.

## 9. Ошибки

Ответ — RFC Problem Details со стабильным `code`, `correlationId` и `retryable`.
Parser принимает решение по `code`, HTTP status использует как fallback.

| Code | Действие |
|---|---|
| `ACTIVATION_INVALID`, `ACTIVATION_EXPIRED`, `ACTIVATION_USED` | запросить новый код |
| `AGENT_UNAUTHORIZED` | остановить server loop и запросить подключение |
| `REGISTRATION_REQUIRED` | registration и один повтор |
| `SEARCH_PERMISSION_REQUIRED` | запретить control-команду; данные не менять |
| `AGENT_MULTIPLE_ACTIVE_WORK` / `AGENT_BUSY` | не брать вторую работу, оператору |
| `LEASE_EXPIRED`, `LEASE_REPLACED` | reconciliation и новый Claim |
| `RESULT_SUPERSEDED` | сохранить payload, terminal Superseded |
| `IDEMPOTENCY_CONFLICT`, `OBSERVATION_KEY_CONFLICT` | изолировать delivery/команду |
| `WORK_NOT_ALLOWED`, `WORK_NOT_ACTIVE` | остановить затронутую операцию |
| network/timeout/5xx/429 или `retryable=true` | повтор неизменного запроса с backoff |

## 10. Совместимость и безопасность

- Старый heartbeat без новых полей допустим.
- Новые optional поля остаются в V1; breaking change требует новой версии.
- Local mode не требует control operations.
- Server не содержит browser/Playwright/source parsing; Parser не получает
  PostgreSQL credentials или внутренние ERP entities.
- CAPTCHA/auth решает человек; автоматический обход запрещён.
- Rate limit применяется ко всему machine API.

Критерии реализации: serialization/compatibility tests, проверка одноразового
подключения, permission/idempotency/audit tests для group/search, one-active-job
concurrency, CAPTCHA→Ready, recovery/outbox и объединённые PostgreSQL tests.
