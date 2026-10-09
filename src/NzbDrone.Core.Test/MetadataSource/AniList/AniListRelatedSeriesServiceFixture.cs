using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListRelatedSeriesServiceFixture : CoreTest<AniListRelatedSeriesService>
    {
        private Series _parent;
        private List<Series> _added;

        private static AniListRelationEdge Edge(string type, int id, string title, bool adult = true, string mediaType = "ANIME")
        {
            return new AniListRelationEdge
            {
                RelationType = type,
                Node = new AniListMedia { Id = id, Type = mediaType, IsAdult = adult, Title = new AniListTitle { Romaji = title } }
            };
        }

        [SetUp]
        public void Setup()
        {
            _added = new List<Series>();

            _parent = new Series
            {
                Id = 5,
                TvdbId = 3479,
                Title = "Taimanin Asagi",
                Path = "/hentai/Taimanin Asagi",
                QualityProfileId = 3,
                Monitored = true,
                MonitorNewItems = NewItemMonitorTypes.All,
                SeasonFolder = false,
                SeriesType = SeriesTypes.Anime,
                SeasonType = SeasonType.Official,
                Language = Language.English,
                Tags = new HashSet<int> { 7 },
                AddOptions = new AddSeriesOptions { Monitor = MonitorTypes.All, SearchForMissingEpisodes = true }
            };

            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AddRelatedSeries).Returns(true);
            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AdultFilter).Returns(AniListAdultFilter.Adult);

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetRelations(3479))
                  .Returns(new List<AniListRelationEdge>
                  {
                      Edge("SEQUEL", 6590, "Makai Kishi Ingrid"),
                      Edge("SIDE_STORY", 8837, "Taimanin Asagi: Bonus Video"),
                      Edge("SEQUEL", 21401, "Taimanin Asagi 2"),
                      Edge("ALTERNATIVE", 93291, "Taimanin Asagi (novel)", mediaType: "MANGA"),
                      Edge("ALTERNATIVE", 4242, "Taimanin TV cut", adult: false),
                      Edge("CHARACTER", 5151, "Some crossover")
                  });

            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(It.IsAny<int>())).Returns((Series)null);
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByTvdbId(It.IsAny<int>())).Returns((ImportListExclusion)null);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolderPath("/hentai/Taimanin Asagi")).Returns("/hentai");

            Mocker.GetMock<IAddSeriesService>()
                  .Setup(s => s.AddSeries(It.IsAny<Series>()))
                  .Returns<Series>(s =>
                  {
                      _added.Add(s);
                      return s;
                  });
        }

        [Test]
        public void should_add_related_adult_anime_entries_with_parent_settings()
        {
            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Select(s => s.TvdbId).Should().BeEquivalentTo(new[] { 6590, 8837, 21401 });

            var ingrid = _added.Single(s => s.TvdbId == 6590);
            ingrid.Title.Should().Be("Makai Kishi Ingrid");
            ingrid.RootFolderPath.Should().Be("/hentai");
            ingrid.QualityProfileId.Should().Be(3);
            ingrid.Monitored.Should().BeTrue();
            ingrid.SeriesType.Should().Be(SeriesTypes.Anime);
            ingrid.Tags.Should().BeEquivalentTo(new[] { 7 });
            ingrid.AddOptions.SearchForMissingEpisodes.Should().BeTrue();
        }

        [Test]
        public void should_skip_entries_already_in_library_or_excluded()
        {
            Mocker.GetMock<ISeriesService>().Setup(s => s.FindByTvdbId(6590)).Returns(new Series { Id = 9, TvdbId = 6590 });
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByTvdbId(21401)).Returns(new ImportListExclusion { TvdbId = 21401 });

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Select(s => s.TvdbId).Should().BeEquivalentTo(new[] { 8837 });
        }

        [Test]
        public void should_include_non_adult_entries_when_filter_allows_all()
        {
            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AdultFilter).Returns(AniListAdultFilter.All);

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Select(s => s.TvdbId).Should().Contain(4242);
            _added.Select(s => s.TvdbId).Should().NotContain(93291);
        }

        [Test]
        public void should_do_nothing_when_disabled()
        {
            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AddRelatedSeries).Returns(false);

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Should().BeEmpty();
            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetRelations(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_continue_when_one_entry_fails_validation()
        {
            Mocker.GetMock<IAddSeriesService>()
                  .Setup(s => s.AddSeries(It.Is<Series>(x => x.TvdbId == 6590)))
                  .Throws(new ValidationException(new List<ValidationFailure> { new ValidationFailure("Path", "exists") }));

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Select(s => s.TvdbId).Should().BeEquivalentTo(new[] { 8837, 21401 });
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_expand_whole_library_on_command()
        {
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { _parent });

            Subject.Execute(new AddRelatedSeriesCommand());

            _added.Should().HaveCount(3);
        }
    }
}
