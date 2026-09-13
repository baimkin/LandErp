using System.Text.Json;
using System.Text.Json.Serialization;

namespace LandErp.ParserSpike.Serialization;

/// <summary>Deterministic JSON settings shared by contract export and import.</summary>
public static class SpikeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    /// <summary>Serializes a contract with stable string enum names.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    /// <summary>Deserializes through validating constructors and rejects null results and unknown members.</summary>
    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("A contract is required.");
}
