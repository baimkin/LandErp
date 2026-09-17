# Gate — Яндекс Диск: файловое хранилище

Разрешён владельцем 17 сентября 2026 года: реализовать и проверить подключение
личного Диска для разработки. Production заказчика запускается с пустой БД и
собственным аккаунтом; перенос данных между аккаунтами не требуется.

Ветка: `codex/yandex-disk-storage`. Изолированная рабочая копия от `6a17671`;
незавершённые Parser/UI изменения основной рабочей копии не затрагиваются.

## Required reading

- README, START_HERE, ACTIVE_TASK, AGENTS.
- ADR-005 «Хранилище файлов»; ADR-007 (граница Collector).
- FP-004 «Сквозное ERP-ядро и общие бизнес-механизмы».
- Этот Gate; существующий контракт файлов, регистрация и attachment flow.

## Scope

REST API через HttpClient без новых пакетов. App-folder-only OAuth, автоматические
папки по организации/объекту/виду вложения, закрытое чтение через Server,
провайдерные ключи и проверка hash, ограниченные повторы и безопасные ошибки.
Старые локальные файлы доступны по старым ключам при явно настроенном local root.
Настройка секрета Windows DPAPI и opt-in live проверка. Production использует
собственный secret store/environment и уникальный идентификатор подключения.

Не входят: перенос файлов, автоскачивание медиа Collector, массовое удаление,
инвесторская публикация, фоновые jobs, антивирусный движок, production apply,
merge/push. Ограничения классов чувствительных документов ADR-005 сохраняются.

## Файлы и проверки

- Application/Foundation/Files; Infrastructure/Foundation/Files.
- ProcurementServices и attachment операции ProcurementWorkspace.
- Server SafeExceptionHandler; scripts настройки, запуска и тестирования.
- Foundation.Tests: adapter contract/security/recovery, real PostgreSQL access.
- README и отчёт `reports/YANDEX_DISK_STORAGE_REPORT.md`.

Обязательно: locked restore, Release build, offline HTTP contract tests,
PostgreSQL attachment scope/regression tests, git diff --check. Live upload/read
в отдельной тестовой папке при локально предоставленном токене; без токена
отмечается как невыполненный, не подменяется mock evidence. Тестовые файлы
оставляются с явным названием, личные файлы не читаются и не удаляются.

Отчёт: изменённые файлы, решения, команды, результаты, ограничения и следующий шаг.
