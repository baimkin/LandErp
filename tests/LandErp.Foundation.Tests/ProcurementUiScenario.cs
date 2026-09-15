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
            ILocator card = page.Locator("article.queue-card", new() { HasText = "UI Telegram Phase 1" });
            await card.GetByRole(AriaRole.Button, new() { Name = "Взять в работу", Exact = true }).ClickAsync();
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

    private static async Task<int> CountCasesAsync(PostgresSandbox sandbox)
    { await using var db = sandbox.Context(true); return await db.PropertyCases.CountAsync(); }
    private static async Task WaitForLiveAsync(System.Diagnostics.Process server, IPage page, string origin)
    { for (int attempt = 0; attempt < 50; attempt++) { Assert.IsFalse(server.HasExited, "HTTPS Server exited; private output suppressed."); try { await page.GotoAsync(origin + "/health/live"); return; } catch (PlaywrightException) { await Task.Delay(100); } } Assert.Fail("HTTPS Server did not become live."); }
    private static async Task LoginAsync(IPage page, string origin, string email)
    { await page.GotoAsync(origin + "/account/login"); await page.GetByLabel("Электронная почта", new() { Exact = true }).FillAsync(email); await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests"); await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync(); await ReadyAsync(page); Assert.AreEqual(origin + "/", page.Url); }
    private static async Task<ILocator> StableDialogAsync(IPage page)
    { ILocator latest = page.Locator("dialog.erp-modal.dialog").Last; await latest.WaitForAsync(); string? heading = await latest.GetAttributeAsync("aria-labelledby"); return page.Locator("dialog[aria-labelledby=\"" + heading + "\"]"); }
    private static Task ReadyAsync(IPage page) => page.Locator(".app-shell[data-interactive-ready=true]").WaitForAsync();
}
