# NOV23-MAIN-SYNC — единая актуальная версия main

23.11.2026 владелец прямо разрешил commit/push накопленных нужных изменений,
исключение служебных материалов и синхронизацию локального checkout с GitHub main.
Разрешение заменяет прежний запрет commit/push в завершённых локальных Gate.

## Required reading

README → START_HERE → ACTIVE_TASK → этот Gate →
`artifacts/main-local-review/REVIEW.md`, `reports/NOV23_MAIN_SYNC_REPORT.md`,
ранее прочитанные Gate/отчёты четырёх групп и FP-004 §§3,9,12–15.
Нового бизнес-поведения в этом Gate нет; неизменившийся комплект не перечитывать.

## Scope и файлы

- Основа — актуальный origin/main, проверенный head c3be8f9.
- Перенести проверенные 134 новых/отличающихся пути из
  `artifacts/incoming-main-conflict-audit/accurate-inventory.json` без пользовательских
  вложений. Группы: Incoming fast actions + A, Parsing retry UI, Inspection draft
  merge, отдельный Parser; соответствующие тесты, спецификации и отчёты.
- Сохранить main-only документы публикации 19.11 и все функции main. Не добавлять
  повторные изменения уже эквивалентных файлов или правки только BOM.
- `.gitignore`: исключить `.codex-remote-attachments`, сохранив файлы на диске.
- `ACTIVE_TASK.md`: один текущий указатель; прежнюю историю и дополнительные
  сведения main сохранить в `ACTIVE_TASK_HISTORY_2026-11-23.md` рядом с ним.
- Этот Gate и `reports/NOV23_MAIN_SYNC_REPORT.md` фиксируют состав и проверки.
- Подготовка в codex/nov23-main-sync от origin/main; один обычный commit и
  fast-forward push в main без force. После публикации переключить основной
  checkout на main и проверить upstream/ahead/behind/status.

Новых функциональных правок, packages, migrations и схемы нет. Production,
развёртывание, работающая БД/preview, обновление установленного Parser и live
источники не входят в scope. B и следующие бизнес-Gate не активируются.
История не переписывается, ветки и пользовательские файлы не удаляются.

## Проверки и завершение

Повторно проверить remote head и сохранность исходного снимка. Подтвердить хешами,
что исходники/tests совпадают с уже проверенной main-based копией: full Release
0 warnings/errors, 38 серверных (5 browser), 285 Parser offline, 8+36 JS.
При неизменном коде эти проверки повторно не запускать. Проверить staged scope,
diff --check, отсутствие секретов/служебных файлов, .gitignore и main-only документы.
Перед push проверить родителя commit и отсутствие конкурентного изменения main;
после — совпадение GitHub main, origin/main и локального main, чистый status.

Отчёт — решения, состав, фактические проверки, ограничения; окончательный SHA и
подтверждение синхронизации — `artifacts/nov23-main-sync/verification.json` и итоговый
ответ владельцу. После успешной проверки Gate завершён; следующая реализация
требует отдельного запроса. Сам файл не содержит собственный будущий commit SHA.
