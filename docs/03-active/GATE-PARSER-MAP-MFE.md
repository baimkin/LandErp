# PARSER-MAP-MFE

16.11.2026 прямой запрос владельца: добавить новую разметку карты Avito
к существующей. Required reading: README, START_HERE, ACTIVE_TASK, этот Gate,
ранее прочитанные MAP-PINS/ATTENTION, затрагиваемый DomSourcePage/tests.
Scope: map-view-mfe и загруженные тайлы ymaps3 как видимая поверхность,
прежние canvas/svg, фильтры/контур/активное выделение/загрузка.
Файлы: DomSourcePage.cs, offline JS test/fixture, Gate/ACTIVE_TASK/отчёт.
Рабочую сборку обновить с backup/hash. Владелец подтвердил закрытие.
Без новых пакетов, Server, публикации Git или live browser тестов.
Отчёт reports/PARSER_MAP_MFE_REPORT.md.

17.11 прямой запрос: готовая карта допустима при document.readyState=interactive.
Основание: last-error 0b06a13d-b737-4c17-84f1-e5c8b8a2ac2a показывает видимые
карту/поверхность/reset, полученный контур и interactive вместо complete.
Остальные условия зоны сохраняются. JS regression tests/Release/рабочая сборка.

17.11 прямой запрос: готовая карта допустима при document.readyState=interactive.
Основание: last-error 0b06a13d-b737-4c17-84f1-e5c8b8a2ac2a показывает видимые
карту/поверхность/reset, полученный контур и interactive вместо complete.
Остальные условия зоны сохраняются. JS regression tests/Release/рабочая сборка.

17.11 прямой запрос: автоматическое сохранение рабочей проверки при ошибке
обычного сбора. В last-error.json записать последнее чтение и новую проверку
с отметками времени. Дополнительно Models/ISourcePage, LastErrorSnapshot/tests.
Сбой диагностики не влияет на исход сбора и доставку observations.

17.11.2026 разрешено дополнение диагностики: записать результат рабочего
MapSelectionScript и блокирующий элемент. Общие dom-state признаки оказались
недостаточны. Без изменения критериев видимости/сбора. Файлы DomSourcePage,
JS сценарии, MapDiagnosticTests и отчёт. Рабочая сборка после тестов.

17.11.2026 разрешено дополнение диагностики: записать результат рабочего
MapSelectionScript и блокирующий элемент. Общие dom-state признаки оказались
недостаточны. Без изменения критериев видимости/сбора. Файлы DomSourcePage,
JS сценарии, MapDiagnosticTests и отчёт. Рабочая сборка после тестов.
