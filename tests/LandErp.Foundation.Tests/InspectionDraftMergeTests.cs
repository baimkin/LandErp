using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Server.Components.Procurement;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class InspectionDraftMergeTests
{
    private static InspectionView View()=>new(Guid.NewGuid(),InspectionStatus.Draft,"","","Inspector",DateTimeOffset.UtcNow,null,1,
        Enumerable.Range(0,2).Select(i=>new InspectionItemView(Guid.NewGuid(),Guid.NewGuid(),1,$"Item {i}",i,
            InspectionAnswerType.Text,[],"","",true,true,InspectionItemStatus.Unanswered,"","",1)).ToArray());
    private static InspectionDraft Edit(InspectionView source,string revision)=>InspectionDraft.From(source) with {Baseline=InspectionDraft.From(source),Revision=revision};
    private static InspectionView Change(InspectionView view,int index,string answer,string note="")=>view with
    {Version=view.Version+1,Items=view.Items.Select((x,i)=>i==index?x with{Version=x.Version+1,Status=InspectionItemStatus.Answered,Answer=answer,Note=note}:x).ToArray()};

    [TestMethod]
    public void DisjointAnswersMergeAndUseLatestVersions()
    {
        var original=View();var local=Edit(original,"a");local.Answers[0].Status=InspectionItemStatus.Answered;local.Answers[0].Answer="local";
        var server=Change(original,1,"server");var result=InspectionDraftMerge.Merge(server,[local]);
        Assert.AreEqual(0,result.Conflicts.Count);Assert.AreEqual("local",result.Draft.Answers[0].Answer);
        Assert.AreEqual("server",result.Draft.Answers[1].Answer);Assert.AreEqual(server.Version,result.Draft.Version);
        Assert.AreEqual(server.Items[1].Version,result.Draft.Answers[1].Version);
    }
    [TestMethod]
    public void NoteAndAnswerOnSameItemMergeWithoutConflict()
    {
        var original=View();var local=Edit(original,"a");local.Answers[0].Note="local note";
        var result=InspectionDraftMerge.Merge(Change(original,0,"server answer"),[local]);
        Assert.AreEqual(0,result.Conflicts.Count);Assert.AreEqual("local note",result.Draft.Answers[0].Note);
        Assert.AreEqual("server answer",result.Draft.Answers[0].Answer);
    }
    [TestMethod]
    public void DifferentSameFieldEditsRequireChoiceButIdenticalChangesDoNot()
    {
        var original=View();var local=Edit(original,"a");local.Answers[0].Status=InspectionItemStatus.Answered;local.Answers[0].Answer="local";
        var result=InspectionDraftMerge.Merge(Change(original,0,"server"),[local]);
        Assert.AreEqual(1,result.Conflicts.Count);Assert.AreEqual(2,result.Conflicts[0].Choices.Length);
        InspectionDraftMerge.Choose(result.Draft,result.Conflicts[0].Key,result.Conflicts[0].Choices.Single(x=>x.Text=="server"));
        Assert.AreEqual("server",result.Draft.Answers[0].Answer);
        Assert.AreEqual(0,InspectionDraftMerge.Merge(Change(original,0,"local"),[local]).Conflicts.Count);
    }
    [TestMethod]
    public void ExplicitClearAndNotCheckedAreEdits()
    {
        var original=Change(View(),0,"old","old note");var local=Edit(original,"a");
        local.Answers[0].Status=InspectionItemStatus.NotChecked;local.Answers[0].Answer="";local.Answers[0].Note="";
        var result=InspectionDraftMerge.Merge(original,[local]);
        Assert.AreEqual(0,result.Conflicts.Count);Assert.AreEqual(InspectionItemStatus.NotChecked,result.Draft.Answers[0].Status);
        Assert.AreEqual("",result.Draft.Answers[0].Note);
    }
    [TestMethod]
    public void TwoUnsavedBranchesMergeAndResolvedRevisionDoesNotReappear()
    {
        var original=View();var a=Edit(original,"a");var b=Edit(original,"b");
        a.Answers[0].Note="A";b.Answers[1].Note="B";
        var merged=InspectionDraftMerge.Merge(original,[a,b]);Assert.AreEqual(0,merged.Conflicts.Count);
        var resolved=merged.Draft with {Revision="resolved"};resolved.Answers[0].Note="chosen";
        var again=InspectionDraftMerge.Merge(original,[a,b,resolved]);
        Assert.AreEqual(0,again.Conflicts.Count);Assert.AreEqual("chosen",again.Draft.Answers[0].Note);
        Assert.AreEqual("B",again.Draft.Answers[1].Note);
    }
    [TestMethod]
    public void NewConcurrentEditAfterRebaseStillRequiresChoice()
    {
        var original=View();var local=Edit(original,"a");local.Answers[0].Note="local";
        var merged=InspectionDraftMerge.Merge(original,[local]).Draft with {Revision="rebased"};
        var server=Change(original,0,"","second edit");
        Assert.AreEqual(1,InspectionDraftMerge.Merge(server,[merged]).Conflicts.Count);
    }
    [TestMethod]
    public void LegacyDraftWithoutBaselinePreservesDifferentAnswersForReview()
    {
        var original=View();var legacy=InspectionDraft.From(original) with {Revision="legacy"};legacy.Answers[0].Note="local";
        var result=InspectionDraftMerge.Merge(Change(original,0,"remote","remote note"),[legacy]);
        Assert.IsTrue(result.Conflicts.Any(x=>x.Legacy&&x.Key.StartsWith("note:",StringComparison.Ordinal)));
        Assert.IsTrue(result.Conflicts.Single(x=>x.Key.StartsWith("note:",StringComparison.Ordinal)).Choices.Any(x=>x.Text=="local"));
    }
    [TestMethod]
    public void ConclusionDecisionAndNewlinesMergeIndependently()
    {
        var original=View();var local=Edit(original,"a") with {Conclusion="one\r\ntwo"};
        var server=original with {Version=2,OverallConclusion="one\ntwo",PreliminaryDecision="server decision"};
        var result=InspectionDraftMerge.Merge(server,[local]);Assert.AreEqual(0,result.Conflicts.Count);
        Assert.AreEqual("server decision",result.Draft.Decision);
    }
    [TestMethod]
    public void EquivalentNumericFormattingIsNotAConflict()
    {
        var initial=View();var original=initial with {Items=initial.Items.Select(x=>x with {AnswerType=InspectionAnswerType.Number}).ToArray()};
        var local=Edit(original,"a");local.Answers[0].Status=InspectionItemStatus.Answered;local.Answers[0].Answer="1,00";
        var result=InspectionDraftMerge.Merge(Change(original,0,"1"),[local]);
        Assert.AreEqual(0,result.Conflicts.Count);Assert.AreEqual("1",result.Draft.Answers[0].Answer);
    }
}
