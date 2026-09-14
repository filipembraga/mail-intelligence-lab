using System.Globalization;
using MailIntelligenceLab.Adapters.Graph;
using MailIntelligenceLab.Adapters.Planning;
using MailIntelligenceLab.Planning;
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

IPlanStore planStore = new FileSystemPlanStore(
    Path.GetFullPath(builder.Configuration["Plans:RawFolder"]!));

// No auth: this reads a local file that is already readable on this disk,
// served on localhost. Revisit if either of those stops being true.
app.MapGet("/api/plan", () =>
{
    string? planPath = planStore.FindNewestPath();

    if (planPath is null)
    {
        return Results.Json(new
        {
            status = "no-plan",
            remedy = "Run 'dotnet run -- plan' in src/MailIntelligenceLab.Console to generate one."
        }, statusCode: StatusCodes.Status404NotFound);
    }

    var plan = planStore.Load(planPath);

    if (plan is null)
    {
        return Results.Json(new
        {
            status = "unparseable-plan",
            fileName = Path.GetFileName(planPath),
            detail = "Filename carries no parseable freeze bound."
        }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    return Results.Ok(new
    {
        fileName = plan.FileName,
        freezeBoundUtc = plan.FreezeBoundUtc,
        rows = plan.Rows
    });
});

app.MapPut("/api/plan/marks", (MarkPlanRequest request) =>
{
    // Omitted is not the same as empty: a client that dropped the field has a bug,
    // and a 200 would tell it everything worked.
    if (request.Marks is null)
    {
        return Results.Json(new
        {
            status = "malformed-request",
            detail = "'marks' is required. Send an empty array to change nothing."
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    string? planPath = planStore.FindNewestPath();

    if (planPath is null)
    {
        return Results.Json(new
        {
            status = "no-plan",
            remedy = "Run 'dotnet run -- plan' in src/MailIntelligenceLab.Console to generate one."
        }, statusCode: StatusCodes.Status404NotFound);
    }

    string newestFileName = Path.GetFileName(planPath);

    // The marks were decided against a different plan's data; applying them here
    // would act on rows the user never saw.
    if (!string.Equals(request.FileName, newestFileName, StringComparison.Ordinal))
    {
        return Results.Json(new
        {
            status = "stale-plan",
            fileName = request.FileName,
            newestFileName
        }, statusCode: StatusCodes.Status409Conflict);
    }

    var plan = planStore.Load(planPath);

    if (plan is null)
    {
        return Results.Json(new
        {
            status = "unparseable-plan",
            fileName = newestFileName,
            detail = "Filename carries no parseable freeze bound."
        }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    var marking = PlanMarker.Apply(plan.Rows, request.Marks);

    if (!marking.IsValid)
    {
        return Results.Json(new
        {
            status = "invalid-marks",
            errors = marking.Errors
        }, statusCode: StatusCodes.Status400BadRequest);
    }

    // Same path, never a new name: the freeze bound lives in the filename.
    planStore.Save(plan.FullPath, marking.Rows);

    return Results.Ok(new
    {
        fileName = plan.FileName,
        markedRows = marking.Rows.Count(row => ActionPlanGenerator.IsActionable(row.Action))
    });
});

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

record MarkPlanRequest(string FileName, IReadOnlyList<PlanMark>? Marks);