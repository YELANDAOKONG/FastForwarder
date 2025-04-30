// VarStringTests.cs
using ConsoleForwarder.Tools;
using System;
using System.Text;
using Xunit;

namespace ConsoleForwarder.Tools.Tests;

public class VarStringTests
{
    [Theory]
    [InlineData("")]
    [InlineData("Hello")]
    [InlineData("中文测试")]
    [InlineData("🐼🎮")]
    public void RoundTrip_ShouldMatchOriginalValue(string value)
    {
        // Arrange
        var buffer = new byte[VarString.CalculateByteSize(value)];
        
        // Act
        var bytesWritten = VarString.Write(buffer, value);
        var result = VarString.Read(buffer.AsSpan(0, bytesWritten), out var bytesConsumed);
        
        // Assert
        Assert.Equal(value, result);
        Assert.Equal(bytesWritten, bytesConsumed);
    }

    [Fact]
    public void Read_ShouldThrowOnExceedMaxLength()
    {
        // Arrange
        var longString = new string('a', 32768);
        var buffer = VarString.ToBytes(longString);
        
        // Act & Assert
        Assert.Throws<FormatException>(() => VarString.Read(buffer, out _));
    }
}