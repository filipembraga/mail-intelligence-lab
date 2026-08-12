namespace MailIntelligenceLab.Planning;

public record PlanValidationResult(
    IReadOnlyList<string> Errors,
    int TotalRows,
    int RowsMarkedForDeletion,
    int MessagesTargeted,
    long BytesTargeted
)
{
    public bool IsValid => Errors.Count == 0;
}