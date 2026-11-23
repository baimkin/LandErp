# PARSER-MAP-BATCHES-PAGINATION

05.11.2026 владелец разрешил исправить Долгопрудный и Архангельское до ручной
перепроверки всех трёх ссылок. Один этап: читать динамические map items после
десятой порции независимо от page limit; не терять явные source exclusions;
распознавать новую pagination-button разметку, не перескакивать страницы и
правильно завершать список по настроенному лимиту.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ранее
прочитанные ADR-007/LIST-AREA/MAP-SELECTION → affected Collector/tests.
Файлы: AvitoMapCapture, DomSourcePage, Models/SearchAreaGuard, QueueRunner,
адресные tests/fixtures, README, Gate/ACTIVE_TASK/отчёт. Новых пакетов нет.
Map geometry/selection/coordinates/filter guard, Partial сохранение полезных
данных, outbox/fencing/25 observations/ResultId сохраняются. Нельзя принимать
неподтверждённые координаты или исправлять next URL догадкой. Если до лимита нет
перехода на n+1, останавливать с диагностикой; выход за лимит не выполнять.
Проверки: offline unit/contract/DOM script, Release, установка рабочей сборки с
backup/hash preservation. Без browser/live source/production/Git publication.
Отчёт reports/PARSER_MAP_BATCHES_PAGINATION_REPORT.md. Затем ручной контроль
владельцем Крекшино/Долгопрудного/Архангельского; без новых автоматических задач.
