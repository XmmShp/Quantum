using Quantum.Logging;
using Serilog.Events;

namespace Quantum.Tests;

public sealed class QuantumLoggingTests
{
    [Fact]
    public void Parse_DefaultsToDailyFileWithoutConsoleOutput()
    {
        var applicationDataPath = Path.Combine("app", "data");

        var options = QuantumLoggingOptions.Parse(["quantum"], applicationDataPath);

        Assert.False(options.WriteToConsole);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(applicationDataPath, "Logs", "quantum-.log")),
            options.FilePath);
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
    public void Logger_WritesDebugEventsToDailyLogFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quantum-logging-{Guid.NewGuid():N}");
        var options = QuantumLoggingOptions.Parse([], root);
        try
        {
            using (var logger = QuantumLogging.CreateLogger(options))
            {
                logger.Write(LogEventLevel.Debug, "Diagnostic value {Value}", 42);
            }

            var logFile = Assert.Single(Directory.GetFiles(
                Path.Combine(root, "Logs"),
                "quantum-*.log"));
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
}
