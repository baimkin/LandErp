# C-01 — Свободный текст и изображения в карточке закупки

Прямой запрос владельца 2026-09-27. Ветка `codex/c-01-rich-case-notes`.
Проверенная база: `1c9c7c73410adbc83b65f75f6e543854706c39aa`, включает F/M.

## Scope

Три независимых документа PropertyCase: «Рабочие заметки и расчёты» под
описанием на вкладке «Основное», свободный текст базовых и глубоких проверок.
«Ход работы» только перемещается ниже; M-03 и структурированные проверки сохраняются.
Один редактор: заголовки, жирное/курсив, списки, ссылки, простые таблицы,
изображения из буфера и файла. Компактное чтение/редактирование, сохранение,
отмена, защита черновика при переходах/загрузке/refresh, явный конфликт версии.

## Required reading

- README → START_HERE → ACTIVE_TASK, AGENTS.md.
- M_03_REPORT — только передача карточки и неприменённых миграций.
- STAGE-1_DATA_CONVENTIONS.md, ERP-01_DB_COMMENT_CONVENTION.md.
- docs/14-ui-kit/AGENT_UI_INSTRUCTIONS.md и screens/03-property-case.md;
  актуальная карточка и два скриншота из прямого запроса — authority размещения C-01.
- docs/04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md:
  §§1–3, 9, 14–15 (границы, права, документы, история).

## Предлагаемое решение и файлы

Готового редактора в текущих assets/dependencies нет. Предложен Tiptap 3
(открытые MIT core/StarterKit/table/image), локальный bundle через esbuild (MIT).
Без облака, CDN, платных расширений. **Согласовано владельцем 2026-09-27**:
только бесплатный open-source. Tiptap 3.31.3 и esbuild 0.28.2 зафиксированы,
транзитивные версии в lockfile, все лицензии MIT; notices включены в assets.
Проверены официальные источники 2026-09-27:
https://tiptap.dev/docs/editor/getting-started/overview,
https://github.com/ueberdosis/tiptap/releases,
https://github.com/evanw/esbuild/blob/main/LICENSE.md.

Хранение: ограниченный JSON-документ с серверной проверкой структуры и ссылок,
без произвольного HTML/base64/внешних изображений; безопасный серверный вывод.
Отдельная запись для пары case/секция, bigint Version, автор/UTC-время;
до/после в принятом append-only audit, без отдельного versioning engine.
Изображения через существующие CaseAttachment/StoredFile/upload/retry/read;
проверка организации, case, типа/статуса файла и прав. Отмена файлы не удаляет.

План файлов:
- Application Procurement: CaseRichNote domain/public contracts/validation.
- Infrastructure ProcurementWorkspace, mappings, DbContext и новая forward migration/snapshot.
- Runtime grants через общий ProductionDatabaseInitializer для штатных setup/test flows.
- Server: CaseRichNoteBlock.razor(.css), изолированный JS, CaseWorkspace.razor(.css).
- Локальные package manifest/lock/build script/assets и лицензии после согласования.
- tests/LandErp.Foundation.Tests/CaseRichNoteTests.cs.
- ACTIVE_TASK, этот Gate и reports/C_01_REPORT.md.

## Проверки и границы

Одна нужная Release-сборка и узкие тесты: независимость трёх полей, повторное
сохранение/версии/конфликт, права/организация/видимость, unsafe content/links,
чужие или недоступные изображения и существующая файловая интеграция.
Схема/comments/grants — только штатная disposable DB; рабочие БД запрещены.
Без полного suite, F/M suites без затронутого инварианта, Parser/Live,
foundation-скрипта, Server/браузера/визуальных прогонов, commit/push/merge/rebase.
Migrations 20260926120000 и 20260926140000 остаются неприменёнными к рабочей БД.
Визуальная приёмка и ввод/картинки/сохранение/отмена — вручную владельцем.
C-02/T/U/K, формулы, изменения рынка/согласований и последующие Gates запрещены.

## Статус

Реализовано. Release-сборка и целевые проверки прошли. Рабочие БД не менялись;
ручная/визуальная приёмка не выполнялась. Evidence: [C_01_REPORT](reports/C_01_REPORT.md).
Следующие Gates не активированы.
