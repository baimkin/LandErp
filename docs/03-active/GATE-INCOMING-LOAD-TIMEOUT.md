# INCOMING-LOAD-TIMEOUT

20.11.2026. Продолжение прямого запроса исправить тормоза «Входящих»;
владелец уточнил, что не загружается сам список. Production Event 1101:
IncomingCatalogV2 / ReloadAsync / Read, DB_TIMEOUT, TimeoutException, 10396 ms.
Это доказывает timeout, но не конкретный SQL/план production.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
FAST-OPEN-TRANSFER/QUICK-DISMISS и отчёты, screen01/UI/tokens,
FP-004 §§3,9,12–15 → incoming read/query/selection/presets и целевые тесты.

Уточнение владельца 20.11: реализацию отложить, сначала полный анализ с простыми,
быстрыми и корректными вариантами ускорения. Код приложения не изменять.

Scope: воспроизвести большой count по сохранённым фильтрам на одноразовой БД,
проанализировать весь путь загрузки, рост работы/истории и предложить варианты
без реализации. Таймаут 10 секунд не увеличивать. Продуктовые файлы — только чтение:
IncomingCatalogQuery/ReadService/Selection/FilterPresetService, UI, market,
мappings/индексы/ingress и команды, влияющие на актуальность выборок.
Запись: диагностический IncomingLoadTimeoutTests.cs, этот Gate/запись ACTIVE_TASK,
reports/INCOMING_LOAD_TIMEOUT_REPORT.md и ignored измерения.
Без schema/packages/Parser/production/commit/push/перезапуска preview.
Текущую ветку, индекс и накопленные изменения сохранять.

Проверки: baseline замеры реального PostgreSQL на синтетических
данных с десятками зон/фильтров и историей observations; точные пересекающиеся
счётчики и membership, SavedFilters/All/Archive, пустая/чужая организация,
анализ действующих selection/archive/pagination инвариантов; сборка диагностики.
SQL/параметры production не собирать; публичные результаты — безопасные замеры.
В отчёте отделить локальное воспроизведение от отсутствующей production EXPLAIN.

Анализ завершён локально 20.11.2026. Reports/измерения описаны в
reports/INCOMING_LOAD_TIMEOUT_REPORT.md; 7 read-сценариев малого и 4 большого
набора Passed. На большом наборе общий count 8.890 с, full service read 11.031 с.
Код приложения не менялся; реализация намеренно не начиналась после уточнения.
