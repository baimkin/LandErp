using System.Text.Json;
using LandErp.ParserSpike.Application;
using LandErp.ParserSpike.Contracts;
using LandErp.ParserSpike.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class ContractTests
{
    private static ObservationResult Result(PageClassification classification = PageClassification.SearchResults,
        Outcome outcome = Outcome.Success, string schema = "1.0", IReadOnlyList<string>? warnings = null,
        IReadOnlyList<string>? errors = null, Uri? url = null) =>
        new(schema, "AVITO", "0.1.0", url, null,
            new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.FromHours(3)), classification,
            outcome, warnings ?? ["FIELD_NOT_INSPECTED"], errors ?? [],
            Guid.Parse("0d73dc97-a05f-4ff1-936f-c348445771fb"),
            [Guid.Parse("f9a720e2-c90c-4430-bae8-a2dc3c3cd103")]);

    [TestMethod]
    public void EnvelopeRoundTripPreservesAllSignificantFields()
    {
        ObservationResult original = Result(url: new Uri("https://example.test/listing/123"));
        string json = SpikeJson.Serialize(original);
        ObservationResult copy = SpikeJson.Deserialize<ObservationResult>(json);
        Assert.AreEqual(original.SchemaVersion, copy.SchemaVersion);
        Assert.AreEqual(original.SourceCode, copy.SourceCode);
        Assert.AreEqual(original.AdapterVersion, copy.AdapterVersion);
        Assert.AreEqual(original.RequestedUrl, copy.RequestedUrl);
        Assert.IsNull(copy.FinalUrl);
        Assert.AreEqual(original.ObservedAtUtc, copy.ObservedAtUtc);
        Assert.AreEqual(original.Classification, copy.Classification);
        Assert.AreEqual(original.Outcome, copy.Outcome);
        Assert.AreEqual(original.CorrelationId, copy.CorrelationId);
        CollectionAssert.AreEqual(original.Warnings.ToArray(), copy.Warnings.ToArray());
        CollectionAssert.AreEqual(original.Errors.ToArray(), copy.Errors.ToArray());
        CollectionAssert.AreEqual(original.DiagnosticReferences.ToArray(), copy.DiagnosticReferences.ToArray());
        Assert.AreEqual(json, SpikeJson.Serialize(copy));
        Assert.AreEqual(json, SpikeJson.Serialize(SpikeJson.Deserialize<ObservationResult>(SpikeJson.Serialize(copy))));
    }

    [TestMethod]
    [DataRow(PageClassification.AuthenticationRequired)]
    [DataRow(PageClassification.Captcha)]
    [DataRow(PageClassification.RateLimited)]
    [DataRow(PageClassification.SourceError)]
    [DataRow(PageClassification.Unknown)]
    [DataRow(PageClassification.ListingUnavailable)]
    public void NonContentPagesCannotSucceedEvenViaJson(PageClassification classification)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Result(classification));
        string json = SpikeJson.Serialize(Result(classification, Outcome.Attention));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SpikeJson.Deserialize<ObservationResult>(json.Replace("\"Attention\"", "\"Success\"", StringComparison.Ordinal)));
        Assert.AreEqual(classification, SpikeJson.Deserialize<ObservationResult>(json).Classification);
        Assert.AreEqual(Outcome.Failure, Result(classification, Outcome.Failure).Outcome);
    }

    [TestMethod]
    public void AllPresenceStatesRoundTripIncludingZeroTypedValue()
    {
        ObservedValue<decimal>[] values =
        [
            new(Presence.NotInspected, null, null, []),
            new(Presence.Absent, null, null, []),
            new(Presence.Empty, "", null, []),
            new(Presence.Present, "0", 0m, []),
            new(Presence.Present, null, 0m, []),
            new(Presence.Present, "negotiable", null, []),
            new(Presence.ParseFailed, "invalid amount", null, ["PRICE_PARSE_FAILED"])
        ];
        foreach (ObservedValue<decimal> value in values)
        {
            string json = SpikeJson.Serialize(value);
            ObservedValue<decimal> copy = SpikeJson.Deserialize<ObservedValue<decimal>>(json);
            Assert.AreEqual(value.Presence, copy.Presence);
            Assert.AreEqual(value.Raw, copy.Raw);
            Assert.AreEqual(value.Parsed, copy.Parsed);
            Assert.AreEqual(json, SpikeJson.Serialize(copy));
        }
    }

    [TestMethod]
    public void InvalidPresenceCombinationsAreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.Present, null, null, []));
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.Present, " ", null, []));
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.ParseFailed, null, null, []));
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.ParseFailed, "bad", 42, []));
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.Absent, "old", null, []));
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.NotInspected, null, 42, []));
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.Empty, "value", null, []));
        Assert.ThrowsExactly<ArgumentException>(() => new ObservedValue<int>(Presence.Empty, null, 0, []));
        Assert.ThrowsExactly<ArgumentException>(() => SpikeJson.Deserialize<ObservedValue<int>>(
            "{\"presence\":\"ParseFailed\",\"raw\":\"bad\",\"parsed\":42,\"warnings\":[]}"));
    }

    [TestMethod]
    public void TimestampUsesUtcAndPreservesInstant()
    {
        ObservationResult result = Result();
        Assert.AreEqual(TimeSpan.Zero, result.ObservedAtUtc.Offset);
        Assert.AreEqual(9, result.ObservedAtUtc.Hour);
        using JsonDocument document = JsonDocument.Parse(SpikeJson.Serialize(result));
        Assert.AreEqual("2026-09-13T09:00:00+00:00", document.RootElement.GetProperty("observedAtUtc").GetString());
    }

    [TestMethod]
    public void MissingTimeAndEmptyOpaqueIdsAreRejected()
    {
        string json = SpikeJson.Serialize(Result());
        Assert.ThrowsExactly<ArgumentException>(() => SpikeJson.Deserialize<ObservationResult>(
            json.Replace("\"observedAtUtc\":\"2026-09-13T09:00:00+00:00\",", "", StringComparison.Ordinal)));
        Assert.ThrowsExactly<ArgumentException>(() => SpikeJson.Deserialize<ObservationResult>(
            json.Replace("0d73dc97-a05f-4ff1-936f-c348445771fb", Guid.Empty.ToString(), StringComparison.Ordinal)));
        Assert.ThrowsExactly<ArgumentException>(() => SpikeJson.Deserialize<ObservationResult>(
            json.Replace("f9a720e2-c90c-4430-bae8-a2dc3c3cd103", Guid.Empty.ToString(), StringComparison.Ordinal)));
    }

    [TestMethod]
    public void RecognizedDetailMaySucceedAndUndefinedPresenceIsRejected()
    {
        ObservationResult detail = Result(PageClassification.ListingDetails);
        Assert.AreEqual(Outcome.Success, SpikeJson.Deserialize<ObservationResult>(SpikeJson.Serialize(detail)).Outcome);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new ObservedValue<int>((Presence)99, null, null, []));
    }

    [TestMethod]
    public void RequiredMetadataAndStableCodesAreEnforced()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Result(schema: ""));
        Assert.ThrowsExactly<ArgumentException>(() => Result(warnings: ["free form message"]));
        Assert.ThrowsExactly<ArgumentException>(() => Result(errors: ["bad-code"]));
        string[] codes = ["SOURCE_UNAVAILABLE"];
        ObservationResult result = Result(PageClassification.SourceError, Outcome.Failure, errors: codes);
        codes[0] = "MUTATED";
        ObservationResult copy = SpikeJson.Deserialize<ObservationResult>(SpikeJson.Serialize(result));
        Assert.AreEqual("SOURCE_UNAVAILABLE", copy.Errors[0]);
        Assert.ThrowsExactly<ArgumentNullException>(() => SpikeJson.Deserialize<ObservationResult>(
            SpikeJson.Serialize(result).Replace("\"schemaVersion\":\"1.0\",", "", StringComparison.Ordinal)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Result((PageClassification)99, Outcome.Failure));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Result(outcome: (Outcome)99));
    }

    [TestMethod]
    public void JsonContainsOnlyApprovedEnvelopeFieldsAndRejectsUnknownFields()
    {
        string json = SpikeJson.Serialize(Result());
        using JsonDocument document = JsonDocument.Parse(json);
        string[] approved = ["schemaVersion", "sourceCode", "adapterVersion", "requestedUrl", "finalUrl",
            "observedAtUtc", "classification", "outcome", "warnings", "errors", "correlationId", "diagnosticReferences"];
        CollectionAssert.AreEquivalent(approved, document.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.ThrowsExactly<JsonException>(() => SpikeJson.Deserialize<ObservationResult>(
            json.Insert(1, "\"cookies\":[],")));
        Assert.ThrowsExactly<JsonException>(() => SpikeJson.Deserialize<ObservationResult>("null"));
    }

    [TestMethod]
    [DataRow("https://user:secret@example.test/path")]
    [DataRow("https://example.test/path?token=synthetic")]
    [DataRow("https://example.test/path#synthetic")]
    [DataRow("file:///C:/synthetic/profile")]
    public void UnsafeUrlMetadataIsRejected(string url)
    {
        Assert.ThrowsExactly<ArgumentException>(() => Result(url: new Uri(url)));
    }

    [TestMethod]
    public void CliHelpSucceedsAndUnsupportedInputFailsWithoutEcho()
    {
        using StringWriter output = new();
        using StringWriter error = new();
        Assert.AreEqual(0, SpikeCli.Run(["--help"], output, error));
        StringAssert.Contains(output.ToString(), "Usage:");
        Assert.AreEqual("", error.ToString());
        output.GetStringBuilder().Clear();
        Assert.AreEqual(2, SpikeCli.Run(["synthetic-private-input"], output, error));
        Assert.AreEqual("", output.ToString());
        StringAssert.Contains(error.ToString(), "CLI_UNKNOWN_COMMAND");
        Assert.IsFalse(error.ToString().Contains("synthetic-private-input", StringComparison.Ordinal));
        Assert.AreEqual(2, SpikeCli.Run([], output, error));
        Assert.AreEqual(2, SpikeCli.Run(["--help", "extra"], output, error));
    }
}
