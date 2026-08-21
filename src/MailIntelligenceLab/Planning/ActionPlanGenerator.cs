using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class ActionPlanGenerator
{
    public const string KeepAction = "";
    public const string DeleteAction = "delete";
    public const string PermanentDeleteAction = "permanent-delete";

    // A row is only useful if its address can be resolved by a Graph $filter.
    // "(unknown)" (null From) and LegacyExchangeDN values ("/o=...") cannot.
    public static bool IsResolvable(string? senderAddress) =>
        !string.IsNullOrWhiteSpace(senderAddress)
        && senderAddress.Contains('@')
        && !senderAddress.StartsWith("/o=", StringComparison.OrdinalIgnoreCase);

    public static PlanGenerationResult Generate(IEnumerable<SenderReportRow> reportRows)
    {
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

        var planRows = groups
            .Select(group =>
            {
                // The lowercase form is the one Graph echoes back and the one that
                // reads consistently in a hand-sorted file; casing carries no meaning.
                string canonicalAddress = group.Key.ToLowerInvariant();

                int messageCount = group.Sum(row => row.MessageCount);
                long attachmentBytes = group.Sum(row => row.TotalAttachmentSizeBytes);

                // Weighted by message count: averaging two averages is wrong when one
                // row has 400 messages and the other has 3.
                double weightedAgeYears = messageCount == 0
                    ? 0
                    : group.Sum(row => row.AverageAgeYears * row.MessageCount) / messageCount;

                return new ActionPlanRow(
                    SenderAddress: canonicalAddress,
                    SenderName: group.OrderByDescending(row => row.MessageCount).First().SenderName,
                    MessageCount: messageCount,
                    MessagesWithAttachmentsCount: group.Sum(row => row.MessagesWithAttachmentsCount),
                    AttachmentFileCount: group.Sum(row => row.AttachmentFileCount),
                    TotalAttachmentSizeMB: (long)Math.Round(attachmentBytes / 1024.0 / 1024.0),
                    TotalAttachmentSizeBytes: attachmentBytes,
                    AverageAgeYears: (int)Math.Round(weightedAgeYears),
                    OldestReceivedDate: group.Min(row => row.OldestReceivedDate)!,
                    NewestReceivedDate: group.Max(row => row.NewestReceivedDate)!,
                    Action: KeepAction);
            })
            .OrderByDescending(row => row.TotalAttachmentSizeBytes)
            .ThenByDescending(row => row.MessageCount)
            .ToList();

        return new PlanGenerationResult(planRows, excludedCount, mergedCount);
    }
}

public record PlanGenerationResult(
    IReadOnlyList<ActionPlanRow> Rows,
    int ExcludedAsUnresolvable,
    int MergedByCase
);
