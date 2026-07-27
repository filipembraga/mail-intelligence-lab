namespace MailIntelligenceLab.Models;

public record SenderAggregate(
    string SenderAddress,
    string SenderName,
    int MessageCount,
    long TotalBodyLength
);

public enum AgeBucket
{
    Days0To30,
    Days31To90,
    Days91To365,
    MoreThan365,
    Unknown
}

public static class AgeBucketDisplay
{
    public static readonly Dictionary<AgeBucket, string> Labels = new()
    {
        [AgeBucket.Days0To30] = "0-30 dias",
        [AgeBucket.Days31To90] = "31-90 dias",
        [AgeBucket.Days91To365] = "91-365 dias",
        [AgeBucket.MoreThan365] = "Mais de 365 dias",
        [AgeBucket.Unknown] = "Idade desconhecida"
    };
}