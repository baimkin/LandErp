# Stage 1 — Checkpoint D

Дата: 2026-09-14. Ветка: `codex/stage-1-procurement-core`.
A/B/C сохранены; D продолжен после C `719a1c6` по прямому разрешению владельца.

## Результат

Реализован Listing → PropertyCase. Менеджер принимает первичное решение,
сохраняет рабочие заметки и результат ручного контакта, передаёт выбранному
руководителю. Руководитель возвращает с причиной, указаниями, менеджером и
опциональным сроком либо одобряет дальнейшую работу, наблюдает или отклоняет.

Общие механизмы FP-004: WorkTask, Assignment, WorkflowStage/Transition, Approval,
BusinessTimeline, Audit Trail и внутренние notifications. Current state реляционный,
значимые факты append-only. Изменение source data после передачи требует обновить
анализ до одобрения. Expected case/data revision защищают от потерянных решений.
Scope/manager/head authorization проверяются service/Server независимо от UI.

UI: очередь карточек с фильтром/причиной/изменениями/неизвестными полями, Drawer,
рабочая карточка, reusable native Dialog, Timeline, SourceBadge и состояния.
Используются UIKit tokens и существующий локальный Bootstrap CSS без Bootstrap JS.
Desktop/tablet/mobile просмотрены по реальным Chrome screenshots.

## Файлы и решения

- Application: Procurement contracts/domain и минимальные общие Workflow models.
- Infrastructure: owning mappings, transactional ProcurementWorkspace, новые
  `20260914200643_ProcurementCore` и `20260914201752_ProcurementResponsibilityComment`.
  Применённые A/B/C migrations не изменены. Уточнение русского comment выполнено
  отдельной migration, а не переписыванием применённой.
- Server: authorized procurement API с CSRF, queue/card/dialog/drawer/timeline,
  уведомления в обзоре и навигация.
- LocalSetup/runtime grants: SELECT/INSERT history, mutable current state,
  read-only `--inspect` с всегда откатываемой DDL probe.
- Readiness требует точного набора migrations, а не только наличия history table.
- Tests: scoped negatives, manager/head actions, return details/due, revisions,
  timeline/audit/notifications, actual browser flow, restart и backup facts.

## Фактические проверки

В командах использован `artifacts/stage1/dotnet/dotnet.exe` (SDK 10.0.112).
Credentials читались только из приватного окружения/ignored settings.

| Команда / проверка | Результат |
|---|---|
| `dotnet restore LandErp.slnx --locked-mode` | success |
| `dotnet build LandErp.slnx -c Release --no-restore` | 0 warnings, 0 errors |
| `dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --no-restore` | 10/10, 2m27s |
| `dotnet test tests/LandErp.ParserSpike.Tests -c Release --no-build --no-restore --filter 'TestCategory!=Live'` | 85/85, 27s |
| targeted PostgresTests/CollectorIntegrationTests after readiness fix | 3/3, 43s |
| `dotnet list LandErp.slnx package --vulnerable --include-transitive` | no vulnerable packages reported |
| `dotnet format whitespace LandErp.slnx --no-restore --verify-no-changes --include <changed C# files>` | success |
| `scripts/Initialize-Local.ps1 -DotnetPath ./artifacts/stage1/dotnet/dotnet.exe` | explicit Local apply success |
| `dotnet run --project src/LandErp.LocalSetup -c Release --no-build -- --inspect` | PostgreSQL 18.6; migrations 6; tables/comments 32/32; runtime DDL denied 42501 |
| Local `/health/live`, `/health/ready` | HTTP 200 / 200 |
| `git diff --check`; tracked local-data/cookies/credentials/SQLite/DB inspection | clean; no local data or secrets tracked |

Real PostgreSQL tests: clean/repeated apply, partial schema readiness false,
zero/reapply, Russian TABLE/COLUMN comments compared with model metadata,
no runtime DDL/delete of facts, backup/restore catalog/audit/workflow/case facts,
separate hosts and PostgreSQL outage ready503/live200. Browser scenario used
isolated PostgreSQL, actual HTTPS Server and employee cookies; manager/head
Forward/Return/Approve, admin API negative, CSRF negative and empty queue passed.

Найденные проблемы устранены: неверное имя policy в новых Razor страницах;
неустойчивое ожидание вложенного dialog в UI test; comparison PostgreSQL
varchar[]/text[] в усиленном readiness. Итоговый полный прогон зелёный.
Промежуточная сборка во время работающего testhost встретила Windows file lock;
последующая самостоятельная Release-сборка прошла без warnings.

## Ограничения

Live Avito/Cian smoke в этом срезе не выполнялся; контрольный Collector использует
существующий local runner с контролируемым source adapter и настоящий HTTPS/PG.
Парсеры проверены офлайн и сохранены. CAPTCHA/ограничения источников ручные.
Сбор/heartbeat/retry требуют открытого Collector. Каталог содержит основные
публичные поля; diagnostics остаются в Collector. Роли/permissions системные,
назначения/scopes изменяемые; нет workflow designer или полноценного Due Diligence.
Stage 2 не начат. Известных blocker/critical defects в реализованном scope нет.
