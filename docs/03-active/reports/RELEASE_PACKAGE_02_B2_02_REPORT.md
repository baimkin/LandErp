# Release Package 02 — B2-02 report

**Finding:** LR-14 — unlink/relink ошибочной confirmed source relation  
**Branch:** `codex/release-package-02`  
**Base:** `c039600af0360e90a3731763f4380c8c06d0c779`

## Реализация

Добавлена одна correction-команда Catalog → PropertyCase:
- проверяет `ExpectedCatalogVersion`;
- проверяет ожидаемый текущий `ExpectedCaseId`;
- требует обязательную причину;
- требует существующий `ManagerDecide`;
- блокирует Catalog row на время correction;
- сохраняет old relation как `Confirmed=false`;
- при relink создаёт новую либо реактивирует существующую historical pair;
- существующий обычный link-flow также реактивирует historical pair вместо duplicate INSERT;
- unique filtered index остаётся последней DB-защитой от двух confirmed relations.

Schema/migration не понадобились.

## Состояние Catalog item

Чистый unlink возвращает item в `Incoming` и ставит attention, потому что `InWork` без PropertyCase был бы противоречивым состоянием.

Relink оставляет item в `InWork` и снимает attention. Source facts, observations и DataRevision не переписываются; меняется только operational Version/ChangedAt и relation state.

## История

- старая relation row физически не удаляется;
- old Case получает timeline `SourceUnlinked`;
- target Case при relink получает timeline `SourceRelinked`;
- append-only audit `PropertyCaseSourceLinkCorrected` хранит CatalogItemId, from/to и reason.

## Targeted tests добавлены

`ReleasePackageB202Tests.cs` фиксирует:
1. relink переносит единственную confirmed связь и сохраняет old relation;
2. unlink возвращает source во входящие;
3. stale correction не создаёт две confirmed связи;
4. relink обратно реактивирует historical pair без duplicate row;
5. обычный link-flow после unlink не ломается и реактивирует historical pair;
6. причина обязательна;
7. операция требует server-side Manager permission.

## Проверки

По решению владельца:
- restore: **Not run**;
- build: **Not run**;
- PostgreSQL tests: **Not run**;
- browser/manual: **Not run**.

Это не трактуется как Passed.

## Не затронуто

B2-03 UI/UX, LR-15 target search/paging, migrations, source facts, merge/dedup и Package 03 не начинались.
