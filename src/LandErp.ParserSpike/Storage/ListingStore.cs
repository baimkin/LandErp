using System.Globalization;
using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Serialization;
using Microsoft.Data.Sqlite;

namespace LandErp.ParserSpike.Storage;

public sealed record SavedListing(SearchListing Listing, DateTimeOffset FirstSeen, DateTimeOffset LastSeen)
{
    public string ExternalId => Listing.ExternalId;
    public string Title => Listing.Title;
    public string Location => Listing.Location;
    public string PriceText => Listing.Price.Raw ?? "Нет данных";
    public decimal? Price => Listing.Price.Parsed;
    public decimal? Area => Listing.AreaSquareMeters.Parsed;
    public string Seller => Listing.SellerName;
}

public sealed record SavedRun(string Id, string Url, string Started, int Limit, int Page, int Count, string State);
public sealed record SavedObservation(string RunId, int Page, string ObservedAt, SearchListing Listing);

/// <summary>Local debugging store. Raw observations are append-only; missing values do not clear known fields.</summary>
public sealed class ListingStore
{
    private readonly string connectionString;

    public ListingStore(string path)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false }.ToString();
        using SqliteConnection db = Open();
        Execute(db, null, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS runs (
              id TEXT PRIMARY KEY, url TEXT NOT NULL, started TEXT NOT NULL,
              page_limit INTEGER NOT NULL, page INTEGER NOT NULL, count INTEGER NOT NULL, state TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS listings (
              id TEXT PRIMARY KEY, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS observations (
              sequence INTEGER PRIMARY KEY, run_id TEXT NOT NULL, page INTEGER NOT NULL,
              id TEXT NOT NULL, observed_at TEXT NOT NULL, json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS observations_id ON observations(id, sequence);
            """);
    }

    public string StartRun(string currentUrl, int limit)
    {
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri? url) || !SearchParser.IsAvitoUrl(url))
            throw new ArgumentException("AVITO_URL_REQUIRED", nameof(currentUrl));
        string id = Guid.NewGuid().ToString("D");
        using SqliteConnection db = Open();
        Execute(db, null, "INSERT INTO runs VALUES ($id,$url,$started,$limit,1,0,'READY')",
            ("$id", id), ("$url", url.GetLeftPart(UriPartial.Path)), ("$started", Time(DateTimeOffset.UtcNow)), ("$limit", limit));
        return id;
    }

    public void UpdateRun(string id, string state, int page, int count)
    {
        using SqliteConnection db = Open();
        Execute(db, null, "UPDATE runs SET state=$state,page=$page,count=$count WHERE id=$id",
            ("$state", state), ("$page", page), ("$count", count), ("$id", id));
    }

    public void Save(string runId, int page, DateTimeOffset observedAt, IReadOnlyList<SearchListing> items)
    {
        using SqliteConnection db = Open();
        using SqliteTransaction transaction = db.BeginTransaction();
        foreach (SearchListing item in items)
        {
            using SqliteCommand lookup = Command(db, transaction, "SELECT json FROM listings WHERE id=$id", ("$id", item.ExternalId));
            string? priorJson = lookup.ExecuteScalar() as string;
            SearchListing latest = priorJson is null ? item : Merge(SpikeJson.Deserialize<SearchListing>(priorJson), item);
            Execute(db, transaction, "INSERT INTO observations(run_id,page,id,observed_at,json) VALUES($run,$page,$id,$time,$json)",
                ("$run", runId), ("$page", page), ("$id", item.ExternalId), ("$time", Time(observedAt)), ("$json", SpikeJson.Serialize(item)));
            Execute(db, transaction, """
                INSERT INTO listings(id,first_seen,last_seen,json) VALUES($id,$time,$time,$json)
                ON CONFLICT(id) DO UPDATE SET last_seen=excluded.last_seen,json=excluded.json
                """, ("$id", item.ExternalId), ("$time", Time(observedAt)), ("$json", SpikeJson.Serialize(latest)));
        }
        transaction.Commit();
    }

    public SavedListing[] ReadListings(string search = "")
    {
        using SqliteConnection db = Open();
        using SqliteCommand cmd = Command(db, null, "SELECT json,first_seen,last_seen FROM listings ORDER BY last_seen DESC");
        using SqliteDataReader reader = cmd.ExecuteReader();
        List<SavedListing> result = [];
        while (reader.Read())
        {
            SearchListing item = SpikeJson.Deserialize<SearchListing>(reader.GetString(0));
            if (search.Length > 0 && !(item.ExternalId + " " + item.Title + " " + item.Location + " " + item.SellerName)
                .Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            result.Add(new(item, DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture)));
        }
        return result.ToArray();
    }

    public SavedRun[] ReadRuns()
    {
        using SqliteConnection db = Open();
        using SqliteCommand cmd = Command(db, null, "SELECT id,url,started,page_limit,page,count,state FROM runs ORDER BY started DESC");
        using SqliteDataReader reader = cmd.ExecuteReader();
        List<SavedRun> result = [];
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetString(6)));
        return result.ToArray();
    }

    public SavedObservation[] ReadHistory(string id)
    {
        using SqliteConnection db = Open();
        using SqliteCommand cmd = Command(db, null, "SELECT run_id,page,observed_at,json FROM observations WHERE id=$id ORDER BY sequence DESC", ("$id", id));
        using SqliteDataReader reader = cmd.ExecuteReader();
        List<SavedObservation> result = [];
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
            SpikeJson.Deserialize<SearchListing>(reader.GetString(3))));
        return result.ToArray();
    }

    private static SearchListing Merge(SearchListing prior, SearchListing next) => next with
    {
        Title = Keep(prior.Title, next.Title),
        Location = Keep(prior.Location, next.Location),
        DateText = Keep(prior.DateText, next.DateText),
        Price = KeepValue(prior.Price, next.Price),
        AreaSquareMeters = KeepValue(prior.AreaSquareMeters, next.AreaSquareMeters),
        PricePerSotka = KeepValue(prior.PricePerSotka, next.PricePerSotka),
        PreviewDescription = Keep(prior.PreviewDescription, next.PreviewDescription),
        SellerName = Keep(prior.SellerName, next.SellerName),
        SellerInfo = Keep(prior.SellerInfo, next.SellerInfo),
        Badges = Keep(prior.Badges, next.Badges),
        PhotoUrl = Keep(prior.PhotoUrl, next.PhotoUrl)
    };

    private static string Keep(string prior, string next) => string.IsNullOrWhiteSpace(next) ? prior : next;
    private static ObservedValue<decimal> KeepValue(ObservedValue<decimal> prior, ObservedValue<decimal> next) =>
        next.Presence is Presence.Absent or Presence.Empty or Presence.NotInspected ? prior : next;
    private static string Time(DateTimeOffset time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private SqliteConnection Open() { SqliteConnection db = new(connectionString); db.Open(); return db; }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
    {
        SqliteCommand cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach ((string name, object value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }
    private static void Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object Value)[] args)
    { using SqliteCommand cmd = Command(db, tx, sql, args); cmd.ExecuteNonQuery(); }
}
