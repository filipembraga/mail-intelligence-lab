namespace MailIntelligenceLab.Models;

public record EmailMetadata(
    string Id,
    string SenderAddress,
    string SenderName,
    DateTimeOffset? ReceivedDateTime,
    bool HasAttachments,
    string ParentFolderId,
    int BodyLength,
    bool BodyHasCidReference
);