using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

internal static class ProcurementUiScenario
{
    internal static async Task RunPhase1Async(PostgresSandbox sandbox, string managerLogin, Guid unlinkedCatalogItemId)
    {
        int port = PostgresTests.FreePort(); string origin = $"https://127.0.0.1:{port}";
        using var server = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true, ChromiumSandbox = true });
            await using IBrowserContext context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
            IPage page = await context.NewPageAsync();
            page.SetDefaultTimeout(10_000);
            await WaitForLiveAsync(server, page, origin);
            await LoginAsync(page, origin, managerLogin);
            Assert.AreEqual(403, await page.EvaluateAsync<int>("async () => (await fetch('/api/audit')).status"));
            Assert.AreEqual(400, await page.EvaluateAsync<int>("async () => (await fetch('/api/procurement/incoming/take-to-work', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}' })).status"));

            await page.GotoAsync(origin + "/incoming"); await ReadyAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "+ Добавить объявление / предложение", Exact = true }).ClickAsync();
            ILocator manual = await StableDialogAsync(page);
            await manual.GetByLabel("Название", new() { Exact = true }).FillAsync("UI Telegram Phase 1");
            await manual.GetByLabel("Локация", new() { Exact = true }).FillAsync("UI Химки");
            await manual.GetByLabel("Цена, ₽", new() { Exact = true }).FillAsync("3100000");
            await manual.GetByLabel("Комментарий / происхождение", new() { Exact = true }).FillAsync("Получено вручную для executable verification");
            await manual.GetByRole(AriaRole.Button, new() { Name = "Сохранить во входящие", Exact = true }).ClickAsync();
            await manual.WaitForAsync(new() { State = WaitForSelectorState.Detached });
            ILocator row = page.Locator("tr", new() { HasText = "UI Telegram Phase 1" });
            await row.GetByRole(AriaRole.Button, new() { Name = "Подробнее", Exact = true }).ClickAsync();
            ILocator drawer = await StableDrawerAsync(page);
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Взять в работу", Exact = true }).ClickAsync();
            ILocator take = await StableDialogAsync(page);
            await take.GetByRole(AriaRole.Button, new() { Name = "Подтвердить", Exact = true }).ClickAsync();
            await page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/procurement/[0-9a-f-]{36}$"));
            await ReadyAsync(page);
            await page.GetByRole(AriaRole.Heading, new() { Name = "UI Telegram Phase 1", Exact = true }).WaitForAsync();
            await page.GetByText("Рабочие факты PropertyCase", new() { Exact = true }).WaitForAsync();
            string canonical = page.Url;

            string images = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "stage1", "phase1-ui");
            Directory.CreateDirectory(images);
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "case-desktop.png"), FullPage = true });
            await page.SetViewportSizeAsync(390, 900);
            Assert.IsTrue(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "case-mobile.png"), FullPage = true });

            int before = await CountCasesAsync(sandbox);
            await page.GotoAsync(origin + $"/procurement/listings/{unlinkedCatalogItemId}"); await ReadyAsync(page);
            await page.GetByText("PropertyCase ещё не создан", new() { Exact = true }).WaitForAsync();
            Assert.AreEqual(before, await CountCasesAsync(sandbox), "Legacy route must not create a PropertyCase.");

            server.Kill(true); await server.WaitForExitAsync();
            using var restarted = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
            try
            {
                await WaitForLiveAsync(restarted, page, origin);
                await page.GotoAsync(canonical);
                await ReadyAsync(page);
                await page.GetByRole(AriaRole.Heading, new() { Name = "UI Telegram Phase 1", Exact = true }).WaitForAsync();
            }
            finally { if (!restarted.HasExited) { restarted.Kill(true); await restarted.WaitForExitAsync(); } }
        }
        finally { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } }
    }

    internal static async Task RunPhase3Async(PostgresSandbox sandbox, string managerLogin)
    {
        int port = PostgresTests.FreePort(); string origin = $"https://127.0.0.1:{port}";
        using var server = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true, ChromiumSandbox = true });
            await using IBrowserContext context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
            IPage page = await context.NewPageAsync(); page.SetDefaultTimeout(10_000);
            await WaitForLiveAsync(server, page, origin); await LoginAsync(page, origin, managerLogin);
            await page.GotoAsync(origin + "/incoming"); await ReadyAsync(page);
            await page.GetByRole(AriaRole.Link, new() { Name = "Входящие", Exact = false }).WaitForAsync();
            await page.GetByRole(AriaRole.Link, new() { Name = "Закупка", Exact = false }).WaitForAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "+ Добавить объявление / предложение", Exact = true }).ClickAsync();
            ILocator manual = await StableDialogAsync(page);
            await manual.GetByLabel("Название", new() { Exact = true }).FillAsync("UI Phase 3 без внешней идентичности");
            await manual.GetByLabel("Локация", new() { Exact = true }).FillAsync("Химки");
            await manual.GetByLabel("Цена, ₽", new() { Exact = true }).FillAsync("3200000");
            await manual.GetByLabel("Площадь, м²", new() { Exact = true }).FillAsync("1000");
            await manual.GetByLabel("Комментарий / происхождение", new() { Exact = true }).FillAsync("Phase 3 browser verification");
            await manual.GetByRole(AriaRole.Button, new() { Name = "Сохранить во входящие", Exact = true }).ClickAsync();
            await manual.WaitForAsync(new() { State = WaitForSelectorState.Detached });
            ILocator row = page.Locator("tr", new() { HasText = "UI Phase 3 без внешней идентичности" });
            await row.GetByText("без внешнего ID", new() { Exact = false }).WaitForAsync();
            await row.GetByRole(AriaRole.Button, new() { Name = "Подробнее", Exact = true }).ClickAsync();
            ILocator drawer = await StableDrawerAsync(page);
            await drawer.GetByLabel("Общая цена ≤, ₽", new() { Exact = true }).FillAsync("2900000");
            await drawer.GetByLabel("Комментарий", new() { Exact = true }).FillAsync("Ждём целевую цену");
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Поставить на мониторинг", Exact = true }).ClickAsync();
            await drawer.GetByText("Мониторинг", new() { Exact = true }).WaitForAsync();
            await drawer.GetByRole(AriaRole.Button, new() { Name = "Фейк", Exact = true }).ClickAsync();
            ILocator classify = await StableDialogAsync(page);
            await classify.GetByLabel("Причина / подтверждение", new() { Exact = true }).FillAsync("Недостоверное предложение");
            await classify.GetByRole(AriaRole.Button, new() { Name = "Подтвердить", Exact = true }).ClickAsync();
            await classify.WaitForAsync(new() { State = WaitForSelectorState.Detached });
            await drawer.Locator("span.badge", new() { HasText = "Фейк" }).WaitForAsync();

            string images = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "stage1", "phase3-ui");
            Directory.CreateDirectory(images);
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "incoming-desktop.png"), FullPage = true });
            await page.SetViewportSizeAsync(390, 900);
            Assert.IsTrue(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "incoming-mobile.png"), FullPage = true });
        }
        finally { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } }
    }

    internal static async Task RunPhase4Async(PostgresSandbox sandbox, string managerLogin, Guid caseId)
    {
        int port = PostgresTests.FreePort(); string origin = $"https://127.0.0.1:{port}";
        using var server = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
        try
        {
            using IPlaywright playwright = await StepResultAsync(Playwright.CreateAsync, "create-playwright");
            await using IBrowser browser = await StepResultAsync(() => playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true, ChromiumSandbox = false }), "launch-browser");
            await using IBrowserContext context = await StepResultAsync(() => browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } }), "create-context");
            IPage page = await StepResultAsync(context.NewPageAsync, "create-page"); page.SetDefaultTimeout(10_000);
            await StepAsync(async () => { await WaitForLiveAsync(server, page, origin); await LoginAsync(page, origin, managerLogin);
                await page.GotoAsync(origin + "/procurement/" + caseId); await ReadyAsync(page);
                await page.GetByRole(AriaRole.Heading, new() { Name = "UI PropertyCase Phase 4", Exact = true }).WaitForAsync(); }, "open-card");
            foreach (string tab in new[] { "Основное", "Переговоры", "Проверки", "Осмотр", "Документы", "История", "Источники" })
                await page.GetByRole(AriaRole.Button, new() { Name = tab, Exact = true }).WaitForAsync();

            await StepAsync(async () => { await page.GetByRole(AriaRole.Button, new() { Name = "Переговоры", Exact = true }).ClickAsync();
                await page.GetByLabel("Цена, ₽", new() { Exact = true }).FillAsync("16600000");
                await page.GetByLabel("Результат", new() { Exact = true }).FillAsync("Продавец готов продолжить обсуждение");
                await page.GetByLabel("Условия", new() { Exact = true }).FillAsync("Документы до следующего контакта");
                await page.GetByLabel("Следующий шаг", new() { Exact = true }).FillAsync("Перезвонить завтра");
                await page.GetByRole(AriaRole.Button, new() { Name = "Добавить контакт", Exact = true }).ClickAsync();
                await page.GetByText("16 600 000 ₽", new() { Exact = false }).First.WaitForAsync(); }, "save-negotiation");

            await StepAsync(async () => { await page.GetByRole(AriaRole.Button, new() { Name = "Проверки", Exact = true }).ClickAsync();
                ILocator checkForm = page.Locator("article.surface", new() { HasText = "Добавить проверку" });
                await checkForm.GetByRole(AriaRole.Heading, new() { Name = "Добавить проверку", Exact = true }).WaitForAsync();
                await checkForm.Locator("select").Nth(1).SelectOptionAsync("Passed");
                await checkForm.Locator("label", new() { HasText = "Название" }).Locator("input").FillAsync("Сверить кадастровую карту");
                await checkForm.Locator("textarea").FillAsync("Границы совпадают");
                await checkForm.GetByRole(AriaRole.Button, new() { Name = "Сохранить проверку", Exact = true }).ClickAsync();
                await page.GetByText("Сверить кадастровую карту", new() { Exact = false }).First.WaitForAsync(); }, "save-check");

            await StepAsync(async () => { await page.GetByRole(AriaRole.Button, new() { Name = "Документы", Exact = true }).ClickAsync();
                ILocator attachmentForm = page.Locator("article.surface", new() { HasText = "Добавить вложение" });
                await attachmentForm.GetByRole(AriaRole.Heading, new() { Name = "Добавить вложение", Exact = true }).WaitForAsync();
                await attachmentForm.Locator("select").Nth(1).SelectOptionAsync("Link");
                await attachmentForm.Locator("label", new() { HasText = "Название" }).Locator("input").FillAsync("Публичная кадастровая карта");
                await attachmentForm.Locator("input[type='url']").FillAsync("https://example.test/cadastre");
                await attachmentForm.GetByRole(AriaRole.Button, new() { Name = "Добавить вложение", Exact = true }).ClickAsync();
                await page.GetByText("Публичная кадастровая карта", new() { Exact = true }).WaitForAsync(); }, "save-attachment");

            string images = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "stage1", "phase4-ui");
            Directory.CreateDirectory(images);
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "property-case-desktop.png"), FullPage = true });
            await page.SetViewportSizeAsync(390, 900);
            Assert.IsTrue(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            await page.GetByRole(AriaRole.Button, new() { Name = "Основное", Exact = true }).ClickAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "property-case-mobile.png"), FullPage = true });
        }
        finally { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } }
    }

    private static async Task<int> CountCasesAsync(PostgresSandbox sandbox)
    { await using var db = sandbox.Context(true); return await db.PropertyCases.CountAsync(); }
    private static async Task WaitForLiveAsync(System.Diagnostics.Process server, IPage page, string origin)
    { for (int attempt = 0; attempt < 50; attempt++) { Assert.IsFalse(server.HasExited, "HTTPS Server exited; private output suppressed."); try { await page.GotoAsync(origin + "/health/live"); return; } catch (PlaywrightException) { await Task.Delay(100); } } Assert.Fail("HTTPS Server did not become live."); }
    private static async Task LoginAsync(IPage page, string origin, string email)
    { await page.GotoAsync(origin + "/account/login"); await page.GetByLabel("Электронная почта", new() { Exact = true }).FillAsync(email); await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests"); await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync(); await ReadyAsync(page); Assert.AreEqual(origin + "/", page.Url); }
    private static async Task<ILocator> StableDialogAsync(IPage page)
    { ILocator latest = page.Locator("dialog.erp-modal.dialog").Last; await latest.WaitForAsync(); string? heading = await latest.GetAttributeAsync("aria-labelledby"); return page.Locator("dialog[aria-labelledby=\"" + heading + "\"]"); }
    private static async Task<ILocator> StableDrawerAsync(IPage page)
    { ILocator latest = page.Locator("dialog.erp-modal.drawer").Last; await latest.WaitForAsync(); string? heading = await latest.GetAttributeAsync("aria-labelledby"); return page.Locator("dialog[aria-labelledby=\"" + heading + "\"]"); }
    private static Task ReadyAsync(IPage page) => page.Locator(".app-shell[data-interactive-ready=true]").WaitForAsync();
    private static async Task StepAsync(Func<Task> action, string name)
    {
        Task work = action();
        if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20))) != work) Assert.Fail("Phase 4 browser step timed out: " + name);
        await work;
    }
    private static async Task<T> StepResultAsync<T>(Func<Task<T>> action, string name)
    {
        Task<T> work = action();
        if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20))) != work) Assert.Fail("Phase 4 browser step timed out: " + name);
        return await work;
    }
}
