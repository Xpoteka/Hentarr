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
        private Series _nullSeries;
        private AniListChain _nullChain;

        private static AniListRelationEdge Edge(string type, int id, string title, bool adult = true, string mediaType = "ANIME", string format = "OVA")
        {
            return new AniListRelationEdge
            {
                RelationType = type,
                Node = new AniListMedia { Id = id, Type = mediaType, IsAdult = adult, Format = format, Title = new AniListTitle { Romaji = title } }
            };
        }

        [SetUp]
        public void Setup()
        {
            _added = new List<Series>();
            _nullSeries = null;
            _nullChain = null;

            _parent = new Series
            {
                Id = 5,
                TvdbId = 3479,
                AniListIds = new HashSet<int> { 3479, 21401, 8837 },
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

            // No cached chain: relations are fetched per member id
            Mocker.GetMock<IAniListChainResolver>().Setup(r => r.TryGetCached(It.IsAny<int>(), out _nullChain)).Returns(false);

            Mocker.GetMock<IAniListGraphQlClient>().Setup(c => c.GetRelations(It.IsAny<int>())).Returns(new List<AniListRelationEdge>());
            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetRelations(3479))
                  .Returns(new List<AniListRelationEdge>
                  {
                      Edge("SEQUEL", 6590, "Makai Kishi Ingrid"),
                      Edge("SIDE_STORY", 8837, "Taimanin Asagi: Bonus Video", format: "SPECIAL"),
                      Edge("SEQUEL", 21401, "Taimanin Asagi 2"),
                      Edge("ALTERNATIVE", 93291, "Taimanin Asagi (novel)", mediaType: "MANGA"),
                      Edge("ALTERNATIVE", 4242, "Taimanin TV cut", adult: false),
                      Edge("CHARACTER", 5151, "Some crossover"),
                      Edge("SIDE_STORY", 9999, "Another Special", format: "SPECIAL")
                  });

            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(It.IsAny<int>())).Returns((Series)null);
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.AllAniListIds(null)).Returns(new HashSet<int> { 3479, 21401, 8837 });
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByTvdbId(It.IsAny<int>())).Returns((ImportListExclusion)null);
            Mocker.GetMock<IRootFolderService>().Setup(s => s.GetBestRootFolderPath("/hentai/Taimanin Asagi")).Returns("/hentai");
            Mocker.GetMock<IAniListGraphQlClient>().Setup(c => c.Search(It.IsAny<string>(), It.IsAny<AniListAdultFilter>())).Returns(new List<AniListMedia>());

            // Every unknown related entry resolves to a chain of its own, rooted at itself
            Mocker.GetMock<IAniListChainResolver>()
                  .Setup(r => r.ResolveForNewId(It.IsAny<int>(), out _nullSeries))
                  .Returns<int, Series>((id, _) => AniListChain.Single(new AniListMedia { Id = id, Type = "ANIME", IsAdult = true, Title = new AniListTitle { Romaji = "Entry " + id } }));

            Mocker.GetMock<IAddSeriesService>()
                  .Setup(s => s.AddSeries(It.IsAny<Series>()))
                  .Returns<Series>(s =>
                  {
                      _added.Add(s);
                      return s;
                  });
        }

        [Test]
        public void should_add_related_stories_but_not_chain_members_or_specials()
        {
            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            // 21401 and 8837 are part of the parent's chain, 9999 is a special, the manga and the non-adult entry are filtered
            _added.Select(s => s.TvdbId).Should().BeEquivalentTo(new[] { 6590 });

            var ingrid = _added.Single();
            ingrid.RootFolderPath.Should().Be("/hentai");
            ingrid.QualityProfileId.Should().Be(3);
            ingrid.Monitored.Should().BeTrue();
            ingrid.SeriesType.Should().Be(SeriesTypes.Anime);
            ingrid.Tags.Should().BeEquivalentTo(new[] { 7 });
            ingrid.AddOptions.SearchForMissingEpisodes.Should().BeTrue();
        }

        [Test]
        public void should_add_a_related_entry_by_its_chain_root()
        {
            var root = new AniListMedia { Id = 6000, Type = "ANIME", IsAdult = true, Title = new AniListTitle { Romaji = "Makai Kishi Ingrid Root" } };
            var chain = new AniListChain();
            chain.Seasons.Add(root);
            chain.Seasons.Add(new AniListMedia { Id = 6590, Type = "ANIME", IsAdult = true, Title = new AniListTitle { Romaji = "Makai Kishi Ingrid" } });

            Mocker.GetMock<IAniListChainResolver>().Setup(r => r.ResolveForNewId(6590, out _nullSeries)).Returns(chain);

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Should().ContainSingle(s => s.TvdbId == 6000 && s.Title == "Makai Kishi Ingrid Root");
        }

        [Test]
        public void should_skip_entries_already_in_library_or_excluded()
        {
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(6590)).Returns(new Series { Id = 9, TvdbId = 6590 });

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Should().BeEmpty();

            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(6590)).Returns((Series)null);
            Mocker.GetMock<IImportListExclusionService>().Setup(s => s.FindByTvdbId(6590)).Returns(new ImportListExclusion { TvdbId = 6590 });

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Should().BeEmpty();
        }

        [Test]
        public void should_skip_entries_whose_chain_is_owned_by_another_series()
        {
            var owner = new Series { Id = 11, TvdbId = 6000 };

            Mocker.GetMock<IAniListChainResolver>().Setup(r => r.ResolveForNewId(6590, out owner)).Returns((AniListChain)null);

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Should().BeEmpty();
        }

        [Test]
        public void should_use_cached_chain_relations_when_available()
        {
            var chain = new AniListChain();
            chain.Seasons.Add(new AniListMedia { Id = 3479, Type = "ANIME", Title = new AniListTitle { Romaji = "Taimanin Asagi" }, Relations = new AniListRelationConnection { Edges = new List<AniListRelationEdge> { Edge("SPIN_OFF", 7777, "A Spin Off") } } });
            chain.Seasons.Add(new AniListMedia { Id = 21401, Type = "ANIME", Title = new AniListTitle { Romaji = "Taimanin Asagi 2" }, Relations = new AniListRelationConnection { Edges = new List<AniListRelationEdge> { Edge("SPIN_OFF", 8888, "Another Spin Off") } } });

            Mocker.GetMock<IAniListChainResolver>().Setup(r => r.TryGetCached(3479, out chain)).Returns(true);

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Select(s => s.TvdbId).Should().BeEquivalentTo(new[] { 7777, 8888 });
            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetRelations(It.IsAny<int>()), Times.Never());
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
            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AdultFilter).Returns(AniListAdultFilter.All);
            Mocker.GetMock<IAddSeriesService>()
                  .Setup(s => s.AddSeries(It.Is<Series>(x => x.TvdbId == 6590)))
                  .Throws(new ValidationException(new List<ValidationFailure> { new ValidationFailure("Path", "exists") }));

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Select(s => s.TvdbId).Should().BeEquivalentTo(new[] { 4242 });
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_add_entries_that_link_back_to_the_library()
        {
            var yukikaze = new AniListMedia
            {
                Id = 20860,
                Type = "ANIME",
                IsAdult = true,
                Title = new AniListTitle { Romaji = "Taimanin Yukikaze" },
                Relations = new AniListRelationConnection { Edges = new List<AniListRelationEdge> { Edge("ALTERNATIVE", 3479, "Taimanin Asagi") } }
            };
            var unrelated = new AniListMedia
            {
                Id = 777,
                Type = "ANIME",
                IsAdult = true,
                Title = new AniListTitle { Romaji = "Taimanin Something Else" },
                Relations = new AniListRelationConnection { Edges = new List<AniListRelationEdge> { Edge("SEQUEL", 999, "Elsewhere") } }
            };
            var otherName = new AniListMedia { Id = 778, Type = "ANIME", IsAdult = true, Title = new AniListTitle { Romaji = "Not In Franchise" } };

            Mocker.GetMock<IAniListGraphQlClient>().Setup(c => c.Search("Taimanin", AniListAdultFilter.Adult)).Returns(new List<AniListMedia> { yukikaze, unrelated, otherName });

            Subject.HandleAsync(new SeriesAddedEvent(_parent));

            _added.Select(s => s.TvdbId).Should().Contain(20860);
            _added.Select(s => s.TvdbId).Should().NotContain(777);
            _added.Select(s => s.TvdbId).Should().NotContain(778);
            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetRelations(20860), Times.Never());
        }

        [TestCase("Taimanin Asagi", "Taimanin")]
        [TestCase("Pure x Holic: Junketsu Otome", "Pure")]
        [TestCase("Makai Kishi Ingrid: Re", "Makai")]
        [TestCase("JK Bitch ni Shiboraretai", "JK Bitch")]
        [TestCase("", null)]
        public void should_derive_franchise_term(string title, string expected)
        {
            AniListRelatedSeriesService.GetFranchiseTerm(title).Should().Be(expected);
        }

        [Test]
        public void should_expand_whole_library_on_command()
        {
            Mocker.GetMock<ISeriesService>().Setup(s => s.GetAllSeries()).Returns(new List<Series> { _parent });

            Subject.Execute(new AddRelatedSeriesCommand());

            _added.Should().HaveCount(1);
        }
    }
}
