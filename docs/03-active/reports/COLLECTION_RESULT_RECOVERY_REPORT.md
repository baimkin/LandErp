# Collection result recovery — отчёт

Дата: 18 сентября 2026 года. Ветка: `codex/collectors-scheduling-ux`.

## Результат

- Контракт V1 расширен без breaking change: `Partial`, machine reason/warnings и факты `Coverage`.
- Неполный или ошибочный финальный результат сохраняет принятые observations.
- Server назначает ограниченные повторы отдельно от обычного расписания: transient 5/15/45 минут, rate limit 30/90 минут/4 часа, interrupted 2/10/30 минут.
- CAPTCHA, authentication, постоянные ошибки и исчерпанные повторы требуют действия оператора; успешный последующий запуск снимает разрешённое внимание.
- Экран `/collectors` показывает частичный результат, rate limit, причину, предупреждения, полноту и время повтора.
- Добавлена forward migration `20260917211828_CollectionResultRecovery`; всего 20 миграций. Локальная и production БД не изменялись.
- Канонический протокол Server ↔ Parser актуализирован.

## Проверки

- `dotnet build LandErp.slnx -c Release --no-restore` — успешно, 0 warnings, 0 errors.
- Parser contract tests — 5/5.
- Collection PostgreSQL regression tests — 17/17.
- Collector HTTPS integration tests — 2/2.
- Полная цепочка миграций, rollback до `0`, повторное применение и `HasPendingModelChanges() == false` — 1/1.
- Browser/visual tests не запускались по прямому решению владельца.

## Границы

Локальная реализация браузерного обхода Parser не менялась. Она должна отдельно начать передавать новые additive поля результата; старый payload остаётся совместимым.
