using Xunit;

namespace Chronicle.Plugin.TMDB.Tests;

// Regex \d matches the whole Unicode Nd category (not just ASCII 0-9), so a title carrying
// a fullwidth year suffix (e.g. "（２０１５）") used to make YearSuffixRe match "２０１５"
// and then int.Parse throw FormatException on it -- same crash class confirmed live
// (2026-08-30) in Chronicle's own MetadataEnrichmentService for a fullwidth volume number.
public class TryParseDigitsTests
{
    [Theory]
    [InlineData("2015", 2015)]
    [InlineData("0", 0)]
    [InlineData("０２", 2)]         // fullwidth, leading zero
    [InlineData("２０１５", 2015)]  // fullwidth digits -- the actual crash shape
    public void TryParseDigits_ParsesAsciiAndFullwidthDigits(string digits, int expected)
    {
        Assert.True(TmdbMetadataProvider.TryParseDigits(digits, out var number));
        Assert.Equal(expected, number);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("15a")]
    public void TryParseDigits_NonDigitInput_ReturnsFalse(string? digits)
    {
        Assert.False(TmdbMetadataProvider.TryParseDigits(digits!, out _));
    }

    [Fact]
    public void TryParseDigits_MidStringFailure_LeavesOutParamAtZero()
    {
        Assert.False(TmdbMetadataProvider.TryParseDigits("15a", out var number));
        Assert.Equal(0, number);
    }

    [Fact]
    public void TryParseDigits_TooLongToFitInInt_ReturnsFalseInsteadOfThrowing()
    {
        var ex = Record.Exception(() => TmdbMetadataProvider.TryParseDigits(new string('9', 50), out _));
        Assert.Null(ex);
        Assert.False(TmdbMetadataProvider.TryParseDigits(new string('9', 50), out var number));
        Assert.Equal(0, number);
    }
}
