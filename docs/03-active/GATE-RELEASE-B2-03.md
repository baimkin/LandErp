# GATE-RELEASE-B2-03 — UI/UX correction flows

**Package:** Release Package 02 — Correctable Business Data  
**Scope:** UI integration of B2-01 + B2-02  
**Ветка:** `codex/release-package-02`  
**Исходный commit:** `d00fd7fd5d49bf1c83fe8d3608f14d357b7c64de`

## Цель

Сделать уже реализованные correction-команды реально используемыми из существующей карточки PropertyCase без отдельного экрана и без расширения backend scope.

## Рабочие факты

В «Основное → Об объекте» появляется одно действие `Исправить рабочие данные`.

Окно:
- выбирает одно из пяти полей;
- показывает `Сейчас` и `После исправления`;
- использует подходящий text/decimal input;
- требует причину;
- явно сообщает, что source data не переписывается;
- при stale/concurrency ошибке оставляет форму открытой и предлагает `Обновить данные`.

## Source unlink / relink

На вкладке «Источники» у manager с существующим `ManagerDecide` появляется `Исправить связь`.

Окно:
- показывает текущий PropertyCase и целевой результат;
- target cases загружаются только при открытии окна;
- `без target` означает unlink и возврат источника во входящие;
- target Case означает relink;
- причина обязательна;
- явно сообщает, что старая relation остаётся в истории;
- stale/concurrency ошибка не закрывает окно и предлагает refresh.

## Права

Новая permission не создаётся.

В read model добавляется только `CanCorrectSourceLinks`, который является проекцией уже существующего `ManagerDecide`. Fact correction использует существующий `CanManageDossier`.

## Вне scope

- LR-15 search/paging;
- новый экран corrections;
- bulk actions;
- новая permission/role;
- migration/schema;
- redesign PropertyCase;
- фактический запуск browser/build/tests.

Existing Phase 4 browser scenario расширяется шагами B2-03 как исполняемый test code, но в этой задаче не запускается.
