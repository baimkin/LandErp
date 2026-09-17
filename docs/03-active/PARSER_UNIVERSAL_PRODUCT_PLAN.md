# Universal Parser — план реализации

**Разрешение владельца:** 17 сентября 2026 года.
**Ветка:** `codex/universal-parser`.
**Правило:** выполнять checkpoints по порядку; working browser/parser pipeline не переписывать.

1. **Контракт:** protocol V1, optional heartbeat/progress, единый код подключения, server search DTO и точные ошибки.
2. **Local workspace:** группы и Manual/Interval/FixedTimes schedules; создание ссылки из настроенного браузера.
3. **Server workspace:** подключение одним кодом, groups/searches/schedules через Server; локальную ссылку добавлять отдельно с выбором server group и подтверждением. Локальные группы и история не переносятся.
4. **Автономная работа:** reconnect/register/claim/parse/deliver loop, один активный Job, общая блокировка Local/Server.
5. **Recovery:** точные Problem Details, heartbeat progress и Ready, durable partitioned outbox, lease reconciliation.
6. **UX и lifecycle:** простой переключатель режима, понятные состояния, корректное закрытие/восстановление; технические IDs только в диагностике.
7. **Проверка:** offline Parser tests, server contract/integration/concurrency tests на disposable PostgreSQL, Release build; live источники и production migration apply отдельно.

Каждый checkpoint завершается фактическими проверками и коротким отчётом. Следующий checkpoint не расширяет предыдущий задним числом.
