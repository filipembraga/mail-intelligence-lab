using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using MailIntelligenceLab.Models;
using MailIntelligenceLab.Ports;

namespace MailIntelligenceLab.Planning;

public sealed class GraphEmailProvider(GraphServiceClient graphClient) : IEmailProvider
{
    public async Task<(string? DisplayName, string? Mail)> GetCurrentUserAsync()
    {
        var me = await graphClient.Me.GetAsync();
        return (me?.DisplayName, me?.Mail ?? me?.UserPrincipalName);
    }

    public async Task<IReadOnlyList<EmailMetadata>> ReadInboxMetadataAsync(int? maxMessages = null)
    {
        var emailList = new List<EmailMetadata>();

        var response = await graphClient.Me.MailFolders["inbox"].Messages.GetAsync(config =>
        {
            config.QueryParameters.Select = new[]
            {
                "id", "from", "receivedDateTime", "hasAttachments", "parentFolderId", "body"
            };
            config.QueryParameters.Top = 50;
        });

        var pageIterator = PageIterator<Message, MessageCollectionResponse>.CreatePageIterator(
            graphClient,
            response!,
            message =>
            {
                emailList.Add(new EmailMetadata(
                    Id: message.Id ?? "",
                    SenderAddress: message.From?.EmailAddress?.Address ?? "(unknown)",
                    SenderName: message.From?.EmailAddress?.Name ?? "(unknown)",
                    ReceivedDateTime: message.ReceivedDateTime,
                    HasAttachments: message.HasAttachments ?? false,
                    ParentFolderId: message.ParentFolderId ?? "",
                    BodyLength: message.Body?.Content?.Length ?? 0,
                    BodyHasCidReference: message.Body?.Content?.Contains("cid:", StringComparison.OrdinalIgnoreCase) ?? false
                ));
                return !maxMessages.HasValue || emailList.Count < maxMessages.Value;
            });

        await pageIterator.IterateAsync();
        return emailList;
    }

    public async Task<(long Bytes, int FileCount)> GetAttachmentInfoAsync(string messageId)
    {
        var attachments = await graphClient.Me.Messages[messageId].Attachments.GetAsync(config =>
        {
            config.QueryParameters.Select = new[] { "size", "isInline", "name", "contentType" };
        });

        long bytes = attachments?.Value?.Sum(a => (long)(a.Size ?? 0)) ?? 0;
        int fileCount = attachments?.Value?.Count ?? 0;
        return (bytes, fileCount);
    }

    public async Task<int> CountFromSenderAsync(
        string folderId, string senderAddress, DateTime? receivedOnOrBeforeUtc = null)
    {
        string filter = BuildSenderFilter(senderAddress, receivedOnOrBeforeUtc);

        var response = await graphClient.Me.MailFolders[folderId].Messages.GetAsync(config =>
        {
            config.QueryParameters.Filter = filter;
            config.QueryParameters.Count = true;
            config.QueryParameters.Top = 1;
            config.QueryParameters.Select = new[] { "id" };
        });

        return (int)(response?.OdataCount ?? 0);
    }

    public async Task<IReadOnlyList<MessageSummary>> ListFromSenderAsync(
        string folderId, string senderAddress, DateTime? receivedOnOrBeforeUtc = null)
    {
        string filter = BuildSenderFilter(senderAddress, receivedOnOrBeforeUtc);
        var summaries = new List<MessageSummary>();

        // No $orderby: combined with this $filter, Graph throws InefficientFilter
        // unless every $orderby property also appears first in $filter. Sort client-side.
        var response = await graphClient.Me.MailFolders[folderId].Messages.GetAsync(config =>
        {
            config.QueryParameters.Filter = filter;
            config.QueryParameters.Top = 100;
            config.QueryParameters.Select = new[] { "id", "subject", "receivedDateTime", "hasAttachments" };
        });

        var pageIterator = PageIterator<Message, MessageCollectionResponse>.CreatePageIterator(
            graphClient,
            response!,
            message =>
            {
                if (!string.IsNullOrEmpty(message.Id))
                {
                    summaries.Add(new MessageSummary(
                        message.Id, message.Subject, message.ReceivedDateTime, message.HasAttachments ?? false));
                }
                return true;
            });

        await pageIterator.IterateAsync();
        return summaries;
    }

    public Task<DeleteResult> DeleteMessageAsync(string messageId) =>
    ExecuteDeleteAsync(() => graphClient.Me.Messages[messageId].DeleteAsync());

    public Task<DeleteResult> PermanentDeleteMessageAsync(string messageId) =>
        ExecuteDeleteAsync(() => graphClient.Me.Messages[messageId].PermanentDelete.PostAsync());

    // 404 means the desired state was already reached, not a failure — same
    // distinction PlanExecutor made before this logic moved here.
    private static async Task<DeleteResult> ExecuteDeleteAsync(Func<Task> deleteCall)
    {
        try
        {
            await deleteCall();
            return new DeleteResult(DeleteOutcome.Deleted);
        }
        catch (ODataError odataError) when (odataError.ResponseStatusCode == 404)
        {
            return new DeleteResult(DeleteOutcome.AlreadyGone);
        }
        catch (Exception ex) when (ex is ODataError or ApiException)
        {
            string error = ex is ODataError odata
                ? $"{odata.Error?.Code}: {odata.Error?.Message}"
                : ex.Message;
            return new DeleteResult(DeleteOutcome.Failed, error);
        }
    }

    private static string BuildSenderFilter(string senderAddress, DateTime? receivedOnOrBeforeUtc)
    {
        string escapedAddress = senderAddress.Replace("'", "''");
        string filter = $"from/emailAddress/address eq '{escapedAddress}'";

        if (receivedOnOrBeforeUtc.HasValue)
        {
            filter += $" and receivedDateTime le {receivedOnOrBeforeUtc.Value:yyyy-MM-ddTHH:mm:ssZ}";
        }

        return filter;
    }
}