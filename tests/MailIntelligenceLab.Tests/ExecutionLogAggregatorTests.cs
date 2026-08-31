using MailIntelligenceLab.Planning;
using Xunit;

namespace MailIntelligenceLab.Tests;

public class ExecutionLogAggregatorTests
{
    private static ExecutionLogRow Log(string sender, string outcome) =>
        new(
            ExecutedAtUtc: "2026-08-21T00:00:00Z",
            PlanFile: "test-plan.csv",
            SenderAddress: sender,
            MessageId: Guid.NewGuid().ToString(),
            Outcome: outcome,
            Error: "");

    [Fact]
    public void CountRemovedPerSender_counts_deleted_purged_and_already_gone()
    {
        var logs = new[]
        {
            Log("a@example.com", ExecutionOutcomes.Deleted),
            Log("a@example.com", ExecutionOutcomes.Purged),
            Log("a@example.com", ExecutionOutcomes.AlreadyGone),
        };

        var result = ExecutionLogAggregator.CountRemovedPerSender(logs);

        Assert.Equal(3, result["a@example.com"]);
    }

    [Fact]
    public void CountRemovedPerSender_ignores_failed_rows()
    {
        var logs = new[]
        {
            Log("a@example.com", ExecutionOutcomes.Deleted),
            Log("a@example.com", ExecutionOutcomes.Failed),
        };

        var result = ExecutionLogAggregator.CountRemovedPerSender(logs);

        Assert.Equal(1, result["a@example.com"]);
    }

    [Fact]
    public void CountRemovedPerSender_merges_senders_differing_only_in_case()
    {
        var logs = new[]
        {
            Log("Someone@Example.com", ExecutionOutcomes.Deleted),
            Log("someone@example.com", ExecutionOutcomes.Deleted),
        };

        var result = ExecutionLogAggregator.CountRemovedPerSender(logs);

        Assert.Equal(2, result["someone@example.com"]);
    }

    [Fact]
    public void CountRemovedPerSender_returns_empty_for_no_logs()
    {
        var result = ExecutionLogAggregator.CountRemovedPerSender(Array.Empty<ExecutionLogRow>());

        Assert.Empty(result);
    }
}