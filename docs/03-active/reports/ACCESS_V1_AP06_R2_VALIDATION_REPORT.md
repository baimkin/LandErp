# AP-06 R2 — Access V1 validation

**Статус:** NOT READY

| Реквизит | Значение |
|---|---|
| Repository | `baimkin/LandErp` |
| Ветка | `codex/access-v1-ap-06-r2-validation` |
| Base commit | `888b5fa73927455f714c4171e921d6709e4bc45c` |
| Итоговый commit | единственный commit отчёта в HEAD ветки |
| `main` в начале | `54fa184e98f41dba3f040cde41aec181153ea054` |
| `main` в конце | `54fa184e98f41dba3f040cde41aec181153ea054` |

Ветка создана непосредственно от указанного base commit и до отчёта не содержала дополнительных commits.

## Выполненные команды

| Команда | Exit code | Результат |
|---|---:|---|
| `dotnet restore LandErp.slnx --locked-mode` | 0 | Зависимости восстановлены в locked mode. |
| `dotnet build LandErp.slnx -c Release --no-restore` | 1 | 0 warnings, 2 errors `RZ9999` в `LandErp.Server`. |

Точные ошибки: `src/LandErp.Server/Components/Pages/OrganizationPage.razor:125` (`Authorized`) и `:181` (`NotAuthorized`). Внутри `EditForm` оба child-content блока `AuthorizeView` неявно используют то же имя параметра `context`, что и внешний `EditForm`. Razor требует явно задать другое имя через `Context`. Вероятная причина — добавление Access V1 UI блока внутрь формы без переименования контекста. Это **merge blocker** и **production blocker**. По правилу validation-only production-код не исправлялся.

## Тесты, регрессии и миграции

После критической ошибки сборки дальнейшие проверки остановлены: binaries Server итогового base commit отсутствуют, а запуск частично собранного набора не подтвердил бы AP-06 R2.

| Проверка | Фактический результат |
|---|---|
| `dotnet test tests/LandErp.Foundation.Tests -c Release --no-build` | Not run; exit code отсутствует. Всего выполнено 0, passed 0, failed 0, skipped 0. |
| `AccessV1Ap02WorkflowTests` | Not run. |
| `AccessV1Ap03UiTests` | Not run. |
| `AccessV1Ap04CutoverTests` | Not run. |
| `AccessV1Ap05HardeningTests` | Not run. |
| Clean disposable PostgreSQL DB → все миграции | Not run. |
| Upgrade через `20260921225000_AccessV1Cutover`, backfill каждого non-Owner, сохранение explicit settings, Owner без обязательной строки | Not run. |
| `HasPendingModelChanges() == false` | Not checked. |

PostgreSQL test harness был доступен (`LANDERP_TEST_ADMIN_CONNECTION` установлен). Production и локальная рабочая БД не изменялись. Browser/Playwright/UI automation не запускались.

## Вердикты

**Merge readiness: NOT READY.** Release build Server не проходит; нет фактических результатов Foundation/PostgreSQL и Access V1 regression tests.

**Production readiness: NOT READY.** До исправления двух ошибок Razor, полного повторного AP-06 и ручного browser smoke владельца текущий commit нельзя развертывать как проверенный production release.

Следующий исправляющий этап: устранить конфликт имён child-content context в двух блоках `AuthorizeView` на `OrganizationPage.razor`, затем заново выполнить весь AP-06 R2. Существующие миграции и тесты ради обхода ошибки не менять.

## Ручной browser smoke после успешной автоматической проверки

1. Owner и сотрудник с ограниченным Access V1: экран настроек и сохранение доступа.
2. Передача объекта между отделами: назначенный менеджер видит и обрабатывает свой объект, посторонний объект остаётся закрыт.
3. Forward руководителю другого отдела, допустимое решение и Return менеджеру; запрет self-approval.
4. Осмотрщик видит только назначенный осмотр; Procurement и подтверждение покупки ограничены отдельными правами.

## CI и состав публикации

На момент создания отчёта CI итогового SHA ещё не мог быть запущен; после публикации требуется read-only проверка checks именно итогового commit. Отсутствие checks не считать успехом. Относительно base commit ожидается один файл — этот отчёт, production diff нулевой. `main` не менялся.
