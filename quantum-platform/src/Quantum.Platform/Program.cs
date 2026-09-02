using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using Quantum.Platform;
using Quantum.Platform.Application;
using Quantum.Platform.Application.Handlers;
using Quantum.Platform.Authentication;
using Quantum.Platform.Contract;

var builder = NOFWebApplicationBuilder.Create(args);

builder.AddApplicationPart(typeof(IQuantumPlatformService).Assembly);
builder.AddApplicationPart(typeof(RegisterUser).Assembly);
builder.AddRpcServer<QuantumPlatformService>();
builder.Services.AddQuantumPlatformApplication();
builder.Services.AddQuantumPlatformServices(builder.Configuration);
builder.AddQuantumPlatformAuthentication();
builder.AddQuantumPlatformPostgreSql();
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
    service = "Quantum Platform",
    protocol = "JSON-RPC 2.0",
    endpoint = "/rpc"
}));

await app.RunAsync();

public partial class Program;
