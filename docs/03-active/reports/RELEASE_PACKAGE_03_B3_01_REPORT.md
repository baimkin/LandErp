# Release Package 03 — B3-01 report

**Finding:** LR-08 — attachment lifecycle & recovery  
**Branch:** `codex/release-package-03`  
**Base:** `936286ef53cc135bdc5c892d62b41332724cd882`

## Реализация

B3-01 не создаёт новую storage architecture. Исправлен existing attachment lifecycle.

Главное изменение: `PendingUpload` теперь является реальной durable recovery point до единого финального DB commit состояния `Available` + document requirement + audit/timeline.

Это закрывает окно частичного состояния, где provider уже подтвердил файл и StoredFile был сохранён Available, но document requirement/audit ещё могли не сохраниться.

Retry также разделён на:
1. provider write;
2. единый DB commit recovered metadata/business state.

Provider failure по-прежнему фиксируется как `UploadFailed`.

## Recovery UX

Карточка PropertyCase:
- показывает честный status PendingUpload;
- позволяет retry PendingUpload и UploadFailed;
- не предлагает открыть недоступный attachment;
- одинаково обрабатывает документы и inspection media;
- объясняет, что retry продолжает существующий attachment, а не создаёт новый.

## Tests as code

Добавлен `ReleasePackageB301Tests.cs`:
- PendingUpload → retry → same attachment → Available + Received document requirement;
- injected provider failure → one UploadFailed attachment → retry → same attachment Available, без duplicate metadata.

Существующая Yandex lost-ack проверка остаётся частью будущей B3-04 validation.

## Не затронуто

B3-02 file limits, B3-03 inspection draft/conflict/validation, migrations, storage providers и Package 04 не начинались.

## Проверки

Restore/build/PostgreSQL/storage/browser в этой задаче по решению владельца **Not run**.
