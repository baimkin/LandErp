# Release Package 02 — Correctable Business Data

**Branch:** `codex/release-package-02`  
**Package base:** `92e952d0ecce6b5235b4c5d13dc0040dc2c236fc`  
**B2-04 base:** `45c29428a40d79482ee5afd0a5ee029378339460`  
**Findings:** LR-13 + LR-14  
**Status:** implementation published; static review completed; executable/manual validation pending owner.

## Commit chain

| Gate | Commit | Результат |
|---|---|---|
| B2-01 | `c039600af0360e90a3731763f4380c8c06d0c779` | Audited correction пяти working PropertyCase facts |
| B2-02 | `d00fd7fd5d49bf1c83fe8d3608f14d357b7c64de` | Audited unlink/relink confirmed source relation |
| B2-03 | `45c29428a40d79482ee5afd0a5ee029378339460` | UI/UX correction flows в PropertyCase card |
| B2-04 | commit, содержащий этот отчёт | Static package review + owner validation handover |

Main не изменялся; история B2-01–03 не переписывалась.

## Бизнес-результат

Сотрудник может штатно исправить title/price/area/location/cadastral number без DB edit, duplicate Case или переписывания внешнего source. Менеджер может исправить ошибочную source relation: unlink либо relink к другому PropertyCase. Причина, история и concurrency являются обязательными частями обоих flows.

## Защита данных

- optimistic version checks;
- row locks;
- existing server-side permissions/scopes;
- filtered unique confirmed source relation;
- audit + business timeline;
- разделение Catalog external facts и PropertyCase working facts;
- stale UI recovery вместо silent overwrite.

## UI

Новых страниц нет: `Исправить рабочие данные` находится в «Основное», `Исправить связь` — на вкладке «Источники». Отдельная подсистема corrections не создавалась.

## Tests as code

- `ReleasePackageB201Tests.cs`;
- `ReleasePackageB202Tests.cs`;
- B2-03 шаги добавлены в `ProcurementUiScenario.RunPhase4Async`.

## P0 boundary

Static review подтверждает: working facts остаются PropertyCase-owned; Catalog facts остаются source-owned; source ownership хранится в PropertyCaseSourceLink; 0..N source links сохраняются; correction paths не возвращают Procurement к Listing-root.

## Не вошло

LR-15, LR-16, bulk corrections, merge/dedup, новые stages, post-purchase modules, новая permission model, migrations и новый corrections subsystem.

## Validation status

| Проверка | Статус |
|---|---|
| Static accumulated diff review | **Completed** |
| Scope / accidental changes review | **Completed** |
| P0 boundary review | **Completed** |
| Locked restore | **Not run** |
| Release build | **Not run** |
| PostgreSQL targeted tests | **Not run** |
| Browser B2-03 scenario | **Not run** |
| Manual owner acceptance | **Not run** |

Точные команды и ручной checklist находятся в `GATE-RELEASE-B2-04.md`.

До owner validation Package 02 нельзя считать Accepted или Production-ready. B2-04 не начинает Package 03 автоматически.