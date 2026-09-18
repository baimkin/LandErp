# GATE-RELEASE-B3-01 — Attachment lifecycle & recovery

**Package:** Release Package 03 — Reliable Files & Field Inspection  
**Finding:** LR-08 — incomplete attachment recovery / PendingUpload  
**Branch:** `codex/release-package-03`  
**Base:** `936286ef53cc135bdc5c892d62b41332724cd882`

## Цель

Если upload оборвался, acknowledgement потерялся или процесс остановился после записи metadata, сотрудник должен видеть однозначное состояние и восстановить **то же вложение**, не создавая blind duplicate через новый AddAttachment.

## Что уже было правильно

Текущая архитектура уже имеет:
- `StoredFileStatus.PendingUpload / Available / UploadFailed`;
- stable `StoredFile.Id`;
- provider write по stable FileId;
- Yandex content-addressed immutable key;
- retry command, принимающий existing AttachmentId;
- lost-ack provider test, где повтор той же загрузки не создаёт второй provider upload.

Поэтому новая storage subsystem и новая таблица не нужны.

## Исправления B3-01

### Durable lifecycle

Для обычного файла:
1. metadata + CaseAttachment сначала фиксируются как `PendingUpload`;
2. выполняется provider write;
3. `Available` + document requirement `Received` + audit/timeline коммитятся одним DB переходом.

Убирается промежуточный DB commit `Available` до document requirement/audit. Если процесс/DB оборвётся после provider success, PostgreSQL остаётся в recoverable `PendingUpload`, а повтор использует тот же StoredFile.Id.

Для external Link provider-step отсутствует, поэтому промежуточный Pending commit не нужен.

### Retry

Provider failure переводит metadata в `UploadFailed`.

После provider success retry:
- StoredFile `Available`;
- связанный document requirement `Received`;
- retry audit

сохраняются одним DB SaveChanges. DB failure после provider success не должен превращать частично сохранённый requirement в Received при failed attachment.

### UI

- `PendingUpload` отображается как «Загрузка не подтверждена», а не как бесконечное «Загружается»;
- retry доступен и для `PendingUpload`, и для `UploadFailed`;
- recovery выполняется через один общий modal независимо от owner attachment;
- modal явно говорит, что продолжает existing attachment и не создаёт новую metadata запись;
- document requirement и inspection media не показывают `Открыть` для недоступного файла;
- статус pending/failed виден рядом с документом/фото.

## Tests as code

`ReleasePackageB301Tests`:
1. искусственно оставленный `PendingUpload` восстанавливается in-place, attachment id не меняется, document requirement становится Received;
2. fault-injection provider падает на первом write; Add оставляет ровно один UploadFailed attachment, retry делает его Available без duplicate StoredFile/CaseAttachment.

Существующий `YandexDiskAttachmentTests` остаётся отдельной lost-ack проверкой content-addressed provider recovery.

## Вне scope

- LR-11 oversize inspection stream;
- LR-20 base64/request-limit contract;
- LR-06 inspection draft overwrite;
- LR-07 inspection version UX;
- LR-12 snapshot semantic validation;
- media transcoding/OCR/background processing;
- новая migration/schema.

## Проверки

По решению владельца:
- restore: **Not run**;
- build: **Not run**;
- PostgreSQL tests: **Not run**;
- storage fault tests: **Not run**;
- browser/manual: **Not run**.

Наличие tests as code не считается Passed.
