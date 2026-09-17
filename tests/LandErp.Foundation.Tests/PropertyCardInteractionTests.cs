using LandErp.Server.Components.Procurement;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class PropertyCardInteractionTests
{
    [TestMethod]
    [DataRow("2026-09-18T11:29")]
    [DataRow("2026-09-18T11:29:00")]
    [DataRow("2026-09-18T11:29:00.000")]
    public void BrowserDateVariantsRepresentSameMoscowInstant(string value)
        => Assert.AreEqual(new DateTimeOffset(2026,9,18,8,29,0,TimeSpan.Zero),CaseFormValues.ParseLocal(value));

    [TestMethod]
    public void OptionalDateRemainsAbsent()=>Assert.IsNull(CaseFormValues.ParseLocal(""));

    [TestMethod]
    public void InvalidDateIsRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(()=>CaseFormValues.ParseLocal("2026-02-30T12:00"));
    }

    [TestMethod]
    public async Task RefreshFailureDoesNotTurnConfirmedWriteIntoFailure()
    {
        int writes=0;
        bool refreshed=await CaseOperation.SaveAndRefreshAsync(()=>{writes++;return Task.CompletedTask;},()=>throw new IOException());
        Assert.IsFalse(refreshed);
        Assert.AreEqual(1,writes);
    }

    [TestMethod]
    public async Task WriteFailureDoesNotRefreshOrReportSuccess()
    {
        bool read=false;
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>CaseOperation.SaveAndRefreshAsync(()=>throw new ArgumentException("invalid"),()=>{read=true;return Task.CompletedTask;}));
        Assert.IsFalse(read);
    }

    [TestMethod]
    public async Task SuccessfulWriteRefreshesOnce()
    {
        int reads=0;
        Assert.IsTrue(await CaseOperation.SaveAndRefreshAsync(()=>Task.CompletedTask,()=>{reads++;return Task.CompletedTask;}));
        Assert.AreEqual(1,reads);
    }

    [TestMethod]
    public async Task ModalRendersOperationErrorInsideTheDialog()
    {
        await using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,UnusedJs>().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        string html=await renderer.Dispatcher.InvokeAsync(async()=>{
            var result=await renderer.RenderComponentAsync<CaseModal>(ParameterView.FromDictionary(new Dictionary<string,object?>
            { ["Title"]="Документ",["Error"]="Выберите файл",["Busy"]=true }));
            return result.ToHtmlString();
        });
        StringAssert.Contains(html,"role=\"alert\"");
        StringAssert.Contains(html,"<fieldset disabled");
        Assert.IsTrue(html.IndexOf("role=\"alert\"",StringComparison.Ordinal)<html.IndexOf("</dialog>",StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task FreshModalHasNoErrorBanner()
    {
        await using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,UnusedJs>().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        string html=await renderer.Dispatcher.InvokeAsync(async()=>
            (await renderer.RenderComponentAsync<CaseModal>()).ToHtmlString());
        Assert.IsFalse(html.Contains("role=\"alert\"",StringComparison.Ordinal));
    }

    private sealed class UnusedJs:IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>throw new InvalidOperationException("Static rendering must not invoke JavaScript.");
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken cancellationToken,object?[]? args)=>InvokeAsync<TValue>(identifier,args);
    }
}
