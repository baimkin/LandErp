using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

internal static partial class IncomingDuplicateDetector
{
    private const int CandidateThreshold = 55;
    [GeneratedRegex(@"(?<!\d)(?<a>\d{2})\s*:\s*(?<b>\d{2})\s*:\s*(?<c>\d{6,7})\s*:\s*(?<d>\d{1,7})(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CadastralPattern();

    [GeneratedRegex(@"\b(?:земельн\w*\s+)?(?:участ(?:ок|ка)|уч\.)\s*(?:№|номер)\s*(?<n>\d{1,6}[а-яa-z]?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlotNumberPattern();

    [GeneratedRegex(@"\b(?:кп|снт|днп)\s*[«""“](?<n>[^»""”\r\n]{2,80})[»""”]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuotedComplexPattern();
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "для","как","это","или","при","без","все","весь","есть","его","еще","уже","под","над","между","рядом",
        "участок","участка","земельный","земельного","продается","продаём","продаем","продажа","собственник",
        "московская","область","около","можно","который","которая","которые","очень","также","только","будет"
    };

    internal static bool ApplyExtractedCadastral(Listing listing, List<string>? changes = null)
    {
        string? normalizedExisting = NormalizeCadastral(listing.CadastralNumber);
        string? extracted = ExtractUniqueCadastral(IdentityText(listing));
        string? value = normalizedExisting ?? extracted;
        if (value == null || string.Equals(value, listing.CadastralNumber, StringComparison.Ordinal)) return false;
        listing.CadastralNumber = value;
        if (changes != null && !changes.Contains("кадастровый номер", StringComparer.Ordinal))
            changes.Add("кадастровый номер");
        return true;
    }

    internal static string? ExtractUniqueCadastral(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        string[] values = CadastralPattern().Matches(text).Select(match =>
            $"{match.Groups["a"].Value}:{match.Groups["b"].Value}:{match.Groups["c"].Value}:{match.Groups["d"].Value}")
            .Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    internal static async Task RefreshAsync(LandErpDbContext db, Listing subject, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (subject.Disposition != CatalogDisposition.Incoming) return;
        ApplyExtractedCadastral(subject);

        decimal? area = subject.AreaSquareMeters;
        string? cadastral = IdentityCadastral(subject);
        string seller = NormalizeSimple(subject.SellerName);
        string location = NormalizeSimple(subject.Location);

        IQueryable<Listing> query = db.Listings.AsNoTracking()
            .Where(item => item.OrganizationId == subject.OrganizationId && item.Id != subject.Id
                && item.Disposition != CatalogDisposition.Fake && item.Disposition != CatalogDisposition.Duplicate);

        if (area is > 0)
        {
            decimal min = area.Value * 0.85m;
            decimal max = area.Value * 1.15m;
            if (cadastral != null)
                query = query.Where(item => item.CadastralNumber == cadastral
                    || item.AreaSquareMeters >= min && item.AreaSquareMeters <= max);
            else
                query = query.Where(item => item.AreaSquareMeters >= min && item.AreaSquareMeters <= max);
        }
        else if (cadastral != null)
        {
            query = query.Where(item => item.CadastralNumber == cadastral);
        }
        else if (seller.Length > 0)
        {
            query = query.Where(item => item.SellerName != null && EF.Functions.ILike(item.SellerName, subject.SellerName!));
        }
        else if (location.Length > 0)
        {
            string token = Tokens(location).FirstOrDefault() ?? "";
            if (token.Length > 0) query = query.Where(item => item.Location != null && EF.Functions.ILike(item.Location, $"%{token}%"));
        }

        Listing[] persisted = await query.OrderByDescending(item => item.ChangedAt).Take(500).ToArrayAsync(cancellationToken);
        Listing[] local = db.Listings.Local.Where(item => item.Id != subject.Id && item.OrganizationId == subject.OrganizationId
            && item.Disposition is not (CatalogDisposition.Fake or CatalogDisposition.Duplicate)).ToArray();

        foreach (Listing other in local.Concat(persisted).GroupBy(item => item.Id).Select(group => group.First()))
        {
            MatchResult? match = Match(subject, other);
            if (match == null || match.Score < CandidateThreshold) continue;

            Guid low = subject.Id.CompareTo(other.Id) < 0 ? subject.Id : other.Id;
            Guid high = subject.Id.CompareTo(other.Id) < 0 ? other.Id : subject.Id;
            string lockKey = $"duplicate:{subject.OrganizationId}:{low}:{high}";
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey},0))", cancellationToken);

            CatalogDuplicateCandidate? existing = db.CatalogDuplicateCandidates.Local.FirstOrDefault(item =>
                item.OrganizationId == subject.OrganizationId
                && ((item.ListingId == subject.Id && item.CandidateListingId == other.Id)
                    || (item.ListingId == other.Id && item.CandidateListingId == subject.Id)))
                ?? await db.CatalogDuplicateCandidates.SingleOrDefaultAsync(item =>
                    item.OrganizationId == subject.OrganizationId
                    && ((item.ListingId == subject.Id && item.CandidateListingId == other.Id)
                        || (item.ListingId == other.Id && item.CandidateListingId == subject.Id)), cancellationToken);

            if (existing == null)
            {
                db.CatalogDuplicateCandidates.Add(new()
                {
                    Id = DataConventions.NewId(),
                    OrganizationId = subject.OrganizationId,
                    ListingId = subject.Id,
                    CandidateListingId = other.Id,
                    Score = match.Score,
                    ReasonsJson = JsonSerializer.Serialize(match.Reasons),
                    Status = DuplicateCandidateStatus.Pending,
                    RecordedAt = now,
                    UpdatedAt = now
                });
            }
            else if (existing.Status == DuplicateCandidateStatus.Pending)
            {
                string reasonsJson = JsonSerializer.Serialize(match.Reasons);
                if (existing.Score != match.Score || !string.Equals(existing.ReasonsJson, reasonsJson, StringComparison.Ordinal))
                {
                    existing.Score = match.Score;
                    existing.ReasonsJson = reasonsJson;
                    existing.UpdatedAt = now;
                }
            }
        }
    }

    private static MatchResult? Match(Listing left, Listing right)
    {
        string? leftCadastral = IdentityCadastral(left);
        string? rightCadastral = IdentityCadastral(right);
        if (leftCadastral != null && rightCadastral != null && leftCadastral != rightCadastral) return null;

        string? leftPlot = ExtractPlotNumber(IdentityText(left));
        string? rightPlot = ExtractPlotNumber(IdentityText(right));
        if (leftPlot != null && rightPlot != null && leftPlot != rightPlot) return null;

        string? leftComplex = ExtractComplex(IdentityText(left));
        string? rightComplex = ExtractComplex(IdentityText(right));
        List<string> reasons = [];
        int score = 0;

        if (leftCadastral != null && leftCadastral == rightCadastral)
        {
            reasons.Add($"Совпадает кадастровый номер {leftCadastral}");
            return new(100, reasons);
        }

        if (leftPlot != null && leftPlot == rightPlot)
        {
            score += leftComplex != null && leftComplex == rightComplex ? 45 : 25;
            reasons.Add(leftComplex != null && leftComplex == rightComplex
                ? $"Совпадают посёлок/товарищество и участок № {leftPlot}"
                : $"Совпадает номер участка № {leftPlot}");
        }

        double description = TextSimilarity(left.Description, right.Description);
        if (description >= .90) { score += 45; reasons.Add("Очень похожее описание"); }
        else if (description >= .78) { score += 35; reasons.Add("Похожее описание"); }
        else if (description >= .65) { score += 25; reasons.Add("Описание заметно совпадает"); }
        else if (description >= .55) { score += 15; reasons.Add("Есть совпадения в описании"); }

        if (left.AreaSquareMeters is > 0 && right.AreaSquareMeters is > 0)
        {
            decimal max = Math.Max(left.AreaSquareMeters.Value, right.AreaSquareMeters.Value);
            decimal delta = Math.Abs(left.AreaSquareMeters.Value - right.AreaSquareMeters.Value) / max;
            if (delta <= .01m) { score += 20; reasons.Add("Практически одинаковая площадь"); }
            else if (delta <= .05m) { score += 12; reasons.Add("Близкая площадь"); }
            else if (delta > .15m) return null;
        }

        double location = TextSimilarity(left.Location, right.Location, minimumTokens: 1);
        if (location >= .75) { score += 15; reasons.Add("Совпадает локация"); }
        else if (location >= .50) { score += 10; reasons.Add("Похожая локация"); }

        string leftSeller = NormalizeSimple(left.SellerName);
        string rightSeller = NormalizeSimple(right.SellerName);
        if (leftSeller.Length >= 3 && leftSeller == rightSeller) { score += 10; reasons.Add("Тот же продавец"); }

        if (left.Price is > 0 && right.Price is > 0)
        {
            decimal maxPrice = Math.Max(left.Price.Value, right.Price.Value);
            if (Math.Abs(left.Price.Value - right.Price.Value) / maxPrice <= .05m)
            { score += 5; reasons.Add("Близкая цена"); }
        }

        if (ExactPhotoUrlOverlap(left.PhotosJson, right.PhotosJson))
        { score += 20; reasons.Add("Совпадает URL фотографии"); }

        bool corroborated = description >= .55 || leftPlot != null && leftPlot == rightPlot
            || location >= .50 || leftSeller.Length >= 3 && leftSeller == rightSeller;
        return score >= CandidateThreshold && corroborated ? new(Math.Min(score, 100), reasons) : null;
    }

    private static string? IdentityCadastral(Listing item) =>
        NormalizeCadastral(item.CadastralNumber) ?? ExtractUniqueCadastral(IdentityText(item));

    private static string? NormalizeCadastral(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        Match match = CadastralPattern().Match(value);
        return !match.Success ? null
            : $"{match.Groups["a"].Value}:{match.Groups["b"].Value}:{match.Groups["c"].Value}:{match.Groups["d"].Value}";
    }

    private static string IdentityText(Listing item) => string.Join("\n",
        new[] { item.Title, item.Location, item.Description }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string? ExtractPlotNumber(string text)
    {
        Match match = PlotNumberPattern().Match(NormalizeConfusables(text));
        return match.Success ? match.Groups["n"].Value.ToLowerInvariant() : null;
    }

    private static string? ExtractComplex(string text)
    {
        Match match = QuotedComplexPattern().Match(NormalizeConfusables(text));
        return match.Success ? NormalizeSimple(match.Groups["n"].Value) : null;
    }

    private static double TextSimilarity(string? left, string? right, int minimumTokens = 8)
    {
        HashSet<string> a = Tokens(NormalizeSimple(left));
        HashSet<string> b = Tokens(NormalizeSimple(right));
        if (a.Count < minimumTokens || b.Count < minimumTokens) return 0;
        int intersection = a.Count(value => b.Contains(value));
        if (intersection == 0) return 0;
        double dice = 2d * intersection / (a.Count + b.Count);
        double containment = (double)intersection / Math.Min(a.Count, b.Count);
        return .65d * dice + .35d * containment;
    }

    private static HashSet<string> Tokens(string text)
    {
        string[] parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Where(value => (value.Length >= 3 || value.All(char.IsDigit) && value.Length >= 2)
                && !StopWords.Contains(value))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string NormalizeSimple(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        string source = NormalizeConfusables(value).ToLowerInvariant();
        StringBuilder result = new(source.Length);
        bool space = false;
        foreach (char ch in source)
        {
            if (char.IsLetterOrDigit(ch))
            {
                result.Append(ch);
                space = false;
            }
            else if (!space)
            {
                result.Append(' ');
                space = true;
            }
        }
        return result.ToString().Trim();
    }

    private static string NormalizeConfusables(string value)
    {
        StringBuilder result = new(value.Length);
        foreach (char ch in value)
        {
            result.Append(ch switch
            {
                'A' => 'А', 'B' => 'В', 'C' => 'С', 'E' => 'Е', 'H' => 'Н', 'K' => 'К',
                'M' => 'М', 'O' => 'О', 'P' => 'Р', 'T' => 'Т', 'X' => 'Х', 'Y' => 'У',
                'a' => 'а', 'c' => 'с', 'e' => 'е', 'o' => 'о', 'p' => 'р', 'x' => 'х', 'y' => 'у',
                _ => ch
            });
        }
        return result.ToString();
    }

    private static bool ExactPhotoUrlOverlap(string leftJson, string rightJson)
    {
        try
        {
            HashSet<string> left = (JsonSerializer.Deserialize<string[]>(leftJson) ?? [])
                .Where(value => !string.IsNullOrWhiteSpace(value)).ToHashSet(StringComparer.Ordinal);
            return (JsonSerializer.Deserialize<string[]>(rightJson) ?? []).Any(left.Contains);
        }
        catch (JsonException) { return false; }
    }

    private sealed record MatchResult(int Score, string[] Reasons)
    {
        public MatchResult(int score, IReadOnlyList<string> reasons) : this(score, reasons.ToArray()) { }
    }
}
