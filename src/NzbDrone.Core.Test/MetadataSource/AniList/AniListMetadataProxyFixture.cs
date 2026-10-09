using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListMetadataProxyFixture : CoreTest<AniListMetadataProxy>
    {
        private AniListMedia _media;

        [SetUp]
        public void Setup()
        {
            _media = new AniListMedia
            {
                Id = 6987,
                IdMal = 6987,
                Title = new AniListTitle { Romaji = "Aki-Sora", Native = "あきそら" },
                Status = "FINISHED",
                Episodes = 1,
                StartDate = new AniListFuzzyDate { Year = 2009, Month = 12, Day = 18 }
            };

            Mocker.GetMock<IAniListMetadataOptions>()
                  .SetupGet(o => o.AdultFilter)
                  .Returns(AniListAdultFilter.Adult);

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMedia(6987))
                  .Returns(_media);

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMedia(It.Is<int>(i => i != 6987)))
                  .Throws(new SeriesNotFoundException(1));

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByMalId(6987))
                  .Returns(_media);

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.Search(It.IsAny<string>(), It.IsAny<AniListAdultFilter>()))
                  .Returns(new List<AniListMedia> { _media });

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTvdbId(It.IsAny<int>()))
                  .Returns((Series)null);
        }

        [Test]
        public void should_get_series_info_by_anilist_id()
        {
            var result = Subject.GetSeriesInfo(6987, Language.English, SeasonType.Official);

            result.Item1.TvdbId.Should().Be(6987);
            result.Item1.Title.Should().Be("Aki-Sora");
            result.Item2.Should().HaveCount(1);
        }

        [Test]
        public void should_throw_when_series_is_not_found()
        {
            Assert.Throws<SeriesNotFoundException>(() => Subject.GetSeriesInfo(1, Language.English, SeasonType.Official));
        }

        [TestCase("anilist:6987")]
        [TestCase("anilistid:6987")]
        [TestCase("AniList: 6987")]
        [TestCase("tvdb:6987")]
        [TestCase("tvdbid:6987")]
        public void should_lookup_by_anilist_id_prefix(string term)
        {
            var results = Subject.SearchForNewSeries(term, Language.English);

            results.Should().HaveCount(1);
            results[0].TvdbId.Should().Be(6987);

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.Search(It.IsAny<string>(), It.IsAny<AniListAdultFilter>()), Times.Never());
        }

        [TestCase("mal:6987")]
        [TestCase("malid:6987")]
        [TestCase("myanimelist:6987")]
        public void should_lookup_by_mal_id_prefix(string term)
        {
            var results = Subject.SearchForNewSeries(term, Language.English);

            results.Should().HaveCount(1);
            results[0].TvdbId.Should().Be(6987);

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.Search(It.IsAny<string>(), It.IsAny<AniListAdultFilter>()), Times.Never());
        }

        [TestCase("anilist:")]
        [TestCase("anilist:abc")]
        [TestCase("anilist:0")]
        [TestCase("anilist:1 2")]
        [TestCase("mal:")]
        [TestCase("mal:-5")]
        [TestCase("anilist:1")]
        [TestCase("mal:1")]
        public void should_return_empty_for_invalid_or_unknown_ids(string term)
        {
            Subject.SearchForNewSeries(term, Language.English).Should().BeEmpty();
        }

        [TestCase("imdb:tt1234567")]
        [TestCase("tmdb:1234")]
        public void should_return_empty_for_unsupported_prefixes(string term)
        {
            Subject.SearchForNewSeries(term, Language.English).Should().BeEmpty();

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.Search(It.IsAny<string>(), It.IsAny<AniListAdultFilter>()), Times.Never());
        }

        [Test]
        public void should_return_existing_series_when_already_in_library()
        {
            var existing = new Series { Id = 42, TvdbId = 6987, Title = "Already here" };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.FindByTvdbId(6987))
                  .Returns(existing);

            Subject.SearchForNewSeries("anilist:6987", Language.English).Should().ContainSingle(s => s.Id == 42);
            Subject.SearchForNewSeries("aki sora", Language.English).Should().ContainSingle(s => s.Id == 42);
        }

        [Test]
        public void should_search_with_configured_adult_filter()
        {
            Mocker.GetMock<IAniListMetadataOptions>()
                  .SetupGet(o => o.AdultFilter)
                  .Returns(AniListAdultFilter.All);

            var results = Subject.SearchForNewSeries("aki sora", Language.English);

            results.Should().HaveCount(1);
            results[0].Title.Should().Be("Aki-Sora");

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.Search("aki sora", AniListAdultFilter.All), Times.Once());
        }

        [Test]
        public void should_treat_bare_number_as_text_search()
        {
            Subject.SearchForNewSeries("6987", Language.English);

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.Search("6987", It.IsAny<AniListAdultFilter>()), Times.Once());
        }

        [Test]
        public void should_reject_path_like_terms()
        {
            Assert.Throws<InvalidSearchTermException>(() => Subject.SearchForNewSeries("/tv/anime", Language.English));
        }

        [Test]
        public void should_implement_both_metadata_interfaces()
        {
            Subject.Should().BeAssignableTo<NzbDrone.Core.MetadataSource.IProvideSeriesInfo>();
            Subject.Should().BeAssignableTo<NzbDrone.Core.MetadataSource.ISearchForNewSeries>();
            typeof(SkyHookProxy).Should().NotImplement<NzbDrone.Core.MetadataSource.IProvideSeriesInfo>();
            typeof(SkyHookProxy).Should().NotImplement<NzbDrone.Core.MetadataSource.ISearchForNewSeries>();
        }
    }
}
