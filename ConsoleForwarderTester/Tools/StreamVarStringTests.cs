// StreamVarStringTests.cs
using ConsoleForwarder.Tools;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace ConsoleForwarder.Tools.Tests;

public class StreamVarStringTests
{
    [Fact]
    public async Task AsyncReadWrite_ShouldHandleMultibyteChars()
    {
        // Arrange
        var ms = new MemoryStream();
        var expected = "𝄞𝄞𝄞"; // 音乐符号
        
        // Act
        await StreamVarString.WriteAsync(ms, expected);
        ms.Position = 0;
        var result = await StreamVarString.ReadAsync(ms);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Read_ShouldHandlePartialData()
    {
        // Arrange
        var data = Encoding.UTF8.GetBytes("\x04Test");
        var ms = new ThrottledStream(data, 2);
        
        // Act & Assert
        Assert.Throws<EndOfStreamException>(() => StreamVarString.Read(ms));
    }

    private class ThrottledStream : MemoryStream
    {
        private readonly int _chunkSize;

        public ThrottledStream(byte[] buffer, int chunkSize) : base(buffer)
        {
            _chunkSize = chunkSize;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return base.Read(buffer, offset, Math.Min(_chunkSize, count));
        }
    }
}