# Release Package 02 — B2-03 report

**Scope:** UI/UX correction flows for LR-13 + LR-14  
**Branch:** `codex/release-package-02`  
**Base:** `d00fd7fd5d49bf1c83fe8d3608f14d357b7c64de`

## Что реализовано

### Working facts

В существующей карточке PropertyCase добавлено одно компактное действие `Исправить рабочие данные`.

Модальное окно:
- работает со всеми пятью B2-01 полями;
- показывает current → proposed value до сохранения;
- позволяет очистить nullable price/area/location/cadastral;
- требует reason;
- использует captured CaseVersion;
- вызывает существующий `CorrectCaseFactAsync`;
- не закрывается при rejected/stale operation;
- даёт явное `Обновить данные`, которое закрывает stale form и перечитывает карточку.

### Source relation

На вкладке «Источники» добавлено `Исправить связь`.

При открытии только этого окна UI адресно получает:
- свежую Catalog version;
- доступные существующие PropertyCase через уже существующий `ReadLinkTargetsAsync`.

Окно показывает current → target, обязательную причину и два режима:
- без target — unlink;
- с target — relink.

Команда использует уже реализованный B2-02 `CorrectCaseLinkAsync`. Source data не редактируется.

### Permission UX

Чтобы не показывать Head действие, которое B2-02 разрешает только Manager, `CaseCard` получил один read-only флаг `CanCorrectSourceLinks`. Это не новое право: значение напрямую вычисляется из существующего `ManagerDecide`.

## Почему не сделан отдельный экран

Correction — редкое контекстное действие над конкретным PropertyCase/source. Отдельная страница и новая навигация увеличили бы объём и дублировали уже существующую карточку без бизнес-пользы.

## Tests as code

Existing `ProcurementUiScenario.RunPhase4Async` расширен:
1. открывает working-fact correction;
2. проверяет before/after;
3. сохраняет correction с reason;
4. открывает source-link correction;
5. проверяет current/target UX и историческое предупреждение.

## Проверки

По решению владельца:
- restore: **Not run**;
- build: **Not run**;
- PostgreSQL tests: **Not run**;
- browser scenario: **Not run**;
- manual visual acceptance: **Not run**.

Наличие test code не трактуется как Passed.

## Не затронуто

LR-15, migrations, backend semantics B2-01/B2-02, bulk/merge/dedup, Package 03 и общий redesign не начинались.
