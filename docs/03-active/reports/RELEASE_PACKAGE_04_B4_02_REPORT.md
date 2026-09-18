# Release Package 04 — B4-02 report

**Finding:** minimal LR-18 operational health  
**Branch:** `codex/release-package-04`  
**Base:** `6543e91569b053e611899aa7d8b49935ed3f6fe5`

## Итог

Вместо новой observability-системы расширена текущая страница `/collectors`.

Она теперь сводит в один блок:
- Server;
- Database;
- Worker;
- Scheduler;
- Parser online/stale/offline;
- backlog;
- expired leases;
- storage.

Страница продолжает обновляться каждые 15 секунд.

## Источники сигналов

- Server: работа текущего Server UI;
- DB: существующий `IDatabaseStatus`;
- Worker/Scheduler: существующий `collection.scheduler_status`;
- Parser: текущие heartbeat records;
- backlog/expired: точные DB counts;
- storage: новый read-only `IFileStorageHealth`.

## Storage probe

Yandex probe не записывает test-object. Он делает metadata GET configured root не чаще раза в минуту на UI.

Missing root до первого upload допустим, потому что успешный 404 подтверждает сеть/auth/provider path. Auth/network/storage errors возвращаются safe code.

## Не добавлено

Нет новой health DB table, background monitor, metrics backend, alert manager, migration или отдельной ops page.

## Tests as code

Добавлен `ReleasePackageB402Tests.cs` для expired lease и storage probe semantics.

Фактические build/tests/browser: **Not run** по решению владельца.
