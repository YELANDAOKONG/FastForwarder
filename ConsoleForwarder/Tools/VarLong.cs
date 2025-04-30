// VarLong.cs
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace ConsoleForwarder.Tools;

public static class VarLong
{
    public static void Write(PipeWriter writer, long value)
    {
        Span<byte> buffer = writer.GetSpan(10);
        ulong num = (ulong)value;
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

    public static async ValueTask<long> ReadAsync(PipeReader reader)
    {
        long result = 0;
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

                result |= (long)(b & 0x7F) << shift;
                shift += 7;

                if (bytesRead > 10)
                    throw new InvalidDataException("VarLong exceeds 10 bytes");

                if (bytesRead == 10 && (b & 0x80) != 0)
                    throw new InvalidDataException("Invalid VarLong format");

                if ((b & 0x80) == 0)
                {
                    reader.AdvanceTo(buffer.Start);
                    return result;
                }
            }

            reader.AdvanceTo(buffer.Start, buffer.End);
        } while (true);
    }

    public static int Write(Span<byte> buffer, long value)
    {
        ulong num = (ulong)value;
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

    public static long Read(ReadOnlySpan<byte> buffer, out int bytesConsumed)
    {
        long result = 0;
        int shift = 0;
        bytesConsumed = 0;

        while (true)
        {
            if (bytesConsumed >= buffer.Length)
                throw new ArgumentException("Incomplete VarLong");

            byte b = buffer[bytesConsumed++];
            result |= (long)(b & 0x7F) << shift;
            shift += 7;

            if (bytesConsumed > 10)
                throw new InvalidDataException("VarLong exceeds 10 bytes");

            if ((b & 0x80) == 0)
                return result;
        }
    }

    public static byte[] ToBytes(long value)
    {
        byte[] buffer = new byte[10];
        int length = Write(buffer, value);
        return buffer.AsSpan(0, length).ToArray();
    }

    public static int CalculateByteSize(long value)
    {
        ulong num = (ulong)value;
        int count = 0;
        do
        {
            count++;
            num >>= 7;
        } while (num != 0);
        return count;
    }
}
