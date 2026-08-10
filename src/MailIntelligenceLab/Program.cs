using Microsoft.Extensions.Configuration;
using Azure.Identity;
using Microsoft.Graph;
using MailIntelligenceLab.Models;
using Microsoft.Graph.Models;
using System.Diagnostics;
using System.Globalization;
using CsvHelper;
using Azure.Core;
using System.Collections.Concurrent;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string clientId = config["AzureAd:ClientId"]!;
string tenantId = config["AzureAd:TenantId"]!;

string tokenCacheFolder = config["TokenCache:FolderPath"]!
    .Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
Directory.CreateDirectory(tokenCacheFolder);
string authRecordPath = Path.Combine(tokenCacheFolder, "authrecord.bin");

var tokenCacheOptions = new TokenCachePersistenceOptions
{
    Name = config["TokenCache:CacheName"]
};

AuthenticationRecord? authRecord = null;
if (File.Exists(authRecordPath))
{
    using var readStream = new FileStream(authRecordPath, FileMode.Open, FileAccess.Read);
    authRecord = await AuthenticationRecord.DeserializeAsync(readStream);
}

var credentialOptions = new DeviceCodeCredentialOptions
{
    TenantId = tenantId,
    ClientId = clientId,
    TokenCachePersistenceOptions = tokenCacheOptions,
    AuthenticationRecord = authRecord,
    DeviceCodeCallback = (code, cancellationToken) =>
    {
        Console.WriteLine(code.Message);
        return Task.CompletedTask;
    }
};

var credential = new DeviceCodeCredential(credentialOptions);

if (authRecord is null)
{
    var graphScope = new TokenRequestContext(new[] { "User.Read", "Mail.Read" });
    var newRecord = await credential.AuthenticateAsync(graphScope);
    using var writeStream = new FileStream(authRecordPath, FileMode.Create, FileAccess.Write);
    await newRecord.SerializeAsync(writeStream);
}

var graphClient = new GraphServiceClient(credential, new[] { "User.Read", "Mail.Read" });

try
{
    var me = await graphClient.Me.GetAsync();
    Console.WriteLine($"Authenticated as: {me?.DisplayName} ({me?.Mail ?? me?.UserPrincipalName})");
}
catch (Exception ex)
{
    Console.WriteLine($"ERROR calling Graph: {ex.Message}");
    Console.WriteLine(ex);
    return;
}

int? maxMessages = config.GetValue<int?>("Discovery:MaxMessages");

Console.WriteLine();
Console.WriteLine("Reading Inbox metadata...");
if (maxMessages.HasValue)
{
    Console.WriteLine($"(test mode: stopping after {maxMessages.Value} messages)");
}

var stopwatch = Stopwatch.StartNew();

try
{
    var emailList = new List<EmailMetadata>();

    var messagesResponse = await graphClient.Me.MailFolders["inbox"].Messages.GetAsync(requestConfiguration =>
    {
        requestConfiguration.QueryParameters.Select = new[]
        {
            "id", "from", "receivedDateTime", "hasAttachments", "parentFolderId", "body"
        };
        requestConfiguration.QueryParameters.Top = 50;
    });

    var pageIterator = PageIterator<Message, MessageCollectionResponse>.CreatePageIterator(
        graphClient,
        messagesResponse!,
        message =>
        {
            // Graph exposes both From (message author) and Sender (transmitting mailbox).
            // They diverge on delegated/list sends. We key on From — it's what Outlook
            // displays and what "this sender" means to a human deciding what to delete.
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

    stopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine($"Total read: {emailList.Count} messages.");
    Console.WriteLine($"Elapsed: {stopwatch.Elapsed:mm\\:ss}");

    Console.WriteLine();
    Console.WriteLine("Calculating real attachment sizes...");

    var messagesWithAttachments = emailList.Where(e => e.HasAttachments).ToList();
    var inlineCandidateMessages = emailList.Where(e => !e.HasAttachments && e.BodyHasCidReference).ToList();
    var messagesToFetch = messagesWithAttachments.Concat(inlineCandidateMessages).ToList();

    Console.WriteLine($"Messages flagged with attachment (hasAttachments): {messagesWithAttachments.Count}");
    Console.WriteLine($"cid: candidates in body (hasAttachments=false): {inlineCandidateMessages.Count}");
    Console.WriteLine($"Total to check: {messagesToFetch.Count}");

    var attachmentSizes = new ConcurrentDictionary<string, long>();
    var attachmentFileCounts = new ConcurrentDictionary<string, int>();

    using var throttle = new SemaphoreSlim(4);
    int requestCount = 0;
    int failureCount = 0;
    int processedCount = 0;

    var attachmentStopwatch = Stopwatch.StartNew();

    var fetchTasks = messagesToFetch.Select(async email =>
    {
        await throttle.WaitAsync();
        try
        {
            Interlocked.Increment(ref requestCount);

            var attachments = await graphClient.Me.Messages[email.Id].Attachments
                .GetAsync(requestConfiguration =>
                {
                    requestConfiguration.QueryParameters.Select = new[]
                    {
                        "size", "isInline", "name", "contentType"
                    };
                });

            attachmentSizes[email.Id] = attachments?.Value?.Sum(a => (long)(a.Size ?? 0)) ?? 0;
            attachmentFileCounts[email.Id] = attachments?.Value?.Count ?? 0;
        }
        catch (Exception)
        {
            Interlocked.Increment(ref failureCount);
            attachmentSizes[email.Id] = 0;
            attachmentFileCounts[email.Id] = 0;
        }
        finally
        {
            throttle.Release();

            int done = Interlocked.Increment(ref processedCount);
            if (done % 250 == 0)
            {
                Console.WriteLine($"  ... {done}/{messagesToFetch.Count} messages processed");
            }
        }
    });

    await Task.WhenAll(fetchTasks);
    attachmentStopwatch.Stop();

    long totalAttachmentBytes = attachmentSizes.Values.Sum();

    Console.WriteLine();
    Console.WriteLine($"Attachment requests: {requestCount} (failures: {failureCount})");
    Console.WriteLine($"Attachment phase elapsed: {attachmentStopwatch.Elapsed:mm\\:ss}");
    Console.WriteLine($"Average throughput: {requestCount / Math.Max(attachmentStopwatch.Elapsed.TotalSeconds, 1):F1} req/s");
    Console.WriteLine($"Total attachment size: {totalAttachmentBytes / 1024.0 / 1024.0:N1} MB");

    int candidatesWithRealAttachment = inlineCandidateMessages.Count(e => attachmentFileCounts.GetValueOrDefault(e.Id, 0) > 0);
    long candidatesTotalBytes = inlineCandidateMessages.Sum(e => attachmentSizes.GetValueOrDefault(e.Id, 0));

    Console.WriteLine();
    Console.WriteLine($"[diagnostic] cid: candidates that actually returned an attachment: {candidatesWithRealAttachment}/{inlineCandidateMessages.Count}");
    Console.WriteLine($"[diagnostic] Size recovered from candidates only: {candidatesTotalBytes / 1024.0 / 1024.0:N1} MB");

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
    Console.WriteLine("Top 15 senders by message count:");
    foreach (var sender in topByCount)
    {
        Console.WriteLine($"  {sender.SenderName} <{sender.SenderAddress}>: {sender.MessageCount} messages, {sender.TotalBodyLength:N0} characters (proxy)");
    }

    Console.WriteLine();
    Console.WriteLine("Top 15 senders by total size (body character proxy):");
    foreach (var sender in topBySize)
    {
        Console.WriteLine($"  {sender.SenderName} <{sender.SenderAddress}>: {sender.TotalBodyLength:N0} characters (proxy), {sender.MessageCount} messages");
    }

    var topByAttachmentSize = emailList
        .GroupBy(e => e.SenderAddress)
        .Select(g => new
        {
            Name = g.First().SenderName,
            Address = g.Key,
            Bytes = g.Sum(e => attachmentSizes.GetValueOrDefault(e.Id, 0))
        })
        .Where(x => x.Bytes > 0)
        .OrderByDescending(x => x.Bytes)
        .Take(15)
        .ToList();

    Console.WriteLine();
    Console.WriteLine("Top 15 senders by REAL attachment size:");
    foreach (var sender in topByAttachmentSize)
    {
        Console.WriteLine($"  {sender.Name} <{sender.Address}>: {sender.Bytes / 1024.0 / 1024.0:N1} MB");
    }

    var ageBuckets = emailList
        .GroupBy(email => GetAgeBucket(email.ReceivedDateTime))
        .ToDictionary(group => group.Key, group => group.Count());

    Console.WriteLine();
    Console.WriteLine("Age distribution:");
    foreach (var bucket in Enum.GetValues<AgeBucket>())
    {
        int count = ageBuckets.GetValueOrDefault(bucket, 0);
        Console.WriteLine($"  {AgeBucketDisplay.Labels[bucket]}: {count} messages");
    }

    var senderReportRows = emailList
        .GroupBy(email => email.SenderAddress)
        .Select(group =>
        {
            var oldestDate = group.Min(e => e.ReceivedDateTime ?? DateTimeOffset.UtcNow);
            var newestDate = group.Max(e => e.ReceivedDateTime ?? DateTimeOffset.UtcNow);
            double averageAgeDays = group.Average(e =>
                (DateTimeOffset.UtcNow - (e.ReceivedDateTime ?? DateTimeOffset.UtcNow)).TotalDays);

            return new SenderReportRow(
                SenderAddress: group.Key,
                SenderName: group.First().SenderName,
                MessageCount: group.Count(),
                MessagesWithAttachmentsCount: group.Count(e => e.HasAttachments),
                AttachmentFileCount: group.Sum(e => attachmentFileCounts.GetValueOrDefault(e.Id, 0)),
                TotalAttachmentSizeMB: Math.Round(group.Sum(e => attachmentSizes.GetValueOrDefault(e.Id, 0)) / 1024.0 / 1024.0, 2),
                TotalAttachmentSizeBytes: group.Sum(e => attachmentSizes.GetValueOrDefault(e.Id, 0)),
                TotalBodyLengthProxy: group.Sum(e => (long)e.BodyLength),
                AverageAgeDays: Math.Round(averageAgeDays, 1),
                AverageAgeYears: Math.Round(averageAgeDays / 365.25, 2),
                OldestReceivedDate: oldestDate.ToString("yyyy-MM-dd"),
                NewestReceivedDate: newestDate.ToString("yyyy-MM-dd")
            );
        })
        .OrderByDescending(r => r.MessageCount)
        .ToList();

    string rawFolderRelative = config["Reports:RawFolder"]!;
    string rawFolder = Path.GetFullPath(rawFolderRelative);
    Directory.CreateDirectory(rawFolder);

    string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
    string filePath = Path.Combine(rawFolder, $"{timestamp}_senders-report.csv");

    var csvConfig = new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture);

    using (var writer = new StreamWriter(filePath))
    using (var csv = new CsvWriter(writer, csvConfig))
    {
        csv.WriteRecords(senderReportRows);
    }

    Console.WriteLine();
    Console.WriteLine($"CSV report saved to: {filePath}");
}
catch (Exception ex)
{
    stopwatch.Stop();
    Console.WriteLine($"ERROR reading messages: {ex.Message}");
    Console.WriteLine(ex);
}

static AgeBucket GetAgeBucket(DateTimeOffset? receivedDateTime)
{
    if (!receivedDateTime.HasValue)
    {
        return AgeBucket.Unknown;
    }

    double daysSinceReceived = (DateTimeOffset.UtcNow - receivedDateTime.Value).TotalDays;

    return daysSinceReceived switch
    {
        <= 30 => AgeBucket.Days0To30,
        <= 90 => AgeBucket.Days31To90,
        <= 365 => AgeBucket.Days91To365,
        _ => AgeBucket.MoreThan365
    };
}
