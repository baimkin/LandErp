# LandErp Collector Server Protocol V1

**Статус:** server authority  
**Transport:** HTTPS JSON  
**Base path:** `/api/collector/v1`

## 1. Граница

Server владеет Search, schedule, shared Job queue, lease/fencing, Catalog и
machine identity. Локальный Parser владеет browser/source adapters, локальной
SQLite, ручным прохождением CAPTCHA и durable outbox. Parser не подключается к
PostgreSQL и не создаёт server Search.

## 2. Подключение

Администратор создаёт Parser и получает один код вида `LDP1.<base64url>`. Payload
— UTF-8 строка `<absolute-server-base-uri>|<agent-id-N>|<64-hex-secret>` без
padding в base64url. Base URI оканчивается `/`; `agent-id-N` — 32 hex digits
без дефисов. Код действует 15 минут и показывается только при создании.

Parser декодирует код локально и вызывает:

`POST /activation`

```json
{
  "agentId": "uuid",
  "activationSecret": "64 hex",
  "machineName": "PC-01",
  "contractVersion": 1,
  "version": "1.2.3",
  "capabilities": ["Avito", "Cian"]
}
```

Успешный ответ один раз возвращает постоянную machine credential:

```json
{
  "agentId": "uuid",
  "credential": "64 hex",
  "contractVersion": 1,
  "agentName": "Ноутбук менеджера"
}
```

Server хранит только SHA-256 verifier activation secret и credential. Потерянный
успешный activation response не повторяется: администратор отзывает identity и
создаёт новый код. Parser обязан атомарно сохранить полученную credential до
начала обычного цикла.

## 3. Постоянная аутентификация

Все операции кроме `/activation` передают:

- `Authorization: Bearer <credential>`;
- `X-LandErp-Agent-Id: <AgentId>`;
- correlation ID без секретов.

Credential хранится локально средствами Windows DPAPI. Она никогда не выводится
в логи, audit, diagnostics или обычный UI.

## 4. Registration

`POST /registration` подтверждает contract version, установленную версию и
capabilities. Registration обязателен для legacy credentials и может безопасно
повторяться после запуска Parser.

## 5. Claim

`POST /work/claim` не имеет body.

- `204 No Content` — совместимой работы сейчас нет;
- `200` — Server возвращает `JobId`, `LeaseId`, `LeaseExpiresAt`, source,
  search URL, max pages и label;
- один Agent имеет максимум один действующий lease;
- повторный Claim того же Agent возвращает ту же действующую работу и lease;
- expired lease может быть выдан другому совместимому Agent;
- Parser не выбирает Search и не изменяет server routing.

Создание и изменение Search выполняется только пользователем на Server. Machine
endpoint создания Search намеренно отсутствует: локальный Parser не является
источником server-функциональности и не расширяет собственные полномочия.

## 6. Heartbeat, state и progress

`POST /heartbeat` продлевает только действующий lease. Поля V1 остаются
необязательными для совместимости старого Parser.

Runtime state:

- `Idle`;
- `Claiming`;
- `Parsing`;
- `Delivering`;
- `AwaitingManualAction`;
- `Paused`.

Progress:

- `processed` — неотрицательное число;
- `total` — nullable;
- `currentPage` — nullable;
- `maxPages` — nullable;
- `lastActivityAt` — nullable UTC.

`sourceStatus` передаёт CAPTCHA, необходимость входа или rate limit.
`clearSourceStatus=true` явно очищает прежнее attention state. Отсутствующее поле
не означает очистку.

## 7. Results

`POST /results` принимает immutable delivery:

- `ResultId` — идемпотентность доставки;
- `JobId + LeaseId` — fencing;
- outcome;
- не более 25 observations в запросе;
- `final=false` разрешён только для успешной промежуточной порции;
- final delivery завершает Job.

Точный повтор `ResultId` с тем же payload возвращает сохранённый receipt. Другой
payload с тем же ResultId возвращает `IDEMPOTENCY_CONFLICT`. Observation key
имеет независимую защиту `OBSERVATION_KEY_CONFLICT`.

## 8. Lease и recovery

- `LEASE_EXPIRED` — срок lease закончился;
- `LEASE_REPLACED` — Job уже имеет другого исполнителя или fencing token;
- `RESULT_SUPERSEDED` — Job уже terminal, старый результат больше не может быть
  принят;
- `WORK_NOT_ACTIVE` — heartbeat относится не к активной работе;
- `WORK_NOT_ALLOWED` — Job не существует в организации Agent либо недоступен.

Parser повторяет неоднозначную доставку с тем же ResultId. Только явный
`RESULT_SUPERSEDED` разрешает пометить локальную delivery terminal Superseded,
сохранить payload для диагностики и разблокировать outbox. Generic 403/409 не
является разрешением удалить локальные данные.

## 9. Machine errors

Ошибки возвращаются как Problem Details. Стабильный код находится в `code`,
признак временной ошибки — в `retryable`.

| Code | Значение | Retry |
|---|---|---|
| `ACTIVATION_INVALID` | неверный или неизвестный код | нет |
| `ACTIVATION_EXPIRED` | срок кода истёк | нет |
| `ACTIVATION_USED` | код уже обменян | нет |
| `AGENT_UNAUTHORIZED` | credential отозвана/неверна | нет |
| `REGISTRATION_REQUIRED` | требуется registration | после registration |
| `VERSION_OR_CAPABILITY_UNSUPPORTED` | несовместимый contract/client | нет |
| `AGENT_MULTIPLE_ACTIVE_WORK` | нарушен server invariant | нет, оператору |
| `LEASE_EXPIRED` | lease истёк | новый Claim |
| `LEASE_REPLACED` | lease заменён | новый Claim |
| `RESULT_SUPERSEDED` | результат окончательно устарел | нет |
| `IDEMPOTENCY_CONFLICT` | ResultId использован с другим payload | нет |
| `OBSERVATION_KEY_CONFLICT` | observation key использован с другими данными | нет |
| `RETRY_LATER` | rate limit Server | да |
| `WORK_NOT_ALLOWED` | работа недоступна Agent | нет |
| `WORK_NOT_ACTIVE` | heartbeat не относится к active lease | новый Claim |

HTTP status используется как класс ответа, но Parser принимает решение по
machine code. Нераспознанный код не должен приводить к потере outbox payload.

## 10. Цикл Parser

Целевой цикл клиента:

`load credential -> registration -> flush outbox -> resume/recover -> claim -> parse -> heartbeat/progress -> deliver -> claim`.

При `204` Parser применяет bounded backoff с jitter. Одновременно выполняется не
более одного Claim и одной server work. Local mode остаётся независимым.

## 11. Совместимость

- существующие AgentId + credential продолжают работать;
- `/registration`, `/heartbeat`, `/work/claim`, `/results` сохраняют V1 path;
- новые heartbeat поля additive и необязательные;
- breaking changes требуют нового contract version и периода совместимости.

## 12. Безопасность

- только HTTPS;
- connection code, credential, cookies и auth headers не логируются;
- Server не принимает browser state, raw HTML или PostgreSQL credentials;
- CAPTCHA и authentication решаются человеком; автоматический обход запрещён;
- rate limit применяется ко всему machine API.
