using MediaConverter;

namespace MediaConverterApp.Tests;

public class ConversionLogicTests
{
    [Theory]
    [InlineData("My Song", "My_Song")]
    [InlineData("track!!!.final", "trackfinal")]
    [InlineData("!!!", "converted")]
    [InlineData("a-b_c", "a-b_c")]
    public void MakeVrcSafeName_StripsUnsafeCharacters(string input, string expected) =>
        Assert.Equal(expected, ConversionLogic.MakeVrcSafeName(input));

    [Theory]
    [InlineData("song", "Same name", "song")]
    [InlineData("song", "Append _converted", "song_converted")]
    [InlineData("My Song!", "VRC-safe filename", "My_Song")]
    [InlineData("song", "Auto-number conflicts", "song")]
    public void BuildBaseName_AppliesNamingRule(string input, string rule, string expected) =>
        Assert.Equal(expected, ConversionLogic.BuildBaseName(input, rule));

    [Fact]
    public void GetRelativeDirectory_ReturnsEmptyForSameDirectory()
    {
        var root = Path.GetTempPath();
        Assert.Equal("", ConversionLogic.GetRelativeDirectory(root, root));
    }

    [Fact]
    public void GetRelativeDirectory_ReturnsSubpath()
    {
        var root = Path.Combine(Path.GetTempPath(), "root");
        var sub = Path.Combine(root, "a", "b");
        Assert.Equal(Path.Combine("a", "b"), ConversionLogic.GetRelativeDirectory(root, sub));
    }

    [Theory]
    [InlineData("mp3", "Balanced", "", "-q:a")]
    [InlineData("mp3", "Small file", "", "128k")]
    [InlineData("mp3", "Custom", "256k", "256k")]
    [InlineData("mp3", "Custom", "", "192k")]
    [InlineData("wav", "Balanced", "", "pcm_s16le")]
    [InlineData("flac", "Balanced", "", "flac")]
    [InlineData("m4a", "High quality", "", "256k")]
    public void GetEncoderArgs_ContainsExpectedToken(string format, string preset, string custom, string expectedToken)
    {
        var args = ConversionLogic.GetEncoderArgs(format, preset, custom);
        Assert.Contains(expectedToken, args);
    }

    [Fact]
    public void GetEncoderArgs_ThrowsForUnsupportedFormat() =>
        Assert.Throws<InvalidOperationException>(() => ConversionLogic.GetEncoderArgs("xyz", "Balanced", ""));

    [Fact]
    public void Csv_EscapesEmbeddedQuotes() =>
        Assert.Equal("\"say \"\"hi\"\"\"", ConversionLogic.Csv("say \"hi\""));

    [Theory]
    [InlineData("=cmd|'/c calc'!A1")]
    [InlineData("+1+1")]
    [InlineData("-1+1")]
    [InlineData("@SUM(A1)")]
    public void Csv_NeutralizesLeadingFormulaCharacters(string malicious)
    {
        var escaped = ConversionLogic.Csv(malicious);
        Assert.StartsWith("\"'", escaped);
    }

    [Fact]
    public void Csv_LeavesOrdinaryTextUnprefixed() =>
        Assert.Equal("\"ordinary text\"", ConversionLogic.Csv("ordinary text"));
}
