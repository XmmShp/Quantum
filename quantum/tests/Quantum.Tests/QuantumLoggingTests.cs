using Quantum.Logging;
using Serilog.Events;

namespace Quantum.Tests;

public sealed class QuantumLoggingTests
{
    [Fact]
    public void Parse_CreatesProcessSpecificStartupFileWithoutConsoleOutput()
    {
        var applicationDataPath = Path.Combine("app", "data");
        var startupTime = new DateTimeOffset(2026, 9, 2, 14, 5, 6, TimeSpan.FromHours(8));

        var options = QuantumLoggingOptions.Parse(
            ["quantum"],
            applicationDataPath,
            startupTime,
            processId: 1234);

        Assert.False(options.WriteToConsole);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(
                applicationDataPath,
                "Logs",
                "quantum-20260902-140506-1234-0.log")),
            options.GetFilePath(segmentIndex: 0));
    }

    [Theory]
    [InlineData("--console")]
    [InlineData("--CONSOLE")]
    public void Parse_EnablesConsoleOutputFromCommandLine(string argument)
    {
        var options = QuantumLoggingOptions.Parse(["quantum", argument], "app-data");

        Assert.True(options.WriteToConsole);
    }

    [Fact]
    public void Logger_WritesEventsToTheFirstStartupLogSegment()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quantum-logging-{Guid.NewGuid():N}");
        var startupTime = new DateTimeOffset(2026, 9, 2, 23, 59, 59, TimeSpan.Zero);
        var options = QuantumLoggingOptions.Parse([], root, startupTime, processId: 4321);
        try
        {
            using (var logger = QuantumLogging.CreateLogger(options))
            {
                logger.Write(LogEventLevel.Debug, "Diagnostic value {Value}", 42);
            }

            var logFile = Assert.Single(Directory.GetFiles(
                Path.Combine(root, "Logs"),
                "quantum-*.log"));
            Assert.Equal(options.GetFilePath(segmentIndex: 0), logFile);
            Assert.Contains("Diagnostic value 42", File.ReadAllText(logFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Logger_RollsToNumberedSegmentsBeforeExceedingTheSizeLimit()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quantum-logging-{Guid.NewGuid():N}");
        var startupTime = new DateTimeOffset(2026, 9, 2, 23, 59, 59, TimeSpan.Zero);
        var options = QuantumLoggingOptions.Parse([], root, startupTime, processId: 4321);
        try
        {
            using (var logger = QuantumLogging.CreateLogger(options, fileSizeLimitBytes: 256))
            {
                logger.Information("First event {Payload}", new string('a', 140));
                logger.Information("Second event {Payload}", new string('b', 140));
            }

            Assert.Equal(
                [options.GetFilePath(0), options.GetFilePath(1)],
                Directory.GetFiles(Path.Combine(root, "Logs"), "quantum-*.log").Order());
            Assert.Contains("First event", File.ReadAllText(options.GetFilePath(0)));
            Assert.Contains("Second event", File.ReadAllText(options.GetFilePath(1)));
            Assert.True(new FileInfo(options.GetFilePath(0)).Length <= 256);
            Assert.True(new FileInfo(options.GetFilePath(1)).Length <= 256);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
