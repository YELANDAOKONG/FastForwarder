// StreamVarIntTests.cs
using ConsoleForwarder.Tools;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace ConsoleForwarder.Tools.Tests;

public class StreamVarIntTests
{
    [Fact]
    public async Task AsyncRoundTrip_ShouldMatch()
    {
        // Arrange
        var ms = new MemoryStream();
        var expected = int.MinValue;
        
        // Act
        await StreamVarInt.WriteAsync(ms, expected);
        ms.Position = 0;
        var result = await StreamVarInt.ReadAsync(ms);
        
        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Read_ShouldThrowOnIncompleteStream()
    {
        // Arrange
        var ms = new MemoryStream(new byte[] { 0x80, 0x80 });
        
        // Act & Assert
        Assert.Throws<EndOfStreamException>(() => StreamVarInt.Read(ms));
    }
}