using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MetadataSource.AniList
{
    [TestFixture]
    public class AniListChainResolverFixture : CoreTest<AniListChainResolver>
    {
        private Dictionary<int, AniListMedia> _media;
        private int _getMediaCalls;
        private int _getMediaByIdsCalls;

        private AniListMedia Load(string file)
        {
            return Json.Deserialize<AniListResponse<AniListMediaData>>(ReadAllText($"Files/AniList/{file}")).Data.Media;
        }

        [SetUp]
        public void Setup()
        {
            // Recorded from AniList: Asagi -> SEQUEL Ingrid (different title), SEQUEL Asagi 2, SIDE_STORY Bonus Video (SPECIAL);
            // Asagi 2 -> PREQUEL Asagi, SEQUEL Asagi 3; Asagi 3 -> PREQUEL Asagi 2 only; Ingrid -> PREQUEL Asagi, SIDE_STORY Ingrid: Re
            _media = new[] { "media_3479_taimanin_asagi.json", "media_21401_taimanin_asagi_2.json", "media_97854_taimanin_asagi_3.json", "media_8837_bonus_video.json", "media_6590_makai_kishi_ingrid.json" }
                     .Select(Load)
                     .ToDictionary(m => m.Id);

            _getMediaCalls = 0;
            _getMediaByIdsCalls = 0;

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMedia(It.IsAny<int>()))
                  .Returns<int>(id =>
                  {
                      _getMediaCalls++;
                      return _media[id];
                  });

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByIds(It.IsAny<IEnumerable<int>>()))
                  .Returns<IEnumerable<int>>(ids =>
                  {
                      _getMediaByIdsCalls++;
                      return ids.Where(_media.ContainsKey).Select(id => _media[id]).ToList();
                  });

            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(It.IsAny<int>())).Returns((Series)null);
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.AllAniListIds(It.IsAny<Series>())).Returns(new HashSet<int>());
        }

        [Test]
        public void should_resolve_chain_from_root()
        {
            var chain = Subject.ResolveForNewId(3479, out var owner);

            owner.Should().BeNull();
            chain.Seasons.Select(m => m.Id).Should().Equal(3479, 21401, 97854);
            chain.Specials.Select(m => m.Id).Should().Equal(8837);
            chain.IsSingle.Should().BeFalse();
            chain.SeasonNumberOf(97854).Should().Be(3);
            chain.SeasonNumberOf(8837).Should().Be(0);
        }

        [Test]
        public void should_resolve_same_chain_from_a_middle_entry()
        {
            var chain = Subject.ResolveForNewId(21401, out _);

            chain.Root.Id.Should().Be(3479);
            chain.Seasons.Select(m => m.Id).Should().Equal(3479, 21401, 97854);
        }

        [Test]
        public void should_keep_entries_with_another_title_out_of_the_chain()
        {
            var chain = Subject.ResolveForNewId(3479, out _);

            chain.All.Select(m => m.Id).Should().NotContain(6590);
        }

        [Test]
        public void should_resolve_ingrid_as_its_own_chain()
        {
            var chain = Subject.ResolveForNewId(6590, out _);

            chain.Root.Id.Should().Be(6590);
            chain.Seasons.Select(m => m.Id).Should().NotContain(3479);
        }

        [Test]
        public void should_use_one_request_per_level()
        {
            Subject.ResolveForNewId(3479, out _);

            _getMediaCalls.Should().Be(1);

            // level 1 (Asagi 2), level 2 (Asagi 3), level 3 (the Toraware spin-off listed by Asagi 3), specials
            _getMediaByIdsCalls.Should().Be(4);
            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetRelations(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_reuse_a_chain_resolved_moments_ago_on_a_forced_refresh()
        {
            var chain = Subject.ResolveForNewId(21401, out _);
            var series = new Series { Id = 4, TvdbId = 3479, AniListIds = chain.All.Select(m => m.Id).ToHashSet() };

            Subject.ResolveForSeries(series, true).Should().BeSameAs(chain);
            _getMediaCalls.Should().Be(1);

            chain.ResolvedAt = System.DateTime.UtcNow.AddHours(-1);

            Subject.ResolveForSeries(series, true).Should().NotBeSameAs(chain);
            _getMediaCalls.Should().Be(2);
        }

        [Test]
        public void should_not_reuse_a_recent_chain_rooted_elsewhere_on_refresh()
        {
            var chain = Subject.ResolveForNewId(21401, out _);
            var legacySequel = new Series { Id = 5, TvdbId = 21401, AniListIds = new HashSet<int> { 21401 } };

            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.AllAniListIds(legacySequel)).Returns(new HashSet<int> { 3479 });

            var refreshed = Subject.ResolveForSeries(legacySequel, true);

            refreshed.Should().NotBeSameAs(chain);
            refreshed.Root.Id.Should().Be(21401);
            refreshed.Seasons.Select(m => m.Id).Should().NotContain(3479);
        }

        [Test]
        public void should_return_owner_when_an_entry_already_belongs_to_a_series()
        {
            var existing = new Series { Id = 4, TvdbId = 3479, AniListIds = new HashSet<int> { 3479, 21401 } };

            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(3479)).Returns(existing);
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(21401)).Returns(existing);

            var chain = Subject.ResolveForNewId(97854, out var owner);

            chain.Should().BeNull();
            owner.Should().Be(existing);
        }

        [Test]
        public void should_never_reroot_an_existing_series()
        {
            var existing = new Series { Id = 4, TvdbId = 21401, Title = "Taimanin Asagi 2", AniListIds = new HashSet<int> { 21401 } };

            var chain = Subject.ResolveForSeries(existing, true);

            chain.Root.Id.Should().Be(21401);
            chain.Seasons.Select(m => m.Id).Should().Equal(21401, 97854);
            chain.Seasons.Select(m => m.Id).Should().NotContain(3479);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_enter_entries_owned_by_another_series()
        {
            var existing = new Series { Id = 4, TvdbId = 3479, AniListIds = new HashSet<int> { 3479 } };

            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.AllAniListIds(existing)).Returns(new HashSet<int> { 97854 });

            var chain = Subject.ResolveForSeries(existing, true);

            chain.Seasons.Select(m => m.Id).Should().Equal(3479, 21401);
        }

        [Test]
        public void should_cache_chain_under_every_member_id()
        {
            Subject.ResolveForNewId(3479, out _);

            Subject.TryGetCached(97854, out var chain).Should().BeTrue();
            chain.Root.Id.Should().Be(3479);
            Subject.TryGetCached(8837, out _).Should().BeTrue();
            Subject.TryGetCached(6590, out _).Should().BeFalse();

            Subject.ResolveForNewId(21401, out _);
            _getMediaCalls.Should().Be(1);
        }

        [Test]
        public void should_store_fetched_entries_in_the_title_cache()
        {
            Subject.ResolveForNewId(3479, out _);

            Mocker.GetMock<IAniListTitleCache>().Verify(c => c.Store(It.Is<AniListMedia>(m => m.Id == 97854)), Times.Once());
            Mocker.GetMock<IAniListTitleCache>().Verify(c => c.Store(It.Is<AniListMedia>(m => m.Id == 8837)), Times.Once());
        }

        [TestCase("Taimanin Asagi 2", "Taimanin Asagi")]
        [TestCase("Taimanin Asagi: Toraware no Niku Ningyou", "Taimanin Asagi")]
        [TestCase("PRETTY×CATION 2 THE ANIMATION", "PRETTY×CATION")]
        [TestCase("PRETTY×CATION THE ANIMATION", "PRETTY×CATION")]
        [TestCase("Makai Kishi Ingrid: Re", "Makai Kishi Ingrid")]
        [TestCase("Joshi Luck! 2nd Season", "Joshi Luck!")]
        [TestCase("Yu-No", "Yu-No")]
        public void should_derive_base_title(string title, string expected)
        {
            AniListChainResolver.GetBaseTitle(title).Should().Be(expected);
        }

        [TestCase("Taimanin Asagi", "Taimanin Asagi 2", true)]
        [TestCase("Taimanin Asagi", "Taimanin Asagi: Toraware no Niku Ningyou", true)]
        [TestCase("Taimanin Asagi", "Makai Kishi Ingrid", false)]
        [TestCase("Taimanin Asagi", "Taimanin Yukikaze", false)]
        [TestCase("Makai Kishi Ingrid", "Makai Kishi Ingrid: Re", true)]
        public void should_decide_title_membership(string baseTitle, string candidate, bool expected)
        {
            AniListChainResolver.TitleBelongsTo(baseTitle, candidate).Should().Be(expected);
        }
    }
}
