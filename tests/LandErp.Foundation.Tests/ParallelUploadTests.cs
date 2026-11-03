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
public sealed class ParallelUploadTests
{
    private static TaskCompletionSource Signal()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TimeSpan Timeout=TimeSpan.FromSeconds(15);
    [TestMethod]
    public async Task FiveTransfersOneHashAndSlowConfirmationDoesNotHoldSixthFile()
    {
        var batch=new CaseUploadBatch();batch.Select(Enumerable.Range(0,7).Select(i=>(IBrowserFile)new MetadataFile(i)).ToArray());
        var five=Signal();var bodies=Signal();var allConfirming=Signal();var releaseConfirmation=Signal();
        int checking=0,maxChecks=0,sending=0,maxSends=0,sent=0,confirming=0;
        Task run=batch.RunParallelAsync(async item=>{
            int n=Interlocked.Increment(ref checking);maxChecks=Math.Max(maxChecks,n);await Task.Yield();item.Sha256="cached";Interlocked.Decrement(ref checking);
        },async _=>{
            int n=Interlocked.Increment(ref sending);maxSends=Math.Max(maxSends,n);if(n==5)five.TrySetResult();
            await bodies.Task;Interlocked.Decrement(ref sending);Interlocked.Increment(ref sent);
        },async _=>{if(Interlocked.Increment(ref confirming)==7)allConfirming.TrySetResult();await releaseConfirmation.Task;});
        try
        {
            await five.Task.WaitAsync(Timeout);Assert.AreEqual(5,maxSends);Assert.AreEqual(1,maxChecks);Assert.AreEqual(0,sent);
            bodies.SetResult();await allConfirming.Task.WaitAsync(Timeout);Assert.AreEqual(7,sent);Assert.IsTrue(batch.Busy);
        }
        finally{bodies.TrySetResult();releaseConfirmation.TrySetResult();await run;}
        Assert.IsTrue(batch.Items.All(x=>x.Complete));Assert.IsFalse(batch.Busy);
    }
    [TestMethod]
    public async Task FailureIsIndependentAndRetryKeepsSuccessfulFilesAndCachedHashes()
    {
        var batch=new CaseUploadBatch();batch.Select(Enumerable.Range(0,8).Select(i=>(IBrowserFile)new MetadataFile(i)).ToArray());
        Guid failed=batch.Items[2].UploadId;int checks=0,sends=0,confirms=0;
        async Task Check(CaseUploadBatch.Item item){if(item.Sha256!=null)return;checks++;await Task.Yield();item.Sha256="cached";}
        await batch.RunParallelAsync(Check,item=>{sends++;if(item.UploadId==failed)throw new ArgumentException("synthetic failure");return Task.CompletedTask;},_=>{confirms++;return Task.CompletedTask;});
        Assert.AreEqual(7,batch.Items.Count(x=>x.Complete));Assert.AreEqual(8,checks);Assert.AreEqual(7,confirms);
        await batch.RunParallelAsync(Check,item=>{Assert.AreEqual(failed,item.UploadId);sends++;return Task.CompletedTask;},_=>Task.CompletedTask);
        Assert.AreEqual(8,checks);Assert.AreEqual(9,sends);Assert.IsTrue(batch.Items.All(x=>x.Complete));
    }
    [TestMethod]
    public async Task StopPreventsQueuedFilesFromStarting()
    {
        var batch=new CaseUploadBatch();batch.Select(Enumerable.Range(0,9).Select(i=>(IBrowserFile)new MetadataFile(i)).ToArray());
        var five=Signal();var release=Signal();int sends=0;
        Task run=batch.RunParallelAsync(_=>Task.CompletedTask,async _=>{if(Interlocked.Increment(ref sends)==5)five.SetResult();await release.Task;throw new ArgumentException("Stopped");},_=>throw new AssertFailedException("No stopped transfer may confirm."));
        try{await five.Task.WaitAsync(Timeout);batch.Stop();}finally{release.TrySetResult();await run;}
        Assert.AreEqual(5,sends);Assert.AreEqual(9,batch.Items.Count(x=>x.Error!=null));Assert.IsFalse(batch.Busy);
    }
    [TestMethod]
    public async Task ActualComponentHasIndependentFiveFileProgressAndReleasesSlotsBeforeConfirmation()
    {
        var activator=new CapturingActivator();var js=new ConcurrentJs();
        await using var services=new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(activator).AddSingleton<IJSRuntime>(js).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        var confirmed=Signal();var releaseConfirm=Signal();int confirms=0;
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var rendered=await renderer.RenderComponentAsync<CaseFileUpload>(ParameterView.FromDictionary(new Dictionary<string,object?>{
                ["UseDirect"]=true,
                ["OnPrepare"]=(Func<IBrowserFile,Guid,string,Task<CaseAttachmentUploadTicket>>)((_,id,_)=>Task.FromResult(new CaseAttachmentUploadTicket(id,"https://storage.yandex.net/synthetic",false))),
                ["OnComplete"]=(Func<CaseAttachmentUploadTicket,Task>)(async _=>{if(++confirms==7)confirmed.TrySetResult();await releaseConfirm.Task;})
            }));
            var component=activator.Value!;var batch=(CaseUploadBatch)typeof(CaseFileUpload).GetField("batch",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(component)!;
            batch.Select(Enumerable.Range(0,7).Select(i=>(IBrowserFile)new MetadataFile(i)).ToArray());
            Task run=(Task)typeof(CaseFileUpload).GetMethod("UploadAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(component,null)!;
            try
            {
                await js.Five.Task.WaitAsync(Timeout);Assert.AreEqual(5,js.MaximumPuts);Assert.AreEqual(1,js.MaximumHashes);
                StringAssert.Contains(System.Net.WebUtility.HtmlDecode(rendered.ToHtmlString()),"МБ/с");
                js.Release.TrySetResult();await confirmed.Task.WaitAsync(Timeout);Assert.AreEqual(7,js.Puts);Assert.IsTrue(batch.Busy);
                Assert.IsTrue(batch.Items.All(x=>x.Stage==CaseUploadStage.Confirming));
            }
            finally{js.Release.TrySetResult();releaseConfirm.TrySetResult();await run;}
            Assert.IsTrue(batch.Items.All(x=>x.Complete));
            Assert.IsTrue(batch.Items.All(x=>x.Duration(CaseUploadStage.Sending)==TimeSpan.FromMilliseconds(1000)));
            Assert.IsTrue(batch.Items.All(x=>x.Duration(CaseUploadStage.WaitingForResponse)==TimeSpan.FromMilliseconds(250)));
            StringAssert.Contains(System.Net.WebUtility.HtmlDecode(rendered.ToHtmlString()),"Все файлы загружены");
        });
    }
    [TestMethod]
    public async Task ModuleLoadingCannotDoubleStartAndFailureCanBeRetried()
    {
        var activator=new CapturingActivator();var js=new LoadingJs();
        await using var services=new ServiceCollection().AddLogging().AddSingleton<IComponentActivator>(activator).AddSingleton<IJSRuntime>(js).BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var rendered=await renderer.RenderComponentAsync<CaseFileUpload>(ParameterView.FromDictionary(new Dictionary<string,object?>{
                ["UseDirect"]=true,
                ["OnPrepare"]=(Func<IBrowserFile,Guid,string,Task<CaseAttachmentUploadTicket>>)((_,id,_)=>Task.FromResult(new CaseAttachmentUploadTicket(id,null,true)))
            }));
            var component=activator.Value!;
            var batch=(CaseUploadBatch)typeof(CaseFileUpload).GetField("batch",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(component)!;
            batch.Select([new MetadataFile(0)]);
            Task Upload()=>(Task)typeof(CaseFileUpload).GetMethod("UploadAsync",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(component,null)!;
            Task first=Upload();await js.Entered.Task.WaitAsync(Timeout);await Upload();Assert.AreEqual(1,js.Imports);
            js.Release.SetResult();await first;
            StringAssert.Contains(System.Net.WebUtility.HtmlDecode(rendered.ToHtmlString()),"Не удалось подготовить загрузку");
            await Upload();Assert.AreEqual(2,js.Imports);Assert.IsTrue(batch.Items.Single().Complete);
            Assert.IsFalse(System.Net.WebUtility.HtmlDecode(rendered.ToHtmlString()).Contains("Не удалось подготовить загрузку",StringComparison.Ordinal));
        });
    }
    private sealed class LoadingJs:IJSRuntime,IJSObjectReference
    {
        public TaskCompletionSource Entered{get;}=Signal();public TaskCompletionSource Release{get;}=Signal();public int Imports{get;private set;}
        public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>InvokeAsync<T>(identifier,CancellationToken.None,args);
        public async ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args)
        {
            if(identifier=="import")
            {
                if(++Imports==1){Entered.SetResult();await Release.Task;throw new JSException("synthetic load failure");}
                return (T)(object)this;
            }
            if(identifier=="hashFile")return (T)(object)new string('a',64);
            return default!;
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }    private sealed class CapturingActivator:IComponentActivator
    {
        public CaseFileUpload? Value{get;private set;}
        public IComponent CreateInstance(Type type){var result=(IComponent)Activator.CreateInstance(type)!;if(result is CaseFileUpload value)Value=value;return result;}
    }
    private sealed class MetadataFile(int number):IBrowserFile
    {
        public string Name=>$"video-{number}.mp4";public DateTimeOffset LastModified=>DateTimeOffset.UnixEpoch;
        public long Size=>FileUploadLimits.MaxRawFileBytes;public string ContentType=>"video/mp4";
        public Stream OpenReadStream(long maxAllowedSize=512000,CancellationToken cancellationToken=default)=>throw new AssertFailedException("Direct upload must not open Server browser streams.");
    }
    private sealed class ConcurrentJs:IJSRuntime,IJSObjectReference
    {
        public TaskCompletionSource Five{get;}=Signal();public TaskCompletionSource Release{get;}=Signal();
        public int MaximumPuts{get;private set;}public int MaximumHashes{get;private set;}public int Puts{get;private set;}
        private int activePuts,activeHashes;
        public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>InvokeAsync<T>(identifier,CancellationToken.None,args);
        public async ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args)
        {
            if(identifier=="import")return (T)(object)this;
            if(identifier=="hashFile")
            {
                MaximumHashes=Math.Max(MaximumHashes,++activeHashes);await Task.Yield();activeHashes--;return (T)(object)new string('a',64);
            }
            if(identifier=="putFile")
            {
                Puts++;MaximumPuts=Math.Max(MaximumPuts,++activePuts);
                var receiver=(DotNetObjectReference<CaseFileUpload>)args![4]!;
                await receiver.Value.UploadProgress((string)args[3]!,"upload",1024*1024,FileUploadLimits.MaxRawFileBytes,1024*1024);
                if(activePuts==5)Five.TrySetResult();await Release.Task;
                await receiver.Value.UploadProgress((string)args[3]!,"response",FileUploadLimits.MaxRawFileBytes,FileUploadLimits.MaxRawFileBytes);
                activePuts--;return (T)(object)new CaseFileUpload.TransferTimings(1000,250);
            }
            return default!;
        }
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}