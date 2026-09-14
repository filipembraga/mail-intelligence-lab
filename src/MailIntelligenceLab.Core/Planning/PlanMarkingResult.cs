using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public record PlanMarkingResult(
    IReadOnlyList<string> Errors,
    IReadOnlyList<ActionPlanRow> Rows
)
{
    public bool IsValid => Errors.Count == 0;
}
