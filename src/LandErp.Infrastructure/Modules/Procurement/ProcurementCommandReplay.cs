using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.IdentityAccess.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

/// <summary>
/// Durable replay for the three Procurement additions in B1-01 only. Their existing,
/// append-only audit event is committed in the same transaction as the business fact.
/// Its PK is the supplied command ID; no receipt table, cache or startup DDL is needed.
/// These audit events must be retained while their command IDs may be retried.
/// </summary>
internal sealed record ProcurementCommandReplay(Guid AuditId, string Action, string? PayloadHash,
    Guid? ExistingCaseId, Guid? ExistingResultId)
{
    public static async Task<ProcurementCommandReplay> BeginAsync(LandErpDbContext db, AccessContext context,
        Subject subject, Guid? commandId, string action, object payload, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction == null)
            throw new InvalidOperationException("Procurement replay requires the business transaction.");
        if (commandId == Guid.Empty) throw new ArgumentException("Идентификатор команды не должен быть пустым.");
        // Legacy callers remain source/wire compatible, but must supply a command ID for replay.
        if (commandId == null) return new(DataConventions.NewId(), action, null, null, null);

        string hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(payload)));
        string key = "ProcurementCommand:" + commandId.Value.ToString("N");
        // Always take the command lock BEFORE a case lock. A shared key must serialize even
        // across actors/organizations because audit IDs have one global primary key.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({key},0))", cancellationToken);
        AuditEvent? previous = await db.AuditEvents.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == commandId.Value, cancellationToken);
        if (previous == null) return new(commandId.Value, action, hash, null, null);
        if (previous.OrganizationId != context.OrganizationId || previous.ActorId != subject.UserId)
            throw new AccessDeniedException();

        using JsonDocument json = JsonDocument.Parse(previous.Changes);
        if (previous.Action != action || previous.ObjectType != "PropertyCase"
            || !json.RootElement.TryGetProperty("CommandReplay", out JsonElement replay)
            || replay.ValueKind != JsonValueKind.Object
            || !replay.TryGetProperty("Version", out JsonElement version) || !version.TryGetInt32(out int value) || value != 1
            || !replay.TryGetProperty("PayloadHash", out JsonElement storedHash) || storedHash.ValueKind != JsonValueKind.String
            || !string.Equals(storedHash.GetString(), hash, StringComparison.Ordinal)
            || !replay.TryGetProperty("ResultId", out JsonElement result) || result.ValueKind != JsonValueKind.String
            || !result.TryGetGuid(out Guid resultId))
            throw new ArgumentException("Эта команда уже использована с другими данными. Проверьте сохранённую запись перед новым действием.");

        // The caller still checks current permissions and visibility before returning the result.
        return new(previous.Id, action, hash, previous.ObjectId, resultId);
    }

    public void Record(LandErpDbContext db, AccessContext context, Subject subject, Guid caseId, Guid resultId,
        object changes, string correlationId, DateTimeOffset recordedAt)
    {
        if (db.Database.CurrentTransaction == null || ExistingResultId != null)
            throw new InvalidOperationException("Replay audit must be recorded once in the business transaction.");
        JsonObject details = JsonSerializer.SerializeToNode(changes)?.AsObject()
            ?? throw new InvalidOperationException("Procurement audit requires an object.");
        if (PayloadHash != null)
            details["CommandReplay"] = JsonSerializer.SerializeToNode(new { Version = 1, PayloadHash, ResultId = resultId });
        // Preserve the action and all existing business fields used by the semantic audit UI.
        db.AuditEvents.Add(new AuditEvent
        {
            Id = AuditId, OrganizationId = context.OrganizationId, ActorId = subject.UserId,
            Action = Action, ObjectType = "PropertyCase", ObjectId = caseId, Changes = details.ToJsonString(),
            CorrelationId = correlationId.Length <= 64 ? correlationId : DataConventions.NewId().ToString(),
            RecordedAt = recordedAt
        });
    }
}
