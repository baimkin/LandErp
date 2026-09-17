# Доработка расписаний и управления Parser

Статус: реализовано и проверено; ручная визуальная приёмка владельцем.
Ветка: `codex/collectors-scheduling-ux`.
Основание: владелец разрешил весь набор исправлений по аудиту экрана.

## Required reading

- README, START_HERE, ACTIVE_TASK;
- UI-kit instructions, screen 07, актуальные UI tokens;
- ADR-007;
- FP-004 (время, права, аудит, границы общих механизмов);
- предыдущий аудит в текущем диалоге и текущие Collection/UI исходники.

## Scope и файлы

- Collectors.razor/css и код формы: отдельные времена, интервалы с единицами,
  создание группы без потери формы, первый запуск, локальное время, архив,
  автoобновление, предупреждения, компактные карточки и иерархия;
- CollectionServices, CollectionAdministration, CollectionScheduleRules:
  атомарное создание поиска с первым заданием, сохранение существующего срока
  при редактировании других полей, серверная валидация;
- CollectionSchedulingTests и необходимые тесты формы;
- локальный launcher с Worker, README и итоговый отчёт.

Без миграций, изменений browser/source adapters, новых пакетов, merge/push.
Проверки: Release build, foundation unit и Collection PostgreSQL tests.
Браузерные тесты исключены владельцем. Финальная визуальная приёмка — вручную.

Evidence: reports/COLLECTORS_SCHEDULING_UX_REPORT.md.
Финальный Release build: 0 warnings/errors, 22/22 целевых теста.
Локальный Server + Worker запущены; scheduler health подтверждён чтением БД.
