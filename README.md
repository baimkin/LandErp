# LandErp

Stage 1 — Procurement Core: самостоятельный Collector Avito/Cian, ASP.NET Core /
Blazor Server и PostgreSQL. Первый контур закупки включает очередь, рабочую
карточку, первичный анализ, передачу руководителю, возврат и решения с историей.

## Запуск на Windows без Docker

1. Нужны PostgreSQL 18 и SDK `10.0.401` из `global.json`. Проверьте выбранную
   версию командой `dotnet --version`; скриптам передавайте `-DotnetPath dotnet`.
2. Выполните locked restore и Release build:

   ```powershell
   dotnet restore LandErp.slnx --locked-mode
   dotnet build LandErp.slnx -c Release --no-restore
   ```

3. Настройте `LANDERP_TEST_ADMIN_CONNECTION` локально вне Git и выполните
   `./scripts/Initialize-Local.ps1 -DotnetPath dotnet`.
   Отдельный инструмент создаёт только новую `landerp_local`, migration/runtime
   роли и применяет migrations. Неизвестную существующую БД он не перезаписывает.
   Повторный запуск использует собственные ignored settings и применяет новые
   migrations. Server/Worker startup никогда не меняет schema.
4. Первоначальные данные Owner находятся только в
   `local-data/stage1/owner-access.txt`. Откройте файл приватно. Запустите
   `./scripts/Start-Local.ps1 -DotnetPath dotnet`,
   откройте `https://localhost:7240`, войдите и настройте authenticator MFA.
   При необходимости доверьте локальный сертификат штатной командой
   `dotnet dev-certs https --trust` через выбранный SDK.
5. Локальный launcher Server также запускает Worker расписаний и останавливает
   свой Worker при завершении Server. На странице «Поиски и парсинг» проверьте
   состояние «Расписания: работает». Для запуска только сайта предусмотрен
   параметр `-ServerOnly`. Worker также можно запустить независимо:
   `./scripts/Start-Local.ps1 -Service Worker -DotnetPath dotnet`.
   Он проверяет сроки каждые 30 секунд и ставит задания в общую очередь.

Credentials не передавайте аргументами команд и не сохраняйте в истории терминала.
Runtime использует отдельную роль без DDL; history разрешает только SELECT/INSERT.
Instant хранится UTC, бизнес-время явно Europe/Moscow. Деньги decimal/RUB.

## Пройти закупку

1. Owner/Admin создаёт отдел закупки, должности и сотрудников через
   `/organization` и `/employees`. Выберите роли `ProcurementManager` и
   `ProcurementHead`, нужные должности и отдел. Для первичной очереди выберите
   область `Department` либо `Team` с подходящей командой. `Own` и
   `AssignedObjects` показывают уже назначенные кейсы. Приглашения активируются
   через `/account/activate`; ID/одноразовый код передаются вручную приватно.
2. На `/collectors` создайте machine identity Collector. Сохраните показанные
   один раз ID/token в локальные переменные процесса Collector:
   `LANDERP_COLLECTOR_AGENT_ID`, `LANDERP_COLLECTOR_TOKEN`,
   `LANDERP_COLLECTOR_SERVER_URL=https://localhost:7240/`.
   Server хранит только hash и metadata; отзыв/перевыпуск выполняется здесь же.
3. Создайте поиск для этого Collector с HTTPS URL Avito/Cian, отделом/командой и
   пределом страниц; поставьте сбор. В существующем WPF Collector откройте вкладку
   «LandErp Server», подключите Server и получите разрешённую работу. CAPTCHA и
   авторизация решаются вручную; обхода защиты нет.
4. Результат Collector появляется в общем входящем Catalog на `/incoming`.
   Там же менеджер может вручную добавить предложение из Telegram, от агента,
   собственника или другого источника без fake Collector/Job/Observation.
5. Действие «Взять в работу» создаёт самостоятельный `PropertyCase`, а
   «Связать» добавляет источник к существующему case. Канонический адрес карточки —
   `/procurement/{CaseId}`; очередь `/procurement` содержит только cases.
6. В карточке менеджер сохраняет заметки/результат ручного контакта и передаёт
   выбранному руководителю. Руководитель возвращает с причиной и указаниями либо
   принимает решение. Исходные объявления и их изменения остаются отдельными от
   рабочих фактов case; один case поддерживает несколько источников.
7. Для полевого осмотра менеджер или руководитель открывает вкладку «Осмотр»,
   назначает сотрудника с ролью `Inspector`, срок и короткое задание. Осмотрщик
   работает с телефона через `/inspections`: видит только назначенные ему
   объекты, заполняет существующий чек-лист и прикладывает материалы. Назначение
   осмотра не даёт права принимать решение по закупке или подтверждать покупку.

Local mode Collector доступен независимо от Server. При обрыве связи результаты
остаются в локальном outbox; «Повторить доставку» отправляет те же ResultId.
После истечения lease повторное получение работы обновляет fencing token и
доставляет сохранённый результат. Heartbeat/retry требуют открытого Collector.

## Production preparation

Первый production-контур Windows — versioned publish, managed Server/Worker, HTTPS reverse proxy, persistent Data Protection keys, backup/restore и rollback — описан в [Production Runbook](docs/03-active/PRODUCTION_RUNBOOK.md).

Первый запуск использует отдельные явные команды
`Initialize-ProductionDatabase.ps1` и `Initialize-ProductionOwner.ps1`;
Server/Worker сами не создают БД, роли, миграции или владельца.

Локальный `Start-Local.ps1` остаётся только development launcher и не является production supervisor.

## Проверки

Яндекс Диск как пилотное хранилище вложений: [настройка и отключение](docs/03-active/YANDEX_DISK_SETUP.md).
Доступ ограничен папкой приложения; production заказчика подключается отдельно.

`./scripts/Test-Foundation.ps1 -DotnetPath dotnet`
использует настоящую PostgreSQL и удаляет только свои disposable test databases.
Офлайн Collector tests запускаются с фильтром `TestCategory!=Live`; CI не зависит
от marketplace. Backup/test artifacts и все local-data игнорируются Git.

Read-only inspection локальной схемы:

```powershell
$env:LANDERP_REPOSITORY_ROOT = (Get-Location).Path
dotnet run --project src/LandErp.LocalSetup -c Release --no-build -- --inspect
```

Он проверяет comments и запрет DDL откатываемой пробой; migrations не применяет.
`/health/live` проверяет процесс, `/health/ready` — PostgreSQL 18 и полную схему.

## Контекст

README → [START_HERE](docs/START_HERE.md) → [ACTIVE_TASK](docs/03-active/ACTIVE_TASK.md)
→ Required reading → затрагиваемый код. Stage 2 требует отдельного запроса.

Рабочий Collector checkpoint `3994644d5a00413bb53513b78c5492c7e0b70692` и ERP docs
объединены безопасным merge внутри `codex/stage-1-procurement-core`. Границы
[ADR-007](docs/02-decisions/ADR-007_Collector_как_самостоятельный_продукт_и_граница_с_Server.md)
сохранены: Collector не подключается к PostgreSQL, Server не содержит браузерной
логики. [Результаты Spike](docs/03-active/SPIKE-001_RESULT.md) сохраняют ограничения
источников; Stage 1 не является общим обещанием доступности Avito/Cian.
