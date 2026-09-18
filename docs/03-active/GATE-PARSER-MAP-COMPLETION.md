# Parser — результат доставки и прогресс карты

Статус: завершён 18 сентября 2026 года.
Ветка: `codex/parser-map-completion`.

## Scope

- перенести на актуальный `main` только полезную часть незавершённой работы Parser;
- вернуть Server в receipt количество новых и изменённых объявлений;
- сохранить receipt в локальной очереди Parser и показать понятный итог доставки;
- показывать для заданий карты прогресс по объявлениям, а не по страницам;
- сохранить совместимость Parser со старым Server, у которого новых полей receipt ещё нет;
- не переносить устаревшие версии `QueueRunner`, `SearchUrls` и правил завершения карты.

Затрагиваемые области: Collector V1 contracts, Collector gateway, Parser outbox,
Server coordinator, Parser workspace и адресные тесты. Новых пакетов и production
migrations нет. `ACTIVE_TASK.md` не изменяется, чтобы работа не конфликтовала с B2.

Проверки: Release build всего решения, адресные Parser tests, адресный PostgreSQL
Collection test и `git diff --check`.

Результат: [PARSER_MAP_COMPLETION_REPORT.md](reports/PARSER_MAP_COMPLETION_REPORT.md).
