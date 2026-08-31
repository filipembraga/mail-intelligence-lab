namespace MailIntelligenceLab.Planning;

// One row per message acted on. Append-only: this file is the record of what
// actually happened, which is not the same thing as what the plan intended.
public record ExecutionLogRow(
    string ExecutedAtUtc,
    string PlanFile,
    string SenderAddress,
    string MessageId,
    string Outcome,   // deleted | already-gone | failed
    string Error
);