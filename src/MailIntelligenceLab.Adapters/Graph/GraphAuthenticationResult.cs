namespace MailIntelligenceLab.Adapters.Graph;

// Returned rather than thrown so driving adapters react to the auth state
// without referencing Azure.Identity to name its exception types.
public enum GraphAuthenticationOutcome
{
    Authenticated,
    AuthenticationRequired,
    Failed
}

public sealed record GraphAuthenticationResult(
    GraphAuthenticationOutcome Outcome,
    Microsoft.Graph.GraphServiceClient? Client = null,
    string? Error = null);