# Публикация доработок 19.11.2026

По прямому запросу владельца подготовлен отдельный снимок на точном GitHub main `1480144c8939e2cedc5bd83ec4faba99bf618a04` (дерево `d15b8f61cceebee43a3958a405cc25eff6f90178`). Текущая рабочая ветка, индекс и preview сохранены.

- **Входящие.** Подтверждение «Не подходит» ограниченно обновляет видимое окно. Файлы: `IncomingCatalogV2.razor`, `IncomingCatalogV2.Dismiss.cs`, `incoming-list-position.js`, входящие контракты и сервис чтения, `WorkspaceComponent.cs`, адресные тесты и спецификация экрана 01.
- **Архивные зоны.** Серверные проверки запуска, расписания, повтора и выдачи заданий учитывают архив. Файлы: `CollectionSearchAvailability.cs`, `CollectionAdministration.cs`, `CollectionScheduler.cs`, `CollectorGateway.cs`, `Collectors.Workspace.cs`, `Collectors.razor` и `Collectors.razor.cs`, тесты архива и спецификация экрана 07. Проверка архива перенесена на опубликованную реализацию кнопок запуска и повтора. Тест UI использует самостоятельную оснастку.
- **Технические логи.** Дневная ротация по Москве, 10 МиБ на файл, 7 суток хранения, поиск по ID инцидента и диагностика сохранения общения. Файлы: `Run-ProductionProcess.ps1`, `Deploy-ProductionRelease.ps1`, `ProductionLogRunner.cs`, `Find-ProductionIncident.ps1`, `Test-ProductionLogging.ps1`, `Program.cs` Server/Worker, `WorkspaceComponent.cs`, `WorkspaceFailureDiagnostics.cs`, `CaseCommunication.razor`, адресные тесты и runbook.

Миграций, изменений зависимостей и исходников локального Parser в снимке нет.

## Проверки снимка

- `dotnet restore LandErp.slnx --locked-mode --artifacts-path artifacts/build -p:NuGetAudit=false` — успешно из имеющегося кэша. NuGet audit отключён для проверки в среде с ограниченным сетевым доступом.
- `dotnet build tests/LandErp.Foundation.Tests -c Release --no-restore --artifacts-path artifacts/build` и отдельный Release build `src/LandErp.Worker` — 0 предупреждений и ошибок.
- Полная сборка решения выявила существующий CA1305 в `tests/LandErp.ParserSpike.Tests/CollectionFixesTests.cs:97`. Файл совпадает с базовым коммитом.
- Адресный `dotnet test` для QuickDismiss, WorkspaceOperationTests, WorkspaceFailureDiagnosticsTests, ParsingArchive, ParsingLargeListsTests, CollectionPoolTests и CollectionSchedulingTests — 53 Passed, 1 Failed. Единственный не пройденный browser-тест остановился во время старта тестового хоста на импорте HTTPS-сертификата Kestrel (`CryptographicException`). Повтор подтвердил сбой старта. В первоначальном отчёте Quick Dismiss зафиксирован успешный браузерный запуск на локальном снимке.
- `powershell.exe -File scripts/Test-ProductionLogging.ps1` — 35 Passed. `node scripts/Test-ParsingLists.mjs` — 11 Passed. `git diff --check` — Passed.

Скрипт обновления фиксирует опубликованный SHA, использует один checkout и существующий адрес из задачи Планировщика. Сборка создаётся в отдельной папке релиза. Штатное развёртывание проверяет здоровье приложения и сохраняет предыдущий релиз. Факт публикации и SHA проверяются отдельно от этого файла. Production в рамках публикации исходников не обновлялся.
