using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleForwarder.Tools;

public static class StreamVarLong
{
    public static void Write(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[10];
        int bytesWritten = 0;
        ulong num = (ulong)value;

        do
        {
            byte b = (byte)(num & 0x7F);
            num >>= 7;
            if (num != 0) b |= 0x80;
            buffer[bytesWritten++] = b;
        } while (num != 0);

        stream.Write(buffer.Slice(0, bytesWritten));
    }
    
    public static async ValueTask WriteAsync(Stream stream, long value,
        CancellationToken cancellationToken = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(10);
        try
        {
            int bytesWritten = 0;
            ulong num = (ulong)value;

            do
            {
                byte b = (byte)(num & 0x7F);
                num >>= 7;
                if (num != 0) b |= 0x80;
                buffer[bytesWritten++] = b;
            } while (num != 0);

            await stream.WriteAsync(buffer.AsMemory(0, bytesWritten), cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
    
    public static long Read(Stream stream)
    {
        long result = 0;
        int shift = 0;
        byte b;
        int bytesRead = 0;

        do
        {
            int read = stream.ReadByte();
            if (read == -1) throw new EndOfStreamException();
            b = (byte)read;
            bytesRead++;

            result |= (long)(b & 0x7F) << shift;
            shift += 7;

            if (bytesRead > 10)
                throw new InvalidDataException("VarLong exceeds maximum 10 bytes");
            
            if (shift > 64)
                throw new InvalidDataException("VarLong exceeds 64-bit range");
        } while ((b & 0x80) != 0);

        return result;
    }
    
    public static async ValueTask<long> ReadAsync(Stream stream,
        byte[]? reusableBuffer = null,
        CancellationToken cancellationToken = default)
    {
        byte[] buffer = reusableBuffer ?? new byte[10];
        long result = 0;
        int shift = 0;
        int bytesRead = 0;

        while (true)
        {
            if (bytesRead >= buffer.Length)
                throw new InvalidDataException("VarLong exceeds maximum 10 bytes");

            int read = await stream.ReadAsync(buffer.AsMemory(bytesRead, 1), cancellationToken);
            if (read == 0) throw new EndOfStreamException();

            byte b = buffer[bytesRead++];
            result |= (long)(b & 0x7F) << shift;
            shift += 7;

            if ((b & 0x80) == 0) break;
            
            if (shift > 64)
                throw new InvalidDataException("VarLong exceeds 64-bit range");
        }

        return result;
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
