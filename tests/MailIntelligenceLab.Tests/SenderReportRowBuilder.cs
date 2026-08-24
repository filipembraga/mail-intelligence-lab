using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Tests;

internal sealed class SenderReportRowBuilder
{
    private string _senderAddress = "someone@example.com";
    private string _senderName = "Someone";
    private int _messageCount = 1;
    private int _messagesWithAttachmentsCount;
    private int _attachmentFileCount;
    private long _totalAttachmentSizeBytes;
    private double _averageAgeYears = 1;

    public SenderReportRowBuilder From(string senderAddress)
    {
        _senderAddress = senderAddress;
        return this;
    }

    public SenderReportRowBuilder Named(string senderName)
    {
        _senderName = senderName;
        return this;
    }

    public SenderReportRowBuilder WithMessageCount(int messageCount)
    {
        _messageCount = messageCount;
        return this;
    }

    public SenderReportRowBuilder WithAttachmentBytes(long bytes)
    {
        _totalAttachmentSizeBytes = bytes;
        return this;
    }

    public SenderReportRowBuilder WithAttachmentFileCount(int count)
    {
        _attachmentFileCount = count;
        return this;
    }

    public SenderReportRowBuilder WithMessagesWithAttachments(int count)
    {
        _messagesWithAttachmentsCount = count;
        return this;
    }

    public SenderReportRowBuilder WithAverageAgeYears(double years)
    {
        _averageAgeYears = years;
        return this;
    }

    public SenderReportRow Build() => new(
        SenderAddress: _senderAddress,
        SenderName: _senderName,
        MessageCount: _messageCount,
        MessagesWithAttachmentsCount: _messagesWithAttachmentsCount,
        AttachmentFileCount: _attachmentFileCount,
        TotalAttachmentSizeMB: Math.Round(_totalAttachmentSizeBytes / 1024.0 / 1024.0, 2),
        TotalAttachmentSizeBytes: _totalAttachmentSizeBytes,
        TotalBodyLengthProxy: 0,
        AverageAgeDays: Math.Round(_averageAgeYears * 365.25, 1),
        AverageAgeYears: _averageAgeYears,
        OldestReceivedDate: "2010-01-01",
        NewestReceivedDate: "2024-01-01");
}