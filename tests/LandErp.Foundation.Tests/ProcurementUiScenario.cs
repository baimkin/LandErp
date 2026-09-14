using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Foundation.Tests;

internal static class ProcurementUiScenario
{
    internal static async Task RunAsync(PostgresSandbox sandbox, Guid listingId)
    {
        int port = PostgresTests.FreePort(); string origin = $"https://127.0.0.1:{port}";
        using var server = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true, ChromiumSandbox = true });
            await using IBrowserContext managerContext = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 1440, Height = 1000 } });
            await using IBrowserContext headContext = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true, ViewportSize = new() { Width = 900, Height = 1000 } });
            IPage manager = await managerContext.NewPageAsync(); IPage head = await headContext.NewPageAsync();
            for (int attempt = 0; attempt < 50; attempt++)
            { Assert.IsFalse(server.HasExited, "HTTPS Server exited; private output suppressed."); try { await manager.GotoAsync(origin + "/health/live"); break; } catch (PlaywrightException) { await Task.Delay(100); } }
            await LoginAsync(manager, origin, "manager-d@test.invalid"); await LoginAsync(head, origin, "head-d@test.invalid");
            Assert.AreEqual(403, (await managerContext.APIRequest.GetAsync(origin + "/api/audit")).Status);
            Assert.AreEqual(400, (await managerContext.APIRequest.PostAsync(origin + "/api/procurement/decisions", new() { DataObject = new { } })).Status);
            await manager.GotoAsync(origin + "/procurement"); await ReadyAsync(manager);
            await manager.GetByText("Данные изменились после решения", new() { Exact = true }).WaitForAsync();
            string images = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "stage1", "ui-d"); Directory.CreateDirectory(images);
            await manager.ScreenshotAsync(new() { Path = Path.Combine(images, "queue-desktop.png"), FullPage = true });
            await manager.GetByRole(AriaRole.Button, new() { Name = "Открыть объект", Exact = true }).ClickAsync();
            await manager.GetByRole(AriaRole.Dialog, new() { Name = "Карточка объекта", Exact = true }).WaitForAsync();
            await DecisionAsync(manager, "В работу");
            await DecisionAsync(manager, "Передать руководителю", "Анализ обновлён", "Руководитель закупки", "Руководитель");
            await head.GotoAsync(origin + $"/procurement/listings/{listingId}"); await ReadyAsync(head);
            await head.GetByRole(AriaRole.Button, new() { Name = "Вернуть менеджеру", Exact = true }).ClickAsync();
            ILocator dialog = await StableDialogAsync(head);
            await dialog.GetByLabel("Причина / результат анализа", new() { Exact = true }).FillAsync("Уточнить подъезд к участку"); await head.WaitForTimeoutAsync(200);
            await dialog.GetByLabel("Что исправить или уточнить", new() { Exact = true }).FillAsync("Получить сведения о дороге"); await head.WaitForTimeoutAsync(200);
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Подтвердить решение", Exact = true }).ClickAsync();
            await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached });
            await head.ScreenshotAsync(new() { Path = Path.Combine(images, "returned-tablet.png"), FullPage = true });
            await manager.GotoAsync(origin + $"/procurement/listings/{listingId}"); await ReadyAsync(manager);
            await manager.GetByText("Получить сведения о дороге", new() { Exact = false }).First.WaitForAsync();
            await manager.SetViewportSizeAsync(390, 900); Assert.IsTrue(await manager.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            await manager.ScreenshotAsync(new() { Path = Path.Combine(images, "case-mobile.png"), FullPage = true });
            await DecisionAsync(manager, "Передать руководителю", "Подъезд подтверждён", "Руководитель закупки", "Руководитель");
            await head.ReloadAsync(); await ReadyAsync(head); await DecisionAsync(head, "Одобрить дальнейшую работу", "Продолжить проверку");
            await head.ReloadAsync(); await ReadyAsync(head); await head.GetByText("Одобрен", new() { Exact = true }).WaitForAsync();
            await manager.GotoAsync(origin + "/procurement"); await ReadyAsync(manager);
            await manager.GetByText("Объектов в очереди нет", new() { Exact = true }).WaitForAsync();
            await manager.ScreenshotAsync(new() { Path = Path.Combine(images, "empty-mobile.png"), FullPage = true });
            await using var db = sandbox.Context(true); Assert.AreEqual("approved", (await db.PropertyCases.SingleAsync()).StageId);
            Assert.IsTrue(await db.BusinessTimeline.AnyAsync(item => item.Body.Contains("Получить сведения о дороге")));
        }
        finally { if (!server.HasExited) { server.Kill(true); await server.WaitForExitAsync(); } }
    }
    private static async Task LoginAsync(IPage page, string origin, string email)
    { await page.GotoAsync(origin + "/account/login"); await page.GetByLabel("Электронная почта", new() { Exact = true }).FillAsync(email); await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests"); await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync(); await page.WaitForURLAsync(origin + "/"); await ReadyAsync(page); }
    private static async Task<ILocator> StableDialogAsync(IPage page) { ILocator latest = page.Locator("dialog.erp-modal.dialog").Last; await latest.WaitForAsync(); string? heading = await latest.GetAttributeAsync("aria-labelledby"); return page.Locator("dialog[aria-labelledby=\"" + heading + "\"]"); }
    private static Task ReadyAsync(IPage page) => page.Locator(".app-shell[data-interactive-ready=true]").WaitForAsync();
    private static async Task DecisionAsync(IPage page, string button, string reason = "Начат анализ", string? targetLabel = null, string? targetName = null)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = button, Exact = true }).ClickAsync(); ILocator dialog = await StableDialogAsync(page);
        await dialog.GetByLabel("Причина / результат анализа", new() { Exact = true }).FillAsync(reason); await page.WaitForTimeoutAsync(200);
        if (targetLabel != null) { await dialog.GetByLabel(targetLabel, new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = targetName }); await page.WaitForTimeoutAsync(200); }
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Подтвердить решение", Exact = true }).ClickAsync();
        await dialog.WaitForAsync(new() { State = WaitForSelectorState.Detached });
    }
}
