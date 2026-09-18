# GATE-RELEASE-B3-03 — Inspection draft, conflict & server validation

**Package:** Release Package 03 — Reliable Files & Field Inspection  
**Findings:** LR-06 + LR-07 + LR-12  
**Branch:** `codex/release-package-03`  
**Base:** `a500aecf3952c5c307082c1d172d1e60fade4e02`

## Цель

Несохранённые полевые ответы не должны исчезать из-за media upload/read refresh; UI не должен называть локальные изменения «сохранёнными»; stale draft не должен молча перетирать сервер; сервер обязан проверять тип ответа по snapshot осмотра.

## LR-06 — draft integrity

`SiteInspectionPage.ReadAsync` больше не применяет server `MapServer()` поверх активного local draft.

Когда media upload успешно добавляет attachment и общий command wrapper перечитывает CaseCard:
- свежие attachments попадают в card;
- answers / conclusion / decision остаются локальными;
- если Inspection.Version за время upload изменилась, включается conflict вместо overwrite.

Browser scenario получает шаг: изменить choice + conclusion, загрузить фото до сохранения inspection и убедиться, что оба несохранённых значения сохранились.

## LR-07 — version/conflict UX

Страница отслеживает:
- server-saved;
- local draft;
- syncing;
- version conflict.

Local draft хранит собственные InspectionId + Version.

При загрузке localStorage draft:
- same version → восстанавливается как обычный local draft;
- different version → local draft всё равно восстанавливается, но UI показывает `Конфликт версии` и блокирует save/complete/media upload.

Автомерджа нет. Единственный recovery path — явная кнопка:
`Отбросить локальный черновик и загрузить серверную версию`.

Это не позволяет случайно перезаписать новую серверную версию и не теряет локальный draft молча.

`landErpInspection.attach` теперь version-aware: после успешного server save listener переустанавливается на новую version вместо продолжения записи старой version в localStorage.

## LR-12 — server authority

`SaveInspectionAsync` валидирует `Answered` по snapshot:
- Boolean: только true/false;
- Number: decimal;
- Percentage: decimal 0..100;
- Choice: только значение из `OptionsJsonSnapshot`;
- Text: непустой текст.

Number/Percentage принимают точку или русскую десятичную запятую и сохраняются канонически invariant.

`NotChecked` server-side очищает Answer.

Диапазоны для обычного Number не добавляются, потому что текущая snapshot/domain model их не содержит. B3-03 не изобретает новый контракт ranges.

## Concurrency

Existing optimistic Inspection.Version / item Version остаются authority.

Для специализированного conflict UX `WorkspaceComponent` только экспонирует bool `LastWriteWasConflict`, выставляемый уже существующим catch `DbUpdateConcurrencyException`. Новая concurrency model не создаётся.

## Tests as code

`ReleasePackageB303Tests`:
- invalid Choice / Boolean / Number / Percentage rejected server-side;
- valid snapshot values (включая decimal comma) accepted and normalized;
- stale Inspection.Version cannot overwrite newer server draft.

Existing Phase5 browser scenario дополнен media-upload-during-unsaved-draft regression.

## Вне scope

- auto merge local/server drafts;
- background/offline media queue;
- schema/migration;
- numeric min/max ranges not represented in snapshot;
- B3-04 execution.

## Проверки

По решению владельца restore/build/PostgreSQL/browser: **Not run**.
