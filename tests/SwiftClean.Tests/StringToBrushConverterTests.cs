using System.Globalization;
using System.Windows.Media;
using SwiftClean.Helpers;
using Xunit;

namespace SwiftClean.Tests;

public class StringToBrushConverterTests
{
    private readonly StringToBrushConverter _conv = new();

    private object Convert(object? value)
        => _conv.Convert(value, typeof(Brush), null, CultureInfo.InvariantCulture);

    [Fact]
    public void Convert_Hex_ReturnsFrozenSolidColorBrush()
    {
        var brush = Assert.IsType<SolidColorBrush>(Convert("#5b4fff"));
        Assert.True(brush.IsFrozen);
        Assert.Equal(Color.FromRgb(0x5b, 0x4f, 0xff), brush.Color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Convert_EmptyOrNonString_ReturnsTransparent(object? value)
        => Assert.Same(Brushes.Transparent, Convert(value));

    [Fact]
    public void Convert_NonString_ReturnsTransparent()
        => Assert.Same(Brushes.Transparent, Convert(42));

    [Fact]
    public void Convert_SameHexTwice_ReturnsCachedInstance()
    {
        var first = Convert("#4ab87d");
        var second = Convert("#4ab87d");
        Assert.Same(first, second);
    }

    [Fact]
    public void ConvertBack_Throws()
        => Assert.Throws<NotSupportedException>(
            () => _conv.ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture));
}
