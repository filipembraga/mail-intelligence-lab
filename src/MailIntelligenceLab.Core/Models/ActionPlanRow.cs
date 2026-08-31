namespace MailIntelligenceLab.Models;

// Mirrors SenderReportRow, minus proxy/derived columns, with all numerics as
// integers — decimals with a '.' are parsed as text (or dates) by locale-aware
// spreadsheets, which breaks sorting. This file is meant to be sorted by hand.
public record ActionPlanRow(
    string SenderAddress,
    string SenderName,
    int MessageCount,
    int MessagesWithAttachmentsCount,
    int AttachmentFileCount,
    long TotalAttachmentSizeMB,
    long TotalAttachmentSizeBytes,
    int AverageAgeYears,
    string OldestReceivedDate,
    string NewestReceivedDate,
    string Action
);