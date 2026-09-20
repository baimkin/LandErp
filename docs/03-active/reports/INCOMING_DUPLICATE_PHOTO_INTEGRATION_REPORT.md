# Интеграция Incoming KPI, дублей и сравнения фотографий

Дата: 2026-09-20  
Интеграционная ветка: `codex/integrate-incoming-photo-duplicates`  
База: `main` `3062c749e31b692af995a599624e6cb22173bb04`  
Исходный пакет: `codex/incoming-image-fingerprints` `2d51c54`

## Что интегрировано

- новые канонические маршруты `/incoming` и `/procurement` с сохранением aliases;
- KPI новых и обработанных предложений и аудит просмотров;
- сохраняемые кандидаты на дубль и решение менеджера;
- фоновые perceptual hashes фотографий без хранения исходных изображений;
- организация-настраиваемые пороги определения дублей.

## Исправления интеграции

- исправлены объявления индексов в двух migrations;
- устранены ошибки типов связей EF Core и nullable pHash;
- runtime получил минимально необходимые права на новые таблицы, включая удаление только устаревших photo fingerprints;
- исправлена настройка новых PostgreSQL tests и субъект чтения аудита;
- добавлена проверка устойчивости реального pHash при PNG/JPEG-перекодировании.

## Фактические проверки

- `dotnet restore LandErp.slnx --locked-mode` — успешно;
- `dotnet build LandErp.slnx -c Release --no-restore` — успешно, 0 warnings, 0 errors;
- 7 целевых Foundation/PostgreSQL tests — 7/7 успешно.

Проверки применяют migrations к одноразовым PostgreSQL databases. Production не изменялась.
