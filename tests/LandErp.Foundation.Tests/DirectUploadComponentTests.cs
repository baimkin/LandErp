using System.Reflection;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Server.Components.Procurement;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class DirectUploadComponentTests
{
    [TestMethod]
    public async Task ActualPickerNeverReadsLargeBrowserBytesAndRetriesSameIdentityAfterLostPut()
    {
        var activator = new CapturingActivator(); var js = new UploadJs();
        var registrations = new ServiceCollection().AddLogging();
        registrations.AddSingleton<IComponentActivator>(activator); registrations.AddSingleton<IJSRuntime>(js);
        await using var services = registrations.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        int prepares = 0, completes = 0, cancels = 0; Guid? stableId = null;
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var view = await renderer.RenderComponentAsync<CaseFileUpload>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["UseDirect"] = true,
                ["OnPrepare"] = (Func<IBrowserFile, Guid, string, Task<CaseAttachmentUploadTicket>>)((file, id, hash) =>
                {
                    Assert.AreEqual((long)FileUploadLimits.MaxRawFileBytes, file.Size); Assert.AreEqual(new string('a', 64), hash);
                    stableId ??= id; Assert.AreEqual(stableId.Value, id); prepares++;
                    return Task.FromResult(new CaseAttachmentUploadTicket(id, "https://storage.yandex.net/synthetic", false));
                }),
                ["OnComplete"] = (Func<CaseAttachmentUploadTicket, Task>)(_ => { completes++; return Task.CompletedTask; }),
                ["OnCancel"] = (Func<CaseAttachmentUploadTicket, Task>)(_ => { cancels++; return Task.CompletedTask; })
            }));
            var component = activator.Value!;
            var batch = (CaseUploadBatch)typeof(CaseFileUpload).GetField("batch", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(component)!;
            batch.Select([new MetadataOnlyBrowserFile()]);
            async Task Upload() => await (Task)typeof(CaseFileUpload).GetMethod("UploadAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(component, null)!;
            await Upload(); Assert.AreEqual(1, cancels); Assert.AreEqual(0, completes); Assert.IsFalse(batch.Items.Single().Complete);
            await Upload(); Assert.AreEqual(2, prepares); Assert.AreEqual(1, completes);
            Assert.AreEqual(1, js.Hashes, "Retry reuses the checked file/hash and original stable identity.");
            Assert.IsTrue(batch.Items.Single().Complete); StringAssert.Contains(System.Net.WebUtility.HtmlDecode(view.ToHtmlString()), "Все файлы загружены");
        });
    }
    private sealed class CapturingActivator : IComponentActivator
    {
        public CaseFileUpload? Value { get; private set; }
        public IComponent CreateInstance(Type type) { var value = (IComponent)Activator.CreateInstance(type)!; if (value is CaseFileUpload upload) Value = upload; return value; }
    }
    private sealed class MetadataOnlyBrowserFile : IBrowserFile
    {
        public string Name => "large.mov"; public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public long Size => FileUploadLimits.MaxRawFileBytes; public string ContentType => "video/quicktime";
        public Stream OpenReadStream(long maxAllowedSize = 512000, CancellationToken cancellationToken = default) => throw new AssertFailedException("Direct upload must never open a browser stream on Server.");
    }
    private sealed class UploadJs : IJSRuntime, IJSObjectReference
    {
        public int Hashes { get; private set; } private int puts;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((T)(object)this);
            if (identifier is "hashFile" or "putFile") Assert.IsTrue(cancellationToken.CanBeCanceled, "Large transfer must have its explicit deadline, bypassing the one-minute JS default.");
            if (identifier == "hashFile") { Hashes++; return ValueTask.FromResult((T)(object)new string('a', 64)); }
            if (identifier == "putFile" && ++puts == 1) throw new JSException("synthetic lost response");
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}