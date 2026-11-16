# Большие списки поисков и парсинга

16.11.2026. Gate PARSING-LARGE-LISTS, прямой запрос владельца.

## Поведение

Зоны и поиски показывают все строки текущего отбора без страниц. На компьютере
таблица прокручивается по доступной высоте окна, заголовки колонок закреплены,
фильтры и счётчик отбора остаются снаружи. На телефоне вертикальная прокрутка
обычная, по странице, горизонтальная — внутри широкой таблицы.

История загружается с сервера по 50 через «Загрузить ещё», включая записи старше
последних 100. Поиск по названию поиска, зоне и парсеру, источник и результат
применяются до чтения порции. Составной курсор CreatedAt/Id не смещается от
новых запусков между запросами, одинаковые даты не дают пропусков и дублей.
Старый ReadAsync по умолчанию сохраняет 100 последних jobs для совместимости;
эта страница отключает их лишнее чтение и использует отдельное чтение истории.

Вкладки списков остаются смонтированы. Фильтры, активная вкладка, объём истории
и положение видимой строки сохраняются при переключениях, правке, обновлении
раз в 15 секунд и перезагрузке страницы в той же вкладке браузера. Позиция
привязана к ID строки, поэтому изменение высоты уведомления не сдвигает контекст.
Новый фильтр возвращает соответствующий список к началу, историю — к 50 строкам.
Устаревшее чтение истории отменяется и не заменяет результат нового фильтра.

Состояние sessionStorage разделено по организации/сотруднику. Значения проверяются
и ограничиваются; повреждённое или недоступное хранилище не ломает страницу.
Это состояние одного браузерного сеанса, не синхронизация между устройствами.
Сервер отдельно проверяет Collection Read и организацию; команды, права и аудит
не менялись. Настройки браузера не являются разрешением на чтение истории.

## Файлы

- Application/Modules/Collection/Public/CollectionServices.cs — запрос/курсор/порция.
- Infrastructure/Modules/Collection/CollectionAdministration.cs, CollectionHistory.cs — совместимость и серверный отбор.
- Server/Components/Pages/Collectors.razor, .razor.cs, .razor.css, .Workspace.cs, .Lists.cs — списки и состояние.
- Server/wwwroot/js/parsing-lists.js — прокрутка, видимая строка, восстановление.
- tests/LandErp.Foundation.Tests/ParsingLargeListsTests.cs и ParsingWorkspaceBrowserTests.cs.
- scripts/Test-ParsingLists.mjs — проверка недоверенного browser storage.
- docs/14-ui-kit/screens/07-parsing-management.md, Gate, ограниченная запись ACTIVE_TASK, этот отчёт.

Пути Application/Infrastructure/Server выше относятся к соответствующим проектам
src/LandErp.Application, src/LandErp.Infrastructure и src/LandErp.Server.
Принятый HTML v2 не переписывался, накопленные работы сохранены.

## Проверки

Locked restore и Release выполнены в отдельном output
artifacts/parsing-large-lists/build, существующий local preview не остановлен.
Restore потребовал доступ к NuGet для аудита; пакеты/lock-файлы не менялись.
Финальная сборка: 0 warnings/errors.

Команды (секрет тестовой БД передаётся существующим окружением, не аргументами):

```powershell
dotnet restore tests/LandErp.Foundation.Tests --locked-mode --artifacts-path artifacts/parsing-large-lists/build
dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/parsing-large-lists/build --disable-build-servers -p:UseSharedCompilation=false
$env:LANDERP_DOTNET = (Get-Command dotnet).Source
$env:LANDERP_TEST_BUILD_ROOT = (Resolve-Path artifacts/parsing-large-lists/build).Path
dotnet test tests/LandErp.Foundation.Tests -c Release --artifacts-path artifacts/parsing-large-lists/build --no-build --no-restore --filter 'FullyQualifiedName~ParsingLargeListsTests|FullyQualifiedName~ParsingWorkspaceBrowserTests|FullyQualifiedName~ParsingWorkspaceTests' --logger 'trx;LogFileName=final.trx' --results-directory artifacts/parsing-large-lists/results
dotnet test tests/LandErp.Foundation.Tests -c Release --artifacts-path artifacts/parsing-large-lists/build --no-build --no-restore --filter 'FullyQualifiedName~CollectionPoolTests|FullyQualifiedName~CollectionSchedulingTests|FullyQualifiedName~CollectionSearchFormTests' --logger 'trx;LogFileName=collection-regression.trx' --results-directory artifacts/parsing-large-lists/results
node scripts/Test-ParsingLists.mjs
```

JS: 11 случаев Passed — неверный JSON, неверные типы, границы, запрет storage.
Финальный .NET запуск: 10/10 Passed; дополнительная проверка существующего
пула/расписаний/форм: 22/22 Passed, всего 32 разных тестовых случая, 0 Failed.
Результаты: artifacts/parsing-large-lists/results/final.trx и collection-regression.trx.
PostgreSQL: 176 исходных jobs, 175 с одним временем, новая запись между порциями,
полное покрытие без дублей, старый поиск, источники/результаты, буквальные %/_,
совместимость старого ReadAsync, чужая организация, read-only и запрет без Collection Read.

Настоящий Server/Edge/PostgreSQL использует одноразовую БД: 70 зон, 120 поисков,
177 исходных запусков; история 50/100/150, догрузка без прыжка, восстановление после
reload, поиск за пределами последних 100, правка/сохранение/постановка запуска,
таймер с открытой формой, фильтр источника, прокрутка после редактирования,
права и клавиатура. Размеры 1720/1440/1024/390 px. Браузерный HTTPS требует
доступа Windows к временному ключу сертификата. Реальные источники не вызываются.

При проверках исправлен явный escape для буквальных %/_ в PostgreSQL ILIKE.
Проверка mobile сравнивает видимую строку, а не абсолютный scrollY: исчезающее
после reload уведомление закономерно меняет абсолютное число пикселей.
Это адресные проверки, не полный foundation suite и не production acceptance.
Ошибок JS и внешних запросов в итоговом браузерном сценарии — 0. Переполнения
документа на проверенных размерах нет. Итоговые desktop/tablet/mobile снимки
просмотрены: artifacts/parsing-large-lists/browser/, включая zones-scrolled-1720.png,
1-1440.png, 2-1024.png, 3-390.png и zones-restored-390.png.
git diff --check для затронутых tracked файлов прошёл. Итоговые source hashes
и ссылки на evidence: artifacts/parsing-large-lists/verification.json.

## Границы

Без миграций и новых пакетов. Production, commit/push и новый Gate не выполнялись.
Работающий старый preview не перезапускался: финальную версию проверяет отдельный
тестовый host. Следующий шаг — просмотр владельцем и отдельный запрос публикации.
