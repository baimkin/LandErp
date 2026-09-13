# TP-006 — Blazor shell и UI foundations

**Статус:** На согласовании  
**Зависимости:** TP-002, TP-005, ADR-006, FP-003  
**Результат:** авторизованный сотрудник видит единый каркас ERP и эталонные состояния UI.

## Входит

- Blazor Web App shell;
- локальный Bootstrap CSS без CDN и Bootstrap JS;
- токены цвета, шрифта, spacing, controls и focus;
- AppShell, PageHeader, Button, FieldMessage, StatusBadge;
- Loading/Empty/Error/Forbidden состояния;
- служебная showcase-страница компонентов только в Development;
- клавиатурный focus и начальная responsive-проверка.

## Эталон токенов

```css
:root {
  --lerp-bg-canvas: #f5f7fa;
  --lerp-bg-surface: #ffffff;
  --lerp-text-primary: #15263c;
  --lerp-text-secondary: #5f7085;
  --lerp-border: #d8e0e8;
  --lerp-accent: #2563b8;
  --lerp-success: #19734a;
  --lerp-warning: #a66500;
  --lerp-danger: #b42318;
  --lerp-control-height: 2.5rem;
  --lerp-table-row: 2.75rem;
  --lerp-table-row-compact: 2.25rem;
  --lerp-font: system-ui, -apple-system, "Segoe UI", Roboto, Arial, sans-serif;
}
```

## Правила

- компоненты не знают доменные статусы;
- цвет дополняется текстом;
- label не заменяется placeholder;
- server permission определяет действие, UI только отражает его;
- сторонняя component library и web-font запрещены;
- TypeScript/npm не добавляются.

## Проверки

- shell работает после входа и не показывает Admin без права;
- компоненты доступны с клавиатуры и имеют visible focus;
- контраст токенов проверен;
- loading/empty/error/forbidden визуально различимы;
- desktop и tablet viewport не ломают основную навигацию;
- production не публикует showcase-route.

## Definition of Done

Первый реальный экран TP-007 может использовать UI foundations без создания
своих кнопок, цветов и состояний.

