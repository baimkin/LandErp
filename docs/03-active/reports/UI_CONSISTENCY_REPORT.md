# Отчёт — единый визуальный стандарт LandErp

Дата: 17 сентября 2026 года.
Ветка: `codex/ui-consistency`; база: `main` / `6a17671`.
Статус: реализовано; визуальная приёмка владельцем ещё не выполнена.

## Результат и решения

- Общая rem-шкала типографики: 12/14/15/16/18/28 px при базе 16, веса 400/500/600.
  Системный стек шрифтов, более контрастный вторичный текст. В затронутых CSS
  нет собственных числовых font-size (кроме корневых 100%) и font-weight.
- Размеры контролов/строк и отступы используют общие параметры плотности;
  переключение плотности не уменьшает шрифт.
- Рабочие области используют доступную ширину; сняты отдельные ограничения
  обзора, организации и аудита. Для широких таблиц сохранена минимальная ширина
  и локальная горизонтальная прокрутка. Длинные названия/значения переносятся.
- Меню expanded 248 px / compact 64 px, отдельная кнопка переключения,
  различимые SVG-иконки, title и доступные названия ссылок. Настройки меню и
  плотности независимо сохраняются в localStorage; ошибки хранилища безопасны.
  На узких экранах — выездное меню с подписями независимо от desktop-настройки.
- Входящие и закупка используют общий ориентир ширины правой панели 760 px;
  контент прокручивается, заголовок и действия не сжимаются. На узком экране
  панель занимает ширину окна. Редактор переговоров центрируется: фиксированное
  смещение, оставлявшее слишком мало места на ноутбуках, убрано.
- Перестроены фильтры, действия, KPI и строки аудита для узких экранов; важные
  показатели, сроки и сведения остаются доступны. Отдельный мобильный UX
  закупочных таблиц не создавался.
- Общие UI-kit tokens синхронизированы с runtime; правила будущих страниц
  закреплены в AGENT_UI_INSTRUCTIONS. Бизнес-правила, backend, схема БД,
  зависимости и Collector не изменены.

## Изменённые файлы

- `src/LandErp.Server/wwwroot/css/tokens.css`, `components.css`;
- `src/LandErp.Server/wwwroot/css/incoming-v2-sizing.css`, `incoming-v2-workflow.css`;
- `src/LandErp.Server/wwwroot/css/procurement-v2-sizing.css`, `procurement-v2-polish.css`;
- `src/LandErp.Server/Components/Layout/AppShell.razor`, `AppShell.razor.css`;
- изолированные `Components/Pages/Home.razor.css`, `Collectors.razor.css`,
  `Audit.razor.css`, `IncomingCatalogV2.razor.css`, `ProcurementQueueV2.razor.css`,
  `SiteInspectionPage.razor.css`;
- `src/LandErp.Server/wwwroot/js/ui.js`;
- `scripts/Test-UiPreferences.cjs`;
- `docs/14-ui-kit/ui-kit/tokens.css`, `docs/14-ui-kit/AGENT_UI_INSTRUCTIONS.md`;
- `docs/03-active/ACTIVE_TASK.md`, `GATE-UI-CONSISTENCY.md`, этот отчёт.

## Команды и проверки

Все команды выполнены из корня репозитория с SDK `artifacts/stage1/dotnet/dotnet.exe`.

1. `restore LandErp.slnx --locked-mode` — успешно после разрешения доступа к NuGet.
2. Обычная `build LandErp.slnx -c Release --no-restore` — не завершилась:
   открытый `LandErp.ParserSpike.Desktop` блокировал свои DLL. Приложение не
   останавливали. Это ограничение обычного каталога сборки, не ошибка компиляции.
3. `restore LandErp.slnx --locked-mode --artifacts-path C:/.Projects/LandErp/artifacts/ui-consistency/build`
   — успешно.
4. `build LandErp.slnx -c Release --no-restore --artifacts-path C:/.Projects/LandErp/artifacts/ui-consistency/build`
   — полная сборка успешна, 0 warnings / 0 errors.
5. `test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --artifacts-path C:/.Projects/LandErp/artifacts/ui-consistency/build --filter FullyQualifiedName~LandErp.Foundation.Tests.FoundationTests`
   — 4/4 успешно; точный фильтр исключает браузерные и PostgreSQL-сценарии.
6. `node --test scripts/Test-UiPreferences.cjs` — 5/5: значения по умолчанию,
   независимое сохранение настроек, повреждённые значения, whitelist полей,
   недоступное storage/quota.
7. `node --check src/LandErp.Server/wwwroot/js/ui.js` — успешно.
8. Проверка исходников 13 CSS-файлов: сбалансированные блоки, все используемые
   custom properties определены, типографика вынесена в токены; runtime/UI-kit
   tokens совпадают. Это статическая проверка исходников, не проверка рендеринга.
9. Финальная `build src/LandErp.Server/LandErp.Server.csproj -c Release --no-restore`
   — успешно, 0 warnings / 0 errors; веб-приложение собрано в обычный каталог запуска.
10. `git diff --check` — успешно.

Первые попытки в sandbox не имели доступа к NuGet и существующему build cache;
повторные команды с разрешённым доступом прошли. Пакеты/lock files не изменялись.

## Ограничения и следующий шаг

Браузер не запускался; browser/visual tests не выполнялись по прямому запросу
владельца. Поэтому визуальная корректность на конкретных разрешениях, zoom и
длинных реальных данных ещё не подтверждена. PostgreSQL-тесты не запускались:
данные/контракты/бизнес-логика не изменялись.

Следующий шаг — запустить/перезапустить Server обычным локальным способом и
выполнить ручную приёмку: обзор, входящие и панель, закупка и панель/редактор,
поиски, организация, аудит, осмотр; оба режима меню и плотности; ширины
1280/1440/1920/2560, планшет/телефон, увеличенный масштаб. После проверки —
адресные корректировки по замечаниям владельца. Merge/push не выполнялись.
