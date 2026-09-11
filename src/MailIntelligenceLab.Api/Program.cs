using System.Globalization;
using MailIntelligenceLab.Adapters.Graph;
using MailIntelligenceLab.Ports;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var graphOptions = new GraphAuthenticationOptions(
    ClientId: builder.Configuration["AzureAd:ClientId"]!,
    TenantId: builder.Configuration["AzureAd:TenantId"]!,
    TokenCacheFolder: builder.Configuration["TokenCache:FolderPath"]!,
    TokenCacheName: builder.Configuration["TokenCache:CacheName"]!,
    // The console owns interactive login. A web host has nowhere to show a
    // device code, so an expired cache must fail fast instead of hanging.
    AllowInteractiveAuthentication: false);

// Once at startup, not per request: the probe costs a Graph round-trip and
// takes the token cache lock.
var authentication = await GraphAuthenticator.CreateAsync(graphOptions);

IEmailProvider? emailProvider = authentication.Outcome == GraphAuthenticationOutcome.Authenticated
    ? new GraphEmailProvider(authentication.Client!)
    : null;

app.MapGet("/api/auth", () => authentication.Outcome switch
{
    GraphAuthenticationOutcome.Authenticated =>
        Results.Ok(new { status = "authenticated" }),

    GraphAuthenticationOutcome.AuthenticationRequired =>
        Results.Json(new
        {
            status = "authentication-required",
            detail = authentication.Error,
            remedy = "Run 'dotnet run' once in src/MailIntelligenceLab.Console to authenticate."
        }, statusCode: StatusCodes.Status401Unauthorized),

    _ => Results.Json(new
    {
        status = "failed",
        detail = authentication.Error
    }, statusCode: StatusCodes.Status503ServiceUnavailable)
});

app.Run();