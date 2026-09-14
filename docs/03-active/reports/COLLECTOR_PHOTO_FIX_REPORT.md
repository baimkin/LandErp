# Исправление фото Collector — 2026-09-15

Разрешение владельца: исправить выбор размера фото и восстановить доставку сохранённых объявлений.

AvitoMapData выбирает самый большой размер из словаря одной фотографии.
ServerCoordinator ограничивает contract media до 100 уникальных URL.
ServerOutbox восстанавливает только подтверждённо отклонённые HTTP 400 oversized пакеты:
исходный JSON сохраняется в локальной server_outbox_recovery, исправленный пакет получает
новый ResultId и новые ключи изменённых наблюдений. Неоднозначные доставки не переписываются.
Для старых плоских списков группировка размеров потеряна: сохраняются первые 100 URL.
Повторный отказ следующего старого oversized пакета обрабатывается автоматически.

Проверки:
- dotnet build src/LandErp.ParserSpike.Desktop -c Release --no-restore --output artifacts/stage1/collector-connect:
  0 warnings, 0 errors.
- dotnet test tests/LandErp.ParserSpike.Tests -c Release --no-build --no-restore
  --filter FullyQualifiedName~ParsesFieldsCoordinatesAndSafePolygonWithoutSecrets:
  1/1 passed, проверены выбор одного размера и сохранение бизнес-полей.
- Более широкий AvitoMapTests run прерван: не завершился в ходе проверки.
- Доставка пользовательских данных ещё не проверена; открытый Collector не закрывался.

Выход: artifacts/stage1/collector-connect/LandErp.ParserSpike.Desktop.exe.
Перезапустить Collector, подключить сервер и получить задание: существующая логика
восстановит истёкшую lease и доставит сохранённые результаты без повторного сбора.
localPriority=0 URL mismatch этой правкой не исправлялся.
