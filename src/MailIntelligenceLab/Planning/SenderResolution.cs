namespace MailIntelligenceLab.Planning;

public record SenderResolution(
    string SenderAddress,
    int PlannedMessageCount,
    int ResolvedMessageCount,
    string? Error
)
{
    public int Drift => ResolvedMessageCount - PlannedMessageCount;
}