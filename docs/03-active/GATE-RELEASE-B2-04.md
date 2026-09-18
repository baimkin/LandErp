# GATE-RELEASE-B2-04 — Package 02 validation handover

**Package:** Release Package 02 — Correctable Business Data  
**Ветка:** `codex/release-package-02`  
**Исходный commit B2-04:** `45c29428a40d79482ee5afd0a5ee029378339460`  
**Режим:** фактические test/build/browser прогоны выполняет владелец самостоятельно.

## Назначение

B2-04 не добавляет бизнес-функцию. Он закрывает статический review B2-01–B2-03 и передаёт владельцу точную матрицу фактической проверки. До её выполнения все executable проверки имеют статус **Not run**.

## Статический review

### LR-13
- correction работает по CaseId для пяти working fields;
- reason обязателен;
- ExpectedCaseVersion проверяется после row lock;
- server-side permission использует существующую dossier policy;
- audit хранит field / before / after / reason;
- timeline хранит читаемое before → after;
- Catalog item, observations и source links этой операцией не изменяются;
- длинные значения сокращаются только в timeline; полный before/after остаётся в audit.

### LR-14
- ExpectedCatalogVersion + ExpectedCaseId + reason обязательны;
- Catalog row блокируется FOR UPDATE;
- старая relation не удаляется, а становится Confirmed=false;
- filtered unique index сохраняет максимум одну confirmed relation;
- relink создаёт новую или реактивирует historical pair;
- обычный TakeToWork(... ExistingCaseId) также реактивирует historical pair;
- unlink возвращает source в Incoming + attention;
- relink оставляет source InWork;
- source facts / observations / DataRevision не переписываются;
- timeline и audit фиксируют correction.

### B2-03
- UI показывает Сейчас → После исправления;
- reason обязателен;
- stale/rejected modal не закрывается и предлагает Обновить данные;
- source targets загружаются только при открытии correction modal;
- CanCorrectSourceLinks — только проекция существующего ManagerDecide, не новая permission.

## P0 boundary

Package 02 сохраняет Catalog и PropertyCase раздельными: working facts принадлежат PropertyCase, external facts — Catalog; ownership источника остаётся через PropertyCaseSourceLink; после unlink PropertyCase допустимо имеет 0 sources; дальнейший Procurement остаётся CaseId-based.

## Что специально не добавлено

- migration;
- новая correction/history table;
- новая permission;
- отдельный corrections screen;
- LR-15 search/paging;
- bulk/merge/dedup;
- отдельный Package 02 CI workflow, который автоматически запустил бы тесты вопреки выбранному режиму;
- новые HTTP endpoints только ради Blazor Server UI.

## Validation matrix владельца

### 1. Restore + Release build

```powershell
$dotnet = "./artifacts/stage1/dotnet/dotnet.exe"
& $dotnet restore tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj --locked-mode
& $dotnet build tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore
```

Статус до запуска: **Not run**.

### 2. PostgreSQL targeted suite

```powershell
& $dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj `
  -c Release --no-build `
  --filter "FullyQualifiedName~ReleasePackageB201Tests|FullyQualifiedName~ReleasePackageB202Tests|FullyQualifiedName~ProcurementPhase4Tests.SourceDiscrepancyRequiresExplicitApplyAndReopenKeepsTheSameCase|FullyQualifiedName~ProcurementTests.ConcurrentTakeToWorkCreatesExactlyOneCaseAndLink|FullyQualifiedName~ProcurementTests.CaseScopesUseResponsibilityAndCatalogRemainsOrganizationShared|FullyQualifiedName~InspectionAcquisitionTests.InspectionSnapshotReconnectMediaAndAcquisitionAreSafeAndTerminal"
```

Нужен LANDERP_TEST_ADMIN_CONNECTION к disposable PostgreSQL 18. Статус до запуска: **Not run**.

### 3. Browser

Запустить `ProcurementPhase4Tests.PropertyCaseDossierBrowserFlowUsesApprovedTabsOnDesktopAndMobile`. B2-03 шаги уже встроены в этот scenario. Статус: **Not run**.

### 4. Manual acceptance

1. Исправить неверную рабочую цену с reason; проверить before/after и timeline.
2. Убедиться, что source price не изменилась.
3. В двух вкладках создать stale correction: вторая должна получить concurrency error и refresh path.
4. Перепривязать неверный source к другому Case; проверить old/new history и одну confirmed relation.
5. Выполнить чистый unlink; source должен вернуться во входящие с attention.
6. Повторно связать source штатным flow; duplicate relation не должна появиться.
7. Проверить отсутствие source-link correction у пользователя без ManagerDecide и server-side rejection.
8. Пройти существующий acquisition flow как regression.

Статус: **Not run**.

## Exit status

Static review B2-04: **Completed**. Реализация B2-01–03: **Published**. Package 02 acceptance: **Pending owner validation**. Не объявлять пакет production-ready до фактического прогона.