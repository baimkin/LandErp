# Release Package 03 — B3-03 report

**Findings:** LR-06 + LR-07 + LR-12  
**Branch:** `codex/release-package-03`  
**Base:** `a500aecf3952c5c307082c1d172d1e60fade4e02`

## Draft integrity

Active local inspection draft теперь переживает обычный CaseCard refresh после media upload. Server read обновляет card/attachments, но не заменяет локальные answers/conclusion/decision.

Если одновременно изменилась Inspection.Version, UI переходит в явный conflict.

## Conflict UX

Статус в шапке больше не говорит «Сохранено», когда есть локальные изменения.

Различаются:
- `Сохранено на сервере`;
- `Локальный черновик`;
- `Синхронизация…`;
- `Конфликт версии`.

При mismatch local draft сохраняется и отображается, save/complete/media блокируются. Продолжить с server version можно только явным discard local draft. Автомерджа нет.

JS listener стал version-aware и не продолжает после server save писать localStorage со старой version.

## Server semantic validation

`SaveInspectionAsync` теперь проверяет answer type по snapshot:
- Boolean;
- Number;
- Percentage 0..100;
- Choice options;
- Text.

Числа нормализуются invariant; русский decimal comma принимается. NotChecked очищает answer.

## Tests as code

Добавлен `ReleasePackageB303Tests.cs`:
- rejected invalid snapshot answers;
- accepted/normalized valid answers;
- stale inspection version does not overwrite newer draft.

Existing Phase5 Playwright scenario дополнен проверкой: несохранённые choice + conclusion → upload photo → значения остаются в editor/local draft.

## Не затронуто

Нет migration, auto-merge, background sync queue или новых range полей. B3-04 не начинался.

## Проверки

Фактические restore/build/PostgreSQL/browser по решению владельца: **Not run**.
