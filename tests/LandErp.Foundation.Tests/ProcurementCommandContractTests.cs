using System.Text.Json;
using LandErp.Application.Modules.Procurement.Contracts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class ProcurementCommandContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void ManualCaseCommandIdRoundTripsThroughTheExistingJsonContract()
    {
        CreateManualPropertyCase command = new("Ручной объект", "Химки", null, 4_000_000m, 900m,
            "Синтетический контакт", Guid.CreateVersion7());
        string json = JsonSerializer.Serialize(command, WebJson);
        CreateManualPropertyCase? restored = JsonSerializer.Deserialize<CreateManualPropertyCase>(json, WebJson);
        Assert.AreEqual(command, restored);
        StringAssert.Contains(json, "commandId");
    }

    [TestMethod]
    public void NoteAndNegotiationCommandIdsRoundTripWithoutChangingThePayload()
    {
        Guid caseId = Guid.CreateVersion7();
        DateTimeOffset effectiveAt = DateTimeOffset.UtcNow;
        AddCaseNote note = new(caseId, 7, "Заметка", false, "", null, Guid.CreateVersion7());
        AddNegotiation contact = new(caseId, 7, null, 3_800_000m, null, "Телефон", "Собственник",
            "Договорились о встрече", "", "Комментарий", "Осмотр", null, effectiveAt, Guid.CreateVersion7());
        Assert.AreEqual(note, JsonSerializer.Deserialize<AddCaseNote>(JsonSerializer.Serialize(note, WebJson), WebJson));
        Assert.AreEqual(contact, JsonSerializer.Deserialize<AddNegotiation>(JsonSerializer.Serialize(contact, WebJson), WebJson));
    }

    [TestMethod]
    public void OldJsonAndOldConstructorsDoNotRequireCommandId()
    {
        const string json = """
            {"title":"Ручной объект","location":null,"cadastralNumber":null,"price":null,"areaSquareMeters":null,"comment":"Контекст объекта"}
            """;
        CreateManualPropertyCase? restored = JsonSerializer.Deserialize<CreateManualPropertyCase>(json, WebJson);
        Assert.IsNotNull(restored);
        Assert.IsNull(restored.CommandId);
        AddCaseNote note = new(Guid.CreateVersion7(), 1, "Заметка", false, "", null);
        AddNegotiation contact = new(note.CaseId, 1, null, null, null, "Телефон", "", "Контакт", "", "", "", null, DateTimeOffset.UtcNow);
        Assert.IsNull(note.CommandId);
        Assert.IsNull(contact.CommandId);
    }
}
