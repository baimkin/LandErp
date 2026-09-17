using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandErp.Application.Foundation.Files;
using LandErp.Infrastructure.Foundation.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("FileStorage")]
public sealed class YandexDiskStorageTests
{
    internal static YandexDiskOptions Options() => new()
    {
        Token = "synthetic-token-do-not-log", ConnectionId = "ac3a294b766d4eceaf8ff32f118973b2", Root = "app:/LandErp/test"
    };

    [TestMethod]
    public async Task UploadCreatesCaseFoldersReadsBytesAndNeverSendsTokenToTransferHost()
    {
        using DiskHandler handler = new(); using HttpClient http = new(handler);
        using YandexDiskFileStorage storage = new(Options(), http);
        FileWriteRequest request = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Photo", "image/png");
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3];
        FileWriteResult result = await storage.WriteAsync(request, bytes, CancellationToken.None);
        StringAssert.Contains(result.StorageKey, $"Objects/{request.OrganizationId:N}/{request.CaseId:N}/Photos/");
        StringAssert.EndsWith(result.StorageKey, ".png");
        CollectionAssert.AreEqual(bytes, await storage.ReadAsync(result.StorageKey, CancellationToken.None));
        Assert.IsTrue(handler.Directories.Contains("app:/LandErp/test"));
        Assert.AreEqual(1, handler.UploadCount);
        Assert.AreEqual(result, await storage.WriteAsync(request, bytes, CancellationToken.None));
        Assert.AreEqual(1, handler.UploadCount, "An acknowledgement retry must reuse verified content.");
        FileWriteResult changed = await storage.WriteAsync(request, new byte[] { 4, 5 }, CancellationToken.None);
        Assert.AreNotEqual(result.StorageKey, changed.StorageKey);
        CollectionAssert.AreEqual(bytes, await storage.ReadAsync(result.StorageKey, CancellationToken.None));
    }

    [TestMethod]
    public async Task LostUploadAcknowledgementCanBeRetriedWithoutDuplicateOrOverwrite()
    {
        using DiskHandler handler = new() { LoseUploadResponse = true }; using HttpClient http = new(handler);
        using YandexDiskFileStorage storage = new(Options(), http);
        Guid id = Guid.NewGuid(); byte[] bytes = [1, 2, 3];
        FileStorageException error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.WriteAsync(id, bytes, CancellationToken.None));
        Assert.IsTrue(error.Retryable);
        Assert.IsFalse(error.ToString().Contains("signed-secret", StringComparison.Ordinal));
        FileWriteResult recovered = await storage.WriteAsync(id, bytes, CancellationToken.None);
        CollectionAssert.AreEqual(bytes, await storage.ReadAsync(recovered.StorageKey, CancellationToken.None));
        Assert.AreEqual(1, handler.UploadCount);
    }

    [TestMethod]
    public async Task TraversalWrongConnectionAndOversizeNeverReachNetwork()
    {
        using DiskHandler handler = new(); using HttpClient http = new(handler);
        YandexDiskOptions options = Options(); options.MaxFileBytes = 3;
        using YandexDiskFileStorage storage = new(options, http);
        foreach (string key in new[] { "disk:/personal/photo.jpg", "yd1:other:Checks/photo.jpg", $"yd1:{options.ConnectionId}:../../personal" })
            await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.ReadAsync(key, CancellationToken.None));
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => storage.WriteAsync(Guid.NewGuid(), new byte[4], CancellationToken.None));
        Assert.AreEqual(0, handler.RequestCount);
        options.Root = "app:/LandErp/../personal";
        Assert.ThrowsExactly<InvalidOperationException>(options.Validate);
    }

    [TestMethod]
    public async Task UnsafeUploadLinkAndDownloadRedirectAreRejected()
    {
        using DiskHandler handler = new() { TransferHost = "127.0.0.1" }; using HttpClient http = new(handler);
        using YandexDiskFileStorage storage = new(Options(), http);
        FileStorageException invalid = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.WriteAsync(Guid.NewGuid(), new byte[] { 1 }, CancellationToken.None));
        Assert.AreEqual("STORAGE_LINK_INVALID", invalid.Code); Assert.AreEqual(0, handler.UploadCount);
        handler.TransferHost = "storage.yandex.net";
        FileWriteResult result = await storage.WriteAsync(Guid.NewGuid(), new byte[] { 1 }, CancellationToken.None);
        handler.RedirectDownload = "https://yandex.net.evil.invalid/private?signed-secret=yes";
        invalid = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.ReadAsync(result.StorageKey, CancellationToken.None));
        Assert.AreEqual("STORAGE_LINK_INVALID", invalid.Code);
    }

    [TestMethod]
    public async Task ChangedFileAndOversizedResponseAreRejected()
    {
        using DiskHandler handler = new(); using HttpClient http = new(handler);
        YandexDiskOptions options = Options(); options.MaxFileBytes = 3;
        using YandexDiskFileStorage storage = new(options, http);
        FileWriteResult result = await storage.WriteAsync(Guid.NewGuid(), new byte[] { 1, 2, 3 }, CancellationToken.None);
        handler.DownloadOverride = [9, 9, 9];
        FileStorageException error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.ReadAsync(result.StorageKey, CancellationToken.None));
        Assert.AreEqual("STORAGE_INTEGRITY", error.Code);
        handler.DownloadOverride = new byte[4];
        error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.ReadAsync(result.StorageKey, CancellationToken.None));
        Assert.AreEqual("STORAGE_SIZE", error.Code);
    }

    [TestMethod]
    public async Task RateLimitRetriesAreBoundedAndErrorsDoNotExposeProviderBody()
    {
        using DiskHandler handler = new() { ApiFailure = HttpStatusCode.TooManyRequests }; using HttpClient http = new(handler);
        using YandexDiskFileStorage storage = new(Options(), http);
        FileStorageException error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.WriteAsync(Guid.NewGuid(), new byte[] { 1 }, CancellationToken.None));
        Assert.AreEqual("STORAGE_BUSY", error.Code); Assert.IsTrue(error.Retryable); Assert.AreEqual(3, handler.RequestCount);
        handler.ApiFailure = HttpStatusCode.Unauthorized;
        error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.WriteAsync(Guid.NewGuid(), new byte[] { 1 }, CancellationToken.None));
        Assert.AreEqual("STORAGE_AUTH", error.Code); Assert.IsFalse(error.Retryable); Assert.AreEqual(4, handler.RequestCount);
        Assert.IsFalse(error.ToString().Contains("signed-secret", StringComparison.Ordinal));
        handler.ApiFailure = HttpStatusCode.InsufficientStorage;
        error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => storage.WriteAsync(Guid.NewGuid(), new byte[] { 1 }, CancellationToken.None));
        Assert.AreEqual("STORAGE_QUOTA", error.Code);
    }

    [TestMethod]
    public async Task CancellationPropagatesWithoutProviderError()
    {
        using DiskHandler handler = new(); using HttpClient http = new(handler);
        using YandexDiskFileStorage storage = new(Options(), http);
        using CancellationTokenSource source = new(); await source.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => storage.WriteAsync(Guid.NewGuid(), new byte[] { 1 }, source.Token));
        Assert.AreEqual(0, handler.RequestCount);
    }

    [TestMethod]
    public async Task LocalLegacyReadSurvivesCloudSelectionAndDisconnectHasNoFallback()
    {
        string directory = Path.Combine(Path.GetTempPath(), "landerp-storage-" + Guid.NewGuid().ToString("N"));
        FileSystemFileStorage local = new(directory);
        FileWriteResult legacy = await local.WriteAsync(Guid.NewGuid(), new byte[] { 7, 8 }, CancellationToken.None);
        try
        {
            using DiskHandler handler = new(); using HttpClient http = new(handler);
            using YandexDiskFileStorage cloud = new(Options(), http);
            RoutedFileStorage routed = new(cloud, cloud, local);
            CollectionAssert.AreEqual(new byte[] { 7, 8 }, await routed.ReadAsync(legacy.StorageKey, CancellationToken.None));
            Assert.AreEqual(0, handler.RequestCount);
            FileWriteResult remote = await routed.WriteAsync(Guid.NewGuid(), new byte[] { 1 }, CancellationToken.None);
            RoutedFileStorage disconnected = new(local, null, local);
            FileStorageException error = await Assert.ThrowsExactlyAsync<FileStorageException>(() => disconnected.ReadAsync(remote.StorageKey, CancellationToken.None));
            Assert.AreEqual("STORAGE_DISCONNECTED", error.Code);
        }
        finally { File.Delete(Path.Combine(directory, legacy.StorageKey)); Directory.Delete(directory); }
    }

    [TestMethod]
    public void ProductionCannotSilentlyUseLocalDiskOrMissingToken()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Root"] = "unused" }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddLandErpFileStorage(configuration, new TestEnvironment()));
        configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Storage:Provider"] = "YandexDisk" }).Build();
        Assert.ThrowsExactly<InvalidOperationException>(() => services.AddLandErpFileStorage(configuration, new TestEnvironment()));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = "unused";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    internal sealed class DiskHandler : HttpMessageHandler
    {
        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public int UploadCount { get; private set; }
        public int RequestCount { get; private set; }
        public bool LoseUploadResponse { get; set; }
        public string TransferHost { get; set; } = "storage.yandex.net";
        public string? RedirectDownload { get; set; }
        public byte[]? DownloadOverride { get; set; }
        public HttpStatusCode? ApiFailure { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            Uri uri = request.RequestUri!;
            string path = Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&')[0].Split('=', 2)[1]);
            if (uri.Host == "cloud-api.yandex.net")
            {
                Assert.AreEqual("OAuth", request.Headers.Authorization?.Scheme);
                Assert.AreEqual(Options().Token, request.Headers.Authorization?.Parameter);
                StringAssert.StartsWith(path, "app:/LandErp");
                if (ApiFailure != null)
                {
                    HttpResponseMessage failure = Json(new { error = "signed-secret" }, ApiFailure.Value);
                    failure.Headers.RetryAfter = new(TimeSpan.Zero); return failure;
                }
                if (uri.AbsolutePath.EndsWith("/upload", StringComparison.Ordinal))
                {
                    StringAssert.Contains(uri.Query, "overwrite=false");
                    return Json(new { href = $"https://{TransferHost}/upload?path={Uri.EscapeDataString(path)}", method = "PUT" });
                }
                if (uri.AbsolutePath.EndsWith("/download", StringComparison.Ordinal))
                    return Json(new { href = $"https://{TransferHost}/download?path={Uri.EscapeDataString(path)}", method = "GET" });
                if (request.Method == HttpMethod.Put)
                    return new(Directories.Add(path) ? HttpStatusCode.Created : HttpStatusCode.Conflict);
                if (Directories.Contains(path)) return Json(new { type = "dir" });
                if (Files.TryGetValue(path, out byte[]? bytes))
                    return Json(new { type = "file", size = bytes.Length, sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
                return new(HttpStatusCode.NotFound);
            }
            Assert.AreEqual("storage.yandex.net", uri.Host);
            Assert.IsNull(request.Headers.Authorization, "OAuth must never reach a signed transfer URL.");
            if (request.Method == HttpMethod.Put)
            {
                UploadCount++; Files.Add(path, await request.Content!.ReadAsByteArrayAsync(cancellationToken));
                if (LoseUploadResponse) { LoseUploadResponse = false; throw new HttpRequestException("signed-secret"); }
                return new(HttpStatusCode.Created);
            }
            if (RedirectDownload != null)
            {
                HttpResponseMessage redirect = new(HttpStatusCode.Found); redirect.Headers.Location = new(RedirectDownload); return redirect;
            }
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(DownloadOverride ?? Files[path]) };
        }

        private static HttpResponseMessage Json(object value, HttpStatusCode status = HttpStatusCode.OK) =>
            new(status) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
