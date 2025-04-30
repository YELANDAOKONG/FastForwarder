using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleForwarder.Tools;

public static class StreamVarString
{
    private const int MaxAllowedLength = 32767;

    public static void Write(Stream stream, string value)
    {
        byte[] lengthBuffer = new byte[5];
        int byteCount = Encoding.UTF8.GetByteCount(value);
        int lengthBytes = VarInt.Write(lengthBuffer.AsSpan(), byteCount);
        stream.Write(lengthBuffer, 0, lengthBytes);
        byte[] strBytes = Encoding.UTF8.GetBytes(value);
        stream.Write(strBytes, 0, strBytes.Length);
    }

    public static async ValueTask WriteAsync(Stream stream, string value,
        CancellationToken cancellationToken = default)
    {
        byte[] lengthBuffer = new byte[5];
        int byteCount = Encoding.UTF8.GetByteCount(value);
        int lengthBytes = VarInt.Write(lengthBuffer.AsSpan(), byteCount);
        await stream.WriteAsync(lengthBuffer.AsMemory(0, lengthBytes), cancellationToken);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            int written = Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, 0);
            await stream.WriteAsync(buffer.AsMemory(0, written), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    
    public static string Read(Stream stream, int maxLength = MaxAllowedLength)
    {
        int length = StreamVarInt.Read(stream);
        ValidateLength(length, maxLength);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = stream.Read(buffer, totalRead, length - totalRead);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Unexpected end of stream. Expected: {length}, Read: {totalRead}");
                }
                totalRead += read;
            }
            return Encoding.UTF8.GetString(buffer, 0, length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    
    public static async ValueTask<string> ReadAsync(Stream stream, 
        int maxLength = MaxAllowedLength,
        byte[]? reusableBuffer = null,
        CancellationToken cancellationToken = default)
    {
        int length = await StreamVarInt.ReadAsync(stream, reusableBuffer, cancellationToken);
        ValidateLength(length, maxLength);
        byte[] buffer = reusableBuffer != null && reusableBuffer.Length >= length 
            ? reusableBuffer 
            : ArrayPool<byte>.Shared.Rent(length);
        
        try 
        {
            int totalRead = 0;
            while (totalRead < length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(totalRead, length - totalRead), cancellationToken);
                if (read == 0)
                {
                    throw new EndOfStreamException($"Unexpected end of stream. Expected: {length}, Read: {totalRead}");
                }
                totalRead += read;
            }
            
            return Encoding.UTF8.GetString(buffer, 0, length);
        }
        finally 
        {
            if (reusableBuffer == null || reusableBuffer.Length < length)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    
    private static void ValidateLength(int length, int maxLength)
    {
        if (length < 0)
            throw new InvalidDataException($"Negative string length: {length}");
        
        if (length > maxLength)
            throw new InvalidDataException($"String length {length} exceeds maximum allowed {maxLength}");
    }
}
