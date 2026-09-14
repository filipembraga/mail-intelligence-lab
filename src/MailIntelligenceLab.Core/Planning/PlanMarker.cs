using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class PlanMarker
{
    public static PlanMarkingResult Apply(IReadOnlyList<ActionPlanRow> rows, IReadOnlyList<PlanMark> marks)
    {
        var errors = new List<string>();

        var duplicates = marks
            .GroupBy(mark => mark.SenderAddress, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicate in duplicates)
        {
            errors.Add($"Duplicate mark for sender: {duplicate}");
        }

        var knownAddresses = rows
            .Select(row => row.SenderAddress)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mark in marks)
        {
            if (!knownAddresses.Contains(mark.SenderAddress))
            {
                errors.Add($"Unknown sender: {mark.SenderAddress}");
            }

            if (!string.IsNullOrWhiteSpace(mark.Action) && !ActionPlanGenerator.IsActionable(mark.Action))
            {
                errors.Add($"Unrecognized action '{mark.Action}' for sender: {mark.SenderAddress}");
            }
        }

        if (errors.Count > 0)
        {
            return new PlanMarkingResult(errors, []);
        }

        var actionBySender = marks.ToDictionary(
            mark => mark.SenderAddress,
            mark => mark.Action?.Trim() ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);

        var markedRows = rows
            .Select(row => actionBySender.TryGetValue(row.SenderAddress, out string? action)
                ? row with { Action = action }
                : row)
            .ToList();

        return new PlanMarkingResult(errors, markedRows);
    }
}
