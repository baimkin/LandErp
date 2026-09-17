using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LandErp.ParserSpike.Avito;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Serialization;
using Microsoft.Data.Sqlite;

namespace LandErp.ParserSpike.LocalCollection;

/// <summary>Durable local queue and result sink. Claims and page coverage commit atomically; no browser objects enter storage.</summary>
public sealed class LocalStore : IJobSource, IResultSink
{
    private readonly string connectionString;
    public string Path { get; }
    public string? MigrationBackup { get; private set; }
    public LocalStore(string path)
    {
        Path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false, DefaultTimeout = 10 }.ToString();
        using SqliteConnection db = Open();
        long version = Convert.ToInt64(Scalar(db, null, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        if (version > 3) throw new InvalidOperationException("DATABASE_VERSION_NEWER: база создана более новой версией приложения.");
        if (version == 3) return;
        if (Convert.ToInt64(Scalar(db, null, "SELECT count(*) FROM sqlite_master WHERE type='table'"), CultureInfo.InvariantCulture) > 0)
        {
            MigrationBackup = Path + ".backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + ".sqlite";
            using SqliteConnection backup = new(new SqliteConnectionStringBuilder { DataSource = MigrationBackup, Pooling = false }.ToString());
            backup.Open(); db.BackupDatabase(backup);
        }
        using SqliteTransaction tx = db.BeginTransaction();
        if (version == 1)
        {
            // Preserve all accepted observations; remove per-pass uniqueness so A→B→A remains a real change.
            Exec(db, tx, """
                ALTER TABLE local_observations RENAME TO local_observations_v1;
                DROP INDEX local_observation_history;
                DROP INDEX local_observation_link;
                DROP INDEX local_observation_job;
                CREATE TABLE local_observations(id TEXT PRIMARY KEY,job_id TEXT NOT NULL,link_id TEXT NOT NULL,pass_id TEXT NOT NULL,
                  page INTEGER NOT NULL,source TEXT NOT NULL,external_id TEXT NOT NULL,observed_at TEXT NOT NULL,json TEXT NOT NULL,fingerprint TEXT NOT NULL);
                INSERT INTO local_observations SELECT * FROM local_observations_v1;
                DROP TABLE local_observations_v1;
                CREATE INDEX local_observation_history ON local_observations(source,external_id,observed_at);
                CREATE INDEX local_observation_link ON local_observations(link_id,source,external_id);
                CREATE INDEX local_observation_job ON local_observations(job_id,source,external_id);
                """);
            CreateMapTables(db, tx);
            CreateWorkspaceTables(db, tx);
            Exec(db, tx, "PRAGMA user_version=3"); tx.Commit(); return;
        }
        if (version == 2)
        {
            CreateWorkspaceTables(db, tx);
            Exec(db, tx, "PRAGMA user_version=3"); tx.Commit(); return;
        }
        Exec(db, tx, """
            CREATE TABLE local_settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
            CREATE TABLE local_links(id TEXT PRIMARY KEY,label TEXT NOT NULL,url TEXT NOT NULL,canonical TEXT NOT NULL,
              source TEXT NOT NULL,selected INTEGER NOT NULL,enabled INTEGER NOT NULL,revision INTEGER NOT NULL,archived INTEGER NOT NULL DEFAULT 0);
            CREATE UNIQUE INDEX local_link_key ON local_links(canonical) WHERE archived=0;
            CREATE TABLE local_batches(id TEXT PRIMARY KEY,started TEXT NOT NULL,settings TEXT NOT NULL);
            CREATE TABLE local_passes(id TEXT PRIMARY KEY,link_id TEXT NOT NULL,revision INTEGER NOT NULL,started TEXT NOT NULL,
              compatibility TEXT NOT NULL,end_page INTEGER);
            CREATE TABLE local_jobs(id TEXT PRIMARY KEY,batch_id TEXT NOT NULL,link_id TEXT NOT NULL,revision INTEGER NOT NULL,
              source TEXT NOT NULL,url TEXT NOT NULL,pass_id TEXT NOT NULL,page INTEGER NOT NULL,page_limit INTEGER NOT NULL,
              state TEXT NOT NULL,reason TEXT NOT NULL,owner TEXT,token TEXT,started TEXT NOT NULL);
            CREATE UNIQUE INDEX local_active_link ON local_jobs(link_id) WHERE state IN ('Pending','Running','AwaitingManualAction','PausedByUser');
            CREATE INDEX local_job_queue ON local_jobs(batch_id,source,state);
            CREATE TABLE local_pages(pass_id TEXT NOT NULL,page INTEGER NOT NULL,url TEXT NOT NULL,next_url TEXT,completed INTEGER NOT NULL,
              job_id TEXT NOT NULL,observed_at TEXT NOT NULL,count INTEGER NOT NULL,reason TEXT NOT NULL,PRIMARY KEY(pass_id,page));
            CREATE TABLE local_observations(id TEXT PRIMARY KEY,job_id TEXT NOT NULL,link_id TEXT NOT NULL,pass_id TEXT NOT NULL,
              page INTEGER NOT NULL,source TEXT NOT NULL,external_id TEXT NOT NULL,observed_at TEXT NOT NULL,json TEXT NOT NULL,fingerprint TEXT NOT NULL);
            CREATE INDEX local_observation_history ON local_observations(source,external_id,observed_at);
            CREATE INDEX local_observation_link ON local_observations(link_id,source,external_id);
            CREATE INDEX local_observation_job ON local_observations(job_id,source,external_id);
            CREATE TABLE local_listings(source TEXT NOT NULL,external_id TEXT NOT NULL,first_seen TEXT NOT NULL,last_seen TEXT NOT NULL,
              title TEXT NOT NULL,location TEXT NOT NULL,seller TEXT NOT NULL,price REAL,json TEXT NOT NULL,PRIMARY KEY(source,external_id));
            CREATE INDEX local_listing_time ON local_listings(last_seen);
            CREATE INDEX local_listing_price ON local_listings(price);
            INSERT INTO local_settings VALUES(1,'{}');
            """);
        ImportLegacy(db, tx);
        CreateMapTables(db, tx);
        CreateWorkspaceTables(db, tx);
        Exec(db, tx, "PRAGMA user_version=3");
        tx.Commit();
        Exec(db, null, "PRAGMA journal_mode=WAL");
    }

    private static void CreateMapTables(SqliteConnection db, SqliteTransaction tx)
    {
        Exec(db, tx, """
            CREATE TABLE local_map_scopes(pass_id TEXT PRIMARY KEY,job_id TEXT NOT NULL,link_id TEXT NOT NULL,json TEXT NOT NULL);
            CREATE TABLE local_sightings(job_id TEXT NOT NULL,link_id TEXT NOT NULL,pass_id TEXT NOT NULL,page INTEGER NOT NULL,
              source TEXT NOT NULL,external_id TEXT NOT NULL,observation_id TEXT NOT NULL,PRIMARY KEY(job_id,page,source,external_id));
            INSERT OR REPLACE INTO local_sightings SELECT job_id,link_id,pass_id,page,source,external_id,id FROM local_observations ORDER BY observed_at,rowid;
            CREATE INDEX local_sighting_link ON local_sightings(link_id,source,external_id);
            CREATE INDEX local_sighting_pass ON local_sightings(pass_id,page);
            """);
    }

    private static void CreateWorkspaceTables(SqliteConnection db, SqliteTransaction tx)
    {
        Exec(db, tx, """
            CREATE TABLE local_groups(id TEXT PRIMARY KEY,name TEXT NOT NULL,sort_order INTEGER NOT NULL,
              active INTEGER NOT NULL,revision INTEGER NOT NULL);
            CREATE UNIQUE INDEX local_group_name ON local_groups(name COLLATE NOCASE);
            CREATE TABLE local_link_schedules(link_id TEXT PRIMARY KEY,group_id TEXT,kind TEXT NOT NULL,
              interval_minutes INTEGER,fixed_times TEXT NOT NULL,time_zone_id TEXT NOT NULL,enabled INTEGER NOT NULL,
              next_run_at TEXT,revision INTEGER NOT NULL,
              FOREIGN KEY(link_id) REFERENCES local_links(id),FOREIGN KEY(group_id) REFERENCES local_groups(id));
            CREATE INDEX local_schedule_due ON local_link_schedules(enabled,next_run_at);
            INSERT INTO local_link_schedules(link_id,group_id,kind,interval_minutes,fixed_times,time_zone_id,enabled,next_run_at,revision)
              SELECT id,NULL,'Manual',NULL,'[]','Europe/Moscow',1,NULL,1 FROM local_links;
            """);
    }
    public MapScope? ReadMapScope(string jobId)
    {
        using SqliteConnection db = Open();
        string? json = Scalar(db, null, "SELECT json FROM local_map_scopes WHERE job_id=$job", ("$job", jobId)) as string;
        return json is null ? null : LocalJson.Read<MapScope>(json);
    }

    private static void ImportLegacy(SqliteConnection db, SqliteTransaction tx)
    {
        if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM sqlite_master WHERE name='listings'"), CultureInfo.InvariantCulture) == 0) return;
        List<(string Json, string First, string Last)> old = [];
        using (SqliteCommand cmd = Command(db, tx, "SELECT json,first_seen,last_seen FROM listings"))
        using (SqliteDataReader reader = cmd.ExecuteReader())
            while (reader.Read()) old.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        foreach (var row in old)
        {
            ListingObservation item = Legacy(SpikeJson.Deserialize<SearchListing>(row.Json), DateTimeOffset.Parse(row.Last, CultureInfo.InvariantCulture));
            Upsert(db, tx, item, row.First);
        }
        if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM sqlite_master WHERE name='runs'"), CultureInfo.InvariantCulture) > 0)
        {
            List<(string Id, string Url, string Started, int Limit, int Page, string State)> runs = [];
            using (SqliteCommand cmd = Command(db, tx, "SELECT id,url,started,page_limit,page,state FROM runs"))
            using (SqliteDataReader r = cmd.ExecuteReader())
                while (r.Read()) runs.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetString(5)));
            Exec(db, tx, "INSERT INTO local_batches VALUES('legacy',$time,$settings)", ("$time", Time(DateTimeOffset.UtcNow)), ("$settings", LocalJson.Write(new CollectionSettings())));
            foreach (var run in runs)
            {
                Exec(db, tx, "INSERT INTO local_passes VALUES($pass,'legacy',0,$time,'legacy',NULL)", ("$pass", "legacy-" + run.Id), ("$time", run.Started));
                Exec(db, tx, "INSERT INTO local_jobs VALUES($id,'legacy','legacy',0,'Avito',$url,$pass,$page,$limit,'StoppedInterrupted',$reason,NULL,NULL,$time)",
                    ("$id", run.Id), ("$url", run.Url), ("$pass", "legacy-" + run.Id), ("$page", run.Page), ("$limit", run.Limit),
                    ("$reason", "Legacy: " + run.State + "; завершение страниц не подтверждено"), ("$time", run.Started));
            }
        }
        if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM sqlite_master WHERE name='observations'"), CultureInfo.InvariantCulture) == 0) return;
        List<(long Sequence, string Run, int Page, string Time, string Json)> history = [];
        using (SqliteCommand cmd = Command(db, tx, "SELECT sequence,run_id,page,observed_at,json FROM observations"))
        using (SqliteDataReader r = cmd.ExecuteReader())
            while (r.Read()) history.Add((r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetString(3), r.GetString(4)));
        foreach (var row in history)
        {
            ListingObservation item = Legacy(SpikeJson.Deserialize<SearchListing>(row.Json), DateTimeOffset.Parse(row.Time, CultureInfo.InvariantCulture));
            Exec(db, tx, "INSERT INTO local_observations VALUES($id,$job,'legacy',$pass,$page,'Avito',$external,$time,$json,$hash)",
                ("$id", "legacy-" + row.Sequence), ("$job", row.Run), ("$pass", "legacy-" + row.Run), ("$page", row.Page),
                ("$external", item.ExternalId), ("$time", Time(item.ObservedAtUtc)), ("$json", LocalJson.Write(item)), ("$hash", Hash(LocalJson.Write(item))));
        }
        // Original tables remain readable by the old prototype. Old runs cannot prove page coverage.
    }
    private static ListingObservation Legacy(SearchListing item, DateTimeOffset time) => new()
    {
        Source = SourceSite.Avito,
        ExternalId = item.ExternalId,
        Url = item.Url,
        ObservedAtUtc = time,
        AdapterVersion = "legacy-0.2.1",
        Provenance = "Legacy",
        Title = TextValue.Legacy(item.Title),
        Price = new(item.Price.Presence, item.Price.Raw, item.Price.Parsed),
        AreaSquareMeters = new(item.AreaSquareMeters.Presence, item.AreaSquareMeters.Raw, item.AreaSquareMeters.Parsed),
        UnitPrice = new(item.PricePerSotka.Presence, item.PricePerSotka.Raw, item.PricePerSotka.Parsed),
        Description = TextValue.Legacy(item.PreviewDescription),
        Location = TextValue.Legacy(item.Location),
        DateText = TextValue.Legacy(item.DateText),
        SellerName = TextValue.Legacy(item.SellerName),
        SellerStatistics = TextValue.Legacy(item.SellerInfo),
        PhotoUrls = item.PhotoUrl.Length == 0 ? [] : [item.PhotoUrl],
        Badges = item.Badges.Length == 0 ? [] : [item.Badges],
        Warnings = item.Warnings
    };

    public CollectionSettings Settings()
    { using SqliteConnection db = Open(); return LocalJson.Read<CollectionSettings>((string)Scalar(db, null, "SELECT json FROM local_settings WHERE id=1")!); }
    public void SaveSettings(CollectionSettings settings)
    { settings.Validate(); using SqliteConnection db = Open(); Exec(db, null, "UPDATE local_settings SET json=$json WHERE id=1", ("$json", LocalJson.Write(settings))); }

    public SearchLink SaveLink(string label, string url, bool selected = true, SourceSite? manualSource = null, string? id = null, bool enabled = true)
    {
        NormalizedSearch safe = SearchUrls.Normalize(url, manualSource);
        if (label.Length > 300 || label.Any(char.IsControl)) throw new ArgumentException("LINK_LABEL_INVALID");
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction();
        string? existing = Scalar(db, tx, "SELECT id FROM local_links WHERE canonical=$key AND archived=0", ("$key", safe.Key)) as string;
        if (existing is not null && existing != id) throw new ArgumentException("Такая ссылка уже сохранена.");
        string linkId = id ?? Guid.NewGuid().ToString("D");
        if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM local_jobs WHERE link_id=$id AND state IN ('Pending','Running','AwaitingManualAction','PausedByUser')", ("$id", linkId)), CultureInfo.InvariantCulture) > 0)
            throw new InvalidOperationException("Нельзя изменять ссылку, пока она находится в очереди.");
        Exec(db, tx, """
            INSERT INTO local_links VALUES($id,$label,$url,$key,$source,$selected,$enabled,1,0)
            ON CONFLICT(id) DO UPDATE SET label=excluded.label,url=excluded.url,canonical=excluded.canonical,source=excluded.source,
            selected=excluded.selected,enabled=excluded.enabled,revision=local_links.revision+CASE WHEN local_links.canonical<>excluded.canonical THEN 1 ELSE 0 END
            """, ("$id", linkId), ("$label", label.Length == 0 ? new Uri(safe.Url).Host : label), ("$url", safe.Url), ("$key", safe.Key),
            ("$source", safe.Source.ToString()), ("$selected", selected ? 1 : 0), ("$enabled", enabled ? 1 : 0));
        Exec(db, tx, """
            INSERT OR IGNORE INTO local_link_schedules(link_id,group_id,kind,interval_minutes,fixed_times,time_zone_id,enabled,next_run_at,revision)
            VALUES($id,NULL,'Manual',NULL,'[]','Europe/Moscow',1,NULL,1)
            """, ("$id", linkId));
        tx.Commit(); return Links().Single(x => x.Id == linkId);
    }

    /// <summary>Server jobs use a hidden durable link so their recovery data never appears in the Local workspace.</summary>
    public SearchLink EnsureServerWorkLink(string label, string url, SourceSite source)
    {
        NormalizedSearch safe = SearchUrls.Normalize(url, source);
        string linkId = "server-" + Hash(safe.Key)[..32];
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction();
        Exec(db, tx, """
            INSERT INTO local_links VALUES($id,$label,$url,$key,$source,0,1,1,1)
            ON CONFLICT(id) DO UPDATE SET label=excluded.label,url=excluded.url,canonical=excluded.canonical,
              source=excluded.source,selected=0,enabled=1,archived=1,
              revision=local_links.revision+CASE WHEN local_links.canonical<>excluded.canonical THEN 1 ELSE 0 END
            """, ("$id", linkId), ("$label", label.Trim().Length == 0 ? new Uri(safe.Url).Host : label.Trim()),
            ("$url", safe.Url), ("$key", safe.Key), ("$source", safe.Source.ToString()));
        Exec(db, tx, """
            INSERT OR IGNORE INTO local_link_schedules(link_id,group_id,kind,interval_minutes,fixed_times,time_zone_id,enabled,next_run_at,revision)
            VALUES($id,NULL,'Manual',NULL,'[]','Europe/Moscow',0,NULL,1)
            """, ("$id", linkId));
        tx.Commit();
        return ReadLink(linkId, includeArchived: true) ?? throw new InvalidOperationException("SERVER_WORK_LINK_NOT_SAVED");
    }

    public LocalGroup SaveGroup(string name, int sortOrder, string? id = null, bool active = true, long? expectedRevision = null)
    {
        string value = name.Trim();
        if (value.Length is < 1 or > 200 || value.Any(char.IsControl)) throw new ArgumentException("LOCAL_GROUP_NAME_INVALID");
        string groupId = id ?? Guid.CreateVersion7().ToString();
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction();
        if (id == null)
            Exec(db, tx, "INSERT INTO local_groups VALUES($id,$name,$sort,$active,1)",
                ("$id", groupId), ("$name", value), ("$sort", sortOrder), ("$active", active ? 1 : 0));
        else
        {
            if (expectedRevision == null) throw new ArgumentException("LOCAL_GROUP_REVISION_REQUIRED");
            int changed = Exec(db, tx, "UPDATE local_groups SET name=$name,sort_order=$sort,active=$active,revision=revision+1 WHERE id=$id AND revision=$revision",
                ("$id", groupId), ("$name", value), ("$sort", sortOrder), ("$active", active ? 1 : 0), ("$revision", expectedRevision.Value));
            if (changed != 1) throw new InvalidOperationException("LOCAL_GROUP_CHANGED");
        }
        tx.Commit(); return Groups().Single(item => item.Id == groupId);
    }

    public LocalGroup[] Groups()
    {
        using SqliteConnection db = Open(); using SqliteCommand command = Command(db, null,
            "SELECT id,name,sort_order,active,revision FROM local_groups ORDER BY sort_order,name COLLATE NOCASE");
        using SqliteDataReader reader = command.ExecuteReader(); List<LocalGroup> groups = [];
        while (reader.Read()) groups.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3), reader.GetInt64(4)));
        return groups.ToArray();
    }

    public LocalScheduledLink SaveSchedule(string linkId, string? groupId, LocalSchedule schedule, long? expectedRevision = null,
        DateTimeOffset? now = null)
    {
        schedule.Validate();
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction();
        if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM local_links WHERE id=$id AND archived=0", ("$id", linkId)), CultureInfo.InvariantCulture) != 1)
            throw new ArgumentException("LOCAL_LINK_NOT_FOUND");
        if (groupId != null && Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM local_groups WHERE id=$id AND active=1", ("$id", groupId)), CultureInfo.InvariantCulture) != 1)
            throw new ArgumentException("LOCAL_GROUP_NOT_FOUND");
        long revision = Convert.ToInt64(Scalar(db, tx, "SELECT revision FROM local_link_schedules WHERE link_id=$id", ("$id", linkId)), CultureInfo.InvariantCulture);
        if (expectedRevision != null && expectedRevision != revision) throw new InvalidOperationException("LOCAL_SCHEDULE_CHANGED");
        DateTimeOffset? next = schedule.Enabled ? LocalScheduleRules.Next(schedule, now ?? DateTimeOffset.UtcNow) : null;
        int changed = Exec(db, tx, """
            UPDATE local_link_schedules SET group_id=$group,kind=$kind,interval_minutes=$interval,fixed_times=$times,
              time_zone_id=$zone,enabled=$enabled,next_run_at=$next,revision=revision+1 WHERE link_id=$id AND revision=$revision
            """, ("$group", groupId), ("$kind", schedule.Kind.ToString()), ("$interval", schedule.IntervalMinutes),
            ("$times", LocalJson.Write(schedule.FixedTimes ?? [])), ("$zone", schedule.TimeZoneId), ("$enabled", schedule.Enabled ? 1 : 0),
            ("$next", next == null ? null : Time(next.Value)), ("$id", linkId), ("$revision", revision));
        if (changed != 1) throw new InvalidOperationException("LOCAL_SCHEDULE_CHANGED");
        tx.Commit(); return ScheduledLinks().Single(item => item.Link.Id == linkId);
    }

    public LocalScheduledLink[] ScheduledLinks()
    {
        using SqliteConnection db = Open(); using SqliteCommand command = Command(db, null, """
            SELECT l.id,l.label,l.url,l.source,l.selected,l.enabled,l.revision,l.archived,
              s.group_id,COALESCE(g.name,''),s.kind,s.interval_minutes,s.fixed_times,s.time_zone_id,s.enabled,s.next_run_at,s.revision
            FROM local_links l JOIN local_link_schedules s ON s.link_id=l.id
            LEFT JOIN local_groups g ON g.id=s.group_id WHERE l.archived=0 ORDER BY COALESCE(g.sort_order,2147483647),l.rowid
            """);
        using SqliteDataReader reader = command.ExecuteReader(); List<LocalScheduledLink> links = [];
        while (reader.Read())
        {
            SearchLink link = new(reader.GetString(0), reader.GetString(1), reader.GetString(2), Enum.Parse<SourceSite>(reader.GetString(3)),
                reader.GetBoolean(4), reader.GetBoolean(5), reader.GetInt32(6), reader.GetBoolean(7));
            LocalSchedule schedule = new(Enum.Parse<LocalScheduleKind>(reader.GetString(10)), reader.IsDBNull(11) ? null : reader.GetInt32(11),
                LocalJson.Read<string[]>(reader.GetString(12)), reader.GetString(13), reader.GetBoolean(14));
            links.Add(new(link, reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), schedule,
                reader.IsDBNull(15) ? null : DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture), reader.GetInt64(16)));
        }
        return links.ToArray();
    }

    public LocalScheduledLink[] DueLinks(DateTimeOffset now) => ScheduledLinks()
        .Where(item => item.Link.Enabled && item.Schedule.Enabled && item.NextRunAt != null && item.NextRunAt <= now)
        .ToArray();

    public void MarkScheduleDispatched(string linkId, long expectedRevision, DateTimeOffset now)
    {
        LocalScheduledLink current = ScheduledLinks().Single(item => item.Link.Id == linkId);
        if (current.Revision != expectedRevision) throw new InvalidOperationException("LOCAL_SCHEDULE_CHANGED");
        DateTimeOffset? next = LocalScheduleRules.Next(current.Schedule, now);
        using SqliteConnection db = Open();
        int changed = Exec(db, null, "UPDATE local_link_schedules SET next_run_at=$next,revision=revision+1 WHERE link_id=$id AND revision=$revision",
            ("$next", next == null ? null : Time(next.Value)), ("$id", linkId), ("$revision", expectedRevision));
        if (changed != 1) throw new InvalidOperationException("LOCAL_SCHEDULE_CHANGED");
    }
    public SearchLink[] Links()
    {
        using SqliteConnection db = Open(); using SqliteCommand cmd = Command(db, null, "SELECT id,label,url,source,selected,enabled,revision,archived FROM local_links WHERE archived=0 ORDER BY rowid");
        using SqliteDataReader r = cmd.ExecuteReader(); List<SearchLink> result = [];
        while (r.Read()) result.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), Enum.Parse<SourceSite>(r.GetString(3)), r.GetBoolean(4), r.GetBoolean(5), r.GetInt32(6), r.GetBoolean(7)));
        return result.ToArray();
    }
    private SearchLink? ReadLink(string id, bool includeArchived)
    {
        using SqliteConnection db = Open(); using SqliteCommand command = Command(db, null,
            "SELECT id,label,url,source,selected,enabled,revision,archived FROM local_links WHERE id=$id AND ($all=1 OR archived=0)",
            ("$id", id), ("$all", includeArchived ? 1 : 0));
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            Enum.Parse<SourceSite>(reader.GetString(3)), reader.GetBoolean(4), reader.GetBoolean(5), reader.GetInt32(6), reader.GetBoolean(7)) : null;
    }
    public void SelectLink(string id, bool selected)
    { using SqliteConnection db = Open(); Exec(db, null, "UPDATE local_links SET selected=$selected WHERE id=$id", ("$selected", selected ? 1 : 0), ("$id", id)); }
    public void ArchiveLink(string id)
    {
        SearchLink link = Links().Single(x => x.Id == id);
        SaveLink(link.Label, link.Url, false, link.Source, id, false);
        using SqliteConnection db = Open(); Exec(db, null, "UPDATE local_links SET archived=1 WHERE id=$id", ("$id", id));
    }

    public string StartBatch(CollectionSettings settings, bool force = false, DateTimeOffset? now = null, string? onlyLinkId = null)
    {
        settings.Validate(); DateTimeOffset time = now ?? DateTimeOffset.UtcNow;
        // Server mode targets one permitted search without changing Local mode selection/settings.
        SearchLink[] links = onlyLinkId == null
            ? Links().Where(x => x.Enabled && x.Selected).ToArray()
            : ReadLink(onlyLinkId, includeArchived: true) is { Enabled: true } single ? [single] : [];
        if (links.Length == 0) throw new InvalidOperationException("Отметьте хотя бы одну ссылку.");
        string batch = Guid.NewGuid().ToString("D");
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction();
        Exec(db, tx, "INSERT INTO local_batches VALUES($id,$time,$settings)", ("$id", batch), ("$time", Time(time)), ("$settings", LocalJson.Write(settings)));
        foreach (SearchLink link in links)
        {
            if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM local_jobs WHERE link_id=$id AND state IN ('Pending','Running','AwaitingManualAction','PausedByUser')", ("$id", link.Id)), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException("Ссылка уже находится в незавершённой очереди. Продолжите или остановите её.");
            string pass = Guid.NewGuid().ToString("D"), startUrl = link.Url; int startPage = 1; bool fresh = false;
            if (!force)
            {
                using SqliteCommand lookup = Command(db, tx, "SELECT id,started,end_page FROM local_passes WHERE link_id=$id AND revision=$revision AND compatibility=$compat ORDER BY started DESC LIMIT 1",
                    ("$id", link.Id), ("$revision", link.Revision), ("$compat", settings.Compatibility));
                using SqliteDataReader r = lookup.ExecuteReader();
                if (r.Read())
                {
                    string candidate = r.GetString(0); DateTimeOffset began = DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture);
                    int? end = r.IsDBNull(2) ? null : r.GetInt32(2); r.Close();
                    int prefix = Prefix(db, tx, candidate);
                    double age = (time - began).TotalHours;
                    fresh = age >= 0 && age < settings.FreshnessHours && prefix >= (end.HasValue ? Math.Min(end.Value, settings.MaxPages) : settings.MaxPages);
                    if (fresh) { pass = candidate; startPage = Math.Min(prefix, settings.MaxPages); }
                    else if (age >= 0 && age < settings.ResumeHours && prefix > 0 && (!end.HasValue || prefix < end))
                    {
                        string? pagination = Scalar(db, tx, "SELECT next_url FROM local_pages WHERE pass_id=$id AND page=$page", ("$id", candidate), ("$page", prefix)) as string;
                        if (pagination is not null) { pass = candidate; startPage = prefix + 1; startUrl = pagination; }
                    }
                }
            }
            Exec(db, tx, "INSERT OR IGNORE INTO local_passes VALUES($id,$link,$revision,$time,$compat,NULL)",
                ("$id", pass), ("$link", link.Id), ("$revision", link.Revision), ("$time", Time(time)), ("$compat", settings.Compatibility));
            Exec(db, tx, "INSERT INTO local_jobs VALUES($id,$batch,$link,$revision,$source,$url,$pass,$page,$limit,$state,$reason,NULL,NULL,$time)",
                ("$id", Guid.NewGuid().ToString("D")), ("$batch", batch), ("$link", link.Id), ("$revision", link.Revision), ("$source", link.Source.ToString()),
                ("$url", startUrl), ("$pass", pass), ("$page", startPage), ("$limit", settings.MaxPages),
                ("$state", fresh ? "SkippedFresh" : "Pending"), ("$reason", fresh ? "Свежий завершённый диапазон" : ""), ("$time", Time(time)));
        }
        tx.Commit(); return batch;
    }
    private static int Prefix(SqliteConnection db, SqliteTransaction tx, string pass)
    {
        using SqliteCommand cmd = Command(db, tx, "SELECT page FROM local_pages WHERE pass_id=$id AND completed=1 ORDER BY page", ("$id", pass));
        using SqliteDataReader r = cmd.ExecuteReader(); int prefix = 0;
        while (r.Read()) { if (r.GetInt32(0) != prefix + 1) break; prefix++; }
        return prefix;
    }
    public CollectionJob? Claim(string batchId, SourceSite source, string owner)
    {
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction();
        string? id = Scalar(db, tx, "SELECT id FROM local_jobs WHERE batch_id=$batch AND source=$source AND state='Pending' ORDER BY rowid LIMIT 1",
            ("$batch", batchId), ("$source", source.ToString())) as string;
        if (id is null) return null;
        Exec(db, tx, "UPDATE local_jobs SET state='Running',owner=$owner,token=$token WHERE id=$id AND state='Pending'",
            ("$owner", owner), ("$token", Guid.NewGuid().ToString("D")), ("$id", id));
        CollectionJob job = ReadJobs(db, tx, " WHERE id=$id", ("$id", id)).Single(); tx.Commit(); return job;
    }
    public CollectionJob[] Jobs(string? batch = null)
    { using SqliteConnection db = Open(); return batch is null ? ReadJobs(db, null, " ORDER BY rowid DESC") : ReadJobs(db, null, " WHERE batch_id=$batch ORDER BY rowid", ("$batch", batch)); }
    private static CollectionJob[] ReadJobs(SqliteConnection db, SqliteTransaction? tx, string where, params (string, object?)[] args)
    {
        using SqliteCommand cmd = Command(db, tx, "SELECT id,batch_id,link_id,revision,source,url,pass_id,page,page_limit,state,reason,owner,token,started FROM local_jobs" + where, args);
        using SqliteDataReader r = cmd.ExecuteReader(); List<CollectionJob> jobs = [];
        while (r.Read()) jobs.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), Enum.Parse<SourceSite>(r.GetString(4)), r.GetString(5),
            r.GetString(6), r.GetInt32(7), r.GetInt32(8), Enum.Parse<JobState>(r.GetString(9)), r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11),
            r.IsDBNull(12) ? null : r.GetString(12), DateTimeOffset.Parse(r.GetString(13), CultureInfo.InvariantCulture)));
        return jobs.ToArray();
    }
    public bool SetState(CollectionJob job, JobState state, string reason)
    {
        using SqliteConnection db = Open();
        return Exec(db, null, "UPDATE local_jobs SET state=$state,reason=$reason WHERE id=$id AND token=$token AND owner=$owner AND state IN ('Running','AwaitingManualAction','PausedByUser')",
            ("$state", state.ToString()), ("$reason", reason), ("$id", job.Id), ("$token", job.Token), ("$owner", job.Owner)) == 1;
    }
    public void RecoverInterrupted()
    {
        // The UI must hold InstanceGuard before calling this; opening a store for a read never cancels another process.
        using SqliteConnection db = Open(); Exec(db, null, "UPDATE local_jobs SET state='StoppedInterrupted',reason='Предыдущий процесс завершился; продолжение только по кнопке',owner=NULL,token=NULL WHERE state IN ('Running','Pending','AwaitingManualAction','PausedByUser')");
    }
    public void StopBatch(string batch)
    { using SqliteConnection db = Open(); Exec(db, null, "UPDATE local_jobs SET state='StoppedInterrupted',reason='Остановлено пользователем',owner=NULL,token=NULL WHERE batch_id=$batch AND state IN ('Running','Pending','AwaitingManualAction','PausedByUser')", ("$batch", batch)); }

    public bool SavePage(CollectionJob job, int page, string pageUrl, IReadOnlyList<ListingObservation> listings, bool completed, Pagination? pagination, string reason)
        => SavePage(job, page, pageUrl, listings, completed, pagination, reason, null, null);

    public bool SavePage(CollectionJob job, int page, string pageUrl, IReadOnlyList<ListingObservation> listings,
        bool completed, Pagination? pagination, string reason, MapScope? map, IReadOnlyList<ListingObservation>? changes)
    {
        string safeUrl = SearchUrls.SafePage(pageUrl, job.Source);
        string? safeNext = pagination?.Url is null ? null : SearchUrls.SafePage(pagination.Url, job.Source);
        using SqliteConnection db = Open(); using SqliteTransaction tx = db.BeginTransaction();
        if (Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM local_jobs WHERE id=$id AND token=$token AND owner=$owner AND state='Running'",
            ("$id", job.Id), ("$token", job.Token), ("$owner", job.Owner)), CultureInfo.InvariantCulture) != 1) return false;
        if (map is not null)
            Exec(db, tx, "INSERT INTO local_map_scopes VALUES($pass,$job,$link,$json) ON CONFLICT(pass_id) DO UPDATE SET job_id=excluded.job_id,json=excluded.json",
                ("$pass", job.PassId), ("$job", job.Id), ("$link", job.LinkId), ("$json", LocalJson.Write(map)));
        foreach (ListingObservation item in (changes ?? []).Concat(listings).DistinctBy(x => LocalJson.Write(x)).OrderBy(x => x.ObservedAtUtc))
        {
            if (item.Source != job.Source) throw new ArgumentException("RESULT_SOURCE_MISMATCH");
            string fingerprint = Hash(LocalJson.Write(item with { ObservedAtUtc = DateTimeOffset.UnixEpoch }));
            string? previousHash = Scalar(db, tx, "SELECT fingerprint FROM local_observations WHERE source=$source AND external_id=$external AND observed_at<=$time ORDER BY observed_at DESC,rowid DESC LIMIT 1",
                ("$source", item.Source.ToString()), ("$external", item.ExternalId), ("$time", Time(item.ObservedAtUtc))) as string;
            if (previousHash != fingerprint) Exec(db, tx, "INSERT INTO local_observations VALUES($id,$job,$link,$pass,$page,$source,$external,$time,$json,$hash)",
                ("$id", Guid.NewGuid().ToString("D")), ("$job", job.Id), ("$link", job.LinkId), ("$pass", job.PassId), ("$page", page),
                ("$source", item.Source.ToString()), ("$external", item.ExternalId), ("$time", Time(item.ObservedAtUtc)), ("$json", LocalJson.Write(item)), ("$hash", fingerprint));
            Upsert(db, tx, item, Time(item.ObservedAtUtc));
        }
        foreach (ListingObservation item in listings)
        {
            string? observationId = Scalar(db, tx, "SELECT id FROM local_observations WHERE source=$source AND external_id=$external ORDER BY observed_at DESC,rowid DESC LIMIT 1",
                ("$source", item.Source.ToString()), ("$external", item.ExternalId)) as string;
            Exec(db, tx, "INSERT OR REPLACE INTO local_sightings VALUES($job,$link,$pass,$page,$source,$external,$observation)",
                ("$job", job.Id), ("$link", job.LinkId), ("$pass", job.PassId), ("$page", page), ("$source", item.Source.ToString()),
                ("$external", item.ExternalId), ("$observation", observationId));
        }
        Exec(db, tx, """
            INSERT INTO local_pages VALUES($pass,$page,$url,$pagination,$completed,$job,$time,$count,$reason)
            ON CONFLICT(pass_id,page) DO UPDATE SET next_url=excluded.next_url,completed=MAX(local_pages.completed,excluded.completed),
              job_id=excluded.job_id,observed_at=excluded.observed_at,count=excluded.count,reason=excluded.reason
            """, ("$pass", job.PassId), ("$page", page), ("$url", safeUrl), ("$pagination", safeNext), ("$completed", completed ? 1 : 0),
            ("$job", job.Id), ("$time", Time(DateTimeOffset.UtcNow)), ("$count", listings.Count), ("$reason", reason));
        if (completed && pagination?.Kind == NextKind.End) Exec(db, tx, "UPDATE local_passes SET end_page=$page WHERE id=$pass", ("$page", page), ("$pass", job.PassId));
        Exec(db, tx, "UPDATE local_jobs SET page=$page,url=$url WHERE id=$id", ("$page", page), ("$url", safeUrl), ("$id", job.Id));
        tx.Commit(); return true;
    }
    private static void Upsert(SqliteConnection db, SqliteTransaction tx, ListingObservation item, string firstSeen)
    {
        string? prior = Scalar(db, tx, "SELECT json FROM local_listings WHERE source=$source AND external_id=$id", ("$source", item.Source.ToString()), ("$id", item.ExternalId)) as string;
        ListingObservation latest = item;
        if (prior is not null)
        {
            ListingObservation old = LocalJson.Read<ListingObservation>(prior);
            if (old.ObservedAtUtc > item.ObservedAtUtc) return;
            latest = Merge(old, item);
        }
        latest = latest with
        {
            DerivedPricePerSotka = latest.AreaSquareMeters.Parsed > 0 && latest.Price.Parsed.HasValue
            ? latest.Price.Parsed / (latest.AreaSquareMeters.Parsed / 100) : null
        };
        Exec(db, tx, """
            INSERT INTO local_listings VALUES($source,$id,$first,$last,$title,$location,$seller,$price,$json)
            ON CONFLICT(source,external_id) DO UPDATE SET last_seen=excluded.last_seen,title=excluded.title,location=excluded.location,
              seller=excluded.seller,price=excluded.price,json=excluded.json
            """, ("$source", latest.Source.ToString()), ("$id", latest.ExternalId), ("$first", firstSeen), ("$last", Time(latest.ObservedAtUtc)),
            ("$title", latest.Title.Raw ?? ""), ("$location", latest.Location.Raw ?? ""), ("$seller", latest.SellerName.Raw ?? ""),
            ("$price", latest.Price.Parsed), ("$json", LocalJson.Write(latest)));
    }
    private static ListingObservation Merge(ListingObservation old, ListingObservation item)
    {
        static TextValue T(TextValue prior, TextValue value) => value.Presence == Presence.Present ? value : prior;
        static NumberValue N(NumberValue prior, NumberValue value) => value.Parsed.HasValue ? value : prior;
        return item with
        {
            Title = T(old.Title, item.Title),
            Price = N(old.Price, item.Price),
            Latitude = N(old.Latitude, item.Latitude),
            Longitude = N(old.Longitude, item.Longitude),
            CoordinatePrecision = N(old.CoordinatePrecision, item.CoordinatePrecision),
            UnitPrice = N(old.UnitPrice, item.UnitPrice),
            AreaSquareMeters = item.Warnings.Contains("AREA_CONFLICT", StringComparer.Ordinal) ? item.AreaSquareMeters : N(old.AreaSquareMeters, item.AreaSquareMeters),
            Location = T(old.Location, item.Location),
            Description = T(old.Description, item.Description),
            DateText = T(old.DateText, item.DateText),
            Transport = T(old.Transport, item.Transport),
            SellerName = T(old.SellerName, item.SellerName),
            SellerUrl = T(old.SellerUrl, item.SellerUrl),
            SellerType = T(old.SellerType, item.SellerType),
            SellerStatistics = T(old.SellerStatistics, item.SellerStatistics),
            CompletedAdvertisements = N(old.CompletedAdvertisements, item.CompletedAdvertisements),
            Assignment = T(old.Assignment, item.Assignment),
            PhotoUrls = item.PhotoUrls.Length == 0 ? old.PhotoUrls : item.PhotoUrls,
            Badges = item.Badges.Length == 0 ? old.Badges : item.Badges,
            Areas = item.Areas.Length == 0 ? old.Areas : item.Areas
        };
    }

    public ListingPage ReadListings(ListingFilter filter)
    {
        if (filter.Size < 1 || filter.Size > 500 || filter.Offset < 0) throw new ArgumentOutOfRangeException(nameof(filter));
        using SqliteConnection db = Open();
        string where = " WHERE ($source IS NULL OR l.source=$source) AND ($text='' OR local_contains(l.title||' '||l.location||' '||l.seller||' '||l.external_id,$text))";
        List<(string, object?)> args = [("$source", filter.Source?.ToString()), ("$text", filter.Text)];
        if (filter.LinkId is not null) { where += " AND EXISTS(SELECT 1 FROM local_sightings o WHERE o.source=l.source AND o.external_id=l.external_id AND o.link_id=$link)"; args.Add(("$link", filter.LinkId)); }
        if (filter.JobId is not null) { where += " AND EXISTS(SELECT 1 FROM local_sightings o WHERE o.source=l.source AND o.external_id=l.external_id AND o.job_id=$job)"; args.Add(("$job", filter.JobId)); }
        long count = Convert.ToInt64(Scalar(db, null, "SELECT count(*) FROM local_listings l" + where, args.ToArray()), CultureInfo.InvariantCulture);
        args.Add(("$limit", filter.Size)); args.Add(("$offset", filter.Offset));
        using SqliteCommand cmd = Command(db, null, "SELECT json,first_seen,last_seen FROM local_listings l" + where
            + (filter.PriceOrder ? " ORDER BY price,source,external_id" : " ORDER BY last_seen DESC,source,external_id") + " LIMIT $limit OFFSET $offset", args.ToArray());
        using SqliteDataReader r = cmd.ExecuteReader(); List<ListingRow> rows = [];
        while (r.Read()) rows.Add(new(LocalJson.Read<ListingObservation>(r.GetString(0)), DateTimeOffset.Parse(r.GetString(1), CultureInfo.InvariantCulture), DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture)));
        return new(rows.ToArray(), count);
    }
    public HistoryRow[] History(SourceSite source, string externalId, int offset = 0, int size = 100)
    {
        using SqliteConnection db = Open(); using SqliteCommand cmd = Command(db, null, "SELECT id,job_id,page,json FROM local_observations WHERE source=$source AND external_id=$id ORDER BY observed_at DESC,rowid DESC LIMIT $size OFFSET $offset",
            ("$source", source.ToString()), ("$id", externalId), ("$size", Math.Clamp(size, 1, 500)), ("$offset", Math.Max(0, offset)));
        using SqliteDataReader r = cmd.ExecuteReader(); List<HistoryRow> rows = [];
        while (r.Read()) rows.Add(new(r.GetString(0), r.GetString(1), r.GetInt32(2), LocalJson.Read<ListingObservation>(r.GetString(3)))); return rows.ToArray();
    }
    public PageJournal[] Journal(string jobId)
    {
        using SqliteConnection db = Open(); using SqliteCommand cmd = Command(db, null, """
            SELECT p.page,p.url,p.observed_at,p.count,p.reason,p.completed,
              (SELECT count(DISTINCT o.source||':'||o.external_id) FROM local_sightings o JOIN local_listings l
               ON l.source=o.source AND l.external_id=o.external_id JOIN local_passes s ON s.id=o.pass_id
               WHERE o.pass_id=p.pass_id AND o.page=p.page AND l.first_seen>=s.started) AS new_count
            FROM local_pages p WHERE p.job_id=$id ORDER BY p.page
            """, ("$id", jobId));
        using SqliteDataReader r = cmd.ExecuteReader(); List<PageJournal> rows = [];
        while (r.Read()) rows.Add(new(jobId, r.GetInt32(0), r.GetString(1), DateTimeOffset.Parse(r.GetString(2), CultureInfo.InvariantCulture), r.GetInt32(3), r.GetString(4))
        { Completed = r.GetBoolean(5), NewCount = r.GetInt32(6), RepeatCount = Math.Max(0, r.GetInt32(3) - r.GetInt32(6)) }); return rows.ToArray();
    }
    public void ExportJob(string jobId, Stream stream)
    {
        using SqliteConnection db = Open(); using SqliteCommand cmd = Command(db, null, """
            SELECT id,page,json FROM (
              SELECT id,page,json,observed_at,rowid AS sequence FROM local_observations WHERE job_id=$job
              UNION
              SELECT o.id,s.page,o.json,o.observed_at,o.rowid AS sequence FROM local_sightings s
                JOIN local_observations o ON o.id=s.observation_id WHERE s.job_id=$job
            ) ORDER BY page,observed_at,sequence
            """, ("$job", jobId));
        using SqliteDataReader r = cmd.ExecuteReader();
        using System.Text.Json.Utf8JsonWriter writer = new(stream, new() { Indented = true });
        writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("jobId", jobId); writer.WriteString("semantics", "raw-observations; merged state is shown separately in local listing view");
        MapScope? scope = ReadMapScope(jobId);
        if (scope is not null) { writer.WritePropertyName("mapScope"); writer.WriteRawValue(LocalJson.Write(scope)); }
        writer.WriteStartArray("observations");
        while (r.Read()) { writer.WriteStartObject(); writer.WriteString("resultId", r.GetString(0)); writer.WriteNumber("page", r.GetInt32(1)); writer.WritePropertyName("observation"); writer.WriteRawValue(r.GetString(2)); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject();
    }
    private SqliteConnection Open()
    {
        SqliteConnection db = new(connectionString); db.Open();
        db.CreateFunction("local_contains", (string text, string query) => text.Contains(query, StringComparison.OrdinalIgnoreCase));
        return db;
    }
    private static string Time(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static object? Scalar(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using SqliteCommand cmd = Command(db, tx, sql, args); return cmd.ExecuteScalar(); }
    private static int Exec(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using SqliteCommand cmd = Command(db, tx, sql, args); return cmd.ExecuteNonQuery(); }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    { SqliteCommand cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value); return cmd; }
}

/// <summary>File ownership lasts for the application lifetime, including pauses. OS releases it after a crash.</summary>
public sealed class InstanceGuard : IDisposable
{
    private readonly FileStream handle;
    public InstanceGuard(string databasePath)
    {
        string path = System.IO.Path.GetFullPath(databasePath) + ".instance";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        try { handle = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException ex) { throw new InvalidOperationException("Уже запущено приложение с этой базой. Закройте другое окно перед сбором.", ex); }
    }
    public void Dispose() => handle.Dispose();
}
