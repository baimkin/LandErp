# Stage 1 completion — Phase 4

Дата: 2026-09-16. Ветка: `codex/stage-1-procurement-core`.
Baseline: `aa661c8cd9fb1171a92972900fd43cd42c1d0eac`.

## Результат

Phase 4 `PropertyCase dossier, workflow, переговоры и проверки` завершена до exit
gate. Каноническая карточка PropertyCase доведена без переписывания существующего
case-centric flow и содержит семь утверждённых вкладок: `Основное`, `Переговоры`,
`Проверки`, `Осмотр`, `Документы`, `История`, `Источники`. Вкладка осмотра в этой
фазе остаётся явно обозначенной read-only границей будущей Phase 5.

Переиспользованы самостоятельный PropertyCase, assignment/task, manager/head
handoff, approvals, source links, lifecycle resume, business timeline, audit и
scope-aware `VisibleCases`. Решение руководителя продолжить работу переводит тот
же Case и тот же task в стадию переговоров и проверок; return, rework, resubmit и
решение не создают новый Case или task и сохраняют историю.

Переговоры хранят независимые типы цены `Ask`, `SellerOffer`, `BuyerOffer`,
`Agreed`, канал/контакт, результат, условия, комментарий, следующий шаг,
фактическое и системное время. `Agreed` не меняет lifecycle на `Acquired`.
Quick/Deep checks получили структурированные статусы, ответственного, срок,
стоимость, результат и защищённый правом руководителя blocker.

Case-owned working facts отделены от source values. Карточка показывает
расхождения, а применение значения источника возможно только отдельной командой;
факт такого применения сохраняется append-only и попадает в timeline/audit.

## Attachment/storage boundary

Добавлены provider-neutral `IFileStorage` и metadata `StoredFile`, стабильная
ссылка `CaseAttachment`, статусы загрузки, SHA-256 и ownership к
Case/Negotiation/Check. Поддержаны фото, документы, видео, аудио и HTTPS-ссылки.
Storage key не входит в staff read model. Чтение вложения повторно проверяет
organization и visibility owning PropertyCase. Локальный filesystem adapter
регистрируется только для `Local`/`Test`/`Development` либо при явном
`Storage:Root`; production без настроенного backend завершается fail-fast.

## Миграция

- `20260915235022_Phase4PropertyCaseDossier`;
- forward-only поверх принятого baseline, опубликованные migrations не изменялись;
- добавлены `foundation.stored_files`, `procurement.negotiations`,
  `procurement.case_checks`, `procurement.case_attachments`,
  `procurement.case_fact_revisions`, их FK/index/check constraints и русские
  schema comments;
- полный clean chain содержит 11 migrations; pending model changes отсутствуют;
- runtime role получает только требуемые права на новые таблицы.

## Verification

| Проверка | Результат |
|---|---|
| Phase 4 PostgreSQL + browser suite | green, 5/5 |
| Типы negotiation и `Agreed != Acquired` | green |
| Return/rework/resubmit, тот же Case/task/history | green |
| Quick/Deep checks и blocker permission | green |
| Attachment owning-case access + tenant negative cases | green |
| Source discrepancy, explicit apply, reopen без duplicate | green |
| Desktop/mobile approved PropertyCase flow в реальном браузере | green |
| Затронутые Phase 1–3 case/scope regression scenarios | green, 3/3 |
| Clean PostgreSQL chain, repeated migrate, rollback `0` и reapply | green, 11 migrations |
| PostgreSQL comments, runtime DDL isolation, backup/restore | green |
| `Database.HasPendingModelChanges()` | `false` |
| Release compilation изменённого dependency graph | green, 0 warnings / 0 errors |
| `git diff --check` | green |

Browser evidence сохранён в ignored `artifacts/stage1/phase4-ui/` и не добавлен
в Git. Расширенный полный test suite не запускался: по указанию владельца выполнены
только проверки, прямо связанные с изменёнными слоями и exit gate Phase 4.

## Ограничения

- Production storage provider и production apply migrations не выполнялись;
  filesystem adapter предназначен только для локального/test контура или явно
  заданного root.
- Неудачная запись файла сохраняет явный `UploadFailed`; автоматический retry,
  inspection-owned attachments и offline media flow относятся к Phase 5.
- Ввод осмотра, purchase completion и состояние `Acquired` не реализованы.
- Универсальная document-management/media platform и generic workflow engine не
  создавались.
- Локальный Parser Agent не изучался и не изменялся.

Phase 4 exit gate выполнен. Phase 5 не активирована и не начиналась.
