# Gate B1-03 — назначения, Owner и граница политики шаблонов

**Дата:** 18 сентября 2026 года.
**Ветка:** `codex/release-package-01`.
**Исходный commit:** `017136fb67f1e0b379a69251f97fd705032c93dc`.
**Scope:** LR-02, LR-04 и decision item LR-23.
**Статус после публикации:** реализовано и опубликовано; исполнение не проверено до B1-04.

## LR-02 — получатель и реальный доступ

Назначение не должно создавать работу, которую получатель не может открыть. B1-03 не расширяет значения `Own` и `AssignedObjects`.

- `Own` видит case, когда сотрудник является `ManagerEmployeeId`.
- `AssignedObjects` видит case, когда сотрудник является текущим case assignment.
- Team/Department/Organization продолжают работать по своей области.

Для передачи решения проверяется видимость **после передачи**. `Forward` меняет case assignment, поэтому AssignedObjects-head допустим, а произвольный Own-head — нет. `Return` меняет manager и assignment, поэтому Own/AssignedObjects manager может стать допустимым получателем.

Назначение только следующей задачи или ответственного проверки не меняет ownership/assignment case. Поэтому такой получатель обязан уже иметь видимость case. Старый/stale payload повторно проверяется сервером.

## LR-04 — последний Owner

`SetEmployeeActiveAsync` и `ChangeAssignmentAsync` используют один organization-scoped PostgreSQL advisory transaction lock. Проверка последнего активного Owner и изменение выполняются внутри транзакции после получения lock. Это сериализует конкурентное отключение и снятие роли, включая смешанный сценарий.

UI-предупреждение не является защитой инварианта; authority остаётся на сервере.

## LR-23 — политика общих шаблонов

Новая матрица прав **не утверждена владельцем продукта**, поэтому эффективное поведение не меняется: Manager/Head с текущим dossier permission по-прежнему могут менять shared check/inspection templates.

Код получает отдельный `RequireTemplateManagementPermissionAsync` и отдельный `CanManageTemplates` capability. Это отделяет будущую продуктовую политику от общего dossier permission без скрытого изменения прав сегодня.

LR-23 остаётся **OPEN DECISION ITEM**. Отдельное решение должно определить read/use/edit/archive shared templates и роли, которым это разрешено.

## Не входит

B1-04 handover при отключении сотрудника, изменение scope semantics, выдача Organization scope ради обхода, новые роли/permissions, миграции, Parser/Collector и production operations.

## Проверки для B1-04

1. Forward/Return с Own/AssignedObjects/Team/Department/Organization дают доступ только там, где он следует из фактической post-assignment visibility.
2. stale payload не может назначить task/check человеку, который case не видит.
3. допустимый получатель task/check открывает case; посторонний case не становится видимым.
4. два конкурентных отключения Owners оставляют минимум одного активного Owner.
5. смешанные deactivate + role removal также оставляют минимум одного активного Owner.
6. существующая эффективная политика шаблонов не изменилась; отдельный capability существует.
7. B1-01/B1-02 regression и общие authorization tests проходят.

Build/tests/browser в B1-03 по схеме 3+1 не запускаются. B1-04 автоматически не начинать.
