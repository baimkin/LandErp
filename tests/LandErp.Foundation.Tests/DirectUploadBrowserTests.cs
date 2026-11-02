using System.Security.Cryptography;
using System.Text.Json;
using LandErp.Application.Foundation.Files;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class DirectUploadBrowserTests
{
    [TestMethod, TestCategory("Browser")]
    public async Task NativeFiveHundredMiBFileHashesAndUploadsAcrossOriginsWithProgressAndCancellation()
    {
        string root = FindRoot();
        string scratch = Path.Combine(root, "artifacts", "media-large-upload", "browser");
        Directory.CreateDirectory(scratch);
        string path = Path.Combine(scratch, Guid.NewGuid().ToString("N") + ".mp4");
        // A native sparse test file, never a 500 MiB managed byte array or base64 payload.
        await using (var file = File.Create(path)) file.SetLength(FileUploadLimits.MaxRawFileBytes);
        try
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0").ConfigureKestrel(k => k.Limits.MaxRequestBodySize = (long)FileUploadLimits.MaxRawFileBytes + 1);
            await using var app = builder.Build();
            string origin = ""; long received = 0; string? providerHash = null; int puts = 0;
            app.Run(async http =>
            {
                string route = http.Request.Path.Value!;
                if (route == "/fixture")
                {
                    http.Response.ContentType = "text/html";
                    await http.Response.WriteAsync("<!doctype html><input id='file' type='file'><script type='module'>import * as upload from '/js/case-upload.js';window.upload=upload;</script>");
                    return;
                }
                if (route is "/js/case-upload.js" or "/js/file-hash-worker.js" or "/js/sha256.mjs")
                {
                    http.Response.ContentType = "text/javascript";
                    await http.Response.SendFileAsync(Path.Combine(root, "src/LandErp.Server/wwwroot", route.TrimStart('/')));
                    return;
                }
                http.Response.Headers.AccessControlAllowOrigin = origin;
                http.Response.Headers.AccessControlAllowMethods = "PUT";
                http.Response.Headers.AccessControlAllowHeaders = "content-type";
                if (http.Request.Method == "OPTIONS") { http.Response.StatusCode = 204; return; }
                if (http.Request.Method != "PUT") { http.Response.StatusCode = 405; return; }
                Interlocked.Increment(ref puts);
                Assert.IsFalse(http.Request.Headers.ContainsKey("Authorization"));
                Assert.IsFalse(http.Request.Headers.ContainsKey("Cookie"));
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] chunk = new byte[64 * 1024]; int read;
                while ((read = await http.Request.Body.ReadAsync(chunk)) > 0) { sha.AppendData(chunk, 0, read); received += read; }
                providerHash = Convert.ToHexStringLower(sha.GetHashAndReset());
                http.Response.StatusCode = 201;
            });
            await app.StartAsync(); origin = app.Urls.Single();
            string target = new UriBuilder(origin) { Host = "localhost", Path = "/upload" }.Uri.AbsoluteUri;
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Channel = "msedge", Headless = true, ChromiumSandbox = true, Timeout = 30000 });
            await using var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync(); page.SetDefaultTimeout(240000);
            await page.GotoAsync(origin + "/fixture"); await page.WaitForFunctionAsync("() => !!window.upload");
            await page.Locator("#file").SetInputFilesAsync(path);
            var result = await page.EvaluateAsync<JsonElement>("""
                async ({target,maxBytes}) => {
                    const element=document.querySelector('#file'),events=[];
                    const receiver={invokeMethodAsync:async (_,id,phase,loaded,total)=>events.push({phase,loaded,total})};
                    const hash=await upload.hashFile(element,0,'large',receiver,maxBytes);
                    await upload.putFile(element,0,target,'large',receiver);upload.release('large');
                    let cancelled=false;
                    const pending=upload.hashFile(element,0,'cancel',receiver,maxBytes);
                    upload.cancel('cancel');try{await pending;}catch{cancelled=true;}upload.release('cancel');
                    return {hash,cancelled,hashProgress:events.some(e=>e.phase==='hash'&&e.loaded===maxBytes),
                       uploadProgress:events.some(e=>e.phase==='upload'&&e.loaded===maxBytes)};
                }
                """, new { target, maxBytes = FileUploadLimits.MaxRawFileBytes });
            Assert.AreEqual((long)FileUploadLimits.MaxRawFileBytes, received);
            Assert.AreEqual(providerHash, result.GetProperty("hash").GetString());
            Assert.IsTrue(result.GetProperty("hashProgress").GetBoolean());
            Assert.IsTrue(result.GetProperty("uploadProgress").GetBoolean());
            Assert.IsTrue(result.GetProperty("cancelled").GetBoolean());
            Assert.AreEqual(1, puts, "Cancelling hashing must not start a PUT.");
            await using (var oversized = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) oversized.SetLength((long)FileUploadLimits.MaxRawFileBytes + 1);
            await page.Locator("#file").SetInputFilesAsync(path);
            Assert.IsTrue(await page.EvaluateAsync<bool>("""
                async maxBytes=>{try{await upload.hashFile(document.querySelector('#file'),0,'oversize',{invokeMethodAsync:async()=>{}},maxBytes);return false;}catch{return true;}}
                """, FileUploadLimits.MaxRawFileBytes));
            Assert.AreEqual(1, puts);
            await app.StopAsync();
        }
        finally
        {
            // Only this exact task-created GUID file, verified beneath the task's artifact directory.
            Assert.AreEqual(Path.GetFullPath(scratch), Path.GetDirectoryName(Path.GetFullPath(path)));
            File.Delete(path);
        }
    }
    private static string FindRoot()
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "LandErp.slnx"))) return d.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
}