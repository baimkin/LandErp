# INSPECTION-DRAFT-MERGE

04.11.2026 владелец разрешил продолжение/завершение осмотра между вкладками
без потери ответов, выбор только для пересекающихся правок. Дополнение: воспроизвести
одну вкладку — начало, видео, ответы, завершение. Причина production случая пока не доказана.
Ветка codex/inspection-file-improvements; накопленное и параллельный Parser сохранить.

## Required reading

README → START_HERE → ACTIVE_TASK → этот Gate; ранее прочитанные FP-004
§§3,9,14–15, ADR-005, UI instructions/consistency/tokens, screen04; страница,
browser drafts, SaveInspection, optimistic versions и адресные tests.

## Scope

Трёхстороннее объединение исходного снимка, пользовательских правок и свежего
серверного состояния. Ответ+статус — одно смысловое поле, комментарий отдельно.
Собственные черновики вкладок не затирают друг друга. Устаревшие ответы не
перезаписываются молча; старые черновики без исходного снимка сохраняются.
Конфликты по отдельным полям с явным выбором, повторный concurrent save защищён.
Завершённый/переназначенный осмотр и отзыв доступа не обходятся.
Без изменения uploads/Диска, ролей, migrations, пакетов, production и commit/push.

Файлы: ACTIVE_TASK, этот Gate, screen04, reports/INSPECTION_DRAFT_MERGE_REPORT.md;
SiteInspectionPage.razor/.css/.js, InspectionDraftMerge.cs; ProcurementWorkspace.cs
(атомарность сохранения); InspectionDraftMergeTests.cs, browser regression и
scripts/Test-PropertyInspectionDraft.cjs.

## Проверки

Release build; unit merge: disjoint/same/different/empty/status/note/legacy/multiple
drafts; JS storage isolation/conditional acknowledgement/offline; PostgreSQL stale
versions/atomic completion; реальные две вкладки и single-tab media→answers→complete,
конфликт/выбор/reload/desktop/mobile. Browser проверка разрешена этим Gate как
обязательная приёмка сценария; историческое исключение UI consistency не относится.
Отчёт: файлы, решения, команды, результаты, ограничения, следующий шаг.

## Результат

Реализовано локально. Release build 0 warnings/errors, 14/14 адресных .NET проверок,
8/8 JS. Реальные single-tab видео→ответы→завершение и две вкладки, выбор поля,
поздний экспорт проверены. Desktop/mobile screenshots просмотрены; финальный browser повтор с геометрией
mobile conflict panel — 1/1 Passed, 1 мин 9 с. Production/commit/push не выполнялись.
Старые черновики без baseline сохраняются со сверкой неоднозначных полей.
Отчёт reports/INSPECTION_DRAFT_MERGE_REPORT.md. Следующий Gate не начат.
