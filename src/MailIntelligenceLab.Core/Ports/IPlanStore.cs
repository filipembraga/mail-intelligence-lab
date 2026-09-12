using MailIntelligenceLab.Planning;

namespace MailIntelligenceLab.Ports;

// Read-only for now: write-back is a separate decision with its own rules
// (preserve schema, preserve filename, never leave a partial file).
public interface IPlanStore
{
    string? FindNewestPath();

    // Null when the filename carries no parseable freeze bound — see the adapter.
    LoadedPlan? Load(string planFilePath);
}