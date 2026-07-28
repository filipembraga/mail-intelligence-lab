namespace MailIntelligenceLab.Models;

public record SenderReportRow(
    string SenderAddress,
    string SenderName,
    int MessageCount,
    long TotalBodyLengthProxy,
    double AverageAgeDays,
    double AverageAgeYears,
    string OldestReceivedDate,
    string NewestReceivedDate,
    int AttachmentCount
);