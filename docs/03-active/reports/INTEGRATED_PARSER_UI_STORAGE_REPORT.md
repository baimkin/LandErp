# Интеграция Parser, UI и Яндекс Диска

Дата: 17 сентября 2026 года. Ветка: `codex/integrate-parser-ui-storage`.
Статус: объединено, автоматические проверки пройдены, локальный Server запущен
для ручной приёмки. Main не менялся; push не выполнялся.

## Состав и коммиты

- `ce8be09`: сохранены завершённые незакоммиченные Parser Workspace UX,
  UI Consistency и корректировка карточки объекта из общей рабочей копии.
  Списки файлов и исходные проверки — в трёх отчётах этих Gate.
- `e91a1e1`: отдельный коммит проверенного Яндекс Диска; секреты не включены.
- `ad63b8b`: merge Яндекс Диска с Parser/UI. Единственный текстовый конфликт —
  ACTIVE_TASK. Исторические записи сохранены, активной назначена интеграция.
  Конфликтов production-кода не было.
- `3da7dbd`: merge `a2d98ac` — grant runtime для case_document_requirements
  в LocalSetup. Сам LocalSetup с применением schema/grants не запускался.
- Старые остальные feature-ветки уже входят в базу; исключение по ancestry —
  `codex/stage1-phase7-audit`, но `git cherry` показал оба коммита как
  patch-equivalent. Они повторно не объединялись.

Две другие актуальные задачи LandErp были idle при начале интеграции.
Существующие удаления устаревших Parser UI файлов сохранены как часть
завершённого Parser Gate; дополнительные пользовательские файлы не удалялись.

## Дополнительные изменения интеграции

- `GATE-INTEGRATE-PARSER-UI-STORAGE.md`, ACTIVE_TASK, этот отчёт.
- `scripts/Start-Local.ps1` и `Test-YandexDiskConnection.ps1`: необязательный
  `-ArtifactsPath` для запуска именно объединённой проверенной сборки.
- Уже настроенные `settings.json` и DPAPI token скопированы из изолированного
  Yandex worktree в ignored `local-data/yandex-disk` основного checkout.
  Подключение осталось тем же; новый OAuth grant не выдавался.

## Проверки объединённого состояния

SDK `artifacts/stage1/dotnet/dotnet.exe` 10.0.112.
Artifacts path: `C:/.Projects/LandErp/artifacts/integrated-parser-ui-storage/build`.

| Проверка | Результат |
|---|---|
| `restore LandErp.slnx --locked-mode --artifacts-path <path>` | Успешно |
| `build LandErp.slnx -c Release --no-restore --artifacts-path <path>` | Успешно, 0 warnings/errors |
| Offline/WPF Parser tests с прежними пятью browser-исключениями | 89/89 |
| FoundationTests + CollectionPoolTests + FileStorage + FileStoragePostgreSQL + два attachment regression | 21/21 |
| `node --test scripts/Test-UiPreferences.cjs` | 5/5 |
| `node --check src/LandErp.Server/wwwroot/js/ui.js` | Успешно |
| `Test-YandexDiskConnection.ps1 -DotnetPath <SDK> -ArtifactsPath <path>` | 1/1, реальный Disk upload/read/idempotency |
| Parser изменённых PowerShell scripts | Успешно |
| `git diff --check` | Успешно |
| `git check-ignore` настройки/DPAPI token | Оба ignored |
| HTTP `/health/live`, `/health/ready`, `/account/login` | 200 / 200 / 200 |
| SELECT has_table_privilege(runtime, case_document_requirements) | SELECT/INSERT/UPDATE = true |

Всего 116 целевых тестов. PostgreSQL tests создавали собственные disposable
базы. UI browser tests и реальные Avito/Cian не запускались по границам Gate.
DPAPI/WPF и live Disk проверены под пользователем Windows; из sandbox
секреты/loopback были недоступны. Разрешения и шифрование не ослаблялись.

TRX evidence: `artifacts/integrated-parser-ui-storage/tests/parser.trx` и
`foundation.trx`. Live smoke оставил ещё два маленьких синтетических файла в
папке приложения `LandErp/development/Checks`; удаления не выполнялись.

## Запуск

Server запущен с `Start-Local.ps1 -DotnetPath <SDK> -ArtifactsPath <path>`
в фоне на `https://localhost:7240`. Использует прежнюю локальную БД и файл
конфигурации, подключение Диска из local-data. Процесс на момент проверки:
Server PID 8852, launcher 29816; PID являются диагностикой, не постоянными ID.
Данные запуска: `artifacts/integrated-parser-ui-storage/server-process.json`.
Stdout/stderr находятся рядом; токен туда не печатался.

Старого Server в момент запуска не было; чужие процессы не останавливались.
Существующий Parser не закрывался автоматически, чтобы не потерять текущую
работу. Для явного перезапуска Parser используется `Start-Parser.cmd` после
закрытия прежнего окна; он собирает актуальные исходники.

Startup не применял migrations. Проверка прав пользовательской БД была только
SELECT. Работающий Parser может продолжать обычные запросы к Server после его
появления; тесты не создавали задания сбора в пользовательской БД.

## Ограничения и следующий шаг

Ручная проверка владельцем: обновить страницу (Ctrl+F5), проверить меню,
карточку объекта, вкладки, загрузку/открытие фото и документа, Server workspace
Parser. Доступ Parser к управлению поисками по-прежнему требует CanManageSearches;
интеграция не расширяет права автоматически.

Чувствительные документы, backup/restore и другие production-ограничения
Яндекс Диска остаются из исходного отчёта. Main/GitHub не обновлены; после ручной
приёмки их обновление требует отдельного разрешения. Старые ветки не удалены.
