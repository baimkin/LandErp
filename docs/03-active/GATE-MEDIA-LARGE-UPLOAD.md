# MEDIA-LARGE-UPLOAD

02.11.2026. Прямое разрешение владельца: сначала проверить прямую загрузку
из браузера на Яндекс Диск; при доказанной работоспособности использовать её,
при доказанной недоступности — потоковую через Server. Лимит 500 МиБ на файл.
Ветка: текущая `codex/inspection-file-improvements`; предыдущие изменения сохранить.

## Scope

- Реальный браузерный CORS/PUT probe на отдельном синтетическом файле в Checks,
  без пользовательских данных и публикации файлов. Токен/подписанные URL не
  выводить и не сохранять в evidence. Тестовый файл физически не удалять.
- Выбор пути только по evidence. Отсутствие доступа к токену или сети не является
  доказательством, что прямая загрузка не поддерживается.
- После выбора: до 500 МиБ, ограниченная память, прогресс и отдельное подтверждение
  хранилища, safe diagnostic Code/Retryable, прежние права и повтор без дубля.
- Без сжатия, галереи, новых пакетов/ролей/миграций, commit/push и развёртывания production ERP. Разрешён только маленький synthetic Checks probe в настроенном хранилище.

## Required reading

README → START_HERE → ACTIVE_TASK → этот Gate; FP-004 §§3, 9, 14–15;
ADR-005; YANDEX_DISK_SETUP; прежний GATE-INSPECTION-FILE-IMPROVEMENTS;
AGENT_UI_INSTRUCTIONS, GATE-UI-CONSISTENCY и UI tokens; affected file contracts,
Yandex/local/routed adapters, ProcurementWorkspace/Attachments/Endpoints,
CaseFileUpload/CaseUploadBatch/CaseWorkspace/SiteInspectionPage и tests.

## Файлы

ACTIVE_TASK, этот Gate, YANDEX_DISK_SETUP, итоговый отчёт;
scripts/Test-YandexDiskDirectUpload.ps1, tools/LandErp.StorageProbe (Microsoft.Playwright 1.62.0 уже используется тестами, без новой версии/библиотеки); FileStorage/ProcurementContracts;
YandexDiskFileStorage, FileSystemFileStorage, RoutedFileStorage, ProcurementWorkspace;
WorkspaceComponent, ProcurementEndpoints, CaseFileUpload/CaseUploadBatch,
CaseWorkspace/SiteInspectionPage; адресные storage/attachment/browser tests.
Конкретные изменения транспортного пути зависят от probe.

## Проверки

Реальный browser preflight/PUT с безопасным evidence; затем Release build,
граница 500 МиБ и превышение, bounded-memory transfer, отмена/повтор, hash/size,
tenant/case/inspection rights, мягкое удаление, существующие адресные регрессии.
Браузер разрешён текущим прямым запросом для probe и проверки выбранной загрузки.
Production и пользовательские вложения не изменяются.

## Отчёт

reports/MEDIA_LARGE_UPLOAD_REPORT.md: файлы, выбор пути и evidence, команды,
результаты, ограничения и следующий рекомендуемый шаг. Следующий Gate не начинать.
## Выбор пути — direct подтверждён 02.11.2026

После разрешённого системного запуска: локальная браузерная самопроверка 2/2 Passed
(реальные local origins, без отключения браузерной защиты). Live на настроенном
development-подключении: origin https://land.easy-sell.pro, preflight 200,
Browser PUT 201, progress и provider size/SHA-256 подтверждены.
Evidence: artifacts/media-large-upload/direct-live-result.json.
Это доказательство механизма, не приёмка рабочего токена/CSP на production.

Реализация: Begin/Complete через текущую авторизацию, browser hashing в worker
ограниченными чанками, прямой XHR PUT с прогрессом и отменой, SHA-256/size до
Available, повтор/конкурентность без дубля, лимит 500 МиБ только нового пути.
Существующие JSON/buffered операции сохраняют лимит 50 МиБ. Локальный dev backend
использует прежний путь до 50 МиБ; production — настроенный Yandex direct.
Дополнительные файлы: ProcurementDirectUploads.cs, YandexDiskDirectUploads.cs,
wwwroot/js/sha256.mjs, file-hash-worker.js, case-upload.js; direct tests и JS tests.
## Завершение реализации

02.11.2026 после возобновления владельцем: direct код завершён, Release build
0 warnings/errors. Core coverage: 39 Passed + исправленный component 1/1 Passed;
реальный Edge 2/2 Passed (500 МиБ/междоменные PUT/прогресс/отмена/oversize и legacy
осмотр/soft delete/history download). JS SHA-256 — 46 Passed. В пример production
конфигурации добавлен MaxFileBytes=524288000; существующий private config меняет
владелец. Новых migrations нет. Деплой, рабочий токен и устройства пользователей
не проверялись; commit/push не выполнялись. Итоговый отчёт обновлён.
## Публикация исходников — 02.11.2026

Владелец прямо разрешил commit/push в main и запросил скрипт обновления.
Снимок текущего Gate публикуется одним коммитом после проверенного
31c0c88b0a675f021afdfcb4b9c7a3fc2a123be2, fast-forward без force.
Предыдущие локальные изменения Collector/README и его история исключены.
ACTIVE_TASK для публикации сохраняет историю удалённого main, добавляя этот этап.
Локальные ветка/индекс сохраняются; рабочий сервер агентом не обновляется.
Добавлен scripts/Update-ProductionMedia.ps1: pin reviewed SHA, clean tracked tree,
ff-only main, versioned publish, private config backup, MaxFileBytes=524288000,
штатный deploy. Секреты/подключение не выводятся и не меняются. Миграций нет.
Синтаксис PowerShell проверен; production-выполнение скрипта выполняет владелец.
Итоговый SHA и совпадение snapshot подтверждаются отдельно в
artifacts/media-large-upload/publication/verification.json.