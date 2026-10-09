using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.AniList;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.DataAugmentation.AniList
{
    [TestFixture]
    public class AniListSceneMappingProviderFixture : CoreTest<AniListSceneMappingProvider>
    {
        private Series _akiSora;
        private Series _euphoria;
        private List<string> _akiSoraTitles;

        [SetUp]
        public void Setup()
        {
            _akiSora = new Series { Id = 1, TvdbId = 6987, Title = "Aki-Sora", CleanTitle = "akisora" };
            _euphoria = new Series { Id = 2, TvdbId = 10851, Title = "euphoria", CleanTitle = "euphoria" };
            _akiSoraTitles = new List<string> { "Autumn Sky", "Akisora" };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series> { _akiSora, _euphoria });

            Mocker.GetMock<IAniListTitleCache>()
                  .Setup(c => c.TryGet(6987, out _akiSoraTitles))
                  .Returns(true);

            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.GetAllByType(It.IsAny<string>()))
                  .Returns(new List<SceneMapping>());

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByIds(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<AniListMedia>());
        }

        private void GivenStoredMappings(params SceneMapping[] mappings)
        {
            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.GetAllByType(AniListSceneMappingProvider.MappingType))
                  .Returns(mappings.ToList());
        }

        private void GivenCachedTitles(int id, params string[] titles)
        {
            var list = titles.ToList();

            Mocker.GetMock<IAniListTitleCache>()
                  .Setup(c => c.TryGet(id, out list))
                  .Returns(true);
        }

        [Test]
        public void should_emit_one_mapping_per_alternate_title()
        {
            GivenCachedTitles(10851, "Euphoria OVA");

            var mappings = Subject.GetSceneMappings();

            mappings.Should().HaveCount(3);

            var autumnSky = mappings.Single(m => m.Title == "Autumn Sky");
            autumnSky.SearchTerm.Should().Be("Autumn Sky");
            autumnSky.ParseTerm.Should().Be("autumnsky");
            autumnSky.TvdbId.Should().Be(6987);
            autumnSky.MappingId.Should().Be("anilist:6987:autumn sky");
            autumnSky.Type.Should().Be(AniListSceneMappingProvider.MappingType);
            autumnSky.SeasonNumber.Should().BeNull();
            autumnSky.SceneSeasonNumber.Should().BeNull();
            autumnSky.SceneOrigin.Should().BeNull();

            mappings.Single(m => m.Title == "Akisora").TvdbId.Should().Be(6987);
            mappings.Single(m => m.Title == "Euphoria OVA").TvdbId.Should().Be(10851);
        }

        [Test]
        public void should_reuse_stored_mappings_when_titles_are_not_cached()
        {
            GivenStoredMappings(new SceneMapping { TvdbId = 10851, Title = "Euphoria OVA", Type = AniListSceneMappingProvider.MappingType });

            var mappings = Subject.GetSceneMappings();

            mappings.Should().Contain(m => m.TvdbId == 10851 && m.Title == "Euphoria OVA");

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetMediaByIds(It.IsAny<IEnumerable<int>>()), Times.Never());
        }

        [Test]
        public void should_fetch_missing_titles_from_anilist_and_cache_them()
        {
            var media = new AniListMedia
            {
                Id = 10851,
                Title = new AniListTitle { Romaji = "euphoria", English = "Euphoria" },
                Synonyms = new List<string> { "Euphoria OVA", "ユーフォリア" }
            };

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByIds(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10851 }))))
                  .Returns(new List<AniListMedia> { media });

            var mappings = Subject.GetSceneMappings();

            mappings.Where(m => m.TvdbId == 10851).Select(m => m.Title).Should().BeEquivalentTo(new[] { "Euphoria OVA" });

            Mocker.GetMock<IAniListTitleCache>().Verify(c => c.Store(media), Times.Once());
        }

        [Test]
        public void should_skip_alternate_title_matching_another_series_main_title()
        {
            GivenCachedTitles(6987, "Autumn Sky", "Euphoria");

            var mappings = Subject.GetSceneMappings();

            mappings.Should().NotContain(m => m.Title == "Euphoria");
            mappings.Should().Contain(m => m.Title == "Autumn Sky");
        }

        [Test]
        public void should_keep_alternate_title_cleaning_to_own_main_title()
        {
            GivenCachedTitles(6987, "Aki Sora");

            var mappings = Subject.GetSceneMappings();

            mappings.Should().ContainSingle(m => m.Title == "Aki Sora" && m.ParseTerm == "akisora" && m.TvdbId == 6987);
        }

        [Test]
        public void should_drop_alternate_titles_shared_by_two_series()
        {
            GivenCachedTitles(6987, "Autumn Sky", "Shared Name");
            GivenCachedTitles(10851, "Shared Name", "Euphoria OVA");

            var mappings = Subject.GetSceneMappings();

            mappings.Should().NotContain(m => m.Title == "Shared Name");
            mappings.Select(m => m.Title).Should().BeEquivalentTo(new[] { "Autumn Sky", "Euphoria OVA" });
        }

        [Test]
        public void should_return_empty_when_library_is_empty()
        {
            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series>());

            Subject.GetSceneMappings().Should().BeEmpty();

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetMediaByIds(It.IsAny<IEnumerable<int>>()), Times.Never());
        }
    }

    [TestFixture]
    public class AniListSceneMappingServiceIntegrationFixture : CoreTest<SceneMappingService>
    {
        private List<SceneMapping> _stored;

        [SetUp]
        public void Setup()
        {
            _stored = new List<SceneMapping>();

            var series = new Series { Id = 1, TvdbId = 6987, Title = "Aki-Sora", CleanTitle = "akisora" };
            var titles = new List<string> { "Autumn Sky", "Akisora" };

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series> { series });

            Mocker.GetMock<IAniListTitleCache>()
                  .Setup(c => c.TryGet(6987, out titles))
                  .Returns(true);

            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.GetAllByType(It.IsAny<string>()))
                  .Returns(new List<SceneMapping>());

            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.InsertMany(It.IsAny<IList<SceneMapping>>()))
                  .Callback<IList<SceneMapping>>(m => _stored.AddRange(m));

            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.All())
                  .Returns(() => _stored);

            Mocker.SetConstant<IEnumerable<ISceneMappingProvider>>(new ISceneMappingProvider[] { Mocker.Resolve<AniListSceneMappingProvider>() });
        }

        [Test]
        public void should_expose_alternate_titles_as_scene_names_and_resolve_them_back()
        {
            Subject.Execute(new UpdateSceneMappingCommand());

            _stored.Should().HaveCount(2);
            _stored.Should().OnlyContain(m => m.Type == AniListSceneMappingProvider.MappingType);

            Subject.GetSceneNames(6987, new List<int> { 1 }, new List<int> { 1 }).Should().BeEquivalentTo(new[] { "Autumn Sky", "Akisora" });
            Subject.FindTvdbId("Autumn Sky", "[Group] Autumn Sky - 01 [1080p].mkv", -1).Should().Be(6987);
            Subject.FindTvdbId("autumn.sky", "autumn.sky.01.1080p.mkv", -1).Should().Be(6987);
            Subject.FindTvdbId("Something Else", "Something Else - 01", -1).Should().BeNull();
        }
    }
}
