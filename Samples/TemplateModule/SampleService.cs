using Microsoft.Extensions.Logging;

namespace TemplateModule;
public class SampleService : ISampleService
{
    private readonly ILogger<SampleService> _logger;

    public SampleService(ILogger<SampleService> logger)
    {
        _logger = logger;
        InitializeTask = InitializeAsync();
    }

    public bool IsInitialized { get; private set; }
    public Task InitializeTask { get; }
    public Task InitializeAsync()
    {
        _logger.LogInformation("SampleService is initialized.");
        IsInitialized = true;
        return Task.CompletedTask;
    }

    public string SayHello() => "Hello, Quantum!";
}
