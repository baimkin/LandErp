# Яндекс Диск — отчёт реализации

Дата: 17 сентября 2026 года. Ветка: `codex/yandex-disk-storage`.
База: `6a17671`. Рабочая копия: `artifacts/yandex-storage/worktree`.
Статус: пилотное подключение реализовано; обязательные целевые проверки,
включая реальный Диск, пройдены. В основную рабочую копию не интегрировано.

## Выполнено

- Официальный REST API через HttpClient, без новых зависимостей.
- Доступ только к `app:/LandErp/<environment>`. Папки создаются автоматически
  по organization/case ID и категории Photos/Documents/Audio/Video.
- OAuth уходит только на фиксированный API origin. Ссылки передачи проверяются,
  redirect скачивания проверяется на каждом шаге, токен туда не передаётся.
  Ошибки API/транспортные исключения и подписанные ссылки не раскрываются наружу.
- Metadata остаётся в PostgreSQL, провайдерный ключ содержит идентификатор
  подключения. Чтение проверяет SHA-256 и размер относительно ключа и записи БД.
- Имена содержат file ID + hash + расширение. Повтор той же загрузки проверяет
  уже сохранённый объект; изменённое содержимое получает другой ключ, без overwrite.
- Лимит 8 МБ, ограничение размера HTTP-ответов, timeouts, максимум три попытки
  для безопасных API запросов, ограничение одновременных операций.
- Авторизация остаётся перед обращением к хранилищу. Старые локальные ключи
  читаются из прежнего local root; облачные ошибки не переключаются на local.
- Production не может молча использовать локальный backend. Новый аккаунт
  заказчика подключается в чистой среде отдельными настройками.
- Локальный ввод секрета скрытый, хранение Windows DPAPI CurrentUser в ignored
  local-data; scripts тестирования/запуска восстанавливают переменные секрета
  после завершения. Пользователь самостоятельно ввёл токен; в код/отчёт он не попал.
- Безопасные сообщения о подключении/лимитах/целостности возвращаются через API
  и отображаются общим компонентом рабочего интерфейса.

## Изменённые файлы

- `README.md`, `docs/03-active/ACTIVE_TASK.md`.
- `docs/03-active/GATE-YANDEX-DISK-STORAGE.md`, `YANDEX_DISK_SETUP.md`, этот отчёт.
- `src/LandErp.Application/Foundation/Files/FileStorage.cs`.
- `src/LandErp.Infrastructure/Foundation/Files/{YandexDiskOptions,YandexDiskFileStorage,RoutedFileStorage,FileStorageServices}.cs`.
- `src/LandErp.Infrastructure/Modules/Procurement/{ProcurementServices,ProcurementWorkspace}.cs`.
- `src/LandErp.Server/Foundation/SafeExceptionHandler.cs`.
- `src/LandErp.Server/Components/WorkspaceComponent.cs`.
- `scripts/{Set-YandexDiskConnection,Import-YandexDiskConnection,Test-YandexDiskConnection,Start-Local}.ps1`.
- `tests/LandErp.Foundation.Tests/{YandexDiskStorageTests,YandexDiskAttachmentTests,YandexDiskLiveTests}.cs`.

## Фактические проверки

SDK: `C:/.Projects/LandErp/artifacts/stage1/dotnet/dotnet.exe` (`10.0.112`).
Команды из рабочей копии; `<dotnet>` ниже означает этот SDK.

| Команда / проверка | Результат |
|---|---|
| `<dotnet> restore LandErp.slnx --locked-mode` | Успешно, lock-файлы не изменены |
| `<dotnet> build LandErp.slnx -c Release --no-restore` | Успешно; 0 warnings / 0 errors |
| `<dotnet> test tests/LandErp.Foundation.Tests -c Release --no-build --filter 'TestCategory=FileStorage'` | 9/9 |
| То же с фильтром `FullyQualifiedName~AttachmentsFollowOwningCaseScopeAndHideStorageKey\|FullyQualifiedName~InspectionSnapshotReconnectMediaAndAcquisitionAreSafeAndTerminal` | 2/2, настоящая PostgreSQL, disposable DB |
| То же с фильтром `TestCategory=FileStoragePostgreSQL` | 1/1: контекст папок, lost acknowledgement + DB recovery, отказ до API, authoritative DB hash |
| `./scripts/Test-YandexDiskConnection.ps1 -DotnetPath <dotnet>` | 1/1 на настоящем Диске; документ и PNG, повтор без дубля, чтение, SHA-256 |
| PowerShell parser для всех изменённых scripts | Успешно |
| `git check-ignore` для settings.json/token.dpapi | Оба исключены из Git |
| `git diff --check` | Успешно после удаления лишней пустой строки |

Всего 13 целевых тестов. Первые сборки выявили неоднозначный overload и отсутствующий
using в новом тесте; исправлены до итоговой успешной сборки. Первый запуск live
проверки в sandbox не смог открыть DPAPI пользователя; проверка выполнена успешно
в пользовательском контексте после исправления обработки завершающего перевода
строки в зашифрованном файле. Секреты при диагностике не печатались.

На Диске оставлены два маленьких синтетических файла под
`LandErp/development/Checks/<check-id>/`. Личные файлы не читались и не изменялись;
удаления не выполнялись. Live проверка применялась к adapter напрямую, без
создания карточек в пользовательской БД. Связь с карточкой и права проверены
отдельно на настоящей disposable PostgreSQL с управляемым HTTP backend.

## Ограничения

- Это Gate хранилища для пилота, а не заявление полной production-готовности.
  Backup/restore файлов и БД, AV/quarantine, signature validation, retention и
  фоновая сверка не реализованы; ограничения ADR-005 для чувствительных документов
  сохраняются. Существующий MIME allowlist не считается антивирусной проверкой.
- Сбор фото с сайтов, новые возможности Collector и инвесторская публикация
  не добавлялись. Файл попадает на Диск через существующую загрузку вложения.
- Browser/UI приёмка не выполнялась. Уже запущенный Server основной рабочей
  копии не перезапускался и новую реализацию автоматически не получил.
- Новых migrations нет; пользовательская/production БД не изменялась этим Gate.
- Незавершённые Parser/UI изменения основного checkout сохранены. Merge,
  commit, push и перенос между аккаунтами не выполнялись.
- Отключение аккаунта делает его облачные вложения недоступными; оно не удаляет
  записи метаданных и не превращает их в локальные файлы.

## Следующий рекомендуемый шаг

Ручная приёмка загрузки и открытия вложения в Server этой рабочей копии по
`YANDEX_DISK_SETUP.md`, затем отдельное разрешение на интеграцию с текущими
Parser/UI изменениями. После проверки владелец отключает личное OAuth-приложение.
Production заказчика получает пустую БД, свой аккаунт, приложение и ConnectionId.
