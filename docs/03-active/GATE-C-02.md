# C-02 — Упрощение структурированных проверок участка

Прямой запрос владельца 2026-09-27, поверх незакоммиченной C-01.
Ветка прежняя: `codex/c-01-rich-case-notes`, HEAD `1c9c7c73410adbc83b65f75f6e543854706c39aa`.

## Scope и решения

Компактное inline создание/изменение записей внутри базовой/глубокой секций.
Одно название с подсказками существующих шаблонов, сразу доступный результат,
общий Tiptap/editor C-01. Свободные документы секций независимы от проверок.
Planned → «Не проверено», Passed → «В порядке», Issue → «Есть вопросы».
InProgress/Blocked не схлопываются: существующее состояние показывается отдельным
вариантом и сохраняется, пока сотрудник явно его не изменит. Completed semantics
и правила принятия решений не меняются. Blocker — отдельный признак с прежними
Head permissions, не включается из-за Issue и не снимается при изменении текста.

Название обязательно (прежние 3–512 символов). Для не-Planned сохраняется
серверное требование результата. Исполнитель/срок/стоимость необязательны при
создании, существующие значения сохраняются. Нового назначения/задач нет.
Глубина определяется секцией, шаблон другой секции не обходит stage guard.
Приобретённые/закрытые объекты остаются в чтении; deep доступна после одобрения.

## Required reading

- ACTIVE_TASK, GATE-C-01, C_01_REPORT; действующие прочитанные UI/data/comment rules.
- FP-004 §§1–3, 9, 14–15 о правах, документах, истории; не весь foundation.
- Только CaseWorkspace/checks, CaseFormValues, CaseRichNoteBlock, CaseNoteDocument,
  SaveCheck, CheckTemplates, CaseCheck domain/contracts/mapping и узкие tests.
- Скриншот `codex-clipboard-b17da81b-5ebb-4dac-8002-0b5695ae0070.png` просмотрен.

## Файлы и хранение

- Server: CaseWorkspace.razor, новый CaseCheckEntry.razor(.css), адаптация
  CaseRichNoteBlock.razor и существующего JS C-01 (общий dirty/save/cancel).
- Application: CaseCheck в PropertyCaseDossier, ProcurementContracts,
  CaseNoteDocument (plain-text compatibility).
- Infrastructure: SaveCheck/ReadCard в ProcurementWorkspace, общий validator
  ссылок на изображения в ProcurementRichNotes, ProcurementMappings.
- Одна forward migration: nullable `case_checks.result_document_json` + comments,
  snapshot. Старый result читается как текст, массовой миграции нет.
  Старый result остаётся текстовым представлением для существующих consumers;
  полный rich result хранится в новом поле. Текущие grants case_checks достаточны.
- Узкие CaseCheckEntryTests; docs ACTIVE_TASK/Gate/C_02_REPORT.

## Проверки и границы

Release build через целевой test project, только новые риски: готовый результат
без назначения/срока, legacy статусы/blocker/дополнительные данные, plain/rich
чтение, безопасность изображений, права/org/version и closed/deep ограничения.
Reuse PostgreSQL fixture, только disposable DB. Новых сторонних пакетов нет.
Без Server/браузера/визуальных прогонов, broad suites/F/M/Parser/Live,
foundation-скрипта, рабочих БД, commit/push/merge/rebase/main и следующих Gates.
Ручная/визуальная приёмка за владельцем. C-01 и её история сохраняются.

Статус: реализовано. Четыре целевых теста и Release build прошли.
Ручная/визуальная приёмка не выполнена. [Отчёт C_02_REPORT](reports/C_02_REPORT.md).
Работа остановлена на C-02; дальнейшие Gates не активированы.
