namespace LandErp.ParserSpike.LocalCollection;

public sealed record NormalizedSearch(string Url, string Key, SourceSite Source, string[] Warnings);

public static class SearchUrls
{
    private static readonly HashSet<string> Tracking = new(StringComparer.OrdinalIgnoreCase)
    { "context", "source", "source_search", "source_string", "src", "iid", "cd", "from", "ref", "utm_source", "utm_medium", "utm_campaign", "utm_content", "utm_term" };
    private static readonly System.Text.RegularExpressions.Regex Sensitive = new(
        @"(?:token|password|passwd|secret|authorization|cookie|session|csrf|jwt|api[_-]?key|auth[_-]?key)|^(?:auth|sid|key|signature)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool IsPublic(Uri uri, SourceSite source) => uri.Scheme == "https" && uri.IsDefaultPort
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        (uri.Host.Equals(source == SourceSite.Avito ? "avito.ru" : "cian.ru", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith(source == SourceSite.Avito ? ".avito.ru" : ".cian.ru", StringComparison.OrdinalIgnoreCase));

    public static NormalizedSearch Normalize(string value, SourceSite? manualSource = null)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? uri)) throw new ArgumentException("Введите HTTPS-ссылку выдачи Avito или Cian.");
        SourceSite source = IsPublic(uri, SourceSite.Avito) ? SourceSite.Avito : IsPublic(uri, SourceSite.Cian)
            ? SourceSite.Cian : throw new ArgumentException("Разрешены только публичные HTTPS-ссылки Avito/Cian без пароля и фрагмента.");
        if (manualSource.HasValue && manualSource != source) throw new ArgumentException("Выбранный источник не соответствует ссылке.");
        List<KeyValuePair<string, string>> filters = [];
        List<string> warnings = [];
        foreach (string pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = pair.Split('=', 2);
            string key = Uri.UnescapeDataString(parts[0].Replace('+', ' '));
            string data = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (Tracking.Contains(key) || key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)) continue;
            if (Sensitive.IsMatch(key)) throw new ArgumentException("Ссылка содержит параметр авторизации. Используйте публичный адрес без секретов.");
            if (key.Length > 200 || key.Any(char.IsControl)) throw new ArgumentException("Некорректное имя параметра ссылки.");
            if (key == "p") { warnings.Add("Параметр страницы исключён из идентификатора поиска; сбор начинается с первой страницы."); continue; }
            if (data.Length > 8000 || data.Any(char.IsControl)) throw new ArgumentException("FILTER_VALUE_INVALID");
            filters.Add(new(key, data));
        }
        string query = string.Join('&', filters.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal)
            .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)));
        string result = uri.GetLeftPart(UriPartial.Path) + (query.Length == 0 ? "" : "?" + query);
        if (IsDetail(result, source)) warnings.Add("Ссылка сохранена. Парсинг отдельного объявления пока не поддерживается; очередь сообщит об этом явно.");
        return new(result, result, source, warnings.ToArray());
    }

    /// <summary>Checkpoint URLs came from DOM. Retain their page number after applying the same safe filter rules.</summary>
    public static string SafePage(string value, SourceSite source)
    {
        NormalizedSearch search = Normalize(value, source);
        Uri uri = new(value);
        string? page = uri.Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("p=", StringComparison.Ordinal));
        if (page is null) return search.Url;
        if (!int.TryParse(page.AsSpan(2), out int number) || number < 1 || number > 10000) throw new ArgumentException("PAGE_NUMBER_INVALID");
        return search.Url + (new Uri(search.Url).Query.Length == 0 ? "?" : "&") + "p=" + number;
    }
    public static int PageNumber(string value)
    {
        string? p = new Uri(value).Query.TrimStart('?').Split('&').FirstOrDefault(x => x.StartsWith("p=", StringComparison.Ordinal));
        return p is not null && int.TryParse(p.AsSpan(2), out int n) ? n : 1;
    }
    public static bool SameSearch(string current, string next, SourceSite source)
    {
        Uri a = new(Normalize(current, source).Url), b = new(Normalize(next, source).Url);
        if (source == SourceSite.Cian)
        {
            string[] leftSemantic = CianSemanticTerms(a);
            string[] rightSemantic = CianSemanticTerms(b);
            if (!leftSemantic.SequenceEqual(rightSemantic, StringComparer.Ordinal)) return false;
            if (a.Host == b.Host && a.AbsolutePath == b.AbsolutePath) return true;
            // A city/SEO route carries geography when no explicit geographic filter exists.
            // Only a matching polygon/region makes its move to cat.php unambiguous.
            bool explicitGeography = leftSemantic.Any(term => term.StartsWith("in_polygon%5B%5D=", StringComparison.Ordinal)
                || term.StartsWith("region=", StringComparison.Ordinal) || term.StartsWith("region%5B%5D=", StringComparison.Ordinal));
            static bool SearchPath(string path) => path == "/cat.php" || path.StartsWith("/kupit-zemelniy-uchastok", StringComparison.Ordinal);
            return explicitGeography && SearchPath(a.AbsolutePath) && SearchPath(b.AbsolutePath)
                && (a.AbsolutePath == "/cat.php" || b.AbsolutePath == "/cat.php");
        }

        if (a.AbsolutePath != b.AbsolutePath && !AvitoCategoryAlias(a.AbsolutePath, b.AbsolutePath)) return false;
        static string Host(Uri uri) => uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
        if (Host(a) != Host(b)) return false;
        static string[] Terms(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        static bool AvitoPresentationOnly(string term)
        {
            string key = term.Split('=', 2)[0];
            // Avito may remove these presentation defaults after the map initializes.
            // They do not change the selected polygon or the membership filters.
            return term == "localPriority=0" || key.Equals("s", StringComparison.OrdinalIgnoreCase);
        }
        string[] left = Terms(a).Where(x => !AvitoPresentationOnly(x)).ToArray();
        string[] right = Terms(b).Where(x => !AvitoPresentationOnly(x)).ToArray();
        return left.SequenceEqual(right, StringComparer.Ordinal);
    }
    private static string[] CianSemanticTerms(Uri uri)
    {
        static string BaseKey(string key) => System.Text.RegularExpressions.Regex.Replace(key, @"\[\d+\]$", "[]");
        // Only documented presentation terms may disappear/change on page 2. Unknown
        // terms are membership filters until proven otherwise, so fail closed on changes.
        static bool PresentationKey(string key) => key is "center" or "zoom" or "polygon_name[]" or "sort" or "engine_version";
        List<(string Key, string Value)> terms = [];
        foreach (string term in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = term.Split('=', 2);
            string key = BaseKey(Uri.UnescapeDataString(parts[0].Replace('+', ' ')));
            if (PresentationKey(key)) continue;
            string value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
            if (key == "bbox") value = CianBoundingBoxIdentity(value);
            terms.Add((key, value));
        }
        return terms.OrderBy(x => x.Key, StringComparer.Ordinal).ThenBy(x => x.Value, StringComparer.Ordinal)
            .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value)).ToArray();
    }

    private static string CianBoundingBoxIdentity(string value)
    {
        string[] parts = value.Split(',');
        if (parts.Length != 4) return value;
        decimal[] coordinates = new decimal[4];
        for (int index = 0; index < coordinates.Length; index++)
        {
            decimal limit = index % 2 == 0 ? 90 : 180;
            if (!decimal.TryParse(parts[index].Trim(), System.Globalization.NumberStyles.AllowLeadingSign
                | System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out decimal number)
                || number < -limit || number > limit) return value;
            // Cian serializes pagination bbox to ten decimal places. Canonicalize only this
            // transport representation; polygon geometry and all membership filters remain exact.
            coordinates[index] = decimal.Round(number, 10, MidpointRounding.AwayFromZero);
        }
        return string.Join(',', coordinates.Select(number => number.ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture)));
    }

    public static string[] DiagnosticIdentityDiff(string current, string? candidate, SourceSite source)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return [];
        try
        {
            Uri a = new(Normalize(current, source).Url), b = new(Normalize(candidate, source).Url);
            if (source != SourceSite.Cian)
                return SameSearch(current, candidate, source) ? [] : ["Нормализованные фильтры поиска различаются"];
            static Dictionary<string, string[]> Values(Uri uri)
            {
                static string BaseKey(string key) => System.Text.RegularExpressions.Regex.Replace(key, @"\[\d+\]$", "[]");
                Dictionary<string, List<string>> result = new(StringComparer.OrdinalIgnoreCase);
                foreach (string term in CianSemanticTerms(uri))
                {
                    string[] parts = term.Split('=', 2);
                    string key = BaseKey(Uri.UnescapeDataString(parts[0].Replace('+', ' ')));
                    string value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1].Replace('+', ' ')) : "";
                    if (!result.TryGetValue(key, out List<string>? list)) result[key] = list = [];
                    list.Add(Fingerprint(value));
                }
                return result.ToDictionary(x => x.Key, x => x.Value.Order(StringComparer.Ordinal).ToArray(), StringComparer.OrdinalIgnoreCase);
            }
            static string Fingerprint(string value)
            {
                byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
                return Convert.ToHexString(hash.AsSpan(0, 6));
            }
            Dictionary<string, string[]> left = Values(a), right = Values(b);
            string[] keys = left.Keys.Concat(right.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return keys.Where(key => !left.GetValueOrDefault(key, []).SequenceEqual(right.GetValueOrDefault(key, []), StringComparer.Ordinal))
                .Select(key => $"{key}: [{string.Join(",", left.GetValueOrDefault(key, []))}] -> [{string.Join(",", right.GetValueOrDefault(key, []))}]").ToArray();
        }
        catch (ArgumentException) { return ["Не удалось безопасно нормализовать одну из ссылок"]; }
    }

    private static bool AvitoCategoryAlias(string first, string second)
    {
        string[] a = first.Trim('/').Split('/'), b = second.Trim('/').Split('/');
        return a.Length == 2 && b.Length == 2 && a[0] == b[0]
            && ((a[1] == "nedvizhimost" && b[1] == "zemelnye_uchastki") || (b[1] == "nedvizhimost" && a[1] == "zemelnye_uchastki"));
    }
    public static bool IsDetail(string url, SourceSite source) => System.Text.RegularExpressions.Regex.IsMatch(new Uri(url).AbsolutePath,
        source == SourceSite.Avito ? @"_\d{5,}/?$" : @"^/(?:sale|rent)/[^/]+/\d+/?$");

    /// <summary>Diagnostics never echo opaque query values: keep useful filter names, show only established public filters.</summary>
    public static string DiagnosticUrl(string? url)
    {
        if (url is null) return "";
        try
        {
            NormalizedSearch safe = Normalize(url); Uri uri = new(SafePage(url, safe.Source));
            string[] visible = ["q","s","p","f","region","deal_type","offer_type","engine_version","localPriority","bbox","center","zoom","maxmcad","land_status","object_type","location","minprice","maxprice"];
            string query = string.Join('&', uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Select(pair =>
            {
                string[] parts = pair.Split('=', 2);
                string key = Uri.UnescapeDataString(parts[0]).Split('[')[0];
                return visible.Contains(key, StringComparer.Ordinal) ? pair : parts[0] + "=REDACTED";
            }));
            return uri.GetLeftPart(UriPartial.Path) + (query.Length == 0 ? "" : "?" + query);
        }
        catch (ArgumentException) { return "URL_REDACTED"; }
    }
}
