using LandErp.Collector.Contracts.V1;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace LandErp.ParserSpike.ServerIntegration;

public sealed record PendingDelivery(long Sequence, CollectionResult Result, int Attempts);
public sealed record LocalServerWork(CollectionWork Work, string? LocalJobId, string ResultReasonCode = "");
public sealed record DeliverySummary(Guid JobId, int Collected, int Accepted, int Duplicates,
    int NewListings, int ChangedListings, string Outcome)
{
    public string Display => $"Собрано {Collected}; сервер принял {Accepted}; без изменений {Duplicates}; новых {NewListings}; обновлено {ChangedListings}.";
}

/// <summary>Delivery is committed before network use; acknowledgement is committed only after a matching receipt.</summary>
public sealed class ServerOutbox
{
    private readonly string connection;
    public ServerOutbox(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connection = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS server_outbox(sequence INTEGER PRIMARY KEY AUTOINCREMENT,result_id TEXT NOT NULL UNIQUE,
              json TEXT NOT NULL,acked INTEGER NOT NULL DEFAULT 0,attempts INTEGER NOT NULL DEFAULT 0,last_code TEXT NOT NULL DEFAULT 'Pending');
            CREATE TABLE IF NOT EXISTS server_work(id INTEGER PRIMARY KEY CHECK(id=1),json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS server_identity(id INTEGER PRIMARY KEY CHECK(id=1),origin TEXT NOT NULL,agent_id TEXT NOT NULL);
            """;
        command.ExecuteNonQuery();
        EnsureColumn(db, "server_outbox", "terminal_state", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(db, "server_outbox", "accepted_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "server_outbox", "duplicate_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "server_outbox", "new_count", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(db, "server_outbox", "changed_count", "INTEGER NOT NULL DEFAULT 0");
    }
    public void Bind(ServerConnection connection, bool allowUnboundData)
    {
        using SqliteConnection db = Open(); using SqliteTransaction transaction = db.BeginTransaction();
        string? ownerOrigin = null;
        string? ownerAgent = null;
        using SqliteCommand read = Command(db, transaction, "SELECT origin,agent_id FROM server_identity WHERE id=1");
        using SqliteDataReader reader = read.ExecuteReader();
        if (reader.Read())
        {
            ownerOrigin = reader.GetString(0);
            ownerAgent = reader.GetString(1);
        }
        reader.Close();
        long data = Convert.ToInt64(Scalar(db, transaction,
            "SELECT (SELECT count(*) FROM server_outbox WHERE acked=0)+(SELECT count(*) FROM server_work)"), System.Globalization.CultureInfo.InvariantCulture);
        bool hasOwner = ownerOrigin != null && ownerAgent != null;
        bool matches = ownerOrigin == connection.Origin.AbsoluteUri && ownerAgent == connection.AgentId.ToString();
        if (matches) return;
        if (data > 0 && hasOwner)
            throw new InvalidOperationException("Есть незавершённое задание или результаты для другого Server или Parser.");
        if (data > 0 && !hasOwner && !allowUnboundData)
            throw new InvalidOperationException("Нельзя определить владельца прежней очереди доставки. Восстановите старое подключение.");
        using SqliteCommand insert = Command(db, transaction, """
            INSERT INTO server_identity VALUES(1,$origin,$agent)
            ON CONFLICT(id) DO UPDATE SET origin=excluded.origin,agent_id=excluded.agent_id
            """,
            ("$origin", connection.Origin.AbsoluteUri), ("$agent", connection.AgentId.ToString()));
        insert.ExecuteNonQuery(); transaction.Commit();
    }
    public void SaveWork(LocalServerWork? work)
    {
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand();
        command.CommandText = work == null ? "DELETE FROM server_work WHERE id=1" : "INSERT INTO server_work VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json";
        if (work != null) command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(work, CollectionJson.Options));
        command.ExecuteNonQuery();
    }
    public LocalServerWork? ReadWork()
    {
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand(); command.CommandText = "SELECT json FROM server_work WHERE id=1";
        return command.ExecuteScalar() is string json ? JsonSerializer.Deserialize<LocalServerWork>(json, CollectionJson.Options) : null;
    }
    public void Enqueue(CollectionResult result)
        => EnqueueMany([result]);

    public void EnqueueMany(IReadOnlyList<CollectionResult> results)
    {
        using SqliteConnection db = Open(); using SqliteTransaction transaction = db.BeginTransaction();
        foreach (CollectionResult result in results)
        {
            using SqliteCommand command = db.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "INSERT OR IGNORE INTO server_outbox(result_id,json) VALUES($id,$json)";
            command.Parameters.AddWithValue("$id", result.ResultId.ToString()); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(result, CollectionJson.Options)); command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    public PendingDelivery[] Pending()
    {
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand();
        command.CommandText = "SELECT sequence,json,attempts FROM server_outbox WHERE acked=0 ORDER BY sequence";
        using SqliteDataReader reader = command.ExecuteReader(); List<PendingDelivery> results = [];
        while (reader.Read()) results.Add(new(reader.GetInt64(0), JsonSerializer.Deserialize<CollectionResult>(reader.GetString(1), CollectionJson.Options)!, reader.GetInt32(2)));
        return results.ToArray();
    }
    public bool HasDelivery(Guid job, Guid lease)
    {
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand(); command.CommandText = "SELECT json FROM server_outbox";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read()) { CollectionResult value = JsonSerializer.Deserialize<CollectionResult>(reader.GetString(0), CollectionJson.Options)!; if (value.JobId == job && value.LeaseId == lease) return true; }
        return false;
    }
    public void RecordAttempt(PendingDelivery delivery, string code, bool acknowledged, string terminalState = "",
        CollectionReceipt? receipt = null)
    {
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand();
        command.CommandText = "UPDATE server_outbox SET attempts=attempts+1,last_code=$code,acked=$acked,terminal_state=$terminal,"
            + "accepted_count=$accepted,duplicate_count=$duplicates,new_count=$new,changed_count=$changed WHERE sequence=$sequence AND result_id=$id";
        command.Parameters.AddWithValue("$code", code); command.Parameters.AddWithValue("$acked", acknowledged ? 1 : 0);
        command.Parameters.AddWithValue("$terminal", terminalState);
        command.Parameters.AddWithValue("$accepted", receipt?.Accepted ?? 0);
        command.Parameters.AddWithValue("$duplicates", receipt?.Duplicates ?? 0);
        command.Parameters.AddWithValue("$new", receipt?.NewListings ?? 0);
        command.Parameters.AddWithValue("$changed", receipt?.ChangedListings ?? 0);
        command.Parameters.AddWithValue("$sequence", delivery.Sequence); command.Parameters.AddWithValue("$id", delivery.Result.ResultId.ToString()); command.ExecuteNonQuery();
    }
    public DeliverySummary? Summary(Guid jobId)
    {
        using SqliteConnection db = Open(); using SqliteCommand command = db.CreateCommand();
        command.CommandText = """
            SELECT COALESCE(SUM(json_array_length(json_extract(json,'$.observations'))),0),
              COALESCE(SUM(accepted_count),0),COALESCE(SUM(duplicate_count),0),COALESCE(SUM(new_count),0),
              COALESCE(SUM(changed_count),0),COALESCE(MAX(CASE WHEN json_extract(json,'$.final')=1 THEN last_code END),'')
            FROM server_outbox WHERE json_extract(json,'$.jobId')=$job AND acked=1
            """;
        command.Parameters.AddWithValue("$job", jobId.ToString());
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        int collected = Convert.ToInt32(reader.GetInt64(0), System.Globalization.CultureInfo.InvariantCulture);
        if (collected == 0 && reader.GetString(5).Length == 0) return null;
        return new(jobId, collected,
            Convert.ToInt32(reader.GetInt64(1), System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(2), System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(3), System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetInt64(4), System.Globalization.CultureInfo.InvariantCulture), reader.GetString(5));
    }
    public void SupersedeLease(Guid job, Guid lease)
    {
        foreach (PendingDelivery pending in Pending().Where(item => item.Result.JobId == job && item.Result.LeaseId != lease))
            RecordAttempt(pending, "SupersededLeaseLocalDataRetained", true, "Superseded");
    }
    public void RetainLocally(Guid job, string code)
    {
        // Only a definitive server rejection may end retries. Payloads and local observations are retained.
        foreach (PendingDelivery pending in Pending().Where(item => item.Result.JobId == job))
            RecordAttempt(pending, code, true, "RetainedLocally");
    }
    public async Task FlushAsync(ServerAdapter adapter, CancellationToken token)
    {
        RecoverRejectedPhotoPayloads();
        ServerDeliveryException? firstPermanent = null;
        HashSet<Guid> blockedJobs = [];
        foreach (PendingDelivery delivery in Pending())
        {
            if (blockedJobs.Contains(delivery.Result.JobId)) continue;
            try
            {
                CollectionReceipt receipt = await adapter.SendResultAsync(delivery.Result, token).ConfigureAwait(false);
                if (receipt.ResultId != delivery.Result.ResultId) throw new ServerDeliveryException("INVALID_RECEIPT", false);
                RecordAttempt(delivery, receipt.Status, true, "Acknowledged", receipt);
            }
            catch (ServerDeliveryException exception)
            {
                RecordAttempt(delivery, exception.Code, false);
                if (exception.Code == "RESULT_INVALID" && delivery.Result.Observations.Any(item => item.Data.PhotoUrls.Length > 100))
                {
                    RecoverRejectedPhotoPayloads();
                    await FlushAsync(adapter, token).ConfigureAwait(false);
                    return;
                }
                if (exception.Retryable) throw; // A transport failure can affect every following delivery.
                blockedJobs.Add(delivery.Result.JobId);
                firstPermanent ??= exception;
            }
        }
        if (firstPermanent != null) throw firstPermanent;
    }
    private void RecoverRejectedPhotoPayloads()
    {
        using SqliteConnection db = Open();
        using SqliteTransaction transaction = db.BeginTransaction();
        using SqliteCommand read = db.CreateCommand(); read.Transaction = transaction;
        // Only HTTP 400 proves this payload was rejected. Never rewrite an ambiguous delivery.
        read.CommandText = "SELECT sequence,json FROM server_outbox WHERE acked=0 AND last_code='RESULT_INVALID'";
        List<(long Sequence, string Json)> rejected = [];
        using (SqliteDataReader reader = read.ExecuteReader())
            while (reader.Read()) rejected.Add((reader.GetInt64(0), reader.GetString(1)));
        foreach (var item in rejected)
        {
            CollectionResult original = JsonSerializer.Deserialize<CollectionResult>(item.Json, CollectionJson.Options)!;
            if (!original.Observations.Any(observation => observation.Data.PhotoUrls.Length > 100)) continue;
            // Preserve original payload and IDs; the repaired payload is a new idempotent delivery.
            using SqliteCommand archive = db.CreateCommand(); archive.Transaction = transaction;
            archive.CommandText = "CREATE TABLE IF NOT EXISTS server_outbox_recovery(sequence INTEGER PRIMARY KEY,json TEXT NOT NULL)";
            archive.ExecuteNonQuery();
            archive.CommandText = "INSERT OR IGNORE INTO server_outbox_recovery VALUES($sequence,$json)";
            archive.Parameters.AddWithValue("$sequence", item.Sequence); archive.Parameters.AddWithValue("$json", item.Json);
            archive.ExecuteNonQuery();
            CollectionResult repaired = original with
            {
                ResultId = Guid.CreateVersion7(),
                Observations = original.Observations.Select(observation => observation.Data.PhotoUrls.Length <= 100
                    ? observation : observation with
                    {
                        ObservationKey = Guid.CreateVersion7().ToString(),
                        Data = observation.Data with { PhotoUrls = observation.Data.PhotoUrls.Distinct(StringComparer.Ordinal).Take(100).ToArray() }
                    }).ToArray()
            };
            using SqliteCommand update = db.CreateCommand(); update.Transaction = transaction;
            update.CommandText = "UPDATE server_outbox SET result_id=$id,json=$json,attempts=0,last_code='Pending' WHERE sequence=$sequence";
            update.Parameters.AddWithValue("$id", repaired.ResultId.ToString());
            update.Parameters.AddWithValue("$json", JsonSerializer.Serialize(repaired, CollectionJson.Options));
            update.Parameters.AddWithValue("$sequence", item.Sequence); update.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    private SqliteConnection Open() { SqliteConnection db = new(connection); db.Open(); return db; }
    private static void EnsureColumn(SqliteConnection db, string table, string column, string declaration)
    {
        using SqliteCommand info = db.CreateCommand(); info.CommandText = "PRAGMA table_info(" + table + ")";
        using SqliteDataReader reader = info.ExecuteReader(); bool exists = false;
        while (reader.Read()) if (reader.GetString(1) == column) exists = true;
        reader.Close();
        if (!exists) { using SqliteCommand alter = db.CreateCommand(); alter.CommandText = "ALTER TABLE " + table + " ADD COLUMN " + column + " " + declaration; alter.ExecuteNonQuery(); }
    }
    private static object? Scalar(SqliteConnection db, SqliteTransaction transaction, string sql)
    { using SqliteCommand command = Command(db, transaction, sql); return command.ExecuteScalar(); }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction transaction, string sql, params (string Name, object Value)[] args)
    {
        SqliteCommand command = db.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach (var (name, value) in args) command.Parameters.AddWithValue(name, value); return command;
    }
}
