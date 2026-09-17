using System.Text;
using LandErp.Application.Foundation.Files;
using LandErp.Infrastructure.Foundation.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("LiveStorage")]
public sealed class YandexDiskLiveTests
{
    [TestMethod]
    public async Task PrivateAppFolderUploadReadAndIdempotentRetry()
    {
        if (Environment.GetEnvironmentVariable("LANDERP_YANDEX_LIVE_TEST") != "1")
            Assert.Inconclusive("Opt-in live test: use Test-YandexDiskConnection.ps1.");
        YandexDiskOptions options = new()
        {
            Token = Environment.GetEnvironmentVariable("Storage__YandexDisk__Token") ?? "",
            ConnectionId = Environment.GetEnvironmentVariable("Storage__YandexDisk__ConnectionId") ?? "",
            Root = Environment.GetEnvironmentVariable("Storage__YandexDisk__Root") ?? "app:/LandErp/development"
        };
        using YandexDiskFileStorage storage = new(options);
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        byte[][] samples = [Encoding.UTF8.GetBytes("LandErp storage connection check. Synthetic data only."),
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jhKsAAAAASUVORK5CYII=")];
        for (int index = 0; index < samples.Length; index++)
        {
            FileWriteRequest request = new(Guid.NewGuid(), Guid.Empty, Guid.Empty, "Export", index == 0 ? "text/plain" : "image/png");
            FileWriteResult uploaded = await storage.WriteAsync(request, samples[index], timeout.Token);
            FileWriteResult retried = await storage.WriteAsync(request, samples[index], timeout.Token);
            Assert.AreEqual(uploaded, retried);
            CollectionAssert.AreEqual(samples[index], await storage.ReadAsync(uploaded.StorageKey, timeout.Token));
        }
        // No cloud deletion: synthetic samples remain under the dedicated Checks folder for inspection.
    }
}
