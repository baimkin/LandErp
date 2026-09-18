# GATE-RELEASE-B2-02 — Source unlink / relink correction

**Package:** Release Package 02 — Correctable Business Data  
**Finding:** LR-14  
**Ветка:** `codex/release-package-02`  
**Исходный commit:** `c039600af0360e90a3731763f4380c8c06d0c779`

## Цель

Дать штатный исправляющий путь для ошибочной confirmed связи Catalog item → PropertyCase без destructive delete и без нарушения уникальности подтверждённой связи.

## Минимальная модель

Новая таблица истории не нужна:
- текущий `PropertyCaseSourceLink` имеет `Confirmed`;
- filtered unique index разрешает не более одной confirmed связи на Catalog item;
- уникальность пары `PropertyCaseId + CatalogItemId` сохраняется;
- старая связь остаётся физической строкой с `Confirmed=false`;
- повторный relink к исторической паре реактивирует существующую строку вместо создания дубля;
- обычный существующий `TakeToWork(... ExistingCaseId)` также реактивирует historical pair, поэтому unlink не ломает штатный link-flow;
- correction history фиксируется append-only audit + Business Timeline.

## Поведение

- команда принимает Catalog item, ожидаемую Catalog version, ожидаемый текущий Case, опциональный target Case и обязательную причину;
- Catalog row lock сериализует correction одного source;
- несовпадение версии или текущего confirmed Case трактуется как stale concurrency;
- from/target Case обязаны быть доступны сотруднику;
- unlink без target возвращает Catalog item в `Incoming` и включает attention;
- relink оставляет Catalog item в `InWork`;
- исходные Catalog facts/observations не изменяются;
- старый link не удаляется;
- audit хранит from/to/reason, timeline отражает unlink и relink.

## Вне scope

- LR-15 target search/paging;
- B2-03 UI/UX;
- merge/dedup cases;
- bulk relink;
- новая permission-модель;
- migration/schema change;
- build/tests/browser execution.

Фактические проверки выполняет владелец отдельно; наличие targeted tests не считается Passed.
