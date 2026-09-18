using System.Security.Claims;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Server.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class WorkspaceOperationTests
{
    [TestMethod]
    public async Task CommittedWriteSurvivesReadFailureAndReadOnlyRetry()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        int writes = 0;
        workspace.Read = () => Task.FromException(new IOException("private refresh detail"));

        bool committed = await workspace.SubmitAsync(() => { writes++; return Task.CompletedTask; });

        Assert.IsTrue(committed);
        Assert.AreEqual(WorkspaceWriteOutcome.Committed, workspace.Outcome);
        Assert.IsNull(workspace.Failure);
        Assert.IsTrue(workspace.NeedsRefresh);
        StringAssert.Contains(workspace.Message!, "Изменения сохранены, но экран не обновлён");
        StringAssert.Contains(workspace.Message!, workspace.RequestId);
        StringAssert.Contains(workspace.Diagnostics.Entries.Single().Message, "RefreshAfterCommit");
        StringAssert.Contains(workspace.Diagnostics.Entries.Single().Message, workspace.RequestId);
        Assert.AreEqual(1, writes);
        Assert.IsFalse(workspace.IsBusy);

        workspace.Read = () => Task.CompletedTask;
        await workspace.RefreshAsync();
        Assert.AreEqual(1, writes, "Refreshing must never call the committed write again.");
        Assert.AreEqual(3, workspace.ReadCount);
        Assert.IsFalse(workspace.NeedsRefresh);
        Assert.AreEqual("Изменения сохранены", workspace.Message);
    }

    [TestMethod]
    public async Task LegacyTaskCallerCanCloseDialogAfterConfirmedWriteAndFailedRefresh()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        workspace.Read = () => Task.FromException(new IOException("read failed"));
        await workspace.SubmitLegacyAsync(() => Task.CompletedTask);
        bool dialogClosed = workspace.Message != null;
        Assert.IsTrue(dialogClosed);
        Assert.IsNull(workspace.Failure, "Existing dialogs must not interpret the refresh failure as a failed write.");
        Assert.AreEqual(WorkspaceWriteOutcome.Committed, workspace.Outcome);
    }

    [TestMethod]
    public async Task ValidationRejectsWithoutClearingTheDraft()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        string draft = "Unsaved user input";
        bool committed = await workspace.SubmitAsync(() => Task.FromException(new ArgumentException("Укажите название.")));
        if (committed) draft = "";
        Assert.IsFalse(committed);
        Assert.AreEqual("Unsaved user input", draft);
        Assert.AreEqual(WorkspaceWriteOutcome.Rejected, workspace.Outcome);
        Assert.IsNull(workspace.Message);
        StringAssert.Contains(workspace.Failure!, "Укажите название.");
        StringAssert.Contains(workspace.Diagnostics.Entries.Single().Message, "Validation");
        Assert.IsFalse(workspace.IsBusy);
    }

    [TestMethod]
    public async Task ConcurrencyConflictIsNotReportedAsSuccess()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        bool committed = await workspace.SubmitAsync(() => Task.FromException(new DbUpdateConcurrencyException("private row values")));
        Assert.IsFalse(committed);
        Assert.AreEqual(WorkspaceWriteOutcome.Rejected, workspace.Outcome);
        Assert.IsNull(workspace.Message);
        StringAssert.Contains(workspace.Failure!, "Данные уже изменены");
        Assert.IsFalse(workspace.Failure!.Contains("private row values", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task PermissionDenialIsNotReportedAsSuccess()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        bool committed = await workspace.SubmitAsync(() => Task.FromException(new AccessDeniedException()));
        Assert.IsFalse(committed);
        Assert.IsTrue(workspace.IsForbidden);
        Assert.AreEqual(WorkspaceWriteOutcome.Rejected, workspace.Outcome);
        Assert.IsNull(workspace.Message);
        Assert.AreEqual(1, workspace.ReadCount, "A rejected command must not reload or erase its form.");
    }

    [TestMethod]
    public async Task LostAcknowledgementIsUnknownEvenIfTheWriteActuallyHappened()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        int writes = 0;
        bool committed = await workspace.SubmitAsync(() =>
        {
            writes++;
            return Task.FromException(new IOException("response lost after durable write"));
        });
        Assert.IsFalse(committed);
        Assert.AreEqual(1, writes);
        Assert.AreEqual(WorkspaceWriteOutcome.Unknown, workspace.Outcome);
        Assert.IsNull(workspace.Message);
        StringAssert.Contains(workspace.Failure!, "Результат сохранения не подтверждён");
        Assert.IsFalse(workspace.Failure!.Contains("Сохранение не выполнено", StringComparison.Ordinal));
        Assert.AreEqual(1, workspace.ReadCount);
    }

    [TestMethod]
    public async Task ProviderFailureDoesNotClaimThatAttachmentMetadataWasRolledBack()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        bool committed = await workspace.SubmitAsync(() => Task.FromException(
            new FileStorageException("STORAGE_UNAVAILABLE", true, "private provider reply")));
        Assert.IsFalse(committed);
        Assert.AreEqual(WorkspaceWriteOutcome.Unknown, workspace.Outcome);
        StringAssert.Contains(workspace.Failure!, "проверьте состояние вложения");
        Assert.IsFalse(workspace.Failure!.Contains("private provider reply", StringComparison.Ordinal));
        StringAssert.Contains(workspace.Diagnostics.Entries.Single().Message, "Storage");
    }

    [TestMethod]
    public async Task FailedAuthenticationDoesNotSendTheCommand()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        workspace.AuthenticationStub.Failure = new IOException("private session info");
        int writes = 0;
        bool committed = await workspace.SubmitAsync(() => { writes++; return Task.CompletedTask; });
        Assert.IsFalse(committed);
        Assert.AreEqual(0, writes);
        Assert.AreEqual(WorkspaceWriteOutcome.NotSent, workspace.Outcome);
        StringAssert.Contains(workspace.Failure!, "Команда не отправлена");
        Assert.IsFalse(workspace.IsBusy);
        StringAssert.Contains(workspace.Diagnostics.Entries.Single().Message, "Authenticate");
    }

    [TestMethod]
    public async Task ConcurrentSubmitAndRefreshDoNotReplaceAnInflightOperation()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        TaskCompletionSource<bool> release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int writes = 0;
        Task<bool> first = workspace.SubmitAsync(async () => { writes++; await release.Task; });
        try
        {
            string operationId = workspace.RequestId;
            Assert.IsTrue(workspace.IsBusy);
            bool second = await workspace.SubmitAsync(() => { writes++; return Task.CompletedTask; });
            await workspace.RefreshAsync();
            Assert.IsFalse(second);
            Assert.AreEqual(1, writes);
            Assert.AreEqual(1, workspace.ReadCount);
            Assert.AreEqual(operationId, workspace.RequestId);
        }
        finally { release.TrySetResult(true); }
        Assert.IsTrue(await first);
        Assert.AreEqual(2, workspace.ReadCount);
        Assert.IsFalse(workspace.IsBusy);
    }

    [TestMethod]
    public async Task InitialLoadingPreventsSubmittingUninitializedForms()
    {
        ProbeWorkspace workspace = new();
        int writes = 0;
        Assert.IsFalse(await workspace.SubmitAsync(() => { writes++; return Task.CompletedTask; }));
        Assert.AreEqual(0, writes);
        Assert.AreEqual(WorkspaceWriteOutcome.None, workspace.Outcome);
    }

    [TestMethod]
    public async Task DiagnosticsContainBoundaryAndIdButNotExceptionPayloadOrInnerException()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        const string secret = "SYNTHETIC_TOKEN_AND_PRIVATE_DOCUMENT";
        await workspace.SubmitAsync(() => Task.FromException(new IOException(secret, new InvalidOperationException(secret))));
        Diagnostic entry = workspace.Diagnostics.Entries.Single();
        StringAssert.Contains(entry.Message, workspace.RequestId);
        StringAssert.Contains(entry.Message, "Command");
        StringAssert.Contains(entry.Message, "SubmitAsync");
        StringAssert.Contains(entry.Message, nameof(IOException));
        Assert.IsNull(entry.Exception, "Do not pass exception or InnerException to the logger.");
        Assert.IsFalse(entry.Message.Contains(secret, StringComparison.Ordinal));
        Assert.IsFalse(workspace.Failure!.Contains(secret, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task InitialReadFailureHasSafeDiagnosticsAndCanBeRetried()
    {
        ProbeWorkspace workspace = new();
        workspace.Read = () => Task.FromException(new IOException("secret connection string"));
        await workspace.RefreshAsync();
        Assert.IsFalse(workspace.IsLoading);
        StringAssert.Contains(workspace.Failure!, workspace.RequestId);
        Diagnostic entry = workspace.Diagnostics.Entries.Single();
        StringAssert.Contains(entry.Message, "Read");
        Assert.IsNull(entry.Exception);
        Assert.IsFalse(entry.Message.Contains("secret connection string", StringComparison.Ordinal));
        workspace.Read = () => Task.CompletedTask;
        await workspace.RefreshAsync();
        Assert.IsNull(workspace.Failure);
    }

    [TestMethod]
    public async Task LoggingFailureCannotChangeTheConfirmedBusinessResult()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        workspace.Diagnostics.ThrowOnLog = true;
        workspace.Read = () => Task.FromException(new IOException("refresh failed"));
        Assert.IsTrue(await workspace.SubmitAsync(() => Task.CompletedTask));
        Assert.AreEqual(WorkspaceWriteOutcome.Committed, workspace.Outcome);
        Assert.IsTrue(workspace.NeedsRefresh);
        Assert.IsFalse(workspace.IsBusy);
    }

    [TestMethod]
    public async Task AccessLostDuringRefreshStillPreservesAcknowledgementOfTheWrite()
    {
        ProbeWorkspace workspace = new();
        await workspace.RefreshAsync();
        workspace.Read = () => Task.FromException(new AccessDeniedException());
        Assert.IsTrue(await workspace.SubmitAsync(() => Task.CompletedTask));
        Assert.IsTrue(workspace.IsForbidden);
        Assert.AreEqual(WorkspaceWriteOutcome.Committed, workspace.Outcome);
        Assert.IsNull(workspace.Failure);
        Assert.IsNotNull(workspace.Message);
    }

    private sealed class ProbeWorkspace : WorkspaceComponent
    {
        public StubAuthentication AuthenticationStub { get; } = new();
        public RecordingLogger Diagnostics { get; } = new();
        public Func<Task> Read { get; set; } = () => Task.CompletedTask;
        public int ReadCount { get; private set; }
        public ProbeWorkspace() { Authentication = AuthenticationStub; Logger = Diagnostics; }
        public WorkspaceWriteOutcome Outcome => WriteOutcome;
        public bool NeedsRefresh => RefreshRequired;
        public bool IsBusy => Busy;
        public bool IsLoading => Loading;
        public bool IsForbidden => Forbidden;
        public string? Failure => Error;
        public string? Message => Success;
        public string RequestId => OperationId;
        public Task RefreshAsync() => ReloadAsync();
        public Task<bool> SubmitAsync(Func<Task> command) => ExecuteConfirmedAsync(command);
        public Task SubmitLegacyAsync(Func<Task> command) => ExecuteAsync(command);
        protected override Task ReadAsync() { ReadCount++; return Read(); }
    }

    private sealed class StubAuthentication : AuthenticationStateProvider
    {
        public Exception? Failure { get; set; }
        private readonly AuthenticationState state = new(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Test")));
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Failure is { } failure
            ? Task.FromException<AuthenticationState>(failure) : Task.FromResult(state);
    }

    private sealed record Diagnostic(string Message, Exception? Exception);

    private sealed class RecordingLogger : ILogger<WorkspaceComponent>
    {
        public List<Diagnostic> Entries { get; } = [];
        public bool ThrowOnLog { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (ThrowOnLog) throw new InvalidOperationException("Synthetic logger failure");
            Entries.Add(new(formatter(state, exception), exception));
        }
    }
}
