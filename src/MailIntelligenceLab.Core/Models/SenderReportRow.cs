namespace MailIntelligenceLab.Models;

public record SenderReportRow(
    string SenderAddress,
    string SenderName,
    int MessageCount,
    int MessagesWithAttachmentsCount,
    int AttachmentFileCount,
    double TotalAttachmentSizeMB,
    long TotalAttachmentSizeBytes,
    long TotalBodyLengthProxy,
    double AverageAgeDays,
    double AverageAgeYears,
    string OldestReceivedDate,
    string NewestReceivedDate
);