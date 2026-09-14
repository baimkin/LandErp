# ERP-01 — техническое основание и БД

**Версия:** 1.0\
**Статус:** Подготовлен; реализация не разрешена запросом ERP-00.\
**Предшественник:** ERP-00 выполнен 2026-09-14.\
**Ветка подготовки:** `docs/erp-core-foundation`.\
**Планируемая implementation-ветка:** `codex/erp-01-foundation`, не создана.\
**Thinking:** High; финальный review conventions/DDL — максимальный доступный.\
**Отчёт реализации:** `reports/ERP-01_REPORT.md` по GATE_REPORT_TEMPLATE; ещё не существует.

## 1. Результат и разрешение

После отдельного утверждения создать минимальный технический фундамент ERP:
независимые Server и Worker hosts, Application/Infrastructure boundaries,
подключение PostgreSQL и проверяемые EF migrations, conventions,
health/logging/error model и обязательные tests. Ни один бизнес-сценарий не строится.

Текущий запрос разрешает подготовку документа, но не реализацию Gate.
Генерация начальной migration в будущем ERP-01 допускается только после
зафиксированного финального review раздела 5. Применение к production требует
отдельного разрешения и не является результатом этого Gate: первая поставка Local/Test.

## 2. Required reading

Точный ограниченный комплект — в [ACTIVE_TASK](ACTIVE_TASK.md).
Обязательны ADR-001/002/006/007, applicable FP-001/002/004 и §§4,7,8 (ERP-01),10
бизнес-карты. FP-002 нужен для будущих scope/audit invariants, а не Identity-кода.
Старые TP-001–TP-004 используются как материал из ревизии ERP-00; не читать
и не исполнять весь TP-001–TP-010 подряд.

## 3. Входит: полный scope одного Gate

| Область | Ограниченный результат |
|---|---|
| Solution/build | Минимальные Server/Application/Infrastructure/Worker; C#14/.NET10, nullable, warnings as errors, CPM/точные версии/locks; reuse проверенных общих настроек Collector после выбора базы ветки |
| Границы | Server и Worker → Application/Infrastructure; Infrastructure → Application; Application не знает hosts/EF/browser; никаких циклов и зависимостей Server от Collector/WPF/Playwright |
| Worker | Самостоятельный host skeleton: конфигурация, lifetime, безопасные startup logs и проверка DB connectivity; никаких фоновых collection jobs, scheduler, leases/reaper |
| Persistence | Один основной LandErpDbContext в Infrastructure по ADR-002, Npgsql/EFCore, design-time factory, отдельная роль migrator, module schema ownership conventions |
| Data conventions | UUID/external identity, UTC/business timezone, decimal/currency, concurrency, provenance/retention/history/naming; записать решения до migration, без бизнес-таблиц |
| Начальная migration | Только техническая foundation schema и EF migration history; без фиктивной business entity, Identity/audit/workflow/queue/outbox/catalog tables; окончательные имена/DDL проходят review |
| Конфигурация | Local/Test и документированные границы будущих Staging/Production; env/approved secret storage, fail-fast при отсутствии критичных настроек, runtime без DDL |
| Наблюдаемость | Server /health/live и /health/ready; Worker проверяется отдельно; UTC/service/version/environment/correlation в безопасных логах |
| Ошибки | Безопасный Problem Details/stable error code/correlation ID; ограничение недоверенного correlation header, без stack trace/SQL/секретов в ответе |
| Проверки | Restore/build/format/audit; meaningful config/architecture/security tests; real PostgreSQL migration/integration, host smoke, DB outage/readiness; reproducible local runner/CI contract без production deploy |

Initial schema хранит только техническое основание. EF history не служит
бизнес-таблицей или generic metadata framework. При пустой модели выбрать
проверяемую schema-only migration, не вводить dummy entity ради генерации.
Test-only таблицы для проверки mappings не входят в production migration.

## 4. Не входит

Identity/Owner login/Organization/permissions/scopes implementation;
Workflow/WorkTask/Approval/Party/финансы/документы/Audit/BusinessTimeline/notifications;
Blazor shell/UI/showcase и business endpoints/modules; Collector rewrite;
новый Agent/AgentContracts и server adapter/registration/heartbeat/job/result API;
lease/retry/server queue/outbox/scheduler; прямой SQLite↔PostgreSQL sync/import;
PostGIS/NTS, брокеры, Redis, ESB, full Event Sourcing; deployment, installer,
auto-update, production DB creation/apply. Collector продолжает отдельный lifecycle.

## 5. Финальный review перед первой production migration

Ни один пункт не считать закрытым из-за наличия старого TP-примера.
В отчёте ERP-01 зафиксировать точное решение, review outcome и evidence.
Review проводят до генерации migration, затем отдельно проверяют полученный SQL.
Все применимые нерешённые вопросы блокируют генерацию; вопросы будущих бизнес-срезов
помечаются deferred с инвариантом, без добавления их таблиц в Gate.

| Решение | Принятая вводная / что требуется финально проверить |
|---|---|
| База ветки/build | Docs-ветка пока не содержит Collector checkpoint. Выбрать базу и способ сохранения SDK/CPM/MSTest/locks; не пересоздавать код и не merge/rebase без отдельного разрешения |
| Версии/зависимости | .NET10/C#14/EFCore10/PostgreSQL18 из ADR-001; выбрать проверенные стабильные patch-версии совместимого Npgsql и dotnet-ef, license/support/advisories; новые пакеты отдельно обосновать и одобрить по ADR-006 |
| DbContext/владение | Один основной контекст принят ADR-002. Утвердить technical schema, module naming/ownership, history table и migration assembly; исключить зависимости от Collector |
| Initial DDL | Утвердить schema-only состав, имена и comments; нет business/Identity/workflow/audit/queue/finance/outbox tables, случайных provider extensions и dummy entities |
| Internal identity | PostgreSQL uuid, предпочтительно UUIDv7; утвердить генерацию application-side/DB и проверки. ExternalId и BusinessNumber отдельны; Source+ExternalId — identity наблюдаемого объявления, не земельного актива |
| Время | UTC/timestamptz для instant, явная business timezone (проверить Windows/Linux ID и DST); различать RecordedAt/EffectiveAt/ObservedAt, date-only и unknown, без выдуманного EffectiveAt |
| Money/числа | decimal + currency, без float/double. Зафиксировать общую storage precision/scale, currency precision/rounding и unit conversions; finance tables/ledger не создавать, специфические правила проверяются первым денежным use case |
| Версии | Optimistic concurrency/version для изменяемых агрегатов; approval привязан к конкретной версии. Определить будущую token policy без создания Approval/агрегатов сейчас |
| История/удаление | Current state relational, значимые facts append-only; audit/approvals/подтверждённые юридические/финансовые facts не удаляются runtime. Retention — отдельный privileged процесс. Full Event Sourcing не использовать |
| Presence/provenance | Raw/Parsed/Presence сохраняют различия; missing не очищает known; источник/наблюдение/order/idempotency определяются контрактом владельца, без автоматического слияния cross-source assets |
| Настраиваемость | Гибкие business stages отделены от strict facts Acquired/Payment/Transfer/Sale; не создавать configurable workflow/универсальное ядро в ERP-01 |
| DB roles/secrets | Runtime без schema DDL; migrator отдельный, connection strings вне Git/logs, design-time без production secrets; проверить fail-fast и запрет startup migration |
| Migration lifecycle | Applied migration immutable; новая migration для исправления. SQL review, чистая DB, повтор применения, upgrade предыдущей schema когда она есть, schema drift; expand–migrate–contract для опасных future changes |
| Recovery/apply | Backup/restore и forward recovery, locks/downtime и app/schema compatibility; rollback проверяется только в disposable Local/Test. Production approval/apply отдельным шагом, не обещать destructive rollback реальных данных |

Детали Identity, Workflow, интеграции и финансовых моделей не являются причиной
строить их заранее. Для ERP-01 достаточно согласованных foundation conventions
и Initial DDL; список будущих обязательных инвариантов не превращается в schema.

## 6. Планируемая карта файлов после утверждения

До первой записи реализации адресно проверить базу и actual файлы;
обновить карту при выявленном отличии, не expanding scope.

| Файлы/каталог | Действие в ERP-01 |
|---|---|
| LandErp.slnx; global.json; Directory.Build.props; Directory.Packages.props; .editorconfig; affected packages.lock.json | Reuse существующих verified settings; добавить только ERP projects/одобренные зависимости, Collector entries сохранить если они есть в выбранной базе |
| src/LandErp.Server/ | Minimal host, configuration, health, Problem Details/correlation, safe settings |
| src/LandErp.Application/ | Минимальная граница application contracts; без placeholder module handlers/entities |
| src/LandErp.Infrastructure/ | Persistence registration/DbContext/design-time/conventions; Migrations только после review |
| src/LandErp.Worker/ | Host skeleton, lifetime/config/DB smoke; без jobs |
| tests/LandErp.UnitTests/ | Config/conventions/security tests, существующий согласованный MSTest reuse |
| tests/LandErp.ArchitectureTests/ | References/forbidden dependencies, public contracts без EF/UI/browser |
| tests/LandErp.IntegrationTests/ | Real PostgreSQL, migration/recovery/runtime role, Server/Worker smoke |
| scripts/ | Воспроизводимый Local/Test runner и проверки; не production apply script |
| docs/03-active/reports/ERP-01_REPORT.md | Решения review, команды/evidence, ограничения, результат проверок, recommendation |

Не создавать автоматически все пять test assemblies из старого FP-001:
E2E smoke внутри integration; Agent contract tests появятся с интеграцией.
Testcontainers — кандидат, не предодобренный новый пакет. Доступная изолированная
PostgreSQL или одобренный container driver должны реально запускать integration tests.

## 7. Обязательные проверки будущей реализации

1. Проверенная environment/version matrix; locked restore, Release 0 warnings/errors,
   format, dependency advisory/license review. Audit не отключать для объявления security check Passed.
2. Forbidden references/cycles и связи с Collector/Playwright/WPF/SQLite ловятся
   architecture checks; Server build/start не запускает браузер.
3. Server и Worker стартуют независимо; missing/invalid config даёт понятный
   fail-fast без secret disclosure.
4. PostgreSQL нужной версии: migration SQL review → apply на чистой disposable DB →
   повтор apply без новых изменений → schema inspection/EF history →
   rollback/reapply Local/Test и backup/restore evidence. Предыдущая production
   schema ещё отсутствует: upgrade из неё NotApplicable, не выдумывать legacy migration.
5. Runtime role не делает DDL/startup migration; migrator role ограничен средой.
   Тесты не подключаются к пользовательской SQLite или production PostgreSQL.
6. DB outage делает Server readiness unhealthy; liveness остаётся живым;
   Worker outage не меняет Server readiness. Health/errors/logs не раскрывают secrets.
7. Problem Details/error code/correlation, invalid/oversized header и негативный
   security case: нет stack trace, SQL parameters, connection strings, tokens/cookies.
8. git diff --check; scope/security review: нет business tables/UI/Collector rewrite,
   credentials/build/local data не tracked. Отчёт содержит actual команды и evidence.

Базовая последовательность (после существования solution в выбранной базе):

```powershell
dotnet --info
dotnet restore LandErp.slnx --locked-mode
dotnet build LandErp.slnx -c Release --no-restore
dotnet format LandErp.slnx --no-restore --verify-no-changes
dotnet test LandErp.slnx -c Release --no-build --filter 'TestCategory!=Live'
dotnet list LandErp.slnx package --vulnerable --include-transitive
git diff --check
```

Actual real-DB/host commands и выбранная isolated DB фиксируются в отчёте ERP-01.
Недоступная DB/container check — NotVerified и блокирует техническую приёмку Gate,
не заменяется SQLite/mock. В этом ERP-00 ни одна из этих implementation checks не запускалась.

## 8. FP-004 applicability и остановка

§16 FP-004 проверен для foundation: scope/оргструктура, гибкие стадии/strict facts,
общие Task/Approval/Party/Document/Notification, budget/commitment/accrual/payment,
LegalEntity/стороны и справочники не реализуются без business use case.
Audit/timeline — будущие invariants, schema deferred; время/provenance и
ExternalReference/idempotency учтены как conventions, transport отсутствует.
Переусложнение исключено: нет universal framework и placeholder core tables.

Done только после фактических mandatory checks, review conventions/DDL,
работающих Local/Test hosts/persistence и отчёта с ограничениями.
После отчёта остановиться. ERP-02 и остальные этапы не активировать.
