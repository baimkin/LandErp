# LandErp

Локальный запуск Stage 1 (Windows, PostgreSQL 18, без Docker):

1. Используйте SDK из `global.json`; выполните `dotnet restore --locked-mode`
   и `dotnet build LandErp.slnx -c Release --no-restore`.
2. Настройте `LANDERP_TEST_ADMIN_CONNECTION` локально вне Git. Для первоначальной
   настройки выполните `scripts/Initialize-Local.ps1`. Этот отдельный инструмент
   создаёт `landerp_local`, migration/runtime роли и применяет migrations.
   Если неизвестная БД с таким именем уже существует, инструмент останавливается.
3. Первоначальные данные Owner находятся только в `local-data/stage1/owner-access.txt`.
   Запустите `scripts/Start-Local.ps1`, откройте `https://localhost:7240` и настройте
   MFA при первом входе. При необходимости доверьте локальный development HTTPS
   certificate штатной командой `dotnet dev-certs https --trust`.
4. Worker запускается отдельно: `scripts/Start-Local.ps1 -Service Worker`.
5. Проверки: `scripts/Test-Foundation.ps1`. Они используют настоящую PostgreSQL
   и автоматически создают/удаляют только собственные disposable test databases.

У scripts есть `-DotnetPath` для явного пути к SDK. Startup Server/Worker не
изменяет schema; последующие migrations применяются через отдельный setup/tooling.

Server mode Collector: Owner/Admin создаёт Collector на `/collectors` и сохраняет
показанные один раз ID/token в локальные переменные `LANDERP_COLLECTOR_AGENT_ID`,
`LANDERP_COLLECTOR_TOKEN`, `LANDERP_COLLECTOR_SERVER_URL=https://localhost:7240/`.
Пароль/token не вводите в команды, сохраняемые в истории терминала. Откройте
существующий WPF Collector, вкладку «LandErp Server», подключите Server и получите
разрешённую работу. В ERP заранее создайте поиск для этого Collector и поставьте
сбор. CAPTCHA/авторизация выполняются вручную. Heartbeat/доставка работают при
открытом Collector; Local mode доступен независимо от Server. При обрыве связи
результаты остаются в локальной очереди, кнопка «Повторить доставку» отправляет
те же ResultId. После истечения lease «Получить…» обновляет fencing token и
доставляет сохранённый результат. Отзыв/перевыпуск token выполняются в ERP.

LandErp — ERP для поиска, оценки и ведения инвестиционных проектов с земельными участками.

Исследовательская фаза SPIKE-001 закрыта документационно по ERP-00.
Collector остаётся самостоятельным локальным Windows-приложением.
Stage 1 — Procurement Core разрешён владельцем 2026-09-14 и реализуется
в `codex/stage-1-procurement-core`. Текущий checkpoint — A (Foundation + Database).

## Локальная проверка фундамента

Нужны stable SDK 10.0.112 и локальная PostgreSQL 18 (проверена 18.6), без Docker.
Runtime secret: `Database__ConnectionString`; migrations: `LANDERP_MIGRATOR_CONNECTION`.
Для tests настройте `LANDERP_TEST_ADMIN_CONNECTION` в process/user environment:
локальный admin с правом создания disposable БД и ролей. Значения вне Git/логов.

```powershell
dotnet tool restore
./scripts/Test-Foundation.ps1
./scripts/Invoke-Migrations.ps1 -Action Script
./scripts/Invoke-Migrations.ps1 -Action Apply
dotnet run --project src/LandErp.Server -c Release
dotnet run --project src/LandErp.Worker -c Release
```

Migration path принимает только `landerp_local` или `landerp_test_*`; runtime не
создаёт schema. `/health/live` проверяет процесс, `/health/ready` — PostgreSQL/schema.
Tests удаляют только собственные автоматически сгенерированные БД/роли текущего
прогона; backup остаётся в ignored artifacts. Для изолированного SDK текущей машины
можно передать `-DotnetPath ./artifacts/stage1/dotnet/dotnet.exe` в scripts.

Остальные сведения ниже — исходная точка ERP-00, не ограничение Stage 1.

## Начать отсюда

README → [START_HERE](docs/START_HERE.md) → [ACTIVE_TASK](docs/03-active/ACTIVE_TASK.md)
→ только Required reading → затрагиваемый код.

Архитектурные планы и последовательность этапов не разрешают писать весь production-контур.

## Фактический статус

- Gate 01 принят владельцем по коммиту `8607e8c60f23ea40177ab8742fadc51f867663e3`.
- Collector Avito/Cian, локальная очередь, SQLite и карта Avito реализованы в
  `spike/001-avito-viability`, checkpoint `3994644d5a00413bb53513b78c5492c7e0b70692`.
- Последний полный контроль: 85 успешных офлайн-тестов; есть сверка 43 карточек
  карты Avito владельцем. Это не общий Go или production-приёмка Collector.
- [Итог Spike](docs/03-active/SPIKE-001_RESULT.md) фиксирует ограничения и evidence.
- [ERP-00](docs/04-foundation/ERP-00_Переход_от_Spike_к_production_ERP.md) выполнен;
  [отчёт](docs/03-active/reports/ERP-00_REPORT.md).
- [ERP-01 — техническое основание и БД](docs/03-active/GATE-ERP-01_Техническое_основание_и_БД.md)
  подготовлен. Код и production migrations в ERP-00 не создаются.

## Ветки и Collector

Текущая документационная ветка `docs/erp-core-foundation` создана от ранней
плановой базы и пока не содержит исходники Collector. Работающий Collector
сохранён в своей ветке; merge/rebase и перенос кода в этой задаче не выполнялись.
Отсутствие его исходников в документационной ветке не означает, что исследование
не выполнено. Перед ERP-01 необходимо явно согласовать базу implementation-ветки
и сохранение уже проверенных SDK/CPM/lock/test настроек, не пересоздавая Collector.

Collector не зависит от ERP для локального запуска. Server не содержит
Playwright/source-specific browser logic; Collector не подключается к PostgreSQL.
Будущий server mode реализуется отдельным адаптером по
[ADR-007](docs/02-decisions/ADR-007_Collector_как_самостоятельный_продукт_и_граница_с_Server.md).

Исторический `CODEX_FIRST_TASK.md` относится к Gate 01 и не является текущим заданием.
