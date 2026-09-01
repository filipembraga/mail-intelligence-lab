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
using MailIntelligenceLab.Planning;
using MailIntelligenceLab.Ports;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

if (args.Length > 0 && args[0].Equals("plan", StringComparison.OrdinalIgnoreCase))
{
    string reportsFolder = Path.GetFullPath(config["Reports:RawFolder"]!);
    string plansFolder = Path.GetFullPath(config["Plans:RawFolder"]!);

    var latestReport = new DirectoryInfo(reportsFolder)
        .GetFiles("*_senders-report.csv")
        .OrderByDescending(file => file.Name, StringComparer.Ordinal)
        .FirstOrDefault();

    if (latestReport is null)
    {
        Console.WriteLine($"No sender report found in: {reportsFolder}");
        return;
    }

    Console.WriteLine($"Source report: {latestReport.Name}");

    List<SenderReportRow> reportRows;
    using (var reportReader = new StreamReader(latestReport.FullName))
    using (var reportCsv = new CsvReader(reportReader, CultureInfo.InvariantCulture))
    {
        reportRows = reportCsv.GetRecords<SenderReportRow>().ToList();
    }

    string logsFolderForPlan = Path.GetFullPath(config["ExecutionLogs:RawFolder"]!);
    var executionLogRows = new List<ExecutionLogRow>();

    if (Directory.Exists(logsFolderForPlan))
    {
        foreach (var logFile in new DirectoryInfo(logsFolderForPlan).GetFiles("*_execution-log.csv"))
        {
            using var logReader = new StreamReader(logFile.FullName);
            using var logCsvReader = new CsvReader(logReader, CultureInfo.InvariantCulture);
            executionLogRows.AddRange(logCsvReader.GetRecords<ExecutionLogRow>().ToList());
        }
    }

    var alreadyRemovedBySender = ExecutionLogAggregator.CountRemovedPerSender(executionLogRows);

    var generation = ActionPlanGenerator.Generate(reportRows, alreadyRemovedBySender);

    Directory.CreateDirectory(plansFolder);

    // UTC, not local time: this timestamp is the plan's freeze bound, used as
    // a receivedDateTime upper limit when the plan is executed.
    string planTimestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmm");
    string planPath = Path.Combine(plansFolder, $"{planTimestamp}{ActionPlanLoader.FileSuffix}");

    using (var planWriter = new StreamWriter(planPath))
    using (var planCsv = new CsvWriter(planWriter, CultureInfo.InvariantCulture))
    {
        planCsv.WriteRecords(generation.Rows);
    }

    Console.WriteLine($"Senders in report: {reportRows.Count}");
    Console.WriteLine($"Execution log rows read: {executionLogRows.Count} (from {logsFolderForPlan})");
    Console.WriteLine($"Senders in plan: {generation.Rows.Count} (merged by case: {generation.MergedByCase}, excluded as unresolvable: {generation.ExcludedAsUnresolvable}, fully removed by prior rounds: {generation.ExcludedAsFullyRemoved})");
    Console.WriteLine($"Action plan saved to: {planPath}");
    Console.WriteLine("Edit the Action column ('delete' to act, blank to keep, 'permanent-delete' to act without recovery), then run the executor.");
    return;
}

if (args.Length > 0 && args[0].Equals("validate", StringComparison.OrdinalIgnoreCase))
{
    string plansFolder = Path.GetFullPath(config["Plans:RawFolder"]!);

    var latestPlanFile = ActionPlanLoader.FindNewest(plansFolder);
    if (latestPlanFile is null)
    {
        Console.WriteLine($"No action plan found in: {plansFolder}");
        return;
    }

    var plan = ActionPlanLoader.Load(latestPlanFile);
    if (plan is null)
    {
        Console.WriteLine($"FAILED: cannot read freeze bound from filename '{latestPlanFile.Name}'.");
        return;
    }

    Console.WriteLine($"Plan file: {plan.FileName}");
    Console.WriteLine($"Freeze bound (UTC): {plan.FreezeBoundUtc:yyyy-MM-ddTHH:mm:ssZ}");

    var validation = ActionPlanValidator.Validate(plan.Rows);

    Console.WriteLine($"Rows: {validation.TotalRows}");
    Console.WriteLine($"Marked for deletion: {validation.RowsMarkedForDeletion} (permanent: {validation.RowsMarkedForPermanentDeletion})");
    Console.WriteLine($"Messages targeted (per plan): {validation.MessagesTargeted}");
    Console.WriteLine($"Attachment weight targeted: {validation.BytesTargeted / 1024.0 / 1024.0:N1} MB");

    if (!validation.IsValid)
    {
        Console.WriteLine();
        Console.WriteLine($"FAILED with {validation.Errors.Count} error(s):");
        foreach (string error in validation.Errors)
        {
            Console.WriteLine($"  - {error}");
        }
        return;
    }

    Console.WriteLine();
    Console.WriteLine(validation.RowsMarkedForDeletion == 0
        ? "Plan is valid, but no row is marked for deletion — nothing to execute."
        : "Plan is valid.");
    return;
}

string[] knownVerbs = ["plan", "validate", "preview", "execute", "verify", "inspect"];

if (args.Length > 0 && !knownVerbs.Contains(args[0], StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"Unknown argument: {args[0]}");
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run              discovery — full mailbox read");
    Console.WriteLine("  dotnet run -- plan      generate action plan from newest report");
    Console.WriteLine("  dotnet run -- validate  check the newest edited plan");
    Console.WriteLine("  dotnet run -- preview   resolve the newest plan against Graph (read-only)");
    Console.WriteLine("  dotnet run -- execute <plan-file>  delete messages marked in that plan");
    Console.WriteLine("  dotnet run -- verify <address>     count a sender's messages across mail folders");
    Console.WriteLine("  dotnet run -- inspect <address> [--all]   list a sender's messages in the inbox");
    return;
}

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
    var graphScope = new TokenRequestContext(new[] { "User.Read", "Mail.ReadWrite" });
    var newRecord = await credential.AuthenticateAsync(graphScope);
    using var writeStream = new FileStream(authRecordPath, FileMode.Create, FileAccess.Write);
    await newRecord.SerializeAsync(writeStream);
}

var graphClient = new GraphServiceClient(credential, new[] { "User.Read", "Mail.ReadWrite" });

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

IEmailProvider emailProvider = new GraphEmailProvider(graphClient);
var planResolver = new PlanResolver(emailProvider);
var senderLocator = new SenderLocator(emailProvider);
var messageInspector = new MessageInspector(emailProvider);
var planExecutor = new PlanExecutor(emailProvider);

if (args.Length > 0 && args[0].Equals("preview", StringComparison.OrdinalIgnoreCase))
{
    string plansFolder = Path.GetFullPath(config["Plans:RawFolder"]!);

    var latestPlanFile = ActionPlanLoader.FindNewest(plansFolder);
    if (latestPlanFile is null)
    {
        Console.WriteLine($"No action plan found in: {plansFolder}");
        return;
    }

    var plan = ActionPlanLoader.Load(latestPlanFile);
    if (plan is null)
    {
        Console.WriteLine($"FAILED: cannot read freeze bound from filename '{latestPlanFile.Name}'.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"Plan file: {plan.FileName}");
    Console.WriteLine($"Freeze bound (UTC): {plan.FreezeBoundUtc:yyyy-MM-ddTHH:mm:ssZ}");

    var validation = ActionPlanValidator.Validate(plan.Rows);
    if (!validation.IsValid)
    {
        Console.WriteLine();
        Console.WriteLine($"FAILED with {validation.Errors.Count} error(s):");
        foreach (string error in validation.Errors)
        {
            Console.WriteLine($"  - {error}");
        }
        return;
    }

    var markedRows = plan.Rows
        .Where(row => ActionPlanGenerator.IsActionable(row.Action))
        .ToList();

    if (markedRows.Count == 0)
    {
        Console.WriteLine("No row is marked for deletion — nothing to preview.");
        return;
    }

    Console.WriteLine($"Resolving {markedRows.Count} sender(s) against Graph...");
    Console.WriteLine();

    var previewStopwatch = Stopwatch.StartNew();
    var resolutions = await planResolver.ResolveAsync(markedRows, plan.FreezeBoundUtc);
    previewStopwatch.Stop();

    foreach (var resolution in resolutions)
    {
        if (resolution.Error is not null)
        {
            Console.WriteLine($"  ERROR  {resolution.SenderAddress}: {resolution.Error}");
            continue;
        }

        string drift = resolution.Drift == 0 ? "" : $"  (drift: {resolution.Drift:+#;-#;0})";
        Console.WriteLine($"  {resolution.SenderAddress}: plan {resolution.PlannedMessageCount}, resolved {resolution.ResolvedMessageCount}{drift}");
    }

    int failedCount = resolutions.Count(r => r.Error is not null);
    int totalResolved = resolutions.Where(r => r.Error is null).Sum(r => r.ResolvedMessageCount);

    Console.WriteLine();
    Console.WriteLine($"Elapsed: {previewStopwatch.Elapsed.TotalSeconds:N1}s");
    Console.WriteLine($"Messages the plan would delete: {totalResolved} (plan claimed: {validation.MessagesTargeted})");

    if (failedCount > 0)
    {
        Console.WriteLine($"{failedCount} sender(s) failed to resolve — not safe to execute.");
        return;
    }

    Console.WriteLine("Preview only. No message has been modified or deleted.");
    return;
}

if (args.Length > 0 && args[0].Equals("verify", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: dotnet run -- verify <sender-address>");
        return;
    }

    string senderAddress = args[1];

    Console.WriteLine();
    Console.WriteLine($"Locating messages from: {senderAddress}");
    Console.WriteLine();

    var locations = await senderLocator.LocateAsync(senderAddress);

    foreach (var (folder, count, error) in locations)
    {
        Console.WriteLine(error is null
            ? $"  {folder,-28} {count}"
            : $"  {folder,-28} ERROR — {error}");
    }

    return;
}

if (args.Length > 0 && args[0].Equals("inspect", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: dotnet run -- inspect <sender-address> [--all]");
        return;
    }

    string senderAddress = args[1];
    bool showAll = args.Contains("--all", StringComparer.OrdinalIgnoreCase);

    var messages = await messageInspector.InspectAsync(senderAddress);

    var toShow = showAll ? messages : messages.Take(15).ToList();

    Console.WriteLine();
    Console.WriteLine($"{senderAddress}: {messages.Count} message(s) in the inbox");
    if (!showAll && messages.Count > 15)
    {
        Console.WriteLine($"Showing the 15 most recent. Use --all to see all {messages.Count}.");
    }
    Console.WriteLine();

    foreach (var message in toShow)
    {
        string date = message.ReceivedDateTime?.ToString("yyyy-MM-dd") ?? "(unknown date)";
        string attachment = message.HasAttachments ? "[attachment]" : "";
        Console.WriteLine($"  {date}  {message.Subject ?? "(no subject)"}  {attachment}");
    }

    return;
}

if (args.Length > 0 && args[0].Equals("execute", StringComparison.OrdinalIgnoreCase))
{
    // Explicit path, not newest-by-default: this is the one verb where acting on
    // a file you forgot you regenerated is unrecoverable.
    if (args.Length < 2)
    {
        Console.WriteLine("Usage: dotnet run -- execute <path-to-plan-file>");
        return;
    }

    var planFile = new FileInfo(Path.GetFullPath(args[1]));
    if (!planFile.Exists)
    {
        Console.WriteLine($"Plan file not found: {planFile.FullName}");
        return;
    }

    var plan = ActionPlanLoader.Load(planFile);
    if (plan is null)
    {
        Console.WriteLine($"FAILED: cannot read freeze bound from filename '{planFile.Name}'.");
        return;
    }

    var validation = ActionPlanValidator.Validate(plan.Rows);
    if (!validation.IsValid)
    {
        Console.WriteLine($"FAILED with {validation.Errors.Count} error(s) — fix the plan before executing.");
        foreach (string error in validation.Errors)
        {
            Console.WriteLine($"  - {error}");
        }
        return;
    }

    var markedRows = plan.Rows
        .Where(row => ActionPlanGenerator.IsActionable(row.Action))
        .ToList();

    if (markedRows.Count == 0)
    {
        Console.WriteLine("No row is marked for deletion — nothing to execute.");
        return;
    }

    Console.WriteLine();
    Console.WriteLine($"Plan file: {plan.FileName}");
    Console.WriteLine($"Freeze bound (UTC): {plan.FreezeBoundUtc:yyyy-MM-ddTHH:mm:ssZ}");
    Console.WriteLine($"Senders marked for deletion: {markedRows.Count}");

    foreach (var row in markedRows)
    {
        Console.WriteLine($"  [{(row.Action ?? string.Empty).Trim().ToLowerInvariant()}] {row.SenderAddress} (plan: {row.MessageCount} messages)");
    }

    Console.WriteLine();

    string expected;
    if (validation.RowsMarkedForPermanentDeletion > 0)
    {
        expected = "PURGE";
        Console.WriteLine($"{validation.RowsMarkedForPermanentDeletion} sender(s) marked 'permanent-delete':");
        Console.WriteLine("  messages go to the Purges folder — NOT recoverable from Outlook.");
        Console.WriteLine("Type PURGE to proceed, anything else to abort:");
    }
    else
    {
        expected = "DELETE";
        Console.WriteLine("Messages are soft-deleted to Recoverable Items.");
        Console.WriteLine("Recoverable via Outlook: Deleted Items > 'Recover items deleted from this folder'.");
        Console.WriteLine("Type DELETE to proceed, anything else to abort:");
    }
    Console.Write("> ");

    string? confirmation = Console.ReadLine();
    if (confirmation?.Trim() != expected)
    {
        Console.WriteLine("Aborted. Nothing was deleted.");
        return;
    }

    string logsFolder = Path.GetFullPath(config["ExecutionLogs:RawFolder"]!);
    Directory.CreateDirectory(logsFolder);
    string logPath = Path.Combine(
        logsFolder,
        $"{DateTime.UtcNow:yyyy-MM-dd_HHmm}_execution-log.csv");

    Console.WriteLine();
    Console.WriteLine($"Execution log: {logPath}");
    Console.WriteLine();

    var executionStopwatch = Stopwatch.StartNew();

    // Opened for the whole run and flushed per row: a killed process still
    // leaves an accurate record of what was actually deleted.
    using (var logWriter = new StreamWriter(logPath, append: true))
    using (var logCsv = new CsvWriter(logWriter, CultureInfo.InvariantCulture))
    {
        logCsv.WriteHeader<ExecutionLogRow>();
        logCsv.NextRecord();
        logCsv.Flush();

        foreach (var row in markedRows)
        {
            Console.WriteLine($"{row.SenderAddress}...");

            var summary = await planExecutor.ExecuteAsync(
                row,
                plan.FreezeBoundUtc,
                plan.FileName,
                logRow =>
                {
                    logCsv.WriteRecord(logRow);
                    logCsv.NextRecord();
                    logCsv.Flush();
                });

            Console.WriteLine(
                $"  resolved {summary.Resolved}, deleted {summary.Deleted}, " +
                $"already gone {summary.AlreadyGone}, failed {summary.Failed}");

            if (summary.Aborted)
            {
                Console.WriteLine();
                Console.WriteLine("ABORTED: too many consecutive failures. See the execution log.");
                return;
            }
        }
    }

    executionStopwatch.Stop();

    Console.WriteLine();
    Console.WriteLine($"Elapsed: {executionStopwatch.Elapsed:mm\\:ss}");
    Console.WriteLine(validation.RowsMarkedForPermanentDeletion > 0
        ? "Done. Purged messages are in Recoverable Items > Purges — not reachable from Outlook."
        : "Done. Soft-deleted messages are recoverable via Outlook: Deleted Items > 'Recover items deleted from this folder'.");
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