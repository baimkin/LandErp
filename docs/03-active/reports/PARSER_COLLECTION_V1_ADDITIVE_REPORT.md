# Отчёт: Parser и additive Collection V1

Дата: 18 сентября 2026 года. Ветка: `codex/parser-additive-contract`.
База: `ee8b5f3`.

## Результат

- Parser передаёт финальные `ReasonCode`, `Warnings` и полный `Coverage`.
- Карта и динамическая выдача завершаются по фактическому концу списка,
  завершённой загрузке и минимум трём стабильным чтениям без новых уникальных
  карточек. `SourceCountHint` не управляет прокруткой.
- Несовпадение подсказки источника не отменяет подтверждённый `Success` и
  передаётся как `COUNT_HINT_MISMATCH`.
- Наблюдения сохраняются локально и ставятся в durable outbox до final при
  `Partial`, `SourceError`, `RateLimited` и `Interrupted`.
- Порция ограничена 25 observations; созданные `ResultId` не меняются при retry.
- `JobId + LeaseId`, partition outbox и `RESULT_SUPERSEDED` сохранены. При смене
  lease выполняется автоматическая сверка, payload не удаляется неоднозначно.
- После принятого final локальная server-work запись очищается, и Parser снова
  входит в auto-claim loop. Локальные группы, расписания и задания не переносятся.
- Факты завершения сохраняются в additive локальной SQLite-таблице и переживают
  перезапуск приложения. Версия локальной схемы и пользовательские данные не
  переписываются.

## Преобразование состояний

| Ситуация Parser | Результат Server | Причина |
|---|---|---|
| Конец подтверждён, загрузка закончена | `Success` | пусто либо `COUNT_HINT_MISMATCH` |
| Завершена последняя разрешённая обычная страница, следующая существует | `LimitReached` | `PAGE_LIMIT_REACHED` |
| Полезные данные есть, но конец или загрузка не подтверждены | `Partial` | `END_NOT_CONFIRMED` / `LOADING_INTERRUPTED` |
| Источник ограничил запросы | `RateLimited` | outcome является причиной |
| Timeout, недоступность, неверный URL/ответ или изменившаяся разметка | `SourceError` | утверждённый соответствующий machine-code |
| Остановка приложения/пользователя | `Interrupted` | `AGENT_INTERRUPTED` |
| Lease истёк или заменён | `Interrupted` при reconciliation | `LEASE_EXPIRED_OR_REPLACED` |
| CAPTCHA или вход в аккаунт | ожидание пользователя | `Captcha` / `AuthenticationRequired` |

## Coverage

`UniqueObserved` считается по уникальной паре source/external ID во всех
сохранённых observations Job. `SourceCountHint` и `ResponseBatches` берутся из
диагностики карты, `CompletedPages` — из подтверждённых страниц журнала,
`RequestedPageLimit` — из server work. `EndReached`, `LoadingCompleted` и
`StableRounds` фиксируются Parser в момент завершения.

## Проверки

- Release build Parser Desktop: успешно, 0 предупреждений и ошибок.
- Целевые unit/contract tests: 25/25 успешно.
- Полный небраузерный Parser suite: 101/101 успешно.
- `git diff --check`: успешно.

Browser/live tests не запускались по прямому условию задачи. Из полного offline
набора исключена старая UI-проверка Windows DPAPI, которой недоступен профиль
пользователя в изолированном тестовом процессе; новая логика от неё не зависит.
