using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class ActionPlanValidator
{
    public static PlanValidationResult Validate(IReadOnlyList<ActionPlanRow> planRows)
    {
        var errors = new List<string>();

        // A spreadsheet copy-paste is the most likely way this file gets corrupted.
        // Two rows for the same sender means the intent is ambiguous — refuse to guess.
        var duplicates = planRows
            .GroupBy(row => row.SenderAddress, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key);

        foreach (var duplicate in duplicates)
        {
            errors.Add($"Duplicate sender: {duplicate}");
        }

        var markedForDeletion = new List<ActionPlanRow>();

        foreach (var row in planRows)
        {
            string action = (row.Action ?? string.Empty).Trim();

            if (action.Length == 0)
            {
                continue;
            }

            if (!ActionPlanGenerator.IsActionable(action))
            {
                errors.Add($"Unrecognized action '{action}' for sender: {row.SenderAddress}");
                continue;
            }

            if (!ActionPlanGenerator.IsResolvable(row.SenderAddress))
            {
                errors.Add($"Sender cannot be resolved by a Graph filter: {row.SenderAddress}");
                continue;
            }

            markedForDeletion.Add(row);
        }

        return new PlanValidationResult(
            Errors: errors,
            TotalRows: planRows.Count,
            RowsMarkedForDeletion: markedForDeletion.Count,
            RowsMarkedForPermanentDeletion: markedForDeletion.Count(row =>
                ActionPlanGenerator.IsPermanentDelete(row.Action)),
            MessagesTargeted: markedForDeletion.Sum(row => row.MessageCount),
            BytesTargeted: markedForDeletion.Sum(row => row.TotalAttachmentSizeBytes)
        );
    }
}