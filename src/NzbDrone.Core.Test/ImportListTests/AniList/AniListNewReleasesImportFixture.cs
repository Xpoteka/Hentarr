using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.ImportLists.AniList.NewReleases;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ImportListTests.AniList
{
    [TestFixture]
    public class AniListNewReleasesImportFixture : CoreTest<AniListNewReleasesImport>
    {
        private static AniListMedia Media(int id, string title, bool adult = true, string type = "ANIME")
        {
            return new AniListMedia { Id = id, Type = type, IsAdult = adult, Title = new AniListTitle { Romaji = title }, StartDate = new AniListFuzzyDate { Year = 2026, Month = 9, Day = 25 } };
        }

        [SetUp]
        public void Setup()
        {
            Subject.Definition = new ImportListDefinition
            {
                Id = 6,
                Name = "New hentai",
                Settings = new AniListNewReleasesSettings { MonthsBack = 3, IncludeUpcoming = true }
            };

            Mocker.GetMock<IAniListMetadataOptions>().SetupGet(o => o.AdultFilter).Returns(AniListAdultFilter.Adult);
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(It.IsAny<int>())).Returns((Series)null);

            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByStartDate(It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<AniListAdultFilter>()))
                  .Returns(new List<AniListMedia>
                  {
                      Media(213732, "Mujikaku na Osananajimi"),
                      Media(213686, "NTR Kishi"),
                      Media(999, "A manga", type: "MANGA"),
                      Media(1000, "Some TV Show", adult: false)
                  });
        }

        [Test]
        public void should_query_the_configured_window_including_upcoming()
        {
            var result = Subject.Fetch();

            result.AnyFailure.Should().BeFalse();
            result.Series.Select(s => s.AniListId).Should().BeEquivalentTo(new[] { 213732, 213686 });

            var expectedFrom = DateTime.UtcNow.Date.AddMonths(-3);

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetMediaByStartDate(expectedFrom, null, AniListAdultFilter.Adult), Times.Once());
            Mocker.GetMock<IImportListStatusService>().Verify(s => s.RecordSuccess(6), Times.Once());
        }

        [Test]
        public void should_cap_at_tomorrow_when_upcoming_is_excluded()
        {
            ((AniListNewReleasesSettings)Subject.Definition.Settings).IncludeUpcoming = false;

            Subject.Fetch();

            Mocker.GetMock<IAniListGraphQlClient>().Verify(c => c.GetMediaByStartDate(It.IsAny<DateTime>(), DateTime.UtcNow.Date.AddDays(1), AniListAdultFilter.Adult), Times.Once());
        }

        [Test]
        public void should_report_members_of_an_existing_chain_under_the_owning_series_id()
        {
            Mocker.GetMock<IAniListSeriesLookup>().Setup(l => l.FindByAniListId(213686)).Returns(new Series { Id = 1, TvdbId = 3479 });

            Subject.Fetch().Series.Single(s => s.AniListId == 213686).TvdbId.Should().Be(3479);
        }

        [Test]
        public void should_report_failure_without_wiping_the_list_when_anilist_is_unreachable()
        {
            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByStartDate(It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<AniListAdultFilter>()))
                  .Throws(new Exception("boom"));

            var result = Subject.Fetch();

            result.AnyFailure.Should().BeTrue();
            result.Series.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_fail_test_when_anilist_is_unreachable()
        {
            Mocker.GetMock<IAniListGraphQlClient>()
                  .Setup(c => c.GetMediaByStartDate(It.IsAny<DateTime>(), It.IsAny<DateTime?>(), It.IsAny<AniListAdultFilter>()))
                  .Throws(new Exception("boom"));

            Subject.Test().IsValid.Should().BeFalse();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_reject_months_back_outside_the_range()
        {
            new AniListNewReleasesSettings { MonthsBack = 0 }.Validate().IsValid.Should().BeFalse();
            new AniListNewReleasesSettings { MonthsBack = 1201 }.Validate().IsValid.Should().BeFalse();
            new AniListNewReleasesSettings { MonthsBack = 6 }.Validate().IsValid.Should().BeTrue();
        }
    }
}
