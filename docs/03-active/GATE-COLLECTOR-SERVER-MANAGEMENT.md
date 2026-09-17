# Gate — Collector Server management и machine protocol

**Статус:** Implemented — ожидает review владельца  
**Ветка:** `codex/collector-server-management`  
**База:** `08771293ae0a75085484d10ed50b9897c480f4c4`

## Цель

Server задаёт полный контракт работы локальных Parser Agent. Локальный Parser
остаётся самостоятельным приложением по ADR-007 и позднее адаптируется к
опубликованному серверному протоколу.

## Checkpoint 1 — server management

- максимум один действующий lease на один Agent;
- точные KPI общей очереди и текущего attention без ограничения окном истории;
- структурированный последний запуск поиска и отдельная история запусков;
- наблюдаемый health scheduler;
- отдельные server-side permissions чтения и управления Collection;
- real-PostgreSQL concurrency, authorization и read-model tests.

## Checkpoint 2 — machine protocol

- одноразовый код подключения и обмен на постоянную machine credential;
- additive heartbeat state/progress;
- точные machine error codes и retry classification;
- явная семантика expired/replaced/superseded lease/result;
- совместимость существующего V1 пути;
- канонический `docs/05-collection/COLLECTOR_SERVER_PROTOCOL_V1.md`;
- executable HTTPS tests контракта.

## Границы

- не менять локальный Parser, browser adapters, WPF и локальную SQLite;
- не менять Procurement, Incoming, Organization и Overview;
- не переносить browser/source-specific код на Server;
- не применять migration к production или пользовательской `landerp_local`;
- старые migrations не переписывать;
- не выполнять merge, rebase или push без отдельного разрешения.

## Required reading

1. `AGENTS.md`;
2. `README.md`;
3. `docs/START_HERE.md`;
4. `docs/03-active/ACTIVE_TASK.md`;
5. этот Gate;
6. `docs/02-decisions/ADR-007_Collector_как_самостоятельный_продукт_и_граница_с_Server.md`;
7. Collection-разделы `docs/03-active/STAGE1_COMPLETION_MASTER_PLAN.md`;
8. `docs/03-active/reports/STAGE1_PHASE2_REPORT.md`;
9. `docs/03-active/reports/COLLECTOR_PHOTO_FIX_REPORT.md`.

## Проверки

- locked restore и Release build без warnings;
- targeted Collection real-PostgreSQL tests;
- executable HTTPS machine protocol tests;
- migration clean/repeat apply и `HasPendingModelChanges() == false`;
- полный `LandErp.Foundation.Tests`, если среда доступна;
- итоговый отчёт с командами, результатами и ограничениями.
