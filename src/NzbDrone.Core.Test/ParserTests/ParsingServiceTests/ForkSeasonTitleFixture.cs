using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // Fork: releases named after the series with no numbers map to the single season of an AniList entry.
    [TestFixture]
    public class ForkSeasonTitleFixture : CoreTest<ParsingService>
    {
        private Series _series;
        private ParsedEpisodeInfo _parsedEpisodeInfo;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                .With(s => s.Id = 7)
                .With(s => s.TvdbId = 6987)
                .With(s => s.Title = "Aki-Sora")
                .With(s => s.CleanTitle = "akisora")
                .With(s => s.SeriesType = SeriesTypes.Anime)
                .With(s => s.SeasonType = SeasonType.Official)
                .With(s => s.UseSceneNumbering = false)
                .With(s => s.Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = true } })
                .Build();

            _parsedEpisodeInfo = Parser.Parser.ParseSeasonTitle("[007nF] Aki Sora (BD 1920x1080 x264 10bits AAC)");
            _parsedEpisodeInfo.Should().NotBeNull();
            _parsedEpisodeInfo.IsSeasonTitle.Should().BeTrue();

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTitle(It.IsAny<string>()))
                  .Returns(_series);

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(v => v.FindSceneMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Returns((SceneMapping)null);
        }

        private List<Episode> GivenEpisodes(int count)
        {
            var episodes = Enumerable.Range(1, count)
                                     .Select(n => new Episode { Id = n, SeriesId = _series.Id, SeasonNumber = 1, EpisodeNumber = n, AbsoluteEpisodeNumber = n })
                                     .ToList();

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesBySeason(_series.Id, 1))
                  .Returns(episodes);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisodesBySceneNumbering(_series.Id, It.IsAny<int>()))
                  .Returns(new List<Episode>());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(_series.Id, It.IsAny<int>()))
                  .Returns<int, int>((id, abs) => episodes.SingleOrDefault(e => e.AbsoluteEpisodeNumber == abs));

            return episodes;
        }

        [Test]
        public void should_map_numberless_release_to_the_only_episode_of_a_single_episode_series()
        {
            GivenEpisodes(1);

            var result = Subject.Map(_parsedEpisodeInfo, 0, 0, null);

            result.Series.Should().Be(_series);
            result.MappedSeasonNumber.Should().Be(1);
            result.ParsedEpisodeInfo.FullSeason.Should().BeFalse();
            result.ParsedEpisodeInfo.IsSeasonTitle.Should().BeFalse();
            result.ParsedEpisodeInfo.AbsoluteEpisodeNumbers.Should().BeEquivalentTo(new[] { 1 });
            result.ParsedEpisodeInfo.EpisodeNumbers.Should().BeEquivalentTo(new[] { 1 });
            result.Episodes.Should().ContainSingle(e => e.EpisodeNumber == 1);
        }

        [Test]
        public void should_map_numberless_release_to_the_whole_season_of_a_multi_episode_series()
        {
            GivenEpisodes(4);

            var result = Subject.Map(_parsedEpisodeInfo, 0, 0, null);

            result.MappedSeasonNumber.Should().Be(1);
            result.ParsedEpisodeInfo.FullSeason.Should().BeTrue();
            result.Episodes.Should().HaveCount(4);
        }

        [Test]
        public void should_not_guess_the_season_when_the_series_has_several_seasons()
        {
            GivenEpisodes(1);
            _series.Seasons.Add(new Season { SeasonNumber = 2, Monitored = true });

            var result = Subject.Map(_parsedEpisodeInfo, 0, 0, null);

            result.MappedSeasonNumber.Should().BeNull();
            result.Episodes.Should().BeEmpty();
        }

        [Test]
        public void should_not_touch_releases_with_episode_numbers()
        {
            var episodes = GivenEpisodes(1);
            var parsed = Parser.Parser.ParseTitle("[Group] Aki Sora - 01 [1080p].mkv");

            parsed.IsSeasonTitle.Should().BeFalse();

            var result = Subject.Map(parsed, 0, 0, null);

            result.Episodes.Should().BeEquivalentTo(episodes);
            result.ParsedEpisodeInfo.Should().BeSameAs(parsed);
        }
    }
}
