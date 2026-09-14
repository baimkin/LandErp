# GATE-SPIKE-001-02 — отчёт первого live-прототипа

Дата: 13 сентября 2026 года. Ветка: `spike/001-avito-viability`.
Статус: реализован локально, ожидает ручной проверки и review владельцем.
Прямой запрос владельца разрешил заменить fixture harness минимальным UI
и немедленно выполнить первый live-проход. Gate 03 не начат.

## 1. Изменённые файлы

- `Directory.Packages.props`, `LandErp.slnx` — Playwright и Windows-проект.
- `src/LandErp.ParserSpike/LandErp.ParserSpike.csproj`, `packages.lock.json`.
- `src/LandErp.ParserSpike/Avito/SearchModels.cs`, `SearchParser.cs`.
- `src/LandErp.ParserSpike/Browser/AvitoBrowser.cs`.
- `src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj`,
  `packages.lock.json`, `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`.
- `tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj`, `packages.lock.json`,
  `SearchParserTests.cs`, `DesktopUiTests.cs`.
- `README.md`, `docs/START_HERE.md`, `docs/03-active/ACTIVE_TASK.md`.
- `docs/03-active/TP-SPIKE-001_План_реализации_прототипа_Avito.md`.
- `docs/03-active/GATE-SPIKE-001-02_Fixture_harness.md` — прежний scope отложен.
- `docs/03-active/GATE-SPIKE-001-02_Live_прототип.md` — быстрый утверждённый план.
- `docs/03-active/reports/GATE-SPIKE-001-02_REPORT.md` — этот отчёт.

Принятые контракты и тесты Gate 01 не изменены. Новые приватные исходные HTML
и fixtures реальных страниц не создавались. Профиль и изображение собственного
окна в `browser-profiles/` и `artifacts/` игнорируются Git.

## 2. Реализованные решения

WPF-окно на .NET 10 Windows: URL, Chrome/Edge, две отдельные кнопки открытия
и парсинга, статус, таблица бизнес-полей и предупреждений, сохранение JSON
по отдельному нажатию. При старте браузер не запускается и парсинга нет.
При открытии URL результата также нет; только «Спарсить» читает текущий DOM.
Кнопки блокируются на время действия, устаревший результат очищается.
Параллельных навигаций, retries, переходов по объявлениям или пагинации нет.

Видимый установленный Chrome/Edge запускается через официальный Playwright
в отдельном persistent profile. До трёх попыток навигации за запуск.
Ручные вход и CAPTCHA оставлены владельцу. Playwright 1.62.0 закреплён в CPM;
MIT. Транзитивные Microsoft.Bcl.AsyncInterfaces 6.0.0 и
System.ComponentModel.Annotations 5.0.0 — MIT (проверены NuGet manifests).
Известных уязвимостей полного дерева NuGet не обнаружено.
Браузеры и новые UI frameworks не скачивались.
Поддержка/версия пакета проверены по [официальной странице NuGet](https://www.nuget.org/packages/Microsoft.Playwright/1.62.0).

Сначала проверяются признаки защиты/ошибок, затем content state.
Только распознанная основная выдача допускается к извлечению.
Экспериментальные селекторы изолированы в браузерном адаптере;
типизация и инварианты — в чистом SearchParser.
Другие регионы после separator и известные recommendation/advert контейнеры
исключаются. До 20 видимых карточек; ID берётся из source attribute или
числовой части прямого URL. Отсутствие ID/URL отклоняет карточку, ID не выдумывается.
Цена и площадь сохраняют Raw / Parsed / Presence, площадь приводится к м².
Отсутствующие текстовые поля отмечаются предупреждениями.

Snapshot содержит только выбранные бизнес-поля и булевы признаки состояния.
Полный HTML, cookies, headers, сообщения и profile paths не читаются для отчёта.
URL query/fragment не экспортируются. SearchParseResult расширяет неизменённую
оболочку Gate 01. JSON-трансфер snapshot исправлен после фактического браузерного
теста: Playwright не создавал positional records напрямую.

## 3. Фактические проверки

| Проверка | Результат |
|---|---|
| `dotnet --info` | Стабильный SDK 10.0.100, Windows win-x64, Desktop runtime 10.0.0 |
| `dotnet restore LandErp.slnx` | Успех с разрешённым сетевым доступом |
| `dotnet restore LandErp.slnx --locked-mode` | Успех |
| `dotnet build LandErp.slnx -c Release --no-restore` | Успех: 0 warnings, 0 errors |
| `dotnet test LandErp.slnx -c Release --no-build --filter 'TestCategory!=Live'` | 27 passed, 0 failed, 0 skipped |
| `dotnet format LandErp.slnx --no-restore --verify-no-changes` | Успех |
| CLI `--help` / `unknown` | Help работает; unknown exit code 2 |
| `dotnet list LandErp.slnx package --include-transitive --vulnerable` | Известных уязвимостей нет |
| Начальное WPF-окно | Показано и отрисовано; таблица пуста, Parse/Save disabled |
| Synthetic DOM в headed Chrome | Поля читаются, другие регионы/рекомендации исключаются, CAPTCHA — Attention |
| `git check-ignore browser-profiles/spike-002/chrome artifacts/ui-startup.png` | Оба пути игнорируются |
| Поиск секретов и просмотр итогового diff | Нет найденных секретов; только scope текущего Gate |

CLI проверялся командой `dotnet run --project src/LandErp.ParserSpike -c Release --no-build -- ...`.
UI изображение создано тестом собственного окна и визуально просмотрено,
не является screenshot реальной страницы или browser trace.
Computer Use runtime `@oai/sky` не загрузился. Вместо автоматизации OS-окна
STA-тест WPF показал окно и вызвал те же отдельные обработчики кнопок.

## 4. Live-результаты

Выполнен `ProvidedPagesOpenAndParseThroughSeparateButtons`:

```powershell
dotnet test LandErp.slnx -c Release --no-build --filter 'TestCategory=Live' --logger 'console;verbosity=detailed'
```

`LANDERP_LIVE_TEST_URLS` содержал три предоставленных владельцем URL, разделённых
`|`: поисковую выдачу Пушкино и карточки 8393014504 / 8272124842.
Перед навигацией удалены context-параметры; q, cd и s поискового URL сохранены.
Исходные query/context не сохранены в Git или отчёте.
Проверка утверждала, что после открытия таблица ещё пуста, затем отдельно
нажимала «Спарсить». Финальный live-тест прошёл: 1 passed.

| Страница | Фактический результат приложения |
|---|---|
| `/pushkino/zemelnye_uchastki/prodam-ASgBAgICAUSWA9oQ` | SearchResults / Success, 20 объявлений |
| `/pushkino/zemelnye_uchastki/uchastok_263_sot._izhs_8393014504` | Captcha / Attention, 0 объявлений |
| `/pushkino/zemelnye_uchastki/uchastok_89_sot._izhs_8272124842` | ListingDetails / Failure, 0 объявлений; detail extraction не поддерживается |

Это доказывает первый технический проход по выдаче. Успех live-теста означает
выполнение кнопок без DOM/I/O ошибки, а не успешный сбор каждой страницы.
CAPTCHA не решалась и не обходилась. Четвёртая предоставленная ссылка не открывалась.

Первоначально сборка выявляла ошибки API/анализаторов — исправлены без отключения
warnings as errors. Браузерный тест выявил преобразование positional records — исправлено.
Ранний live-тест прерван ошибкой повторного Close во время Closing; исправлено
отложенным закрытием и идемпотентным освобождением browser resources.
Повторная сборка временно блокировалась ранее запущенным прототипом; закрыты
только процессы текущей задачи после проверки их пути/командной строки.
После успешной новой сборки финальный live-тест прошёл.

## 5. Ограничения первичного прототипа и следующий шаг

После ручной проверки владельцем исправлены параметры браузера:
`ChromiumSandbox = true` убирает запуск с `--no-sandbox`,
`ViewportSize = ViewportSize.NoViewport` позволяет странице изменять размер
вместе с окном. Отдельный постоянный профиль сохраняется; основной повседневный
профиль Chrome не подключается.

Повторные проверки: `dotnet build LandErp.slnx -c Release --artifacts-path artifacts/browser-fix`
— 0 предупреждений/ошибок; `dotnet test LandErp.slnx -c Release --no-build --artifacts-path artifacts/browser-fix --filter 'TestCategory!=Live'`
— 27 passed. Браузерный тест проверяет отсутствие `--no-sandbox` и увеличение
`window.innerWidth` при изменении размеров нативного окна через CDP.
Первый запуск проверки команды Chrome потребовал явного `--enable-automation`
только в тесте; параметр приложения не изменялся. Первоначальная сборка в sandbox
не могла получить NuGet audit; повторная сборка с доступом к сети прошла.
Live-проверка источника повторно не запускалась.

Обновлённый exe собран отдельно в
`artifacts/browser-fix/bin/LandErp.ParserSpike.Desktop/release/`, чтобы не закрывать
приложение и браузер владельца. Изменения применяются после запуска нового exe.

Полнота каждого поля и отсутствие посторонних карточек на реальной выдаче
ещё не сверены человеком. Success означает техническое извлечение валидных
ID/URL; отдельные поля могут иметь предупреждения или ParseFailed.
Селекторы экспериментальные; пустой/неизвестный DOM не считается успехом.
Нет detail extraction, полного fixture harness, сервера, БД, ERP UI,
установщика, автообновления, tray или защитных обходов.
Выбор источника поддерживается только для HTTPS avito.ru/www.avito.ru.

Следующий шаг — владелец открывает готовый exe, проверяет свою поисковую ссылку,
сверяет таблицу с браузером и при необходимости сохраняет JSON. Ручную CAPTCHA
проходит сам. Решение Go для всего SPIKE-001 пока не принято.
Реализация локальная: commit/push этой реализации не выполнялись без отдельного
запроса. ACTIVE_TASK остаётся Gate 02; следующий Gate не начат.

## 6. Утверждённое расширение: страницы, пауза и локальная база

13 сентября 2026 года владелец после обсуждения попросил зафиксировать план
доработок и начать реализацию. Новый scope записан в начале документа Gate 02;
ограничения первичного прототипа выше про 20 объявлений, 3 навигации и отсутствие
БД заменены этим расширением. Gate 03–08 не активировались.

### Файлы и решения

- Gate 02, ACTIVE_TASK, TP и README — утверждённый план и новый сценарий работы.
- `.gitignore` — `local-data/`, база не попадает в Git.
- CPM, ParserSpike.csproj и три lock-файла — SQLite provider и безопасный bundle.
- `Application/SearchCollector.cs` — 1–10 страниц, накопление на каждом шаге,
  дедупликация, прогресс, cancellation, ограничение 120 шагов на страницу,
  явные исходы и ручное возобновление защитных/неизвестных состояний.
- `Browser/AvitoBrowser.cs` — wheel по 480 px с 650 ms для подгрузки;
  окончание страницы после трёх стабильных наблюдений внизу; обычный клик
  доступной next-ссылки, проверка URL. URL следующей страницы не конструируется.
- `Avito/SearchModels.cs`, `Avito/SearchParser.cs` — все загруженные основные
  карточки, проверка соответствия ID и URL, дополнительные поля выдачи:
  цена за сотку с исходным текстом/типизацией, краткое описание, имя и
  информация продавца, отметки, URL одного превью без query. Фотографии
  не скачиваются, телефонные/контактные блоки не читаются.
- `Storage/ListingStore.cs` — SQLite, транзакции, parameterized SQL,
  актуальные известные поля, first/last seen, исходные наблюдения и журнал.
  Отсутствие нового поля не очищает ранее найденное. Наблюдения показывают
  исходное отсутствие, даже если актуальное представление сохраняет старое.
- MainWindow.xaml/.cs — количество страниц, прогресс, заметное уведомление,
  «Продолжить»/«Остановить», текущая выдача, поиск/сортировка сохранённых
  объявлений, детали с состояниями полей и историей, журнал запусков.
  Открытие ссылки/ручное изменение страницы не запускает сбор автоматически.
  JSON экспортирует состояние запуска, лимит/страницу и накопленные результаты.
- `CollectionStorageTests.cs`, `DesktopUiTests.cs` — проверка накопления,
  подгрузки/пагинации, защиты, ручного продолжения, остановки, SQLite и UI.
  Контракты Gate 01 не изменены.

Microsoft.Data.Sqlite/Core 10.0.0: MIT, официальный Microsoft provider,
используется без EF Core. SQLitePCLRaw bundle/core/provider/lib 2.1.13:
Apache-2.0 по nuspec. Нативная библиотека поставляется bundle, установка
сервера не нужна. Обновления принадлежат техническому владельцу Spike.
Первый restore выявил GHSA-2m69-gcr7-jv3q в транзитивной lib 2.1.11;
bundle 2.1.13 закреплён явно, audit не отключался.
Источники: [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.0),
[SQLitePCLRaw 2.1.13](https://www.nuget.org/packages/SQLitePCLRaw.bundle_e_sqlite3/2.1.13),
[advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).

### Проверки расширения

| Проверка | Фактический результат |
|---|---|
| `dotnet --info` | SDK 10.0.100, Windows x64, Desktop runtime установлен |
| `dotnet restore LandErp.slnx --locked-mode --artifacts-path artifacts/multipage` | Passed |
| `dotnet build LandErp.slnx -c Release --no-restore --artifacts-path artifacts/multipage` | Passed, 0 warnings / 0 errors |
| `dotnet test LandErp.slnx -c Release --no-build --artifacts-path artifacts/multipage --filter 'TestCategory!=Live'` | 38 passed |
| `dotnet format LandErp.slnx --no-restore --verify-no-changes` | Passed |
| `dotnet list LandErp.slnx package --vulnerable --include-transitive` | Уязвимых зависимостей не обнаружено |
| CLI нового build: `--help`, `unknown` | Exit 0 / 2, аргументы не раскрываются |
| WPF startup render: `artifacts/multipage-startup.png` | Просмотрен: кнопки/поле страниц/таблицы видимы, сбор при старте отсутствует |
| Headed Chrome + routed synthetic pages | Подгрузка по scroll, обычный next-клик, сбор трёх объявлений на двух страницах |
| Пауза через реальные обработчики WPF | CAPTCHA уведомление, сохранено объявление; продолжение только по отдельному клику |
| SQLite tests | Повторное открытие, история, поиск, пропуски не стирают поля, дубликаты не размножают объявления |
| Scope/diff | Принятые контракты и CLI не изменены; база/профиль/build игнорируются |

Первоначальная сборка расширения выявила имя свойства метаданных и требования
анализатора к культуре форматирования — исправлены без отключения проверок.
Сборка сделана отдельно от запущенной версии владельца; его окно/браузер
не закрывались. Проверки используемых селекторов основаны на synthetic DOM,
новые поля на реальном Avito ещё требуют ручной сверки.

### Ограничения и передача владельцу

Обновлённый executable:
`artifacts/multipage/bin/LandErp.ParserSpike.Desktop/release/LandErp.ParserSpike.Desktop.exe`.
База: `local-data/spike-002.sqlite`; профиль сохраняет прежний repo-путь.
Закройте прежний экземпляр и его исследовательский браузер перед открытием
новой версии, чтобы постоянный профиль не был занят.

Новая live-пагинация не запускалась автоматически: доступность next-селектора,
подгрузка с задержками и полнота дополнительных полей Avito пока не доказаны.
Три стабильных наблюдения внизу — экспериментальный критерий; очень поздняя
подгрузка может быть пропущена. Не найденная next-ссылка означает отсутствие
доступного перехода по текущим селекторам, а не доказанное окончание всего сайта.
После аварийного завершения нет автоматического восстановления запуска;
сохранённые объявления/наблюдения остаются, RUNNING в журнале может быть
остаточным состоянием. Ручное продолжение действует в пределах текущего процесса.
База не зашифрована и не является production-схемой.

Рекомендуемый шаг: владелец проверяет 2–3 страницы своей выдачи, уведомление,
таблицу и историю в локальной базе. Detail extraction, сервер, БД ERP,
обходы защиты и следующие Gate не начаты. Commit/push этого расширения не выполнялись.
