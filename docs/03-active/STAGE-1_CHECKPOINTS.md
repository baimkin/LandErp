# Stage 1 — Procurement Core

Разрешён прямым запросом владельца 2026-09-14. Один активный checkpoint за раз.

| Checkpoint | Результат | Обязательные проверки |
|---|---|---|
| A (проверен) | Server/Application/Infrastructure/Worker, EF/PostgreSQL, design-time migration, health/error/config, tooling | Locked restore, Release 0 warnings, format, architecture, isolated real PostgreSQL clean/repeat apply, metadata comments, runtime no DDL, backup/restore, independent hosts, outage readiness |
| B (проверен) | Identity/Organization/permissions/scopes, UI Kit Razor shell и admin | Real authentication/negative permissions/visibility, PostgreSQL persistence, audit, critical UI states, responsive inspection |
| C (проверен) | Тонкий Collector server adapter + registration/heartbeat/work/lease/result; catalog | Existing Collector regressions, idempotency/duplicates/retry/lease fencing/presence/order/provenance, local usability, admin state |
| D (проверен) | Listing → PropertyCase, очередь/карточка, manager/head decisions, общие task/assignment/approval/timeline/notification | Forward/Return and authorization, concurrency, history/audit, restart persistence, full local control scenario, UI states |

## Файлы checkpoint A

`LandErp.slnx`, `global.json`, `Directory.Packages.props`, новые `src/LandErp.Server/`,
`src/LandErp.Application/`, `src/LandErp.Infrastructure/`, `src/LandErp.Worker/`,
`tests/LandErp.Foundation.Tests/`, `scripts/Test-Foundation.ps1`,
`scripts/Invoke-Migrations.ps1`, affected lock files, README/START_HERE/ACTIVE_TASK,
`STAGE-1_DATA_CONVENTIONS.md`, checkpoint report. Collector код не меняется в A.

Проверки не зависят от live Avito/Cian; live verification дополнительна.
Следующий checkpoint активируется только после успешных проверок предыдущего.

## Файлы checkpoint C

`src/LandErp.Collector.Contracts/` (только wire DTO/validation), public/domain
Collection/Catalog Application, owning Infrastructure mappings/services/migration,
Server Collector endpoints/admin Razor screen, Collector `ServerIntegration/`,
минимальная выборка одного local link для QueueRunner и отдельная вкладка WPF.
Tests: contract/idempotency/observation dedup/presence/order/lease/retry/outages,
полная существующая Collector regression suite, PG metadata и backup/restore.
Runtime grants только mutable current state и INSERT/SELECT history. Парсеры не
переносятся и не переписываются. Report/README/locks обновляются вместе с кодом.

## Файлы checkpoint D

Application Procurement use case + минимальные общие WorkTask/Assignment/
WorkflowStage/Transition/Approval/BusinessTimeline/Notification; owning mappings
и service Infrastructure, новая migration после review; Server queue/card/drawer/
timeline/dialog/filter states и authorized API. Tests: manager/head/scope negatives,
Forward/Return/approval, concurrency/revisions, timeline/audit/notification, restart,
реальный браузерный контрольный сценарий и PostgreSQL outage readiness. README,
Local tooling, ACTIVE_TASK и итоговый STAGE-1_REPORT обновляются с результатом.
