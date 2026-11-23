# PARSER-LIST-AREA — Крекшино

05.11.2026 владелец утвердил последовательное исправление ошибок. Первый этап:
восстановить сбор Avito списка с drawId без требования map geometry; затем
контрольный запуск той же ссылки владельцем. Следующие ошибки пока не исправлять.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ранее
прочитанные ADR-007, MAP-SELECTION → affected Collector/tests.
Текущая ветка codex/inspection-file-improvements, накопленное сохранить.

Список: источник декларирует тот же drawId в видимых map links, неизменны
фильтры/категория/metro/пагинация; подтверждение проверяется при каждом read и
финальном контроле. Это декларация области источником, не pixel/polygon proof.
Не включать карту, не получать геометрию отдельными source-запросами. Карта
сохраняет geometry/selection/point-in-polygon guard, включая ссылки без drawId.
Смена map→list не понижает требования уже начатого map run.

Файлы: QueueRunner.cs, DomSourcePage.cs/AvitoSearchIdentity.cs при необходимости,
адресные list-area tests/fixtures, README, ACTIVE_TASK/Gate/отчёт.
Проверки: Release + offline unit/contract/DOM script. Новых пакетов, live source,
Browser/Server/API/schema, Git publication нет. Рабочую сборку обновить после
закрытия Parser с backup/SHA-256 и сохранением данных; не запускать автоматически.
Отчёт reports/PARSER_LIST_AREA_REPORT.md. После этого ждать Крекшино от владельца.
