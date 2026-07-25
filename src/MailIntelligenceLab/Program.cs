using Microsoft.Extensions.Configuration;
using Azure.Identity;
using Microsoft.Graph;

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();

string clientId = config["AzureAd:ClientId"]!;
string tenantId = config["AzureAd:TenantId"]!;

var credential = new DeviceCodeCredential(new DeviceCodeCredentialOptions
{
    TenantId = tenantId,
    ClientId = clientId,
    DeviceCodeCallback = (code, cancellationToken) =>
    {
        Console.WriteLine(code.Message);
        return Task.CompletedTask;
    }
});

var graphClient = new GraphServiceClient(credential, new[] { "User.Read" });

try
{
    var me = await graphClient.Me.GetAsync();
    Console.WriteLine($"Autenticado como: {me?.DisplayName} ({me?.Mail ?? me?.UserPrincipalName})");
}
catch (Exception ex)
{
    Console.WriteLine($"ERRO ao chamar o Graph: {ex.Message}");
    Console.WriteLine(ex);
}