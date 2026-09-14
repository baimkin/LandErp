# Stage 1 — Procurement Core

Разрешён прямым запросом владельца 2026-09-14. Один активный checkpoint за раз.

| Checkpoint | Результат | Обязательные проверки |
|---|---|---|
| A (активный) | Server/Application/Infrastructure/Worker, EF/PostgreSQL, design-time migration, health/error/config, tooling | Locked restore, Release 0 warnings, format, architecture, isolated real PostgreSQL clean/repeat apply, metadata comments, runtime no DDL, backup/restore, independent hosts, outage readiness |
| B | Identity/Organization/permissions/scopes, UI Kit Razor shell и admin | Real authentication/negative permissions/visibility, PostgreSQL persistence, audit, critical UI states, responsive inspection |
| C | Тонкий Collector server adapter + registration/heartbeat/work/lease/result; catalog | Existing Collector regressions, idempotency/duplicates/retry/lease fencing/presence/order/provenance, local usability, admin state |
| D | Listing → PropertyCase, очередь/карточка, manager/head decisions, общие task/assignment/approval/timeline/notification | Forward/Return and authorization, concurrency, history/audit, restart persistence, full local control scenario, UI states |

## Файлы checkpoint A

`LandErp.slnx`, `global.json`, `Directory.Packages.props`, новые `src/LandErp.Server/`,
`src/LandErp.Application/`, `src/LandErp.Infrastructure/`, `src/LandErp.Worker/`,
`tests/LandErp.Foundation.Tests/`, `scripts/Test-Foundation.ps1`,
`scripts/Invoke-Migrations.ps1`, affected lock files, README/START_HERE/ACTIVE_TASK,
`STAGE-1_DATA_CONVENTIONS.md`, checkpoint report. Collector код не меняется в A.

Проверки не зависят от live Avito/Cian; live verification дополнительна.
Следующий checkpoint активируется только после успешных проверок предыдущего.
