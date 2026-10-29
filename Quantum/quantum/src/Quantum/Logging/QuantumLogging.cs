using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Quantum.Logging;

internal sealed record QuantumLoggingOptions(string FilePathPrefix, bool WriteToConsole)
{
    public const string ConsoleArgument = "--console";

    public static QuantumLoggingOptions Parse(
        IEnumerable<string> arguments,
        string applicationDataPath)
    {
        return Parse(
            arguments,
            applicationDataPath,
            DateTimeOffset.Now,
            Environment.ProcessId);
    }

    internal static QuantumLoggingOptions Parse(
        IEnumerable<string> arguments,
        string applicationDataPath,
        DateTimeOffset startupTime,
        int processId)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationDataPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);

        var writeToConsole = arguments.Any(argument => string.Equals(
            argument,
            ConsoleArgument,
            StringComparison.OrdinalIgnoreCase));
        var filePathPrefix = Path.Combine(
            Path.GetFullPath(applicationDataPath),
            "Logs",
            $"quantum-{startupTime:yyyyMMdd-HHmmss}-{processId}");
        return new QuantumLoggingOptions(filePathPrefix, writeToConsole);
    }

    public string GetFilePath(int segmentIndex)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(segmentIndex);
        return $"{FilePathPrefix}-{segmentIndex}.log";
    }
}

internal static class QuantumLogging
{
    internal const long FileSizeLimitBytes = 20 * 1024 * 1024;

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

    internal static Serilog.Core.Logger CreateLogger(
        QuantumLoggingOptions options,
        long fileSizeLimitBytes = FileSizeLimitBytes)
    {
        ArgumentNullException.ThrowIfNull(options);

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Information)
            .Enrich.FromLogContext()
            .WriteTo.Sink(new SizeRollingFileSink(
                options.FilePathPrefix,
                new MessageTemplateTextFormatter(OutputTemplate, formatProvider: null),
                fileSizeLimitBytes));

        if (options.WriteToConsole)
        {
            configuration.WriteTo.Console(outputTemplate: OutputTemplate);
        }

        return configuration.CreateLogger();
    }
}
