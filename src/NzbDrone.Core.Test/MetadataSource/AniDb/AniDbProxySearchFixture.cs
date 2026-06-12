using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MetadataSource.AniDb;
using NzbDrone.Core.MetadataSource.AniDb.Catalog;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.AniDb
{
    [TestFixture]
    public class AniDbProxySearchFixture : CoreTest<AniDbProxy>
    {
        private List<CatalogItem> _works;

        [SetUp]
        public void Setup()
        {
            _works = new List<CatalogItem>
            {
                new CatalogItem { AniDbId = 9988, Title = "Fake Anime", Year = 2021, Studio = "Fake Studio", Restricted = true },
                new CatalogItem { AniDbId = 9989, Title = "Other Fake Anime", Year = 2020, Studio = "Fake Studio", Restricted = false }
            };

            Mocker.GetMock<IAniDbCatalogService>()
                  .Setup(s => s.SearchStudioWorks("Fake Studio", It.IsAny<int>()))
                  .Returns(_works);
        }

        [TestCase("studio:Fake Studio")]
        [TestCase("company:Fake Studio")]
        [TestCase("STUDIO:Fake Studio")]
        [TestCase("studio: Fake Studio ")]
        public void should_map_studio_works_for_studio_prefix(string term)
        {
            var result = Subject.SearchForNewSeries(term);

            result.Should().HaveCount(2);

            result[0].TvdbId.Should().Be(9988);
            result[0].Title.Should().Be("Fake Anime");
            result[0].TitleSlug.Should().Be("9988");
            result[0].Year.Should().Be(2021);
            result[0].Network.Should().Be("Fake Studio");
            result[0].Certification.Should().Be("X");

            result[1].TvdbId.Should().Be(9989);
            result[1].Certification.Should().BeNull();
        }

        [TestCase("studio:")]
        [TestCase("studio:   ")]
        [TestCase("company:")]
        public void should_return_empty_list_when_studio_term_is_empty(string term)
        {
            Subject.SearchForNewSeries(term).Should().BeEmpty();

            Mocker.GetMock<IAniDbCatalogService>()
                  .Verify(s => s.SearchStudioWorks(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_return_existing_series_for_studio_works_already_added()
        {
            var existing = new Series { TvdbId = 9988, Title = "Fake Anime" };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTvdbId(9988))
                  .Returns(existing);

            var result = Subject.SearchForNewSeries("studio:Fake Studio");

            result.Should().HaveCount(2);
            result[0].Should().BeSameAs(existing);
        }

        [Test]
        public void should_not_treat_plain_title_search_as_studio_search()
        {
            Mocker.GetMock<IAniDbTitlesService>()
                  .Setup(s => s.Search("some title", It.IsAny<int>()))
                  .Returns(new List<AniDbTitle>());

            Subject.SearchForNewSeries("some title").Should().BeEmpty();

            Mocker.GetMock<IAniDbCatalogService>()
                  .Verify(s => s.SearchStudioWorks(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }
    }
}
