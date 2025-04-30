// StreamVarLongTests.cs
using ConsoleForwarder.Tools;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace ConsoleForwarder.Tools.Tests;

public class StreamVarLongTests
{
    [Fact]
    public async Task AsyncReadWrite_ShouldHandleLargeValue()
    {
        // Arrange
        var ms = new MemoryStream();
        var expected = long.MaxValue;
        
        // Act
        await StreamVarLong.WriteAsync(ms, expected);
        ms.Position = 0;
        var result = await StreamVarLong.ReadAsync(ms);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Read_ShouldDetectInvalidFormat()
    {
        // Arrange
        var ms = new MemoryStream(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80 });
        
        // Act & Assert
        Assert.Throws<InvalidDataException>(() => StreamVarLong.Read(ms));
    }
}