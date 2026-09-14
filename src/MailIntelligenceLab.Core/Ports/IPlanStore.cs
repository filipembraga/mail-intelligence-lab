using MailIntelligenceLab.Models;
using MailIntelligenceLab.Planning;

namespace MailIntelligenceLab.Ports;

public interface IPlanStore
{
    string? FindNewestPath();

    // Null when the filename carries no parseable freeze bound — see the adapter.
    LoadedPlan? Load(string planFilePath);

    void Save(string planFilePath, IReadOnlyList<ActionPlanRow> rows);
}