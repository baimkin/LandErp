# Collector ↔ Server protocol V1

**Статус:** активный контракт универсального Parser
**Дата:** 17 сентября 2026 года
**Граница:** самостоятельный Parser и LandErp Server по ADR-007

## 1. Режимы Parser

Parser использует один browser/parser pipeline и работает в одном из двух режимов:

- **Локально** — группы, ссылки, расписания, очередь и результаты принадлежат этому компьютеру. Локальные ссылки могут относиться к любым поддерживаемым задачам, не только к LandErp или земле.
- **Через Server** — группы, поиски и расписания принадлежат Server. Parser получает готовую работу из общего пула, сохраняет результат локально до подтверждения и отправляет его Server.

Переключение режима не переносит данные. Незавершённая работа сначала безопасно завершается или останавливается. Browser profiles остаются локальными в обоих режимах.

## 2. Явное добавление локальной ссылки на Server

Автоматической синхронизации нет. Пользователь выбирает одну локальную ссылку и нажимает «Добавить ссылку на Server». Перед сохранением он:

1. видит точный URL, источник и лимит;
2. выбирает существующую серверную группу либо создаёт серверную группу при наличии права;
3. задаёт серверное расписание;
4. подтверждает создание.

Локальная ссылка после этого остаётся локальной. Локальная группа, локальное расписание, задания, история и результаты не передаются. Server предотвращает повтор команды по `CommandId` и проверяет дубликат поиска по своим правилам.

## 3. Владение и права

- Server — единственный владелец server groups/searches/schedules и server jobs.
- Parser — владелец локальных групп, локальных расписаний, browser state и SQLite outbox.
- Machine credential разрешает выполнение работы. Управление server searches требует отдельного granted feature `searches.manage`; чтение — `searches.read`.
- Parser не создаёт ERP workflow, Catalog items или jobs напрямую. Создание/изменение server search приводит к работе только через server scheduler/manual run.

## 4. Подключение

Пользователь вводит один connection envelope. Он содержит версию формата, HTTPS origin Server, AgentId и постоянный machine token. Server показывает envelope только один раз при создании или перевыпуске Parser. Поэтому весь код является секретом и передаётся тем же защищённым способом, что прежний token.

Parser проверяет версию, HTTPS origin без user-info/query/fragment, AgentId и длину token, выполняет registration и немедленно защищает credential через Windows DPAPI CurrentUser. Отдельный activation endpoint и хранение recoverable token на Server в первой версии не вводятся.

Существующий ручной ввод Server URL + AgentId + token сохраняется как диагностический совместимый путь V1.

## 5. Существующий runtime V1

Базовый путь: `/api/collector/v1`.

| Операция | Назначение |
|---|---|
| `POST /registration` | Версия и capabilities |
| `POST /heartbeat` | Liveness, lease и additive runtime state/progress |
| `POST /work/claim` | Получить одну совместимую работу; `204` означает отсутствие работы |
| `POST /results` | Идемпотентная порционная и final доставка |

Authentication после подключения: `Authorization: Bearer <token>` и `X-LandErp-Agent-Id`. Только HTTPS.

Новые control operations для server mode:

| Операция | Право |
|---|---|
| `POST /workspace` — получить groups/searches | `searches.read` |
| `POST /workspace/groups` — создать group | `searches.manage` |
| `POST /workspace/searches` — создать search и schedule | `searches.manage` |

DTO находятся в `LandErp.Collector.Contracts.V1`; Server не отдаёт внутренние ERP entities. Изменение существующих записей зарезервировано контрактом и будет добавлено отдельным endpoint после появления соответствующего сценария в Parser UI.

## 6. Автономный цикл

После подключения Parser выполняет:

`Idle → Claiming → Parsing → Delivering → Idle`.

При `204` он повторяет Claim с bounded backoff и jitter. Один Agent имеет не более одного действующего server lease. Повторный Claim при действующем lease возвращает ту же работу или `AGENT_BUSY`; он не выдаёт второй Job.

Local и Server используют общий локальный диспетчер запуска: параллельный запуск двух режимов запрещён. Ручная настройка ссылки в браузере сама по себе не запускает сбор.

## 7. Heartbeat и ручная проверка

Старые поля `JobId`, `LeaseId`, `SourceStatus` сохраняются. Additive поля:

- `RuntimeState`: Idle, Claiming, Parsing, AwaitingManualAction, Delivering, Paused, Recovering;
- `SourceState`: Ready, Captcha, AuthenticationRequired, RateLimited, SourceError, Unknown;
- `Progress`: page, maxPages, processedCount, известный total, phase, lastUsefulActionAt.

`null` означает «значение не передано». `SourceState=Ready` явно снимает прежнюю CAPTCHA/auth/rate-limit индикацию после того, как Parser повторно проверил страницу. Heartbeat не завершает Job.

## 8. Lease и outbox

- ResultId создаётся до первой отправки и не меняется при неоднозначном сетевом результате.
- Точный повтор принятого ResultId возвращает прежний receipt, даже если lease позже истёк.
- Исходный JSON сохраняется для диагностики.
- `Superseded` — отдельное локальное terminal-состояние, а не server acknowledgement.
- Перенос сохранённого результата на новый lease того же Job создаёт новые deliveries атомарно и сохраняет связь с исходными.
- Permanent failure одного Job не блокирует deliveries других Jobs.
- Outbox partition привязан к Server origin и AgentId.

Если работу получил другой Agent, старый Parser прекращает browser execution на безопасной границе, сохраняет уже полученные данные и ожидает точного server reconciliation. `WORK_NOT_ALLOWED` сам по себе недостаточен для удаления или supersede результата.

## 9. Ошибки

Parser читает `code` из RFC Problem Details и использует HTTP status только как fallback. Ответ не попадает в пользовательский текст или логи целиком.

| Категория | Действие Parser |
|---|---|
| network/timeout/5xx/429 | повтор неизменного запроса с backoff |
| `AGENT_UNAUTHORIZED` | остановить server loop и запросить подключение |
| `REGISTRATION_REQUIRED` | registration и один повтор операции |
| `AGENT_BUSY` | восстановить текущую работу, не брать новую |
| `LEASE_EXPIRED_OR_REPLACED` | reconciliation; данные сохранить |
| `IDEMPOTENCY_CONFLICT` | изолировать delivery; ResultId не менять |
| `OBSERVATION_KEY_CONFLICT` | изолировать delivery; observation key не менять |
| validation/source/permission | остановить только затронутую команду |

Технический code доступен в диагностике. Основной UI показывает действие человека понятным текстом.

## 10. Совместимость и критерии

- Старый heartbeat без новых полей остаётся допустимым.
- Новый Parser не требует control operations для Local mode.
- Server игнорирует отсутствующие optional runtime fields.
- Breaking change требует нового contract version; новые optional поля остаются в V1.
- Server не содержит browser/Playwright/source parsing, Parser не подключается к PostgreSQL.

Протокол считается реализованным после contract serialization tests, старый/new compatibility tests, проверки кода подключения, one-active-lease concurrency test, CAPTCHA→Ready test и crash tests переходов outbox.
