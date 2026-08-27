using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace MailIntelligenceLab.Planning;

public static class MessageInspector
{
    public static async Task<IReadOnlyList<InspectedMessage>> InspectAsync(
        GraphServiceClient graphClient, string senderAddress)
    {
        string escapedAddress = senderAddress.Replace("'", "''");
        string filter = $"from/emailAddress/address eq '{escapedAddress}'";

        var messages = new List<InspectedMessage>();

        var firstPage = await graphClient.Me.MailFolders["inbox"].Messages
            .GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = filter;
                requestConfiguration.QueryParameters.Top = 100;
                requestConfiguration.QueryParameters.Select = new[]
                {
                    "subject", "receivedDateTime", "hasAttachments"
                };
            });

        var pageIterator = PageIterator<Message, MessageCollectionResponse>.CreatePageIterator(
            graphClient,
            firstPage!,
            message =>
            {
                messages.Add(new InspectedMessage(
                    message.ReceivedDateTime,
                    message.Subject ?? "(no subject)",
                    message.HasAttachments ?? false));
                return true;
            });

        await pageIterator.IterateAsync();

        return messages.OrderByDescending(m => m.ReceivedDateTime).ToList();
    }
}

public record InspectedMessage(DateTimeOffset? ReceivedDateTime, string Subject, bool HasAttachments);