using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using System.Text;

namespace LandErp.Infrastructure.Persistence;

internal static class ModelConventions
{
    private static readonly Dictionary<string, string> TableComments = new(StringComparer.Ordinal)
    {
        ["property_cases"] = "Самостоятельные рабочие объекты закупки. Внешние предложения связаны отдельно и не владеют жизненным циклом кейса.",
        ["property_case_source_links"] = "Подтверждённые и возможные связи входящих элементов Catalog с PropertyCase. Один входящий элемент может иметь не более одной подтверждённой связи.",
        ["stages"] = "Стабильные стадии первого процесса закупки: new/analysis/clarify/monitor/rejected/pending_head/returned/approved. Approved означает дальнейшую работу, не покупку.",
        ["assignments"] = "Актуальная ответственность за бизнес-объект. Передача и возврат меняют исполнителя атомарно с workflow и историей.",
        ["work_tasks"] = "Актуальная рабочая задача сотрудника по объекту, с опциональным сроком UTC; Completed не удаляет историю выполненной работы.",
        ["transitions"] = "Неизменяемые факты переходов workflow: кто выполнил действие, прежняя/новая стадия и версия объекта.",
        ["approvals"] = "Неизменяемые решения руководителя по конкретной передаче и версии данных. Одобрение не является юридическим или финансовым фактом сделки.",
        ["business_timeline"] = "Неизменяемая бизнес-история объекта: решения, заметки, контакты, кому возвращён объект, что уточнить и срок. Отдельна от технического audit.",
        ["notifications"] = "Внутренние уведомления исполнителю о передаче/возврате/решении. ReadAt отмечает просмотр; внешняя доставка не включена.",
        ["agents"] = "Зарегистрированные локальные Collector. Сервер хранит только verifier credential; отзыв немедленно запрещает новые обращения.",
        ["search_configurations"] = "Разрешённые поиски организации с конкретным Collector и областью закупки. Browser settings и cookies остаются локально.",
        ["jobs"] = "Одна работа сбора: Pending ожидает; Leased закреплена до срока; Completed/LimitReached завершена; AwaitingManualAction требует ручного действия; Failed/Interrupted не считаются пустым успехом.",
        ["deliveries"] = "Неизменяемые квитанции доставки Collector. Повтор ResultId с тем же payload возвращает прежний ответ; другой payload запрещён.",
        ["listings"] = "Универсальные входящие предложения Catalog из автоматических и ручных источников; не идентичность земельного участка.",
        ["observations"] = "Неизменяемые наблюдения публичных объявлений: Source/ExternalId, Raw/Parsed/Presence, provenance и версия адаптера. Browser state и raw HTML здесь не хранятся.",
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
        ["BusinessNumber"] = "Читаемый номер кейса PC-000001 из отдельной последовательности; не UUID и не ExternalId объявления.",
        ["StageId"] = "Текущая стадия первичной закупки. Approved разрешает следующий анализ, не обозначает завершённую покупку.",
        ["AssignmentId"] = "Общая актуальная ответственность за кейс; изменение исполнителя сохраняется вместе с переходом.",
        ["WorkTaskId"] = "Общая текущая задача по кейсу и её срок; будущий SLA/job engine не включён.",
        ["PendingApprovalId"] = "ID конкретной передачи руководителю. Один immutable Approval завершает эту передачу.",
        ["ReviewedDataRevision"] = "Версия публичных данных, рассмотренная при последнем решении. Новые данные возвращают объект в изменившуюся очередь.",
        ["WorkingTitle"] = "Рабочее название PropertyCase, сохраняемое независимо от последующих изменений внешних источников.",
        ["WorkingPrice"] = "Рабочая цена PropertyCase; не перезаписывается автоматически при изменении публичной цены источника.",
        ["WorkingAreaSquareMeters"] = "Рабочая площадь PropertyCase в м²; исходные значения каждого источника сохраняются отдельно.",
        ["WorkingLocation"] = "Рабочее местоположение PropertyCase, независимое от текущей доступности внешнего объявления.",
        ["FactsProvenance"] = "Происхождение первоначальных рабочих фактов кейса; migration/system не означает подтверждение человеком.",
        ["CatalogItemId"] = "Входящий элемент Catalog, связанный с PropertyCase; подтверждённая связь уникальна для элемента.",
        ["PropertyCaseId"] = "Самостоятельный рабочий объект закупки, к которому относится источник.",
        ["Confirmed"] = "Подтверждённая связь владеет принадлежностью источника одному PropertyCase; возможные совпадения не подтверждены.",
        ["RelationType"] = "Тип связи источника с кейсом; не изменяет жизненный цикл самого Catalog item.",
        ["Provenance"] = "Человекочитаемое происхождение входящего элемента или подтверждения связи без технических секретов.",
        ["IngestionKind"] = "Способ поступления: Collector, сотрудник, миграция или интеграция; ручной путь не создаёт фиктивные jobs/observations.",
        ["CreatedByEmployeeId"] = "Сотрудник, вручную добавивший входящее предложение; отсутствует у автоматического Collector ingress.",
        ["IngressComment"] = "Комментарий о происхождении или контексте ручного входящего предложения.",
        ["Disposition"] = "Текущее решение первичного отбора: входящее, мониторинг, в работе, отклонено, снято или продано.",
        ["ReceivedAt"] = "UTC момент поступления элемента в Catalog; для автоматического источника отличается от времени наблюдения при задержке доставки.",
        ["ExternalId"] = "Опциональный внешний ID в конкретном источнике; отсутствие не заменяется пустой строкой или synthetic ID.",
        ["CadastralNumber"] = "Кадастровый номер, если известен; не является обязательной или единственной идентичностью объекта.",
        ["FromStageId"] = "Стадия объекта непосредственно перед сохранённым переходом.",
        ["ToStageId"] = "Стадия объекта после сохранённого перехода; не планируемая будущая стадия.",
        ["ObjectVersion"] = "Версия объекта, к которой относится переход или решение; stale commands отклоняются.",
        ["ConsideredDataRevision"] = "Точная версия данных объявления, рассмотренная руководителем при решении.",
        ["RequesterEmployeeId"] = "Менеджер, передавший объект на рассмотрение. Не может одобрить собственную передачу.",
        ["ApproverEmployeeId"] = "Руководитель, фактически сохранивший решение по передаче.",
        ["Outcome"] = "Решение руководителя: Return/Approve/Monitor/Reject. Не подразумевает покупку или финансовое обязательство.",
        ["DueAt"] = "Опциональный UTC срок выполнения рабочей задачи или уточнений при возврате.",
        ["EffectiveAt"] = "Фактический UTC момент бизнес-действия (например контакта), если известен; RecordedAt отдельно фиксирует запись.",
        ["ReadAt"] = "UTC момент просмотра внутреннего уведомления; отсутствие означает непрочитанное.",
        ["Completed"] = "Завершена ли текущая рабочая задача; факты истории сохраняются независимо.",
        ["Body"] = "Рабочее содержание записи timeline: причина, что исправить/уточнить или результат ручного контакта.",
        ["State"] = "Состояние работы: Pending ожидает выдачи; Leased действует до LeaseExpiresAt; Completed/LimitReached завершены; ручная проверка/ошибка/прерывание не являются успешной пустой выдачей.",
        ["CredentialHash"] = "SHA-256 verifier высокоэнтропийного credential Collector. Открытый token показывается только при создании.",
        ["Capabilities"] = "Источники, которые поддерживает установленная версия Collector; неподдерживаемая работа не выдаётся.",
        ["VersionText"] = "Версия установленного приложения Collector; не concurrency token.",
        ["LastHeartbeatAt"] = "Последний принятый heartbeat UTC; online означает enabled и связь не старше трёх минут.",
        ["AgentId"] = "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение.",
        ["DepartmentId"] = "Подразделение закупки, ограничивающее Department visibility объекта.",
        ["LeaseId"] = "Случайный fencing token конкретной выдачи работы; прежний token после перевыдачи отклоняется.",
        ["LeaseExpiresAt"] = "UTC срок действия reservation; heartbeat продлевает только действующий lease.",
        ["PayloadHash"] = "SHA-256 принятого contract payload для проверки неизменности повторной доставки.",
        ["ReceiptJson"] = "Прежний результат при idempotent retry, включая фактические accepted/duplicate counters.",
        ["ObservationKey"] = "Стабильный ID локального наблюдения Collector; не ExternalId объявления.",
        ["ContentHash"] = "SHA-256 typed observation для deduplication; не идентификатор объекта недвижимости.",
        ["PayloadJson"] = "Typed public source observation: Raw/Parsed/Presence, provenance, adapter version. Не raw browser response.",
        ["ChangesJson"] = "Поля, изменившие известное состояние; отсутствие поля не обозначает очистку.",
        ["ObservedAt"] = "UTC момент наблюдения источника; поздняя доставка не делает старые значения новыми.",
        ["FirstObservedAt"] = "Самый ранний известный UTC момент наблюдения этого объявления.",
        ["LastObservedAt"] = "Самый новый принятый UTC момент наблюдения для обновления current state.",
        ["ChangedAt"] = "UTC момент регистрации изменения бизнес-данных; используется для очереди новых/изменившихся объектов.",
        ["DataRevision"] = "Версия существенных данных объявления. Не увеличивается от неизменного повторного наблюдения.",
        ["QueueReason"] = "Объяснение появления в очереди: новое объявление или реально изменённые поля.",
        ["Price"] = "Последняя известная публичная цена предложения, decimal; не подтверждённый факт сделки. Валюта отдельно.",
        ["Currency"] = "ISO 4217 валюта денежного значения; Stage 1 принимает RUB.",
        ["AreaSquareMeters"] = "Последняя известная площадь в м², decimal. Отсутствие не равно нулю.",
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
                else if (table == "listings" && property.Name == "Url")
                {
                    property.SetComment("Опциональная HTTPS-ссылка на источник; ручное предложение может существовать без URL.");
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
