# LandErp

LandErp — будущая ERP-система для поиска, предварительной оценки и ведения
инвестиционных проектов с земельными участками.

Сейчас репозиторий находится на исследовательском этапе. Production-кода ещё
нет. Первая активная работа — `SPIKE-001`: проверка жизнеспособности парсинга
Avito через C# 14, .NET 10 и Microsoft.Playwright.

## Начать отсюда

Человеку и ИИ-агенту сначала нужно прочитать только:

1. [`AGENTS.md`](AGENTS.md);
2. [`docs/START_HERE.md`](docs/START_HERE.md);
3. [`docs/03-active/ACTIVE_TASK.md`](docs/03-active/ACTIVE_TASK.md).

Не нужно читать все планы подряд. Документы следующих волн сохранены в
репозитории как дорожная карта, но не являются активными заданиями.

## Текущий следующий шаг

Владелец проекта открывает репозиторий в Codex и передаёт текст из
[`docs/03-active/CODEX_FIRST_TASK.md`](docs/03-active/CODEX_FIRST_TASK.md).
Этот запрос разрешает только первый Gate. Переход к следующему Gate требует
нового явного решения владельца.

## Статус

- архитектурные и продуктовые планы: подготовлены;
- стартовый пакет правил: подготовлен;
- `SPIKE-001`: спроектирован;
- первый Gate: готов к утверждению;
- исходный код: ещё не создавался.

## Сборка и запуск Gate 01

Offline-каркас `LandErp.ParserSpike` использует C# 14 и .NET 10. SDK 10.0.100
закреплён в `global.json`; допускаются стабильные patch-обновления той же feature band.
Executable зависит только от BCL. Тесты используют MSTest; версии заданы
централизованно, транзитивные версии закреплены в `packages.lock.json`.

```powershell
dotnet --info
dotnet restore LandErp.slnx --locked-mode
dotnet build LandErp.slnx -c Release --no-restore
dotnet test LandErp.slnx -c Release --no-build
dotnet run --project src/LandErp.ParserSpike -c Release --no-build -- --help
dotnet run --project src/LandErp.ParserSpike -c Release --no-build -- unknown
$LASTEXITCODE # ожидается 2
```

CLI предоставляет только help и отклоняет остальные команды без вывода входных
аргументов. Gate 01 не обращается к Avito и не запускает браузер.
Контракты отделяют классификацию, outcome и `Raw / Parsed / Presence`.
Для текстового поля допустим raw без parsed; typed value представлен nullable
value type. Диагностика содержит только opaque GUID, без файловых путей.
URL-метаданные допускают публичные HTTP(S) URL без credentials, query и fragment;
чувствительные данные в любых строковых значениях передавать запрещено.
