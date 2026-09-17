# Отчёт — Collector Server management и machine protocol

**Статус:** реализовано, ожидает review владельца  
**Ветка:** `codex/collector-server-management`  
**База:** `08771293ae0a75085484d10ed50b9897c480f4c4`

## Результат

Server теперь является полным authority для Search, общей очереди, lease/fencing,
machine identity, состояния Parser и управленческого read model. Локальный Parser,
его WPF, browser adapters и SQLite не изменялись.

Закрыто:

- максимум один действующий lease на Agent; повторный Claim возвращает ту же работу;
- точные KPI очереди, online/busy Agent, актуальный attention и структурированный last run/history;
- сохраняемый health scheduler;
- `collection.read` отдельно от `agents.manage` и `searches.manage`, включая upgrade существующих ролей;
- одноразовый 15-минутный код подключения и однократный обмен на server-generated credential;
- heartbeat runtime state, progress и явная очистка временного attention;
- точные `LEASE_EXPIRED`, `LEASE_REPLACED`, `RESULT_SUPERSEDED`, `WORK_NOT_ACTIVE`,
  `WORK_NOT_ALLOWED` и `RETRY_LATER` с `retryable` в Problem Details;
- forward-миграция существующих Agent в `Idle`;
- канонический контракт `docs/05-collection/COLLECTOR_SERVER_PROTOCOL_V1.md`.

Machine endpoint создания Search намеренно не добавлен. Search создаёт и меняет
пользователь на Server; Parser получает только назначенную Server работу.

## Основные файлы

- `src/LandErp.Collector.Contracts/V1/CollectionContracts.cs`;
- `src/LandErp.Infrastructure/Modules/Collection/CollectorGateway.cs`;
- `src/LandErp.Infrastructure/Modules/Collection/CollectionAdministration.cs`;
- `src/LandErp.Infrastructure/Modules/Collection/CollectionScheduler.cs`;
- `src/LandErp.Server/Foundation/CollectorEndpoints.cs`;
- `src/LandErp.Server/Components/Pages/Collectors.razor`;
- migrations `20260917114553_CollectorServerManagement` и
  `20260917115520_CollectorMachineProtocol`;
- targeted tests в `tests/LandErp.Foundation.Tests`.

## Проверки

- locked restore выполнен ранее в Gate;
- Release build: успешно, 0 warnings, 0 errors;
- Collection/Collector/migration PostgreSQL + HTTPS: 12/12 успешно;
- Parser contract tests без браузера: 19/19 успешно;
- `HasPendingModelChanges() == false`, repeat apply, rollback-to-zero/reapply,
  upgrade существующего Agent и permission grant проверены;
- `git diff --check`: успешно, только уведомления о будущей нормализации LF/CRLF;
- полный Foundation-прогон до исключения UI: 41/47; один migration assertion затем
  исправлен и подтверждён targeted-тестом, остальные пять — браузерные timeout на
  форме входа при параллельных ветках. По решению владельца браузерные тесты не
  являются блокером и проверяются вручную;
- repository-wide `dotnet format --verify-no-changes` неприменим как Gate-check:
  фиксирует большой существующий whitespace backlog, включая неизменённые Parser-файлы.

## Ограничения и следующий шаг

- migrations не применялись к production или пользовательской `landerp_local`;
- commit, push, merge и rebase не выполнялись;
- код подключения начнёт использоваться конечным пользователем после отдельной
  адаптации локального Parser;
- следующий отдельный Gate: аудит текущего Parser относительно опубликованного
  Server protocol, затем bounded реализация подключения, auto-claim loop и outbox recovery.
