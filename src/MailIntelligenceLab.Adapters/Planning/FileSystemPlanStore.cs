using System.Globalization;
using CsvHelper;
using MailIntelligenceLab.Models;
using MailIntelligenceLab.Planning;
using MailIntelligenceLab.Ports;

namespace MailIntelligenceLab.Adapters.Planning;

public sealed class FileSystemPlanStore(string plansFolder) : IPlanStore
{
    public const string FileSuffix = "_action-plan.csv";
    private const string TimestampFormat = "yyyy-MM-dd_HHmm";

    public string? FindNewestPath() =>
        new DirectoryInfo(plansFolder)
            .GetFiles($"*{FileSuffix}")
            // Ordinal on a yyyy-MM-dd_HHmm prefix: lexicographic order is chronological.
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault()
            ?.FullName;

    // Returns null when the filename doesn't carry a parseable freeze bound.
    // Every verb that reads a plan must fail on that, not guess a bound —
    // the bound is what stops the executor from acting on mail that arrived
    // after the plan was approved.
    public LoadedPlan? Load(string planFilePath)
    {
        var planFile = new FileInfo(planFilePath);
        string timestampPart = planFile.Name[..^FileSuffix.Length];

        if (!DateTime.TryParseExact(
                timestampPart,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTime freezeBoundUtc))
        {
            return null;
        }

        using var reader = new StreamReader(planFile.FullName);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        var rows = csv.GetRecords<ActionPlanRow>().ToList();

        return new LoadedPlan(planFile.Name, planFile.FullName, freezeBoundUtc, rows);
    }

    public void Save(string planFilePath, IReadOnlyList<ActionPlanRow> rows)
    {
        var planFile = new FileInfo(planFilePath);

        // .tmp, not the plan suffix: FindNewestPath must never see a half-written file.
        string tempPath = Path.Combine(planFile.DirectoryName!, $".{planFile.Name}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var writer = new StreamWriter(tempPath))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(rows);
            }

            File.Move(tempPath, planFile.FullName, overwrite: true);
        }
        catch
        {
            // Swallowed deliberately: a cleanup failure must not replace the
            // exception that explains why the write failed.
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }
}