# История задач до синхронизации main 23.11.2026

Это архив прежнего ACTIVE_TASK, а не разрешение выполнять перечисленные задачи. Текущий scope задаёт [ACTIVE_TASK.md](ACTIVE_TASK.md). Исходная локальная история сохранена полностью; отсутствовавшие в ней исторические сведения опубликованного main добавлены в конце.

---

# Задача этого чата — PARSER-MAP-MFE

16.11.2026 разрешена additive поддержка новой карты Avito.
Required reading: README → START_HERE → ACTIVE_TASK → GATE-PARSER-MAP-MFE.md
→ ранее прочитанный MAP-PINS/ATTENTION → DomSourcePage/tests.
Статус: реализовано offline. Отчёт reports/PARSER_MAP_MFE_REPORT.md.
17.11 разрешена готовая карта при interactive. Остальные gates сохранены.
207/207 .NET тестов, 36/36 JS сценариев прошли, Release без ошибок.
Staging artifacts/parser-map-interactive/local-update/ready.
Владелец закрыл Parser, рабочая сборка обновлена с backup.
155 файлов сверены, 259 защищённых файлов без изменений.
17.11 добавлено MapSelection в last-error.json обычного сбора:
последний Read и повторная диагностическая проверка при остановке.
207/207 тестов и 28/28 JS сценариев Passed. Release без warnings/errors.
Staging artifacts/parser-error-selection/local-update/ready.
Владелец закрыл Parser, рабочая сборка обновлена с backup.
155 файлов сверены, 256 защищённых файлов без изменений. Ожидается обычный сбор Иславского.
17.11 добавлен selection-state.json с результатом именно рабочего скрипта
и причинами отказа по элементам. 204/204 тестов и 28/28 JS сценариев прошли.
Release без предупреждений/ошибок. Рабочая сборка обновлена с backup,
248 защищённых файлов без изменений. Следующий шаг — диагностика Иславского.
203/203 адресных тестов и 17/17 новых JS сценариев прошли, Release без ошибок.
Рабочая сборка обновлена, 155 hashes verified, 4 файла с backup.
235 защищённых файлов без изменений. Далее ручной контроль Иславского.
Offline tests/Release/рабочая сборка с backup/hash разрешены. Parser закрыт.

---
# Предыдущая задача этого чата — PARSER-ATTENTION

Прямой запрос 12.11.2026: уведомления о ручной остановке.
Required reading: README → START_HERE → ACTIVE_TASK → GATE-PARSER-ATTENTION.md
→ Desktop/Runner/tests. Отчёт reports/PARSER_ATTENTION_REPORT.md.
Scope: плашка, Windows notification, напоминание, открытие вкладки.
Статус: реализовано offline. 203/203 адресных тестов и финальные 4/4 прошли.
Release без предупреждений и ошибок. Готовая сборка в artifacts/parser-attention/
local-update/ready. Владелец закрыл Parser, рабочая сборка обновлена.
155 файлов сверены, 4 заменены с backup, 206 защищённых файлов без изменений.
Визуальная проверка Windows notification ожидается при следующей ручной остановке.

---
# Предыдущая задача этого чата — PARSER-MAP-PINS

10.11.2026 владелец разрешил поддержку Avito pins и markers, тесты и обновление
рабочей сборки. Required reading: README → START_HERE → ACTIVE_TASK →
GATE-PARSER-MAP-PINS.md → ранее прочитанный MAP-LOADING → Collector/tests.
Отчёт reports/PARSER_MAP_PINS_REPORT.md. Статус: реализовано offline.
Добавлены Structure и Rejection для диагностики drawArea.
Release без предупреждений и ошибок, 34/34 адресных тестов прошли.
Нагрузочный SQLite тест прервался из-за недостатка места.
Рабочая сборка обновлена с резервной копией. Ожидается диагностика Дубков.

---
# Предыдущая задача этого чата — PARSER-MAP-LOADING

05.11.2026 прямой запрос: исправить Апрелевку по сохранённой диагностике,
прогнать тесты и обновить рабочую сборку. Gate GATE-PARSER-MAP-LOADING.md.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ранее
прочитанный MAP-BATCHES-PAGINATION → Collector/tests. Текущая ветка сохраняется.
Без Server/schema/packages/Git publication/live browser. Parser закрыт владельцем.
Отчёт reports/PARSER_MAP_LOADING_REPORT.md. Статус: реализовано offline.
187/187 адресных тестов и 9/9 JS проверок Passed. Release 0 warnings/errors.
Рабочая сборка обновлена: 155 hashes verified, 4 файла с backup,
128 защищённых файлов без изменений. Ожидается ручной запуск Апрелевки владельцем.

---
# Предыдущая задача этого чата — PARSER-MAP-BATCHES-PAGINATION

05.11.2026 прямой запрос владельца: исправить также Долгопрудный и Архангельское,
затем проверить все три ссылки. Gate GATE-PARSER-MAP-BATCHES-PAGINATION.md.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ранее
прочитанные ADR-007/LIST-AREA/MAP-SELECTION → affected Collector/tests.
Текущая ветка/накопленное сохраняются. Scope: ответы карты после 10-й порции,
source exclusions, новая разметка пагинации, корректный предел страниц.
Offline tests/Release/рабочая сборка с backup разрешены; live выполняет владелец.
Без новых пакетов/Server/schema/Git publication. Отчёт
reports/PARSER_MAP_BATCHES_PAGINATION_REPORT.md. Статус: реализовано offline.
185/185 адресных проверок и 11/11 сценариев пагинации Passed; Release без
предупреждений и ошибок. Рабочая сборка обновлена: 155 hashes verified,
4 файла с backup, 125 защищённых файлов без изменений. Live контроль владельцем
ещё не выполнен: Крекшино → Долгопрудный → Архангельское, по одной ссылке.

---
# Предыдущая задача этого чата — PARSER-LIST-AREA

05.11.2026 прямой запрос: исправлять ошибки последовательно, первым Крекшино.
Gate GATE-PARSER-LIST-AREA.md. Required reading: README → START_HERE → ACTIVE_TASK
→ этот Gate → ранее прочитанные ADR-007/MAP-SELECTION → affected Collector/tests.
Scope: список с drawId подтверждается источником без map geometry; strict filters,
page/final checks, карты по прежнему guard. Следующие ошибки не начинать.
Release/offline tests и обновление рабочей сборки с backup разрешены; live запуск
делает владелец. Ветка текущая, накопленное/параллельное сохранить. Без Server,
новых пакетов, Git publication. Отчёт reports/PARSER_LIST_AREA_REPORT.md.
Статус PARSER-LIST-AREA: исправлено offline, ожидается контроль Крекшино владельцем.
Release 0 warnings/errors, 180/180 адресных проверок Passed. Рабочая сборка
обновлена: 155 hashes verified, 4 изменённых файла с backup, 125 защищённых
файлов без изменений. Parser автоматически не запускался; следующий этап не начат.

---
# Предыдущая задача этого чата — PARSER-MAP-SELECTION

03.11.2026 владелец разрешил доработать и тестировать парсинг после сравнения
двух диагностик. Gate GATE-PARSER-MAP-SELECTION.md; Required reading: README →
START_HERE → ACTIVE_TASK → этот Gate → ранее прочитанные ADR-007/diagnostic Gate
→ затронутые Collector/tests. Текущую ветку и параллельные изменения сохранить.
Scope: текущая геометрия + состояние выделения + фильтры, карта без drawId,
проверка границ и сброса во время сбора. Offline tests/Release и обновление
рабочей сборки с backup разрешены, автоматический/live запуск запрещён.
Отчёт reports/PARSER_MAP_SELECTION_REPORT.md. Статус: реализовано offline.
Release 0 warnings/errors, 170/170 Parser/contract/UI tests и 10/10 JS scenarios.
Рабочая сборка обновлена: 155 hashes verified, 4 файла с backup, 51 защищённый
файл без изменений. Live Avito/автоматический запуск не выполнялись; требуется
контроль владельцем. Пиксельная видимость canvas не заявляется доказанной.

---
# Предыдущая задача этого чата — PARSER-MAP-DIAGNOSTICS

Прямой запрос владельца 03.11.2026: подготовить сравнимую диагностику Avito
с видимым контуром и без него. Gate: GATE-PARSER-MAP-DIAGNOSTICS.md.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ранее
прочитанный ADR-007 → Collector/Desktop/tests. Ветка текущая; накопленное сохранить.
Пассивный observer, отдельная папка запуска, DOM/screenshot/JSON schema+геометрия,
ярлык --map-diagnostics без автоработы/заданий/доставки. Проверки и обновление
рабочей сборки с backup/SHA-256 разрешены. Самостоятельный live-сбор не разрешён.
Отчёт: reports/PARSER_MAP_DIAGNOSTICS_REPORT.md. Другие Parser эвристики и
параллельная MEDIA-GALLERY ниже этим запросом не изменяются.
Статус подготовки: выполнено. Release 0 warnings/errors, 161/161 адресных
небраузерных проверок Passed. Рабочая сборка и диагностический ярлык обновлены:
156 hashes verified, 39 защищённых файлов без изменений, backup сохранён.
Live-примеры владелец запускает сам; разрешение обычного сбора по признаку
видимого контура ещё не реализовано и не заявляется. Следующий Gate не начат.

---
# Активная задача этого чата — INCOMING-PACKAGE-A

23.11.2026 прямым запросом разрешён пакет A подготовленного плана: реализация,
проверка корректности и скорости. Gate GATE-INCOMING-PACKAGE-A.md.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate →
PLAN-INCOMING-FILTER-PRECOMPUTE §§1–6,8–10 / отчёт аудита → ранее прочитанные
load-timeout report и FP-004 §§3,9,12–15 → адресный read/UI/tests.
Накопленное и текущая ветка сохраняются; B/schema/ingress/production/commit/push
не разрешены. Build/адресные tests/замеры на disposable PostgreSQL разрешены.
Отчёт reports/INCOMING_PACKAGE_A_REPORT.md; после A остановиться.
Пакет A реализован и проверен 23.11.2026: main rows+exact Total отдельно от
counts/рынка, scoped SQL-CTE, batch metadata и guard UI. Differential 10/10,
UI/браузерные проверки и 2 performance набора Passed. На 25k/500k/50 main
1.482–1.976 с, full 1.712–1.822 с; 100k/2m/100 main 7.689–9.733 с.
Схема/ingress/пакеты/production/commit/push не менялись. B не активирован.

## Завершённый аудит — INCOMING-FILTER-PLAN-AUDIT

23.11.2026 владелец запросил новый чат для полного плана реализации и
независимого аудита проекта ускорения фильтров Incoming. Только анализ и документы.
Required reading: README → START_HERE → ACTIVE_TASK → GATE-INCOMING-FILTER-PLAN-AUDIT.md
→ GATE-INCOMING-LOAD-TIMEOUT.md / reports/INCOMING_LOAD_TIMEOUT_REPORT.md →
FP-004 §§3,9,12–15 → адресный код/mappings/grants/tests, screen01 по необходимости.
Выход: docs/03-active/PLAN-INCOMING-FILTER-PRECOMPUTE.md и
reports/INCOMING_FILTER_PLAN_AUDIT_REPORT.md. Схема 2 таблицы/3 столбца —
предложение для проверки; реализация/migrations/БД/Git/production не разрешены.
Ветка, индекс, весь накопленный и параллельный scope сохраняются.
Остальные секции ACTIVE_TASK не заменять. Реализацию после аудита не начинать.
Аудит завершён 23.11.2026. Отчёт reports/INCOMING_FILTER_PLAN_AUDIT_REPORT.md;
план PLAN-INCOMING-FILTER-PRECOMPUTE.md. Исходный полный filter precompute не готов:
конкуренция/динамика/duplicate-ingress/AutoInclude риски. Рекомендуется отдельный
пакет A: SQL + rows/exact Total отдельно от информационных counts, batch metadata,
без schema/новой работы ingress. Одна listing↔search проекция — условный B лишь
после замеров A. Только документы; новые тесты/build/БД/preview не запускались.
Следующий Gate/реализация не активированы.

---
# Завершённый анализ этого чата — INCOMING-LOAD-TIMEOUT

20.11.2026 продолжение запроса исправить тормоза «Входящих»: не загружается
сам список; production Read DB_TIMEOUT / TimeoutException / 10396 ms.
Required reading: README → START_HERE → ACTIVE_TASK → GATE-INCOMING-LOAD-TIMEOUT.md
→ уже прочитанные FAST-OPEN-TRANSFER/QUICK-DISMISS, screen01/UI/tokens,
FP-004 §§3,9,12–15 → read/query/selection/presets/tests.
Уточнение владельца: только полный анализ и baseline, предложения простого
ускорения с оценкой корректности; реализацию отложить. Код приложения не менять.
Без schema/packages/Parser/Git/production/перезапуска preview.
Отчёт reports/INCOMING_LOAD_TIMEOUT_REPORT.md. Остальные задачи сохранены.
Анализ завершён: текущий count при 50 фильтрах — 700 узлов чтения observations;
на 25 000 listings / 500 000 observations service read 11.031 с, count 8.890 с.
Window без общего count — 1.009 с (без exact Total). Рекомендуются исправление
SQL, разделение rows/информационных агрегатов, batch metadata и reuse сводок;
новая membership table только следующим шагом при необходимости.
Код приложения/production не менялся. Реализация отложена до решения владельца.

---
# Завершённая задача этого чата — INCOMING-FAST-OPEN-TRANSFER

20.11.2026 разрешено исправить тормоза открытия карточки и передачи менеджеру
во входящих. Required reading: README → START_HERE → ACTIVE_TASK →
GATE-INCOMING-FAST-OPEN-TRANSFER.md → уже прочитанные QUICK-DISMISS/MANAGER-TRANSFER,
их отчёты/screen01/UI/tokens/FP-004 §§3,9,12–15 → UI/read/команды/tests.
Без общего list/facets/market read после просмотра/передачи; сохранить права,
историю, New, группу источников/пагинацию/выбор/скролл и подтверждённый исход записи.
Файлы/проверки ограничены Gate. Без schema/packages/Parser/логов production/
Git/deploy/перезапуска preview. Отчёт reports/INCOMING_FAST_OPEN_TRANSFER_REPORT.md.
Реализовано локально: точечные reads просмотра/группы, loading/error/cancel drawer,
подтверждённая передача с локальным обновлением и bounded refill без full read.
Release Passed; 9/9 PostgreSQL/UI/browser регрессий Passed (25/50/100, скрытый
источник группы, last-page clamp, выбор/права, поздний ответ/повтор чтения,
Rejected/Unknown/commit + read failure, скролл/DOM/JS). Production latency и
причина screenshot incident не заявляются доказанными. Git/production не менялись.

---
# Завершённая задача этого чата — PRODUCTION-LOGGING

19.11.2026 разрешены дневные логи по месяцам, предел 10 МиБ, retention 7 суток,
безопасная диагностика следующего сбоя общения и поиск по incident ID.
Required reading: README → START_HERE → ACTIVE_TASK → GATE-PRODUCTION-LOGGING.md
→ PRODUCTION_RUNBOOK (запуск/диагностика), FP-004 §§9,12,14,15 → runner/deploy,
Server/Worker/WorkspaceComponent/CaseCommunication/tests. Текущая ветка и всё
накопленное, Parser и preview сохраняются. Без schema/packages/commit/push/
production и удаления реальных логов. Отчёт reports/PRODUCTION_LOGGING_REPORT.md.
Реализовано локально: Windows PowerShell 5.1 runner/helper, ротация 10 МиБ по
московским дням/месяцам, 7 суток last-write retention, безопасный поиск и UTC JSON.
Event 1101 дополнен кодами/SQLSTATE/объектами БД/длительностью/CommandId общения.
Release clean; 35 реальных PowerShell/helper/launcher и 26 .NET проверок Passed.
Production/Git не изменялись; причина прежнего сбоя общения без данных не заявляется.

---
# Завершённая задача этого чата — INCOMING-QUICK-DISMISS

19.11.2026 владелец разрешил точечное удаление строки после подтверждения
«Не подходит», без полной перезагрузки, с прокруткой/следующим/пагинацией.
Gate GATE-INCOMING-QUICK-DISMISS.md. Required reading: README → START_HERE →
ACTIVE_TASK → Gate → ранее прочитанные incoming pagination/screen01/UI/tokens,
FP-004 §§3,9,12–15 → IncomingCatalogV2/WorkspaceComponent/read contracts и tests.
Без схемы/packages/Parser/массовой архивации/смены прав/commit/push/production.
Сохранить предыдущие доработки, накопленное и preview. Проверить лёгкое обновление,
ошибки/unknown, порядок/границы страниц/следующее/скролл и отсутствие тяжёлых read.
Отчёт reports/INCOMING_QUICK_DISMISS_REPORT.md.
Реализовано локальное подтверждённое исключение и ограниченная подгрузка строк,
без общего read/рынка; сохранены прокрутка, следующий, пагинация и выбор.
Release — clean; 25 предварительных проверок Passed, 21 адресная final-build
Passed, заключительный localhost browser — 1 Passed (47s). Позиция/DOM,
границы страниц, New, rejected/unknown, восстановление после сбоя проверены.
Убрано перекрывающее пагинацию уведомление успеха. Production/Git не изменялись.

---
# Завершённая задача этого чата — PARSING-ARCHIVE-GUARD

19.11.2026 владелец разрешил исключить архивные зоны из текущих ошибок и прямо
проверять архив при enqueue/schedule/retry/claim, сохраняя историю.
Gate GATE-PARSING-ARCHIVE-GUARD.md. Required reading: README → START_HERE →
ACTIVE_TASK → Gate → ранее прочитанные Retry/Large-Lists/UI/screen07/tokens,
FP-004 §§3,9,12–15 → Collection/UI/tests. Без schema/packages/Parser переписывания,
commit/push/production. Сохранить накопленное, исправление кнопок и preview.
Отчёт reports/PARSING_ARCHIVE_GUARD_REPORT.md. Проверить архив при enabled=true,
старые задания, права, активные/без группы, ошибки/счётчики и доступную историю.
Статус: реализовано локально. Release 0 warnings/errors, 37/37 .NET и 11 JS
проверок Passed; PostgreSQL/фактический HTML/browser-регрессия пройдены.
Commit/push/deploy не выполнялись; следующий Gate не начат.

---
# Завершённая задача этого чата — PARSING-RETRY-STATE

16.11.2026 владелец разрешил независимую отправку повторов и понятные статусы
кнопок, включая причину паузы. Gate GATE-PARSING-RETRY-STATE.md.
Required reading: README → START_HERE → ACTIVE_TASK → Gate → ранее прочитанные
PARSING-WORKSPACE/LARGE-LISTS/UI/tokens/screen07/FP-004 §§3,9,12–15 → Collectors,
WorkspaceComponent, EnqueueAsync/tests. Только UI отправки/состояния, прежние
команды и права. Без backend/schema/packages/Parser/commit/push/production.
Ветка, накопленное и preview сохраняются. Отчёт reports/PARSING_RETRY_STATE_REPORT.md.
Проверить две независимые отправки, статусы, ошибки, refresh, desktop/mobile.
Статус: реализовано локально, Release 0 warnings/errors, 15/15 адресных проверок
Passed; browser/PostgreSQL с задержкой первой отправки и desktop/mobile просмотрены.
Commit/push/deploy не выполнялись; следующий Gate не начат.

---
# Завершённая задача этого чата — PARSING-LISTS-PUBLICATION

16.11.2026 владелец разрешил commit/push в main доработки больших списков и
запросил скрипт обновления. Gate GATE-PARSING-LISTS-PUBLICATION.md.
Required reading: README → START_HERE → ACTIVE_TASK → Gate → завершённый
PARSING-LARGE-LISTS Gate/report → PRODUCTION_RUNBOOK §§5–6 / publish/deploy.
Отдельный main-based snapshot от 9bf4ed9, только текущая доработка и документация.
Галерея/передача менеджеру сохраняются; накопленное/branch/index/preview не менять.
Без production/schema/packages/config. Проверить Release/tests точного снимка,
опубликовать один fast-forward commit без force, дать pinned update script.
Отчёт reports/PARSING_LISTS_PUBLICATION_REPORT.md. Подготовлено 18 scoped файлов;
locked restore/Release 0 warnings/errors; 37/37 .NET и 11 JS проверок Passed на
точном main-based снимке. SHA и факт публикации — artifacts/parsing-lists-publication/verification.json.

---
# Завершённая задача этого чата — PARSING-LARGE-LISTS

16.11.2026 разрешены зоны/поиски без страниц с внутренней прокруткой, история
порциями по 50, сохранение позиции/фильтров. Gate GATE-PARSING-LARGE-LISTS.md.
Required reading: README → START_HERE → ACTIVE_TASK → Gate → уже прочитанные
PARSING-WORKSPACE/UI/tokens/screen07/v2/FP-004 §§3,9,12–15 → Collection/UI/tests.
Только списки и чтение истории; права/команды/schema/packages/Parser без изменений.
Текущая ветка и накопленное сохраняются. Без commit/push/production.
Отчёт reports/PARSING_LARGE_LISTS_REPORT.md. Статус: реализовано локально,
Release 0 warnings/errors, 32/32 .NET и 11 JS проверок Passed; настоящий browser
на 70 зонах/120 поисках, history 50/100/150, desktop/tablet/mobile просмотрен.
Commit/push/production не выполнялись, следующий Gate не начат.

---
# Завершённая задача этого чата — WORKSPACE-TRANSFER-PUBLICATION

16.11.2026 владелец разрешил commit/push в main обеих доработок и запросил
скрипт обновления. Gate GATE-WORKSPACE-TRANSFER-PUBLICATION.md. Required reading:
README → START_HERE → ACTIVE_TASK → Gate → оба завершённых Gate/отчёта →
PRODUCTION_RUNBOOK §§5–6 / publish/deploy. Проверенный base main edce4b9.
Только main-based snapshot поисков/парсинга и передачи менеджеру, без прочих
локальных работ; branch/index и локальный preview сохраняются. Production не
выполняется, migrations/config/packages не меняются. Подготовлено: 24 файла,
locked restore / Release 0 warnings/errors, 35/35 адресных проверок на точном
main-based snapshot прошли. Галерея уже в base и входит в полную сборку.
Факт публикации и SHA — внешний verification.json и итоговое сообщение.
Отчёт reports/WORKSPACE_TRANSFER_PUBLICATION_REPORT.md.

---
# Завершённая задача этого чата — INCOMING-MANAGER-TRANSFER

16.11.2026 разрешена кнопка «Передать менеджеру» с выбором сотрудника и назначением
его ответственным. Права остаются прежними. Gate GATE-INCOMING-MANAGER-TRANSFER.md.
Required reading: README → START_HERE → ACTIVE_TASK → Gate → уже прочитанные
UI instructions/consistency/tokens, screen01/reference, FP-004 §§3,9,14–15 →
IncomingCatalogV2 и существующий transfer/Access V1 tests. Только поштучный UI,
существующие команды; без backend/schema/packages/production/commit/push.
Текущая ветка, Parser и накопленное сохраняются. Статус: реализовано локально.
Release 0 warnings/errors, адресные backend и реальный browser/PostgreSQL проверки
прошли. Назначение/история/права/защита от дубля подтверждены, 1440/390 просмотрены.
Отчёт reports/INCOMING_MANAGER_TRANSFER_REPORT.md. Production/commit/push не было.

---
# Завершённая задача этого чата — PARSING-WORKSPACE

16.11.2026 владелец принял автономный v2 и разрешил интеграцию /collectors.
Gate GATE-PARSING-WORKSPACE.md. Required reading — README → START_HERE →
ACTIVE_TASK → этот Gate → prototype Gate/report, UI instructions/consistency,
screen07, v2/reference v1.3/tokens, FP-004 §§3,9,12–15 → affected Collection/UI/tests.
Четыре вкладки, доказанные причины в таблицах, правка/повтор через реальные
команды, сохранение прав и истории. Без migrations/packages/Parser/production/
commit/push. Текущая ветка и накопленное сохраняются. Отчёт
reports/PARSING_WORKSPACE_REPORT.md. Статус: реализовано локально. Release без
warnings/errors; финальные 13/13 (включая реальный browser + disposable PostgreSQL),
ещё 18 проверок расписаний/пула прошли. Desktop/tablet/mobile просмотрены,
переполнения документа нет. Production/commit/push не выполнялись.

---
# Активная задача этого чата — INSPECTION-DRAFT-MERGE

04.11.2026 владелец разрешил объединение ответов между вкладками и безопасное
завершение; выбирать только разные изменения одного поля. Проверить одну вкладку:
начало → видео → ответы → завершение. Gate GATE-INSPECTION-DRAFT-MERGE.md.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → ранее прочитанные
FP-004 §§3,9,14–15, ADR-005, UI/screens04/tokens → affected draft/save/tests.
Ветка текущая; параллельный Parser и накопленное сохранить. Без production,
commit/push, migrations, пакетов, ролей и изменений cloud uploads.
Отчёт reports/INSPECTION_DRAFT_MERGE_REPORT.md. Реализовано: Release 0 warnings/errors,
14/14 адресных .NET проверок, 8/8 JS. Single-tab видео→ответы→завершение и две
вкладки/выбор/late export проверены. Без commit/push/production. Следующий Gate не начат.

---
# История — INSPECTION-MEDIA-LAYOUT

03.11.2026 прямой запрос: крупный кадр + лента миниатюр слева, исходное описание
справа с сохранением переносов и ограниченной высотой; mobile вертикально.
Gate GATE-INSPECTION-MEDIA-LAYOUT.md. Required reading: README → START_HERE →
ACTIVE_TASK → этот Gate → уже прочитанные UI/screens03/04/tokens, FP-004/ADR-005,
media Gate → affected components/tests. Ветка текущая; накопленное сохранить.
Без backend/пакетов/migrations/production/commit/push/Collector.
Отчёт reports/INSPECTION_MEDIA_LAYOUT_REPORT.md. Статус: реализовано локально; Release build 0 warnings/errors, component 2/2, финальный real browser 1/1 Passed (20 материалов, desktop/mobile, переносы). Скриншоты просмотрены. Commit/push/production не выполнялись.

---
# История — MEDIA-GALLERY

03.11.2026 владелец разрешил галерею фото/видео, просмотр с листанием/увеличением,
видеоплеер, отдельные скачивание/мягкое удаление и компактный блок ошибок/повтора.
Gate GATE-MEDIA-GALLERY.md. Ветка codex/inspection-file-improvements, накопленное сохранить.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
FP-004 §§3,9,14–15, ADR-005, media Gates, UI instructions/consistency/tokens,
screens03/04 → affected code/tests. Новых пакетов/migrations/ролей нет.
Без live cloud/production/Collector/commit/push. Отчёт reports/MEDIA_GALLERY_REPORT.md.
Статус: реализовано. Release build 0 warnings/errors; core 28 Passed и исправленный endpoint/UI 22/22 Passed, итоговый real browser 1/1 Passed (desktop/mobile/видео/повтор/soft delete). Production/commit/push не выполнялись. Следующий Gate не начинать.

---
# История — MEDIA-PARALLEL-UPLOAD

03.11.2026 владелец разрешил до пяти параллельных direct uploads, скорость передачи,
понятные этапы/время; проверка SHA-256 последовательная. Ветка текущая
codex/inspection-file-improvements, накопленные изменения сохранить.
Gate: GATE-MEDIA-PARALLEL-UPLOAD.md. Required reading: README → START_HERE → ACTIVE_TASK
→ этот Gate → уже прочитанные FP-004 §§3,9,14–15, ADR-005, YANDEX_DISK_SETUP,
прежний media Gate и UI instructions/consistency/tokens → affected code/tests.
Без новых пакетов/ролей/migrations, галереи/сжатия, Collector, live cloud,
production и commit/push/merge. Отчёт reports/MEDIA_PARALLEL_UPLOAD_REPORT.md.
Статус: реализовано. Release build 0 warnings/errors; core 46/46, финальный UI 21/21, browser 2/2, JS metrics 11/11 Passed. Production/commit/push не выполнялись. Завершённый Parser сохранён ниже; следующий Gate не начинать.

---
# История — PARSER-SAVED-SEARCH-AREA

03.11.2026 владелец разрешил сохранять эталон геометрии поиска, проверять
координаты объявлений и фильтры независимо от drawId, сохранять неподтверждённые
данные/диагностику и понятный Partial. Ветка текущего checkout:
`codex/inspection-file-improvements`; накопленные изменения сохранить.
Gate: `GATE-PARSER-SAVED-SEARCH-AREA.md`. Required reading: README → START_HERE →
ACTIVE_TASK → этот Gate → уже прочитанный ADR-007 → затрагиваемый Collector/tests.
Отчёт: `reports/PARSER_SAVED_SEARCH_AREA_REPORT.md`. После тестов обновить
`artifacts/LandErp-Parser` с backup/SHA-256, не запускать автоматически.
Без live marketplace, новых пакетов, Server API/schema, commit/push/merge.

Дополнение владельца 03.11.2026: старые ссылки должны работать без ручного
подтверждения. Первый валидный контур выделения из source response принимать
автоматически после загрузки и проверки фильтров, независимо от нового drawId.
Существующий эталон, фильтры и point-in-polygon сохраняются. Требуется повторный
прогон тестов и немедленное обновление рабочей сборки. Файлы: SearchAreaGuard.cs,
SavedSearchAreaTests.cs, README, текущий Gate/отчёт. Предыдущий запрет bootstrap
при новом ID отменён этим прямым запросом; viewport не считается контуром.


Статус: реализовано, включая автоматическую поддержку старых ссылок. Финальные
155/155 небраузерных проверок Passed; Release build
Parser/Desktop/tests — 0 warnings/errors. Рабочая сборка artifacts/LandErp-Parser
обновлена с backup и SHA-256 всех 155 программных файлов. Финальная проверка
15 защищённых файлов — изменений нет. 60 101 observations и 7 350 listings
сохранены. Владелец сам закрыл Parser перед установкой; автоматического запуска
и live-сбора не было. Последнее обновление автоматического bootstrap: 2 файла
заменены с backup, все 155 программных файлов проверены, 15 защищённых неизменны.
Evidence: artifacts/parser-auto-area/local-update/verification.json.
Следующий Gate не активирован.

---
# История — MEDIA-LARGE-UPLOAD

02.11.2026 владелец разрешил проверить прямую браузерную загрузку на Яндекс Диск
и реализовать выбранный по evidence путь: direct либо потоковый через Server.
Лимит 500 МиБ. Gate: GATE-MEDIA-LARGE-UPLOAD.md. Текущая ветка
`codex/inspection-file-improvements`; накопленные изменения сохраняются.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → FP-004
§§3, 9, 14–15 → ADR-005 → YANDEX_DISK_SETUP → прежний inspection Gate →
UI instructions/consistency/tokens → затрагиваемые contracts/adapters/forms/tests.
Разрешён opt-in live probe на синтетическом файле; токены/URL не раскрывать.
Без сжатия, галереи, production, новых пакетов/миграций/ролей, commit/push/merge.
Отчёт: reports/MEDIA_LARGE_UPLOAD_REPORT.md. Статус: реализация direct завершена. Release build — 0 warnings/errors; адресные проверки прошли (39 core + 1 исправленный component, 2 browser), включая native 500 МиБ и прежний осмотр. Production обновляет/проверяет владелец; в приватной конфигурации требуется MaxFileBytes=524288000. Новых migrations нет. Рабочий токен и iPhone/Safari ещё не проверены. Commit/push не выполнялись.

---
# История — PROCUREMENT-PIPELINE-SEARCH

02.11.2026 владелец утвердил поиск по выбранной воронке закупки в канбане и
таблице. Gate: `GATE-PROCUREMENT-PIPELINE-SEARCH.md`. Работа в текущей ветке
`codex/inspection-file-improvements`; накопленные изменения сохранить.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate →
FP-004 §§3, 9, 14–15 → AGENT_UI_INSTRUCTIONS → GATE-UI-CONSISTENCY →
UI-kit tokens → screen 02/reference HTML → KANBAN_PLAN (контекст/пагинация) → код.
Прежнее отсутствие поиска на доске отменено текущим запросом владельца.
Без новых пакетов/ролей/миграций, production, commit/push/merge и изменений Collector.
Проверки: Release build, адресные PostgreSQL/HtmlRenderer tests, git diff --check.
Отчёт: `reports/PROCUREMENT_PIPELINE_SEARCH_REPORT.md`. Статус: реализовано.
Release build — 0 warnings/errors; итоговый адресный PostgreSQL/HtmlRenderer
прогон — 7/7 Passed. Evidence: `artifacts/procurement-pipeline-search/tests/pipeline-search-verified.trx`.
Браузер/визуальная приёмка, production и commit/push не выполнялись.
Следующий Gate не активирован.


## Публикация исходников — 02.11.2026

Прямым запросом владелец поручил commit/push поиска в `origin/main`.
Публикуется только 13 файлов этого Gate: поиск, адресные тесты, спецификация и
документы этапа. Накопленные изменения Collector/README и локальная история
его неопубликованных Gate исключены. Публикация — один коммит поверх проверенного
head `main`, fast-forward с expected SHA без force через подключённый GitHub.
Локальная ветка/индекс и production не изменяются. Итоговый SHA подтверждается
отдельным чтением `main`; evidence — `artifacts/procurement-pipeline-search/publication/verification.json`.

---
# История — INSPECTION-FILE-IMPROVEMENTS

29.10.2026 владелец утвердил реализацию пунктов 1–4 обращений Telegram:
пакетная последовательная загрузка, 50 МБ на файл, галерея/камера,
мягкое удаление с историей и скачиванием по существующему праву редактирования
карточки. Ветка `codex/inspection-file-improvements`.
Gate: `GATE-INSPECTION-FILE-IMPROVEMENTS.md`.
Required reading: README → START_HERE → ACTIVE_TASK → этот Gate →
`docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md`
(§§3, 9, 14–15) → затрагиваемые forms/contracts/storage/workspace/tests.
Отчёт: `reports/INSPECTION_FILE_IMPROVEMENTS_REPORT.md`.
Без новых ролей/пакетов, физического удаления, production, commit/push/merge.
Предыдущие локальные изменения Collector сохранить. После завершения остановиться.
Статус: реализовано. Release build — 0 warnings/errors; адресный небраузерный
прогон — 15/15 Passed; Edge headless на disposable БД — 1/1 Passed (несколько
документов, осмотр с видео 30 МиБ, удаление и скачивание через историю).
Один прежний тест ожидает 5 обязательных документов вместо существующих 4;
расхождение вне scope зафиксировано в отчёте. iPhone/Safari — ручная приёмка.
Production не обновлялся; следующий Gate не активирован.

## Публикация исходников — 30.10.2026

Прямым запросом владелец разрешил commit/push этой доработки в `origin/main`.
Включаются только загрузка материалов, мягкое удаление, история/права, адресные
тесты и документы этого этапа. Предыдущие локальные изменения Collector,
README и его отчёты исключены. Локальные metadata Git доступны только для
чтения; публикация выполняется через подключённый GitHub, с проверкой исходного
и итогового SHA `main`. Рабочий сервер и БД не обновляются.
Попытка публикации заблокирована разрешениями сеанса: локальный Git — read-only,
GitHub create_tree требует approval при policy=never. Коммит/push не выполнены.
Подготовлен отдельный patch из 22 файлов в `artifacts/inspection-files/publication`.

---
# История — PARSER-CIAN-BBOX-ROUNDING

23.10.2026 владелец разрешил исправить сравнение округлённых координат Циана,
сохранить строгую проверку реальных изменений фильтров, выполнить тесты и сразу
обновить рабочую сборку `artifacts/LandErp-Parser`. Ветка `codex/parser-reliability`.
Gate: `GATE-PARSER-CIAN-BBOX-ROUNDING.md`. Required reading: уже прочитанные README
→ START_HERE → ACTIVE_TASK → этот Gate → ADR-007 → SearchUrls.cs и адресные tests.
Отчёт: `reports/PARSER_CIAN_BBOX_ROUNDING_REPORT.md`. Scope: только канонизация
bbox в сравнении/диагностике Cian; исходные ссылки, контуры, остальные фильтры,
Server и БД не менять. Без live/браузера, новых пакетов, commit/push/merge.
Обновление — с резервированием, SHA-256 и сохранением данных/настроек/диагностики;
Parser автоматически не запускать. Предыдущие локальные изменения сохранить.

Статус: реализовано. Финальные 66/66 небраузерных проверок Passed, Release build —
0 warnings/errors. Рабочая сборка обновлена: все 155 файлов проверены по SHA-256,
9 файлов базы/настроек/последней диагностики неизменны. Прежние программные файлы
сохранены. Parser/live не запускались. Evidence:
`artifacts/parser-cian-bbox/local-update/verification.json`. Следующий Gate не активирован.

---
# История — PARSER-AVITO-SEARCH-IDENTITY

22.10.2026 владелец прямо разрешил поддержку преобразованных ссылок Avito с
проверкой зоны и фильтров, тестами и обновлением рабочей сборки. Ветка:
`codex/parser-reliability`. Gate: `GATE-PARSER-AVITO-SEARCH-IDENTITY.md`.
Required reading: уже прочитанные README → START_HERE → ACTIVE_TASK → этот Gate
→ ADR-007 → затрагиваемый ParserSpike/tests. Отчёт:
`reports/PARSER_AVITO_SEARCH_IDENTITY_REPORT.md`.
Scope уточнён владельцем: обычная выдача парсится как список по сохранённой
ссылке, без нажатия карты или переключения режима. Проверять сохранение ссылки
на зону и фильтров; геометрию проверять только у реально открытого режима карты.
Исправить чтение select-text из фактического HTML и проверить пагинацию списка.
Без live-сбора, новых пакетов, Server API/schema, commit/push/merge.
Рабочие данные, настройки, outbox, профили и предыдущие изменения сохранить.
После тестов обновить artifacts/LandErp-Parser с резервированием и SHA-256;
автоматически Parser не запускать. Статус: реализовано; Release build — 0
warnings/errors, финальный offline-прогон списка — 194/194 Passed. Рабочая сборка обновлена:
4 программных файла заменены с резервной копией, SHA-256 всех 155 совпали.
Все 9 защищённых файлов данных/настроек/последней диагностики неизменны.
Открытый Parser штатно закрыт перед установкой (активных заданий не было),
после установки агентом не запускался. Нераспознавание select исправлено;
автопереход в карту удалён. Адресные тесты списка — 29/29 Passed, включая
две страницы без нажатия карты/запросов map API. Очищенный рабочий HTML локально
проверен без скриптов/сети; форма распознана. Финальная установка и SHA-256:
`artifacts/parser-avito-identity/list-mode/local-update/verification.json`.
Commit/push не выполнялись, следующий Gate не активирован.

---
# История — PARSER-LAST-ERROR-HTML

Дата: 22.10.2026. Ветка: `codex/parser-reliability`. Прямым запросом владелец
разрешил сохранять только последний HTML ошибки сбора, причину и доступ из UI.
Gate: `GATE-PARSER-LAST-ERROR-HTML.md`. Required reading: README → START_HERE →
ACTIVE_TASK → этот Gate → ADR-007 → затрагиваемый ParserSpike/Desktop/tests.
Отчёт: `reports/PARSER_LAST_ERROR_HTML_REPORT.md`. Предыдущие локальные изменения
сохраняются. Рабочие данные, профили и outbox не изменяются; live-сбор, production,
commit/push/merge не выполняются.

Статус: реализовано. Release build — 0 warnings/errors; 26 адресных проверок
Passed, после последней правки повторно 12/12 Passed. Один прежний серверный
UI-тест в общем запуске недоступен из-за Windows DPAPI в среде проверки;
ограничение отражено в отчёте. Браузер и рабочий Parser не запускались,
рабочий экземпляр в artifacts/LandErp-Parser не обновлялся.

Следующим прямым запросом владелец поручил обновлять рабочую сборку после
исправлений. Разрешено адресное обновление `artifacts/LandErp-Parser` с резервной
копией программных файлов и проверкой сохранности данных/настроек, без запуска.
Результат фиксируется в текущем отчёте и evidence `artifacts/parser-last-error/local-update`.

Обновление выполнено: заменены 4 программных файла, их прежние версии сохранены
в резервной копии. Проверены SHA-256 всех 155 файлов сборки и неизменность
7 файлов базы/настроек. Parser автоматически не запускался. Рабочая папка
`artifacts/LandErp-Parser` теперь содержит сохранение последнего HTML ошибки.

---
# История — PARSER-RELIABILITY: отзывчивость и надёжность локального парсера

Дата активации: 22.10.2026. Ветка: `codex/parser-reliability`.
Основание: прямое согласие владельца реализовать все шесть предложенных
исправлений отдельным этапом с тестами. Статус: реализовано, 154/154 offline
проверки Passed; Release build — 0 warnings/errors. Итоговый отчёт:
`reports/PARSER_RELIABILITY_REPORT.md`. Рабочий экземпляр обновлён по отдельному
запросу ниже, без запуска. Контрольные суммы 7 файлов базы/настроек совпали.
Коммит и push не выполнялись; следующий этап не активирован.

## Локальное обновление рабочего экземпляра — 22.10.2026

Следующим прямым запросом владелец разрешил обновить сборку в текущей рабочей
папке. Подтверждён путь `artifacts/LandErp-Parser`; процессы Parser не запущены.
Допустимы отдельная Release-сборка, резервная копия заменяемых программных файлов,
копирование в эту папку и проверка хешей. Данные, настройки, outbox и профили
сохраняются. Автоматический запуск, live-сбор, production, commit и push не
выполняются. Результат дополняет текущий отчёт, новый Gate не активируется.

Результат: обновлены 8 программных файлов, прежние версии сохранены в резервной
копии; все 155 файлов сборки проверены по SHA-256. Три собственные DLL совпадают
со сборкой, прошедшей 154 теста. Повторная проверка 7 файлов данных/настроек:
изменений нет. Evidence: `artifacts/parser-reliability/local-update/verification.json`.

Scope: фоновое пакетное обновление активных заданий и история по 50 записей;
гарантированное освобождение вкладок/сессий при отмене и ошибках; наблюдение
фоновых ошибок; ограничение диагностических коллекций; сохранение известных
полей внутри Local/Server; полная проверка условий Cian.
Gate: `GATE-PARSER-RELIABILITY.md`. Итог: `reports/PARSER_RELIABILITY_REPORT.md`.

Рабочие базы, настройки, outbox и профили браузера сохранить. Проверки только
на изолированных базах/фикстурах/временных профилях. Без live marketplace,
production, новых пакетов, коммита и push. Не переходить к следующим этапам.

## Required reading текущего этапа

README → START_HERE → этот файл → `GATE-PARSER-RELIABILITY.md`;
`docs/02-decisions/ADR-007_Collector_как_самостоятельный_продукт_и_граница_с_Server.md`;
аудит текущего чата и затрагиваемые ParserSpike/Desktop/tests.
ERP/UI-kit документы завершённой задачи ниже не являются зависимостями
локального WPF-парсера. Версионированные серверные контракты не меняются.

---
# История — ручная группа поиска объекта закупки

Дата активации: 21.10.2026. Ветка: `codex/procurement-manual-search-group`.
Статус: реализовано по прямому согласию владельца в текущем чате.
Release build — 0 errors/warnings; 7/7 адресных небраузерных проверок Passed
на изолированных PostgreSQL-базах. Итог: `reports/PROCUREMENT_MANUAL_SEARCH_GROUP_REPORT.md`.
На этапе реализации commit/push не выполнялись. Рабочая БД/сервер и визуальная
приёмка не затрагивались. Отдельное разрешение на публикацию исходников — ниже.

## Публикация обеих доработок — 21.10.2026

Прямым запросом владелец разрешил коммит обеих доработок и отправку в
`origin/main` для самостоятельного обновления сервера. Включаются назначение
группы объектам закупки, компактные строки ориентиров, новая миграция, тесты,
спецификация и отчёты. Пользовательские вложения и локальные артефакты исключены.
Перед публикацией проверен актуальный `origin/main`; фактический SHA и совпадение
удалённой ветки проверяются после push и сообщаются в итоговом ответе.
Production-развёртывание и применение миграции к рабочей БД не выполняются.

## Корректировка компактности показателей — 21.10.2026

Следующим прямым запросом владелец разрешил исправить раскладку строк
«Ориентир теста спроса» и «Желаемая цена покупки» по скриншоту: подпись,
значение, единица и редактирование в один ряд при достаточной ширине.
Это текущая ограниченная UI-доработка в той же ветке после завершения назначения
групп. Состав показателей и бизнес-логика не меняются. Допустимы общий блок
GroupMarketPrices, стили DemandTestPrice и узкое исправление CSS входящих,
этот указатель и отчёт. Проверки: Release build Server, существующий адресный
тест представления, git diff --check. Браузер и production не используются.
Required reading: уже прочитанные README/START_HERE, UI instructions/tokens,
screen 01 и reference HTML, текущие общие компоненты и их стили.
Статус: реализовано; Release-сборка Server в составе адресной проверки и
существующий тест представления — Passed (1/1), git diff --check — Passed.
Визуальная приёмка и обновление сервера не выполнялись.
Отчёт: `reports/GROUP_MARKET_COMPACT_ROWS_REPORT.md`.

## Разрешённый scope завершённого назначения групп

- Одна необязательная ручная группа поиска у PropertyCase: при создании и в
  существующей полной/боковой карточке, включая ранее созданные вручную объекты.
- Явная группа имеет приоритет над группами подтверждённых источников. Снятие
  выбора восстанавливает прежнее определение групп по источникам.
- При архивировании группы связь сохраняется; автоматической подмены нет.
  Назначать можно только действующие группы своей организации.
- Используются текущие показатели карточки и фильтрация закупки по группе.
  Цена объекта не включается в медиану; Catalog/Collector не меняются.
- Одна nullable-ссылка в PropertyCase, новая миграция, текущие права закупки,
  версии, audit и business timeline. Без новых таблиц и зависимостей.
- Release build, адресные небраузерные проверки на disposable PostgreSQL:
  создание/смена/снятие, приоритет/архив, фильтр, изоляция/права/конкурентность,
  неизменность медианы, миграция на чистую и предыдущую схему; git diff --check.

Production и рабочая БД/сервер не входят в поручение. Публикация исходников
в main отдельно разрешена запросом 21.10.2026 выше.
Браузер/CUA/Playwright не используются; визуальная приёмка владельцем отдельно.
Пользовательские вложения и локальные данные сохраняются.

## Required reading

README → START_HERE → этот файл; уже прочитанные неизменившиеся KANBAN_PLAN,
FP-004 §§3–4, 7–9, 14–15, AGENT_UI_INSTRUCTIONS, GATE-UI-CONSISTENCY и tokens.css;
`docs/14-ui-kit/screens/02-procurement-queue.md` и его reference HTML;
затрагиваемый код. Прежний отчёт: `reports/INCOMING_PAGINATION_REPORT.md`.
Итоговый отчёт: `reports/PROCUREMENT_MANUAL_SEARCH_GROUP_REPORT.md`:
файлы, решения, команды/результаты, миграция, ограничения и следующий шаг.

---
# История — постраничная навигация входящих объявлений

Дата активации: 19.10.2026. Ветка: `main` (текущая ветка активной задачи).
Статус: реализовано и подготовлено к отправке в GitHub; Release build — 0 errors/warnings, подтверждены 7
различных адресных сценариев (6 результатов прерванного запуска + 1 отдельный Passed).
Полного завершённого общего прогона нет; детали — в `reports/INCOMING_PAGINATION_REPORT.md`.
Production и визуальная приёмка не выполнялись. Новая задача не активирована.
Основание: прямое согласие владельца добавить номера страниц, начало/конец,
номер текущей страницы и выбор 25 / 50 / 100 строк.
Предыдущая доработка массового архива сохраняется; её отчёт —
`reports/INCOMING_BULK_ARCHIVE_REPORT.md`.

## Разрешённый scope

- Номера страниц, текущая страница, переходы в начало/конец и назад/вперёд.
- Выбор 25 / 50 / 100 строк; по умолчанию 25, при смене размера — первая страница.
- Сохранение условий, сортировки, явного выбора и режима «все» с исключениями.
- Возврат на последнюю доступную страницу при сокращении результатов, пустой список.
- Существующая серверная пагинация; новая схема, зависимости и парсеры не требуются.
- Небраузерные проверки настоящей страницы на изолированной PostgreSQL,
  Release build, `git diff --check`, отчёт `reports/INCOMING_PAGINATION_REPORT.md`.

Production, рабочая БД и работающий сервер не изменяются. Парсеры/Collector,
физическое удаление, новые пакеты, миграции и следующие задачи не входят в scope.
Браузер/CUA/Playwright не использовать; визуальная приёмка остаётся владельцу.
Накопленные локальные изменения массового архива и вложения пользователя сохранять.

## Публикация исходников — 19.10.2026

Прямым запросом владелец разрешил коммит и push в `origin/main`, чтобы затем
самостоятельно опубликовать приложение на сервере. Включаются накопленные
изменения массового архива с галочкой медианы и постраничной навигации,
их контракты/сервисы, тесты, спецификация и отчёты. Пользовательские вложения,
локальные данные и TRX в коммит не включаются. Новые миграции не требуются.
Production-развёртывание, обновление рабочего сервера и БД не выполняются.
Фактический SHA и синхронизация с GitHub проверяются после push и сообщаются владельцу.

## Required reading

README → START_HERE → этот файл → KANBAN_PLAN; FP-004 §§3–4, 7–9, 14–15;
AGENT_UI_INSTRUCTIONS; GATE-UI-CONSISTENCY; `docs/14-ui-kit/ui-kit/tokens.css`;
`docs/14-ui-kit/screens/01-incoming-listings.md` и его reference HTML;
затрагиваемый код. Не перечитывать уже прочитанные неизменившиеся документы.

---
# История — пользовательские доработки 5–8 октября 2026 года

Дата активации: 09.10.2026. Ветка: `main`.

Прямым запросом владельца утверждена последовательная реализация всех шести
этапов из переданного задания. Работа выполняется непосредственно на актуальном
`origin/main`; каждый законченный этап получает адресные тесты, отдельный коммит
и push в `origin/main`. Следующий этап начинается только после завершения и
отправки предыдущего, но повторное согласование между этапами не требуется.

Production, рабочий сервер и production-БД не изменяются. Браузер, CUA,
Playwright/CDP и скриншоты не используются; визуальная приёмка остаётся
владельцу. Допустимы Release-сборки, небраузерные JS-проверки и адресные тесты на
изолированных PostgreSQL-базах. Полный набор тестов без необходимости не
запускается.

## Публикация исходников — 19.10.2026

Прямым запросом владелец разрешил коммит и push в `origin/main`, чтобы затем
самостоятельно опубликовать приложение на сервере. Включаются накопленные
изменения массового архива с галочкой медианы и постраничной навигации,
их контракты/сервисы, тесты, спецификация и отчёты. Пользовательские вложения,
локальные данные и TRX в коммит не включаются. Новые миграции не требуются.
Production-развёртывание, обновление рабочего сервера и БД не выполняются.
Фактический SHA и синхронизация с GitHub проверяются после push и сообщаются владельцу.

## Required reading

README → START_HERE → этот файл → KANBAN_PLAN; FP-004 §§3–4, 7–9, 14–15;
AGENT_UI_INSTRUCTIONS; GATE-UI-CONSISTENCY; `docs/14-ui-kit/ui-kit/tokens.css`;
screen spec и reference HTML конкретной изменяемой страницы; только затем
затрагиваемый код.

## Общие границы

- Использовать существующие архитектуру, модели, аудит, историю, права и UI.
- Не переписывать модули целиком и не создавать параллельные механизмы.
- Все записи и области данных проверять на сервере.
- Не допускать запросов к БД на каждую строку списка.
- Новые миграции создавать только для фактических изменений схемы; runtime
  grants обновлять вместе с новыми таблицами.
- Не затрагивать локальные парсеры Avito/Cian, полноту парсинга, ошибку 500 от
  05.10, общее перепроектирование авторизации или отдельную медиану по зонам.
- Перед реализацией пункта проверять, не выполнен ли он уже в `main`.

## Этап 1. Очередь закупки

Сохранить исправление `faeb021`: менеджер канбана — ответственный объекта
`Assignment.EmployeeId`, а не исполнитель задачи `WorkTask.EmployeeId`.
Сохранить выбранного менеджера в серверном запросе конкретной воронки, выровнять
набор таблицы и канбана и добавить серверную стабильную сортировку по имени
ответственного в обе стороны через заголовок столбца. Проверить несколько
менеджеров, видимость и постраничную загрузку.

## Этап 2. Отклонение и канбан

Отклонение PropertyCase атомарно перемещает все его активные участия во всех
воронках в однозначно назначенную отрицательную завершающую стадию каждой
воронки. Сравнение по названию запрещено; используется `NegativeFinal` и явное
назначение системной цели отклонения. Бизнес-история и история канбана
сохраняются в одной транзакции.

При возобновлении каждое участие возвращается в последнюю доступную рабочую
стадию этой воронки; если она недоступна — в активную начальную. Отклонённые
входящие остаются в архиве входящих, отклонённые PropertyCase — в закупке и
канбане. Входящее объявление не создаёт PropertyCase только ради отображения.

## Этап 3. Зоны и медиана

`SearchGroup` является зоной. Право создавать зоны отделяется от управления
парсерами. Создатель становится владельцем зоны и может изменять/архивировать
только свои зоны. Старые зоны без известного владельца считаются
административными. Руководитель закупок, Administrator и Owner могут управлять
всеми зонами организации. Изоляция организаций обязательна.

Владелец зоны может исключать из медианы и возвращать в расчёт только объявления
из результатов своей зоны. Руководитель закупок, Administrator и Owner могут
менять участие в медиане у любого доступного объявления организации. Отдельного
`CanManageMedian` нет: право обычного сотрудника следует из владения зоной.
Операция всегда получает контекст зоны и сервером проверяет её владельца,
видимость и принадлежность объявления; общий список и массовые операции не
могут обходить эту проверку.

Сохраняется общий `Listing.IncludeInCalculation`: изменение влияет на расчёты
всех зон, куда входит объявление. Отдельной таблицы медианы по зонам нет.
Управление зонами остаётся на странице «Поиски и парсинг»; сотрудник с правами на
зоны, но без прав на парсеры, видит только разрешённый блок зон. Во входящих
остаются выбор зоны, расчёты и ссылка на управление зонами.

## Этап 4. Экономика локаций

Переиспользовать `DemandTestPricePerSotka` и `TargetPurchasePricePerSotka` как
два независимых ручных ориентира зоны. Показать их рядом с понятными подписями.
В карточке и боковой панели для каждой связанной зоны показывать желаемую цену
покупки за сотку и вычисляемую целевую стоимость участка. В блоке «Цена и торг»
динамически показывать цену объявления, продавца и согласованную цену за сотку.
Вычисляемые значения не хранить; отсутствие/нулевую площадь обрабатывать явно.

## Этап 5. Комментарии входящих объявлений

Добавить `catalog.comment_types`, `catalog.listing_comments` и
`catalog.listing_comments_history`. Один актуальный текст каждого вида на
объявление защищается уникальностью организации, объявления и вида. История
UPDATE/DELETE записывается PostgreSQL-триггером только при содержательном
изменении. Автор операции передаётся транзакционным локальным контекстом без
утечки между соединениями пула.

Комментарии редактируют сотрудники с обработкой входящих и доступом к
объявлению. Видами комментариев управляют ProcurementHead, Administrator и
Owner. Использованный вид не удаляется вместе с данными. Во входящих — одна
компактная колонка и редактор с историей; поиск по актуальным комментариям
выполняется до пагинации, данные страницы загружаются пакетно.

## Этап 6. Адресные улучшения

Сделать существующее исправление кадастрового номера заметнее, не дублируя
сохранение и историю. Подтвердить обычный вход сотрудников и обязательную MFA
Owner/Administrator; при соответствии код не менять. Проверить создание
сотрудника, обязательные поля, валидацию, сохранение доступа и отсутствие
блокировки необязательными параметрами; исправлять только доказанные дефекты.

## Завершение

Итоговый отчёт: `reports/USER_REQUESTS_2026-10-05_08_REPORT.md`. Для каждого
этапа указать файлы, решения, миграции, фактически выполненные команды и
результаты, ограничения и SHA отправленного коммита. После этапа 6 остановиться;
production-развёртывание выполняется отдельно владельцем по runbook.

## Результат попытки публикации — 02.11.2026

Проверен актуальный GitHub main: `5fd9da257bc462fab0e0693ad460111183775a25`.
Подготовлен снимок ровно 13 файлов (277021 байт, LF), SHA каждого blob указан
в `artifacts/procurement-pipeline-search/publication/verification.json`.
Снимок ACTIVE_TASK содержит опубликованную историю main и новый этап поиска;
неопубликованная локальная история Collector исключена, локальный оригинал сохранён.
`create_tree` отклонён до записи: `MCP tool call requires approval, but approval policy is never`.
Новый commit не создан, push не выполнен. Повторное согласие владельца получено;
блокер — техническая политика сеанса. Рабочие файлы/ветка/индекс сохранены.
## Публикация завершена — 02.11.2026

После изменения разрешений опубликован коммит
`31c0c88b0a675f021afdfcb4b9c7a3fc2a123be2` —
`Add search to procurement pipeline board and table`.
Единственный родитель: `5fd9da257bc462fab0e0693ad460111183775a25`.
Подтверждены ровно 13 файлов и SHA всех blob, ahead 1 / behind 0, удалённый
head main и локальный origin/main после fetch. Force и переписывание истории
не использовались. Накопленные локальные изменения и рабочая ветка сохранены.
Рабочий сервер и БД не обновлялись. Предыдущий блокер разрешений снят.
Evidence: `artifacts/procurement-pipeline-search/publication/verification.json`.
## Публикация исходников — 02.11.2026

Владелец прямо разрешил commit/push в main и запросил скрипт обновления.
Снимок текущего Gate публикуется одним коммитом после проверенного
31c0c88b0a675f021afdfcb4b9c7a3fc2a123be2, fast-forward без force.
Предыдущие локальные изменения Collector/README и его история исключены.
ACTIVE_TASK для публикации сохраняет историю удалённого main, добавляя этот этап.
Локальные ветка/индекс сохраняются; рабочий сервер агентом не обновляется.
Добавлен scripts/Update-ProductionMedia.ps1: pin reviewed SHA, clean tracked tree,
ff-only main, versioned publish, private config backup, MaxFileBytes=524288000,
штатный deploy. Секреты/подключение не выводятся и не меняются. Миграций нет.
Синтаксис PowerShell проверен; production-выполнение скрипта выполняет владелец.
Итоговый SHA и совпадение snapshot подтверждаются отдельно в
artifacts/media-large-upload/publication/verification.json.
## Публикация завершена — 02.11.2026

Remote main: b0a18a9af7469f00756ba81ce5da8052f1f81897.
Единственный родитель — 31c0c88b0a675f021afdfcb4b9c7a3fc2a123be2.
Ровно 37 файлов текущего Gate, SHA всех опубликованных blob совпали со snapshot.
Снимок на основе удалённого main собран отдельно: 0 warnings/errors.
Force/merge/rebase не использовались. Локальные ветка/индекс/Collector сохранены.
Production не обновлялся. Готовый pinned script:
artifacts/media-large-upload/Update-Production-b0a18a9.ps1.
Проверка — publication/verification.json; синтаксис скрипта Passed.


---

# Дополнительная история GitHub main c3be8f9

Ниже сохранены отличающиеся исторические абзацы из опубликованного указателя; они не активируют старые Gate и отражают состояние на момент тех публикаций.

## Из опубликованного main: Публикация доработок LandErp за 19.11.2026

Прямой запрос владельца: опубликовать точечный отказ входящих, контроль архивных зон и технические логи Server/Worker. Снимок основан на GitHub main `1480144c8939e2cedc5bd83ec4faba99bf618a04`.

Gate: `GATE-NOV19-PUBLICATION.md`. Отчёт: `reports/NOV19_PUBLICATION_REPORT.md`.

## Из опубликованного main: Активная задача — INSPECTION-MEDIA-LAYOUT

Владелец прямо разрешил commit/push текущей переделки в main.
Публикуется ровно UI scope INSPECTION-MEDIA-LAYOUT одним коммитом после
0608fbdc1ea6dd39d3f64fba233972a00d9859fb, fast-forward без force.
Другие локальные Parser/README/диагностика и общий ACTIVE_TASK другого чата
сохраняются. В published ACTIVE_TASK — только новый media этап и история main.
Проверяется отдельная сборка точного main-based snapshot, scope, blob SHA и remote head.
Production/конфигурация/БД агентом не обновляются.
Evidence artifacts/inspection-media-layout/publication/verification.json.
---


## Из опубликованного main: Активная задача — MEDIA-GALLERY

Владелец прямо поручил commit/push обеих доработок в main и скрипт обновления.
Публикуются MEDIA-PARALLEL-UPLOAD и MEDIA-GALLERY одним коммитом после
b0a18a9af7469f00756ba81ce5da8052f1f81897, fast-forward без force.
Предыдущие локальные изменения Parser/README/диагностики и их история исключены.
ACTIVE_TASK снимка сохраняет историю remote main и только два разрешённых media Gate;
общий локальный ACTIVE_TASK другого чата не перезаписывается.
Проверка отдельной сборки точного main-based snapshot, SHA всех blob и итогового main.
Рабочая ветка/индекс и production сохраняются. Скрипт — прежний проверенный
Update-ProductionMedia.ps1 с закреплённым новым SHA в отдельной копии для владельца;
ff-only, unique version, private config backup, 500 МиБ, без migrations.
Evidence: artifacts/media-gallery/publication/verification.json.
---


## Из опубликованного main: Активная задача — MEDIA-LARGE-UPLOAD

Владелец прямо разрешил commit/push в main и запросил скрипт обновления.
Снимок текущего Gate публикуется одним коммитом после проверенного
31c0c88b0a675f021afdfcb4b9c7a3fc2a123be2, fast-forward без force.
Предыдущие локальные изменения Collector/README и его история исключены.
ACTIVE_TASK для публикации сохраняет историю удалённого main, добавляя этот этап.
Локальные ветка/индекс сохраняются; рабочий сервер агентом не обновляется.
Добавлен scripts/Update-ProductionMedia.ps1: pin reviewed SHA, clean tracked tree,
ff-only main, versioned publish, private config backup, MaxFileBytes=524288000,
штатный deploy. Секреты/подключение не выводятся и не меняются. Миграций нет.
Синтаксис PowerShell проверен; production-выполнение скрипта выполняет владелец.
Итоговый SHA и совпадение snapshot подтверждаются отдельно в
artifacts/media-large-upload/publication/verification.json.
---


## Из опубликованного main: История — INSPECTION-FILE-IMPROVEMENTS

Прямым запросом владелец разрешил commit/push этой доработки в `origin/main`.
Включаются только загрузка материалов, мягкое удаление, история/права, адресные
тесты и документы этого этапа. Предыдущие локальные изменения Collector,
README и его отчёты исключены. Локальные metadata Git доступны только для
чтения; публикация выполняется через подключённый GitHub, с проверкой исходного
и итогового SHA `main`. Рабочий сервер и БД не обновляются.
