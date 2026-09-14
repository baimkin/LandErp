using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class IdentityUiTests
{
    public TestContext TestContext { get; set; } = default!;
    [TestMethod]
    public async Task RealCookieMfaAdminPersistenceCsrfAndResponsiveStates()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using LandErpDbContext db = sandbox.Context();
        await db.Database.MigrateAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        await IdentityOrganizationTests.BootstrapAsync(services, "owner-ui@test.invalid", "UI test organization");
        await sandbox.GrantRuntimeAsync();
        int port = PostgresTests.FreePort();
        string origin = $"https://127.0.0.1:{port}";
        using var server = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
        try
        {
            using IPlaywright playwright = await Playwright.CreateAsync();
            await using IBrowser browser = await playwright.Chromium.LaunchAsync(new() { Channel = "chrome", Headless = true, ChromiumSandbox = true });
            await using IBrowserContext context = await browser.NewContextAsync(new()
            {
                IgnoreHTTPSErrors = true,
                ViewportSize = new() { Width = 1440, Height = 1000 }
            });
            IPage page = await context.NewPageAsync();
            for (int attempt = 0; attempt < 50; attempt++)
            {
                Assert.IsFalse(server.HasExited, "HTTPS Server exited; private output suppressed.");
                try { await page.GotoAsync(origin + "/health/live"); break; }
                catch (PlaywrightException) { await Task.Delay(100); }
            }

            var anonymous = await context.APIRequest.GetAsync(origin + "/api/organization");
            Assert.AreEqual(401, anonymous.Status);
            await page.GotoAsync(origin + "/account/login");
            await page.GetByLabel("Электронная почта", new() { Exact = true }).FillAsync("owner-ui@test.invalid");
            await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync();
            await page.GetByLabel("Ключ настройки MFA", new() { Exact = true }).WaitForAsync();
            Assert.IsTrue(page.Url.EndsWith("/account/mfa", StringComparison.Ordinal));
            string key = await page.GetByLabel("Ключ настройки MFA", new() { Exact = true }).InputValueAsync();
            Assert.AreEqual(401, (await context.APIRequest.GetAsync(origin + "/api/organization")).Status);
            await page.GetByLabel("Код из приложения", new() { Exact = true }).FillAsync(Totp(key));
            await page.GetByRole(AriaRole.Button, new() { Name = "Включить MFA", Exact = true }).ClickAsync();
            await page.GetByText("MFA включена", new() { Exact = true }).WaitForAsync();
            Assert.AreEqual(200, (await context.APIRequest.GetAsync(origin + "/api/organization")).Status);
            var cookies = await context.CookiesAsync();
            Assert.IsTrue(cookies.Where(cookie => cookie.Name.Contains("Application", StringComparison.Ordinal)).All(cookie => cookie.Secure && cookie.HttpOnly));
            IFormData forged = context.APIRequest.CreateFormData();
            forged.Set("_handler", "logout");
            Assert.AreEqual(400, (await context.APIRequest.PostAsync(origin + "/account/logout", new() { Form = forged })).Status);
            await page.GotoAsync(origin + "/organization");
            await page.Locator(".app-shell[data-interactive-ready='true']").WaitForAsync();
            await page.GetByLabel("Название отдела", new() { Exact = true }).FillAsync("Закупка UI");
            await page.GetByRole(AriaRole.Button, new() { Name = "Добавить отдел", Exact = true }).ClickAsync();
            await page.GetByText("Закупка UI", new() { Exact = true }).First.WaitForAsync();
            Assert.IsTrue(await db.OrgUnits.AnyAsync(item => item.Name == "Закупка UI"));
            await page.GetByLabel("Название должности", new() { Exact = true }).FillAsync("Менеджер UI");
            await page.GetByRole(AriaRole.Button, new() { Name = "Добавить должность", Exact = true }).ClickAsync();
            await page.GetByText("Менеджер UI", new() { Exact = true }).WaitForAsync();
            Assert.IsTrue(await db.Positions.AnyAsync(item => item.Name == "Менеджер UI"));
            await page.GotoAsync(origin + "/employees");
            await page.Locator(".app-shell[data-interactive-ready='true']").WaitForAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Сотрудники", Exact = true }).WaitForAsync();
            await page.GetByLabel("Имя сотрудника", new() { Exact = true }).FillAsync("Тестовый закупщик");
            await page.WaitForTimeoutAsync(200);
            await page.GetByLabel("Электронная почта", new() { Exact = true }).FillAsync("invited-ui@test.invalid");
            await page.WaitForTimeoutAsync(200);
            await page.GetByLabel("Подразделение", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Закупка UI" });
            await page.WaitForTimeoutAsync(200);
            await page.GetByLabel("Роль", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "ProcurementManager" });
            await page.GetByRole(AriaRole.Button, new() { Name = "Создать приглашение", Exact = true }).ClickAsync();
            await page.WaitForTimeoutAsync(1000);
            var errors = await page.Locator(".validation-errors, .state-error, .page-state.error, .page-state.forbidden").AllTextContentsAsync();
            Assert.AreEqual(0, errors.Count, "Safe validation messages: " + string.Join("; ", errors));
            await page.GetByText("Приглашение создано", new() { Exact = true }).WaitForAsync();
            Assert.IsTrue(await db.Employees.AnyAsync(item => item.DisplayName == "Тестовый закупщик"));
            string invitationId = await page.GetByLabel("ID приглашения", new() { Exact = true }).InputValueAsync();
            string invitationToken = await page.GetByLabel("Одноразовый код", new() { Exact = true }).InputValueAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Скрыть код" }).ClickAsync();
            string images = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "stage1", "ui-b");
            Directory.CreateDirectory(images);
            await page.ScreenshotAsync(new() { Path = Path.Combine(images, "desktop.png"), FullPage = true });
            foreach (int width in new[] { 900, 390 })
            {
                await page.SetViewportSizeAsync(width, 900);
                Assert.IsTrue(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
                await page.GetByRole(AriaRole.Button, new() { Name = "Открыть меню" }).WaitForAsync();
                await page.ScreenshotAsync(new() { Path = Path.Combine(images, $"width-{width}.png"), FullPage = true });
            }

            await using IBrowserContext employeeContext = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
            IPage employeePage = await employeeContext.NewPageAsync();
            await employeePage.GotoAsync(origin + "/account/activate");
            await employeePage.GetByLabel("ID приглашения", new() { Exact = true }).FillAsync(invitationId);
            await employeePage.GetByLabel("Одноразовый код", new() { Exact = true }).FillAsync(invitationToken);
            await employeePage.GetByLabel("Новый пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await employeePage.GetByLabel("Повторите пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await employeePage.GetByRole(AriaRole.Button, new() { Name = "Активировать" }).ClickAsync();
            await employeePage.GetByText("Учётная запись активирована", new() { Exact = true }).WaitForAsync();
            await employeePage.GotoAsync(origin + "/account/login");
            await employeePage.GetByLabel("Электронная почта", new() { Exact = true }).FillAsync("invited-ui@test.invalid");
            await employeePage.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await employeePage.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync();
            await employeePage.Locator(".app-shell[data-interactive-ready='true']").WaitForAsync();
            Assert.AreEqual(origin + "/", employeePage.Url);
            Assert.AreEqual(200, (await employeeContext.APIRequest.GetAsync(origin + "/api/organization")).Status);
            Assert.AreEqual(403, (await employeeContext.APIRequest.GetAsync(origin + "/api/audit")).Status);
            await employeePage.GotoAsync(origin + "/organization");
            await employeePage.GetByRole(AriaRole.Heading, new() { Name = "Нет доступа", Exact = true }).WaitForAsync();
            await page.GotoAsync(origin + "/collectors");
            await page.Locator(".app-shell[data-interactive-ready='true']").WaitForAsync();
            await page.GetByLabel("Имя Collector", new() { Exact = true }).FillAsync("Collector UI");
            await page.GetByRole(AriaRole.Button, new() { Name = "Создать Collector", Exact = true }).ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = "Collector создан", Exact = true }).WaitForAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Скрыть token", Exact = true }).ClickAsync();
            await page.GetByLabel("Название поиска", new() { Exact = true }).FillAsync("Поиск UI");
            await page.WaitForTimeoutAsync(200);
            await page.GetByLabel("Ссылка поиска", new() { Exact = true }).FillAsync("https://www.avito.ru/moskva/zemelnye_uchastki");
            await page.WaitForTimeoutAsync(200);
            await page.GetByLabel("Исполняющий Collector", new() { Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Collector UI" });
            await page.GetByRole(AriaRole.Button, new() { Name = "Сохранить поиск", Exact = true }).ClickAsync();
            await page.GetByText("Поиск UI", new() { Exact = true }).WaitForAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Поставить сбор", Exact = true }).ClickAsync();
            await page.GetByText("Ожидает", new() { Exact = true }).WaitForAsync();
            Assert.AreEqual(1, await db.CollectionJobs.CountAsync());
            string collectorImages = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "stage1", "ui-c");
            Directory.CreateDirectory(collectorImages);
            await page.EvaluateAsync("window.scrollTo(0,0)");
            await page.ScreenshotAsync(new() { Path = Path.Combine(collectorImages, "admin.png"), FullPage = true });
        }
        finally { await PostgresTests.StopAsync(server); }
    }

    // Test helper only: production MFA verification is entirely ASP.NET Core Identity.
    internal static string Totp(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        List<byte> bytes = []; int buffer = 0; int bits = 0;
        foreach (char character in key)
        {
            buffer = (buffer << 5) | alphabet.IndexOf(character);
            bits += 5;
            if (bits >= 8) { bits -= 8; bytes.Add((byte)(buffer >> bits)); }
        }
        byte[] counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
#pragma warning disable CA5350 // RFC 6238 Identity authenticator interoperability requires HMAC-SHA1.
        byte[] hash = HMACSHA1.HashData(bytes.ToArray(), counter);
#pragma warning restore CA5350
        int offset = hash[^1] & 15;
        int number = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff) % 1000000;
        return number.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
