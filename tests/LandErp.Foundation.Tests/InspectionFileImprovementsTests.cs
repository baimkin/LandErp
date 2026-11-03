using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Server.Components.Procurement;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class InspectionFileImprovementsTests
{
    private static readonly string[] BatchNames = ["one.jpg", "two.jpg", "three.jpg"];
    private static readonly string[] RetryNames = ["two.jpg"];
    [TestMethod]
    public async Task BatchIsSequentialKeepsSuccessfulFilesAndReusesFailedFileIdentity()
    {
        CaseUploadBatch batch = new();
        batch.Select([new BrowserFile("one.jpg"), new BrowserFile("two.jpg"), new BrowserFile("three.jpg")]);
        Guid failedId = batch.Items[1].UploadId;
        List<string> calls = [];
        int active = 0;
        await batch.RunAsync(async item =>
        {
            Assert.AreEqual(1, ++active, "Only one file may be in memory/transfer at a time.");
            await Task.Yield();
            calls.Add(item.File.Name);
            active--;
            if (item.UploadId == failedId) throw new IOException("Synthetic lost response");
        });
        CollectionAssert.AreEqual(BatchNames, calls);
        Assert.AreEqual(2, batch.Items.Count(item => item.Complete));
        Assert.IsNotNull(batch.Items[1].Error);
        calls.Clear();
        await batch.RunAsync(item =>
        {
            Assert.AreEqual(failedId, item.UploadId);
            calls.Add(item.File.Name);
            return Task.CompletedTask;
        });
        CollectionAssert.AreEqual(RetryNames, calls);
        Assert.IsTrue(batch.Items.All(item => item.Complete));
    }

    [TestMethod]
    public async Task BatchLimitsRejectOversizeButKeepOtherFilesAndPreventChangingStartedBatch()
    {
        CaseUploadBatch batch = new();
        Assert.ThrowsExactly<ArgumentException>(() => batch.Select(Enumerable.Range(0, 21).Select(_ => (IBrowserFile)new BrowserFile("x")).ToArray()));
        batch.Select([new BrowserFile("large.mp4", (long)FileUploadLimits.MaxRawFileBytes + 1), new BrowserFile("valid.jpg")]);
        int uploaded = 0;
        await batch.RunAsync(_ => { uploaded++; return Task.CompletedTask; });
        Assert.AreEqual(1, uploaded);
        Assert.AreEqual(FileUploadLimits.TooLargeMessage, batch.Items[0].Error);
        Assert.ThrowsExactly<InvalidOperationException>(() => batch.Select([new BrowserFile("replacement")]));
        FileUploadLimits.EnsureRawFileSize(FileUploadLimits.MaxRawFileBytes);
    }

    [TestMethod]
    public void DeletedTimelineLinkUsesStableIdentityAndTenantCaseBoundary()
    {
        var file = new CaseAttachment { Id = Guid.NewGuid(), OrganizationId = Guid.NewGuid(), PropertyCaseId = Guid.NewGuid(), Label = "Same label" };
        var entry = new BusinessTimelineEntry { Id = file.Id, OrganizationId = file.OrganizationId, ObjectId = file.PropertyCaseId, ObjectType = "PropertyCase", Kind = "AttachmentDeleted" };
        Assert.AreEqual(file.Id, CaseTimelineAttachment.Find(entry, [file]));
        entry.ObjectId = Guid.NewGuid();
        Assert.IsNull(CaseTimelineAttachment.Find(entry, [file]));
        entry.ObjectId = file.PropertyCaseId;
        entry.OrganizationId = Guid.NewGuid();
        Assert.IsNull(CaseTimelineAttachment.Find(entry, [file]));
    }

    [TestMethod, TestCategory("PostgreSQL")]
    public async Task SoftDeletePreservesBytesHistoryAndChecklistAndConcurrentRepeatIsIdempotent()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(await fixture.CreateUnlinkedManualAsync()), "take", CancellationToken.None);
        var storage = new CountingStorage(fixture.FileStorage);
        var workspace = new ProcurementWorkspace(fixture.Factory, TimeProvider.System, storage);
        var card = await workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        var requirement = card.DocumentRequirements[0];
        byte[] content = "Original document must remain intact"u8.ToArray();
        async Task<Guid> Add(string name) => await workspace.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document, name, "", name + ".pdf", "application/pdf", content, null, requirement.Id), "add", CancellationToken.None);
        Guid first = await Add("First");
        Guid second = await Add("Second");
        card = await workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        requirement = card.DocumentRequirements.Single(item => item.Id == requirement.Id);
        await workspace.SaveDocumentRequirementAsync(fixture.Manager,
            new(taken.CaseId, requirement.Id, card.Item.CaseVersion, requirement.Version, CaseDocumentStatus.Verified, null, "Checked"), "verify", CancellationToken.None);
        await workspace.DeleteAttachmentAsync(fixture.Manager, new(taken.CaseId, first), "delete-first", CancellationToken.None);
        card = await workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(CaseDocumentStatus.Verified, card.DocumentRequirements.Single(item => item.Id == requirement.Id).Status);
        await Task.WhenAll(
            workspace.DeleteAttachmentAsync(fixture.Manager, new(taken.CaseId, second), "delete-second", CancellationToken.None),
            workspace.DeleteAttachmentAsync(fixture.Manager, new(taken.CaseId, second), "repeat-second", CancellationToken.None));
        Assert.AreEqual(2, storage.WriteCount);
        Assert.AreEqual(0, storage.ReadCount, "Deletion must not contact storage.");
        card = await workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(0, card.Attachments.Count);
        var missing = card.DocumentRequirements.Single(item => item.Id == requirement.Id);
        Assert.AreEqual(CaseDocumentStatus.Missing, missing.Status);
        Assert.AreEqual(0, missing.AttachmentIds.Count);
        var deleted = card.Timeline.Where(item => item.Kind == "AttachmentDeleted").ToArray();
        Assert.AreEqual(2, deleted.Length);
        Assert.IsTrue(deleted.All(item => item.Actor == "Manager" && item.Attachments.Single().CanDownloadDeleted));
        CollectionAssert.AreEqual(content, (await workspace.ReadAttachmentAsync(fixture.Manager, second, CancellationToken.None)).Content);
        await using var db = await fixture.Factory.CreateDbContextAsync();
        Assert.AreEqual(2, await db.CaseAttachments.CountAsync(item => item.PropertyCaseId == taken.CaseId));
        var files = await db.StoredFiles.Where(item => item.OrganizationId == fixture.OrganizationId).ToArrayAsync();
        Assert.IsTrue(files.All(item => item.Status == StoredFileStatus.Deleted && item.StorageKey != null && item.SizeBytes == content.Length
            && item.Sha256 == Convert.ToHexStringLower(SHA256.HashData(content))));
        Assert.AreEqual(2, await db.AuditEvents.CountAsync(item => item.ObjectId == taken.CaseId && item.Action == "CaseAttachmentDeleted"));
        var queue = new ProcurementQueueV2ReadService(fixture.Factory, TimeProvider.System);
        var detail = await queue.ReadDetailAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(2, detail.Timeline.Count(item => item.Kind == "AttachmentDeleted" && item.Attachments.Single().CanDownloadDeleted));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.RetryAttachmentAsync(fixture.Manager,
            new(taken.CaseId, second, "Second.pdf", "application/pdf", content), "retry-deleted", CancellationToken.None));
    }

    [TestMethod, TestCategory("PostgreSQL")]
    public async Task DeleteAndDeletedDownloadUseExistingWorkScopeWhileActiveReadIsUnchanged()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        var taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(await fixture.CreateUnlinkedManualAsync()), "take", CancellationToken.None);
        var storage = new CountingStorage(fixture.FileStorage);
        var workspace = new ProcurementWorkspace(fixture.Factory, TimeProvider.System, storage);
        byte[] content = "access boundary"u8.ToArray();
        Guid file = await workspace.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document, "Document", "", "d.pdf", "application/pdf", content, null), "add", CancellationToken.None);
        await fixture.SetExplicitAccessAsync(fixture.EmployeeId("manager2-phase1@test.invalid"),
            EmployeeAccessRules.NoAccess with { ProcurementAccess = ProcurementAccessLevel.Read, ProcurementReadScope = AccessScope.Organization });
        CollectionAssert.AreEqual(content, (await workspace.ReadAttachmentAsync(fixture.SecondManager, file, CancellationToken.None)).Content);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.DeleteAttachmentAsync(fixture.SecondManager, new(taken.CaseId, file), "readonly", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.DeleteAttachmentAsync(fixture.ForeignOwner, new(taken.CaseId, file), "foreign", CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.DeleteAttachmentAsync(fixture.Manager, new(Guid.NewGuid(), file), "wrong-case", CancellationToken.None));
        await workspace.DeleteAttachmentAsync(fixture.Manager, new(taken.CaseId, file), "delete", CancellationToken.None);
        int reads = storage.ReadCount;
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadAttachmentAsync(fixture.SecondManager, file, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadAttachmentAsync(fixture.ForeignOwner, file, CancellationToken.None));
        Assert.AreEqual(reads, storage.ReadCount);
        var readOnlyCard = await workspace.ReadCardAsync(fixture.SecondManager, taken.CaseId, CancellationToken.None);
        Assert.IsFalse(readOnlyCard.Timeline.Single(item => item.Kind == "AttachmentDeleted").Attachments.Single().CanDownloadDeleted);
        // A manager with broad read scope but narrow work scope also cannot use the history link.
        await fixture.SetExplicitAccessAsync(fixture.EmployeeId("manager2-phase1@test.invalid"),
            EmployeeAccessRules.NoAccess with { ProcurementAccess = ProcurementAccessLevel.Manager, ProcurementReadScope = AccessScope.Organization });
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadAttachmentAsync(fixture.SecondManager, file, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.DeleteAttachmentAsync(fixture.SecondManager, new(taken.CaseId, file), "narrow", CancellationToken.None));
        CollectionAssert.AreEqual(content, (await workspace.ReadAttachmentAsync(fixture.Head, file, CancellationToken.None)).Content);
    }

    [TestMethod, TestCategory("PostgreSQL")]
    public async Task InspectionBatchLostAcknowledgementRetriesSameFilesAndDeletedMediaDisappearsFromReports()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        var taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(await fixture.CreateUnlinkedManualAsync()), "take", CancellationToken.None);
        Guid inspector = fixture.EmployeeId("manager2-phase1@test.invalid");
        await fixture.SetExplicitAccessAsync(inspector,
            EmployeeAccessRules.NoAccess with { CanPerformInspections = true });
        await fixture.Workspace.AssignInspectionAsync(fixture.Head, new(taken.CaseId, inspector, null, "Inspect", null), "assign", CancellationToken.None);
        var inspection = (await fixture.Workspace.ReadInspectionAsync(fixture.SecondManager, taken.CaseId, CancellationToken.None)).Inspection!;
        await fixture.Workspace.SaveInspectionAsync(fixture.SecondManager, new(taken.CaseId, inspection.Id, inspection.Version, "", "", [], false), "start", CancellationToken.None);
        CaseUploadBatch batch = new();
        batch.Select([new BrowserFile("one.jpg"), new BrowserFile("two.jpg")]);
        bool lost = true;
        async Task Upload(CaseUploadBatch.Item item)
        {
            await fixture.Workspace.AddAttachmentAsync(fixture.SecondManager,
                new(taken.CaseId, CaseAttachmentOwner.Inspection, inspection.Id, CaseAttachmentKind.Photo, item.File.Name, "", item.File.Name, "image/jpeg", [1, 2, 3], null, UploadId: item.UploadId), "batch", CancellationToken.None);
            if (lost) { lost = false; throw new IOException("Synthetic lost acknowledgement"); }
        }
        await batch.RunAsync(Upload);
        Assert.AreEqual(1, batch.Items.Count(item => item.Complete));
        await batch.RunAsync(Upload);
        Assert.IsTrue(batch.Items.All(item => item.Complete));
        Assert.AreEqual(2, await fixture.CountAsync(db => db.CaseAttachments.CountAsync(item => item.PropertyCaseId == taken.CaseId)));
        Guid deleted = batch.Items[0].UploadId;
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.DeleteAttachmentAsync(fixture.SecondManager, new(taken.CaseId, deleted), "inspector", CancellationToken.None));
        await fixture.Workspace.DeleteAttachmentAsync(fixture.Manager, new(taken.CaseId, deleted), "delete", CancellationToken.None);
        Assert.AreEqual(1, (await fixture.Workspace.ReadInspectionAsync(fixture.SecondManager, taken.CaseId, CancellationToken.None)).Attachments.Count);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => fixture.Workspace.ReadAttachmentAsync(fixture.SecondManager, deleted, CancellationToken.None));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, (await fixture.Workspace.ReadAttachmentAsync(fixture.SecondManager, batch.Items[1].UploadId, CancellationToken.None)).Content);
        var queue = new ProcurementQueueV2ReadService(fixture.Factory, TimeProvider.System);
        Assert.AreEqual(1, (await queue.ReadInspectionReportAsync(fixture.Manager, taken.CaseId, CancellationToken.None))!.Attachments.Count);
        Assert.AreEqual(1, (await queue.ReadDetailAsync(fixture.Manager, taken.CaseId, CancellationToken.None)).Inspection.MaterialCount);
    }

    [TestMethod, TestCategory("PostgreSQL")]
    public async Task ThirtyMiBVideoCanBeSavedAndReadWithStableUploadId()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(await fixture.CreateUnlinkedManualAsync()), "take", CancellationToken.None);
        byte[] video = new byte[30 * 1024 * 1024];
        video[0] = 1; video[^1] = 2;
        Guid uploadId = Guid.CreateVersion7();
        var command = new AddCaseAttachment(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Video, "Video", "", "video.mp4", "video/mp4", video, null, UploadId: uploadId);
        Assert.AreEqual(uploadId, await fixture.Workspace.AddAttachmentAsync(fixture.Manager, command, "video", CancellationToken.None));
        Assert.AreEqual(uploadId, await fixture.Workspace.AddAttachmentAsync(fixture.Manager, command, "repeat", CancellationToken.None));
        CollectionAssert.AreEqual(video, (await fixture.Workspace.ReadAttachmentAsync(fixture.Manager, uploadId, CancellationToken.None)).Content);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.CaseAttachments.CountAsync(item => item.PropertyCaseId == taken.CaseId)));
    }

    [TestMethod, TestCategory("PostgreSQL"), TestCategory("Browser"), Timeout(150_000)]
    public async Task ActualFormsUploadMultipleFilesAndThirtyMiBVideoThenDeleteAndDownloadThroughHistory()
    {
        await using var fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        var taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(await fixture.CreateUnlinkedManualAsync()), "take", CancellationToken.None);
        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId,
            EmployeeAccessRules.NoAccess with { ProcurementAccess = ProcurementAccessLevel.Manager,
                ProcurementReadScope = AccessScope.Department, ProcurementWorkScope = AccessScope.Department, CanPerformInspections = true });
        await fixture.Workspace.AssignInspectionAsync(fixture.Head, new(taken.CaseId, fixture.ManagerEmployeeId, null, "Inspect", null), "assign", CancellationToken.None);
        var inspection = (await fixture.Workspace.ReadInspectionAsync(fixture.Manager, taken.CaseId, CancellationToken.None)).Inspection!;
        await fixture.Workspace.SaveInspectionAsync(fixture.Manager, new(taken.CaseId, inspection.Id, inspection.Version, "", "", [], false), "start", CancellationToken.None);
        string root = Path.Combine(FoundationTests.RepositoryRoot(), "artifacts", "inspection-files", "browser", Guid.NewGuid().ToString("N"));
        string? oldRoot = Environment.GetEnvironmentVariable("Storage__Root");
        string? oldProvider = Environment.GetEnvironmentVariable("Storage__Provider");
        string? oldCertificate = Environment.GetEnvironmentVariable("Kestrel__Certificates__Default__Path");
        string? oldCertificatePassword = Environment.GetEnvironmentVariable("Kestrel__Certificates__Default__Password");
        System.Diagnostics.Process server;
        System.Collections.Concurrent.ConcurrentQueue<string> failureTypes = new();
        int port = PostgresTests.FreePort();
        string origin = $"https://127.0.0.1:{port}";
        try
        {
            // Test-only certificate: no development certificate/trust-store or working server changes.
            Directory.CreateDirectory(root);
            using RSA key = RSA.Create(2048);
            CertificateRequest request = new("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            SubjectAlternativeNameBuilder names = new();
            names.AddDnsName("localhost"); names.AddIpAddress(System.Net.IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
            string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            string certificatePath = Path.Combine(root, "test-https.pfx");
            await File.WriteAllBytesAsync(certificatePath, certificate.Export(X509ContentType.Pfx, password));
            Environment.SetEnvironmentVariable("Kestrel__Certificates__Default__Path", certificatePath);
            Environment.SetEnvironmentVariable("Kestrel__Certificates__Default__Password", password);
            Environment.SetEnvironmentVariable("Storage__Root", root);
            Environment.SetEnvironmentVariable("Storage__Provider", "Local");
            server = PostgresTests.StartHost("LandErp.Server", fixture.Sandbox.RuntimeConnection, port, true);
            // Preserve only exception type names; never include private process messages/configuration.
            void CaptureFailure(object sender, System.Diagnostics.DataReceivedEventArgs args)
            {
                foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                    args.Data ?? "", @"(?:[A-Za-z_]\w*\.)+[A-Za-z_]\w*Exception"))
                    failureTypes.Enqueue(match.Value);
            }
            server.OutputDataReceived += CaptureFailure;
            server.ErrorDataReceived += CaptureFailure;
        }
        finally
        {
            Environment.SetEnvironmentVariable("Storage__Root", oldRoot);
            Environment.SetEnvironmentVariable("Storage__Provider", oldProvider);
            Environment.SetEnvironmentVariable("Kestrel__Certificates__Default__Path", oldCertificate);
            Environment.SetEnvironmentVariable("Kestrel__Certificates__Default__Password", oldCertificatePassword);
        }
        using (server)
        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true });
            await using var context = await browser.NewContextAsync(new() { IgnoreHTTPSErrors = true });
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(20_000);
            bool live = false;
            for (int attempt = 0; attempt < 50 && !live; attempt++)
            {
                Assert.IsFalse(server.HasExited, "Isolated test server exited; private output suppressed. Types: " + string.Join(", ", failureTypes.Distinct()));
                try { await page.GotoAsync(origin + "/health/live"); live = true; }
                catch (PlaywrightException) { await Task.Delay(100); }
            }
            Assert.IsTrue(live);
            await page.GotoAsync(origin + "/account/login");
            await page.GetByLabel("Логин", new() { Exact = true }).FillAsync("manager-phase1@test.invalid");
            await page.GetByLabel("Пароль", new() { Exact = true }).FillAsync("Synthetic1!PasswordForTests");
            await page.GetByRole(AriaRole.Button, new() { Name = "Войти", Exact = true }).ClickAsync();
            await page.Locator(".app-shell[data-interactive-ready=true]").WaitForAsync();
            await page.GotoAsync(origin + $"/procurement/{taken.CaseId}?tab=documents");
            await page.GetByRole(AriaRole.Button, new() { Name = "＋ Добавить документ", Exact = true }).ClickAsync();
            var modal = page.Locator("dialog.case-modal[open]");
            byte[] document = "synthetic document"u8.ToArray();
            await modal.Locator("input[type=file][multiple]").SetInputFilesAsync([
                new FilePayload { Name = "first.pdf", MimeType = "application/pdf", Buffer = document },
                new FilePayload { Name = "second.pdf", MimeType = "application/pdf", Buffer = document }]);
            await modal.GetByRole(AriaRole.Button, new() { Name = "Загрузить выбранные", Exact = true }).ClickAsync();
            await modal.GetByText("Все файлы загружены.", new() { Exact = true }).WaitForAsync();
            await modal.GetByRole(AriaRole.Button, new() { Name = "Закрыть", Exact = true }).Last.ClickAsync();
            await page.Locator(".gallery-file", new() { HasText = "first.pdf" }).GetByRole(AriaRole.Button, new() { Name = "Удалить", Exact = true }).ClickAsync();
            await page.Locator("dialog.case-modal[open]").GetByRole(AriaRole.Button, new() { Name = "Удалить", Exact = true }).ClickAsync();
            try { await page.Locator("dialog.case-modal[open]").WaitForAsync(new() { State = WaitForSelectorState.Detached }); }
            catch (TimeoutException) { Assert.Fail("Delete did not complete: " + await page.Locator("dialog.case-modal[open]").InnerTextAsync()); }
            await page.GetByRole(AriaRole.Button, new() { Name = "История", Exact = true }).ClickAsync();
            var download = page.GetByRole(AriaRole.Link, new() { Name = "Скачать удалённый файл: first.pdf ↗", Exact = true }).Last;
            await download.WaitForAsync();
            string? href = await download.GetAttributeAsync("href");
            var response = await context.APIRequest.GetAsync(origin + href);
            Assert.AreEqual(200, response.Status);
            CollectionAssert.AreEqual(document, await response.BodyAsync());

            await page.GotoAsync(origin + $"/procurement/{taken.CaseId}/inspection");
            await page.GetByRole(AriaRole.Button, new() { Name = "＋ Фото / материал", Exact = true }).ClickAsync();
            modal = page.Locator("dialog.case-modal[open]");
            Assert.IsNull(await modal.Locator("input[multiple]").GetAttributeAsync("capture"), "Gallery must not force camera capture.");
            Assert.AreEqual("environment", await modal.Locator("input[capture]").GetAttributeAsync("capture"));
            byte[] photo=Convert.FromBase64String(await page.EvaluateAsync<string>("""
                ()=>{const c=document.createElement('canvas');c.width=960;c.height=600;const g=c.getContext('2d');
                const sky=g.createLinearGradient(0,0,0,600);sky.addColorStop(0,'#b9d6ef');sky.addColorStop(1,'#e9f3df');g.fillStyle=sky;g.fillRect(0,0,960,600);
                g.fillStyle='#83a977';g.beginPath();g.moveTo(0,420);g.lineTo(310,260);g.lineTo(620,370);g.lineTo(960,210);g.lineTo(960,600);g.lineTo(0,600);g.fill();
                g.fillStyle='#fff';g.font='40px sans-serif';g.fillText('Материалы осмотра',50,530);return c.toDataURL('image/png').split(',')[1];}
                """));
            byte[] clip=Convert.FromBase64String(await page.EvaluateAsync<string>("""
                async()=>{const c=document.createElement('canvas');c.width=640;c.height=360;const g=c.getContext('2d');const stream=c.captureStream(12);
                const recorder=new MediaRecorder(stream,{mimeType:'video/webm'}),parts=[];recorder.ondataavailable=e=>parts.push(e.data);
                const done=new Promise(resolve=>recorder.onstop=resolve);let frame=0;
                const draw=()=>{g.fillStyle='#d4e9f4';g.fillRect(0,0,640,360);g.fillStyle='#65965b';g.fillRect(0,180,640,180);g.fillStyle='#fff';g.font='30px sans-serif';g.fillText('Видео осмотра '+frame++,40,120);};draw();
                recorder.start();const timer=setInterval(draw,80);await new Promise(resolve=>setTimeout(resolve,900));recorder.stop();await done;clearInterval(timer);stream.getTracks().forEach(t=>t.stop());
                const bytes=new Uint8Array(await new Blob(parts,{type:'video/webm'}).arrayBuffer());let result='';for(const byte of bytes)result+=String.fromCharCode(byte);return btoa(result);}
                """));
            byte[] video = new byte[30 * 1024 * 1024];
            video[0] = 1; video[^1] = 2;
            await modal.Locator("input[multiple]").SetInputFilesAsync([
                new FilePayload { Name = "inspection.png", MimeType = "image/png", Buffer = photo },
                new FilePayload { Name = "second.png", MimeType = "image/png", Buffer = photo },
                new FilePayload { Name = "walkthrough.webm", MimeType = "video/webm", Buffer = clip },
                new FilePayload { Name = "inspection.mp4", MimeType = "video/mp4", Buffer = video }]);
            await modal.GetByRole(AriaRole.Button, new() { Name = "Загрузить выбранные", Exact = true }).ClickAsync();
            try
            {
                await Assertions.Expect(modal.Locator("li[data-upload-state=pending]")).ToHaveCountAsync(0, new() { Timeout = 60_000 });
                Assert.AreEqual(0, await modal.Locator("li[data-upload-state=failed]").CountAsync(), await modal.InnerTextAsync());
                await modal.GetByText("Все файлы загружены.", new() { Exact = true }).WaitForAsync();
            }
            catch
            {
                await page.ScreenshotAsync(new() { Path = Path.Combine(root, "upload-failure.png") });
                throw;
            }
            await modal.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).Last.ClickAsync();
            var gallery=page.Locator(".inspection-summary .attachment-gallery");
            await Assertions.Expect(gallery.Locator(".media-card")).ToHaveCountAsync(4);
            string evidence=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","media-gallery");Directory.CreateDirectory(evidence);
            await gallery.GetByRole(AriaRole.Button,new(){Name="Открыть: inspection.png",Exact=true}).ClickAsync();
            var viewer=page.Locator("dialog.media-viewer[open]");await viewer.WaitForAsync();
            await page.WaitForFunctionAsync("()=>{const i=document.querySelector('dialog.media-viewer img');return i?.complete&&i.naturalWidth>0;}");
            await viewer.GetByRole(AriaRole.Button,new(){Name="Увеличить фото",Exact=true}).ClickAsync();
            await Assertions.Expect(viewer.GetByRole(AriaRole.Button,new(){Name="Сбросить увеличение",Exact=true})).ToHaveTextAsync("150%");
            await page.ScreenshotAsync(new(){Path=Path.Combine(evidence,"gallery-photo-desktop.png")});
            await page.Keyboard.PressAsync("ArrowRight");await Assertions.Expect(viewer.Locator("header strong")).Not.ToHaveTextAsync("inspection.png");
            await page.Keyboard.PressAsync("Escape");await viewer.WaitForAsync(new(){State=WaitForSelectorState.Detached});
            await gallery.GetByRole(AriaRole.Button,new(){Name="Открыть: walkthrough.webm",Exact=true}).ClickAsync();
            await page.WaitForFunctionAsync("()=>document.querySelector('dialog.media-viewer video')?.readyState>=2");
            await viewer.Locator("video").EvaluateAsync("async v=>{v.muted=true;await v.play();}");
            await page.WaitForFunctionAsync("()=>document.querySelector('dialog.media-viewer video')?.currentTime>0.05");
            await page.ScreenshotAsync(new(){Path=Path.Combine(evidence,"gallery-video-desktop.png")});
            await page.Keyboard.PressAsync("Escape");await viewer.WaitForAsync(new(){State=WaitForSelectorState.Detached});
            await gallery.GetByRole(AriaRole.Button,new(){Name="Открыть: inspection.mp4",Exact=true}).ClickAsync();
            await viewer.GetByText("Не удалось открыть материал",new(){Exact=true}).WaitForAsync();
            await viewer.GetByRole(AriaRole.Link,new(){Name="Скачать оригинал",Exact=true}).WaitForAsync();
            await viewer.GetByRole(AriaRole.Button,new(){Name="Закрыть просмотр",Exact=true}).ClickAsync();
            Guid failed;
            await using(var db=fixture.Sandbox.Context())
            {
                var record=await (from link in db.CaseAttachments join file in db.StoredFiles on link.StoredFileId equals file.Id where link.PropertyCaseId==taken.CaseId&&file.OriginalName=="second.png" select new{Link=link,File=file}).SingleAsync();
                record.File.Status=StoredFileStatus.UploadFailed;failed=record.Link.Id;await db.SaveChangesAsync();
            }
            await page.ReloadAsync();await page.Locator("[data-inspection-ready=true]").WaitForAsync();
            gallery=page.Locator(".inspection-summary .attachment-gallery");
            await gallery.Locator(".failed-uploads summary").ClickAsync();
            await page.WaitForFunctionAsync("()=>Array.from(document.querySelectorAll('video[data-preview]')).some(v=>v.readyState>=2&&v.currentTime>0.05)");
            await gallery.ScreenshotAsync(new(){Path=Path.Combine(evidence,"gallery-grid-desktop.png")});
            await gallery.GetByRole(AriaRole.Button,new(){Name="Повторить загрузку",Exact=true}).ClickAsync();
            modal=page.Locator("dialog.case-modal[open]");
            await modal.Locator("input[type=file]").SetInputFilesAsync(new FilePayload{Name="second.png",MimeType="image/png",Buffer=photo});
            await modal.GetByRole(AriaRole.Button,new(){Name="Загрузить выбранные",Exact=true}).ClickAsync();
            try{await modal.GetByText("Все файлы загружены.",new(){Exact=true}).WaitForAsync();}
            catch{await page.ScreenshotAsync(new(){Path=Path.Combine(evidence,"retry-failure.png")});Assert.Fail("Retry UI: "+await modal.InnerTextAsync());}
            await modal.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            await Assertions.Expect(gallery.Locator(".failed-uploads")).ToHaveCountAsync(0);
            await using(var db=fixture.Sandbox.Context())Assert.AreEqual(StoredFileStatus.Available,(await (from link in db.CaseAttachments join file in db.StoredFiles on link.StoredFileId equals file.Id where link.Id==failed select file).SingleAsync()).Status);
            await page.SetViewportSizeAsync(390,844);
            await page.WaitForFunctionAsync("()=>document.querySelector('.sidebar').getBoundingClientRect().right<=1");
            await gallery.ScreenshotAsync(new(){Path=Path.Combine(evidence,"gallery-grid-mobile.png")});
            await gallery.GetByRole(AriaRole.Button,new(){Name="Открыть: inspection.png",Exact=true}).ClickAsync();
            await viewer.WaitForAsync();Assert.IsTrue(await viewer.EvaluateAsync<bool>("d=>{const r=d.getBoundingClientRect();return r.left>=-1&&r.right<=innerWidth+1;}"));
            await page.ScreenshotAsync(new(){Path=Path.Combine(evidence,"gallery-photo-mobile.png")});
            await viewer.GetByRole(AriaRole.Button,new(){Name="Удалить",Exact=true}).ClickAsync();
            await page.Locator("dialog.case-modal[open]").GetByRole(AriaRole.Button,new(){Name="Удалить",Exact=true}).ClickAsync();
            await page.Locator("dialog.case-modal[open]").WaitForAsync(new(){State=WaitForSelectorState.Detached});
            await Assertions.Expect(gallery.Locator(".media-card")).ToHaveCountAsync(3);
            Guid deletedPhoto;
            await using(var db=fixture.Sandbox.Context())
            {
                var record=await (from link in db.CaseAttachments join file in db.StoredFiles on link.StoredFileId equals file.Id where link.PropertyCaseId==taken.CaseId&&file.OriginalName=="inspection.png" select new{Link=link,File=file}).SingleAsync();
                Assert.AreEqual(StoredFileStatus.Deleted,record.File.Status);deletedPhoto=record.Link.Id;
            }
            var preserved=await context.APIRequest.GetAsync(origin+$"/api/procurement/attachments/{deletedPhoto}");
            Assert.AreEqual(200,preserved.Status);CollectionAssert.AreEqual(photo,await preserved.BodyAsync());
            Assert.AreEqual(403,(await context.APIRequest.GetAsync(origin+$"/api/procurement/attachments/{deletedPhoto}/media")).Status);
            Assert.AreEqual(6, await fixture.CountAsync(db => db.CaseAttachments.CountAsync(item => item.PropertyCaseId == taken.CaseId)));
            Assert.AreEqual(6, Directory.GetFiles(root, "*.bin").Length, "Soft deletion must retain the actual file on disk.");
        }
        finally { await PostgresTests.StopAsync(server); }
    }

    private sealed class BrowserFile(string name, long size = 3) : IBrowserFile
    {
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UtcNow;
        public long Size => size;
        public string ContentType => "image/jpeg";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => new MemoryStream([1, 2, 3]);
    }

    private sealed class CountingStorage(IFileStorage inner) : IFileStorage
    {
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        { WriteCount++; return inner.WriteAsync(stableFileId, content, cancellationToken); }
        public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken)
        { ReadCount++; return inner.ReadAsync(storageKey, cancellationToken); }
    }
}
