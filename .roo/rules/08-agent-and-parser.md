# LandErp — Agent и адаптер источника

- Первый поддерживаемый Agent — Windows x64; протокол и ядро остаются переносимыми.
- Agent общается с Server только по версионированному HTTPS API и не знает PostgreSQL.
- Результат сначала записывается в локальный SQLite outbox, затем отправляется идемпотентно.
- Очередь использует lease, heartbeat, retry, AttentionRequired и dead-letter.
- Парсинг выдачи и подробной карточки — разные типы задания и контракта.
- DOM-селекторы и признаки страниц находятся только в source adapter.
- Сначала классифицировать SearchResults/Details/Auth/Captcha/RateLimit/Unknown,
  затем извлекать бизнес-поля.
- Пустая или неизвестная страница не означает снятие объявлений.
- CAPTCHA и блокировки не обходить; остановиться и запросить внимание человека.
- Raw/Parsed/Assertion не смешивать; каждое поле имеет presence и provenance.
- Изменение адаптера требует fixture и contract tests.

