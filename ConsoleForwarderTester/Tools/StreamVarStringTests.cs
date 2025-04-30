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
        // Arrange - 使用特殊的流模拟部分数据
        var stream = new MockEndingStream();
        
        // Act & Assert
        Assert.Throws<EndOfStreamException>(() => StreamVarString.Read(stream));
    }

    // 修改后的Mock流实现，使其在读取一定次数后始终返回0
    private class MockEndingStream : Stream
    {
        private int _readCount = 0;
        private readonly byte[] _initialData = new byte[] { 0x05 }; // VarInt(5)

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotImplementedException();
        public override long Position { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_readCount == 0)
            {
                _readCount++;
                buffer[offset] = _initialData[0];
                return 1;
            }
            
            if (_readCount < 3)
            {
                _readCount++;
                return 1; // 返回一些数据但不足以满足所需长度
            }
            
            return 0; // 模拟流结束但没有足够数据
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotImplementedException();
        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    }
}
