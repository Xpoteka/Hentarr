using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.AniDb
{
    [TestFixture]
    public class AniDbAnimeParserFixture : CoreTest
    {
        private const string ANIME_XML = @"<anime id=""9999"" restricted=""true"">
  <type>OVA</type>
  <episodecount>2</episodecount>
  <startdate>2020-03-27</startdate>
  <enddate>2020-06-26</enddate>
  <titles>
    <title xml:lang=""x-jat"" type=""main"">Fake Anime</title>
    <title xml:lang=""en"" type=""official"">Fake Anime (English)</title>
  </titles>
  <creators>
    <name id=""111"" type=""Direction"">Some Director</name>
    <name id=""222"" type=""Animation Work"">Fake Studio</name>
  </creators>
  <description>Source: http://anidb.net/ch12345 [Some Character] does things.</description>
  <ratings>
    <permanent count=""123"">6.78</permanent>
  </ratings>
  <picture>9999.jpg</picture>
  <tags>
    <tag id=""1"" weight=""600""><name>high weight tag</name></tag>
    <tag id=""2"" weight=""100""><name>low weight tag</name></tag>
  </tags>
  <episodes>
    <episode id=""100001""><epno type=""1"">1</epno><length>25</length><airdate>2020-03-27</airdate><title xml:lang=""en"">First Lesson</title></episode>
    <episode id=""100002""><epno type=""1"">2</epno><length>25</length><airdate>2020-06-26</airdate><title xml:lang=""x-jat"">Dainika</title></episode>
    <episode id=""100003""><epno type=""2"">S1</epno><length>5</length><airdate>2020-07-01</airdate><title xml:lang=""en"">Special</title></episode>
    <episode id=""100004""><epno type=""3"">C1</epno><length>2</length><title xml:lang=""en"">Opening</title></episode>
  </episodes>
</anime>";

        [Test]
        public void should_parse_anime_to_series()
        {
            var anime = AniDbAnimeParser.ParseAnime(XDocument.Parse(ANIME_XML).Root);

            anime.Restricted.Should().BeTrue();
            anime.Type.Should().Be("OVA");

            anime.Series.TvdbId.Should().Be(9999);
            anime.Series.Title.Should().Be("Fake Anime");
            anime.Series.TitleSlug.Should().Be("9999");
            anime.Series.Network.Should().Be("Fake Studio");
            anime.Series.Year.Should().Be(2020);
            anime.Series.Status.Should().Be(SeriesStatusType.Ended);
            anime.Series.Certification.Should().Be("X");
            anime.Series.Overview.Should().NotContain("anidb.net");
            anime.Series.Overview.Should().Contain("Some Character");
            anime.Series.Ratings.Votes.Should().Be(123);
            anime.Series.Genres.Should().ContainInOrder("high weight tag", "low weight tag");
            anime.Series.Images.Should().HaveCount(1);
            anime.Series.Runtime.Should().Be(25);
        }

        [Test]
        public void should_parse_regular_episodes_and_specials_only()
        {
            var anime = AniDbAnimeParser.ParseAnime(XDocument.Parse(ANIME_XML).Root);

            anime.Episodes.Should().HaveCount(3);

            var first = anime.Episodes.Single(e => e.TvdbId == 100001);
            first.Title.Should().Be("First Lesson");
            first.SeasonNumber.Should().Be(2020);
            first.AbsoluteEpisodeNumber.Should().Be(1);
            first.AirDate.Should().Be("2020-03-27");
            first.Runtime.Should().Be(25);

            var second = anime.Episodes.Single(e => e.TvdbId == 100002);
            second.Title.Should().Be("Dainika");
            second.AbsoluteEpisodeNumber.Should().Be(2);

            var special = anime.Episodes.Single(e => e.TvdbId == 100003);
            special.SeasonNumber.Should().Be(0);
            special.AbsoluteEpisodeNumber.Should().BeNull();
        }
    }
}
