using Microsoft.Extensions.Configuration;
using Azure.Identity;
using Microsoft.Graph;
using MailIntelligenceLab.Models;
using Microsoft.Graph.Models;

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string clientId = config["AzureAd:ClientId"]!;
string tenantId = config["AzureAd:TenantId"]!;

var credential = new DeviceCodeCredential(new DeviceCodeCredentialOptions
{
    TenantId = tenantId,
    ClientId = clientId,
    DeviceCodeCallback = (code, cancellationToken) =>
    {
        Console.WriteLine(code.Message);
        return Task.CompletedTask;
    }
});

var graphClient = new GraphServiceClient(credential, new[] { "User.Read", "Mail.Read" });

try
{
    var me = await graphClient.Me.GetAsync();
    Console.WriteLine($"Autenticado como: {me?.DisplayName} ({me?.Mail ?? me?.UserPrincipalName})");
}
catch (Exception ex)
{
    Console.WriteLine($"ERRO ao chamar o Graph: {ex.Message}");
    Console.WriteLine(ex);
}

int? maxMessages = config.GetValue<int?>("Discovery:MaxMessages");

Console.WriteLine();
Console.WriteLine("Lendo metadados da Inbox...");
if (maxMessages.HasValue)
{
    Console.WriteLine($"(modo de teste: parando após {maxMessages.Value} mensagens)");
}

try
{
    var emailList = new List<EmailMetadata>();

    var messagesResponse = await graphClient.Me.MailFolders["inbox"].Messages.GetAsync(requestConfiguration =>
    {
        requestConfiguration.QueryParameters.Select = new[]
        {
            "sender", "receivedDateTime", "hasAttachments", "parentFolderId", "body"
        };
        requestConfiguration.QueryParameters.Top = 50;
    });

    var pageIterator = PageIterator<Message, MessageCollectionResponse>.CreatePageIterator(
        graphClient,
        messagesResponse!,
        message =>
        {
            emailList.Add(new EmailMetadata(
                SenderAddress: message.Sender?.EmailAddress?.Address ?? "(desconhecido)",
                SenderName: message.Sender?.EmailAddress?.Name ?? "(desconhecido)",
                ReceivedDateTime: message.ReceivedDateTime,
                HasAttachments: message.HasAttachments ?? false,
                ParentFolderId: message.ParentFolderId ?? "",
                BodyLength: message.Body?.Content?.Length ?? 0
            ));

            if (emailList.Count % 100 == 0)
            {
                Console.WriteLine($"  {emailList.Count} mensagens lidas...");
            }

            return !maxMessages.HasValue || emailList.Count < maxMessages.Value;
        });

    await pageIterator.IterateAsync();

    Console.WriteLine();
    Console.WriteLine($"Total lido: {emailList.Count} mensagens.");
}
catch (Exception ex)
{
    Console.WriteLine($"ERRO ao ler mensagens: {ex.Message}");
    Console.WriteLine(ex);
}
