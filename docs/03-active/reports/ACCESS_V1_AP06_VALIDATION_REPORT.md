# AP-06 — Access V1 validation report

**Статус:** NOT READY
**Repository:** `baimkin/LandErp`
**Рабочая ветка:** `codex/access-v1-ap-06-validation`
**Base AP-05:** `752692d54747a4dd641790914963f1ea3f949961`
**Итоговый commit:** commit этого отчёта (см. HEAD ветки)
**`main` в начале и в конце:** `54fa184e98f41dba3f040cde41aec181153ea054` (локальный и `origin/main` на момент проверки).

## Фактические проверки

| Проверка | Команда | Результат |
|---|---|---|
| Locked restore, изолированная среда | `dotnet restore LandErp.slnx --locked-mode` | Failed: NU1900, среда не могла обратиться к `api.nuget.org` за audit metadata. |
| Locked restore, сетевой доступ | `dotnet restore LandErp.slnx --locked-mode` | Passed, exit 0. Lock-файлы и зависимости не менялись. |
| Release build | `dotnet build LandErp.slnx -c Release --no-restore` | Failed, exit 1: 5 × CS9113 в `LandErp.Infrastructure`, 0 warnings. |

Компиляция останавливается на неиспользуемом параметре конструктора `legacyAccess` в пяти компонентах:

- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs:25`;
- `src/LandErp.Infrastructure/Modules/Collection/CollectionAdministration.cs:18`;
- `src/LandErp.Infrastructure/Modules/Overview/OverviewService.cs:24`;
- `src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs:18`;
- `src/LandErp.Infrastructure/Modules/Procurement/ProcurementQueueV2ReadService.cs:16`.

Вероятная причина: при cutover удалено чтение legacy access, но параметры остались в primary constructors. В проекте warnings as errors, поэтому это ошибки сборки. В рамках validation-only AP-06 production-код не исправлялся. Это **merge blocker** и **production blocker**.

## Тесты и миграции

`dotnet test tests/LandErp.Foundation.Tests -c Release --no-build` не запускался: после провалившейся сборки нет валидного набора бинарников AP-05. **Выполнено 0, passed 0, failed 0, skipped 0**; это не означает, что тесты проходят.

По той же причине не запускались отдельные AP-05 regression scenarios и PostgreSQL migration validation. Clean DB: **Not run**. Upgrade DB до/после `20260921225000_AccessV1Cutover`: **Not run**. Pending EF model changes: **Not checked**. Проверка authorization regressions: **Not run**. PostgreSQL 18 и `LANDERP_TEST_ADMIN_CONNECTION` были доступны, но запуск старых или частично собранных binaries дал бы недостоверный результат. Production-БД и локальная рабочая БД не менялись.

## Вердикты

**Merge readiness:** нет. Цепочку AP-01–AP-05 нельзя переносить в `main` до исправления CS9113 и полного повторного AP-06.

**Production readiness:** нет. Нельзя выкладывать текущий AP-05 в локальный или внешний production: Release build не проходит, автоматическая server/DB validation и ручная проверка интерфейса не выполнены.

**Следующий исправляющий этап:** удалить пять устаревших constructor dependencies и обновить регистрации/создание этих компонентов при необходимости. После отдельного fix commit заново выполнить locked restore, Release build, весь Foundation/PostgreSQL suite, AP-05 regressions и оба migration scenarios без изменения опубликованных миграций.

## Ручная браузерная проверка после успешной повторной валидации

1. Войти как Owner и как сотрудник с ограниченными ReadScope/WorkScope; проверить видимость своей и назначенной карточки.
2. Передать объект между отделами, затем проверить действия назначенного менеджера и отсутствие доступа к постороннему объекту.
3. Forward руководителю другого отдела, Return менеджеру и решение руководителя; проверить запрет self-approval.
4. Проверить, что назначение осмотра не открывает Procurement и что права Procurement не позволяют выполнять чужой осмотр.
5. Проверить экран настроек Access V1, запрет записи при Read и запрет подтверждения покупки при `CanConfirmPurchase=false`.

Browser/Playwright/UI automation и ручной browser smoke в AP-06 не запускались согласно задаче.

## Integration notes и CI

Production diff относительно AP-05: нулевой. AP-06 содержит только этот отчёт. `main` не менялся. Локальная `codex/parser-runtime-url-stability` содержит отдельный commit `41d9557` с pacing карты Avito и ускорением следующей server work; он расходится с AP-05 от `3062c74` и не включался в validation-only AP-06. Его интеграцию следует рассматривать после исправления Access V1 отдельной проверкой.

CI итогового commit: на момент записи и локального commit не запущен; статус итогового SHA проверяется после публикации. Отсутствие запуска не считается успехом CI.
