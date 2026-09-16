# Stage 1 completion — Phase 5

Дата: 2026-09-16. Ветка: `codex/stage-1-procurement-core`.
Baseline: `f8687d85a1e9d57c78c4cf998c62885211da7be1`.

## Результат

Phase 5 `Site Inspection + purchase completion to Acquired` реализована до
production/business exit gate. Существующий case-centric procurement flow сохранён:
осмотр и покупка принадлежат `PropertyCase`, отдельная вкладка/модуль сделки и
`LandAsset` не создавались. Phase 6 и post-purchase scope не начинались.

Мобильный осмотр создаётся из server-configurable versioned шаблона из 20 пунктов.
Экземпляр сохраняет snapshot названия, порядка, типа ответа, вариантов, единицы,
нормального ответа, разрешения материалов и обязательности. Поддержаны состояния
ответа, заметка, общий вывод, предварительное решение, прогресс, завершение и
read-only результат в карточке. Уже начатый осмотр имеет локальный draft answers,
notes и итогов; reconnect-save защищён версиями осмотра и пунктов, конфликт не
перезаписывает данные молча.

Ownership вложений расширен до Inspection/InspectionItem. Фото, video, audio и
документы используют существующие `StoredFile`, `CaseAttachment` и `IFileStorage`.
Ошибка загрузки оставляет стабильную ссылку со статусом `UploadFailed`; повторная
загрузка переводит тот же attachment в `Available`.

Команда `Отметить как куплено` сохраняет фактическую цену, дату покупки/регистрации
и необязательный комментарий, переводит Case в конечное состояние `Acquired`,
закрывает active procurement task, создаёт workflow transition, business timeline
и audit. Повтор той же команды идемпотентен, повтор с другими фактами отклоняется.
Купленная карточка остаётся доступной со всей историей и явно показывает цену,
дату и комментарий покупки.

## Доведение findings Phase 4

1. `CaseCheck` сохранён конкретной проверкой Case. `ResponsibleEmployeeId`
   необязателен; обычная правка может сохранить существующего ответственного,
   разные проверки обновляются независимо, фактический автор остаётся в
   timeline/audit. Добавлен редактируемый справочник Quick/Deep с description,
   sort order и active; CaseCheck хранит snapshot, ad-hoc проверка разрешена.
2. К `CaseAttachment` добавлено человеческое описание. UI показывает понятные
   связи с PropertyCase, названием проверки, событием переговоров, осмотром или
   пунктом осмотра без технических OwnerType/GUID.
3. `CaseNegotiation` стал CRM-журналом событий: запись допустима без цены, а
   seller price, buyer offer и agreed price независимы и могут находиться в одном
   разговоре. Старые `price_type`/`amount` удалены без backfill согласно
   pre-production migration policy; source/working/negotiation facts не смешаны.
4. Кадастровый номер карточки читается непосредственно из `PropertyCase`, вне
   зависимости от наличия этого поля у source.

## Миграция

- `20260916092903_Phase5InspectionAcquisition`;
- добавлены acquisition facts, snapshot-поля проверок, descriptions/inspection
  ownership вложений, справочники checks/inspection и экземпляры осмотра;
- из negotiations удалены legacy `price_type` и `amount`, добавлены nullable
  `seller_price`, `buyer_offer`, `agreed_price` без backfill;
- опубликованные старые migrations не переписывались;
- clean chain содержит 12 migrations; `HasPendingModelChanges=false`.

## Verification

| Проверка | Фактический результат |
|---|---|
| Phase 4 corrections + Phase 5 backend/integration targeted suite | green, 6/6, 1 min 21 s |
| Inspection draft/save + stale reconnect conflict | green |
| Inspection/InspectionItem media ownership + failed upload retry | green |
| Completion + Acquired terminal/idempotent + task/timeline/audit/history | green |
| Negotiation without price + three optional prices in one event | green |
| Clean PostgreSQL chain, rollback `0`, reapply, comments/runtime isolation/recovery | green, 1/1, 8 s |
| `Database.HasPendingModelChanges()` | `false` |
| Release compilation затронутого dependency graph | green, 0 warnings / 0 errors |
| `git diff --check` | green |

Автоматический browser scenario по прямому решению владельца исключён из gate и
после остановки debug-loop не запускался. Он сохранён как будущий тестовый код.
Итоговый UI предназначен для ручного owner smoke-test.

Среда имела системный SDK 10.0.100 при закреплённом в `global.json` 10.0.112;
фактические проверки выполнены установленным .NET 10 SDK 10.0.100 из нейтральной
рабочей директории. Target framework и Release build не менялись.

## Известные ограничения

- Ручной owner smoke-test мобильного осмотра, краткого offline/reload и состояния
  купленной карточки ещё не выполнен; automated browser test не является gate.
- Offline media bytes не переживают reload: UI показывает материал, ожидающий
  синхронизации, а server retry покрывает уже созданную ссылку `UploadFailed`.
  Полноценный distributed/offline media sync намеренно не строился.
- Production storage provider и production apply migrations не выполнялись.
- Универсальные CRM/workflow/DMS/checklist engines, отдельная сделка, `LandAsset`,
  Stage 2 и Phase 6 не создавались.
- Локальный Parser Agent не изучался и не изменялся.

Phase 5 exit gate выполнен для production/server business path. UI acceptance
оставлен владельцу как явно не блокирующий ручной smoke-test. Phase 6 не начиналась.
