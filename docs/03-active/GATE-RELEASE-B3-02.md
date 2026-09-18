# GATE-RELEASE-B3-02 — File limits & safe upload errors

**Package:** Release Package 03 — Reliable Files & Field Inspection  
**Findings:** LR-11 + LR-20  
**Branch:** `codex/release-package-03`  
**Base:** `5507a9e14a190a949fb9c683f6f4cec0aa1c31fd`

## Проблема

До B3-02 разные слои фактически обещали один и тот же лимит 8 MiB, но HTTP transport был ограничен теми же 8 MiB.

`byte[]` в JSON передаётся base64: 8 MiB raw превращаются примерно в 10.67 MiB ещё до остальных JSON полей. Поэтому API мог отклонить корректный по application/storage правилам файл раньше model binding.

Отдельно SiteInspectionPage читал stream через `OpenReadStream(8 MiB)` до входа в общий `ExecuteConfirmedAsync`. Oversize IOException мог обходить нормальный пользовательский validation/error flow.

## Решение

### Единый контракт

`FileUploadLimits` в Application.Foundation.Files:
- raw file: **8 MiB**;
- JSON request envelope: **12 MiB**;
- единое safe validation message.

8 MiB сохраняется как пользовательский лимит, потому что существующий storage provider уже рассчитан на него. 12 MiB даёт запас для base64 + JSON metadata.

### Server / API

- Kestrel MaxRequestBodySize использует 12 MiB envelope;
- AddAttachment и RetryAttachment используют общий 8 MiB raw validation;
- raw oversize получает `FileUploadLimitException`;
- SafeExceptionHandler отдаёт HTTP 413 + code `FILE_TOO_LARGE` без provider/internal details.

### Storage

Yandex default/cap использует тот же общий raw maximum. Provider по-прежнему может быть настроен строже для tests/локальной политики, но не выше application maximum.

### UI

CaseWorkspace и SiteInspectionPage:
- показывают общий 8 MiB limit;
- предупреждают при выборе слишком большого файла;
- не запускают upload button для явно oversize selection;
- чтение browser stream выполняется внутри `ExecuteConfirmedAsync`;
- oversize становится обычной validation error, а не необработанным stream exception.

Это закрывает LR-11 без изменения inspection draft/version semantics — они остаются B3-03.

## Tests as code

`ReleasePackageB302Tests`:
1. сериализует реальный `AddCaseAttachment` и `RetryCaseAttachment` с 8 MiB content и доказывает, что старые 8 MiB request limit недостаточны, а 12 MiB envelope достаточен;
2. 8 MiB + 1 byte отклоняется до provider write и до создания attachment metadata;
3. Yandex default использует общий raw max.

## Вне scope

- LR-06 unsaved inspection draft;
- LR-07 inspection conflict/version UX;
- LR-12 answer semantic validation;
- streaming/multipart redesign;
- chunked upload;
- увеличение raw file limit;
- migration/schema.

## Проверки

По решению владельца:
- restore: **Not run**;
- build: **Not run**;
- tests: **Not run**;
- browser/manual oversize: **Not run**.

Наличие test code не считается Passed.
