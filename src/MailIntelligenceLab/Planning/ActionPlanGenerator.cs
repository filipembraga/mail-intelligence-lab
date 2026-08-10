using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class ActionPlanGenerator
{
    public const string KeepAction = "";
    public const string DeleteAction = "delete";

    // A row is only useful if its address can be resolved by a Graph $filter.
    // "(unknown)" (null From) and LegacyExchangeDN values ("/o=...") cannot.
    public static bool IsResolvable(string? senderAddress) =>
        !string.IsNullOrWhiteSpace(senderAddress)
        && senderAddress.Contains('@')
        && !senderAddress.StartsWith("/o=", StringComparison.OrdinalIgnoreCase);

    public static List<ActionPlanRow> Generate(IEnumerable<SenderReportRow> reportRows) =>
        reportRows
            .Where(row => IsResolvable(row.SenderAddress))
            .OrderByDescending(row => row.TotalAttachmentSizeBytes)
            .ThenByDescending(row => row.MessageCount)
            .Select(row => new ActionPlanRow(
                SenderAddress: row.SenderAddress,
                SenderName: row.SenderName,
                MessageCount: row.MessageCount,
                MessagesWithAttachmentsCount: row.MessagesWithAttachmentsCount,
                AttachmentFileCount: row.AttachmentFileCount,
                TotalAttachmentSizeMB: (long)Math.Round(row.TotalAttachmentSizeMB),
                TotalAttachmentSizeBytes: row.TotalAttachmentSizeBytes,
                AverageAgeYears: (int)Math.Round(row.AverageAgeYears),
                OldestReceivedDate: row.OldestReceivedDate,
                NewestReceivedDate: row.NewestReceivedDate,
                Action: KeepAction))
            .ToList();
}