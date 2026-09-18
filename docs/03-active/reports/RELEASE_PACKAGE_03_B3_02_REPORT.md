# Release Package 03 — B3-02 report

**Findings:** LR-11 + LR-20  
**Branch:** `codex/release-package-03`  
**Base:** `5507a9e14a190a949fb9c683f6f4cec0aa1c31fd`

## Реализация

Добавлен единый `FileUploadLimits`:
- MaxRawFileBytes = 8 MiB;
- MaxJsonRequestBodyBytes = 12 MiB.

Это сохраняет привычный raw limit и устраняет противоречие, при котором 8 MiB raw превращались в ~10.67 MiB base64 JSON и не помещались в прежний 8 MiB Kestrel request body.

Workspace и Yandex options используют общий raw max.

## Safe errors

Добавлен отдельный safe `FileUploadLimitException`.

В Blazor он попадает в существующий ArgumentException validation flow и показывает понятное сообщение.

В HTTP API SafeExceptionHandler возвращает 413 / `FILE_TOO_LARGE`.

## Inspection / ordinary attachment UI

Browser stream теперь читается внутри общего command error boundary. До чтения проверяется `IBrowserFile.Size`.

Это применено:
- SiteInspectionPage media upload;
- обычному AddAttachment;
- recovery RetryAttachment.

LR-06/LR-07 behavior local draft в B3-02 не менялся.

## Tests as code

Добавлен `ReleasePackageB302Tests.cs`:
- actual JSON serialization max raw payload vs request envelope;
- oversize rejected before storage/metadata;
- provider default limit aligned with shared contract.

## Не затронуто

B3-03 inspection draft/conflict/server semantic validation, migration, multipart/chunked upload и Package 04 не начинались.

## Проверки

Restore/build/tests/browser по решению владельца: **Not run**.
