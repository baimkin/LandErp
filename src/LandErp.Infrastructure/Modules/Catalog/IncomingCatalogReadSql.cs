using System.Data.Common;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LandErp.Infrastructure.Modules.Catalog;

// Request-local SQL only. EF still translates the existing business predicates;
// MATERIALIZED sets prevent their history subqueries from being expanded per metric.
// Never used by writes, never persisted, and never shared between organizations/requests.
internal sealed class IncomingCatalogReadSql(LandErpDbContext db, Guid organizationId)
{
    private readonly List<string> definitions = [];
    private readonly List<NpgsqlParameter> parameters = [];
    private int queryNumber;

    internal void AddSet(string name, IQueryable<Guid> query)
        => definitions.Add($"{name} AS MATERIALIZED (SELECT * FROM ({Translate(query)}) AS ids(\"Value\"))");

    internal void AddSearchMemberships()
    {
        // Collapse repeated runs before joining mutable search/group metadata. The distinct
        // key contains only listing/search, not a wide copy of group metadata per observation.
        var history = (from observation in db.ListingObservations.AsNoTracking()
            join job in db.CollectionJobs.AsNoTracking() on observation.JobId equals job.Id
            where job.OrganizationId == organizationId
            select new { observation.ListingId, SearchId = job.SearchId }).Distinct();
        var memberships = from observation in history
            join search in db.SearchConfigurations.AsNoTracking() on observation.SearchId equals search.Id
            join groupItem in db.SearchGroups.AsNoTracking() on search.SearchGroupId equals (Guid?)groupItem.Id into groups
            from groupItem in groups.DefaultIfEmpty()
            where search.OrganizationId == organizationId
            select new { observation.ListingId, SearchId = search.Id, GroupId = search.SearchGroupId,
                GroupActive = groupItem != null && groupItem.OrganizationId == organizationId && groupItem.Active };
        definitions.Add($"incoming_search_memberships AS MATERIALIZED ({Translate(memberships)})");
    }

    internal IQueryable<Guid> Ids(string name) => db.Database.SqlQueryRaw<Guid>(name switch
    {
        "incoming_price_changed" => "SELECT \"Value\" FROM incoming_price_changed",
        "incoming_returned" => "SELECT \"Value\" FROM incoming_returned",
        "incoming_reviewed" => "SELECT \"Value\" FROM incoming_reviewed",
        "incoming_duplicates" => "SELECT \"Value\" FROM incoming_duplicates",
        "incoming_processed" => "SELECT \"Value\" FROM incoming_processed",
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    });
    internal IQueryable<Guid> GroupIds(Guid id) => db.Database.SqlQueryRaw<SearchMembership>(
            "SELECT * FROM incoming_search_memberships")
        .Where(item => item.GroupId == id && item.GroupActive).Select(item => item.ListingId);
    internal IQueryable<Guid> SearchIds(Guid id) => db.Database.SqlQueryRaw<SearchMembership>(
            "SELECT * FROM incoming_search_memberships")
        .Where(item => item.SearchId == id).Select(item => item.ListingId);

    internal IQueryable<FilterMatch> AddMatches(string name,
        IReadOnlyDictionary<Guid, Expression<Func<Listing, bool>>> conditions)
    {
        List<string> branches = [];
        foreach (var (id, predicate) in conditions)
        {
            string parameterName = $"preset_{parameters.Count}";
            parameters.Add(new(parameterName, id));
            var ids = db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId)
                .Where(predicate).Select(item => item.Id);
            branches.Add($"SELECT @{parameterName} AS \"PresetId\", ids.\"ListingId\" FROM ({Translate(ids)}) AS ids(\"ListingId\")");
        }
        string body = branches.Count == 0
            ? "SELECT NULL::uuid AS \"PresetId\", NULL::uuid AS \"ListingId\" WHERE FALSE"
            : string.Join("\nUNION ALL\n", branches);
        definitions.Add($"{name} AS MATERIALIZED ({body})");
        return db.Database.SqlQueryRaw<FilterMatch>(name switch
        {
            "incoming_saved_matches" => "SELECT * FROM incoming_saved_matches",
            "incoming_alternative_matches" => "SELECT * FROM incoming_alternative_matches",
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        });
    }

    internal Task<Listing[]> ReadItemsAsync(IQueryable<Listing> query, CancellationToken cancellationToken)
    {
        string sql = With(Translate(query));
        return db.Listings.FromSqlRaw(sql, ParameterCopies()).AsNoTracking().ToArrayAsync(cancellationToken);
    }

    internal IQueryable<FilterMatch> AddAlternatives(Expression<Func<Listing, bool>> textAndSlice)
    {
        // Alternative filters reuse the saved matches. Only text and the active slice are
        // additional here; the visible previous preset/group must never narrow an alternative.
        var ids = db.Listings.Where(item => item.OrganizationId == organizationId).Where(textAndSlice).Select(item => item.Id);
        var matches = db.Database.SqlQueryRaw<FilterMatch>("SELECT * FROM incoming_saved_matches")
            .Where(match => ids.Contains(match.ListingId));
        definitions.Add($"incoming_alternative_matches AS MATERIALIZED ({Translate(matches)})");
        return db.Database.SqlQueryRaw<FilterMatch>("SELECT * FROM incoming_alternative_matches");
    }

    internal async Task<int[]> CountAsync(IQueryable<Listing> items,
        IReadOnlyList<Expression<Func<Listing, bool>>> predicates, CancellationToken cancellationToken)
    {
        string query = Translate(IncomingCatalogQuery.CountProjection(items, predicates));
        string sql = With($"SELECT * FROM ({query}) AS aggregate(\"Value\")");
        int[][] rows = await db.Database.SqlQueryRaw<int[]>(sql, ParameterCopies()).ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault() ?? new int[predicates.Count];
    }

    internal async Task<int[]> CountInformationAsync(IQueryable<Listing> items,
        IReadOnlyList<Expression<Func<Listing, bool>>> summaryAndAll,
        IReadOnlyList<IncomingFilterPresetView> presets, Guid?[] groups, CancellationToken cancellationToken)
    {
        // Count the (PresetId, ListingId) relation once. Group counts are DISTINCT unions,
        // not SUM(preset counts), and never conditional tests over every organization row.
        List<string> values = [];
        List<string> counts = [];
        foreach (var preset in presets)
        {
            string id = AddParameter(preset.Id);
            string group = preset.Criteria.SearchGroupId is Guid groupId ? AddParameter(groupId) : "NULL::uuid";
            values.Add($"({id}, {group})");
            counts.Add($"COALESCE((SELECT \"Count\" FROM incoming_preset_counts WHERE \"Id\"={id}),0)");
        }
        string metadata = values.Count == 0 ? "SELECT NULL::uuid, NULL::uuid WHERE FALSE" : "VALUES " + string.Join(',', values);
        definitions.Add($"incoming_preset_groups(\"PresetId\",\"GroupId\") AS ({metadata})");
        definitions.Add("""
            incoming_preset_counts AS MATERIALIZED (
                SELECT "PresetId" AS "Id", count(*)::int AS "Count"
                FROM incoming_alternative_matches GROUP BY "PresetId")
            """);
        definitions.Add("""
            incoming_group_counts AS MATERIALIZED (
                SELECT g."GroupId" AS "Id", count(DISTINCT m."ListingId")::int AS "Count"
                FROM incoming_alternative_matches m JOIN incoming_preset_groups g ON g."PresetId"=m."PresetId"
                GROUP BY g."GroupId")
            """);
        foreach (Guid? group in groups)
        {
            string id = group is Guid value ? AddParameter(value) : "NULL::uuid";
            counts.Add($"COALESCE((SELECT \"Count\" FROM incoming_group_counts WHERE \"Id\" IS NOT DISTINCT FROM {id}),0)");
        }
        int last = summaryAndAll.Count;
        string summary = Translate(IncomingCatalogQuery.CountProjection(items, summaryAndAll));
        string sql = With($"SELECT aggregate.\"Value\"[1:{last - 1}] || ARRAY[{string.Join(',', counts)}]::int[] || "
            + $"ARRAY[aggregate.\"Value\"[{last}]] AS \"Value\" FROM ({summary}) AS aggregate(\"Value\")");
        int[][] rows = await db.Database.SqlQueryRaw<int[]>(sql, ParameterCopies()).ToArrayAsync(cancellationToken);
        return rows.SingleOrDefault() ?? new int[last + presets.Count + groups.Length];
    }

    private string AddParameter(Guid value)
    {
        string name = $"value_{parameters.Count}";
        parameters.Add(new(name, value));
        return "@" + name;
    }

    private string With(string sql) => "WITH " + string.Join(",\n", definitions) + "\n" + sql;
    private object[] ParameterCopies() => parameters.Select(item => (object)item.Clone()).ToArray();

    private string Translate(IQueryable query)
    {
        using DbCommand command = query.CreateDbCommand();
        string sql = command.CommandText.TrimEnd().TrimEnd(';');
        string prefix = $"q{queryNumber++}_";
        foreach (NpgsqlParameter parameter in command.Parameters)
        {
            string previousName = parameter.ParameterName.TrimStart('@');
            string newName = prefix + previousName;
            sql = Regex.Replace(sql, "@" + Regex.Escape(previousName) + @"\b", "@" + newName);
            var copy = parameter.Clone();
            copy.ParameterName = newName;
            parameters.Add(copy);
        }
        return sql;
    }

    internal sealed class SearchMembership
    {
        public Guid ListingId { get; set; }
        public Guid SearchId { get; set; }
        public Guid? GroupId { get; set; }
        public bool GroupActive { get; set; }
    }
    internal sealed class FilterMatch
    {
        public Guid PresetId { get; set; }
        public Guid ListingId { get; set; }
    }
}
