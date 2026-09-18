# Интеграция B3-03 и B4-01–B4-03

**Дата:** 2026-09-18  
**Ветка:** `codex/integrate-b3-03-b4`  
**База:** `7958f92`

## Результат

- B3-03 встроена в актуальный экран осмотра без возврата старой разметки.
- Локальный черновик переживает загрузку материала; конфликт версии блокирует
  запись до явного выбора серверной версии.
- Сервер проверяет ответы по snapshot осмотра.
- B4-01 сохраняет последнее полезное действие Parser и принимает ранний
  heartbeat с nullable `LastUsefulActionAt`.
- B4-02 добавляет operational health для Server, Database, Worker, Scheduler,
  Parser, backlog, expired leases и storage.
- Проверка storage кешируется на минуту для всего процесса и ограничена пятью
  секундами; Local storage не считается исправным только по наличию диска.
- B4-03 добавляет внешний production config, persistent Data Protection,
  trusted forwarded headers, Windows supervisor/deploy/rollback и backup/restore.
- Backup manifest проверяет БД, config, release metadata и Data Protection keys;
  восстановленные секреты получают закрытый ACL.

## Исправленные дефекты исходной ветки

- добавлен пропущенный namespace в тест B3-03;
- исправлена nullable-проверка heartbeat, которая ошибочно отвергала `null`;
- добавлен пропущенный configuration namespace Worker;
- исправлены ложноположительная Local storage health и долгий UI probe;
- усилена целостность и приватность application-файлов backup/restore.

## Фактические проверки

- `dotnet build LandErp.slnx -c Release --no-restore --disable-build-servers` —
  успешно, 0 warnings, 0 errors.
- B3-03, B4-01 и B4-02 targeted suite — 6 passed, 0 failed, 0 skipped.
- синтаксический разбор всех `*Production*.ps1` — успешно.

## Не выполнялось

Production Scheduled Tasks, reboot/crash restart, публичный TLS/reverse proxy,
настоящие `pg_dump`/`pg_restore`, production deploy/rollback и чтение attachment
после restore. Это разрушительные или зависящие от целевого production-хоста
проверки B4-04; локальный Server запускается отдельно для ручной UI-приёмки.
