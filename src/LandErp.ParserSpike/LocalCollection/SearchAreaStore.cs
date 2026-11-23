using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record SavedSearchArea(int Version, string Key, DateTimeOffset CapturedAtUtc, GeoPoint[][][] Polygons, string Signature);
public sealed record SearchAreaCheck(bool FiltersMatch, bool HasReference, bool GeometryMatches,
    string? ProblemCode, int MissingCoordinates, int OutsideCount, bool DiagnosticTruncated = false);
public sealed record SearchAreaEvidence(string JobId, DateTimeOffset TimeUtc, SearchAreaCheck Check,
    GeoPoint[][][] Reference, GeoPoint[][][] Observed, string? RequestedDrawId, string? CurrentDrawId);

/// <summary>Local evidence, never server entities. Separate namespaces isolate Local from Server.</summary>
public sealed class SearchAreaStore(string directory)
{
    private readonly object sync = new();
    public string RootPath { get; } = System.IO.Path.GetFullPath(directory);
    public static string Key(string url, bool server) => (server ? "server-" : "local-")
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SearchUrls.Normalize(url, SourceSite.Avito).Key)));
    private string FilePath(string key, string suffix) => System.IO.Path.Combine(RootPath, key + suffix + ".json");

    public SavedSearchArea? Load(string url, bool server)
    {
        string key = Key(url, server);
        lock (sync)
        {
            try
            {
                string path = FilePath(key, "");
                if (!File.Exists(path) || new FileInfo(path).Length > 2 * 1024 * 1024) return null;
                SavedSearchArea? saved = LocalJson.Read<SavedSearchArea>(File.ReadAllText(path));
                return saved is { Version: 1, Polygons: not null } && saved.Key == key
                    && SearchAreaGeometry.Signature(saved.Polygons) is string signature && signature == saved.Signature ? saved : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NullReferenceException) { return null; }
        }
    }

    public SavedSearchArea? Remember(string url, bool server, GeoPoint[][][] polygons)
    {
        string? signature = SearchAreaGeometry.Signature(polygons);
        if (signature is null) return null;
        string key = Key(url, server);
        lock (sync)
        {
            SavedSearchArea? existing = Load(url, server);
            if (File.Exists(FilePath(key, ""))) return existing; // Never overwrite an established (or corrupt) reference silently.
            SavedSearchArea reference = new(1, key, DateTimeOffset.UtcNow, polygons, signature);
            try
            {
                Directory.CreateDirectory(RootPath);
                string path = FilePath(key, ""), temporary = path + ".tmp";
                File.WriteAllText(temporary, LocalJson.Write(reference), new UTF8Encoding(false));
                File.Move(temporary, path, false);
                return Load(url, server);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Load(url, server); }
        }
    }

    public void WriteDiagnostics(string url, bool server, SearchAreaEvidence evidence, IReadOnlyCollection<ListingObservation> pending)
    {
        string key = Key(url, server);
        lock (sync)
        {
            try
            {
                Directory.CreateDirectory(RootPath);
                Write("-evidence", evidence);
                Write("-unverified", new { evidence.JobId, evidence.TimeUtc, Total = pending.Count, Truncated = evidence.Check.DiagnosticTruncated,
                    Observations = pending.Take(5000).ToArray() });
                void Write<T>(string suffix, T data)
                {
                    string path = FilePath(key, suffix);
                    File.WriteAllText(path + ".tmp", LocalJson.Write(data), new UTF8Encoding(false));
                    File.Move(path + ".tmp", path, true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
