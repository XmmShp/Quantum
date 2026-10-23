using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using Quantum.Platform;
using Quantum.Platform.Application;
using Quantum.Platform.Application.Handlers;
using Quantum.Platform.Authentication;
using Quantum.Platform.Contract;
using Quantum.Platform.UI.Components;
using Quantum.Platform.SkillDistribution;

var builder = NOFWebApplicationBuilder.Create(args);
var emailConfigurationPath = builder.Configuration["QuantumPlatform:Email:ConfigurationPath"];
if (!string.IsNullOrWhiteSpace(emailConfigurationPath))
{
    builder.Configuration.AddJsonFile(emailConfigurationPath, optional: false, reloadOnChange: false);
}

builder.AddApplicationPart(typeof(IQuantumPlatformService).Assembly);
builder.AddApplicationPart(typeof(RegisterUser).Assembly);
builder.AddRpcServer<QuantumPlatformService>();
builder.AddRpcServer<MobileBridgeService>();
builder.Services.AddQuantumPlatformApplication();
builder.Services.AddQuantumPlatformServices(builder.Configuration, builder.Environment);
builder.AddQuantumPlatformAuthentication();
builder.AddQuantumPlatformPostgreSql();
builder.Services.AddHealthChecks();
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = await builder.BuildAsync();

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = static _ => false });
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapQuantumPublishSkill();
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Routes).Assembly);
app.MapGet("/api/status", () => Results.Ok(new
{
    service = "Quantum Platform",
    protocol = "JSON-RPC 2.0",
    endpoint = "/rpc",
    portal = "/portal/"
}));

await app.RunAsync();
