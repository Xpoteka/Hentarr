using System;
using System.Collections.Generic;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    // Fork: adding an AniList sequel stores the chain root returned by the metadata source, not the requested id.
    [TestFixture]
    public class ForkAddSeriesChainFixture : CoreTest<AddSeriesService>
    {
        private Series _resolved;

        [SetUp]
        public void Setup()
        {
            _resolved = new Series
            {
                TvdbId = 3479,
                Title = "Taimanin Asagi",
                TitleSlug = "taimanin-asagi",
                CleanTitle = "taimaninasagi",
                AniListIds = new HashSet<int> { 3479, 21401, 97854 },
                SeriesType = SeriesTypes.Anime,
                Seasons = new List<Season>
                {
                    new Season { SeasonNumber = 1, Monitored = true, Title = "Taimanin Asagi" },
                    new Season { SeasonNumber = 2, Monitored = true, Title = "Taimanin Asagi 2" },
                    new Season { SeasonNumber = 3, Monitored = true, Title = "Taimanin Asagi 3" }
                }
            };

            Mocker.GetMock<IProvideSeriesInfo>()
                  .Setup(s => s.GetSeriesInfo(21401, Language.English, It.IsAny<string>()))
                  .Returns(new Tuple<Series, List<Episode>>(_resolved, new List<Episode>()));

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetSeriesFolder(It.IsAny<Series>(), null))
                  .Returns<Series, NamingConfig>((c, n) => c.Title);

            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.IsAny<Series>()))
                  .Returns(new ValidationResult());
        }

        [Test]
        public void should_store_the_chain_root_id_when_a_sequel_is_added()
        {
            var newSeries = new Series
            {
                TvdbId = 21401,
                RootFolderPath = "/hentai",
                Language = Language.English,
                SeriesType = SeriesTypes.Standard,
                Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = false } }
            };

            var series = Subject.AddSeries(newSeries);

            series.TvdbId.Should().Be(3479);
            series.Title.Should().Be("Taimanin Asagi");
            series.AniListIds.Should().BeEquivalentTo(new[] { 3479, 21401, 97854 });
            series.SeriesType.Should().Be(SeriesTypes.Anime);
            series.Seasons.Should().HaveCount(3);
            series.Seasons.Should().OnlyContain(s => s.Monitored);

            Mocker.GetMock<ISeriesService>().Verify(s => s.AddSeries(It.Is<Series>(x => x.TvdbId == 3479)), Times.Once());
        }

        [Test]
        public void should_keep_requested_seasons_when_the_id_was_not_remapped()
        {
            Mocker.GetMock<IProvideSeriesInfo>()
                  .Setup(s => s.GetSeriesInfo(3479, Language.English, It.IsAny<string>()))
                  .Returns(new Tuple<Series, List<Episode>>(_resolved, new List<Episode>()));

            var newSeries = new Series
            {
                TvdbId = 3479,
                RootFolderPath = "/hentai",
                Language = Language.English,
                Seasons = new List<Season> { new Season { SeasonNumber = 1, Monitored = false }, new Season { SeasonNumber = 2, Monitored = true }, new Season { SeasonNumber = 3, Monitored = false } }
            };

            var series = Subject.AddSeries(newSeries);

            series.TvdbId.Should().Be(3479);
            series.Seasons.Should().ContainSingle(s => s.Monitored).Which.SeasonNumber.Should().Be(2);
        }
    }
}
