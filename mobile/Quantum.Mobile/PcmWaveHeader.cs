using System.Buffers.Binary;
using System.Text;

namespace Quantum.MobileApp;

internal static class PcmWaveHeader
{
    public static byte[] Create(int dataBytes, int sampleRate = 16000)
    {
        if (dataBytes < 0 || dataBytes > 16 * 1024 * 1024 || sampleRate is < 8000 or > 48000) throw new ArgumentOutOfRangeException();
        var header = new byte[44];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), dataBytes + 36);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(header, 8);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(22), 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(28), sampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(32), 2);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(34), 16);
        Encoding.ASCII.GetBytes("data").CopyTo(header, 36);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), dataBytes);
        return header;
    }
}
