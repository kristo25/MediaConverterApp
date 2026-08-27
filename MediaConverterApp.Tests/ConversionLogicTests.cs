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

    [Fact]
    public void BuildOutputPath_PreservesRelativeFolderWhenUsingOutputFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "media-root");
        var source = Path.Combine(root, "album", "clip.wav");
        var outputRoot = Path.Combine(Path.GetTempPath(), "converted-root");

        var output = ConversionLogic.BuildOutputPath(
            source,
            root,
            "mp3",
            "Append _converted",
            useOutputFolder: true,
            outputRoot,
            preserveFolders: true);

        Assert.Equal(Path.Combine(outputRoot, "album", "clip_converted.mp3"), output);
    }

    [Fact]
    public void GetUniqueDestination_SkipsExistingAndReservedPaths()
    {
        var temp = Directory.CreateTempSubdirectory("media-converter-test-");
        try
        {
            var source = Path.Combine(temp.FullName, "source.wav");
            var destination = Path.Combine(temp.FullName, "source.mp3");
            File.WriteAllText(destination, "already here");

            var reserved = new[]
            {
                Path.Combine(temp.FullName, "source (1).mp3"),
                Path.Combine(temp.FullName, "source (2).mp3")
            };

            var unique = ConversionLogic.GetUniqueDestination(destination, source, reserved);

            Assert.Equal(Path.Combine(temp.FullName, "source (3).mp3"), unique);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
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

    [Theory]
    [InlineData("192k", "-q:a", "6")]
    [InlineData("256k", "-q:a", "8")]
    [InlineData("", "-q:a", "6")]
    [InlineData("not-a-number", "-q:a", "6")]
    public void GetEncoderArgs_OggCustom_UsesQualityScaleNotBitrate(string custom, string flag, string scale)
    {
        // Regression test: ogg/Custom used to pass the raw kbps straight to libvorbis's -b:a
        // (managed bitrate) mode, which libvorbis can outright reject for some sample
        // rate/channel combos (e.g. 192k on mono 44.1kHz - "encoder setup failed", verified
        // live against real ffmpeg). Mapping onto -q:a avoids that failure mode entirely.
        var args = ConversionLogic.GetEncoderArgs("ogg", "Custom", custom);
        Assert.DoesNotContain("-b:a", args);
        Assert.Equal(flag, args[2]);
        Assert.Equal(scale, args[3]);
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
