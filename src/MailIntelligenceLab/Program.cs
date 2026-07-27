using Microsoft.Extensions.Configuration;
using Azure.Identity;
using Microsoft.Graph;
using MailIntelligenceLab.Models;
using Microsoft.Graph.Models;
using System.Diagnostics;

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
    return;
}

int? maxMessages = config.GetValue<int?>("Discovery:MaxMessages");

Console.WriteLine();
Console.WriteLine("Lendo metadados da Inbox...");
if (maxMessages.HasValue)
{
    Console.WriteLine($"(modo de teste: parando após {maxMessages.Value} mensagens)");
}

var stopwatch = Stopwatch.StartNew();

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

    stopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine($"Total lido: {emailList.Count} mensagens.");
    Console.WriteLine($"Tempo total: {stopwatch.Elapsed:mm\\:ss}");

    var senderAggregates = emailList
    .GroupBy(email => email.SenderAddress)
    .Select(group => new SenderAggregate(
        SenderAddress: group.Key,
        SenderName: group.First().SenderName,
        MessageCount: group.Count(),
        TotalBodyLength: group.Sum(email => (long)email.BodyLength)
    ))
    .ToList();

    var topByCount = senderAggregates.OrderByDescending(s => s.MessageCount).Take(15).ToList();
    var topBySize = senderAggregates.OrderByDescending(s => s.TotalBodyLength).Take(15).ToList();

    Console.WriteLine();
    Console.WriteLine("Top 15 remetentes por quantidade de mensagens:");
    foreach (var sender in topByCount)
    {
        Console.WriteLine($"  {sender.SenderName} <{sender.SenderAddress}>: {sender.MessageCount} mensagens, {sender.TotalBodyLength:N0} caracteres (proxy)");
    }

    Console.WriteLine();
    Console.WriteLine("Top 15 remetentes por tamanho total (proxy de caracteres do body):");
    foreach (var sender in topBySize)
    {
        Console.WriteLine($"  {sender.SenderName} <{sender.SenderAddress}>: {sender.TotalBodyLength:N0} caracteres (proxy), {sender.MessageCount} mensagens");
    }

    var ageBuckets = emailList
        .GroupBy(email => GetAgeBucket(email.ReceivedDateTime))
        .ToDictionary(group => group.Key, group => group.Count());

    Console.WriteLine();
    Console.WriteLine("Distribuição por idade:");
    foreach (var bucket in Enum.GetValues<AgeBucket>())
    {
        int quantidade = ageBuckets.GetValueOrDefault(bucket, 0);
        Console.WriteLine($"  {AgeBucketDisplay.Labels[bucket]}: {quantidade} mensagens");
    }
}
catch (Exception ex)
{
    stopwatch.Stop();
    Console.WriteLine($"ERRO ao ler mensagens: {ex.Message}");
    Console.WriteLine(ex);
}

static AgeBucket GetAgeBucket(DateTimeOffset? receivedDateTime)
{
    if (!receivedDateTime.HasValue)
    {
        return AgeBucket.Unknown;
    }

    double diasDesdeRecebimento = (DateTimeOffset.UtcNow - receivedDateTime.Value).TotalDays;

    return diasDesdeRecebimento switch
    {
        <= 30 => AgeBucket.Days0To30,
        <= 90 => AgeBucket.Days31To90,
        <= 365 => AgeBucket.Days91To365,
        _ => AgeBucket.MoreThan365
    };
}
