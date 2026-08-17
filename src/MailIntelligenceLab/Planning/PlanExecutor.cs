using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class PlanExecutor
{
    public const string OutcomeDeleted = "deleted";
    public const string OutcomeAlreadyGone = "already-gone";
    public const string OutcomeFailed = "failed";

    // Systemic failures (expired token, throttling) look like many individual
    // failures in a row. Independent failures are worth continuing through;
    // a repeated one means something changed since preview and isn't worth
    // pushing a lot more requests into.
    private const int ConsecutiveFailureLimit = 10;

    public static async Task<ExecutionSummary> ExecuteAsync(
        GraphServiceClient graphClient,
        ActionPlanRow row,
        DateTime freezeBoundUtc,
        string planFileName,
        Action<ExecutionLogRow> onRowCompleted)
    {
        string freezeBoundLiteral = freezeBoundUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");
        string escapedAddress = row.SenderAddress.Replace("'", "''");

        string filter =
            $"from/emailAddress/address eq '{escapedAddress}' " +
            $"and receivedDateTime le {freezeBoundLiteral}";

        // Collect IDs first, then delete. Paging while deleting from the same
        // collection shifts the pages underneath the iterator.
        var messageIds = new List<string>();

        var firstPage = await graphClient.Me.MailFolders["inbox"].Messages
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = filter;
                requestConfiguration.QueryParameters.Top = 100;
                requestConfiguration.QueryParameters.Select = new[] { "id" };
            });

        var pageIterator = PageIterator<Message, MessageCollectionResponse>.CreatePageIterator(
            graphClient,
            firstPage!,
            message =>
            {
                if (!string.IsNullOrEmpty(message.Id))
                {
                    messageIds.Add(message.Id);
                }
                return true;
            });

        await pageIterator.IterateAsync();

        int deleted = 0;
        int alreadyGone = 0;
        int failed = 0;
        int consecutiveFailures = 0;
        bool aborted = false;

        foreach (string messageId in messageIds)
        {
            string outcome;
            string error = string.Empty;

            try
            {
                await graphClient.Me.Messages[messageId].DeleteAsync();
                outcome = OutcomeDeleted;
                deleted++;
                consecutiveFailures = 0;
            }
            catch (ODataError odataError) when (odataError.ResponseStatusCode == 404)
            {
                // The desired state was already reached — not a failure.
                outcome = OutcomeAlreadyGone;
                alreadyGone++;
                consecutiveFailures = 0;
            }
            catch (Exception ex) when (ex is ODataError or ApiException)
            {
                outcome = OutcomeFailed;
                error = ex is ODataError odata
                    ? $"{odata.Error?.Code}: {odata.Error?.Message}"
                    : ex.Message;
                failed++;
                consecutiveFailures++;
            }

            onRowCompleted(new ExecutionLogRow(
                ExecutedAtUtc: DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                PlanFile: planFileName,
                SenderAddress: row.SenderAddress,
                MessageId: messageId,
                Outcome: outcome,
                Error: error));

            if (consecutiveFailures >= ConsecutiveFailureLimit)
            {
                aborted = true;
                break;
            }
        }

        return new ExecutionSummary(
            SenderAddress: row.SenderAddress,
            Resolved: messageIds.Count,
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