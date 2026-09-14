# TP-LOCAL-001 — отчёт точечных доработок

Дата: 2026-09-14. Ветка: spike/001-avito-viability.
Основание: прямое «давай сделаем» владельца для четырёх исправлений,
мешающих испытанию на объёме. Доработки реализованы локально.
Приёмка, Go и переход к следующему Gate не объявлены.

## Scope и решения

1. **Переходы и таймауты.** Клик по номеру/следующей странице больше не требует
   ответа нового HTML-документа: поддерживается обновление выдачи внутри страницы.
   Проверяются номер страницы, совместимость фильтров и появление новых карточек
   либо нового документа. Одной смены адреса недостаточно для успешного перехода.
   Если SPA оставила тот же набор ID, ожидание ограничено таймаутом: безопаснее
   сохранить прогресс до перехода, чем приписать старую выдачу следующей странице.
   Прерванная навигация закрывает только принадлежащую сборщику вкладку при
   освобождении. Начальный адрес после загрузки используется как фактический
   адрес поиска; преобразование сайтом отдельно записывается.
   При сравнении разрешены известные эквиваленты Avito: отсутствующий
   localPriority=0, www и переход между общей недвижимостью/земельными участками
   в том же регионе. Изменение фильтров не объявляется изменением «вручную»:
   источник изменения неизвестен, сравнение доступно в журнале.
2. **Сохранение ссылок и UI.** Публичные HTTPS-ссылки Avito/Cian с SEO-путём,
   поддоменом, картой и неизвестными обычными фильтрами сохраняются.
   География, повторяющиеся параметры и новые фильтры не удаляются.
   Tracking/context удаляются; известные параметры авторизации отклоняются.
   Сохранение отделено от возможности парсинга: detail URL сохраняется с
   предупреждением и явным отказом при обработке, detail-парсер не добавлен.
   Ошибка рядом с полем выделена цветом, введённый адрес остаётся для исправления.
   Успех также подтверждается рядом с полем; повторный клик при сохранении отключён.
   Неизвестная разметка карты не становится пустой успешной выдачей.
3. **Прокрутка.** Колесо отправляет небольшие события; шаг, интервал, число
   событий серии и пауза между сериями доступны в настройках. Новые значения:
   100 px / 20 мс / 12 событий / 40 мс вместо прежних 60 px / 60 мс / 6 / 250 мс.
   Сохранённые ранее шаг и интервал не переписываются; для ускорения их нужно
   поставить 100 и 20. Пауза больше не зашита в цикл сбора.
   Скорость не является обходом защиты, CAPTCHA/вход/429 требуют ручного действия.
4. **Диагностика.** Append-only JSONL в local-data/diagnostics/collection.jsonl:
   задача, запуск, источник, вкладка, страница, действие, начало/конец, длительность,
   тип ошибки, сохранённые результаты, безопасные ожидаемый/фактический адреса
   и причина завершения. Есть событие приостановки для ручной проверки источника.
   Вкладка «Диагностика» показывает последние 300 событий, с фильтром задачи.
   Не записываются HTML, заголовки, cookies, токены или исходный текст исключений
   Playwright. Значения неизвестных параметров в диагностическом URL маскируются.
   Файл находится в исключённой из Git папке; для разбора проблем можно передать его.

## Изменённые файлы этого запроса

- src/LandErp.ParserSpike/LocalCollection/SearchUrls.cs
- src/LandErp.ParserSpike/LocalCollection/DomSourcePage.cs
- src/LandErp.ParserSpike/LocalCollection/QueueRunner.cs
- src/LandErp.ParserSpike/LocalCollection/Models.cs
- src/LandErp.ParserSpike/LocalCollection/DiagnosticJournal.cs — новый
- src/LandErp.ParserSpike.Desktop/SettingsEditor.cs
- src/LandErp.ParserSpike.Desktop/WorkspaceController.cs
- src/LandErp.ParserSpike.Desktop/WorkspaceWindow.xaml
- src/LandErp.ParserSpike.Desktop/WorkspaceWindow.xaml.cs
- tests/LandErp.ParserSpike.Tests/CollectionFixesTests.cs — новый
- tests/LandErp.ParserSpike.Tests/LocalCollectionTests.cs
- tests/LandErp.ParserSpike.Tests/QueueRunnerTests.cs
- tests/LandErp.ParserSpike.Tests/WorkspaceUiTests.cs
- README.md
- docs/03-active/ACTIVE_TASK.md
- docs/03-active/reports/TP-LOCAL-001_REPORT.md
- этот отчёт.

Другие изменения в рабочем дереве существовали до этого запроса.
Новых зависимостей и изменений схемы SQLite нет. Профили и база пользователя
сохранены; рабочие окна пользователя не закрывались.

## Проверки и результаты

Изолированная сборка: artifacts/local-001-fixes, чтобы не заменять запущенный exe.
Фактически выполненные команды:

    dotnet restore LandErp.slnx --locked-mode -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-fixes
    dotnet build LandErp.slnx --no-restore -c Release -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-fixes
    dotnet test LandErp.slnx --no-build --no-restore -c Release -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-fixes --filter "TestCategory!=Live" --logger "trx;LogFileName=fixes-offline.trx" --results-directory artifacts/local-001-fixes/test-results --blame-hang-timeout 60s
    $env:ArtifactsPath='C:/.Projects/LandErp/artifacts/local-001-fixes'
    dotnet format LandErp.slnx --no-restore --verify-no-changes --include src/LandErp.ParserSpike/LocalCollection src/LandErp.ParserSpike.Desktop/WorkspaceController.cs src/LandErp.ParserSpike.Desktop/WorkspaceWindow.xaml.cs src/LandErp.ParserSpike.Desktop/SettingsEditor.cs tests/LandErp.ParserSpike.Tests/CollectionFixesTests.cs tests/LandErp.ParserSpike.Tests/LocalCollectionTests.cs tests/LandErp.ParserSpike.Tests/QueueRunnerTests.cs tests/LandErp.ParserSpike.Tests/WorkspaceUiTests.cs
    dotnet list LandErp.slnx package --vulnerable --include-transitive --no-restore
    git diff --check

- Locked restore: успешно. Release build: 0 предупреждений / 0 ошибок.
- Офлайн: **67 / 67 Passed**, 0 пропущенных, около 21 с.
- Регрессии: обычная пагинация, SPA с отложенной заменой карточек, SEO/карта,
  сохранность дополнительных фильтров, заметная ошибка UI без потери ввода,
  корректные паузы/CAPTCHA и продолжение другого источника, остановка на каждом
  браузерном действии, защита незавершённого checkpoint, журнал таймаута без
  секретного текста исключения. Прежние проверки SQLite/очереди/объёма также Passed.
- Format verify и git diff --check: успешно.
- NuGet audit: известных уязвимых пакетов не найдено во всех трёх проектах.
- Результаты: artifacts/local-001-fixes/test-results/fixes-offline.trx.
- При первом запуске тестов найдён и исправлен неверный регистр полей
  JS snapshot в ожидании перехода. Live-тест случайно включился без входных URL;
  финальный запуск явно исключает категорию Live. Это не live-проверка.
- Сетевые проверки и тестовые браузеры запускались с разрешением среды.

## Ограничения и следующий шаг

Live-навигация Avito/Cian после исправлений и длительный сбор на объёме —
**NotVerified**. Баны не были причиной, подтверждённой наблюдением владельца;
новый журнал позволит отделить загрузку, переход, классификацию и сохранение.
Повторные timeout не скрываются автоматическими повторами.

Закрыть прежнюю версию, запустить
artifacts/local-001-fixes/bin/LandErp.ParserSpike.Desktop/release/LandErp.ParserSpike.Desktop.exe,
при необходимости ускорить сохранённые параметры колеса, обработать выбранные
ссылки. При ошибке выбрать задачу → «Диагностика» → «Обновить журнал» и передать JSONL.
Новый live-тест принадлежит владельцу. Реализация останавливается здесь;
commit/push — только по отдельному запросу.
