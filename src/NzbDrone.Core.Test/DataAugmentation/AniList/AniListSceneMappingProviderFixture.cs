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
        private AniListMedia _akiSoraMedia;
        private AniListChain _nullChain;

        private static AniListMedia Media(int id, string romaji, string english = null, params string[] synonyms)
        {
            return new AniListMedia { Id = id, Type = "ANIME", Title = new AniListTitle { Romaji = romaji, English = english }, Synonyms = synonyms.ToList() };
        }

        [SetUp]
        public void Setup()
        {
            _akiSora = new Series { Id = 1, TvdbId = 6987, Title = "Aki-Sora", CleanTitle = "akisora", AniListIds = new HashSet<int> { 6987 }, Seasons = new List<Season> { new Season { SeasonNumber = 1 } } };
            _euphoria = new Series { Id = 2, TvdbId = 10851, Title = "euphoria", CleanTitle = "euphoria", AniListIds = new HashSet<int> { 10851 }, Seasons = new List<Season> { new Season { SeasonNumber = 1 } } };
            _akiSoraMedia = Media(6987, "Aki-Sora", null, "Autumn Sky", "Akisora");
            _nullChain = null;

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series> { _akiSora, _euphoria });

            Mocker.GetMock<IAniListChainResolver>()
                  .Setup(r => r.TryGetCached(It.IsAny<int>(), out _nullChain))
                  .Returns(false);

            Mocker.GetMock<IAniListTitleCache>()
                  .Setup(c => c.TryGetMedia(6987, out _akiSoraMedia))
                  .Returns(true);

            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.GetAllByType(It.IsAny<string>()))
                  .Returns(new List<SceneMapping>());

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByIds(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<AniListMedia>());
        }

        private void GivenCachedMedia(int id, AniListMedia media)
        {
            Mocker.GetMock<IAniListTitleCache>()
                  .Setup(c => c.TryGetMedia(id, out media))
                  .Returns(true);
        }

        private void GivenChain(int rootId, AniListChain chain)
        {
            Mocker.GetMock<IAniListChainResolver>()
                  .Setup(r => r.TryGetCached(rootId, out chain))
                  .Returns(true);
        }

        [Test]
        public void should_emit_one_mapping_per_alternate_title()
        {
            GivenCachedMedia(10851, Media(10851, "euphoria", null, "Euphoria OVA"));

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
        public void should_emit_season_mappings_for_a_chain()
        {
            var chain = new AniListChain();
            chain.Seasons.Add(Media(3479, "Taimanin Asagi", "Anti-Demon Ninja Asagi"));
            chain.Seasons.Add(Media(21401, "Taimanin Asagi 2"));
            chain.Seasons.Add(Media(97854, "Taimanin Asagi 3"));
            chain.Specials.Add(Media(8837, "Taimanin Asagi: Bonus Video"));

            var taimanin = new Series { Id = 3, TvdbId = 3479, Title = "Taimanin Asagi", CleanTitle = "taimaninasagi", AniListIds = new HashSet<int> { 3479, 21401, 97854, 8837 } };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { taimanin });
            GivenChain(3479, chain);

            var mappings = Subject.GetSceneMappings();

            mappings.Should().OnlyContain(m => m.TvdbId == 3479 && m.SeasonNumber == null);
            mappings.Select(m => (m.Title, m.SceneSeasonNumber)).Should().BeEquivalentTo(new[]
            {
                ("Taimanin Asagi", (int?)1),
                ("Anti-Demon Ninja Asagi", (int?)1),
                ("Taimanin Asagi 2", (int?)2),
                ("Taimanin Asagi 3", (int?)3)
            });
            mappings.Single(m => m.Title == "Taimanin Asagi 2").MappingId.Should().Be("anilist:3479:s2:taimanin asagi 2");
            mappings.Single(m => m.Title == "Taimanin Asagi").MappingId.Should().Be("anilist:3479:s1:taimanin asagi");
        }

        [Test]
        public void should_collapse_title_shared_by_several_seasons_to_a_series_wide_mapping()
        {
            var chain = new AniListChain();
            chain.Seasons.Add(Media(1, "Root Title", null, "Shared"));
            chain.Seasons.Add(Media(2, "Root Title 2", null, "Shared"));

            var series = new Series { Id = 3, TvdbId = 1, Title = "Root Title", CleanTitle = "roottitle", AniListIds = new HashSet<int> { 1, 2 } };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { series });
            GivenChain(1, chain);

            var mappings = Subject.GetSceneMappings();

            var shared = mappings.Single(m => m.Title == "Shared");
            shared.SceneSeasonNumber.Should().BeNull();
            shared.MappingId.Should().Be("anilist:1:shared");
        }

        [Test]
        public void should_reuse_stored_mappings_when_titles_are_not_cached()
        {
            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.GetAllByType(AniListSceneMappingProvider.MappingType))
                  .Returns(new List<SceneMapping>
                  {
                      new SceneMapping { TvdbId = 10851, Title = "Euphoria OVA", Type = AniListSceneMappingProvider.MappingType },
                      new SceneMapping { TvdbId = 10851, Title = "Euphoria 2", SceneSeasonNumber = 2, Type = AniListSceneMappingProvider.MappingType }
                  });

            var mappings = Subject.GetSceneMappings();

            mappings.Should().Contain(m => m.TvdbId == 10851 && m.Title == "Euphoria OVA" && m.SceneSeasonNumber == null);
            mappings.Should().Contain(m => m.TvdbId == 10851 && m.Title == "Euphoria 2" && m.SceneSeasonNumber == 2);

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetMediaByIds(It.IsAny<IEnumerable<int>>()), Times.Never());
        }

        [Test]
        public void should_fetch_missing_titles_from_anilist_and_cache_them()
        {
            var media = Media(10851, "euphoria", "Euphoria", "Euphoria OVA", "ユーフォリア");

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByIds(It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 10851 }))))
                  .Returns(new List<AniListMedia> { media });

            var mappings = Subject.GetSceneMappings();

            mappings.Where(m => m.TvdbId == 10851).Select(m => m.Title).Should().BeEquivalentTo(new[] { "Euphoria OVA" });

            Mocker.GetMock<IAniListTitleCache>().Verify(c => c.Store(media), Times.Once());
        }

        [Test]
        public void should_match_fetched_chain_members_to_seasons_by_season_title()
        {
            var taimanin = new Series
            {
                Id = 3,
                TvdbId = 3479,
                Title = "Taimanin Asagi",
                CleanTitle = "taimaninasagi",
                AniListIds = new HashSet<int> { 3479, 21401 },
                Seasons = new List<Season> { new Season { SeasonNumber = 1, Title = "Taimanin Asagi" }, new Season { SeasonNumber = 2, Title = "Taimanin Asagi 2" } }
            };

            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { taimanin });
            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByIds(It.IsAny<IEnumerable<int>>()))
                  .Returns(new List<AniListMedia> { Media(3479, "Taimanin Asagi"), Media(21401, "Taimanin Asagi 2", "Taimanin Asagi II") });

            var mappings = Subject.GetSceneMappings();

            mappings.Should().Contain(m => m.Title == "Taimanin Asagi 2" && m.SceneSeasonNumber == 2);
            mappings.Should().Contain(m => m.Title == "Taimanin Asagi II" && m.SceneSeasonNumber == 2);
            mappings.Should().Contain(m => m.Title == "Taimanin Asagi" && m.SceneSeasonNumber == 1);
        }

        [Test]
        public void should_skip_alternate_title_matching_another_series_main_title()
        {
            GivenCachedMedia(6987, Media(6987, "Aki-Sora", null, "Autumn Sky", "Euphoria"));

            var mappings = Subject.GetSceneMappings();

            mappings.Should().NotContain(m => m.Title == "Euphoria");
            mappings.Should().Contain(m => m.Title == "Autumn Sky");
        }

        [Test]
        public void should_keep_alternate_title_cleaning_to_own_main_title()
        {
            GivenCachedMedia(6987, Media(6987, "Aki-Sora", null, "Aki Sora"));

            var mappings = Subject.GetSceneMappings();

            mappings.Should().ContainSingle(m => m.Title == "Aki Sora" && m.ParseTerm == "akisora" && m.TvdbId == 6987);
        }

        [Test]
        public void should_drop_alternate_titles_shared_by_two_series()
        {
            GivenCachedMedia(6987, Media(6987, "Aki-Sora", null, "Autumn Sky", "Shared Name"));
            GivenCachedMedia(10851, Media(10851, "euphoria", null, "Shared Name", "Euphoria OVA"));

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

            var series = new Series { Id = 1, TvdbId = 6987, Title = "Aki-Sora", CleanTitle = "akisora", AniListIds = new HashSet<int> { 6987 }, Seasons = new List<Season> { new Season { SeasonNumber = 1 } } };
            var media = new AniListMedia { Id = 6987, Title = new AniListTitle { Romaji = "Aki-Sora", English = "Autumn Sky" }, Synonyms = new List<string> { "Akisora" } };
            AniListChain nullChain = null;

            Mocker.GetMock<ISeriesService>()
                  .Setup(s => s.GetAllSeries())
                  .Returns(new List<Series> { series });

            Mocker.GetMock<IAniListChainResolver>()
                  .Setup(r => r.TryGetCached(It.IsAny<int>(), out nullChain))
                  .Returns(false);

            Mocker.GetMock<IAniListTitleCache>()
                  .Setup(c => c.TryGetMedia(6987, out media))
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
