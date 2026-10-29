using System.Runtime.InteropServices;
using System.Text;

namespace Quantum;

internal static class WindowsConsole
{
    private const uint AttachParentProcess = unchecked((uint)-1);
    private const int ErrorAccessDenied = 5;

    public static bool TryEnable()
    {
        var attached = AttachConsole(AttachParentProcess);
        if (!attached && Marshal.GetLastWin32Error() == ErrorAccessDenied)
        {
            // The process already has a console. This can happen when a console host launches
            // the managed entry point directly instead of starting the Windows apphost.
            attached = true;
        }

        if (!attached)
        {
            attached = AllocConsole();
        }

        return attached && TryRebindStandardWriters();
    }

    private static bool TryRebindStandardWriters()
    {
        try
        {
            Console.SetOut(CreateWriter(Console.OpenStandardOutput()));
            Console.SetError(CreateWriter(Console.OpenStandardError()));
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return false;
        }
    }

    private static TextWriter CreateWriter(Stream stream)
        => TextWriter.Synchronized(new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        });

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllocConsole();
}
