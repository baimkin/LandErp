using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Infrastructure.Modules.Procurement;
using LandErp.Server.Components.Procurement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class CaseFeedTests
{
    private static readonly DateTimeOffset Recorded = new(2026,9,27,9,0,0,TimeSpan.Zero);
    private static readonly DateTimeOffset Effective = Recorded.AddDays(-1);

    [TestMethod]
    public void CommunicationKeepsLegacyFieldsAndOnlyRemovesLiteralDuplicates()
    {
        var communication = Communication() with { Outcome="Договорились об осмотре",Comment="Договорились об осмотре",
            Conditions="Договорились об осмотре после дождя",Contact="Агент Иван",NextStep="Проверить подъезд",NextStepDueAt=Recorded.AddDays(1),
            SellerPrice=12000000m,BuyerOffer=10000000m,AgreedPrice=11000000m };
        var row=CaseFeedFormat.From(communication);
        Assert.AreEqual(communication.Outcome,row.Text);
        Assert.IsFalse(row.Details.Any(x=>x==communication.Outcome));
        Assert.IsTrue(row.Details.Any(x=>x.Contains(communication.Conditions,StringComparison.Ordinal)));
        Assert.IsTrue(row.Details.Any(x=>x.Contains(communication.Contact,StringComparison.Ordinal)));
        Assert.IsTrue(row.Details.Any(x=>x.Contains(communication.NextStep,StringComparison.Ordinal)));
        Assert.IsTrue(row.Details.Any(x=>x.StartsWith("Срок следующего шага:",StringComparison.Ordinal)));
        string prices=row.Details.Single(x=>x.StartsWith("Продавец:",StringComparison.Ordinal));
        StringAssert.Contains(prices,"Предложили:");StringAssert.Contains(prices,"Согласовали:");
        Assert.AreEqual("Звонок",row.Type);Assert.AreEqual("А. Баимкин",row.Author);Assert.AreEqual("Александр Баимкин",row.FullAuthor);
        var empty=CaseFeedFormat.From(Communication());Assert.AreEqual(0,empty.Details.Count);
    }

    [TestMethod]
    public void MoscowActionDateAndRegistrationRemainAvailableWithoutResorting()
    {
        var row=CaseFeedFormat.From(Communication());
        Assert.AreEqual(Effective,row.Date);Assert.AreEqual("26.09 12:00",row.DateLabel);
        StringAssert.Contains(row.DateHint,"26.09.2026 12:00:00 МСК");StringAssert.Contains(row.DateHint,"27.09.2026 12:00:00 МСК");
        var note=CaseFeedFormat.From(new TimelineItem(Guid.NewGuid(),"Note","Рабочая заметка","Подъезд проверили",
            "Александр Баимкин",null,Recorded,null,null));
        Assert.AreEqual("Подъезд проверили",note.Text);Assert.AreEqual("Заметка",note.Type);Assert.AreEqual(Recorded,note.Date);
        Assert.AreEqual(0,note.Details.Count);
    }

    [TestMethod]
    public void ExactTimelineEnrichmentRejectsAmbiguityAndNeverHidesTaskEvents()
    {
        var item=new CaseNegotiation { Id=Guid.NewGuid(),OrganizationId=Guid.NewGuid(),PropertyCaseId=Guid.NewGuid(),
            AuthorEmployeeId=Guid.NewGuid(),RecordedAt=Recorded,EffectiveAt=Effective,Channel="Звонок",Contact="Продавец",
            Outcome="Обсудили осмотр",Conditions="Дорога после дождя",Comment="Комментарий" };
        var entry=new BusinessTimelineEntry { Id=Guid.NewGuid(),OrganizationId=item.OrganizationId,ObjectId=item.PropertyCaseId,
            ObjectType="PropertyCase",Kind="Negotiation",ActorEmployeeId=item.AuthorEmployeeId,RecordedAt=Recorded,EffectiveAt=Effective,
            Body="Канал: Звонок\nКонтакт: Продавец\nРезультат: Обсудили осмотр\nКомментарий",Title=item.Outcome };
        var matched=CaseTimelineCommunication.Find(entry,[item],_=>"Автор");
        Assert.IsNotNull(matched);Assert.AreEqual(item.Conditions,matched.Conditions);
        Assert.IsNull(CaseTimelineCommunication.Find(entry,[item,item],_=>"Автор"));
        entry.Body+="\nДополнительные сведения";
        Assert.IsNull(CaseTimelineCommunication.Find(entry,[item],_=>"Автор"));
        var raw=new TimelineItem(entry.Id,entry.Kind,entry.Title,entry.Body,"Автор",null,Recorded,Effective,null);
        var fallback=CaseFeedFormat.From(raw);Assert.AreEqual(entry.Body,fallback.Text);
        Assert.AreEqual(0,fallback.Details.Count);
        var task=CaseFeedFormat.From(new TimelineItem(Guid.NewGuid(),"CaseTaskSave","Задача добавлена после общения",
            item.Outcome+"\nБез срока","Автор",null,Recorded,null,null));
        Assert.AreNotEqual(fallback.Id,task.Id);StringAssert.Contains(task.Text,"Задача добавлена после общения:");
        StringAssert.Contains(task.Text,item.Outcome);Assert.AreEqual("Задача",task.Type);
    }

    [TestMethod]
    public void UnknownSystemBusinessChangesArePreservedAndUnsafeSourceUrlsAreInactive()
    {
        var row=CaseFeedFormat.From(new TimelineItem(Guid.NewGuid(),"CustomEvent","Уточнены сведения",
            "Старая площадь: 900 м²\nНовая площадь: 1000 м²","Автор",null,Recorded,null,null));
        Assert.AreEqual("Уточнены сведения",row.Text);StringAssert.Contains(row.Details.Single(),"Новая площадь: 1000 м²");
        Assert.IsNull(CaseSourceLinks.SafeUrl(null));Assert.IsNull(CaseSourceLinks.SafeUrl("javascript:alert(1)"));
        Assert.IsNull(CaseSourceLinks.SafeUrl("/relative"));Assert.IsNull(CaseSourceLinks.SafeUrl("https://user:pass@example.com/a"));
        Assert.AreEqual("https://www.avito.ru/item/123",CaseSourceLinks.SafeUrl("https://www.avito.ru/item/123"));
    }

    [TestMethod]
    [TestCategory("PostgreSQL")]
    public async Task CardDrawerAndNegotiationHistoryReadSameStructuredCommunication()
    {
        await using var f=await ProcurementTests.Phase1Fixture.CreateAsync(false,false);
        var ct=CancellationToken.None;
        Guid id=(await f.Workspace.CreateManualCaseAsync(f.Manager,new("Участок U-01","Москва",null,1000000m,900m,"Проверка отображения",Guid.CreateVersion7()),"create",ct)).CaseId;
        var property=await f.ReadCaseAsync(id);
        await f.Workspace.AddNegotiationAsync(f.Manager,new(id,property.Version,12000000m,10000000m,11000000m,
            "Телефон","Агент Иван","Договорились об осмотре","После дождя","Нужны сапоги","Проверить дорогу",Recorded.AddDays(2),Effective,Guid.CreateVersion7()),"communication",ct);
        var card=await f.Workspace.ReadCardAsync(f.Manager,id,ct);
        var reader=new ProcurementQueueV2ReadService(f.Factory,TimeProvider.System);
        var drawer=await reader.ReadDetailAsync(f.Manager,id,ct);
        var history=await reader.ReadNegotiationsAsync(f.Manager,id,0,20,ct);
        var cardEvent=card.Timeline.Single(x=>x.Kind=="Negotiation");
        var drawerEvent=drawer.Timeline.Single(x=>x.Kind=="Negotiation");
        Assert.IsNotNull(cardEvent.Communication);Assert.IsNotNull(drawerEvent.Communication);
        var expected=CaseFeedFormat.From(card.Negotiations.Single());
        foreach(var actual in new[]{CaseFeedFormat.From(cardEvent),CaseFeedFormat.From(drawerEvent),
            CaseFeedFormat.From(drawer.Negotiations.Single()),CaseFeedFormat.From(history.Items.Single())})
        {
            Assert.AreEqual(expected.Text,actual.Text);Assert.AreEqual(expected.Date,actual.Date);Assert.AreEqual(expected.FullAuthor,actual.FullAuthor);
            CollectionAssert.AreEqual(expected.Details.ToArray(),actual.Details.ToArray());
        }
        // Полная техническая история по-прежнему получает исходный Body.
        StringAssert.Contains(cardEvent.Body,"Канал: Телефон");
    }

    private static NegotiationView Communication()=>new(Guid.NewGuid(),null,null,null,"RUB","Телефон","Продавец",
        "Результат разговора","","","",null,"Александр Баимкин",Effective,Recorded);
}
