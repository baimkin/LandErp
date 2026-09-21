using System.Text;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Foundation.Files;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
[TestCategory("FileStoragePostgreSQL")]
public sealed class YandexDiskAttachmentTests
{
    [TestMethod]
    public async Task CaseFoldersRecoveryAuthorizationAndDatabaseHashRemainAuthoritative()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(true, true);
        await fixture.ChangeScopeAsync("manager-phase1@test.invalid", AccessScope.Team, fixture.TeamA);
        await fixture.ChangeScopeAsync("manager2-phase1@test.invalid", AccessScope.Team, fixture.TeamB);
        Guid itemId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(itemId), "storage-take", CancellationToken.None);
        using YandexDiskStorageTests.DiskHandler handler = new() { LoseUploadResponse = true };
        using HttpClient http = new(handler);
        using YandexDiskFileStorage cloud = new(YandexDiskStorageTests.Options(), http);
        ProcurementWorkspace workspace = new(fixture.Factory, TimeProvider.System, cloud);
        byte[] content = Encoding.UTF8.GetBytes("synthetic project document");
        await Assert.ThrowsExactlyAsync<FileStorageException>(() => workspace.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document, "Проверка хранилища", "",
                "test.txt", "text/plain", content, null), "storage-upload", CancellationToken.None));
        CaseCard card = await workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        AttachmentView failed = card.Attachments.Single();
        Assert.AreEqual(StoredFileStatus.UploadFailed, failed.Status);
        await workspace.RetryAttachmentAsync(fixture.Manager, new(taken.CaseId, failed.Id, "test.txt", "text/plain", content),
            "storage-retry", CancellationToken.None);
        Assert.AreEqual(1, handler.UploadCount);
        Assert.IsTrue(handler.Files.Keys.Single().Contains($"/{taken.CaseId:N}/Documents/", StringComparison.Ordinal));
        CollectionAssert.AreEqual(content, (await workspace.ReadAttachmentAsync(fixture.Manager, failed.Id, CancellationToken.None)).Content);
        int requestCount = handler.RequestCount;
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadAttachmentAsync(fixture.SecondManager, failed.Id, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.ReadAttachmentAsync(fixture.ForeignOwner, failed.Id, CancellationToken.None));
        Assert.AreEqual(requestCount, handler.RequestCount, "Denied requests must not even reach the provider.");
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            StoredFile stored = await db.StoredFiles.SingleAsync(item => item.Id == db.CaseAttachments.Where(link => link.Id == failed.Id).Select(link => link.StoredFileId).Single());
            stored.Sha256 = new string('0', 64); await db.SaveChangesAsync();
        }
        FileStorageException error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => workspace.ReadAttachmentAsync(fixture.Manager, failed.Id, CancellationToken.None));
        Assert.AreEqual("STORAGE_INTEGRITY", error.Code);
    }
}
