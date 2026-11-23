using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Server.Components.Procurement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class InspectionDraftMergeFlowTests
{
    private static async Task<Guid> AssignAsync(ProcurementTests.Phase1Fixture fixture)
    {
        var taken=await fixture.Workspace.TakeToWorkAsync(fixture.Manager,new(await fixture.CreateUnlinkedManualAsync()),"take",CancellationToken.None);
        await fixture.SetExplicitAccessAsync(fixture.ManagerEmployeeId,EmployeeAccessRules.NoAccess with
        {IncomingAccess=IncomingAccessLevel.Process,ProcurementAccess=ProcurementAccessLevel.Manager,ProcurementReadScope=AccessScope.Department,
            ProcurementWorkScope=AccessScope.Department,CanPerformInspections=true});
        await fixture.Workspace.AssignInspectionAsync(fixture.Head,new(taken.CaseId,fixture.ManagerEmployeeId,null,"Inspect",null),"assign",CancellationToken.None);
        return taken.CaseId;
    }
    private static SaveInspection Command(Guid id,InspectionDraft draft,bool complete=false)=>new(id,draft.InspectionId,draft.Version,
        draft.Conclusion,draft.Decision,draft.Answers.Select(x=>new InspectionAnswer(x.Id,x.Version,x.Status,x.Answer,x.Note)).ToArray(),complete);

    [TestMethod,TestCategory("PostgreSQL")]
    public async Task StaleAnswersAreMergedAndOnlyOneConcurrentCompletionCommits()
    {
        await using var fixture=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        var id=await AssignAsync(fixture);
        var original=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None)).Inspection!;
        await fixture.Workspace.SaveInspectionAsync(fixture.Manager,Command(id,InspectionDraft.From(original)),"start",CancellationToken.None);
        original=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None)).Inspection!;
        var a=InspectionDraft.From(original) with {Baseline=InspectionDraft.From(original)};
        var b=InspectionDraft.From(original) with {Baseline=InspectionDraft.From(original)};
        a.Answers[0].Note="A";b.Answers[1].Note="B";
        await fixture.Workspace.SaveInspectionAsync(fixture.Manager,Command(id,a),"a",CancellationToken.None);
        await Assert.ThrowsExactlyAsync<DbUpdateConcurrencyException>(()=>fixture.Workspace.SaveInspectionAsync(fixture.Manager,Command(id,b),"stale-b",CancellationToken.None));
        var server=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None)).Inspection!;
        var merged=InspectionDraftMerge.Merge(server,[b]);Assert.AreEqual(0,merged.Conflicts.Count);
        foreach(var item in merged.Draft.Answers){item.Status=InspectionItemStatus.NotChecked;item.Answer="";}
        var done=merged.Draft with {Conclusion="Checked all items"};
        async Task<bool> Complete()
        {
            try{await fixture.Workspace.SaveInspectionAsync(fixture.Manager,Command(id,done,true),"complete",CancellationToken.None);return true;}
            catch(DbUpdateConcurrencyException){return false;}
            catch(ArgumentException){return false;}
        }
        var outcomes=await Task.WhenAll(Complete(),Complete());Assert.AreEqual(1,outcomes.Count(x=>x));
        var result=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None)).Inspection!;
        Assert.AreEqual(InspectionStatus.Completed,result.Status);Assert.AreEqual("A",result.Items[0].Note);Assert.AreEqual("B",result.Items[1].Note);
        Assert.AreEqual(1,await fixture.CountAsync(db=>db.BusinessTimeline.CountAsync(x=>x.ObjectId==id&&x.Title=="Осмотр участка завершён")));
    }

    [TestMethod,TestCategory("PostgreSQL"),TestCategory("Browser"),Timeout(240_000)]
    public async Task SingleTabMediaThenAnswersAndTwoTabsMergeChooseAndComplete()
    {
        await using var fixture=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        var id=await AssignAsync(fixture);
        string root=Path.Combine(FoundationTests.RepositoryRoot(),"artifacts","inspection-draft-merge","browser",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using RSA rsa=RSA.Create(2048);
        var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        var names=new SubjectAlternativeNameBuilder();names.AddDnsName("localhost");names.AddIpAddress(IPAddress.Loopback);request.CertificateExtensions.Add(names.Build());
        using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5),DateTimeOffset.UtcNow.AddDays(1));
        var password=Convert.ToHexString(RandomNumberGenerator.GetBytes(24));string certPath=Path.Combine(root,"test.pfx");
        await File.WriteAllBytesAsync(certPath,cert.Export(X509ContentType.Pfx,password));
        string[] keys=["Storage__Root","Storage__Provider","Kestrel__Certificates__Default__Path","Kestrel__Certificates__Default__Password"];
        var previous=keys.Select(Environment.GetEnvironmentVariable).ToArray();Process host;
        var hostFailures=new System.Collections.Concurrent.ConcurrentQueue<string>();
        int port=PostgresTests.FreePort();string origin=$"https://127.0.0.1:{port}";
        try
        {
            Environment.SetEnvironmentVariable(keys[0],root);Environment.SetEnvironmentVariable(keys[1],"Local");
            Environment.SetEnvironmentVariable(keys[2],certPath);Environment.SetEnvironmentVariable(keys[3],password);
            host=PostgresTests.StartHost("LandErp.Server",fixture.Sandbox.RuntimeConnection,port,true);
            void Capture(object sender,DataReceivedEventArgs args)
            {
                foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(args.Data??"",@"\b[A-Za-z][A-Za-z.]+Exception\b"))hostFailures.Enqueue(match.Value);
                foreach(string known in new[]{"You must install or update .NET","Storage:Provider","STORAGE_","permission denied","Address already in use"})
                    if((args.Data??"").Contains(known,StringComparison.OrdinalIgnoreCase))hostFailures.Enqueue(known);
            }
            host.OutputDataReceived+=Capture;host.ErrorDataReceived+=Capture;
        }
        finally{for(int i=0;i<keys.Length;i++)Environment.SetEnvironmentVariable(keys[i],previous[i]);}
        using(host)
        try
        {
            using var pw=await Playwright.CreateAsync();
            await using var browser=await pw.Chromium.LaunchAsync(new(){Channel="msedge",Headless=true});
            await using var context=await browser.NewContextAsync(new(){IgnoreHTTPSErrors=true});
            var a=await context.NewPageAsync();a.SetDefaultTimeout(20_000);
            for(int attempt=0;;attempt++)
            {
                Assert.IsFalse(host.HasExited,"Isolated host exited: "+string.Join(", ",hostFailures.Distinct()));
                try{await a.GotoAsync(origin+"/health/live");break;}catch(PlaywrightException)when(attempt<50){await Task.Delay(100);}
            }
            await a.GotoAsync(origin+"/account/login");
            await a.GetByLabel("Логин",new(){Exact=true}).FillAsync("manager-phase1@test.invalid");
            await a.GetByLabel("Пароль",new(){Exact=true}).FillAsync("Synthetic1!PasswordForTests");
            await a.GetByRole(AriaRole.Button,new(){Name="Войти",Exact=true}).ClickAsync();
            await a.Locator(".app-shell[data-interactive-ready=true]").WaitForAsync();
            string route=origin+$"/procurement/{id}/inspection";
            await a.GotoAsync(route);await a.GetByRole(AriaRole.Button,new(){Name="Начать осмотр",Exact=true}).ClickAsync();
            await a.Locator("[data-inspection-draft-ready=true]").WaitForAsync();
            await a.GetByRole(AriaRole.Button,new(){Name="＋ Фото / материал",Exact=true}).ClickAsync();
            var modal=a.Locator("dialog.case-modal[open]");
            await modal.Locator("input[type=file][multiple]").SetInputFilesAsync([
                new FilePayload{Name="one.mp4",MimeType="video/mp4",Buffer=[1,2,3]},new FilePayload{Name="two.mp4",MimeType="video/mp4",Buffer=[4,5,6]}]);
            await modal.GetByRole(AriaRole.Button,new(){Name="Загрузить выбранные",Exact=true}).ClickAsync();
            await modal.GetByText("Все файлы загружены.",new(){Exact=true}).WaitForAsync();
            await modal.GetByRole(AriaRole.Button,new(){Name="Закрыть",Exact=true}).ClickAsync();
            async Task Note(IPage page,int index,string value)
            {
                var item=page.Locator(".question").Nth(index);
                if(!await item.Locator("details").EvaluateAsync<bool>("e=>e.open"))await item.Locator("summary").ClickAsync();
                await item.Locator("[data-inspection-note]").FillAsync(value);
            }
            async Task Save(IPage page)
            {
                await page.GetByRole(AriaRole.Button,new(){Name="Сохранить черновик",Exact=true}).ClickAsync();
                await page.WaitForFunctionAsync("()=>document.querySelector('.inspection-footer')?.textContent.includes('Сохранено на сервере')");
            }
            await Note(a,0,"single tab after video");await Save(a);
            for(int part=0;part<4;part++)
            {
                await a.Locator("nav[aria-label='Разделы осмотра'] button").Nth(part).ClickAsync();
                foreach(var button in await a.GetByRole(AriaRole.Button,new(){Name="Не удалось проверить",Exact=true}).AllAsync())await button.ClickAsync();
            }
            await a.GetByRole(AriaRole.Button,new(){Name="Завершить осмотр",Exact=true}).ClickAsync();
            modal=a.Locator("dialog.case-modal[open]");
            await modal.Locator("[data-inspection-conclusion]").FillAsync("Single tab: videos first, then all answers, then completion");
            await modal.GetByRole(AriaRole.Button,new(){Name="Завершить осмотр",Exact=true}).ClickAsync();
            await a.GetByText("Осмотр завершён",new(){Exact=true}).WaitForAsync();
            var single=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None));
            Assert.AreEqual(InspectionStatus.Completed,single.Inspection!.Status);Assert.AreEqual(2,single.Attachments.Count);
            Assert.AreEqual("single tab after video",single.Inspection.Items[0].Note);
            id=await AssignAsync(fixture);route=origin+$"/procurement/{id}/inspection";
            await a.GotoAsync(route);await a.GetByRole(AriaRole.Button,new(){Name="Начать осмотр",Exact=true}).ClickAsync();
            await a.Locator("[data-inspection-draft-ready=true]").WaitForAsync();

            var b=await context.NewPageAsync();b.SetDefaultTimeout(20_000);await b.GotoAsync(route);
            await b.Locator("[data-inspection-draft-ready=true]").WaitForAsync();
            await Note(a,0,"new A");await Note(b,1,"new B");await Save(a);await Save(b);
            var after=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None)).Inspection!;
            Assert.AreEqual("new A",after.Items[0].Note);Assert.AreEqual("new B",after.Items[1].Note);
            Assert.AreEqual(0,await b.Locator("[data-inspection-conflicts]").CountAsync());
            await Note(a,0,"choice A");await Note(b,0,"choice B");
            await a.GetByRole(AriaRole.Button,new(){Name="Сохранить черновик",Exact=true}).ClickAsync();
            await a.Locator("[data-inspection-conflicts]").WaitForAsync();
            Assert.AreEqual(1,await a.Locator(".inspection-conflict").CountAsync());
            await a.ScreenshotAsync(new(){Path=Path.Combine(root,"conflict-desktop.png"),FullPage=true});
            await a.SetViewportSizeAsync(390,844);
            Assert.IsTrue(await a.Locator(".inspection-conflicts").EvaluateAsync<bool>("e=>e.getBoundingClientRect().right<=innerWidth&&e.scrollWidth<=e.clientWidth"));
            await a.Locator(".inspection-conflicts").ScreenshotAsync(new(){Path=Path.Combine(root,"conflict-mobile.png")});
            await a.GetByRole(AriaRole.Button,new(){Name="choice B",Exact=true}).ClickAsync();await Save(a);
            await a.SetViewportSizeAsync(1280,720);
            await b.ReloadAsync();await b.Locator("[data-inspection-draft-ready=true]").WaitForAsync();
            for(int part=0;part<4;part++)
            {
                await b.Locator("nav[aria-label='Разделы осмотра'] button").Nth(part).ClickAsync();
                foreach(var button in await b.GetByRole(AriaRole.Button,new(){Name="Не удалось проверить",Exact=true}).AllAsync())await button.ClickAsync();
            }
            await b.SetViewportSizeAsync(390,844);
            await b.GetByRole(AriaRole.Button,new(){Name="Завершить осмотр",Exact=true}).ClickAsync();
            modal=b.Locator("dialog.case-modal[open]");
            await modal.Locator("[data-inspection-conclusion]").FillAsync("Completed from another tab without lost answers");
            await modal.GetByRole(AriaRole.Button,new(){Name="Завершить осмотр",Exact=true}).ClickAsync();
            await b.GetByText("Осмотр завершён",new(){Exact=true}).WaitForAsync();
            await b.ScreenshotAsync(new(){Path=Path.Combine(root,"completed-mobile.png"),FullPage=true});
            var done=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None)).Inspection!;
            Assert.AreEqual(InspectionStatus.Completed,done.Status);Assert.AreEqual("choice B",done.Items[0].Note);Assert.AreEqual("new B",done.Items[1].Note);
            Assert.IsTrue(done.Items.All(x=>x.Status==InspectionItemStatus.NotChecked));
            await Note(a,0,"late unsaved note after another tab completed");
            await a.GetByRole(AriaRole.Button,new(){Name="Сохранить черновик",Exact=true}).ClickAsync();
            await a.GetByRole(AriaRole.Button,new(){Name="Скачать черновик",Exact=true}).WaitForAsync();
            var download=await a.RunAndWaitForDownloadAsync(()=>a.GetByRole(AriaRole.Button,new(){Name="Скачать черновик",Exact=true}).ClickAsync());
            var backup=await download.PathAsync();Assert.IsNotNull(backup);
            StringAssert.Contains(await File.ReadAllTextAsync(backup),"late unsaved note after another tab completed");
            var unchanged=(await fixture.Workspace.ReadInspectionAsync(fixture.Manager,id,CancellationToken.None)).Inspection!;
            Assert.AreEqual("choice B",unchanged.Items[0].Note);

        }
        finally{if(!host.HasExited){host.Kill(true);await host.WaitForExitAsync();}}
    }
}
