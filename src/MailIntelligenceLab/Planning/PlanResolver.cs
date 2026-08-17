using Microsoft.Graph;
using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class PlanResolver
{
    // Counts the messages a plan row will actually act on, without fetching them.
    // Sequential by design: a plan marks a handful of senders, so one request each
    // is fast enough that bounded parallelism would be optimising an unmeasured cost.
    public static async Task<List<SenderResolution>> ResolveAsync(
        GraphServiceClient graphClient,
        IEnumerable<ActionPlanRow> markedRows,
        DateTime freezeBoundUtc)
    {
        var resolutions = new List<SenderResolution>();

        // Graph expects an unquoted ISO 8601 UTC literal for datetime comparisons.
        string freezeBoundLiteral = freezeBoundUtc.ToString("yyyy-MM-ddTHH:mm:ssZ");

        foreach (var row in markedRows)
        {
            // Single quotes are the OData string delimiter; a literal quote inside
            // a value is escaped by doubling it. Email addresses shouldn't contain
            // one, but the plan file is hand-edited — don't build a filter on trust.
            string escapedAddress = row.SenderAddress.Replace("'", "''");

            string filter =
                $"from/emailAddress/address eq '{escapedAddress}' " +
                $"and receivedDateTime le {freezeBoundLiteral}";

            try
            {
                var response = await graphClient.Me.MailFolders["inbox"].Messages
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = filter;
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.QueryParameters.Top = 1;
                        requestConfiguration.QueryParameters.Select = new[] { "id" };
                    });

                resolutions.Add(new SenderResolution(
                    SenderAddress: row.SenderAddress,
                    PlannedMessageCount: row.MessageCount,
                    ResolvedMessageCount: (int)(response?.OdataCount ?? 0),
                    Error: null));
            }
            catch (Exception ex)
            {
                resolutions.Add(new SenderResolution(
                    SenderAddress: row.SenderAddress,
                    PlannedMessageCount: row.MessageCount,
                    ResolvedMessageCount: 0,
                    Error: ex.Message));
            }
        }

        return resolutions;
    }
}