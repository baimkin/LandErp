# GATE-SPIKE-001-01 — отчёт выполнения

**Дата:** 13 сентября 2026 года  
**Ветка:** `spike/001-avito-viability`  
**Результат:** Gate 01 выполнен, готов к review владельцем  
**Режим:** offline; Gate 02 не начат

## 1. Изменённые файлы

- `global.json` — стабильный SDK 10.0.100, `latestPatch`, без preview.
- `Directory.Build.props` — .NET 10, C# 14, nullable, warnings as errors,
  рекомендованные analyzers, генерация lock-файлов.
- `Directory.Packages.props` — фиксированные версии тестового стека.
- `LandErp.slnx` — один executable и один тестовый проект.
- `src/LandErp.ParserSpike/LandErp.ParserSpike.csproj`.
- `src/LandErp.ParserSpike/packages.lock.json` — отсутствие внешних пакетов executable.
- `src/LandErp.ParserSpike/Program.cs`.
- `src/LandErp.ParserSpike/Application/SpikeCli.cs` — help и безопасный отказ.
- `src/LandErp.ParserSpike/Contracts/ContractStates.cs` — классификации, presence, outcome.
- `src/LandErp.ParserSpike/Contracts/ContractValidation.cs` — проверки метаданных.
- `src/LandErp.ParserSpike/Contracts/ObservedValue.cs` — Raw / Parsed / Presence.
- `src/LandErp.ParserSpike/Contracts/ObservationResult.cs` — оболочка результата.
- `src/LandErp.ParserSpike/Serialization/SpikeJson.cs` — JSON-контракт.
- `tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj`.
- `tests/LandErp.ParserSpike.Tests/packages.lock.json` — закреплённое транзитивное дерево.
- `tests/LandErp.ParserSpike.Tests/ContractTests.cs` — 19 выполненных тестовых случаев.
- `README.md` — команды сборки, тестов и запуска.
- `docs/03-active/reports/GATE-SPIKE-001-01_REPORT.md` — этот отчёт.

## 2. Решения

Контракты неизменяемы: списки копируются и доступны только для чтения.
Десериализация проходит через те же проверяющие конструкторы. Неизвестные
JSON-поля и числовые enum запрещены. Default classification — `Unknown`,
default outcome — `Failure`.

`Success` допускается только для `SearchResults` и `ListingDetails`.
`ListingUnavailable` также не считается успешным извлечением контента.
Остальные классификации могут иметь `Attention` или `Failure`; конкретная
политика классификатора в этом Gate не реализуется.

`Present` требует непустой raw или typed value; typed zero допустим.
`ParseFailed` требует непустой raw и запрещает typed value.
`NotInspected` и `Absent` запрещают значения, `Empty` допускает только пустой
raw без typed value. Typed value — nullable value type; текст может быть raw-only.
Контракты не выполняют обновление предыдущих наблюдений и не очищают старые поля.

Время обязательно и нормализуется в UTC, JSON использует явный offset `+00:00`.
Версии схемы и адаптера обязательны. Source, warnings и errors используют
стабильные коды из заглавных ASCII-букв, цифр и underscore (до 64 символов).
Correlation ID обязателен. Диагностика представлена только непустыми opaque GUID.

URL необязательны; допускается HTTP(S) без credentials, query и fragment.
Это консервативный контракт Gate 01, а не обработчик поисковых ссылок.
В модели отсутствуют поля cookies, headers, password и profile path.
CLI не выводит неизвестные пользовательские аргументы.

## 3. Зависимости

Executable использует только BCL. Playwright не добавлен.
Для тестов выбраны `MSTest.TestFramework` и `MSTest.TestAdapter` 4.4.0,
`Microsoft.NET.Test.Sdk` 18.10.0. MSTest поддерживается Microsoft и совместим
с .NET 10; BCL не предоставляет интеграцию обнаружения unit-тестов с `dotnet test`.
Выбор тестового framework разрешён разделом 6 утверждённого Gate.

Источники проверки поддержки и версий:
[MSTest.TestFramework](https://www.nuget.org/packages/MSTest.TestFramework/4.4.0),
[MSTest.TestAdapter](https://www.nuget.org/packages/MSTest.TestAdapter/4.4.0),
[Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.10.0).
Лицензии прямых и всех транзитивных пакетов проверены по установленным NuGet
manifest: MIT. Известные уязвимости проверены для полного дерева: не выявлены.

Транзитивные пакеты: Microsoft.ApplicationInsights 2.23.0;
Microsoft.CodeCoverage, Microsoft.TestPlatform.ObjectModel и
Microsoft.TestPlatform.TestHost 18.10.0; Microsoft.Testing.Platform,
Microsoft.Testing.Platform.MSBuild, Microsoft.Testing.Extensions.Telemetry и
Microsoft.Testing.Extensions.TrxReport.Abstractions 2.4.0; MSTest.Analyzers 4.4.0.
Это инструменты тестового проекта с `PrivateAssets=all`, они не входят в runtime
executable. Lock-файлы закрепляют версии и content hashes.
Ответственность за обновления — технический владелец тестового стека LandErp;
обновление выполняется отдельной задачей. Тестовый framework изолирован в tests
и может быть заменён без изменения executable и контрактов.

## 4. Фактически выполненные проверки

| Команда / проверка | Результат |
|---|---|
| `dotnet --info` | SDK 10.0.100, Windows win-x64, runtime 10.0.0 |
| `dotnet restore LandErp.slnx` | Успех после разрешения сетевого доступа к NuGet |
| `dotnet restore LandErp.slnx --locked-mode` | Успех |
| `dotnet build LandErp.slnx -c Release --no-restore` | Успех, 0 warnings, 0 errors |
| `dotnet test LandErp.slnx -c Release --no-build` | 19 passed, 0 failed, 0 skipped |
| `dotnet run --project src/LandErp.ParserSpike -c Release --no-build -- --help` | Help, exit code 0 |
| `dotnet run --project src/LandErp.ParserSpike -c Release --no-build -- unknown` | `CLI_UNKNOWN_COMMAND`, exit code 2; проверен `$LASTEXITCODE` |
| `dotnet format LandErp.slnx --no-restore --verify-no-changes` | Успех, изменений не требуется |
| `dotnet list LandErp.slnx package --include-transitive --vulnerable` | Известных уязвимостей не найдено |
| Поиск секретов и локальных путей профиля | Поиск по `git ls-files --cached --others --exclude-standard`; совпадений нет |
| Итоговый diff и `git diff --check` | Просмотрены README и новые файлы; ошибок whitespace нет |

Поиск охватывал private key markers, присваивания password/token/API secret,
Authorization Bearer/Basic, Cookie/Set-Cookie и типовые абсолютные пути профиля.
Вывод совпадений ограничен именами файлов и номерами строк.
JSON отдельно проверен по полному allowlist полей; неизвестное поле cookies
отклоняется. Все URL в тестах синтетические (`example.test`), навигации нет.

Первый restore в ограниченной среде завершился NU1301 из-за сетевых прав;
повтор с разрешённым доступом успешно загрузил фиксированные пакеты.
Первый test: 16 passed, 1 failed — тест ожидал точный `ArgumentException`,
но отсутствие schema version корректно вызвало `ArgumentNullException`.
Исправлено точное ожидание исключения; инвариант не ослаблен. После добавления
проверок времени и opaque IDs финальный результат — 19 passed.

## 5. Ограничения и противоречия

Этот результат не доказывает жизнеспособность парсинга Avito и не является
решением Go / Conditional Go / No-Go для SPIKE-001. Нет браузера, селекторов,
HTML-import, fixtures реальных страниц, extractor, сервера, БД или UI.

Отсутствие секретных полей не является универсальной очисткой произвольного
raw-текста или URL path: вызывающая сторона обязана передавать очищенные данные.
Импорт и sanitization относятся к будущему отдельно утверждаемому этапу.
Проверка уязвимостей отражает доступные данные NuGet на дату выполнения.

Общие `.roo` требуют отдельного подтверждения каждой команды и записи;
прямой запрос пользователя утверждает выполнение Gate и всех его проверок,
а AGENTS.md задаёт более высокий приоритет этого запроса и активного Gate.
Работа выполнена в уже существующей указанной ветке без изменения Git-истории.

В AGENTS.md маршрут начинается с README, в прямом запросе — с AGENTS.md.
Соблюдён порядок прямого запроса; README прочитан после Required reading,
до реализации. Старый статус «исходный код ещё не создавался» в README и
статусы планов не обновлены: разрешённое изменение README ограничено командами
запуска, остальные планы и ACTIVE_TASK не изменяются этим Gate.

## 6. Следующий рекомендуемый шаг

Review владельцем контрактов и результатов Gate 01. Gate 02 может начаться
только после отдельного явного утверждения пользователя. Агент остановился
на Gate 01; ACTIVE_TASK не переключён.
