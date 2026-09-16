# Stage 1 completion — Phase 7

Дата: 2026-09-16. Ветка: `codex/stage1-phase7-audit`.
Baseline: `ea61e1da7bee8cfd8255ef97e8811efcd4b1a18a`.
Статус: реализация и автоматические проверки завершены, ожидается приёмка владельца.

## Результат

Phase 7 `semantic Audit` реализована поверх существующего append-only хранилища
`foundation.audit_events`. Физическая модель и прошлые migrations не менялись,
event sourcing и универсальная audit-платформа не вводились.

Страница `/audit` перенесена на композицию приложенного владельцем HTML-макета:
сводка, фильтры, быстрые категории, группировка по дням, постраничная история и
правый drawer события. Демонстрационные записи и тексты макета в production не
переносились. Все строки, счётчики и подробности строятся из PostgreSQL.

Добавлен отдельный server-owned audit read model:

- период в `Organization.BusinessTimeZone`, actor, module/category и search;
- стабильная пагинация `RecordedAt DESC, Id DESC`, page size 10–100;
- display name автора и понятное имя/номер объекта;
- явные formatter-семейства Organization, Security, Collection и Procurement;
- семантические изменения `Поле / Было / Стало` без показа неизменившихся полей;
- результат вместо искусственного diff для входа, смены пароля, MFA и постановки job;
- архивные признаки и fallback на безопасный display snapshot;
- нейтральный fallback для неизвестного legacy action;
- статистика за выбранный период;
- CSV по текущим фильтрам без raw action/GUID/JSON.

Raw action, entity, Actor/Object GUID, correlation и JSON не входят в основной
список и CSV. Они подгружаются только после открытия скрытого технического блока
конкретного события. Server повторно проверяет `audit.read`, organization scope и
принадлежность события текущей организации; попытка чтения foreign event получает
отказ.

Добавлены явные состояния loading, forbidden, error, empty, filtered empty и
disabled pagination. Произвольный период открывается отдельным диалогом. Строка
события целиком является доступной кнопкой; выбранное событие получает собственный
семантический drawer.

## Миграция

Migration не создавалась: схема и EF model не изменены. Существующий immutable
audit storage сохранён. Читаемость архивных сущностей обеспечивается текущими
soft-archive данными и уже сохранёнными non-secret именами в payload; неизвестные
старые события используют безопасный fallback.

## Verification

| Проверка | Фактический результат |
|---|---|
| Locked restore Server и target test project | green |
| Release build `LandErp.Server.csproj` | green, 0 warnings / 0 errors |
| Release build `LandErp.Foundation.Tests.csproj` | green, 0 warnings / 0 errors |
| `AuditReadModelTests` | green, 1/1 |
| `IdentityOrganizationTests` | green, 2/2 |
| Paging, search, categories, semantic diff и unknown fallback | green |
| `audit.read`, technical details и tenant isolation | green |
| Filtered CSV без raw action и secrets | green |

Финальный targeted запуск: 3/3 PostgreSQL tests green. Browser automation по
прямому требованию владельца не запускалась. Дополнительные unrelated проверки
«на всякий случай» не выполнялись.

## Известные ограничения

- Search использует ограниченный периодом PostgreSQL substring-поиск по action,
  object type, JSON payload и display name автора. Отдельный full-text index не
  добавлялся без измеренной необходимости.
- Один запрос ограничен периодом не более 367 дней; CSV — не более 10 000 строк.
  Для большего экспорта UI просит сузить фильтры.
- Технические данные доступны тем же organization-level пользователям с
  `audit.read`; отдельное новое permission без решения владельца не вводилось.
- Ручной визуальный smoke-test не выполнялся, так как browser automation запрещена
  запросом владельца. Razor/CSS прошли compile verification.
- Production apply не выполнялся. Phase 8 и локальный Parser Agent не затрагивались.

Следующий шаг — ручная приёмка владельцем страницы `/audit`. После принятия можно
зафиксировать Phase 7 как accepted; Phase 8 требует отдельного прямого запроса.
