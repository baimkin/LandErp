# INCOMING-MANAGER-TRANSFER

16.11.2026 прямой запрос: кнопка «Передать менеджеру» во входящих, выбор
сотрудника и назначение его ответственным. Существующие права сохраняются.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
AGENT_UI_INSTRUCTIONS / GATE-UI-CONSISTENCY / tokens, screen01 и его reference,
FP-004 §§3,9,14–15 → IncomingCatalogV2, существующие Catalog/Procurement transfer
команды и адресные Access V1 tests. Документы неизменны, повторное чтение не нужно.

Scope: одна кнопка в открытой карточке входящего без PropertyCase, существующий
список допустимых получателей, явный выбор, понятные loading/empty/error состояния,
существующая TransferToProcurementAsync. Получатель — ответственный за новый
PropertyCase, отправитель — автор передачи. Подтверждённая группа источников
передаётся вместе штатным механизмом. Массовая передача не входит.

Файлы: IncomingCatalogV2.razor; новый IncomingManagerTransferBrowserTests.cs;
screen01, этот Gate, ограниченная запись ACTIVE_TASK и
reports/INCOMING_MANAGER_TRANSFER_REPORT.md. Backend/права/schema не меняются.
Текущая ветка codex/inspection-file-improvements, накопленное сохранить.
Без новых пакетов, production, commit/push и изменений Parser.

Проверки: Release build; существующая проверка IncomingOnlyTransfers из Access V1;
реальный браузер + disposable PostgreSQL: явный выбор, назначение ответственного,
автор истории/аудит, отсутствие дубля и кнопки у переданного/read-only объявления;
ошибка устаревшего выбора получателя и mobile layout.
Отчёт: фактические результаты, ограничения, следующий шаг владельца.

Результат: реализовано локально. Release 0 warnings/errors. Существующая backend
проверка и новая browser/PostgreSQL проверка Passed; 1440/390 px просмотрены.
Выбор, ответственность, автор истории, аудит, права и отсутствие дублей подтверждены.
Отчёт reports/INCOMING_MANAGER_TRANSFER_REPORT.md. Публикация не выполнялась.
