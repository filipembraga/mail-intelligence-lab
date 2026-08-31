namespace MailIntelligenceLab.Planning;

// Reads what prior execution rounds already removed, so a new plan doesn't
// re-list a sender the mailbox no longer has anything left to act on for.
public static class ExecutionLogAggregator
{
    private static readonly HashSet<string> RemovedOutcomes = new(StringComparer.OrdinalIgnoreCase)
    {
        PlanExecutor.OutcomeDeleted,
        PlanExecutor.OutcomePurged,
        PlanExecutor.OutcomeAlreadyGone
    };

    // "Removed" means the message left the inbox — deleted (soft, recoverable),
    // purged (hard) or already-gone (removed by something else before this run
    // reached it).
    public static IReadOnlyDictionary<string, int> CountRemovedPerSender(IEnumerable<ExecutionLogRow> logRows) =>
        logRows
            .Where(row => RemovedOutcomes.Contains(row.Outcome))
            .GroupBy(row => row.SenderAddress, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
}