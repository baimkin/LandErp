# Локальный редактор C-01

Бесплатные open-source компоненты согласованы владельцем 2026-09-27:
Tiptap core/pm/StarterKit/image/table **3.31.3**, esbuild **0.28.2**.
Прямые зависимости закреплены точными версиями, все транзитивные — package-lock.json.
Проверенные лицензии всех пакетов lockfile — MIT. Pro, trial, CDN, облако,
регистрация и лицензионные ключи не используются.

Обновить локальные assets из этого каталога:

```powershell
npm ci --ignore-scripts --no-fund
npm run build
```

Проверено с Node 24.20.0 / npm 11.19.0. esbuild нужен только разработчику при
пересборке. Приложение использует готовый `wwwroot/js/case-notes.js`; Node и npm
не нужны для его запуска. `ClientAssets` не включается в runtime publish.
`node_modules` не входит в Git. Сборка проверяет MIT-лицензии и собирает
`wwwroot/js/case-notes.LICENSE.txt`, включая license основного esbuild для
его платформенного бинарного пакета. Bundle и notices поставляются вместе.

JS отвечает за редактирование и предупреждения о черновике. Он не является
границей доверия: сервер `CaseNoteDocument` заново строит ограниченный JSON и
безопасный HTML. Изображения — только CaseAttachment ID; загрузка и чтение
выполняются существующими защищёнными методами ProcurementWorkspace.
Документ до 128 КиБ, изображения PNG/JPEG/WebP/GIF до действующих 8 МиБ.
Простые таблицы без объединения ячеек; вычислений нет.

Официальные источники лицензий и поддержки:
- https://tiptap.dev/docs/editor/getting-started/overview
- https://github.com/ueberdosis/tiptap/releases
- https://github.com/evanw/esbuild/blob/main/LICENSE.md
