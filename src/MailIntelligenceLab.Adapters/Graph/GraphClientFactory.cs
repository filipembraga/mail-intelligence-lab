using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;

namespace MailIntelligenceLab.Adapters.Graph;

public sealed record GraphClientOptions(
    string ClientId,
    string TenantId,
    string TokenCacheFolder,
    string TokenCacheName,
    bool AllowInteractiveAuthentication);

public static class GraphClientFactory
{
    // One place: three bugs in this project came from this array being updated
    // in one location and not the other — see ADR-002.
    private static readonly string[] Scopes = ["User.Read", "Mail.ReadWrite"];

    private const string AuthRecordFileName = "authrecord.bin";

    public static async Task<GraphAuthenticationResult> CreateAsync(
        GraphClientOptions options,
        Action<string>? deviceCodeMessageWriter = null,
        CancellationToken cancellationToken = default)
    {
        string cacheFolder = ExpandHome(options.TokenCacheFolder);
        Directory.CreateDirectory(cacheFolder);
        string authRecordPath = Path.Combine(cacheFolder, AuthRecordFileName);

        AuthenticationRecord? authRecord = null;
        if (File.Exists(authRecordPath))
        {
            await using var readStream = new FileStream(authRecordPath, FileMode.Open, FileAccess.Read);
            authRecord = await AuthenticationRecord.DeserializeAsync(readStream, cancellationToken);
        }

        var credentialOptions = new DeviceCodeCredentialOptions
        {
            TenantId = options.TenantId,
            ClientId = options.ClientId,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions { Name = options.TokenCacheName },
            AuthenticationRecord = authRecord,
            // Throws AuthenticationRequiredException instead of prompting — stops a
            // non-interactive host printing a device code to an unwatched stdout.
            DisableAutomaticAuthentication = !options.AllowInteractiveAuthentication,
            DeviceCodeCallback = (code, _) =>
            {
                deviceCodeMessageWriter?.Invoke(code.Message);
                return Task.CompletedTask;
            }
        };

        var credential = new DeviceCodeCredential(credentialOptions);

        if (authRecord is null)
        {
            if (!options.AllowInteractiveAuthentication)
            {
                return new GraphAuthenticationResult(
                    GraphAuthenticationOutcome.AuthenticationRequired,
                    Error: $"No cached credential at {authRecordPath}.");
            }

            try
            {
                var newRecord = await credential.AuthenticateAsync(
                    new TokenRequestContext(Scopes), cancellationToken);

                await using var writeStream = new FileStream(authRecordPath, FileMode.Create, FileAccess.Write);
                await newRecord.SerializeAsync(writeStream, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new GraphAuthenticationResult(
                    GraphAuthenticationOutcome.Failed, Error: ex.Message);
            }
        }

        // Building the credential fetches no token, so the auth state would
        // otherwise first surface on a Graph call inside a request handler.
        try
        {
            await credential.GetTokenAsync(new TokenRequestContext(Scopes), cancellationToken);
        }
        catch (AuthenticationRequiredException ex)
        {
            return new GraphAuthenticationResult(
                GraphAuthenticationOutcome.AuthenticationRequired, Error: ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new GraphAuthenticationResult(
                GraphAuthenticationOutcome.Failed, Error: ex.Message);
        }

        return new GraphAuthenticationResult(
            GraphAuthenticationOutcome.Authenticated,
            new GraphServiceClient(credential, Scopes));
    }

    private static string ExpandHome(string path) =>
        path.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
}