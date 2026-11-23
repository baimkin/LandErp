using System.Reflection;
using System.Text;
using System.Text.Json;
using LandErp.ParserSpike.Desktop;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class MapDiagnosticTests
{
    private const string Url = "https://www.avito.ru/moskva/zemelnye_uchastki?drawId=original&f=filter";
    private static string Root() => Path.Combine(Path.GetTempPath(), "LandErp-MapDiagnostic", Guid.NewGuid().ToString("N"));
    private static T Proxy<T>(Func<string, object?[]?, object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, BrowserReliabilityTests.InterfaceProxy>();
        ((BrowserReliabilityTests.InterfaceProxy)(object)proxy).Handler = handler; return proxy;
    }
    [TestMethod]
    public void NetworkEvidenceRetainsGeometryButNotPrivateValuesOrOpaqueUrls()
    {
        const string polygon = """{"type":"Polygon","coordinates":[[[37,55],[38,55],[38,56],[37,55]]],"token":"SECRET"}""";
        string json = JsonSerializer.Serialize(new { result = new
        {
            drawAreaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(polygon)),
            selectedArea = JsonSerializer.Deserialize<JsonElement>(polygon),
            session = "SECRET", price = 9999999, title = "PRIVATE_TITLE", seller = new { name = "PRIVATE_NAME" }
        } });
        string saved = JsonSerializer.Serialize(MapDiagnosticCapture.DescribeJson(json));
        Assert.Contains("Polygon", saved); Assert.Contains("37", saved);
        foreach (string secret in new[] { "SECRET", "PRIVATE_TITLE", "PRIVATE_NAME", "9999999" }) Assert.DoesNotContain(secret, saved);
        Assert.AreEqual("https://www.avito.ru/web/1/map/_opaque_", MapDiagnosticCapture.SafeEndpoint("https://www.avito.ru/web/1/map/abcdef0123456789?token=SECRET#PRIVATE"));
        Assert.IsNull(MapDiagnosticCapture.SafeEndpoint("https://www.avito.ru/api/session"));
        Assert.IsNull(MapDiagnosticCapture.SafeEndpoint("https://avito.ru.evil.test/map"));
        Assert.IsNull(MapDiagnosticCapture.SafeEndpoint("https://name:secret@avito.ru/map"));
        Assert.IsNull(MapDiagnosticCapture.SafeEndpoint("https://avito.ru:444/map"));
    }
    [TestMethod]
    public void ShapeIsBoundedAndViewportIsNotPromotedToSelectedGeometry()
    {
        string saved = JsonSerializer.Serialize(MapDiagnosticCapture.DescribeJson("""{"bbox":[37,55,38,56],"items":[{"id":12345678},{"id":99999999}],"unknown":"PRIVATE"}"""));
        Assert.DoesNotContain("12345678", saved); Assert.DoesNotContain("PRIVATE", saved);
        Assert.DoesNotContain("selectedArea", saved);
        saved = JsonSerializer.Serialize(MapDiagnosticCapture.DescribeJson(JsonSerializer.Serialize(new { coordinates = Enumerable.Range(0, 20000).ToArray() })));
        Assert.IsLessThan(100000, saved.Length);
    }
    [TestMethod]
    public void DrawAreaStructureKeepsUnknownGeometryKeysAndHidesValues()
    {
        const string drawArea = """{"paths":[{"vertices":[[37.123456,55.123456],[38,56],[39,57]],"name":"PRIVATE_LABEL","token":"PRIVATE_TOKEN","active":true}]}""";
        string response = JsonSerializer.Serialize(new { success = new
        {
            drawArea = Convert.ToBase64String(Encoding.UTF8.GetBytes(drawArea))
        } });
        string saved = JsonSerializer.Serialize(MapDiagnosticCapture.DescribeJson(response));
        using JsonDocument output = JsonDocument.Parse(saved);
        JsonElement diagnostic = output.RootElement.GetProperty("success").GetProperty("drawArea");
        Assert.AreEqual("DecodedJson", diagnostic.GetProperty("Outcome").GetString());
        Assert.AreEqual(0, diagnostic.GetProperty("PolygonCount").GetInt32());
        Assert.AreEqual("AREA_GEOMETRY_TYPE_UNSUPPORTED", diagnostic.GetProperty("Rejection").GetString());
        JsonElement path = diagnostic.GetProperty("Structure").GetProperty("paths").GetProperty("Samples")[0];
        JsonElement vertices = path.GetProperty("vertices");
        Assert.AreEqual(3, vertices.GetProperty("Count").GetInt32());
        Assert.AreEqual(2, vertices.GetProperty("Samples").GetArrayLength());
        Assert.AreEqual("Number", vertices.GetProperty("Samples")[0].GetProperty("Samples")[0].GetString());
        Assert.AreEqual("String", path.GetProperty("name").GetString());
        Assert.AreEqual("Boolean", path.GetProperty("active").GetString());
        Assert.IsFalse(path.TryGetProperty("token", out _));
        Assert.DoesNotContain("37.123456", saved);
        Assert.DoesNotContain("PRIVATE", saved);
    }
    [TestMethod]
    [DataRow("[[37,55],[38,55],[38,56],[37,56]]", "AREA_RING_NOT_CLOSED")]
    [DataRow("[[37,55],[38,55],[38,56],[38,55],[37,55]]", "AREA_RING_REPEATED_VERTEX")]
    [DataRow("[[37,55],[38,56],[38,55],[37,56],[37,55]]", "AREA_RING_SELF_INTERSECTION")]
    [DataRow("[[37,55],[38,55],[38,56],[37,56],[37,55]]", null)]
    public void DrawAreaReportsActualRingValidationResult(string ring, string? rejection)
    {
        string drawArea = "{\"type\":\"Polygon\",\"coordinates\":[" + ring + "]}";
        string response = JsonSerializer.Serialize(new { success = new { drawArea } });
        using JsonDocument diagnostic = JsonDocument.Parse(JsonSerializer.Serialize(MapDiagnosticCapture.DescribeJson(response)));
        JsonElement area = diagnostic.RootElement.GetProperty("success").GetProperty("drawArea");
        Assert.AreEqual("DecodedJson", area.GetProperty("Outcome").GetString());
        Assert.AreEqual(rejection, area.GetProperty("Rejection").GetString());
        Assert.AreEqual(rejection is null ? 1 : 0, area.GetProperty("PolygonCount").GetInt32());
    }
    [TestMethod]
    public async Task DiagnosticStartupIgnoresAutoOnAndNeverRunsOrDelivers()
    {
        string root = Root(); Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "automation.txt"), "on");
        FakePage page = new();
        await using WorkspaceController controller = new(Path.Combine(root, "data.sqlite"), root, new Sessions(page),
            () => throw new AssertFailedException("No Server delivery/claim expected"), diagnosticsOnly: true);
        controller.Store.SaveLink("Fixture", Url);
        Assert.IsFalse(controller.AutomationEnabled);
        Assert.ThrowsExactly<InvalidOperationException>(() => controller.SetAutomation(true));
        await controller.TickAsync(CancellationToken.None);
        string first = await controller.BeginMapDiagnosticsAsync(new("fixture", "Fixture", Url, SourceSite.Avito, false, true, 1), CancellationToken.None);
        Assert.AreEqual("begin,open,activate", string.Join(',', page.Calls));
        await controller.TickAsync(CancellationToken.None);
        await controller.FinishMapDiagnosticsAsync(CancellationToken.None);
        string second = await controller.BeginMapDiagnosticsAsync(new("fixture", "Fixture", Url, SourceSite.Avito, false, true, 1), CancellationToken.None);
        Assert.AreNotEqual(first, second); Assert.IsTrue(File.Exists(Path.Combine(first, "search.json")));
        Assert.IsEmpty(controller.Store.Jobs());
        Assert.AreEqual(0L, controller.Store.ReadListings(new()).Total);
        Assert.IsFalse(File.Exists(Path.Combine(root, "collector-server-outbox.sqlite")));
        Assert.AreEqual("on", File.ReadAllText(Path.Combine(root, "automation.txt")));
    }
    [TestMethod]
    public async Task FailedNavigationStillFinalizesItsOwnEvidence()
    {
        string root = Root(); FakePage page = new() { FailOpen = true };
        await using WorkspaceController controller = new(Path.Combine(root, "data.sqlite"), root, new Sessions(page), diagnosticsOnly: true);
        await Assert.ThrowsExactlyAsync<TimeoutException>(() => controller.BeginMapDiagnosticsAsync(new("fixture", "Fixture", Url, SourceSite.Avito, false, true, 1), CancellationToken.None));
        Assert.AreEqual("begin,open,finish", string.Join(',', page.Calls));
        Assert.IsNotNull(controller.MapDiagnosticDirectory);
        Assert.IsEmpty(controller.Store.Jobs());
    }
    [TestMethod]
    public async Task ClosedPageKeepsNetworkEvidenceAndRecordsMissingSnapshotWithoutCollecting()
    {
        EventHandler<IResponse>? observe = null;
        IPage browserPage = Proxy<IPage>((name, args) => name switch
        {
            "add_Response" => Add((EventHandler<IResponse>)args![0]!),
            "remove_Response" => Remove((EventHandler<IResponse>)args![0]!),
            "get_IsClosed" => true,
            "get_Url" => Url,
            "EvaluateAsync" => Task.FromException<string>(new PlaywrightException("closed")),
            "ScreenshotAsync" => Task.FromException<byte[]>(new PlaywrightException("closed")),
            "CloseAsync" => Task.CompletedTask,
            _ => throw new AssertFailedException("Unexpected browser operation: " + name)
        });
        object? Add(EventHandler<IResponse> h) { observe += h; return null; }
        object? Remove(EventHandler<IResponse> h) { observe -= h; return null; }
        await using DomSourcePage source = new(browserPage, SourceSite.Avito);
        string dir = MapDiagnosticCapture.CreateDirectory(Root());
        await source.BeginMapDiagnosticsAsync(dir, CancellationToken.None);
        IRequest request = Proxy<IRequest>((name, _) => name switch { "get_ResourceType" => "fetch", "get_IsNavigationRequest" => false, _ => null });
        IResponse response = Proxy<IResponse>((name, _) => name switch
        {
            "get_Url" => "https://www.avito.ru/new/map/shape?token=SECRET", "get_Request" => request,
            "get_Status" => 200, "HeaderValueAsync" => Task.FromResult<string?>("application/json"),
            "BodyAsync" => Task.FromResult(Encoding.UTF8.GetBytes("""{"selectedArea":{"type":"Polygon","coordinates":[[[37,55],[38,55],[38,56],[37,55]]]}}""")),
            _ => null
        });
        observe!.Invoke(browserPage, response);
        Assert.ThrowsExactly<InvalidOperationException>(() => source.ReadAsync(CancellationToken.None));
        await source.FinishMapDiagnosticsAsync(CancellationToken.None);
        string saved = File.ReadAllText(Path.Combine(dir, "network.jsonl"));
        Assert.Contains("selectedArea", saved); Assert.DoesNotContain("SECRET", saved);
        Assert.Contains("HTML_UNAVAILABLE", File.ReadAllText(Path.Combine(dir, "snapshot.json")));
        Assert.Contains("Unknown", File.ReadAllText(Path.Combine(dir, "capture.json")));
        Assert.IsFalse(File.Exists(Path.Combine(dir, "page.html")));
    }
    [TestMethod]
    public async Task DiagnosticSnapshotUsesTheProductionSelectionScriptWithDetailsEnabled()
    {
        bool evaluated = false;
        IPage browserPage = Proxy<IPage>((name, args) => name switch
        {
            "add_Response" or "remove_Response" => null,
            "get_IsClosed" => true,
            "get_Url" => Url,
            "EvaluateAsync" => Evaluate(args!),
            "ScreenshotAsync" => Task.FromResult(Array.Empty<byte>()),
            "CloseAsync" => Task.CompletedTask,
            _ => throw new AssertFailedException(name)
        });
        Task<string> Evaluate(object?[] args)
        {
            if ((string)args[0]! == DomSourcePage.MapSelectionScript)
            {
                Assert.AreEqual(true, args[1]);
                evaluated = true;
                return Task.FromResult("""{"MapVisible":true,"SurfaceVisible":false,"ResetVisible":true,"Loading":false,"Diagnostic":{"MapCandidates":1}}""");
            }
            return Task.FromResult("{}");
        }
        await using DomSourcePage source = new(browserPage, SourceSite.Avito);
        string dir = MapDiagnosticCapture.CreateDirectory(Root());
        await source.BeginMapDiagnosticsAsync(dir, CancellationToken.None);
        await source.FinishMapDiagnosticsAsync(CancellationToken.None);
        Assert.IsTrue(evaluated);
        using JsonDocument state = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "selection-state.json")));
        Assert.IsTrue(state.RootElement.GetProperty("MapVisible").GetBoolean());
        Assert.IsFalse(state.RootElement.GetProperty("SurfaceVisible").GetBoolean());
        Assert.AreEqual(1, state.RootElement.GetProperty("Diagnostic").GetProperty("MapCandidates").GetInt32());
    }

    private sealed class Sessions(FakePage page) : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source, CollectionSettings settings, CancellationToken cancellationToken) => Task.FromResult<ISourcePage>(page);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakePage : ISourcePage
    {
        public List<string> Calls { get; } = [];
        public bool FailOpen { get; init; }
        public SourceSite Source => SourceSite.Avito;
        public string CurrentUrl => Url;
        public Task BeginMapDiagnosticsAsync(string directory, CancellationToken cancellationToken) { Calls.Add("begin"); return Task.CompletedTask; }
        public Task FinishMapDiagnosticsAsync(CancellationToken cancellationToken) { Calls.Add("finish"); return Task.CompletedTask; }
        public Task OpenAsync(string url, CancellationToken cancellationToken) { Calls.Add("open"); return FailOpen ? Task.FromException(new TimeoutException()) : Task.CompletedTask; }
        public Task ActivateAsync(CancellationToken cancellationToken) { Calls.Add("activate"); return Task.CompletedTask; }
        public Task<PageObservation> ReadAsync(CancellationToken cancellationToken) => throw new AssertFailedException("No observations");
        public Task<bool> WheelAsync(CollectionSettings settings, CancellationToken cancellationToken) => throw new AssertFailedException("No scroll");
        public Task<Pagination> NextAsync(CancellationToken cancellationToken) => throw new AssertFailedException("No pagination");
        public Task FollowAsync(Pagination pagination, CancellationToken cancellationToken) => throw new AssertFailedException("No navigation");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
