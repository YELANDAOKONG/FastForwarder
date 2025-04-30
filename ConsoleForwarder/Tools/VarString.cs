// VarString.cs
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;

namespace ConsoleForwarder.Tools;

public static class VarString
{
    private const int MaxAllowedLength = 32767;

    public static void Write(PipeWriter writer, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        VarInt.Write(writer, byteCount);

        Span<byte> buffer = writer.GetSpan(byteCount);
        int written = Encoding.UTF8.GetBytes(value, buffer);
        Debug.Assert(written == byteCount);
        writer.Advance(written);
    }

    public static async ValueTask<string> ReadAsync(PipeReader reader, int maxLength = MaxAllowedLength)
    {
        int length = await VarInt.ReadAsync(reader);
        if (length < 0) throw new InvalidDataException("Negative string length");
        if (length > maxLength) throw new InvalidDataException($"String length {length} exceeds {maxLength}");

        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int bytesRead = 0;
            while (bytesRead < length)
            {
                ReadResult result = await reader.ReadAsync();
                var data = result.Buffer;

                if (data.IsEmpty && result.IsCompleted)
                    throw new EndOfStreamException();

                int toCopy = (int)Math.Min(length - bytesRead, data.Length);
                data.Slice(0, toCopy).CopyTo(buffer.AsSpan(bytesRead, toCopy));
                bytesRead += toCopy;
                reader.AdvanceTo(data.GetPosition(toCopy));
            }

            return Encoding.UTF8.GetString(buffer, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static int Write(Span<byte> buffer, string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        int varIntBytes = VarInt.Write(buffer, byteCount);

        Span<byte> dataBuffer = buffer.Slice(varIntBytes);
        if (dataBuffer.Length < byteCount)
            throw new ArgumentException($"Insufficient buffer space. Required: {varIntBytes + byteCount}");

        int written = Encoding.UTF8.GetBytes(value, dataBuffer);
        return varIntBytes + written;
    }

    public static string Read(ReadOnlySpan<byte> buffer, out int bytesConsumed)
    {
        int length = VarInt.Read(buffer, out int varIntBytes);
        bytesConsumed = varIntBytes + length;

        if (length < 0) throw new FormatException("Negative string length");
        if (length > MaxAllowedLength) throw new FormatException($"String length exceeds {MaxAllowedLength}");
        if (bytesConsumed > buffer.Length) throw new ArgumentException("Incomplete string data");

        return Encoding.UTF8.GetString(buffer.Slice(varIntBytes, length));
    }

    public static byte[] ToBytes(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte[] varInt = VarInt.ToBytes(byteCount);
        byte[] strBytes = new byte[varInt.Length + byteCount];
        
        varInt.AsSpan().CopyTo(strBytes);
        Encoding.UTF8.GetBytes(value, strBytes.AsSpan(varInt.Length));
        return strBytes;
    }

    public static int CalculateByteSize(string value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        return VarInt.CalculateByteSize(byteCount) + byteCount;
    }
}
