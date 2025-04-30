// VarIntTests.cs
using ConsoleForwarder.Tools;
using System;
using System.IO;
using Xunit;

namespace ConsoleForwarder.Tools.Tests;

public class VarIntTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(2097151)]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RoundTrip_ShouldMatchOriginalValue(int value)
    {
        // Arrange
        var buffer = new byte[5];
        
        // Act
        var bytesWritten = VarInt.Write(buffer, value);
        var result = VarInt.Read(buffer.AsSpan(0, bytesWritten), out var bytesConsumed);
        
        // Assert
        Assert.Equal(value, result);
        Assert.Equal(bytesWritten, bytesConsumed);
    }

    [Fact]
    public void Read_ShouldThrowOnIncompleteData()
    {
        // Arrange
        var buffer = new byte[] { 0x80, 0x80 };
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => VarInt.Read(buffer, out _));
    }

    [Fact]
    public void Write_ShouldThrowOnSmallBuffer()
    {
        // Arrange
        var buffer = new byte[3];
        
        // Act & Assert
        Assert.Throws<ArgumentException>(() => VarInt.Write(buffer, 2147483647));
    }
}