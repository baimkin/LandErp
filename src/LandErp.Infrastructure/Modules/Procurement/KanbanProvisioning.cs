using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public static class KanbanProvisioning
{
    public static KanbanPipeline CreateDefault(LandErpDbContext db, Guid org)
    {
        var p = new KanbanPipeline { Id = Guid.CreateVersion7(), OrganizationId = org, Name = "Закупка", IsDefault = true };
        db.KanbanPipelines.Add(p);
        (string Name, string Description)[] defaults = [
            ("Взят в работу", "Подошла цена, локация; менеджер готовится к звонку или уже смотрит КН, если он есть"),
            ("Первый созвон", "Заданы вопросы по анкете, зафиксированы ответы, взят КН"),
            ("Переговоры", "Участок подходит по земельному анализу с кадастровой карты, стратегии, ответы продавца по анкете устраивают; вопрос только цены и осмотра"),
            ("Планируется осмотр", "Цена участка близка к нужной или есть ожидание сторговать до нужной после осмотра"),
            ("Осмотр состоялся", "Фото/видео и фиксация по анкете осмотра"),
            ("Финальные переговоры по цене", "Уже всё подходит, остались торг и юридическая проверка"),
            ("Проверка юристом", "Уже готовы отдавать задаток"),
            ("Задаток переведен", "Отдельное пояснение не дано"),
            ("Финальная проверка юристом", "Итог — полное заключение юриста"),
            ("Сделка оформлена в Дом клик", "Отдельное пояснение не дано"),
            ("Участок в собственности", "Отдельное пояснение не дано"),
            ("Не подходит", "Добавлено по прямому согласованию владельца после стадий заказчика") ];
        for (int i = 0; i < defaults.Length; i++) db.KanbanStages.Add(new() {
            Id = Guid.CreateVersion7(), OrganizationId = org, PipelineId = p.Id, Name = defaults[i].Name, Description = defaults[i].Description,
            IsInitial = i == 0, SortOrder = i, Kind = i == 10 ? KanbanStageKind.PositiveFinal : i == 11 ? KanbanStageKind.NegativeFinal : KanbanStageKind.Working,
            ColorKey = i == 10 ? "success" : i == 11 ? "danger" : i > 5 ? "purple" : "info" });
        return p;
    }

    // Explicit setup only. Any existing membership, including another pipeline or
    // a transferred position, means the case was already placed and must not be reimported.
    public static async Task InitializeAsync(LandErpDbContext db, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (Guid org in await db.Organizations.Select(x => x.Id).OrderBy(x => x).ToArrayAsync(ct))
        {
            await EmployeeWorkInvariant.LockOrganizationAsync(db, org, ct);
            var p = await db.KanbanPipelines.SingleOrDefaultAsync(x => x.OrganizationId == org && x.IsDefault && x.IsActive, ct);
            if (p == null) { p = CreateDefault(db, org); await db.SaveChangesAsync(ct); }
            var s = await db.KanbanStages.SingleAsync(x => x.PipelineId == p.Id && x.IsActive && x.IsInitial, ct);
            var cases = await db.PropertyCases.Where(x => x.OrganizationId == org && x.StageId != "acquired" && x.StageId != "rejected"
                && !db.KanbanMemberships.Any(m => m.OrganizationId == org && m.PropertyCaseId == x.Id)).Select(x => x.Id).ToArrayAsync(ct);
            foreach (var id in cases)
            {
                var m = KanbanWorkspace.NewMembership(org, id, p, s, now); db.KanbanMemberships.Add(m);
                KanbanWorkspace.AddTransition(db, m, null, null, p, s, null, null, "InitialImport", now);
            }
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    internal static async Task JoinNewCaseAsync(LandErpDbContext db, PropertyCase propertyCase, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        // Caller holds the organization lock and the case-creation transaction.
        var p = await db.KanbanPipelines.SingleAsync(x => x.OrganizationId == propertyCase.OrganizationId && x.IsDefault && x.IsActive, ct);
        var s = await db.KanbanStages.SingleAsync(x => x.PipelineId == p.Id && x.IsActive && x.IsInitial, ct);
        var m = KanbanWorkspace.NewMembership(propertyCase.OrganizationId, propertyCase.Id, p, s, now);
        db.KanbanMemberships.Add(m);
        KanbanWorkspace.AddTransition(db, m, null, null, p, s, actor, null, "Created", now);
    }
}
