using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Quantum.Logging;

internal sealed record QuantumLoggingOptions(string FilePath, bool WriteToConsole)
{
    public const string ConsoleArgument = "--console";

    public static QuantumLoggingOptions Parse(
        IEnumerable<string> arguments,
        string applicationDataPath)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataPath);

        var writeToConsole = arguments.Any(argument => string.Equals(
            argument,
            ConsoleArgument,
            StringComparison.OrdinalIgnoreCase));
        var filePath = Path.Combine(
            Path.GetFullPath(applicationDataPath),
            "Logs",
            "quantum-.log");
        return new QuantumLoggingOptions(filePath, writeToConsole);
    }
}

internal static class QuantumLogging
{
    private const string OutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] "
        + "[{SourceContext}] {Message:lj}{NewLine}{Exception}";

    public static void Configure(ILoggingBuilder logging, QuantumLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(options);

        logging.ClearProviders();
        logging.AddSerilog(CreateLogger(options), dispose: true);
    }

    internal static Serilog.Core.Logger CreateLogger(QuantumLoggingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.File(
                options.FilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                rollOnFileSizeLimit: true,
                outputTemplate: OutputTemplate);

        if (options.WriteToConsole)
        {
            configuration.WriteTo.Console(outputTemplate: OutputTemplate);
        }

        return configuration.CreateLogger();
    }
}
