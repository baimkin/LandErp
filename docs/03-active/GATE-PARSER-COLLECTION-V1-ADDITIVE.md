# Parser — additive Collection V1 и факты полноты

Статус: завершён 18 сентября 2026 года.
Ветка: `codex/parser-additive-contract`. База: `ee8b5f3`.

## Required reading

- README, START_HERE, ACTIVE_TASK;
- `docs/05-collection/COLLECTOR_SERVER_PROTOCOL_V1.md`;
- ADR-007;
- текущие Parser Core, Server adapter/outbox и небраузерные тесты.

## Scope

- поддержать `Partial`, `ReasonCode`, `Warnings`, `Coverage`;
- завершать динамическую выдачу по фактическому концу, окончанию загрузки и
  стабильным раундам без новых уникальных карточек;
- считать source count только подсказкой;
- сохранять и доставлять наблюдения при любом финальном исходе;
- сохранить размер порции, идемпотентный outbox, lease fencing и обработку
  `RESULT_SUPERSEDED`;
- после подтверждённого final возвращаться в auto-claim loop;
- не менять Local mode без необходимости и принимать старые payload V1.

Планируемые файлы: Parser local completion facts/runner/store, server result
classifier/coordinator/outbox, небраузерные unit/contract tests, этот Gate и отчёт.
Новых пакетов, production migrations, browser tests, merge и push нет.

Проверки: Release build Parser Desktop, целевые unit/contract tests и полный
небраузерный Parser suite.

Результат: [PARSER_COLLECTION_V1_ADDITIVE_REPORT.md](reports/PARSER_COLLECTION_V1_ADDITIVE_REPORT.md).
