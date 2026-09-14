# Stage 1 — checkpoint B

Identity/Organization и Blazor UI реализованы в implementation-ветке.

Результат: Owner/Admin с обязательным authenticator MFA; приглашения сотрудников
с одноразовой активацией; departments/positions/teams и назначения с role/scope;
permissions и изоляция организации проверяются сервером по актуальным DB данным.
Смена роли отзывает security stamp; последний активный Owner защищён.
Аудит не содержит пароли, invitation tokens или MFA keys.

UI: reusable Razor AppShell/PageHeader/PageState/StatusBadge/ActionButton,
локальный Bootstrap CSS foundation и точные tokens UI Kit. Bootstrap JS/CDN и
новые component libraries не используются. Admin экраны реально сохраняют данные.
Отдельный LocalSetup применяет migrations и выдаёт первоначальный Owner вне Git;
Server/Worker запускаются независимо, runtime не получает schema ownership/DDL.

Проверки перед фиксацией: все 8 ERP tests прошли (7 в полном запуске,
последняя браузерная проверка после исправления accessible labels — 1/1).
Локальный повторный setup успешен, Server HTTPS readiness — 200.

- SDK 10.0.112: locked restore; Release build без warnings.
- Real PostgreSQL: clean/repeat apply, model drift, rollback/reapply, backup/restore,
  Russian TABLE/COLUMN metadata против design-time EF model, runtime DDL/audit delete denied.
- Identity: MFA negative, token invalid/reuse, permission/scope/organization negatives,
  assignment concurrency и last Owner; бизнес-операции под runtime-role.
- Browser Chrome: реальные cookie/MFA/CSRF, department/position/invitation persistence,
  activation/login employee, forbidden admin/API states; widths 1440/900/390.
- Scoped `dotnet format` для ERP projects/tests; `git diff --check`; NuGet vulnerability
  audit — ни одного уязвимого пакета. Collector source не переписан.

Снимки проверки находятся в ignored `artifacts/stage1/ui-b/` и не содержат secrets.
Local credentials только в ignored `local-data/stage1/`, с Windows ACL для владельца.

Ограничения среза: роли/permissions Stage 1 системные, назначение настраивается;
email delivery и публичная регистрация не включены. MFA key вводится вручную в
authenticator; recovery codes показываются один раз. Procurement экраны и
Collector server mode относятся к следующим checkpoints C/D.
