using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Server.Foundation;
using LandErp.Infrastructure.Foundation.Files;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
namespace LandErp.Foundation.Tests;
[TestClass]
public sealed class MediaGalleryTests
{
    [TestMethod]
    public async Task CloudStreamsExactRangesWithoutOAuthTransferAndRejectsIgnoredRange()
    {
        using var handler=new YandexDiskStorageTests.DiskHandler();using var http=new HttpClient(handler);
        using var storage=new YandexDiskFileStorage(YandexDiskStorageTests.Options(),http);
        byte[] data=Enumerable.Range(0,1024).Select(x=>(byte)x).ToArray();
        var result=await storage.WriteAsync(new FileWriteRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"Video","video/mp4"),data,CancellationToken.None);
        await using(var stream=await storage.OpenReadAsync(result.StorageKey,data.Length,result.Sha256,new(128,256),CancellationToken.None))
        {
            Assert.IsFalse(stream.CanSeek);using var buffer=new MemoryStream();await stream.CopyToAsync(buffer);
            CollectionAssert.AreEqual(data[128..384],buffer.ToArray());
        }
        await Assert.ThrowsExactlyAsync<FileStorageException>(()=>storage.OpenReadAsync(result.StorageKey,data.Length,new string('0',64),null,CancellationToken.None));
        handler.IgnoreRange=true;
        await Assert.ThrowsExactlyAsync<FileStorageException>(()=>storage.OpenReadAsync(result.StorageKey,data.Length,result.Sha256,new(0,2),CancellationToken.None));
    }
    [TestMethod]
    public async Task BoundedMediaStreamStopsAtRangeAndDisposesOwnerAndRejectsTruncation()
    {
        using var owner=new Owner();
        await using(var stream=new FileReadStream(new MemoryStream(new byte[1024]),16,owner))
        {
            byte[] chunk=new byte[65536];Assert.AreEqual(16,await stream.ReadAsync(chunk));Assert.AreEqual(0,await stream.ReadAsync(chunk));
        }
        Assert.IsTrue(owner.Disposed);
        await using var truncated=new FileReadStream(new MemoryStream(new byte[1]),2);
        Assert.AreEqual(1,await truncated.ReadAsync(new byte[2]));
        await Assert.ThrowsExactlyAsync<FileStorageException>(async()=>Assert.AreEqual(0,await truncated.ReadAsync(new byte[2])));
        Assert.IsFalse(AttachmentMediaTypes.IsPreviewable("image/svg+xml"));Assert.IsFalse(AttachmentMediaTypes.IsPreviewable("text/html"));
    }
    [TestMethod,TestCategory("PostgreSQL")]
    public async Task ActualMediaEndpointPreservesAuthorizationAndRangesAndDeletedHistory()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        var workspace=new LandErp.Infrastructure.Modules.Procurement.ProcurementWorkspace(f.Factory,TimeProvider.System,new FileSystemFileStorage(Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","media-gallery","endpoint",Guid.NewGuid().ToString("N"))));
        Guid id=await f.InsertIndependentCaseAsync("Gallery range test");
        byte[] data=Enumerable.Range(0,1024).Select(x=>(byte)x).ToArray();
        Guid file=await workspace.AddAttachmentAsync(f.Manager,new(id,CaseAttachmentOwner.Case,null,CaseAttachmentKind.Video,"Панорама","","panorama.mp4","video/mp4",data,null),"gallery",CancellationToken.None);
        var builder=WebApplication.CreateSlimBuilder();builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IProcurementWorkspace>(workspace);builder.Services.AddSingleton<ICatalogWorkspace>(workspace);
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication("test").AddCookie("test",o=>o.Events.OnRedirectToLogin=c=>{c.Response.StatusCode=401;return Task.CompletedTask;});
        builder.Services.AddAuthorization();builder.Services.AddAntiforgery();builder.Services.AddProblemDetails();builder.Services.AddExceptionHandler<SafeExceptionHandler>();
        await using var app=builder.Build();app.UseExceptionHandler();app.UseAuthentication();app.UseAuthorization();
        app.MapGet("/login/{foreign:bool}",async(bool foreign,HttpContext httpContext)=>{
            var subject=foreign?f.ForeignOwner:f.Manager;
            await httpContext.SignInAsync("test",new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,subject.UserId.ToString()),new Claim("amr",subject.MultiFactorAuthenticated?"mfa":"pwd")],"test")));
        });
        typeof(SafeExceptionHandler).Assembly.GetType("LandErp.Server.Foundation.ProcurementEndpoints")!.GetMethod("MapProcurementEndpoints",BindingFlags.Static|BindingFlags.Public)!.Invoke(null,[app]);
        await app.StartAsync();using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};
        string route=$"/api/procurement/attachments/{file}/media";
        using(var response=await client.GetAsync(route))Assert.AreEqual(HttpStatusCode.Unauthorized,response.StatusCode);
        await client.GetAsync("/login/false");
        using(var response=await client.GetAsync(route))
        {
            Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);Assert.AreEqual("video/mp4",response.Content.Headers.ContentType!.MediaType);
            Assert.IsTrue(response.Headers.CacheControl!.NoStore);Assert.IsNull(response.Content.Headers.ContentDisposition);
            CollectionAssert.AreEqual(data,await response.Content.ReadAsByteArrayAsync());
        }
        foreach(var range in new[]{("bytes=128-383",data[128..384]),("bytes=-2",data[^2..]),("bytes=1023-",data[^1..])})
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,route);request.Headers.Add("Range",range.Item1);
            using var response=await client.SendAsync(request);Assert.AreEqual(HttpStatusCode.PartialContent,response.StatusCode);
            Assert.AreEqual((long)data.Length,response.Content.Headers.ContentRange!.Length);CollectionAssert.AreEqual(range.Item2,await response.Content.ReadAsByteArrayAsync());
        }
        foreach(string range in new[]{"bytes=2048-","bytes=0-2,5-8","bytes=-0","invalid"})
        {
            using var request=new HttpRequestMessage(HttpMethod.Get,route);request.Headers.TryAddWithoutValidation("Range",range);
            using var response=await client.SendAsync(request);Assert.AreEqual(HttpStatusCode.RequestedRangeNotSatisfiable,response.StatusCode);
        }
        await client.GetAsync("/login/true");using(var response=await client.GetAsync(route))Assert.AreEqual(HttpStatusCode.Forbidden,response.StatusCode);
        await client.GetAsync("/login/false");await workspace.DeleteAttachmentAsync(f.Manager,new(id,file),"soft-delete",CancellationToken.None);
        using(var response=await client.GetAsync(route))Assert.AreEqual(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await client.GetAsync($"/api/procurement/attachments/{file}")){Assert.AreEqual(HttpStatusCode.OK,response.StatusCode);CollectionAssert.AreEqual(data,await response.Content.ReadAsByteArrayAsync());}
        await app.StopAsync();
    }
    private sealed class Owner:IDisposable{public bool Disposed{get;private set;}public void Dispose()=>Disposed=true;}
}