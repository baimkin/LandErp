# INCOMING-PACKAGE-A

23.11.2026 владелец прямо разрешил пакет A по PLAN-INCOMING-FILTER-PRECOMPUTE:
реализация и проверка корректности/скорости, с сохранением накопленных изменений.
Ветка codex/inspection-file-improvements сохраняется; без commit/push/production.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate →
PLAN-INCOMING-FILTER-PRECOMPUTE §§1–6,8–10 →
reports/INCOMING_FILTER_PLAN_AUDIT_REPORT.md и уже прочитанные
reports/INCOMING_LOAD_TIMEOUT_REPORT.md / FP-004 §§3,9,12–15 → адресный код/tests.
Пакет B не активирован. Схема, migrations, runtime grants, ingress и пакеты не меняются.

Scope: отдельное основное чтение rows+exact Total со снимком и clamp;
пакетный resolver, SQL-CTE запросного времени с прежними predicates;
ограниченная metadata; отдельные summary/alternatives/market состояния UI,
локальный снимок чисел до 60 с, отмена/late response/mutation guards.
Сохранить authoritative bulk/права/median и fast-dismiss/open/transfer.

Файлы: IncomingCatalogReadContracts; IncomingCatalogReadService/Query/Selection,
IncomingFilterPresetService и внутренний IncomingCatalogReadSql; IncomingCatalogV2
(.razor/.Dismiss.cs/.Updates.cs, новый partial .Information.cs), адресные Foundation
tests и screen01; этот Gate/своя секция ACTIVE_TASK/отчёт и ignored artifacts.
WorkspaceComponent не менять, если локального partial достаточно.

Проверки: Release build, differential IDs/Total/all informational counts,
overlaps/zones/legacy/org/modes/time/criteria, 25/50/100/selection/fast updates,
secondary error/late response, cold/warm repeat measurements на synthetic
25k/500k/50 и запас роста; ≥4 readers с ingress, DB round trips/plan/locks.
Только disposable test PostgreSQL; работающую БД и preview не трогать.
Отчёт reports/INCOMING_PACKAGE_A_REPORT.md фиксирует результаты и ограничения;
при невыполненной цели не заявлять SLA и не переходить к B.

## Результат — 23.11.2026

Пакет A реализован. Отчёт [INCOMING_PACKAGE_A_REPORT](../../reports/INCOMING_PACKAGE_A_REPORT.md).
Main rows+exact Total и информационные числа разделены; shared запросные CTE,
batch resolver, bounded metadata, UI scope/mutation/single-flight и fast updates.
Differential/selection/preset/archive/sort 10/10, UI/component и headless browser
проверки пройдены. Оба performance набора Passed с concurrent readers + ingress.
25k/500k/50: main 1.482–1.976 с, full 1.712–1.822 с против прежнего full 11.031 с.
100k/2m/100: main 7.689–9.733 с; это предел, а не обещание 3 с на любом объёме.
Новых schema/пакетов/ingress изменений нет; рабочие БД/preview/production,
ветка/индекс и чужой накопленный scope сохранены. Commit/push не выполнялись.
Финальная проверка документов/сохранности пройдена; контрольный UI guard 1/1 Passed.
Gate завершён. Пакет B и следующий Gate не активированы; работа остановлена.
