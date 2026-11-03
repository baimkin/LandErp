using System.Net;
using System.Text.RegularExpressions;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Server.Components.Procurement;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace LandErp.Foundation.Tests;
[TestClass]
public sealed class InspectionMediaLayoutTests
{
    private static readonly AttachmentView[] Files=Enumerable.Range(0,20).Select(i=>new AttachmentView(Guid.NewGuid(),CaseAttachmentOwner.Inspection,Guid.NewGuid(),i%3==0?CaseAttachmentKind.Video:CaseAttachmentKind.Photo,
        $"Кадр {i}","","Осмотр",$"file-{i}",i%3==0?"video/mp4":"image/png",1024,StoredFileStatus.Available,false,DateTimeOffset.UnixEpoch,null)).ToArray();
    [TestMethod]
    public async Task TwentyFilesRenderOneLargePhotoOneActionBarAndSafeOriginalText()
    {
        await using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,NoBrowser>().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            const string description="Подъезд\n\nАсфальт до участка.\n<script>alert('x')</script>";
            var view=await renderer.RenderComponentAsync<InspectionMediaOverview>(ParameterView.FromDictionary(new Dictionary<string,object?>{["Files"]=Files,["Description"]=description,["CanDelete"]=true}));
            string html=view.ToHtmlString(),decoded=WebUtility.HtmlDecode(html);
            Assert.AreEqual(20,Regex.Count(html,"class=\"featured-thumb\""));
            Assert.AreEqual(0,Regex.Count(html,"class=\"media-card\""));
            Assert.AreEqual(1,Regex.Count(decoded,">Скачать</a>"));
            StringAssert.Contains(decoded,"Открыть: Кадр 1");
            StringAssert.Contains(decoded,description);
            Assert.IsFalse(html.Contains("<script>",StringComparison.Ordinal));
            StringAssert.Contains(decoded,"Описание осмотра");StringAssert.Contains(decoded,"Удалить выбранный материал");
        });
    }
    [TestMethod]
    public async Task NonInspectionGalleryKeepsGridAndReadOnlyModeHasNoDelete()
    {
        await using var services=new ServiceCollection().AddLogging().AddSingleton<IJSRuntime,NoBrowser>().BuildServiceProvider();
        await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
        await renderer.Dispatcher.InvokeAsync(async()=>{
            var view=await renderer.RenderComponentAsync<AttachmentGallery>(ParameterView.FromDictionary(new Dictionary<string,object?>{["Files"]=Files.Take(2).ToArray()}));
            string html=WebUtility.HtmlDecode(view.ToHtmlString());
            Assert.AreEqual(2,Regex.Count(html,"class=\"media-card\""));
            Assert.IsFalse(html.Contains("Удалить",StringComparison.Ordinal));
            Assert.IsFalse(html.Contains("featured-thumbnails",StringComparison.Ordinal));
        });
    }
    private sealed class NoBrowser:IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>throw new JSDisconnectedException("Static render only.");
        public ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args)=>InvokeAsync<T>(identifier,args);
    }
}