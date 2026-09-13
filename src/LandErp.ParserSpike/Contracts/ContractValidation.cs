using System.Collections.ObjectModel;

namespace LandErp.ParserSpike.Contracts;

internal static class ContractValidation
{
    internal static string Required(string value, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value;
    }

    internal static ReadOnlyCollection<string> Codes(IReadOnlyList<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        foreach (string code in codes)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            if (code.Length > 64 || !char.IsAsciiLetterUpper(code[0]) ||
                code.Any(c => !char.IsAsciiLetterUpper(c) && !char.IsAsciiDigit(c) && c != '_'))
            {
                throw new ArgumentException("Expected a stable uppercase code.", nameof(codes));
            }
        }

        return Array.AsReadOnly(codes.ToArray());
    }

    internal static Uri? PublicUrl(Uri? url)
    {
        if (url is not null && (!url.IsAbsoluteUri ||
            (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp) ||
            url.UserInfo.Length != 0 || url.Query.Length != 0 || url.Fragment.Length != 0))
        {
            throw new ArgumentException("Only public HTTP(S) URLs without credentials, query or fragment are allowed.", nameof(url));
        }

        return url;
    }
}
