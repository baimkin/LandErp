# Компактные строки ориентиров группы

Дата: 21.10.2026. Ветка: `codex/procurement-manual-search-group`.
Основание: прямой запрос владельца по скриншоту — убрать лишнюю высоту строк
«Ориентир теста спроса» и «Желаемая цена покупки».

## Причина и исправление

Глобальное правило `.incoming-v2 .v2-stat span` из incoming-v2-workflow.css
задавало display:block и верхний margin всем вложенным span, включая контейнер
значения и единицу измерения. Внутренний редактор цены также не задавал свою
горизонтальную раскладку значения и кнопки редактирования.

- Правило KPI ограничено непосредственной подписью `.v2-stat > span`.
- DemandTestPrice размещает значение и кнопку через inline-flex с выравниванием
  по центру. Подсказка редактирования и ошибки сохраняют отдельную строку.
- Контейнер значения и единицы GroupMarketPrices заменён со span на div,
  соответствующий блочному корню дочернего компонента. Единица измерения и
  редактор держатся вместе. На узкой ширине строка переносится целым блоком
  значения, без уменьшения шрифта.
- Исправление общее для обоих ориентиров в обычном и закупочном вариантах.
  Состав показателей, значения, права, сохранение и вычисления не менялись.

## Файлы

- `src/LandErp.Server/wwwroot/css/incoming-v2-workflow.css`.
- `src/LandErp.Server/Components/Primitives/DemandTestPrice.razor.css`.
- `src/LandErp.Server/Components/Primitives/GroupMarketPrices.razor`.
- `src/LandErp.Server/Components/Primitives/GroupMarketPrices.razor.css`.
- `docs/03-active/ACTIVE_TASK.md` и этот отчёт.

Ранее реализованное назначение группы объекта закупки сохранено.

## Проверки

`dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LocationEconomicsUiKeepsBothGuidesAllZonesAndExplicitUnavailableCalculations" --logger "console;verbosity=normal" --logger "trx;LogFileName=group-market-compact-rows.trx" --results-directory artifacts/tests/group-market-compact-rows`

Passed: 1/1. Команда также успешно собрала Server и зависимости в Release.
Это существующая статическая проверка состава представления, не проверка
браузерной геометрии. Новые тесты для CSS-правки не добавлялись.
`git diff --check` — Passed.

Исходный скриншот просмотрен. Браузер/CUA/Playwright и визуальная приёмка не
выполнялись согласно границам задачи. Рабочая БД/сервер не обновлялись,
на этапе реализации commit/push не выполнялись. После установки проверить обе строки с пустыми
и заполненными ориентирами, редактирование и узкую ширину карточки.

21.10.2026 владелец отдельно разрешил общий коммит этой доработки и назначения
группы объектам закупки, затем отправку в `origin/main`. Этот отчёт входит в
публикацию; рабочий сервер и БД владелец обновляет отдельно. Фактический SHA
и синхронизация с GitHub проверяются после push и сообщаются в чате.
