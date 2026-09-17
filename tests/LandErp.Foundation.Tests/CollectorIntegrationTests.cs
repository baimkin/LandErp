using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Collector.Contracts.V1;
using LandErp.Infrastructure.Modules.Collection;
using LandErp.Infrastructure.Persistence;
using LandErp.ParserSpike.LocalCollection;
using LandErp.ParserSpike.ServerIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace LandErp.Foundation.Tests;

[TestClass]
[TestCategory("PostgreSQL")]
public sealed class CollectorIntegrationTests
{
    [TestMethod]
    public async Task HttpsActivationReturnsMachineCredentialOnceAndProblemDetailsCodeAfterUse()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using (LandErpDbContext migrator = sandbox.Context()) await migrator.Database.MigrateAsync();
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid userId = await IdentityOrganizationTests.BootstrapAsync(bootstrap, "activation-http@test.invalid", "Activation HTTP");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap, userId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        CollectionAdministration admin = new(factory, services.GetRequiredService<IAccessControl>(), TimeProvider.System);
        AgentConnectionCode code = await admin.CreateConnectionCodeAsync(new(userId, true), "HTTP Parser", "activation", CancellationToken.None);
        AgentActivation request = new(code.AgentId, code.ActivationSecret, "PC-HTTP", 1, "http-test", [ListingSource.Avito]);
        int port = PostgresTests.FreePort(); Uri origin = new($"https://127.0.0.1:{port}/");
        using HttpClientHandler handler = new() { ServerCertificateCustomValidationCallback = (message, _, _, _) => message.RequestUri?.Host == "127.0.0.1" && message.RequestUri.Port == port };
        using HttpClient http = new(handler) { Timeout = TimeSpan.FromSeconds(10) };
        using var server = PostgresTests.StartHost("LandErp.Server", sandbox.RuntimeConnection, port, true);
        try
        {
            await WaitLiveAsync(http, origin, server);
            HttpResponseMessage response = await http.PostAsJsonAsync(new Uri(origin, "api/collector/v1/activation"), request, CollectionJson.Options);
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            AgentActivationReceipt receipt = (await response.Content.ReadFromJsonAsync<AgentActivationReceipt>(CollectionJson.Options))!;
            Assert.AreEqual(code.AgentId, receipt.AgentId);
            Assert.AreEqual(64, receipt.Credential.Length);
            HttpResponseMessage repeated = await http.PostAsJsonAsync(new Uri(origin, "api/collector/v1/activation"), request, CollectionJson.Options);
            Assert.AreEqual(HttpStatusCode.Conflict, repeated.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(await repeated.Content.ReadAsStringAsync());
            Assert.AreEqual("ACTIVATION_USED", problem.RootElement.GetProperty("code").GetString());
        }
        finally { await PostgresTests.StopAsync(server); }
    }

    [TestMethod]
    public async Task ControlCollectorHttpsDurableRetryCatalogPresenceDedupLeaseAndRevocation()
    {
        await using PostgresSandbox sandbox = await PostgresSandbox.CreateAsync();
        await using LandErpDbContext db = sandbox.Context(); await db.Database.MigrateAsync();
        Assert.IsFalse(db.Database.HasPendingModelChanges());
        await using ServiceProvider bootstrap = IdentityOrganizationTests.Services(sandbox.MigratorConnection);
        Guid userId = await IdentityOrganizationTests.BootstrapAsync(bootstrap,"owner-c@test.invalid","Collector test");
        await IdentityOrganizationTests.EnableMfaAsync(bootstrap,userId);
        await sandbox.GrantRuntimeAsync();
        await using ServiceProvider services = IdentityOrganizationTests.Services(sandbox.RuntimeConnection);
        IDbContextFactory<LandErpDbContext> factory = services.GetRequiredService<IDbContextFactory<LandErpDbContext>>();
        IAccessControl access = services.GetRequiredService<IAccessControl>();
        CollectionAdministration admin = new(factory,access,TimeProvider.System);
        Subject owner = new(userId,true);
        AgentCredential credential = await admin.CreateAgentAsync(owner,"Control Collector",false,"test",CancellationToken.None);
        await admin.CreateSearchAsync(owner,new("Control Avito",LandErp.Application.Modules.Catalog.Domain.CatalogSource.Avito,"https://www.avito.ru/moskva/zemelnye_uchastki",1),"test",CancellationToken.None);
        Guid searchId=(await admin.ReadAsync(owner,CancellationToken.None)).Searches.Single().Id;
        await admin.EnqueueAsync(owner,searchId,"test",CancellationToken.None);
        await Assert.ThrowsExactlyAsync<ArgumentException>(()=>admin.EnqueueAsync(owner,searchId,"test",CancellationToken.None));
        int port=PostgresTests.FreePort(); Uri origin=new($"https://127.0.0.1:{port}/");
        // Isolated test certificate only; production ServerAdapter always uses platform validation.
        using HttpClientHandler handler=new() { ServerCertificateCustomValidationCallback=(message,_,_,_)=>message.RequestUri?.Host=="127.0.0.1" && message.RequestUri.Port==port };
        using HttpClient http=new(handler) { Timeout=TimeSpan.FromSeconds(10) };
        using var server=PostgresTests.StartHost("LandErp.Server",sandbox.RuntimeConnection,port,true);
        string directory=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","stage1","collector-test",Guid.NewGuid().ToString("N"));
        LocalStore local=new(Path.Combine(directory,"local.sqlite"));
        ServerOutbox outbox=new(Path.Combine(directory,"outbox.sqlite"));
        ServerAdapter adapter=new(http,new(origin,credential.AgentId,credential.Token));
        await using QueueRunner runner=new(local,new ControlSessions());
        await using ServerCoordinator coordinator=new(local,runner,outbox,adapter);
        try
        {
            await WaitLiveAsync(http,origin,server);
            Assert.AreEqual(HttpStatusCode.Unauthorized,(await http.PostAsync(new Uri(origin,"api/collector/v1/work/claim"),null)).StatusCode);
            await coordinator.StartWorkAsync(CancellationToken.None);
            foreach (string path in new[] { "api/organization", "api/audit" })
            {
                using HttpRequestMessage machineRequest=new(HttpMethod.Get,new Uri(origin,path));
                machineRequest.Headers.Authorization=new("Bearer",credential.Token);
                machineRequest.Headers.Add("X-LandErp-Agent-Id",credential.AgentId.ToString());
                Assert.AreEqual(HttpStatusCode.Unauthorized,(await http.SendAsync(machineRequest)).StatusCode);
            }
            Assert.IsNotNull(coordinator.CurrentWork); CollectionWork work=coordinator.CurrentWork.Work;
            await runner.Completion!;
            Assert.AreEqual(1L,local.ReadListings(new()).Total);
            Assert.AreEqual(0, local.Links().Length, "Server work links must stay hidden from the Local workspace.");
            await coordinator.DeliverAsync(CancellationToken.None);
            Assert.AreEqual(0,outbox.Pending().Length);
            ListingData data=ServerCoordinator.Map(local.ReadListings(new()).Rows.Single().Observation);
            var listing=await db.Listings.AsNoTracking().SingleAsync();
            Assert.AreEqual(1500000m,listing.Price); Assert.AreEqual(1000m,listing.AreaSquareMeters);
            Assert.AreEqual(1,await db.ListingObservations.CountAsync());
            CollectionResult delivered=new(Guid.CreateVersion7(),work.JobId,work.LeaseId,CollectionOutcome.Success,[],true);
            // A separate final delivery after completion is rejected; the original outbox receipt was accepted.
            await Assert.ThrowsExactlyAsync<ServerDeliveryException>(()=>adapter.SendResultAsync(delivered,CancellationToken.None));
            await admin.EnqueueAsync(owner,searchId,"test",CancellationToken.None);
            CollectionWork second=(await adapter.ClaimAsync(CancellationToken.None))!;
            CollectionResult result=new(Guid.CreateVersion7(),second.JobId,second.LeaseId,CollectionOutcome.Success,[new("duplicate-control",data)],false);
            CollectionReceipt receipt=await adapter.SendResultAsync(result,CancellationToken.None);
            Assert.AreEqual(1,receipt.Duplicates); Assert.AreEqual(receipt,await adapter.SendResultAsync(result,CancellationToken.None));
            await Assert.ThrowsExactlyAsync<ServerDeliveryException>(()=>adapter.SendResultAsync(result with { Final=true },CancellationToken.None));
            ListingData missing=data with { ObservedAt=data.ObservedAt.AddSeconds(1), Price=new(FieldPresence.Absent,null,null),
                AreaSquareMeters=new(FieldPresence.ParseFailed,"неизвестно",null), Location=new(FieldPresence.Present,"Новый адрес") };
            await adapter.SendResultAsync(new(Guid.CreateVersion7(),second.JobId,second.LeaseId,CollectionOutcome.Success,[new("missing",missing)],false),CancellationToken.None);
            ListingData old=data with { ObservedAt=data.ObservedAt.AddSeconds(-1),Price=new(FieldPresence.Present,"1 ₽",1m) };
            await adapter.SendResultAsync(new(Guid.CreateVersion7(),second.JobId,second.LeaseId,CollectionOutcome.Success,[new("late",old)],false),CancellationToken.None);
            listing=await db.Listings.AsNoTracking().SingleAsync();
            Assert.AreEqual(1500000m,listing.Price); Assert.AreEqual(1000m,listing.AreaSquareMeters); Assert.AreEqual("Новый адрес",listing.Location);
            Assert.AreEqual(2L,listing.DataRevision); Assert.IsTrue(listing.QueueReason.Contains("местоположение",StringComparison.Ordinal));
            Assert.AreEqual(3,await db.ListingObservations.CountAsync());
            await adapter.SendResultAsync(new(Guid.CreateVersion7(),second.JobId,second.LeaseId,CollectionOutcome.Captcha,[],true),CancellationToken.None);
            Assert.AreEqual("AwaitingManualAction",(await admin.ReadAsync(owner,CancellationToken.None)).Jobs[0].State);
            Assert.AreEqual(HttpStatusCode.OK,(await http.GetAsync(new Uri(origin,"health/ready"))).StatusCode);

            await admin.EnqueueAsync(owner,searchId,"test",CancellationToken.None);
            CollectionWork retryWork=(await adapter.ClaimAsync(CancellationToken.None))!;
            CollectionResult retry=new(Guid.CreateVersion7(),retryWork.JobId,retryWork.LeaseId,CollectionOutcome.Success,[],true);
            outbox.Enqueue(retry); await PostgresTests.StopAsync(server);
            await Assert.ThrowsExactlyAsync<ServerDeliveryException>(()=>outbox.FlushAsync(adapter,CancellationToken.None));
            Assert.AreEqual(retry.ResultId,new ServerOutbox(Path.Combine(directory,"outbox.sqlite")).Pending().Single().Result.ResultId);
            Assert.AreEqual(1L,local.ReadListings(new()).Total); local.SaveLink("Local independent","https://www.cian.ru/cat.php?deal_type=sale&engine_version=2&offer_type=suburban",false);
            using var restarted=PostgresTests.StartHost("LandErp.Server",sandbox.RuntimeConnection,port,true);
            try { await WaitLiveAsync(http,origin,restarted); await outbox.FlushAsync(adapter,CancellationToken.None); Assert.AreEqual(0,outbox.Pending().Length); }
            finally { await PostgresTests.StopAsync(restarted); }
            Assert.AreEqual(3,await db.ListingObservations.CountAsync());

            TestClock clock=new(); CollectorGateway gateway=new(factory,clock);
            await admin.EnqueueAsync(owner,searchId,"test",CancellationToken.None);
            CollectionWork firstLease=(await gateway.ClaimAsync(credential,CancellationToken.None))!;
            clock.Advance(TimeSpan.FromMinutes(4));
            CollectionWork renewed=(await gateway.ClaimAsync(credential,CancellationToken.None))!;
            Assert.AreEqual(firstLease.JobId,renewed.JobId); Assert.AreNotEqual(firstLease.LeaseId,renewed.LeaseId);
            await Assert.ThrowsExactlyAsync<CollectorProtocolException>(()=>gateway.HeartbeatAsync(credential,new(firstLease.JobId,firstLease.LeaseId),CancellationToken.None));
            await Assert.ThrowsExactlyAsync<CollectorProtocolException>(()=>gateway.AcceptAsync(credential,new(Guid.CreateVersion7(),firstLease.JobId,firstLease.LeaseId,CollectionOutcome.Success,[],true),CancellationToken.None));
            await gateway.HeartbeatAsync(credential,new(renewed.JobId,renewed.LeaseId),CancellationToken.None);
            AgentView agent=(await admin.ReadAsync(owner,CancellationToken.None)).Agents.Single();
            await admin.RevokeAgentAsync(owner,agent.Id,agent.Revision,"test",CancellationToken.None);
            await Assert.ThrowsExactlyAsync<CollectorProtocolException>(()=>gateway.ClaimAsync(credential,CancellationToken.None));
            AgentView revoked=(await admin.ReadAsync(owner,CancellationToken.None)).Agents.Single();
            AgentCredential rotated=await admin.RotateCredentialAsync(owner,revoked.Id,revoked.Revision,"test",CancellationToken.None);
            Assert.AreNotEqual(credential.Token,rotated.Token);
            await Assert.ThrowsExactlyAsync<CollectorProtocolException>(()=>gateway.RegisterAsync(credential,new(1,"control",[ListingSource.Avito]),CancellationToken.None));
            await gateway.RegisterAsync(rotated,new(1,"control",[ListingSource.Avito]),CancellationToken.None);
            await sandbox.BackupRestoreAsync();
            await Assert.ThrowsExactlyAsync<CollectorProtocolException>(()=>gateway.AcceptAsync(rotated,new(Guid.CreateVersion7(),Guid.CreateVersion7(),Guid.CreateVersion7(),CollectionOutcome.Success,[],true),CancellationToken.None));
        }
        finally { await PostgresTests.StopAsync(server); }
    }
    private static async Task WaitLiveAsync(HttpClient http,Uri origin,System.Diagnostics.Process process)
    {
        for(int i=0;i<50;i++) { Assert.IsFalse(process.HasExited); try { if((await http.GetAsync(new Uri(origin,"health/live"))).IsSuccessStatusCode) return; } catch(HttpRequestException) { } await Task.Delay(100); }
        Assert.Fail("HTTPS host did not start; private output suppressed.");
    }
    private sealed class TestClock : TimeProvider { private DateTimeOffset now=DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow()=>now; public void Advance(TimeSpan interval)=>now+=interval; }
    private sealed class ControlSessions : ISourceSessions
    {
        public Task<ISourcePage> CreatePageAsync(SourceSite source,CollectionSettings settings,CancellationToken cancellationToken)=>Task.FromResult<ISourcePage>(new ControlPage());
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
    private sealed class ControlPage : ISourcePage
    {
        public SourceSite Source => SourceSite.Avito;
        private readonly ListingObservation observation=new() { Source=SourceSite.Avito,ExternalId="123456789",Url="https://www.avito.ru/moskva/zemelnye_uchastki/uchastok_123456789",ObservedAtUtc=DateTimeOffset.UtcNow,
            Title=TextValue.Read("Контрольный участок 10 соток"),Location=TextValue.Read("Москва, контрольный адрес"),Price=NumberValue.Read("1 500 000 ₽"),AreaSquareMeters=NumberValue.Read("1000 м²") };
        public string CurrentUrl { get; private set; }="";
        public Task OpenAsync(string url,CancellationToken token) { CurrentUrl=url; return Task.CompletedTask; }
        public Task<PageObservation> ReadAsync(CancellationToken token)=>Task.FromResult(new PageObservation(PageKind.SearchResults,[observation],[]));
        public Task<bool> WheelAsync(CollectionSettings settings,CancellationToken token)=>Task.FromResult(false);
        public Task<Pagination> NextAsync(CancellationToken token)=>Task.FromResult(new Pagination(NextKind.End));
        public Task FollowAsync(Pagination pagination,CancellationToken token)=>Task.CompletedTask;
        public Task ActivateAsync(CancellationToken token)=>Task.CompletedTask;
        public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
    }
}
