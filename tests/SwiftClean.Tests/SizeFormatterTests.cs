using SwiftClean.Helpers;
using Xunit;

namespace SwiftClean.Tests;

public class SizeFormatterTests
{
    private const long Kb = 1024;
    private const long Mb = Kb * 1024;
    private const long Gb = Mb * 1024;

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1, "1 B")]
    [InlineData(1023, "1023 B")]
    public void Format_Bytes_BelowKilobyte(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Theory]
    [InlineData(Kb, "1 KB")]
    [InlineData(Kb * 92, "92 KB")]
    [InlineData(Mb - 1, "1024 KB")] // rounds up but still below MB threshold
    public void Format_Kilobytes(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Theory]
    [InlineData(Mb, "1 MB")]
    [InlineData(Mb * 876, "876 MB")]
    public void Format_Megabytes(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Theory]
    [InlineData(Gb, "1.0 GB")]
    [InlineData((long)(Gb * 2.1), "2.1 GB")]
    public void Format_Gigabytes(long bytes, string expected)
        => Assert.Equal(expected, SizeFormatter.Format(bytes));

    [Fact]
    public void Format_UsesInvariantCulture_DecimalPoint()
        => Assert.Contains(".", SizeFormatter.Format((long)(Gb * 1.5)));

    [Fact]
    public void ToGigabytes_DividesByGibibyte()
        => Assert.Equal(2.0, SizeFormatter.ToGigabytes(Gb * 2), 5);
}
