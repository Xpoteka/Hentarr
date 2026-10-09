using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.EpisodeImport.Aggregation.Aggregators
{
    [TestFixture]
    public class AggregateSingleEpisodeFallbackFixture : CoreTest<AggregateSingleEpisodeFallback>
    {
        private Series _series;
        private List<Episode> _episodes;

        [SetUp]
        public void Setup()
        {
            _series = Builder<Series>.CreateNew()
                .With(s => s.Id = 3)
                .With(s => s.TvdbId = 6987)
                .With(s => s.Title = "Aki-Sora")
                .With(s => s.CleanTitle = "akisora")
                .With(s => s.CleanOriginalTitle = null)
                .Build();

            _episodes = new List<Episode> { new Episode { Id = 11, SeriesId = 3, SeasonNumber = 1, EpisodeNumber = 1, AbsoluteEpisodeNumber = 1 } };

            Mocker.GetMock<IEpisodeService>()
                  .Setup(s => s.GetEpisodeBySeries(3))
                  .Returns(_episodes);

            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.FindTvdbId(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                  .Returns((int?)null);
        }

        private LocalEpisode GivenFile(string fileName, bool otherVideoFiles = false)
        {
            return new LocalEpisode
            {
                Path = $"/downloads/{fileName}".AsOsAgnostic(),
                Series = _series,
                Episodes = new List<Episode>(),
                OtherVideoFiles = otherVideoFiles
            };
        }

        [Test]
        public void should_map_numberless_file_named_after_the_series()
        {
            var localEpisode = GivenFile("[Group] Aki Sora [BD 1080p].mkv", otherVideoFiles: true);

            Subject.Aggregate(localEpisode, null);

            localEpisode.Episodes.Should().BeEquivalentTo(_episodes);
            localEpisode.FileEpisodeInfo.Should().NotBeNull();
            localEpisode.FileEpisodeInfo.AbsoluteEpisodeNumbers.Should().BeEquivalentTo(new[] { 1 });
            localEpisode.FileEpisodeInfo.FullSeason.Should().BeFalse();
            localEpisode.FileEpisodeInfo.Quality.Quality.Should().Be(Quality.Bluray1080p);
            localEpisode.FileEpisodeInfo.ReleaseGroup.Should().Be("Group");
            localEpisode.Quality.Quality.Should().Be(Quality.Bluray1080p);
        }

        [Test]
        public void should_map_numberless_file_matched_through_scene_mapping()
        {
            Mocker.GetMock<ISceneMappingService>()
                  .Setup(s => s.FindTvdbId("Autumn Sky", It.IsAny<string>(), -1))
                  .Returns(6987);

            var localEpisode = GivenFile("Autumn Sky [720p].mkv", otherVideoFiles: true);

            Subject.Aggregate(localEpisode, null);

            localEpisode.Episodes.Should().BeEquivalentTo(_episodes);
        }

        [Test]
        public void should_map_only_video_file_even_when_name_does_not_match()
        {
            var localEpisode = GivenFile("random_name_1080p.mkv", otherVideoFiles: false);

            Subject.Aggregate(localEpisode, null);

            localEpisode.Episodes.Should().BeEquivalentTo(_episodes);
        }

        [Test]
        public void should_not_map_unmatched_file_when_other_video_files_exist()
        {
            var localEpisode = GivenFile("Something Else [1080p].mkv", otherVideoFiles: true);

            Subject.Aggregate(localEpisode, null);

            localEpisode.Episodes.Should().BeEmpty();
            localEpisode.FileEpisodeInfo.Should().BeNull();
        }

        [Test]
        public void should_not_map_when_series_has_more_than_one_episode()
        {
            _episodes.Add(new Episode { Id = 12, SeriesId = 3, SeasonNumber = 1, EpisodeNumber = 2, AbsoluteEpisodeNumber = 2 });

            var localEpisode = GivenFile("[Group] Aki Sora [BD 1080p].mkv");

            Subject.Aggregate(localEpisode, null);

            localEpisode.Episodes.Should().BeEmpty();
        }

        [Test]
        public void should_not_override_episodes_already_found()
        {
            var localEpisode = GivenFile("[Group] Aki Sora - 01 [BD 1080p].mkv");
            var existing = new Episode { Id = 99 };
            localEpisode.Episodes = new List<Episode> { existing };

            Subject.Aggregate(localEpisode, null);

            localEpisode.Episodes.Should().ContainSingle(e => e.Id == 99);
        }

        [Test]
        public void should_ignore_non_video_files()
        {
            var localEpisode = GivenFile("[Group] Aki Sora [BD 1080p].nfo");

            Subject.Aggregate(localEpisode, null);

            localEpisode.Episodes.Should().BeEmpty();
        }
    }
}
