# LandErp — бизнес-карта ERP и порядок первой реализации

**Версия:** 0.1  
**Дата:** 14 сентября 2026 года  
**Статус:** Принят как обязательная вводная и порядок ERP-этапов по ERP-00, 2026-09-14; не разрешает их автоматическую реализацию\
**Тип:** обязательное уточнение MASTER перед production ERP  
**Связанные документы:** MASTER, FP-001–FP-004, ADR-001–ADR-007

## 1. Зачем нужен документ

Документ уточняет реальный бизнес-процесс и порядок первых ERP-этапов. До обновления MASTER он используется как более новое уточнение в этой части. Сам по себе код не разрешает: исполняемой единицей остаётся Gate/TP.

## 2. Реальный поток объекта

```text
Рынок / Collector
  ↓
Listing
  ↓
Менеджер закупки
  ├─ отклонить
  ├─ наблюдать → возврат по цене/дате/событию
  ├─ уточнить
  └─ передать руководителю
          ↓
Руководитель закупки
  ├─ вернуть менеджеру
  ├─ отклонить/наблюдать
  └─ Due Diligence
          ↓
Инвестиционный кандидат
   ↙                     ↘
сделка/юристы       Investment Opportunity
   ↘                     ↙
             Покупка
               ↓
       InvestmentProject
      ↙        ↓         ↘
   Хозблок   Продажи   Маркетинг
      ↘        ↓         ↙
         ProjectLot / Sale
               ↓
     финансы / инвесторы / закрытие
```

Коммерческий директор не является стадией: он имеет сквозной доступ по permissions и получает управленческий cockpit по закупке, срокам, финансам, работам и продажам.

Основная трассируемая цепочка: `Listing → PropertyCase → InvestmentOpportunity → Purchase → InvestmentProject → LandAsset → ProjectLot → SaleCase`.

## 3. Что добавить в будущие планы

Существующие FP сохраняются. Перед соответствующей реализацией нужны:

- `FP-027` — закупка и передача менеджер → руководитель, включая возврат вниз;
- `FP-037` — Investment Opportunity до покупки: экономика, капитал, материалы и интерес инвесторов;
- `FP-047` — CRM продажи лотов: лиды, звонки, показы, follow-up, предложения и бронь;
- `FP-048` — Marketing Workspace: фото, схемы, тексты, презентации, версии и согласование.

`FP-071` остаётся расширенной аналитикой, но базовый Director Cockpit наращивается постепенно с каждым production-контуром.

## 4. Соглашения по данным, которые нельзя откладывать

Перед первой production-миграцией на High-уровне проверить:

- внутренний ID — PostgreSQL `uuid`, предпочтительно UUIDv7; внешний ID и человекочитаемый `BusinessNumber` хранятся отдельно;
- время — UTC/`timestamptz`, явная business timezone, для значимых фактов `RecordedAt` и `EffectiveAt`, для сбора также `ObservedAt`;
- деньги — decimal + currency, единая precision/rounding policy, без float/double;
- значимые изменяемые агрегаты — optimistic concurrency/version; approval относится к конкретной версии согласуемых данных;
- audit, approvals, подтверждённые финансовые и юридические факты не удаляются обычным приложением;
- applied migrations не переписываются, исправления идут новой migration;
- не использовать full Event Sourcing: текущее состояние реляционное, важные факты имеют append-only историю;
- настраиваемые бизнес-стадии отделяются от строгих системных фактов (`Acquired`, Payment, Transfer, Sale и т.п.).

## 5. Ядро

FP-004 задаёт общие механизмы: Identity/permissions, оргструктуру, workflow/справочники, WorkTask, Approval, документы/версии, Audit + BusinessTimeline, уведомления, integration references, метрики и финансовый управленческий subledger.

Правило реализации: общий механизм проектируется качественно, но кодируется только в минимальном объёме первого реального use case. Никаких собственных BPMN, low-code, ESB или «универсального конструктора ERP» без доказанной необходимости.

Для SLA вводится business timezone и простой `WorkingCalendar`; сложные календари подразделений позже.

## 6. Финансы

LandErp ведёт управленческий/project subledger и не заменяет 1С. Различаются `Budget`, `Commitment`, `Accrual/Obligation`, `Payment`, `Receipt`, `Allocation`, `Adjustment/Reversal`.

Подтверждённый финансовый факт не редактируется незаметно. Он имеет `LegalEntity`, `Counterparty`, проект, при необходимости лот/работу, договор, категорию, бюджетную строку, Money, даты, source document и будущую `ExternalAccountingReference`.

Полный план счетов, налоговые регистры и бухгалтерские проводки остаются в 1С. Интеграция появится позже, но external IDs и idempotency закладываются заранее.

## 7. Collector

ADR-007 обязателен: Collector остаётся самостоятельным локальным продуктом. Server общается с ним только через versioned API/contracts. Agent не подключается к PostgreSQL и не знает ERP workflow; Server не содержит source-specific browser logic.

## 8. Первый цикл реализации

### ERP-00 — переход от Spike к production
**Thinking:** High.  
Зафиксировать фактический результат Spike, reusable/experimental части Collector, обновить `ACTIVE_TASK`/`START_HERE`, открыть первый production Gate. ERP-код не писать.

### ERP-01 — техническое основание и БД
**Thinking:** High; финальный review схемы — максимальный доступный.  
Server/Application/Infrastructure/Worker skeleton, PostgreSQL, EF migrations, data conventions из раздела 4, health/logging/error model, migration/integration tests. Без бизнес-модулей.

### ERP-02 — Identity, Organization и visibility
**Thinking:** High для модели/security, Medium для простого UI.  
Owner login, сотрудники, OrgUnit, Position, назначения, роли/permissions/scopes, Department/Organization visibility и audit.

### ERP-03 — Workflow core на закупке
**Thinking:** High для модели/concurrency, Medium для UI.  
Минимальные справочники, WorkflowStage/Transition, Assignment, WorkTask, BusinessTimeline и Approval. Живой поток: `менеджер → руководитель → Return/Approve/Monitor/Reject`. Без BPMN-конструктора.

### ERP-04 — Collector → Server и каталог
**Thinking:** Medium; contract/idempotency review — High.  
Versioned Agent contract, registration/heartbeat/job/result, Listing/Observation, история/dedup/quality minimum. Рабочий Collector не переписывается, получает Server Adapter.

### ERP-05 — рабочее место закупки
**Thinking:** Medium.  
Очередь, карточка, фото/источники/цена, первичный анализ, решения, monitoring, контакты, передача руководителю, возврат и BusinessTimeline. Это первый полноценный business MVP.

После ERP-05 система используется на реальных объектах до расширения ядра.

### ERP-06 — DD + Investment Candidate
**Thinking:** Medium для workflow/UI; High для финансовой модели и юридически значимых data rules.  
Каркас DD, evidence/риски, предварительная экономика, лимит покупки и первая InvestmentOpportunity. Финансовое ядро физически расширяется только под первый настоящий денежный use case.

## 9. Что пока не строим

Полноценную 1С-интеграцию, бухгалтерию, BPMN/low-code, сложные approval-кворумы, email/Telegram delivery, ESB, банк/ЭДО, автоматическую публикацию объявлений и расширенный BI.

## 10. Условие старта ERP-01

До первой production-миграции достаточно:

1. формально выполнить ERP-00 и снять устаревший запрет Spike на Server/PostgreSQL/Blazor;
2. включить FP-001, FP-002, FP-004 и применимые ADR в Required reading;
3. принять соглашения раздела 4;
4. ограничить первый Gate только ERP-01.

После этого дальнейшее планирование не должно блокировать код: детали уточняются перед соответствующим вертикальным этапом.
ERP-00 выполнен документационно; следующий [ERP-01](../03-active/GATE-ERP-01_Техническое_основание_и_БД.md)
подготовлен. Его реализация требует отдельного запроса владельца; data conventions
и состав начальной migration проходят финальный review до её генерации.
