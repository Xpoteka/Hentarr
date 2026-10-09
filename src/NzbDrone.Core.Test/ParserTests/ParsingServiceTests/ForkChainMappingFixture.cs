using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.AniList;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // Fork: releases of an AniList sequel chain are numbered per entry and resolve through the per-season scene mappings.
    [TestFixture]
    public class ForkChainMappingFixture : CoreTest<ParsingService>
    {
        private Series _series;
        private List<Episode> _episodes;
        private List<SceneMapping> _mappings;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                .With(s => s.Id = 7)
                .With(s => s.TvdbId = 3479)
                .With(s => s.Title = "Taimanin Asagi")
                .With(s => s.CleanTitle = "taimaninasagi")
                .With(s => s.SeriesType = SeriesTypes.Anime)
                .With(s => s.SeasonType = SeasonType.Official)
                .With(s => s.UseSceneNumbering = false)
                .With(s => s.Seasons = new List<Season>
                {
                    new Season { SeasonNumber = 1, Monitored = true, Title = "Taimanin Asagi" },
                    new Season { SeasonNumber = 2, Monitored = true, Title = "Taimanin Asagi 2" },
                    new Season { SeasonNumber = 3, Monitored = true, Title = "Taimanin Asagi 3" }
                })
                .Build();

            _episodes = new List<Episode>();
            AddSeason(1, 4);
            AddSeason(2, 1);
            AddSeason(3, 2);

            _mappings = new List<SceneMapping>
            {
                Mapping("Taimanin Asagi", 1),
                Mapping("Anti-Demon Ninja Asagi", 1),
                Mapping("Taimanin Asagi 2", 2),
                Mapping("Taimanin Asagi 3", 3)
            };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTitle(It.IsAny<string>()))
                  .Returns((Series)null);

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTitle("taimaninasagi"))
                  .Returns(_series);

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTvdbId(3479))
                  .Returns(_series);

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(v => v.FindSceneMapping(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Returns<string, string, int>((title, release, season) => _mappings.SingleOrDefault(m => m.ParseTerm == title.CleanSeriesTitle()));

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(v => v.GetSceneSeasonNumber(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns<string, string>((title, release) => _mappings.SingleOrDefault(m => m.ParseTerm == title.CleanSeriesTitle())?.SceneSeasonNumber);

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(v => v.FindTvdbId(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Returns<string, string, int>((title, release, season) => _mappings.SingleOrDefault(m => m.ParseTerm == title.CleanSeriesTitle())?.TvdbId);

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodesBySeason(_series.Id, It.IsAny<int>()))
                  .Returns<int, int>((id, season) => _episodes.Where(e => e.SeasonNumber == season).ToList());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisodesBySceneNumbering(_series.Id, It.IsAny<int>(), It.IsAny<int>()))
                  .Returns<int, int, int>((id, season, episode) => _episodes.Where(e => e.SceneSeasonNumber == season && e.SceneEpisodeNumber == episode).ToList());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisodesBySceneNumbering(_series.Id, It.IsAny<int>()))
                  .Returns<int, int>((id, abs) => _episodes.Where(e => e.SceneAbsoluteEpisodeNumber == abs).ToList());

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(_series.Id, It.IsAny<int>(), It.IsAny<int>()))
                  .Returns<int, int, int>((id, season, episode) => _episodes.SingleOrDefault(e => e.SeasonNumber == season && e.EpisodeNumber == episode));

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.FindEpisode(_series.Id, It.IsAny<int>()))
                  .Returns<int, int>((id, abs) => _episodes.SingleOrDefault(e => e.AbsoluteEpisodeNumber == abs));
        }

        private static SceneMapping Mapping(string title, int sceneSeason)
        {
            return new SceneMapping
            {
                Title = title,
                SearchTerm = title,
                ParseTerm = title.CleanSeriesTitle(),
                TvdbId = 3479,
                SceneSeasonNumber = sceneSeason,
                Type = AniListSceneMappingProvider.MappingType
            };
        }

        private void AddSeason(int season, int count)
        {
            var offset = _episodes.Count;

            for (var k = 1; k <= count; k++)
            {
                _episodes.Add(new Episode
                {
                    Id = offset + k,
                    SeriesId = _series.Id,
                    SeasonNumber = season,
                    EpisodeNumber = k,
                    AbsoluteEpisodeNumber = offset + k,
                    SceneSeasonNumber = season,
                    SceneEpisodeNumber = k,
                    SceneAbsoluteEpisodeNumber = k
                });
            }
        }

        [Test]
        public void should_map_sequel_release_to_the_sequel_season()
        {
            var parsed = Parser.Parser.ParseTitle("[Group] Taimanin Asagi 3 - 02 [1080p].mkv");

            var result = Subject.Map(parsed, 0, 0, null);

            result.Series.Should().Be(_series);
            result.Episodes.Should().ContainSingle(e => e.SeasonNumber == 3 && e.EpisodeNumber == 2);
        }

        [Test]
        public void should_map_root_release_to_season_one()
        {
            var parsed = Parser.Parser.ParseTitle("[Group] Taimanin Asagi - 02 [1080p].mkv");

            var result = Subject.Map(parsed, 0, 0, null);

            result.Series.Should().Be(_series);
            result.Episodes.Should().ContainSingle(e => e.SeasonNumber == 1 && e.EpisodeNumber == 2);
        }

        [Test]
        public void should_map_alternate_title_release_to_season_one()
        {
            var parsed = Parser.Parser.ParseTitle("[Group] Anti-Demon Ninja Asagi - 03 [1080p].mkv");

            var result = Subject.Map(parsed, 0, 0, null);

            result.Series.Should().Be(_series);
            result.Episodes.Should().ContainSingle(e => e.SeasonNumber == 1 && e.EpisodeNumber == 3);
        }

        [Test]
        public void should_map_numberless_sequel_release_to_its_single_episode()
        {
            var parsed = Parser.Parser.ParseSeasonTitle("[Group] Taimanin Asagi 2 (BD 1920x1080 x264 10bits AAC)");

            parsed.Should().NotBeNull();
            parsed.IsSeasonTitle.Should().BeTrue();

            var result = Subject.Map(parsed, 0, 0, null);

            result.Series.Should().Be(_series);
            result.MappedSeasonNumber.Should().Be(2);
            result.ParsedEpisodeInfo.FullSeason.Should().BeFalse();
            result.Episodes.Should().ContainSingle(e => e.SeasonNumber == 2 && e.EpisodeNumber == 1);
        }

        [Test]
        public void should_map_numberless_sequel_release_to_the_whole_season()
        {
            var parsed = Parser.Parser.ParseSeasonTitle("[Group] Taimanin Asagi 3 (BD 1920x1080 x264 10bits AAC)");

            parsed.Should().NotBeNull();

            var result = Subject.Map(parsed, 0, 0, null);

            result.MappedSeasonNumber.Should().Be(3);
            result.ParsedEpisodeInfo.FullSeason.Should().BeTrue();
            result.Episodes.Should().HaveCount(2);
            result.Episodes.Should().OnlyContain(e => e.SeasonNumber == 3);
        }

        [Test]
        public void should_map_numberless_root_release_to_season_one()
        {
            var parsed = Parser.Parser.ParseSeasonTitle("[Group] Taimanin Asagi (BD 1920x1080 x264 10bits AAC)");

            parsed.Should().NotBeNull();

            var result = Subject.Map(parsed, 0, 0, null);

            result.MappedSeasonNumber.Should().Be(1);
            result.ParsedEpisodeInfo.FullSeason.Should().BeTrue();
            result.Episodes.Should().HaveCount(4);
            result.Episodes.Should().OnlyContain(e => e.SeasonNumber == 1);
        }
    }
}
