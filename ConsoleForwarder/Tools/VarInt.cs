using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Text;

namespace ConsoleForwarder.Tools;

public static class VarInt
{
    public static void Write(PipeWriter writer, int value)
    {
        Span<byte> buffer = writer.GetSpan(5);
        uint num = (uint)value;
        int bytesWritten = 0;

        do
        {
            byte b = (byte)(num & 0x7F);
            num >>= 7;
            if (num != 0) b |= 0x80;
            buffer[bytesWritten++] = b;
        } while (num != 0);

        writer.Advance(bytesWritten);
    }

    public static async ValueTask<int> ReadAsync(PipeReader reader)
    {
        uint result = 0;
        int shift = 0;
        int bytesRead = 0;

        do
        {
            ReadResult readResult = await reader.ReadAsync();
            var buffer = readResult.Buffer;

            while (!buffer.IsEmpty)
            {
                byte b = buffer.FirstSpan[0];
                buffer = buffer.Slice(1);
                bytesRead++;

                result |= (uint)(b & 0x7F) << shift;
                shift += 7;

                if (bytesRead > 5)
                    throw new InvalidDataException("VarInt exceeds 5 bytes");

                if (bytesRead == 5 && (b & 0x80) != 0)
                    throw new InvalidDataException("Invalid VarInt format");

                if ((b & 0x80) == 0)
                {
                    reader.AdvanceTo(buffer.Start);
                    return (int)result;
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        } while (true);
    }

    public static int Write(Span<byte> buffer, int value)
    {
        uint num = (uint)value;
        int bytesWritten = 0;

        do
        {
            if (bytesWritten >= buffer.Length)
                throw new ArgumentException("Buffer too small");

            byte b = (byte)(num & 0x7F);
            num >>= 7;
            if (num != 0) b |= 0x80;
            buffer[bytesWritten++] = b;
        } while (num != 0);

        return bytesWritten;
    }
    
    public static int Read(ReadOnlySpan<byte> buffer, out int bytesConsumed)
    {
        uint result = 0;
        int shift = 0;
        bytesConsumed = 0;

        while (bytesConsumed < buffer.Length)
        {
            byte b = buffer[bytesConsumed++];
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;

            if (bytesConsumed > 5)
                throw new InvalidDataException("VarInt exceeds 5 bytes");

            if ((b & 0x80) == 0)
                return (int)result;
        }

        throw new ArgumentException("Incomplete VarInt");
    }

    public static byte[] ToBytes(int value)
    {
        byte[] buffer = new byte[5];
        int length = Write(buffer, value);
        return buffer.AsSpan(0, length).ToArray();
    }

    public static int CalculateByteSize(int value)
    {
        uint num = (uint)value;
        int count = 0;
        do
        {
            count++;
            num >>= 7;
        } while (num != 0);
        return count;
    }
}
