# INCOMING-QUICK-DISMISS

Дата: 19.11.2026. Gate: `docs/03-active/GATE-INCOMING-QUICK-DISMISS.md`.
Запрос: после подтверждения «Не подходит» убрать строку без полного чтения
входящих, сохранив прокрутку, переход к следующему и корректную пагинацию.

## Найденный путь задержки

`ClassifyAsync` вызывал `ExecuteAsync(SetDispositionAsync)`, а общий
`WorkspaceComponent.ExecuteConfirmedAsync` после записи ожидал `ReadAsync`.
Это полный incoming read со счётчиками/справочниками плюс `Markets.ReadIncomingAsync`.
Затем стандартный переход к следующему вызывал `RegisterViewedForUiAsync`,
который после регистрации просмотра запускал такой же полный read повторно.
Это подтверждённый избыточный путь кода, а не измерение причины всех минут
ожидания в production: производственных логов/замеров в этой работе нет.

## Реализация и границы

- Прежняя `SetDispositionAsync` сохраняет причину, проверяет права/организацию
  и версию, пишет историю/аудит. Схема, правила медианы и массовый архив не менялись.
- `ExecuteConfirmedAsync` допускает ограниченное обновление после commit;
  остальные вызовы сохраняют прежний `ReadAsync`. Обновление остаётся внутри Busy.
  Его сбой сохраняет Committed и требует только чтения, не повторной команды.
- Обычные входящие, чья применённая выборка исключает Dismissed: после подтверждения
  убираются строка и её выбор, корректируется Total, закрывается подтверждение.
  Список остаётся смонтированным; у строк стабильные ключи. Ошибка/Unknown
  сохраняет карточку, строку и причину; ошибка показывается внутри подтверждения.
- Лёгкий `ReadWindowAsync` использует ту же серверную подготовку условий,
  сортировку и проекцию строк, но не считает фасеты, не перечитывает справочники
  и рынок. Для AllListings не загружает сохранённые фильтры.
  Для SavedFilters сервер по-прежнему получает актуальные критерии/совместимость.
- После удаления страницы сохраняют нормальные Offset/Size: недостающая строка
  читается с конца текущего окна. При исчезновении последней страницы читается
  только окно предыдущей. При Total=0 запрос подгрузки не нужен.
- Следующее объявление получает прежнюю серверную регистрацию просмотра
  и отдельное чтение карточки, без общего read. Срез New учитывает его выход
  из выдачи после просмотра и восполняет окно. Конец выдачи не открывает заново
  первое объявление предыдущей страницы. При выключенном переходе карточка закрывается.
- Явный выбор прочих строк и «все результаты минус исключения» сохраняются;
  устаревший штамп массовой операции сбрасывается для новой проверки preview.
- Маленький JS-модуль сохраняет вертикальную прокрутку страницы/предков и
  горизонтальную прокрутку таблицы. Ошибка JS не препятствует бизнес-команде.
  При уходе с экрана позиция предыдущего экрана не восстанавливается.
- В успешном fast-path не остаётся плавающее уведомление, перекрывающее
  пагинацию/кнопки следующей карточки. Подтверждение видно по результату действия;
  сообщения о сбоях сохраняются.
- Сводные счётчики фильтров остаются последним снимком до обычного чтения
  (фильтры, страница, обновление). Текущее количество и диапазон меняются локально.
  Архив/участники/отборы, допускающие Dismissed, сохраняют прежнее чтение,
  чтобы не менять их бизнес-состав.

## Файлы текущей доработки

- `src/LandErp.Application/Modules/Catalog/Public/IncomingCatalogReadContracts.cs`
- `src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs`
- `src/LandErp.Server/Components/WorkspaceComponent.cs` — только новый callback
  ограниченного обновления, прежние независимые доработки сохранены.
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor` — ключи строк,
  fast-path отказа, точка прокрутки, ошибка/Busy подтверждения; накопленное сохранено.
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.Dismiss.cs`
- `src/LandErp.Server/wwwroot/js/incoming-list-position.js`
- `tests/LandErp.Foundation.Tests/CatalogBulkArchiveUiTests.cs` — partial/harness.
- `tests/LandErp.Foundation.Tests/IncomingQuickDismissTests.cs`
- `tests/LandErp.Foundation.Tests/IncomingQuickDismissBrowserTests.cs`
- `tests/LandErp.Foundation.Tests/WorkspaceOperationTests.cs` — callback/commit test.
- `docs/14-ui-kit/screens/01-incoming-listings.md`
- Gate, собственная запись `ACTIVE_TASK.md`, этот отчёт.

## Проверки

Команды из корня репозитория; секретные строки подключения не выводились.

```powershell
dotnet restore tests/LandErp.Foundation.Tests --locked-mode --artifacts-path artifacts/incoming-quick-dismiss/final-build
dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/incoming-quick-dismiss/final-build --disable-build-servers -p:UseSharedCompilation=false
$env:LANDERP_DOTNET = (Get-Command dotnet).Source
$env:LANDERP_TEST_BUILD_ROOT = (Resolve-Path artifacts/incoming-quick-dismiss/final-build).Path
dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --no-restore --artifacts-path artifacts/incoming-quick-dismiss/final-build --filter 'FullyQualifiedName~QuickDismiss|FullyQualifiedName~WorkspaceOperationTests' --logger 'console;verbosity=normal' --logger 'trx;LogFileName=quick-dismiss-final.trx' --results-directory artifacts/incoming-quick-dismiss/tests
```

- Locked restore: Passed; проверка NuGet выполнена с доступом к сети.
- Финальная Release-сборка: Passed, 0 warnings / 0 errors.
- Предварительный адресный прогон CatalogBulkArchiveUiTests + WorkspaceOperationTests:
  **25 Passed**, 0 Failed/Skipped, 7m52s. Включает 4 новых UI/PostgreSQL сценария,
  старые 25/50/100, сортировку, явный/массовый выбор, архив и безопасные исходы записи.
  В тестах общего read и рынка после отказа запрещены вызовы; проверена подгрузка
  ровно одной строки и отсутствие пропусков/повторов при следующих страницах.
- Прогон на final-build: **21 Passed** (5 новых PostgreSQL/UI сценариев +
  16 WorkspaceOperationTests). Первый браузерный сценарий в том прогоне не прошёл
  сравнение прокрутки: подготовка теста снимала позицию во время CSS smooth scroll.
  Диагностика подтвердила вызов восстановления сохранённой координаты. Подготовка
  исправлена на instant scroll; тест не подменяет модуль приложения.
- Следующий браузерный проход подтвердил прокрутку/следующее, но выявил реальное
  перекрытие «Далее» плавающим Success-toast. Успешный fast-path приведён к прежнему
  поведению перехода к следующему: без постоянного toast. Ошибки не скрываются.
- Заключительный браузерный тест: **1 Passed**, 0 Failed, 47s;
  `quick-dismiss-browser-accepted.trx`. Проверены реальные клики, 25 строк →
  подтверждённый отказ → 25 строк с одной новой, та же таблица в DOM,
  вертикальная/горизонтальная позиция, правильная следующая карточка,
  «Далее» → 5 строк, ровно 30 уникальных оставшихся ID, сохранённая классификация,
  отсутствие JS errors и горизонтального overflow на 390px.
  Скриншоты `browser/before-confirm-1440.png`, `after-confirm-1440.png`,
  `remaining-page-390.png` просмотрены. На 390px меню осталось открытым;
  этот снимок подтверждает только технический overflow-check, а не полную
  визуальную приёмку мобильного интерфейса. Тестовые host/browser закрыты.
- Ранний restore без сети не прошёл NU1900; повтор с доступом прошёл.
  Промежуточная пересборка во время старого testhost столкнулась с блокировкой DLL;
  окончательная сборка вынесена в отдельный final-build и прошла без предупреждений.

## Ограничения и следующий шаг

Production не изменялся; существующий preview не останавливался/не заменялся.
Другие накопленные задачи, ветка и индекс сохранены. Commit/push/deploy не выполнялись.
Конкурентные внешние изменения могут потребовать обычного обновления списка;
новая операция не вводит длительный серверный снимок выдачи.
Gate локально завершён. Следующий шаг — отдельно согласованная публикация пакета.
