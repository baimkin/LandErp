# Gate B1-04 — передача активной работы и проверка Release Package 01

**Дата:** 18 сентября 2026 года.  
**Ветка:** `codex/release-package-01`.  
**Исходный SHA:** `54fbb73a233bbfe6a085423f0ab5fd8668b916aa`.  
**Scope:** LR-03 + интеграционная проверка B1-01–B1-03.  
**Статус:** реализация B1-04 и удалённая проверка пакета.

## Цель

Отключение сотрудника не должно оставлять неуправляемую активную работу и не должно сохранять ему доступ только ради ручного перераспределения.

Перед отключением система показывает:
- затрагиваемые активные PropertyCase;
- где сотрудник является менеджером или текущим исполнителем;
- незавершённые WorkTask;
- открытые CaseCheck;
- ожидающие решения руководителя;
- сотрудников, которые способны принять **весь** набор работы с учётом роли, permission и scope.

Обычный путь — атомарная передача выбранному сотруднику и отзыв доступа. Для срочного инцидента разрешён явный emergency revoke без переназначения: доступ отзывается немедленно, в timeline/audit фиксируется незавершённый handover, а позднее работу можно передать отдельной командой.

## Инварианты

1. Исторические авторы заметок, переговоров, проверок, осмотров и аудита не переписываются.
2. При handover меняются только текущие responsibility pointers: `PropertyCase.ManagerEmployeeId`, case assignment, незавершённый task и открытые checks.
3. Pending approval не пересоздаётся; меняется текущий assignee, а новый получатель обязан иметь Head permission и не должен становиться одновременно manager этого case.
4. Recipient обязан реально видеть каждый затронутый case после передачи. Scope `Own` / `AssignedObjects` не расширяется.
5. Deactivation, handover, role/scope change и Procurement-команды, создающие/меняющие текущую ответственность, сериализуются общим organization-scoped PostgreSQL advisory transaction lock.
6. После завершившегося deactivation новая активная работа не должна появиться на отключённом employee: если assignment успел первым — deactivation видит и передаёт его; если deactivation успел первым — последующая команда отклоняет неактивного получателя.
7. Последний Owner по-прежнему защищён отдельным инвариантом B1-03.
8. LR-23 остаётся отдельным product decision; B1-04 его не переопределяет.

## UI

Экран Organization при отключении сотрудника сначала загружает impact preview. При наличии работы пользователь выбирает допустимого получателя и подтверждает «Передать и отключить». Отдельная заметная кнопка emergency revoke не маскируется под обычное подтверждение. Для уже отключённого сотрудника доступно позднее «Переназначить работу».

## Проверка пакета

В репозитории ранее не было GitHub Actions workflow для этой ветки, а текущая чат-среда не может клонировать GitHub наружу. Поэтому B1-04 добавляет branch-scoped workflow `.github/workflows/release-package-01.yml`:

- Ubuntu runner;
- .NET SDK из `global.json`;
- PostgreSQL 18 service;
- locked restore;
- Release build тестового графа;
- targeted tests B1-01–B1-04 + Identity/Organization и выборочные Procurement regressions;
- публикация TRX artifact.

Workflow не применяет migrations к общей/production БД: каждый PostgreSQL test создаёт собственный sandbox.

## Не входит

Merge в `main`, production deployment, изменение Parser/Collector, рабочая БД пользователя, новый workflow engine, post-purchase modules и изменение политики shared templates.

При сбое build/test обычные ошибки текущего пакета исправляются в B1-04 отдельным follow-up commit с записью evidence. Нельзя ослаблять assertions ради зелёного результата.
