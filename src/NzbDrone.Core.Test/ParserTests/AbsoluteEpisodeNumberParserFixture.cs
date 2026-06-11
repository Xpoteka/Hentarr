using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class AbsoluteEpisodeNumberParserFixture : CoreTest
    {
        [TestCase("[SubsPlease] Fake Anime Title - 01 (1080p) [B1F227CF]", "Fake Anime Title", "SubsPlease", new[] { 1 })]
        [TestCase("[SubsPlease] Fake Anime Title - 01v2 (1080p) [B1F227CF]", "Fake Anime Title", "SubsPlease", new[] { 1 })]
        [TestCase("[Erai-raws] Fake Anime Title - 03 [1080p][Multiple Subtitle]", "Fake Anime Title", "Erai-raws", new[] { 3 })]
        [TestCase("[Judas] Fake Anime Title - 01-02 [BD 1080p][HEVC x265 10bit][Dual-Audio]", "Fake Anime Title", "Judas", new[] { 1, 2 })]
        [TestCase("Fake Anime Title - 05 [SomeGroup]", "Fake Anime Title", "SomeGroup", new[] { 5 })]
        [TestCase("Fake Anime Title - 12 (DVD 480p)", "Fake Anime Title", null, new[] { 12 })]
        [TestCase("Fake Anime Title Episode 1", "Fake Anime Title", null, new[] { 1 })]
        [TestCase("Fake.Anime.Title.Ep02.1080p.x265", "Fake Anime Title", null, new[] { 2 })]
        public void should_parse_absolute_numbered_anime_releases(string postTitle, string seriesTitle, string releaseGroup, int[] absoluteEpisodeNumbers)
        {
            var result = Parser.Parser.ParseTitle(postTitle);

            result.Should().NotBeNull();
            result.AbsoluteEpisodeNumbers.Should().BeEquivalentTo(absoluteEpisodeNumbers);
            result.SeriesTitle.Should().Be(seriesTitle);
            result.IsAbsoluteNumbering.Should().BeTrue();
            result.IsDaily.Should().BeFalse();

            if (releaseGroup != null)
            {
                result.ReleaseGroup.Should().Be(releaseGroup);
            }
        }

        [TestCase("Site Title 19-07-2023 - Performer Name - Beautiful Episode 2160p")]
        [TestCase("Site.Title.23.04.28.Performer.Name.Episode.Title.XXX.1080p")]
        public void should_not_parse_date_based_releases_as_absolute(string postTitle)
        {
            var result = Parser.Parser.ParseTitle(postTitle);

            result.Should().NotBeNull();
            result.IsDaily.Should().BeTrue();
            result.AbsoluteEpisodeNumbers.Should().BeEmpty();
        }
    }
}
