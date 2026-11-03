using System.Net;
using System.Security.Cryptography;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Foundation.Files;
using LandErp.Infrastructure.Modules.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class DirectAttachmentUploadTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static BeginCaseAttachmentUpload Command(Guid id, long bytes = FileUploadLimits.MaxRawFileBytes, Guid? uploadId = null) =>
        new(new(id, CaseAttachmentOwner.Case, null, CaseAttachmentKind.Video, "Большое видео", "", "large.mov", "video/quicktime", null, null, UploadId: uploadId ?? Guid.CreateVersion7()), bytes, Hash);

    [TestMethod, TestCategory("PostgreSQL")]
    public async Task FiveHundredMiBUsesOnlyMetadataAndConfirmsOnceAfterAmbiguousUpload()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await f.InsertIndependentCaseAsync("Большой файл");
        var storage = new DirectStorage(f.FileStorage);
        var workspace = new ProcurementWorkspace(f.Factory, TimeProvider.System, storage);
        var command = Command(id);
        var ticket = await workspace.BeginAttachmentUploadAsync(f.Manager, command, "begin-direct", Ct);
        Assert.IsFalse(ticket.Available); Assert.IsNotNull(ticket.UploadUrl);
        await using (var db = f.Sandbox.Context())
        {
            var file = await db.StoredFiles.SingleAsync();
            Assert.AreEqual(524288000L, file.SizeBytes);
            Assert.AreEqual(StoredFileStatus.PendingUpload, file.Status);
            Assert.IsNull(file.StorageKey);
            Assert.AreEqual(0, await db.BusinessTimeline.CountAsync(x => x.ObjectId == id && x.Kind == "Attachment"));
        }
        await Assert.ThrowsExactlyAsync<FileStorageException>(() => workspace.CompleteAttachmentUploadAsync(f.Manager, new(id, ticket.AttachmentId), "not-uploaded", Ct));
        var attempts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => workspace.BeginAttachmentUploadAsync(f.Manager, command, "same-draft", Ct)));
        Assert.IsTrue(attempts.All(x => x.AttachmentId == ticket.AttachmentId));
        storage.Uploaded = true; // Emulates the browser PUT; no content ever enters this workspace.
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => workspace.CompleteAttachmentUploadAsync(f.Manager, new(id, ticket.AttachmentId), "confirm", Ct)));
        var replay = await workspace.BeginAttachmentUploadAsync(f.Manager, command, "replay", Ct);
        Assert.IsTrue(replay.Available); Assert.IsNull(replay.UploadUrl);
        Assert.AreEqual(0, storage.BufferedWrites);
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(1, await check.CaseAttachments.CountAsync());
        Assert.AreEqual(StoredFileStatus.Available, (await check.StoredFiles.SingleAsync()).Status);
        Assert.AreEqual(1, await check.BusinessTimeline.CountAsync(x => x.ObjectId == id && x.Kind == "Attachment"));
        Assert.AreEqual(1, await check.AuditEvents.CountAsync(x => x.ObjectId == id && x.Action == "CaseAttachmentAdded"));
    }

    [TestMethod, TestCategory("PostgreSQL")]
    public async Task LimitForgeryForeignScopeCancellationAndDeletionNeverPublishWrongFile()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(false, false);
        Guid id = await f.InsertIndependentCaseAsync("Проверки прямой загрузки");
        var storage = new DirectStorage(f.FileStorage); var workspace = new ProcurementWorkspace(f.Factory, TimeProvider.System, storage);
        await Assert.ThrowsExactlyAsync<FileUploadLimitException>(() => workspace.BeginAttachmentUploadAsync(f.Manager, Command(id, (long)FileUploadLimits.MaxRawFileBytes + 1), "oversize", Ct));
        Assert.AreEqual(0, storage.Prepares);
        var command = Command(id, 60);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.BeginAttachmentUploadAsync(f.ForeignOwner, command, "foreign", Ct));
        var ticket = await workspace.BeginAttachmentUploadAsync(f.Manager, command, "begin", Ct);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.BeginAttachmentUploadAsync(f.Manager, command with { Sha256 = new string('b',64) }, "hash-forgery", Ct));
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.CompleteAttachmentUploadAsync(f.ForeignOwner, new(id, ticket.AttachmentId), "foreign-confirm", Ct));
        storage.Uploaded = true; storage.WrongHash = true;
        await Assert.ThrowsExactlyAsync<FileStorageException>(() => workspace.CompleteAttachmentUploadAsync(f.Manager, new(id, ticket.AttachmentId), "wrong-hash", Ct));
        await using (var db = f.Sandbox.Context()) Assert.AreNotEqual(StoredFileStatus.Available, (await db.StoredFiles.SingleAsync()).Status);
        await workspace.CancelAttachmentUploadAsync(f.Manager, new(id, ticket.AttachmentId), "cancel", Ct);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.DeleteAttachmentAsync(f.Manager, new(id, ticket.AttachmentId), "unconfirmed-delete", Ct));
        storage.WrongHash = false;
        await workspace.BeginAttachmentRetryAsync(f.Manager, new(id, ticket.AttachmentId, "large.mov", "video/quicktime", 60, Hash), "retry", Ct);
        await workspace.CompleteAttachmentUploadAsync(f.Manager, new(id, ticket.AttachmentId), "confirm", Ct);
        await workspace.DeleteAttachmentAsync(f.Manager, new(id, ticket.AttachmentId), "delete", Ct);
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.CompleteAttachmentUploadAsync(f.Manager, new(id, ticket.AttachmentId), "late-confirm", Ct));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => workspace.BeginAttachmentUploadAsync(f.Manager, command, "late-begin", Ct));
        await using var check = f.Sandbox.Context();
        Assert.AreEqual(StoredFileStatus.Deleted, (await check.StoredFiles.SingleAsync()).Status);
        Assert.AreEqual(1, await check.BusinessTimeline.CountAsync(x => x.ObjectId == id && x.Kind == "Attachment"));
        Assert.AreEqual(0, storage.BufferedWrites);
    }

    [TestMethod, TestCategory("PostgreSQL")]
    public async Task InspectionAssignmentAndRightsAreRecheckedAtConfirmation()
    {
        await using var f = await ProcurementTests.Phase1Fixture.CreateAsync(true, false);
        Guid id = await f.InsertIndependentCaseAsync("Осмотр");
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess with
        { ProcurementAccess = ProcurementAccessLevel.Manager, ProcurementReadScope = AccessScope.Department, ProcurementWorkScope = AccessScope.Department, CanPerformInspections = true });
        await f.Workspace.AssignInspectionAsync(f.Head, new(id, f.ManagerEmployeeId, null, "Осмотреть", null), "assign", Ct);
        Guid inspection = (await f.Workspace.ReadInspectionAsync(f.Manager, id, Ct)).Inspection!.Id;
        var storage = new DirectStorage(f.FileStorage); var workspace = new ProcurementWorkspace(f.Factory, TimeProvider.System, storage);
        var command = Command(id, 60);
        command = command with { Attachment = command.Attachment with { OwnerType = CaseAttachmentOwner.Inspection, OwnerId = inspection } };
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.BeginAttachmentUploadAsync(f.SecondManager, command, "not-inspector", Ct));
        var ticket = await workspace.BeginAttachmentUploadAsync(f.Manager, command, "inspector", Ct);
        storage.Uploaded = true;
        await f.SetExplicitAccessAsync(f.ManagerEmployeeId, EmployeeAccessRules.NoAccess);
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(() => workspace.CompleteAttachmentUploadAsync(f.Manager, new(id, ticket.AttachmentId), "revoked", Ct));
        await using var db = f.Sandbox.Context();
        Assert.AreEqual(StoredFileStatus.PendingUpload, (await db.StoredFiles.SingleAsync()).Status);
    }

    [TestMethod]
    public async Task YandexIssuesOnlyCapabilityAndConfirmsBrowserBytesWithNoServerPut()
    {
        using var handler = new YandexDiskStorageTests.DiskHandler(); using var http = new HttpClient(handler);
        using var storage = new YandexDiskFileStorage(YandexDiskStorageTests.Options(), http);
        byte[] bytes = "browser supplied bytes"u8.ToArray();
        string hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var request = new FileWriteRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Video", "video/quicktime");
        var link = await storage.PrepareDirectUploadAsync(request, bytes.Length, hash, Ct);
        Assert.AreEqual(0, handler.UploadCount);
        Assert.IsFalse(link.ToString().Contains(link.UploadUrl!, StringComparison.Ordinal));
        using var response = await http.PutAsync(link.UploadUrl, new ByteArrayContent(bytes), Ct);
        Assert.AreEqual(HttpStatusCode.Created, response.StatusCode);
        var confirmed = await storage.ConfirmDirectUploadAsync(request, bytes.Length, hash, Ct);
        StringAssert.EndsWith(confirmed.StorageKey, ".mov");
        Assert.AreEqual(hash, confirmed.Sha256);
        Assert.IsNull((await storage.PrepareDirectUploadAsync(request, bytes.Length, hash, Ct)).UploadUrl);
        Assert.AreEqual(1, handler.UploadCount);
        handler.Files[handler.Files.Keys.Single()] = [1,2,3];
        var error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.ConfirmDirectUploadAsync(request, bytes.Length, hash, Ct));
        Assert.AreEqual("STORAGE_INTEGRITY", error.Code);
    }
    [TestMethod, TestCategory("PostgreSQL")]
    public async Task ProviderWaitReleasesCaseLockAndRechecksRevokedRightsAfterWaiting()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        Guid id=await f.InsertIndependentCaseAsync("Independent confirmations");
        var storage=new DirectStorage(f.FileStorage);var workspace=new ProcurementWorkspace(f.Factory,TimeProvider.System,storage);
        var first=await workspace.BeginAttachmentUploadAsync(f.Manager,Command(id,60),"first",Ct);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.Uploaded=true;storage.BeforeConfirm=async _=>{entered.TrySetResult();await release.Task;};
        Task confirmation=workspace.CompleteAttachmentUploadAsync(f.Manager,new(id,first.AttachmentId),"slow-provider",Ct);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var second=await workspace.BeginAttachmentUploadAsync(f.Manager,Command(id,60),"second-during-wait",Ct).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.AreNotEqual(first.AttachmentId,second.AttachmentId,"A provider wait must not lock the case against subsequent uploads.");
            await f.SetExplicitAccessAsync(f.ManagerEmployeeId,EmployeeAccessRules.NoAccess);
        }
        finally{release.TrySetResult();}
        await Assert.ThrowsExactlyAsync<AccessDeniedException>(()=>confirmation);
        await using var db=f.Sandbox.Context();
        Assert.AreEqual(0,await db.StoredFiles.CountAsync(x=>x.Status==StoredFileStatus.Available));
        Assert.AreEqual(0,await db.BusinessTimeline.CountAsync(x=>x.ObjectId==id&&x.Kind=="Attachment"));
    }
    internal sealed class DirectStorage(IFileStorage inner) : IFileStorage, IDirectFileStorage
    {
        public bool SupportsDirectUploads => true;
        public Func<FileWriteRequest,Task>? BeforeConfirm { get; set; }
        public bool Uploaded { get; set; }
        public bool WrongHash { get; set; }
        public int Prepares { get; private set; }
        public int BufferedWrites { get; private set; }
        public Task<FileWriteResult> WriteAsync(Guid id, ReadOnlyMemory<byte> bytes, CancellationToken ct)
        { BufferedWrites++; throw new InvalidOperationException("Direct path must not send bytes through ERP."); }
        public Task<byte[]> ReadAsync(string key, CancellationToken ct) => inner.ReadAsync(key, ct);
        public Task<DirectFileUpload> PrepareDirectUploadAsync(FileWriteRequest request, long sizeBytes, string sha256, CancellationToken ct)
        { Prepares++; return Task.FromResult(new DirectFileUpload(Uploaded ? null : "https://storage.yandex.net/synthetic-upload")); }
        public async Task<FileWriteResult> ConfirmDirectUploadAsync(FileWriteRequest request, long sizeBytes, string sha256, CancellationToken ct)
        {
            if(BeforeConfirm!=null)await BeforeConfirm(request);
            if (!Uploaded) throw new FileStorageException("STORAGE_UPLOAD_PENDING", true, "synthetic pending");
            return new FileWriteResult("synthetic-key", WrongHash ? new string('f',64) : sha256, sizeBytes);
        }
    }
}