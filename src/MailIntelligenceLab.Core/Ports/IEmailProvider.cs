using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Ports;

// Every Graph operation the domain actually calls today, and nothing else.
// Concurrency and orchestration (how many requests at once, retry policy)
// stay with the caller — this interface exposes one-message primitives.
public interface IEmailProvider
{
    Task<(string? DisplayName, string? Mail)> GetCurrentUserAsync();

    Task<IReadOnlyList<EmailMetadata>> ReadInboxMetadataAsync(int? maxMessages = null);

    Task<(long Bytes, int FileCount)> GetAttachmentInfoAsync(string messageId);

    Task<int> CountFromSenderAsync(
        string folderId, string senderAddress, DateTime? receivedOnOrBeforeUtc = null);

    Task<IReadOnlyList<MessageSummary>> ListFromSenderAsync(
        string folderId, string senderAddress, DateTime? receivedOnOrBeforeUtc = null);

    Task DeleteMessageAsync(string messageId);
    Task PermanentDeleteMessageAsync(string messageId);
}

public record MessageSummary(string Id, string? Subject, DateTimeOffset? ReceivedDateTime, bool HasAttachments);