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
        ["stages"] = "Стабильные стадии первого процесса закупки, включая terminal acquired. Approved означает дальнейшую работу, не покупку.",
        ["assignments"] = "Актуальная ответственность за бизнес-объект. Передача и возврат меняют исполнителя атомарно с workflow и историей.",
        ["work_tasks"] = "Актуальная рабочая задача сотрудника по объекту, с опциональным сроком UTC; Completed не удаляет историю выполненной работы.",
        ["transitions"] = "Неизменяемые факты переходов workflow: кто выполнил действие, прежняя/новая стадия и версия объекта.",
        ["approvals"] = "Неизменяемые решения руководителя по конкретной передаче и версии данных. Одобрение не является юридическим или финансовым фактом сделки.",
        ["business_timeline"] = "Неизменяемая бизнес-история объекта: решения, заметки, контакты, кому возвращён объект, что уточнить и срок. Отдельна от технического audit.",
        ["notifications"] = "Внутренние уведомления исполнителю о передаче/возврате/решении. ReadAt отмечает просмотр; внешняя доставка не включена.",
        ["negotiations"] = "Неизменяемый CRM-журнал событий коммуникации PropertyCase. Цена продавца, предложение покупателя и согласованная цена опциональны и не смешиваются с публичной ценой источника.",
        ["case_checks"] = "Структурированные быстрые и глубокие проверки PropertyCase с ответственным, сроком, результатом и защищённым признаком блокера.",
        ["case_check_template_items"] = "Редактируемый справочник типовых проверок организации. CaseCheck хранит snapshot и не меняется вслед за справочником.",
        ["stored_files"] = "Провайдер-независимые метаданные файлов и внешних ссылок. Непрозрачный ключ хранилища не является пользовательским URL.",
        ["case_attachments"] = "Связи вложений с PropertyCase, переговорами, проверками, осмотром или его пунктом; доступ всегда наследуется от owning PropertyCase.",
        ["inspection_template_items"] = "Версионируемый настраиваемый чек-лист полевого осмотра организации без универсального rules engine.",
        ["site_inspections"] = "Полевой осмотр конкретного PropertyCase: черновик, общий вывод, решение и факт завершения.",
        ["site_inspection_items"] = "Snapshot пунктов шаблона и ответы конкретного осмотра; последующее изменение шаблона не переписывает историю.",
        ["case_fact_revisions"] = "Неизменяемые подтверждения явного переноса значения источника в рабочий факт PropertyCase; автоматическое перезаписывание запрещено.",
        ["agents"] = "Зарегистрированные локальные Collector. Сервер хранит только verifier credential; отзыв немедленно запрещает новые обращения.",
        ["search_configurations"] = "Поисковые ссылки организации без назначения конкретного Collector или области закупки. Browser settings и cookies остаются локально.",
        ["search_groups"] = "Тонкая организационная группировка поисков без владения маршрутизацией, исполнителем или бизнес-процессом.",
        ["jobs"] = "Работа общего пула: Pending без исполнителя; Agent назначается при claim. Lease fencing и terminal executor сохраняют фактическую историю.",
        ["deliveries"] = "Неизменяемые квитанции доставки Collector. Повтор ResultId с тем же payload возвращает прежний ответ; другой payload запрещён.",
        ["listings"] = "Универсальные входящие предложения Catalog из автоматических и ручных источников; не идентичность земельного участка.",
        ["observations"] = "Неизменяемые наблюдения публичных объявлений: Source/ExternalId, Raw/Parsed/Presence, provenance и версия адаптера. Browser state и raw HTML здесь не хранятся.",
        ["events"] = "Неизменяемая бизнес-история внимания к входящему предложению: изменения источника, мониторинг цены, классификация и возобновление кейса.",
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
        ["StageId"] = "Текущая стадия закупки. Только acquired обозначает подтверждённую завершённую покупку.",
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
        ["Disposition"] = "Текущее решение первичного отбора: входящее, мониторинг, в работе, отклонено, дубль, фейк, снято или продано.",
        ["AttentionRequired"] = "Явный признак, что входящий элемент требует повторного внимания из-за новых данных или выполненного условия мониторинга.",
        ["AttentionAt"] = "UTC момент последнего сигнала внимания к входящему элементу.",
        ["TargetTotalPrice"] = "Независимый порог общей цены для мониторинга; null означает, что условие не задано.",
        ["TargetPricePerSotka"] = "Независимый порог цены за сотку для мониторинга; заполненные пороги применяются по правилу ИЛИ.",
        ["MonitoringStartedAt"] = "UTC момент установки текущих условий мониторинга цены.",
        ["LastEvaluatedPrice"] = "Последняя общая цена источника, использованная при оценке условий мониторинга.",
        ["LastEvaluatedPricePerSotka"] = "Последняя вычисленная цена за сотку, использованная при оценке условий мониторинга.",
        ["LastEvaluatedAt"] = "UTC момент последней оценки текущих source values против условий мониторинга.",
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
        ["SellerPrice"] = "Опциональная названная продавцом цена в конкретном событии коммуникации; публичная цена источника хранится отдельно.",
        ["BuyerOffer"] = "Опциональное предложение покупателя в том же реальном событии коммуникации.",
        ["AgreedPrice"] = "Опциональная согласованная цена переговоров; сама по себе не означает приобретение.",
        ["Channel"] = "Канал контакта: звонок, сообщение, встреча или другой понятный сотруднику способ.",
        ["NextStep"] = "Следующее согласованное действие после контакта без создания отдельного workflow engine.",
        ["Level"] = "Уровень проверки: быстрая первичная либо глубокая/юридическая.",
        ["Blocker"] = "Явный блокирующий риск; установка и снятие требуют права руководителя.",
        ["OwnerType"] = "Owning business object вложения: PropertyCase, событие переговоров, проверка, осмотр или пункт осмотра.",
        ["StoredFileId"] = "Стабильная ссылка на provider-neutral метаданные; StorageKey не показывается сотруднику.",
        ["StorageKey"] = "Непрозрачный внутренний ключ backend-хранилища; не является публичной ссылкой и не выводится в staff UI.",
        ["ExternalUrl"] = "Проверенная HTTPS-ссылка для link-вложения; внутренний файл вместо неё использует закрытый StorageKey.",
        ["Sha256"] = "SHA-256 содержимого для контроля целостности без хранения бинарных данных в PostgreSQL.",
        ["SizeBytes"] = "Размер файла в байтах после принятой загрузки; лимит проверяется сервером.",
        ["Field"] = "Рабочее поле PropertyCase, которое пользователь явно подтвердил из конкретного источника.",
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
        ["MustChangePassword"] = "Требует сменить выданный администратором временный пароль до обычной работы в ERP.",
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
                if (table == "search_configurations" && property.Name is "AgentId" or "DepartmentId" or "TeamId")
                {
                    property.SetComment("Устаревшее поле прежней маршрутизации; новый runtime его не читает и не заполняет.");
                }
                else if (table == "jobs" && property.Name == "AgentId")
                {
                    property.SetComment("Фактический исполнитель работы; отсутствует у Pending и устанавливается атомарно при claim.");
                }
                else if (table == "search_configurations" && property.Name == "NextRunAt") property.SetComment("Следующий расчётный запуск UTC; отсутствует у ручного или приостановленного поиска.");
                else if (table == "jobs" && property.Name == "ScheduledFor") property.SetComment("Расчётный момент запуска UTC; обеспечивает идемпотентность планировщика.");
                else if (table == "events" && property.Name == "CatalogItemId") property.SetComment("Входящий элемент Catalog, к которому относится неизменяемое событие внимания.");
                else if (table == "events" && property.Name == "Kind") property.SetComment("Стабильный тип события: изменение источника, мониторинг, классификация или возобновление кейса.");
                else if (table == "events" && property.Name == "Message") property.SetComment("Человекочитаемое объяснение события без секретов и raw payload источника.");
                else if (table == "events" && property.Name == "ObservedPrice") property.SetComment("Общая цена источника в момент события, если была известна.");
                else if (table == "events" && property.Name == "ObservedPricePerSotka") property.SetComment("Вычисленная цена за сотку в момент события, если цена и площадь были известны.");
                else if (table == "case_fact_revisions" && property.Name == "Value") property.SetComment("Человекочитаемое значение рабочего факта в момент явного подтверждения сотрудником.");
                else if (table == "negotiations" && property.Name == "Outcome") property.SetComment("Результат конкретного контакта с продавцом; не является workflow-решением или фактом покупки.");
                else if (table == "stored_files" && property.Name == "CreatedByEmployeeId") property.SetComment("Сотрудник, инициировавший загрузку файла или добавление внешней ссылки.");
                else if (table is "org_units" or "teams" && property.Name == "ManagerEmployeeId") property.SetComment("Назначенный руководитель подразделения или команды; права доступа определяются отдельно ролью и scope.");
                else if (table is "org_units" or "positions" or "teams" && property.Name == "Active") property.SetComment("Активный элемент доступен для новых назначений; архивный сохраняется в истории и может быть восстановлен.");
                else if (ColumnComments.TryGetValue(property.Name, out string? comment))
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
