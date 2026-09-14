using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Tests;

internal sealed class ActionPlanRowBuilder
{
    private string _senderAddress = "someone@example.com";
    private string _senderName = "Someone";
    private int _messageCount = 12;
    private int _messagesWithAttachmentsCount = 3;
    private int _attachmentFileCount = 5;
    private long _totalAttachmentSizeBytes = 4_194_304;
    private int _averageAgeYears = 2;
    private string _action = "";

    public ActionPlanRowBuilder From(string senderAddress)
    {
        _senderAddress = senderAddress;
        return this;
    }

    public ActionPlanRowBuilder Named(string senderName)
    {
        _senderName = senderName;
        return this;
    }

    public ActionPlanRowBuilder WithMessageCount(int messageCount)
    {
        _messageCount = messageCount;
        return this;
    }

    public ActionPlanRowBuilder WithAction(string action)
    {
        _action = action;
        return this;
    }

    public ActionPlanRow Build() => new(
        SenderAddress: _senderAddress,
        SenderName: _senderName,
        MessageCount: _messageCount,
        MessagesWithAttachmentsCount: _messagesWithAttachmentsCount,
        AttachmentFileCount: _attachmentFileCount,
        TotalAttachmentSizeMB: (long)Math.Round(_totalAttachmentSizeBytes / 1024.0 / 1024.0),
        TotalAttachmentSizeBytes: _totalAttachmentSizeBytes,
        AverageAgeYears: _averageAgeYears,
        OldestReceivedDate: "2010-01-01",
        NewestReceivedDate: "2024-01-01",
        Action: _action);
}
