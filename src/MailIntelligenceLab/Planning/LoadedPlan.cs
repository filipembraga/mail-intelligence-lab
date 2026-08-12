using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public record LoadedPlan(
    string FileName,
    string FullPath,
    DateTime FreezeBoundUtc,
    IReadOnlyList<ActionPlanRow> Rows
);