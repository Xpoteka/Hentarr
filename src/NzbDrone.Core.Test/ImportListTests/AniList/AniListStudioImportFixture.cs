using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.Studio;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests.AniList
{
    [TestFixture]
    public class AniListStudioImportFixture : CoreTest<AniListStudioImport>
    {
        private static AniListMedia Media(int id, string title, bool adult = true, int? mal = null, int? year = null)
        {
            return new AniListMedia
            {
                Id = id,
                IdMal = mal,
                Type = "ANIME",
                IsAdult = adult,
                Title = new AniListTitle { Romaji = title },
                StartDate = year.HasValue ? new AniListFuzzyDate { Year = year } : null
            };
        }

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ImportListDefinition
            {
                Id = 4,
                Name = "Pink Pineapple",
                Settings = new AniListStudioSettings { StudioName = " Pink Pineapple ", MainStudioOnly = true }
            };

            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AdultFilter).Returns(AniListAdultFilter.Adult);
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(It.IsAny<int>())).Returns((Series)null);

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByStudio("Pink Pineapple", true))
                  .Returns(new List<AniListMedia>
                  {
                      Media(3479, "Taimanin Asagi", mal: 7054, year: 2007),
                      Media(21401, "Taimanin Asagi 2", year: 2013),
                      Media(1000, "Some TV Show", adult: false, year: 2001)
                  });
        }

        [Test]
        public void should_map_studio_entries_to_list_items()
        {
            var result = Subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Should().HaveCount(2);

            var asagi = result.Series.Single(s => s.AniListId == 3479);
            asagi.TvdbId.Should().Be(3479);
            asagi.MalId.Should().Be(7054);
            asagi.Title.Should().Be("Taimanin Asagi");
            asagi.Year.Should().Be(2007);
            asagi.ImportListId.Should().Be(4);

            Mocker.GetMock<IImportListStatusService>().Verify(s => s.RecordSuccess(4), Times.Once());
        }

        [Test]
        public void should_report_members_of_an_existing_chain_under_the_owning_series_id()
        {
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(21401)).Returns(new Series { Id = 1, TvdbId = 3479 });

            var result = Subject.Fetch();

            result.Series.Single(s => s.AniListId == 21401).TvdbId.Should().Be(3479);
        }

        [Test]
        public void should_include_non_adult_entries_when_the_filter_allows_all()
        {
            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AdultFilter).Returns(AniListAdultFilter.All);

            Subject.Fetch().Series.Should().HaveCount(3);
        }

        [Test]
        public void should_report_failure_without_wiping_the_list_when_anilist_is_unreachable()
        {
            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByStudio(It.IsAny<string>(), It.IsAny<bool>()))
                  .Throws(new Exception("boom"));

            var result = Subject.Fetch();

            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();

            Mocker.GetMock<IImportListStatusService>().Verify(s => s.RecordFailure(4, It.IsAny<TimeSpan>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_fail_test_when_studio_is_unknown()
        {
            Mocker.GetMock<IAniListGraphQlClient>().Setup(c => c.GetStudio("Pink Pineapple")).Returns((AniListStudioResource)null);

            var result = Subject.Test();

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.PropertyName == "StudioName");
        }

        [Test]
        public void should_pass_test_when_studio_exists()
        {
            Mocker.GetMock<IAniListGraphQlClient>().Setup(c => c.GetStudio("Pink Pineapple")).Returns(new AniListStudioResource { Id = 1, Name = "Pink Pineapple" });

            Subject.Test().IsValid.Should().BeTrue();
        }
    }
}
