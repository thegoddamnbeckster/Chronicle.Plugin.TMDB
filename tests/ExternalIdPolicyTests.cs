using Chronicle.Plugin.TMDB;
using Xunit;

namespace Chronicle.Plugin.TMDB.Tests;

public class ExternalIdPolicyTests
{
    [Fact]
    public void TheProviderDeclaresHowItsIdsBehave()
    {
        var p = new TmdbMetadataProvider().GetExternalIdPolicy();
        Assert.Equal("tmdb", p.Source);
        Assert.Equal("tmdb:", p.StoredPrefixRewrites["person:"]);
        Assert.Matches(p.FamilyIdPatterns["movie"], "movie:550");
        Assert.DoesNotMatch(p.FamilyIdPatterns["movie"], "tv:1396");
        // every id this plugin issues for a title is described by exactly the shapes that say where it can sit
        Assert.Contains(p.IdShapes, s => System.Text.RegularExpressions.Regex.IsMatch("tv:1396/season:2/episode:3", s.Pattern) && s.Depths.Contains(2));
        Assert.Contains(p.IdShapes, s => System.Text.RegularExpressions.Regex.IsMatch("movie:550", s.Pattern) && s.ItemFamilies.Contains("movie"));
        Assert.DoesNotContain(p.IdShapes, s => System.Text.RegularExpressions.Regex.IsMatch("person:42", s.Pattern));   // people are not judged by shape
        var season = Assert.Single(p.ChildIds, c => c.Depth == 1);
        Assert.Matches(season.RootIdPattern, "tv:1396");
        Assert.DoesNotMatch(season.RootIdPattern, "tv:1396/season:2");
    }
}
