using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text;

namespace LandErp.Infrastructure.Persistence;

internal static class ModelConventions
{
    private static readonly Dictionary<string, string> TableComments = new(StringComparer.Ordinal)
    {
        ["users"] = "Учётные записи сотрудников. Пароли хранятся только как Identity verifier; доступ к бизнес-данным задаётся назначениями и permissions.",
        ["roles"] = "Именованные наборы разрешений. Роль не заменяет проверку области данных.",
        ["user_roles"] = "Технические роли Identity для требований MFA и управления сессией. Бизнес-доступ проверяется по актуальному назначению сотрудника.",
        ["user_claims"] = "Технические утверждения Identity. OrganizationId из клиентского payload не считается доказательством доступа.",
        ["user_logins"] = "Технические связи с провайдерами входа Identity. В Stage 1 внешние провайдеры не включены.",
        ["user_tokens"] = "Технические секреты и verifier Identity, включая ключ MFA. Не выводятся в UI журналов, логи и аудит.",
        ["role_claims"] = "Технические утверждения ролей Identity; бизнес permissions хранятся отдельно.",
        ["permissions"] = "Каталог стабильных системных разрешений на действия. Проверяется сервером вместе с scope.",
        ["role_permissions"] = "Состав разрешений каждой роли. Не даёт доступ к другим организациям.",
        ["employee_invitations"] = "Одноразовые приглашения для активации сотрудников; открытый token не сохраняется, повторное использование запрещено.",
        ["organizations"] = "Организации, являющиеся границами доступа LandErp. Не являются юридическими лицами сделки.",
        ["org_units"] = "Подразделения организации, определяющие рабочую ответственность и Department visibility.",
        ["positions"] = "Настраиваемые должности сотрудников; название должности само по себе не предоставляет permissions.",
        ["teams"] = "Рабочие команды внутри подразделения; используются для Team visibility.",
        ["employees"] = "Профили сотрудников организации, связанные с учётной записью. Неактивный сотрудник не получает бизнес-доступ.",
        ["employee_assignments"] = "Актуальное назначение сотрудника: подразделение, должность, команда, руководитель и роль с областью доступа.",
        ["audit_events"] = "Неизменяемый журнал значимых действий: кто, когда, что и в какой области изменил. Runtime не исправляет и не удаляет события."
    };

    private static readonly Dictionary<string, string> ColumnComments = new(StringComparer.Ordinal)
    {
        ["OrganizationId"] = "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером.",
        ["UserId"] = "Связь с технической учётной записью; не является внешним ID или бизнес-номером.",
        ["EmployeeId"] = "Сотрудник, к которому относится назначение или приглашение.",
        ["OrgUnitId"] = "Подразделение рабочей ответственности; используется для Department scope.",
        ["PositionId"] = "Должность в организации; права определяются отдельно через роль и permission.",
        ["TeamId"] = "Рабочая команда для Team scope. Отсутствие команды не расширяет область доступа.",
        ["ManagerEmployeeId"] = "Руководитель сотрудника, которому можно передать рабочую ответственность.",
        ["RoleId"] = "Набор permissions для текущего назначения; итоговый доступ ограничен scope.",
        ["PermissionId"] = "Стабильный код серверной проверки операции, а не название кнопки интерфейса.",
        ["Scope"] = "Own — собственные записи; AssignedObjects — назначенные объекты; Team — команда; Department — подразделение; Organization — одна организация.",
        ["Version"] = "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.",
        ["Active"] = "Разрешена ли работа сотрудника. При выключении существующая cookie не обходит серверную проверку.",
        ["TokenHash"] = "SHA-256 одноразового высокоэнтропийного token; открытое значение не хранится.",
        ["ExpiresAt"] = "UTC момент окончания действия приглашения; после него активация запрещена.",
        ["AcceptedAt"] = "UTC момент однократной активации. Непустое значение запрещает повторное использование.",
        ["RecordedAt"] = "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.",
        ["ActorId"] = "Автор действия — доверенная серверная идентичность; не берётся из клиентского payload.",
        ["Changes"] = "Безопасные значения значимых изменений до/после в JSON; passwords, cookies, tokens и ключи исключены.",
        ["CorrelationId"] = "Ограниченный безопасный идентификатор связи действия с HTTP-запросом или локальной командой.",
        ["ObjectId"] = "Внутренний UUID бизнес-объекта, к которому относится событие.",
        ["ObjectType"] = "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия.",
        ["Action"] = "Стабильный код выполненного значимого действия, история сохраняется append-only.",
        ["BusinessTimeZone"] = "Явный IANA timezone бизнес-сроков организации. Instants в БД сохраняются UTC.",
        ["PasswordHash"] = "Identity password verifier; открытый пароль не хранится и не логируется.",
        ["SecurityStamp"] = "Версия безопасности Identity для отзыва сессий при изменении учётной записи.",
        ["ConcurrencyStamp"] = "Технический Identity concurrency token для защиты от потерянных обновлений.",
        ["TwoFactorEnabled"] = "Включена MFA. Для Owner/Administrator этого поля недостаточно: текущий вход также должен пройти MFA.",
        ["LockoutEnd"] = "UTC момент завершения блокировки входа после неуспешных попыток.",
        ["AccessFailedCount"] = "Число неуспешных попыток, используемое Identity lockout policy.",
        ["EmailConfirmed"] = "Адрес активирован закрытым приглашением или локальным Owner bootstrap; публичной регистрации нет.",
        ["Value"] = "Техническое значение Identity token или claim. Секретные token values не публикуются и не входят в аудит."
    };

    public static void Apply(ModelBuilder builder)
    {
        foreach (IMutableEntityType entity in builder.Model.GetEntityTypes())
        {
            string table = entity.GetTableName()!;
            entity.SetComment(TableComments[table]);
            foreach (IMutableProperty property in entity.GetProperties())
            {
                property.SetColumnName(Snake(property.Name));
                if (ColumnComments.TryGetValue(property.Name, out string? comment))
                {
                    property.SetComment(comment);
                }

                if (property.Name == "Version")
                {
                    property.IsConcurrencyToken = true;
                }

                if (property.ClrType == typeof(string) && property.GetMaxLength() == null
                    && property.GetColumnType() != "jsonb")
                {
                    property.SetMaxLength(property.Name is "Changes" ? 8000 : 512);
                }
            }

            foreach (IMutableKey key in entity.GetKeys())
            {
                key.SetName((key.IsPrimaryKey() ? "pk_" : "ak_") + table
                    + (key.IsPrimaryKey() ? "" : "_" + string.Join('_', key.Properties.Select(item => Snake(item.Name)))));
            }

            foreach (IMutableIndex index in entity.GetIndexes())
            {
                index.SetDatabaseName("ix_" + table + "_" + string.Join('_', index.Properties.Select(item => Snake(item.Name))));
            }

            foreach (IMutableForeignKey key in entity.GetForeignKeys())
            {
                key.SetConstraintName("fk_" + table + "_" + string.Join('_', key.Properties.Select(item => Snake(item.Name))));
            }
        }
    }

    private static string Snake(string name)
    {
        StringBuilder result = new();
        for (int index = 0; index < name.Length; index++)
        {
            if (index > 0 && char.IsUpper(name[index]))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(name[index]));
        }

        return result.ToString();
    }
}
