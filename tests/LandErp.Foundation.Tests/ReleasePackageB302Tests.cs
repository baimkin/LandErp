using System.Text.Json;
using System.Text.Json.Serialization;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Foundation.Files;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ReleasePackageB302Tests
{
    [TestMethod]
    public void EightMiBRawAttachmentFitsConfiguredJsonEnvelope()
    {
        byte[] content = new byte[FileUploadLimits.MaxRawFileBytes];
        AddCaseAttachment add = new(Guid.NewGuid(), CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document,
            new string('L', 512), new string('D', 4000), new string('N', 512), "application/octet-stream", content, null);
        RetryCaseAttachment retry = new(Guid.NewGuid(), Guid.NewGuid(), new string('N', 512), "application/octet-stream", content);

        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        int addBytes = JsonSerializer.SerializeToUtf8Bytes(add, options).Length;
        int retryBytes = JsonSerializer.SerializeToUtf8Bytes(retry, options).Length;

        Assert.IsTrue(addBytes > FileUploadLimits.MaxRawFileBytes,
            "Base64 JSON must demonstrate why an 8 MiB HTTP limit was insufficient.");
        Assert.IsTrue(addBytes <= FileUploadLimits.MaxJsonRequestBodyBytes);
        Assert.IsTrue(retryBytes <= FileUploadLimits.MaxJsonRequestBodyBytes);
    }

    [TestMethod]
    public async Task OversizeAttachmentIsRejectedBeforeStorageProviderWrite()
    {
        await using ProcurementTests.Phase1Fixture fixture = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid sourceId = await fixture.CreateUnlinkedManualAsync();
        TakeToWorkResult taken = await fixture.Workspace.TakeToWorkAsync(fixture.Manager, new(sourceId), "b302-take", CancellationToken.None);
        CountingFileStorage storage = new();
        ProcurementWorkspace workspace = new(fixture.Factory, fixture.Access, TimeProvider.System, storage);
        byte[] oversize = new byte[FileUploadLimits.MaxRawFileBytes + 1];

        FileUploadLimitException error = await Assert.ThrowsExactlyAsync<FileUploadLimitException>(() =>
            workspace.AddAttachmentAsync(fixture.Manager,
                new(taken.CaseId, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Document,
                    "Oversize", "", "oversize.bin", "application/pdf", oversize, null),
                "b302-oversize", CancellationToken.None));

        Assert.AreEqual(FileUploadLimits.TooLargeMessage, error.Message);
        Assert.AreEqual(0, storage.WriteCount);
        Assert.AreEqual(0, await fixture.CountAsync(db => db.CaseAttachments.CountAsync(item => item.PropertyCaseId == taken.CaseId)));
        Assert.AreEqual(0, await fixture.CountAsync(db => db.StoredFiles.CountAsync(item => item.OrganizationId == fixture.OrganizationId)));
    }

    [TestMethod]
    public void YandexDefaultUsesSharedRawFileLimit()
    {
        Assert.AreEqual(FileUploadLimits.MaxRawFileBytes, YandexDiskStorageTests.Options().MaxFileBytes);
        FileUploadLimits.EnsureRawFileSize(FileUploadLimits.MaxRawFileBytes);
        Assert.ThrowsExactly<FileUploadLimitException>(() => FileUploadLimits.EnsureRawFileSize((long)FileUploadLimits.MaxRawFileBytes + 1));
    }

    private sealed class CountingFileStorage : IFileStorage
    {
        public int WriteCount { get; private set; }
        public Task<FileWriteResult> WriteAsync(Guid stableFileId, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
        {
            WriteCount++;
            throw new AssertFailedException("Oversize file reached storage provider.");
        }
        public Task<byte[]> ReadAsync(string storageKey, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
