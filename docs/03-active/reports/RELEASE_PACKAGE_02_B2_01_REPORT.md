# Release Package 02 — B2-01 report

**Finding:** LR-13 — audited correction рабочих PropertyCase facts  
**Branch:** `codex/release-package-02`  
**Base:** `92e952d0ecce6b5235b4c5d13dc0040dc2c236fc`

## Реализация

Добавлена одна явная correction-команда для одного выбранного рабочего факта:
- Title/Location/CadastralNumber используют текстовое значение;
- Price/Area используют decimal;
- nullable рабочие факты можно штатно очистить;
- reason обязателен;
- `ExpectedCaseVersion` проверяется после row lock.

Использованы существующие механизмы вместо новой инфраструктуры:
- `RequireDossierPermissionAsync` + текущий case visibility для server-side access;
- `PropertyCase.Version` для optimistic concurrency;
- append-only `BusinessTimeline`;
- общий append-only `AuditEvent` с before/after/reason/correlation id.

Отдельная таблица corrections и migration не добавлялись: это дублировало бы уже существующий Audit Trail.

## Защита source-of-truth

Correction изменяет только рабочие поля `PropertyCase`. Catalog item, observations, source link и source revisions не записываются. После correction существующая discrepancy logic сравнивает source с новым рабочим значением как и раньше.

## Targeted tests добавлены

`ReleasePackageB201Tests.cs` фиксирует:
1. исправление всех пяти полей;
2. source остаётся неизменным;
3. timeline и audit содержат before/after/reason;
4. stale version не перетирает новую correction;
5. reason обязателен;
6. неавторизованный subject отклоняется сервером;
7. nullable working fact можно очистить штатно.

## Проверки

По прямому решению владельца:
- restore: **Not run**;
- build: **Not run**;
- PostgreSQL tests: **Not run**;
- browser/manual: **Not run**.

Это не трактуется как Passed. Фактический прогон и пользовательскую приёмку выполняет владелец отдельно.

## Не затронуто

LR-14 unlink/relink, UI B2-03, migrations, новая permission-модель, source data и последующие packages не начинались.
