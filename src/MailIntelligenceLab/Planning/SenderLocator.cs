using Microsoft.Graph;

namespace MailIntelligenceLab.Planning;

public static class SenderLocator
{
    // Well-known folder names. The two Recoverable Items folders live in the
    // mailbox's non-IPM subtree and are not visible in Outlook or OWA as normal
    // folders — which is exactly why a tool that deletes mail needs to report them.
    public static readonly string[] Folders =
    [
        "inbox",
        "deleteditems",
        "recoverableitemsdeletions",
        "recoverableitemspurges"
    ];

    public static async Task<List<(string Folder, int Count, string? Error)>> LocateAsync(
        GraphServiceClient graphClient,
        string senderAddress)
    {
        string escapedAddress = senderAddress.Replace("'", "''");
        string filter = $"from/emailAddress/address eq '{escapedAddress}'";

        var results = new List<(string, int, string?)>();

        foreach (string folder in Folders)
        {
            try
            {
                var response = await graphClient.Me.MailFolders[folder].Messages
                    .GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Filter = filter;
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.QueryParameters.Top = 1;
                        requestConfiguration.QueryParameters.Select = new[] { "id" };
                    });

                results.Add((folder, (int)(response?.OdataCount ?? 0), null));
            }
            catch (Exception ex)
            {
                results.Add((folder, 0, ex.Message));
            }
        }

        return results;
    }
}