using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LandErp.ParserSpike.LocalCollection;

public sealed record JobProgressRow(CollectionJob Job, bool IsMap, int? ExpectedCount, int Collected, int? SourceCountHint,
    CollectionCompletionKind? CompletionKind = null);
public sealed record JobPage(JobProgressRow[] Rows, long Total, int Offset);

public sealed partial class LocalStore
{
    public Dictionary<JobState, long> JobStateCounts()
    {
        using SqliteConnection db = Open(); using SqliteCommand command = Command(db, null, "SELECT state,count(*) FROM local_jobs GROUP BY state");
        using SqliteDataReader reader = command.ExecuteReader(); Dictionary<JobState, long> counts = [];
        while (reader.Read()) counts.Add(Enum.Parse<JobState>(reader.GetString(0)), reader.GetInt64(1));
        return counts;
    }
    public SearchLink[] ResultLinks(bool serverWork)
    {
        using SqliteConnection db = Open();
        using SqliteCommand command = Command(db, null, """
            SELECT DISTINCT l.id,l.label,l.url,l.source,l.selected,l.enabled,l.revision,l.archived
            FROM local_jobs j JOIN local_links l ON l.id=j.link_id WHERE (l.id LIKE 'server-%')=$server ORDER BY l.label,l.id
            """, ("$server", serverWork ? 1 : 0));
        using SqliteDataReader reader = command.ExecuteReader(); List<SearchLink> links = [];
        while (reader.Read()) links.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), Enum.Parse<SourceSite>(reader.GetString(3)),
            reader.GetBoolean(4), reader.GetBoolean(5), reader.GetInt32(6), reader.GetBoolean(7)));
        return links.ToArray();
    }

    public CollectionJob? Job(string id)
    { using SqliteConnection db = Open(); return ReadJobs(db, null, " WHERE id=$id", ("$id", id)).SingleOrDefault(); }

    /// <summary>One bounded snapshot and two commands, independent of history length.
    /// Active workers are read separately; completed history is refreshed only on changes.</summary>
    public JobPage ReadJobPage(bool serverWork, int offset = 0, int size = 50, bool activeOnly = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (size is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(size));
        using SqliteConnection db = Open();
        using SqliteTransaction tx = db.BeginTransaction(deferred: true);
        // Match the existing partial active-link index; active polling need not scan history.
        string where = " WHERE (link_id LIKE 'server-%')=$server AND " + (activeOnly
            ? "state IN ('Pending','Running','AwaitingManualAction','PausedByUser') AND state<>'Pending'"
            : "state NOT IN ('Running','PausedByUser','AwaitingManualAction')");
        long total = Convert.ToInt64(Scalar(db, tx, "SELECT count(*) FROM local_jobs" + where, ("$server", serverWork ? 1 : 0)), CultureInfo.InvariantCulture);
        int start = total == 0 ? 0 : (int)Math.Min(offset, (total - 1) / size * size);
        using SqliteCommand command = Command(db, tx, """
            WITH selected AS (
              SELECT rowid AS sequence,* FROM local_jobs
            """ + where + " ORDER BY rowid DESC LIMIT $size OFFSET $offset) " + """
            SELECT j.id,j.batch_id,j.link_id,j.revision,j.source,j.url,j.pass_id,j.page,j.page_limit,
              j.state,j.reason,j.owner,j.token,j.started,m.json,c.json,
              COALESCE((SELECT MAX(count) FROM local_pages p WHERE p.pass_id=j.pass_id),0)
            FROM selected j LEFT JOIN local_map_scopes m ON m.pass_id=j.pass_id
              LEFT JOIN local_job_completion c ON c.job_id=j.id ORDER BY j.sequence DESC
            """, ("$server", serverWork ? 1 : 0), ("$size", size), ("$offset", start));
        List<JobProgressRow> rows = [];
        using (SqliteDataReader reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                CollectionJob job = new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    Enum.Parse<SourceSite>(reader.GetString(4)), reader.GetString(5), reader.GetString(6), reader.GetInt32(7), reader.GetInt32(8),
                    Enum.Parse<JobState>(reader.GetString(9)), reader.GetString(10), reader.IsDBNull(11) ? null : reader.GetString(11),
                    reader.IsDBNull(12) ? null : reader.GetString(12), DateTimeOffset.Parse(reader.GetString(13), CultureInfo.InvariantCulture));
                MapScope? map = reader.IsDBNull(14) ? null : LocalJson.Read<MapScope>(reader.GetString(14));
                CollectionCompletionFacts? completion = reader.IsDBNull(15) ? null : LocalJson.Read<CollectionCompletionFacts>(reader.GetString(15));
                rows.Add(new(job, map != null, map?.ExpectedCount, reader.GetInt32(16), completion?.SourceCountHint, completion?.Kind));
            }
        }
        tx.Commit();
        return new(rows.ToArray(), total, start);
    }
}
