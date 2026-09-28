using LandErp.Application.Modules.Procurement.Contracts;

namespace LandErp.Server.Components.Procurement;

// Presentation only: the command handler remains the authority. Changed is business
// review state; viewing source comparisons must never unlock an approval here.
public static class CaseDecisionPresentation
{
    public static string Title(ProcurementAction action) => action switch
    {
        ProcurementAction.Forward => "Передать руководителю",
        ProcurementAction.Return => "Вернуть менеджеру",
        ProcurementAction.Approve => "Продолжить работу",
        ProcurementAction.Monitor => "Наблюдать",
        ProcurementAction.Clarify => "Уточнить данные",
        ProcurementAction.Reject => "Отклонить",
        _ => "Решение по объекту"
    };

    public static string State(CaseCard card) => card.Item.Stage switch
    {
        "acquired" => "Покупка оформлена. Закупка завершена.",
        "rejected" => card.Item.Changed ? "Объект отклонён. Новые данные позволяют повторный анализ." : "Объект отклонён. Повторный анализ доступен после изменения данных источников.",
        "monitor" => "Объект на наблюдении.",
        "approved" or "negotiation" => "Продолжение работы одобрено. Покупка ещё не оформлена.",
        "pending_head" => "Ожидается решение руководителя закупки.",
        "returned" => "Объект возвращён менеджеру для доработки.",
        "clarify" => "Требуется уточнить данные объекта.",
        _ => "Объект в работе. Решение принимает назначенный менеджер."
    };

    public static string Responsibility(CaseCard card)
    {
        if (card.Item.Stage == "acquired") return "Новые решения по закупке недоступны.";
        string assignee = string.IsNullOrWhiteSpace(card.Item.Assignee)
            ? "Исполнитель не указан. Требуется проверить назначение."
            : $"Текущий исполнитель: {card.Item.Assignee}.";
        if (!card.CanManageDossier) return assignee + " У вас доступ только для просмотра этого объекта.";
        if (Actions(card).Count > 0) return assignee + " Вы можете принять решение.";
        if (card.Item.Stage is "rejected" or "approved" && !card.Item.Changed) return assignee + " Решение завершено; новых данных для анализа нет.";
        return assignee + " " + (card.Item.Stage == "pending_head"
            ? "Решение доступно назначенному руководителю с правом согласования; автор анализа не может согласовать его сам."
            : "Решение доступно назначенному менеджеру.");
    }

    public static IReadOnlyList<ProcurementAction> Actions(CaseCard card)
    {
        if (card.Item.Stage == "acquired" || card.Item.Stage is "approved" or "rejected" && !card.Item.Changed) return [];
        if (card.CanHeadDecide) return [ProcurementAction.Approve, ProcurementAction.Return, ProcurementAction.Monitor, ProcurementAction.Reject];
        if (card.CanManagerDecide) return [ProcurementAction.Forward, ProcurementAction.Clarify, ProcurementAction.Monitor, ProcurementAction.Reject];
        return [];
    }

    public static string? Blocked(CaseCard card, ProcurementAction action)
    {
        if (!Actions(card).Contains(action)) return "Это решение больше недоступно. Актуальное состояние указано выше.";
        if (action == ProcurementAction.Forward && card.Heads.Count == 0) return "Нет доступного руководителя для передачи. Проверьте назначения и права сотрудников.";
        if (action == ProcurementAction.Return && card.Managers.Count == 0) return "Нет доступного менеджера для возврата. Проверьте назначения и права сотрудников.";
        if (card.CanHeadDecide && action != ProcurementAction.Return && !card.Managers.Any(x => x.EmployeeId == card.ManagerEmployeeId))
            return "Менеджер объекта недоступен для продолжения процесса. Верните объект другому доступному менеджеру.";
        if (action == ProcurementAction.Approve && card.Item.Changed) return "Источники изменились после передачи. Верните объект менеджеру для обновления анализа. Просмотр изменений не заменяет анализ.";
        return null;
    }

    public static bool HasActions(CaseCard card) => Actions(card).Any(action => Blocked(card, action) == null);
    public static bool CanPurchase(CaseCard card) => card.CanConfirmPurchase && card.Item.Stage is not ("monitor" or "rejected" or "acquired");
}
