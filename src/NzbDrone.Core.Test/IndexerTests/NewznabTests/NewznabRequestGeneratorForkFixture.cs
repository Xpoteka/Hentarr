using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.NewznabTests
{
    // Fork: Series.TvdbId holds an AniList id, so no external id may ever reach an indexer.
    [TestFixture]
    public class NewznabRequestGeneratorForkFixture : CoreTest<NewznabRequestGenerator>
    {
        private static readonly string[] ForbiddenParameters = { "tvdbid=", "imdbid=", "rid=", "tvmazeid=", "tmdbid=" };

        private Tv.Series _series;

        [SetUp]
        public void SetUp()
        {
            Subject.Definition = new IndexerDefinition { Name = "Newznab" };
            Subject.Settings = new NewznabSettings
            {
                BaseUrl = "http://127.0.0.1:1234/",
                Categories = new[] { 1, 2 },
                AnimeCategories = new[] { 3, 4 },
                ApiKey = "abcd",
                AnimeStandardFormatSearch = true
            };

            _series = new Tv.Series { TvRageId = 10, TvdbId = 10851, TvMazeId = 30, ImdbId = "tt40", TmdbId = 50 };

            var capabilities = new NewznabCapabilities
            {
                SupportedTvSearchParameters = new[] { "q", "season", "ep", "tvdbid", "imdbid", "rid", "tvmazeid", "tmdbid" },
                SupportsAggregateIdSearch = true
            };

            Mocker.GetMock<INewznabCapabilitiesProvider>()
                  .Setup(v => v.GetCapabilities(It.IsAny<NewznabSettings>()))
                  .Returns(capabilities);
        }

        private static List<string> AllQueries(IndexerPageableRequestChain chain)
        {
            return chain.GetAllTiers().Select(pageable => pageable.First().Url.Query).ToList();
        }

        [Test]
        public void should_search_single_episode_by_title_only()
        {
            var criteria = new SingleEpisodeSearchCriteria
            {
                Series = _series,
                SceneTitles = new List<string> { "euphoria", "Euphoria OVA" },
                SeasonNumber = 1,
                EpisodeNumber = 2
            };

            var queries = AllQueries(Subject.GetSearchRequests(criteria));

            queries.Should().NotBeEmpty();
            queries.Should().OnlyContain(q => q.Contains("q=") && !ForbiddenParameters.Any(q.Contains));
            queries.Should().Contain(q => q.Contains("q=euphoria&season=1&ep=2"));
            queries.Should().Contain(q => q.Contains("q=Euphoria%20OVA&season=1&ep=2"));
        }

        [Test]
        public void should_search_season_by_title_only()
        {
            var criteria = new SeasonSearchCriteria
            {
                Series = _series,
                SceneTitles = new List<string> { "euphoria" },
                SeasonNumber = 1
            };

            var queries = AllQueries(Subject.GetSearchRequests(criteria));

            queries.Should().NotBeEmpty();
            queries.Should().OnlyContain(q => q.Contains("q=") && !ForbiddenParameters.Any(q.Contains));
        }

        [Test]
        public void should_search_anime_episode_by_title_and_absolute_number()
        {
            var criteria = new AnimeEpisodeSearchCriteria
            {
                Series = _series,
                SceneTitles = new List<string> { "euphoria" },
                AbsoluteEpisodeNumber = 3,
                SeasonNumber = 1,
                EpisodeNumber = 3
            };

            var queries = AllQueries(Subject.GetSearchRequests(criteria));

            queries.Should().NotBeEmpty();
            queries.Should().OnlyContain(q => !ForbiddenParameters.Any(q.Contains));
            queries.Should().Contain(q => q.Contains("q=euphoria%2003") || q.Contains("q=euphoria+03"));
        }

        [Test]
        public void should_search_anime_season_by_plain_title_when_standard_format_is_off()
        {
            var criteria = new AnimeSeasonSearchCriteria
            {
                Series = _series,
                SceneTitles = new List<string> { "euphoria", "Euphoria OVA" },
                SeasonNumber = 1
            };

            Subject.Settings.AnimeStandardFormatSearch = false;

            var queries = AllQueries(Subject.GetSearchRequests(criteria));

            queries.Should().HaveCount(2);
            queries.Should().Contain(q => q.Contains("t=search") && q.EndsWith("&q=euphoria"));
            queries.Should().Contain(q => q.Contains("t=search") && q.EndsWith("&q=Euphoria%20OVA"));
            queries.Should().OnlyContain(q => !q.Contains("season="));
        }

        [Test]
        public void should_page_title_searches()
        {
            var criteria = new AnimeEpisodeSearchCriteria
            {
                Series = _series,
                SceneTitles = new List<string> { "euphoria" },
                AbsoluteEpisodeNumber = 3,
                SeasonNumber = 1,
                EpisodeNumber = 3
            };

            Subject.Settings.AnimeStandardFormatSearch = false;

            var results = Subject.GetSearchRequests(criteria);

            results.GetAllTiers().Should().HaveCount(1);

            var pages = results.GetAllTiers().First().ToList();

            pages.Should().HaveCount(Subject.MaxPages);
            pages[0].Url.Query.Should().Contain("&offset=0&");
            pages[1].Url.Query.Should().Contain("&offset=100&");
            pages.Should().OnlyContain(p => p.Url.Query.Contains("&cat=3,4&"));
        }

        [Test]
        public void should_search_anime_season_by_title_only()
        {
            var criteria = new AnimeSeasonSearchCriteria
            {
                Series = _series,
                SceneTitles = new List<string> { "euphoria" },
                SeasonNumber = 1
            };

            var queries = AllQueries(Subject.GetSearchRequests(criteria));

            queries.Should().NotBeEmpty();
            queries.Should().OnlyContain(q => !ForbiddenParameters.Any(q.Contains));
        }
    }
}
