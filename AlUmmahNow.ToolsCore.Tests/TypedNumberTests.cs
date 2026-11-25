using Xunit;

namespace AlUmmahNow.ToolsCore.Tests;

public class TypedNumberTests
{
    [Theory]
    [InlineData("12")]
    [InlineData(" 12 ")]
    [InlineData("١٢")]
    [InlineData("۱۲")]
    [InlineData("1٢")]
    [InlineData("012")]
    [InlineData("٢١")] // drawn as «١٢» in a box on macOS and Windows
    public void Any_digits_are_the_number(string text) => Assert.True(TypedNumber.Is(text, 12));

    [Theory]
    [InlineData("1,234")]
    [InlineData("١٬٢٣٤")]
    [InlineData("1 234")]
    public void Grouping_is_let_through(string text) => Assert.True(TypedNumber.Is(text, 1234));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(",")]
    [InlineData("1")]
    [InlineData("121")]
    [InlineData("21")]
    [InlineData("۲۱")]
    [InlineData("-12")]
    [InlineData("12x")]
    [InlineData("1.2")]
    [InlineData("twelve")]
    [InlineData("99999999999999999999")]
    public void Anything_else_is_not(string? text) => Assert.False(TypedNumber.Is(text, 12));
}
