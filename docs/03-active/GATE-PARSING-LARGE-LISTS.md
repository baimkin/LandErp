# PARSING-LARGE-LISTS

16.11.2026 владелец разрешил: зоны/поиски без страниц с внутренней прокруткой;
история по 50 с «Загрузить ещё»; сохранение фильтров и позиции при вкладках,
редактировании и автообновлении. Mobile — обычная вертикальная прокрутка страницы.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
PARSING-WORKSPACE Gate/report, UI instructions/consistency/tokens, screen07/v2,
FP-004 §§3,9,12–15 → Collectors/CollectionAdministration/contracts/tests.
Документы неизменны; исходный reference не переписывать.

Scope: реальные списки, закреплённая шапка, фильтры над таблицей, счётчики отбора;
серверная история по 50 с устойчивым курсором и фильтрами по всей истории,
а не только последним 100; обновление загруженного окна без потери контекста.
Состояние браузера разделяется по организации/сотруднику; недоступное storage
не блокирует работу. Права и область организации проверяются сервером.

Файлы: CollectionServices.cs; CollectionAdministration.cs / новый CollectionHistory.cs;
Collectors.razor/.razor.cs/.razor.css/.Workspace.cs, partial Lists.cs и scoped JS;
адресные .NET/JS/browser tests, обновление существующего browser flow,
screen07, этот Gate, ограниченная запись ACTIVE_TASK, отчёт
reports/PARSING_LARGE_LISTS_REPORT.md. Текущая ветка сохраняется.
Без migrations/packages/Parser, commit/push и production. Другие работы сохранить.

Проверки: Release в отдельном output без остановки существующего local preview;
PostgreSQL: >100 записей, одинаковое время, новые записи между порциями,
серверные фильтры и изоляция/права; JS: storage/scroll/anchor;
реальный browser: десятки зон/поисков, 50/100/150 истории, вкладки/формы/таймер,
desktop/tablet/mobile. Снимки просмотреть. Следующий Gate не начинать.

Результат: реализовано и проверено локально. Release 0 warnings/errors,
32/32 адресных .NET теста и 11 JS случаев Passed. Настоящий browser на 70 зонах,
120 поисках, истории 50/100/150, с правкой, обновлением, сохранением строки и
фильтров; desktop/tablet/mobile снимки просмотрены. Production и публикации нет.
Отчёт reports/PARSING_LARGE_LISTS_REPORT.md, evidence artifacts/parsing-large-lists/.
