# Stage 1 — Procurement Core: итоговый отчёт

Дата: 2026-09-14. Ветка: `codex/stage-1-procurement-core`.

## Что работает

Collector Avito/Cian остаётся самостоятельным Windows приложением с Local mode,
SQLite, browser/source adapters и диагностикой. Тонкий Server adapter доставляет
versioned HTTPS результаты через durable local outbox. Server хранит каталог и
наблюдения в PostgreSQL; browser logic и доступ к SQLite Collector отсутствуют.

В Blazor Web App доступны вход/MFA, оргструктура, сотрудники/назначения/scopes,
административный экран Collector, очередь закупки и рабочая карточка. Менеджер
проводит первичный анализ, сохраняет заметки/ручной контакт и передаёт объект
руководителю. Возврат сохраняет причину, что уточнить, исполнителя и срок;
одобрение/наблюдение/отклонение сохраняют history, task, assignment, audit и
внутренние notifications. Одобрение означает дальнейшую работу, а не покупку.

## История checkpoints

| Checkpoint | Commit | Результат |
|---|---|---|
| База | `3e30755` | safe merge ERP docs и Collector checkpoint `3994644d5a00413bb53513b78c5492c7e0b70692` внутри implementation-ветки |
| A | `03d940ea6ecd22d2562e3742d8e1251faad7de` | Foundation, PostgreSQL, migration tooling, независимые Server/Worker, architecture/integration tests |
| B | `699db191482ddd23716eabf03d265797c145b045` | Identity/Organization, server permissions/scopes, MFA, UIKit Razor shell и реальные admin screens |
| C | `719a1c6b5a6049f1c18593993d20494536eb24b4` | revocable machine identity, lease/result/idempotency/retry, Listing Catalog и Collector adapter |
| D | `50b9aba64ded93f805b7af0e40370e650dad7c52` | Procurement workflow, очередь/карточка/история/notifications |

Финальный commit содержит этот отчёт, завершённый ACTIVE_TASK и дополнительную
проверку открытия одобренного кейса после настоящего Server restart. Main не
изменялся; force-push/rebase/удаление веток не выполнялись. A/B не пересоздавались.

## Технологии и база

C# 14; SDK 10.0.112 / runtime ASP.NET Core 10.0.12; EF/Identity 10.0.12;
Npgsql/EF provider 10.0.3; PostgreSQL 18.6; существующий Playwright 1.62.0.
Stable версии проверены перед фиксацией. CPM и locked restores сохранены.
Portable SDK текущей машины находится в ignored `artifacts/stage1/dotnet`.

Реальная `landerp_local`: 6 migrations; 32 business/technical tables, comments
32/32. Русские TABLE/COLUMN comments сравниваются integration tests с model
metadata. Ключи UUIDv7, ExternalId отдельно от BusinessNumber; UTC/timestamptz,
явный Europe/Moscow, Money decimal(19,4)/RUB с округлением цены до 2 знаков ToEven.
Version защищает изменяемые агрегаты. Current state реляционный, важные факты
append-only; full Event Sourcing не используется.

Migration/runtime роли разделены; startup не применяет migrations. Runtime DDL
отклонён с 42501 и не получает DELETE/UPDATE важных facts. Tests создают только
собственные `landerp_test_<uuid>` и generated roles, cleanup проверяет точные
принадлежащие текущему sandbox имена. Docker/SQLite/InMemory для ERP DB tests
не использовались. Backup/restore выполнен настоящими pg_dump/pg_restore.

Machine token генерируется Server, показывается один раз; хранится только hash и
metadata. Revoke/rotate немедленно закрывают старый token. Agent получает только
свою разрешённую work/lease и может heartbeat/results; ERP/admin API закрыты.
Новые credentials, cookies, local DB, outbox и local-data не входят в Git/отчёт.

## Команды и результаты

Ниже `dotnet` означает `./artifacts/stage1/dotnet/dotnet.exe`.
Secret environment/settings читались приватно; значения не выводились.

| Фактическая команда / проверка | Результат |
|---|---|
| `dotnet restore LandErp.slnx --locked-mode` | success |
| `dotnet build LandErp.slnx -c Release --no-restore` | 0 warnings / 0 errors |
| `dotnet test tests/LandErp.Foundation.Tests -c Release --no-build --no-restore` | 10/10, 2m42s |
| `dotnet test tests/LandErp.ParserSpike.Tests -c Release --no-build --no-restore --filter 'TestCategory!=Live'` | 85/85, 27s |
| targeted PostgresTests/CollectorIntegrationTests после readiness fix | 3/3, 43s |
| `dotnet list LandErp.slnx package --vulnerable --include-transitive` | vulnerabilities не обнаружены |
| `dotnet format whitespace LandErp.slnx --no-restore --verify-no-changes --include <changed C# files>` | success |
| `scripts/Initialize-Local.ps1 -DotnetPath ./artifacts/stage1/dotnet/dotnet.exe` | явное Local migration apply success |
| `dotnet run --project src/LandErp.LocalSetup -c Release --no-build -- --inspect` | PG18.6; migrations6; tables/comments32/32; runtime DDL denied42501 |
| localhost `/health/live`, `/health/ready` | HTTP200 / HTTP200 |
| `git diff --check`, staged paths, tracked local-data/DB/cookies/credentials inspection | clean; secrets/local data отсутствуют |

TRX/screenshots/backup находятся в ignored `artifacts/stage1`.
Контрольный C test использует настоящий Collector LocalStore/QueueRunner с
контролируемым source adapter → Collector HTTPS adapter → Server → PostgreSQL.
D проверяет менеджера/руководителя и реальные cookie/UI действия в Chrome с той
же PostgreSQL системой, включая настоящий restart Server с сохранением кейса.
Live marketplace не является зависимостью этих tests.

Исправлены найденные проблемы: имя policy новых экранов; comparison varchar[] /
text[] в readiness; ожидание вложенного native dialog; нестабильные navigation
load-state ожидания UI tests заменены ожиданием конкретного DOM и URL assertion.
Windows DLL locks при промежуточной сборке с работающими hosts устранены их
остановкой и отдельной финальной сборкой без warnings.

## Definition of Done: evidence

| Требование | Фактическое доказательство |
|---|---|
| 1–3: PostgreSQL, migrations, русские comments | PG18.6 Local inspection; real PostgreSQL clean/repeat/zero/reapply и model/metadata tests |
| 4–5: Server и UIKit Blazor UI | HTTPS startup/ready200; actual Chrome desktop1440/tablet900/mobile390 screenshots просмотрены |
| 6–8: Owner/Admin, сотрудники/оргструктура, ограничения | actual login/MFA/invitation/admin forms сохраняют в PG; permission/scoped negative tests |
| 9–10: Collector Local/Server modes | 85 offline regressions; existing WPF Local tabs сохранены; actual HTTPS adapter/outbox scenario |
| 11: контрольное объявление через contract в PG | real Collector controlled source → HTTPS result delivery, price/area/provenance проверены в PG |
| 12–15: очередь, менеджер, передача, решения руководителя | actual browser TakeWork → Forward → Return → Forward → Approve; service Monitor/Clarify/Reject и negative cases |
| 16: history/исполнитель/срок | timeline/audit/approval/notification assertions, Return body/target/due и ручной контакт EffectiveAt |
| 17: Server restart сохраняет данные | C durable retry/restart; D process stop/start, approved card и прежний timeline открываются после restart |
| 18–19: tests и defects | итоговые suites зелёные; известных blocker/critical defects в реализованном scope нет |

Дополнительно: организация/Department/Team/Own/AssignedObjects visibility;
idempotency/conflicting ResultId, duplicate observations, lease expiry/fencing,
revocation; missing field не очищает известное значение, старое наблюдение не
переписывает новое; source changes требуют актуального анализа; optimistic
concurrency; machine/admin negatives; CSRF; actual empty/forbidden states и компоненты loading/error;
независимость Worker/Server; PostgreSQL outage ready503/live200; backup/restore
catalog/audit/timeline/transitions/approvals/case facts.

## Запуск и известные ограничения

Полная инструкция — [README](README.md). Готовая Local БД сохранена. Для запуска:

```powershell
./scripts/Start-Local.ps1 -DotnetPath ./artifacts/stage1/dotnet/dotnet.exe
```

Адрес `https://localhost:7240`. Owner credentials откройте приватно в ignored
`local-data/stage1/owner-access.txt`; локальному Owner ещё нужно пройти MFA setup.
Создайте отдел/должности/менеджера и руководителя, выберите Department/Team scope,
зарегистрируйте Collector и настройте разрешённый поиск. Подробные шаги в README.

- Live Avito/Cian проверка в Stage 1 не выполнялась; использован контрольный source
  и offline fixtures существующих adapters. Marketplace доступность, CAPTCHA,
  rate limits и авторизация остаются внешними/ручными ограничениями.
- Сбор запускается пользователем; heartbeat/retry работают при открытом Collector.
  Нет установщика/tray/автообновления или нового distributed platform.
- Catalog contract содержит основные публичные поля закупки; полные локальные
  diagnostics остаются Collector. Фото отображаются по доступным source URLs.
- Money contract Stage 1 поддерживает RUB. Роли/permissions системные; назначения
  и scopes изменяемые. Invitation и внутренние notifications без email/Telegram.
- Queue содержит 40 объектов на странице; card показывает до 100 source observations
  и 200 последних timeline entries. Все предыдущие facts остаются в PostgreSQL.
- Первый применённый foundation migration сохранил исторический короткий timestamp
  identifier; его не переписывали. Полный apply/zero/reapply проверен, partial apply
  проверяется по следующей стандартной IdentityOrganization migration.
- Development HTTPS certificate требует локального trust для обычного браузера;
  bypass certificate validation используется только изолированными tests.
- Это Local Stage 1, не production deployment. Production apply не выполнялся.
  Нет полного Due Diligence, покупки/финансового ядра, кабинета инвестора, BPMN,
  PostGIS, AI и функций Stage 2. Stage 2 не начат.
