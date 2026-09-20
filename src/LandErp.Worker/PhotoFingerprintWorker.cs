using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace LandErp.Worker;

public sealed class PhotoFingerprintWorker(
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<PhotoFingerprintWorker> logger) : BackgroundService
{
    private const int RecentBatchSize = 8;
    private const int BackfillBatchSize = 16;
    private const int MaximumPhotosPerListing = 30;
    private const int MaximumImageBytes = 8 * 1024 * 1024;
    private const long MaximumPixels = 40_000_000;
    private static readonly Action<ILogger, Guid, string, Exception?> LogPhotoFailure =
        LoggerMessage.Define<Guid, string>(LogLevel.Warning, new EventId(21, "PHOTO_FINGERPRINT_FAILED"),
            "Photo fingerprint failed for catalog item {CatalogItemId}; code {Code}");
    private static readonly Action<ILogger, Exception?> LogCycleFailure =
        LoggerMessage.Define(LogLevel.Error, new EventId(22, "PHOTO_FINGERPRINT_WORKER_FAILED"),
            "Photo fingerprint worker cycle failed");
    private int backfillOffset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            bool work = false;
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                IDbContextFactory<LandErpDbContext> factory =
                    scope.ServiceProvider.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
                IIncomingDuplicateMatchingMaintenance matcher =
                    scope.ServiceProvider.GetRequiredService<IIncomingDuplicateMatchingMaintenance>();

                await using LandErpDbContext read = await factory.CreateDbContextAsync(stoppingToken);
                Guid[] recent = await read.Listings.AsNoTracking()
                    .Where(item => item.Disposition == CatalogDisposition.Incoming)
                    .OrderByDescending(item => item.ChangedAt).ThenByDescending(item => item.Id)
                    .Take(RecentBatchSize).Select(item => item.Id).ToArrayAsync(stoppingToken);
                Guid[] backfill = await read.Listings.AsNoTracking()
                    .Where(item => item.Disposition == CatalogDisposition.Incoming)
                    .OrderBy(item => item.ReceivedAt).ThenBy(item => item.Id)
                    .Skip(backfillOffset).Take(BackfillBatchSize)
                    .Select(item => item.Id).ToArrayAsync(stoppingToken);

                if (backfill.Length == 0) backfillOffset = 0;
                else backfillOffset += backfill.Length;

                HashSet<Guid> backfillIds = backfill.ToHashSet();
                foreach (Guid id in recent.Concat(backfill).Distinct())
                {
                    bool changed = await ProcessListingAsync(factory, id, stoppingToken);
                    if (changed || backfillIds.Contains(id))
                        await matcher.RefreshAsync(id, stoppingToken);
                    work |= changed;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { LogCycleFailure(logger, exception); }

            await Task.Delay(work ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    private async Task<bool> ProcessListingAsync(
        IDbContextFactory<LandErpDbContext> factory, Guid listingId, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        Listing? listing = await db.Listings.SingleOrDefaultAsync(item => item.Id == listingId, cancellationToken);
        if (listing == null || listing.Disposition != CatalogDisposition.Incoming) return false;

        string[] urls = PhotoUrls(listing.PhotosJson).Take(MaximumPhotosPerListing).ToArray();
        DateTimeOffset now = time.GetUtcNow();
        CatalogPhotoFingerprint[] allRows = await db.CatalogPhotoFingerprints
            .Where(item => item.ListingId == listing.Id)
            .ToArrayAsync(cancellationToken);

        if (urls.Length == 0)
        {
            if (allRows.Length == 0) return false;
            bool hadReady = allRows.Any(item => item.Status == PhotoFingerprintStatus.Ready);
            db.CatalogPhotoFingerprints.RemoveRange(allRows);
            await db.SaveChangesAsync(cancellationToken);
            return hadReady;
        }

        string[] urlHashes = urls.Select(HashUrl).ToArray();
        HashSet<string> currentHashes = urlHashes.ToHashSet(StringComparer.Ordinal);
        CatalogPhotoFingerprint[] staleRows = allRows.Where(item => !currentHashes.Contains(item.UrlHash)).ToArray();
        if (staleRows.Length > 0) db.CatalogPhotoFingerprints.RemoveRange(staleRows);
        Dictionary<string, CatalogPhotoFingerprint> byHash = allRows
            .Where(item => currentHashes.Contains(item.UrlHash))
            .ToDictionary(item => item.UrlHash, StringComparer.Ordinal);

        bool changed = staleRows.Length > 0;
        bool matchingChanged = staleRows.Any(item => item.Status == PhotoFingerprintStatus.Ready);
        for (int index = 0; index < urls.Length; index++)
        {
            string url = urls[index];
            string urlHash = urlHashes[index];
            byHash.TryGetValue(urlHash, out CatalogPhotoFingerprint? row);

            if (row is { Status: PhotoFingerprintStatus.Ready or PhotoFingerprintStatus.Unsupported })
            {
                if (row.PhotoIndex != index || row.SourceDataRevision != listing.DataRevision)
                {
                    row.PhotoIndex = index;
                    row.SourceDataRevision = listing.DataRevision;
                    row.UpdatedAt = now;
                    changed = true;
                    matchingChanged |= row.Status == PhotoFingerprintStatus.Ready;
                }
                continue;
            }

            if (row is { Status: PhotoFingerprintStatus.Retry, RetryAt: { } retryAt } && retryAt > now)
            {
                if (row.PhotoIndex != index || row.SourceDataRevision != listing.DataRevision)
                {
                    row.PhotoIndex = index;
                    row.SourceDataRevision = listing.DataRevision;
                    row.UpdatedAt = now;
                    changed = true;
                }
                continue;
            }

            FetchResult fetch = await FetchImageAsync(url, cancellationToken);
            row ??= new CatalogPhotoFingerprint
            {
                Id = DataConventions.NewId(),
                OrganizationId = listing.OrganizationId,
                ListingId = listing.Id,
                UrlHash = urlHash
            };
            if (db.Entry(row).State == EntityState.Detached) db.CatalogPhotoFingerprints.Add(row);

            row.PhotoIndex = index;
            row.SourceDataRevision = listing.DataRevision;
            row.UpdatedAt = now;
            if (fetch.Bytes != null)
            {
                try
                {
                    row.PerceptualHash = PerceptualHash(fetch.Bytes);
                    row.Status = PhotoFingerprintStatus.Ready;
                    row.FailureCount = 0;
                    row.RetryAt = null;
                    matchingChanged = true;
                }
                catch (InvalidDataException)
                {
                    row.PerceptualHash = null;
                    row.Status = PhotoFingerprintStatus.Unsupported;
                    row.FailureCount++;
                    row.RetryAt = null;
                    LogPhotoFailure(logger, listing.Id, "DECODE_UNSUPPORTED", null);
                }
            }
            else if (fetch.Permanent)
            {
                row.PerceptualHash = null;
                row.Status = PhotoFingerprintStatus.Unsupported;
                row.FailureCount++;
                row.RetryAt = null;
                LogPhotoFailure(logger, listing.Id, fetch.Code, null);
            }
            else
            {
                row.PerceptualHash = null;
                row.Status = PhotoFingerprintStatus.Retry;
                row.FailureCount++;
                row.RetryAt = now.Add(RetryDelay(row.FailureCount));
                LogPhotoFailure(logger, listing.Id, fetch.Code, null);
            }
            changed = true;
        }

        if (changed) await db.SaveChangesAsync(cancellationToken);
        return matchingChanged;
    }

    private static string[] PhotoUrls(string json)
    {
        try
        {
            return (JsonSerializer.Deserialize<string[]>(json) ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        }
        catch (JsonException) { return []; }
    }

    private static string HashUrl(string url) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)));

    private static TimeSpan RetryDelay(int failureCount) => failureCount switch
    {
        <= 1 => TimeSpan.FromMinutes(5),
        2 => TimeSpan.FromMinutes(30),
        3 => TimeSpan.FromHours(2),
        _ => TimeSpan.FromHours(12)
    };

    private static async Task<FetchResult> FetchImageAsync(string rawUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            return new(null, true, "URL_NOT_HTTPS");

        Uri current = uri;
        for (int redirect = 0; redirect <= 3; redirect++)
        {
            IPAddress[] addresses;
            try { addresses = await Dns.GetHostAddressesAsync(current.DnsSafeHost, cancellationToken); }
            catch (SocketException) { return new(null, false, "DNS_FAILED"); }
            IPAddress[] publicAddresses = addresses.Where(IsPublicAddress).ToArray();
            if (publicAddresses.Length == 0) return new(null, true, "PRIVATE_ADDRESS_BLOCKED");

            using SocketsHttpHandler handler = new()
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            handler.ConnectCallback = async (context, token) =>
            {
                Exception? last = null;
                foreach (IPAddress address in publicAddresses)
                {
                    Socket socket = new(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                    try
                    {
                        await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch (Exception exception) when (exception is SocketException or OperationCanceledException)
                    {
                        socket.Dispose();
                        last = exception;
                    }
                }
                throw new HttpRequestException("No validated endpoint was reachable.", last);
            };

            using HttpClient client = new(handler) { Timeout = TimeSpan.FromSeconds(12) };
            using HttpRequestMessage request = new(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("LandErp-PhotoFingerprint/1.0");
            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new(null, false, "DOWNLOAD_TIMEOUT");
            }
            catch (HttpRequestException)
            {
                return new(null, false, "DOWNLOAD_FAILED");
            }
            using (response)
            {
                if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
                {
                    if (response.Headers.Location == null || redirect == 3)
                        return new(null, true, "REDIRECT_INVALID");
                    current = response.Headers.Location.IsAbsoluteUri
                        ? response.Headers.Location
                        : new Uri(current, response.Headers.Location);
                    if (current.Scheme != Uri.UriSchemeHttps) return new(null, true, "REDIRECT_NOT_HTTPS");
                    continue;
                }

                if ((int)response.StatusCode is 408 or 425 or 429 || (int)response.StatusCode >= 500)
                    return new(null, false, "HTTP_RETRY");
                if (!response.IsSuccessStatusCode) return new(null, true, "HTTP_REJECTED");

                string? mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                    && !mediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
                    return new(null, true, "CONTENT_TYPE_UNSUPPORTED");
                if (response.Content.Headers.ContentLength is > MaximumImageBytes)
                    return new(null, true, "IMAGE_TOO_LARGE");

                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
                using MemoryStream target = new();
                byte[] buffer = new byte[81920];
                while (true)
                {
                    int read = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                    if (read == 0) break;
                    if (target.Length + read > MaximumImageBytes) return new(null, true, "IMAGE_TOO_LARGE");
                    target.Write(buffer, 0, read);
                }
                return new(target.ToArray(), true, "OK");
            }
        }
        return new(null, true, "REDIRECT_INVALID");
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;

        byte[] bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] != 0
                && bytes[0] != 10
                && bytes[0] != 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
                && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && bytes[0] < 224;
        }

        return !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal
            && (bytes[0] & 0xFE) != 0xFC;
    }

    internal static long PerceptualHash(byte[] bytes)
    {
        using SKData data = SKData.CreateCopy(bytes);
        using SKCodec? codec = SKCodec.Create(data);
        if (codec == null || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > MaximumPixels)
            throw new InvalidDataException("Unsupported image dimensions.");

        int longest = Math.Max(codec.Info.Width, codec.Info.Height);
        float decodeScale = Math.Min(1f, 256f / longest);
        SKSizeI scaled = codec.GetScaledDimensions(decodeScale);
        if (scaled.Width <= 0 || scaled.Height <= 0)
            throw new InvalidDataException("Image scaling is unsupported.");

        SKImageInfo decodeInfo = new(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKBitmap? decoded = SKBitmap.Decode(codec, decodeInfo);
        if (decoded == null) throw new InvalidDataException("Image decode failed.");

        SKImageInfo targetInfo = new(32, 32, SKColorType.Rgba8888, SKAlphaType.Premul);
        SKSamplingOptions sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
        using SKBitmap? bitmap = decoded.Resize(targetInfo, sampling);
        if (bitmap == null) throw new InvalidDataException("Image resize failed.");

        double[] coefficients = new double[64];
        for (int u = 0; u < 8; u++)
        {
            for (int v = 0; v < 8; v++)
            {
                double sum = 0;
                for (int x = 0; x < 32; x++)
                {
                    double cosX = Math.Cos((2 * x + 1) * u * Math.PI / 64d);
                    for (int y = 0; y < 32; y++)
                    {
                        SKColor pixel = bitmap.GetPixel(x, y);
                        double luminance = .299d * pixel.Red + .587d * pixel.Green + .114d * pixel.Blue;
                        double cosY = Math.Cos((2 * y + 1) * v * Math.PI / 64d);
                        sum += luminance * cosX * cosY;
                    }
                }
                coefficients[u * 8 + v] = sum;
            }
        }

        double[] medianSource = coefficients.Skip(1).Order().ToArray();
        double median = medianSource[medianSource.Length / 2];
        ulong hash = 0;
        for (int index = 0; index < coefficients.Length; index++)
            if (coefficients[index] > median) hash |= 1UL << index;
        return unchecked((long)hash);
    }

    private sealed record FetchResult(byte[]? Bytes, bool Permanent, string Code);
}
