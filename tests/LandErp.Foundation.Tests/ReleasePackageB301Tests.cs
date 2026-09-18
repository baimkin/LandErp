using System.Security.Cryptography;
using System.Text;
using LandErp.Application.Foundation;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class ReleasePackageB301Tests
{
    [TestMethod]
    public async Task PendingUploadCanRecoverInPlaceAndDocumentRequirementBecomesReceived()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b301-take", CancellationToken.None);
        CaseCard initial = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        DocumentRequirementView requirement = initial.DocumentRequirements[0];

        Guid storedId = DataConventions.NewId();
        Guid attachmentId = DataConventions.NewId();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await using (LandErpDbContext db = await fixture.Factory.CreateDbContextAsync())
        {
            db.StoredFiles.Add(new()
            {
                Id = storedId, OrganizationId = fixture.OrganizationId, OwnerModule = "Procurement", Purpose = "Document",
                OriginalName = "pending.txt", ContentType = "text/plain", Status = StoredFileStatus.PendingUpload,
                CreatedByEmployeeId = fixture.ManagerEmployeeId, RecordedAt = now
            });
            db.CaseAttachments.Add(new()
            {
                Id = attachmentId, OrganizationId = fixture.OrganizationId, PropertyCaseId = taken.CaseId, StoredFileId = storedId,
                OwnerType = CaseAttachmentOwner.Case, DocumentRequirementId = requirement.Id, Kind = CaseAttachmentKind.Document,
                Label = "Незавершённая загрузка", ActorEmployeeId = fixture.ManagerEmployeeId, RecordedAt = now
            });
            await db.SaveChangesAsync();
        }

        CaseCard pending = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(1, pending.Attachments.Count(item => item.Id == attachmentId));
        Assert.AreEqual(StoredFileStatus.PendingUpload, pending.Attachments.Single(item => item.Id == attachmentId).Status);
        Assert.AreEqual(CaseDocumentStatus.Missing, pending.DocumentRequirements.Single(item => item.Id == requirement.Id).Status);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            fixture.Workspace.ReadAttachmentAsync(fixture.Manager, attachmentId, CancellationToken.None));

        byte[] bytes = Encoding.UTF8.GetBytes("recovered pending document");
        await fixture.Workspace.RetryAttachmentAsync(fixture.Manager,
            new(taken.CaseId, attachmentId, "pending.txt", "text/plain", bytes), "b301-retry-pending", CancellationToken.None);

        CaseCard recovered = await fixture.Workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(1, recovered.Attachments.Count(item => item.Id == attachmentId), "Recovery must continue the existing attachment.");
        Assert.AreEqual(StoredFileStatus.Available, recovered.Attachments.Single(item => item.Id == attachmentId).Status);
        Assert.AreEqual(CaseDocumentStatus.Received, recovered.DocumentRequirements.Single(item => item.Id == requirement.Id).Status);
        CollectionAssert.AreEqual(bytes, (await fixture.Workspace.ReadAttachmentAsync(fixture.Manager, attachmentId, CancellationToken.None)).Content!);
    }

    [TestMethod]
    public async Task ProviderFailureKeepsSingleRecoverableAttachmentAndRetryDoesNotDuplicateMetadata()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b301-take", CancellationToken.None);
        FailOnceFileStorage storage = new();
        ProcurementWorkspace workspace = new(fixture.Factory, fixture.Access, TimeProvider.System, storage);
        byte[] bytes = Encoding.UTF8.GetBytes("fault injected document");

        await Assert.ThrowsExactlyAsync<FileStorageException>(() => workspace.AddAttachmentAsync(fixture.Manager,
            new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document, "Fault recovery", "",
                "fault.txt", "text/plain", bytes, null), "b301-fault-add", CancellationToken.None));

        CaseCard failedCard = await workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        AttachmentView failed = failedCard.Attachments.Single();
        Assert.AreEqual(StoredFileStatus.UploadFailed, failed.Status);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.CaseAttachments.CountAsync(item => item.PropertyCaseId == taken.CaseId)));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.StoredFiles.CountAsync(item => item.OrganizationId == fixture.OrganizationId)));

        await workspace.RetryAttachmentAsync(fixture.Manager,
            new(taken.CaseId, failed.Id, "fault.txt", "text/plain", bytes), "b301-fault-retry", CancellationToken.None);

        CaseCard recovered = await workspace.ReadCardAsync(fixture.Manager, taken.CaseId, CancellationToken.None);
        Assert.AreEqual(failed.Id, recovered.Attachments.Single().Id);
        Assert.AreEqual(StoredFileStatus.Available, recovered.Attachments.Single().Status);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.CaseAttachments.CountAsync(item => item.PropertyCaseId == taken.CaseId)));
        Assert.AreEqual(1, await fixture.CountAsync(db => db.StoredFiles.CountAsync(item => item.OrganizationId == fixture.OrganizationId)));
        Assert.AreEqual(2, storage.WriteAttempts);
        CollectionAssert.AreEqual(bytes, (await workspace.ReadAttachmentAsync(fixture.Manager, failed.Id, CancellationToken.None)).Content!);
        Assert.AreEqual(1, await fixture.CountAsync(db => db.AuditEvents.CountAsync(item =>
            item.ObjectId == taken.CaseId && item.Action == "CaseAttachmentUploadRetried")));
    }

    private sealed class FailOnceFileStorage : IFileStorage
    {
        private readonly Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        public int WriteAttempts { get; private set; }

        public Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken) =>
            WriteAsync(new FileWriteRequest(stableFileId, Guid.Empty, Guid.Empty, "Document", "text/plain"), content, cancellationToken);

        public Task<FileWriteResult> WriteAsync(FileWriteRequest request, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            WriteAttempts++;
            if (WriteAttempts == 1)
                throw new FileStorageException("STORAGE_FAULT_INJECTED", true, "Synthetic retryable storage failure.");

            byte[] bytes = content.ToArray();
            string key = request.FileId.ToString("N");
            files[key] = bytes;
            string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return Task.FromResult(new FileWriteResult(key, hash, bytes.LongLength));
        }

        public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult(files[storageKey].ToArray());
    }
}
