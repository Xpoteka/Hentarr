using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation.Results;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.Test.TvTests
{
    // Fork: AniList entries must always be anime, regardless of the type sent by the add form.
    [TestFixture]
    public class AddSeriesForkFixture : CoreTest<AddSeriesService>
    {
        private Series _fakeSeries;

        [SetUp]
        public void Setup()
        {
            _fakeSeries = Builder<Series>.CreateNew()
                .With(s => s.Path = null)
                .With(s => s.SeriesType = SeriesTypes.Anime)
                .Build();

            Mocker.GetMock<IProvideSeriesInfo>()
                  .Setup(s => s.GetSeriesInfo(It.IsAny<int>(), It.IsAny<Language>(), It.IsAny<string>()))
                  .Returns(new Tuple<Series, List<Episode>>(_fakeSeries, new List<Episode>()));

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetSeriesFolder(It.IsAny<Series>(), null))
                  .Returns<Series, NamingConfig>((c, n) => c.Title);

            Mocker.GetMock<IAddSeriesValidator>()
                  .Setup(s => s.Validate(It.IsAny<Series>()))
                  .Returns(new ValidationResult());
        }

        [TestCase(SeriesTypes.Standard)]
        [TestCase(SeriesTypes.Daily)]
        [TestCase(SeriesTypes.Anime)]
        public void should_always_add_series_as_anime(SeriesTypes requested)
        {
            var newSeries = new Series
            {
                TvdbId = 10851,
                RootFolderPath = @"C:\Test\TV",
                Language = Language.English,
                SeriesType = requested
            };

            var series = Subject.AddSeries(newSeries);

            series.SeriesType.Should().Be(SeriesTypes.Anime);
        }
    }
}
