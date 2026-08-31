using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using Quantum.ExtensionMarket;
using Quantum.ExtensionMarket.Application;
using Quantum.ExtensionMarket.Application.Handlers;
using Quantum.ExtensionMarket.Authentication;
using Quantum.ExtensionMarket.Contract;
using Quantum.ExtensionMarket.Infrastructure;

var builder = NOFWebApplicationBuilder.Create(args);

builder.AddApplicationPart(typeof(IExtensionMarketService).Assembly);
builder.AddApplicationPart(typeof(RegisterUser).Assembly);
builder.AddRpcServer<ExtensionMarketService>();
builder.Services.AddExtensionMarketApplication();
builder.Services.AddExtensionMarketInfrastructure(builder.Configuration);
builder.AddExtensionMarketAuthentication();
builder.AddExtensionMarketPostgreSql();
builder.Services.Configure<BootstrapAdminOptions>(
    builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.AddInitializationStep<BootstrapAdminInitializationStep>();
builder.Services.AddHealthChecks();

var app = await builder.BuildAsync();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false });
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/", () => Results.Ok(new
{
    service = "Quantum Extension Market",
    protocol = "JSON-RPC 2.0",
    endpoint = "/rpc"
}));

await app.RunAsync();

public partial class Program;
