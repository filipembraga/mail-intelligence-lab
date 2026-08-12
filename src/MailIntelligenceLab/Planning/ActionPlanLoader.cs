using System.Globalization;
using CsvHelper;
using MailIntelligenceLab.Models;

namespace MailIntelligenceLab.Planning;

public static class ActionPlanLoader
{
    public const string FileSuffix = "_action-plan.csv";
    private const string TimestampFormat = "yyyy-MM-dd_HHmm";

    public static FileInfo? FindNewest(string plansFolder) =>
        new DirectoryInfo(plansFolder)
            .GetFiles($"*{FileSuffix}")
            // Ordinal on a yyyy-MM-dd_HHmm prefix: lexicographic order is chronological.
            .OrderByDescending(file => file.Name, StringComparer.Ordinal)
            .FirstOrDefault();

    // Returns null when the filename doesn't carry a parseable freeze bound.
    // Every verb that reads a plan must fail on that, not guess a bound —
    // the bound is what stops the executor from acting on mail that arrived
    // after the plan was approved.
    public static LoadedPlan? Load(FileInfo planFile)
    {
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
}