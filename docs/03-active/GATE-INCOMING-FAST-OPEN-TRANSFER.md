# INCOMING-FAST-OPEN-TRANSFER

20.11.2026 владелец разрешил сначала исправить тормоза открытия карточки и передачи
менеджеру во входящих. Разбор production incident 01a1f951-dafb-7209-bf62-431a9dbeeaf8
не подменяется догадкой о причине; основная доказанная лишняя работа — полный
incoming/facet/market read после RegisterView и Transfer.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
GATE-INCOMING-QUICK-DISMISS / MANAGER-TRANSFER и их отчёты, screen01/UI/tokens,
FP-004 §§3,9,12–15 → IncomingCatalogV2/partials, incoming contracts/read/selection,
WorkspaceComponent, RegisterView/Transfer, тесты. Неизменившееся не перечитывать.

Scope: прежние серверные команды/права/история; открытие без общего list/facets/
market read, точечная отметка просмотра с учётом New; локальное состояние загрузки,
ошибка чтения/повтор без повторной бизнес-записи, защита от позднего ответа.
После подтверждённой передачи — обновление затронутых строк, Total/выбора/пагинации,
учёт связанной группы источников (включая другие страницы), лёгкий refill/window
без полного пересчёта фильтров/рынка. Сохранить детали переданного объявления,
прокрутку, фильтры/сортировку. Rejected/Unknown не скрывают строку и выбор получателя.
Сбой post-commit read не предлагает повторять подтверждённую передачу.

Файлы: IncomingCatalogV2.razor и локальные partial/css, IncomingCatalogReadContracts,
IncomingCatalogReadService; выделение общих update helpers из Dismiss partial;
новые/адресные UI+PostgreSQL+browser tests, минимальная оснастка существующих tests;
screen01, этот Gate/ACTIVE_TASK, reports/INCOMING_FAST_OPEN_TRANSFER_REPORT.md.
Без packages/schema/переписывания команд или прав, Parser, логов production,
commit/push/deploy/перезапуска preview. Текущую ветку/индекс/накопленное сохранить.

Проверки: Release отдельно; запрет полных read/market в тестах целевых действий,
карточка/просмотр/права, фильтр New, ошибка/отмена/поздний ответ, передача/группа
между страницами/границы 25/50/100/последняя страница, Total/выбор/скролл,
подтверждённая запись и сбой обновления, actual localhost browser и одноразовая БД.
Отчёт о командах/результатах/ограничениях; production latency не заявлять измеренной.

Локально завершён 20.11.2026. Release Passed; 9/9 целевых PostgreSQL/UI/browser
проверок Passed. Отчёт reports/INCOMING_FAST_OPEN_TRANSFER_REPORT.md.
Git/production/preview/права/схема не менялись. Следующий Gate не начат.
