using MailIntelligenceLab.Ports;

namespace MailIntelligenceLab.Planning;

public sealed class SenderLocator(IEmailProvider emailProvider)
{
    // The two Recoverable Items folders live in the mailbox's non-IPM subtree
    // and aren't visible in Outlook or OWA — this is the only way to see them.
    public static readonly string[] Folders =
    [
        "inbox",
        "deleteditems",
        "recoverableitemsdeletions",
        "recoverableitemspurges"
    ];

    public async Task<List<(string Folder, int Count, string? Error)>> LocateAsync(string senderAddress)
    {
        var results = new List<(string, int, string?)>();

        foreach (string folder in Folders)
        {
            try
            {
                int count = await emailProvider.CountFromSenderAsync(folder, senderAddress);
                results.Add((folder, count, null));
            }
            catch (Exception ex)
            {
                results.Add((folder, 0, ex.Message));
            }
        }

        return results;
    }
}