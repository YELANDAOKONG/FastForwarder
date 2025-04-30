// VarLongTests.cs
using ConsoleForwarder.Tools;
using System;
using Xunit;

namespace ConsoleForwarder.Tools.Tests;

public class VarLongTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(9223372036854775807L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    public void RoundTrip_ShouldMatchOriginalValue(long value)
    {
        // Arrange
        var buffer = new byte[10];
        
        // Act
        var bytesWritten = VarLong.Write(buffer, value);
        var result = VarLong.Read(buffer.AsSpan(0, bytesWritten), out var bytesConsumed);
        
        // Assert
        Assert.Equal(value, result);
        Assert.Equal(bytesWritten, bytesConsumed);
    }

    [Fact]
    public void Read_ShouldThrowOnOverflow()
    {
        // Arrange
        var buffer = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 };
        
        // Act & Assert
        Assert.Throws<InvalidDataException>(() => VarLong.Read(buffer, out _));
    }
}