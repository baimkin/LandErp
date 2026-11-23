# PARSER-MAP-SELECTION

03.11.2026 прямой запрос: доработать и тестировать парсинг по двум сохранённым
запускам. Активный scope этого чата: Avito карта требует текущую геометрию,
неизменные фильтры, загруженную видимую карту и активное выделение. Ссылки без
drawId не обходят проверку. Старый эталон не разрешает данные после сброса.
Неподтверждённые карточки только в диагностике, ранее проверенные сохраняются.
Это проверка состояния выделения, не гарантия видимости каждого пикселя canvas.
Сравнение старого/new drawId не становится условием допуска.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ранее
прочитанные ADR-007 и предыдущий diagnostic Gate → affected Collector/tests.
Файлы: Models, SearchAreaGuard, DomSourcePage, QueueRunner, AvitoMapCapture
(при необходимости), соответствующие tests/fixtures, README, Gate/отчёт.
Проверки: Release и адресные небраузерные unit/contract tests; offline browser
fixture разрешена, внешняя сеть запрещена. Не запускать live/production, не менять
Server/Cian/list mode/outbox contract, пакеты, Git history. Обновить рабочую
сборку с backup/hash preservation, не запускать автоматически.
