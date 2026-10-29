using System.Text;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;

namespace Quantum.Logging;

internal sealed class SizeRollingFileSink : ILogEventSink, IDisposable
{
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _sync = new();
    private readonly string _filePathPrefix;
    private readonly ITextFormatter _formatter;
    private readonly long _fileSizeLimitBytes;
    private StreamWriter? _writer;
    private long _currentFileSize;
    private int _segmentIndex;
    private bool _disposed;

    public SizeRollingFileSink(
        string filePathPrefix,
        ITextFormatter formatter,
        long fileSizeLimitBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePathPrefix);
        ArgumentNullException.ThrowIfNull(formatter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileSizeLimitBytes);

        _filePathPrefix = filePathPrefix;
        _formatter = formatter;
        _fileSizeLimitBytes = fileSizeLimitBytes;
        OpenSegment();
    }

    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        using var buffer = new StringWriter();
        _formatter.Format(logEvent, buffer);
        var renderedEvent = buffer.ToString();
        var eventSize = FileEncoding.GetByteCount(renderedEvent);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_currentFileSize > 0 && _currentFileSize + eventSize > _fileSizeLimitBytes)
            {
                _writer!.Dispose();
                _segmentIndex++;
                OpenSegment();
            }

            _writer!.Write(renderedEvent);
            _writer.Flush();
            _currentFileSize += eventSize;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _writer?.Dispose();
            _disposed = true;
        }
    }

    private void OpenSegment()
    {
        var directory = Path.GetDirectoryName(_filePathPrefix)!;
        Directory.CreateDirectory(directory);

        var filePath = $"{_filePathPrefix}-{_segmentIndex}.log";
        var stream = new FileStream(
            filePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read);
        _currentFileSize = stream.Length;
        _writer = new StreamWriter(stream, FileEncoding);
    }
}
