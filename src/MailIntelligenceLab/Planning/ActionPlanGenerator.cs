using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class ActionPlanGenerator
{
    public const string KeepAction = "";
    public const string DeleteAction = "delete";
    public const string PermanentDeleteAction = "permanent-delete";

    // Single source of truth for "does this cell mean act on this row?"
    public static bool IsActionable(string? action) =>
        Matches(action, DeleteAction) || Matches(action, PermanentDeleteAction);

    public static bool IsPermanentDelete(string? action) =>
        Matches(action, PermanentDeleteAction);

    private static bool Matches(string? action, string expected) =>
        (action ?? string.Empty).Trim().Equals(expected, StringComparison.OrdinalIgnoreCase);

    // A row is only useful if its address can be resolved by a Graph $filter.
    // "(unknown)" (null From) and LegacyExchangeDN values ("/o=...") cannot.
    public static bool IsResolvable(string? senderAddress) =>
        !string.IsNullOrWhiteSpace(senderAddress)
        && senderAddress.Contains('@')
        && !senderAddress.StartsWith("/o=", StringComparison.OrdinalIgnoreCase);

    public static PlanGenerationResult Generate(
        IEnumerable<SenderReportRow> reportRows,
        IReadOnlyDictionary<string, int>? alreadyRemovedBySender = null)
    {
        var removed = alreadyRemovedBySender ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resolvable = reportRows.Where(row => IsResolvable(row.SenderAddress)).ToList();
        int excludedCount = reportRows.Count() - resolvable.Count;

        // Graph's `eq` on an email address is case-insensitive, so two report rows
        // differing only in case resolve to the same messages at execution time.
        // Merge here, not in the report: the report stays a faithful record of what
        // Graph returned, and every matching rule lives in one layer.
        var groups = resolvable
            .GroupBy(row => row.SenderAddress, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int mergedCount = resolvable.Count - groups.Count;

        var candidates = groups
            .Select(group =>
            {
                string canonicalAddress = group.Key.ToLowerInvariant();
                int messageCount = group.Sum(row => row.MessageCount);
                long attachmentBytes = group.Sum(row => row.TotalAttachmentSizeBytes);

                double weightedAgeYears = messageCount == 0
                    ? 0
                    : group.Sum(row => row.AverageAgeYears * row.MessageCount) / messageCount;

                int adjustedMessageCount = messageCount - removed.GetValueOrDefault(canonicalAddress, 0);

                var row = new ActionPlanRow(
                    SenderAddress: canonicalAddress,
                    SenderName: group.OrderByDescending(r => r.MessageCount).First().SenderName,
                    MessageCount: adjustedMessageCount,
                    MessagesWithAttachmentsCount: group.Sum(r => r.MessagesWithAttachmentsCount),
                    AttachmentFileCount: group.Sum(r => r.AttachmentFileCount),
                    TotalAttachmentSizeMB: (long)Math.Round(attachmentBytes / 1024.0 / 1024.0),
                    TotalAttachmentSizeBytes: attachmentBytes,
                    AverageAgeYears: (int)Math.Round(weightedAgeYears),
                    OldestReceivedDate: group.Min(r => r.OldestReceivedDate)!,
                    NewestReceivedDate: group.Max(r => r.NewestReceivedDate)!,
                    Action: KeepAction);

                return (Row: row, FullyRemoved: adjustedMessageCount <= 0);
            })
            .ToList();

        int excludedAsFullyRemoved = candidates.Count(c => c.FullyRemoved);

        var planRows = candidates
            .Where(c => !c.FullyRemoved)
            .Select(c => c.Row)
            .OrderByDescending(row => row.TotalAttachmentSizeBytes)
            .ThenByDescending(row => row.MessageCount)
            .ToList();

        return new PlanGenerationResult(planRows, excludedCount, mergedCount, excludedAsFullyRemoved);
    }
}

public record PlanGenerationResult(
    IReadOnlyList<ActionPlanRow> Rows,
    int ExcludedAsUnresolvable,
    int MergedByCase,
    int ExcludedAsFullyRemoved = 0
);