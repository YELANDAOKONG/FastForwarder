using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleForwarder.Tools;

public static class StreamVarInt
{
    // 同步写入
    public static void Write(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[5];
        int bytesWritten = 0;
        uint num = (uint)value;
        do
        {
            byte b = (byte)(num & 0x7F);
            num >>= 7;
            if (num != 0) b |= 0x80;
            buffer[bytesWritten++] = b;
        } while (num != 0);
        stream.Write(buffer.Slice(0, bytesWritten));
    }

    public static async ValueTask WriteAsync(Stream stream, int value, 
        CancellationToken cancellationToken = default)
    {
        byte[] buffer = new byte[5];
        int bytesWritten = 0;
        uint num = (uint)value;
        do
        {
            byte b = (byte)(num & 0x7F);
            num >>= 7;
            if (num != 0) b |= 0x80;
            buffer[bytesWritten++] = b;
        } while (num != 0);
        await stream.WriteAsync(buffer.AsMemory(0, bytesWritten), cancellationToken);
    }

    public static int Read(Stream stream)
    {
        int result = 0;
        int shift = 0;
        byte b;
        int bytesRead = 0;
        do
        {
            int readByte = stream.ReadByte();
            if (readByte == -1) throw new EndOfStreamException();
            b = (byte)readByte;
            bytesRead++;
            
            if (bytesRead > 5)
                throw new InvalidDataException("VarInt exceeds maximum 5 bytes");
            if (bytesRead == 5)
            {
                if ((b & 0x80) != 0)
                    throw new InvalidDataException("VarInt format invalid: 5th byte has continuation flag");
                
                if (b > 0x0F)
                    throw new InvalidDataException("VarInt exceeds 32-bit range");
            }
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        if (bytesRead == 5 && (result & 0x8000_0000) != 0)
        {
            result |= ~0x7FFF_FFFF;
        }
        return result;
    }

    public static async ValueTask<int> ReadAsync(Stream stream, 
        byte[]? reusableBuffer = null,
        CancellationToken cancellationToken = default)
    {
        byte[] buffer = reusableBuffer ?? new byte[5];
        int result = 0;
        int shift = 0;
        int bytesRead = 0;
        while (true)
        {
            if (bytesRead >= buffer.Length)
                throw new InvalidDataException("VarInt too long");
            int read = await stream.ReadAsync(buffer.AsMemory(bytesRead, 1), cancellationToken);
            if (read == 0) throw new EndOfStreamException();
            byte b = buffer[bytesRead++];
            result |= (b & 0x7F) << shift;
            shift += 7;
            if ((b & 0x80) == 0) break;
            if (shift > 35) throw new InvalidDataException("VarInt exceeds 32-bit range");
        }
        return result;
    }
}
