using MailIntelligenceLab.Models;
using MailIntelligenceLab.Ports;

namespace MailIntelligenceLab.Planning;

public sealed class PlanExecutor(IEmailProvider emailProvider)
{
    // Systemic failures (expired token, throttling) look like many individual
    // failures in a row. Independent failures are worth continuing through;
    // a repeated one means something changed since preview.
    private const int ConsecutiveFailureLimit = 10;

    public async Task<ExecutionSummary> ExecuteAsync(
        ActionPlanRow row,
        DateTime freezeBoundUtc,
        string planFileName,
        Action<ExecutionLogRow> onRowCompleted)
    {
        var messages = await emailProvider.ListFromSenderAsync("inbox", row.SenderAddress, freezeBoundUtc);

        int deleted = 0;
        int alreadyGone = 0;
        int failed = 0;
        int consecutiveFailures = 0;
        bool aborted = false;

        bool permanent = ActionPlanGenerator.IsPermanentDelete(row.Action);

        foreach (var message in messages)
        {
            var result = permanent
                ? await emailProvider.PermanentDeleteMessageAsync(message.Id)
                : await emailProvider.DeleteMessageAsync(message.Id);

            string outcome;
            switch (result.Outcome)
            {
                case DeleteOutcome.Deleted:
                    outcome = permanent ? ExecutionOutcomes.Purged : ExecutionOutcomes.Deleted;
                    deleted++;
                    consecutiveFailures = 0;
                    break;
                case DeleteOutcome.AlreadyGone:
                    outcome = ExecutionOutcomes.AlreadyGone;
                    alreadyGone++;
                    consecutiveFailures = 0;
                    break;
                default:
                    outcome = ExecutionOutcomes.Failed;
                    failed++;
                    consecutiveFailures++;
                    break;
            }

            onRowCompleted(new ExecutionLogRow(
                ExecutedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                PlanFile: planFileName,
                SenderAddress: row.SenderAddress,
                MessageId: message.Id,
                Outcome: outcome,
                Error: result.Error ?? string.Empty));

            if (consecutiveFailures >= ConsecutiveFailureLimit)
            {
                aborted = true;
                break;
            }
        }

        return new ExecutionSummary(
            SenderAddress: row.SenderAddress,
            Resolved: messages.Count,
            Deleted: deleted,
            AlreadyGone: alreadyGone,
            Failed: failed,
            Aborted: aborted);
    }
}

public record ExecutionSummary(
    string SenderAddress,
    int Resolved,
    int Deleted,
    int AlreadyGone,
    int Failed,
    bool Aborted
);