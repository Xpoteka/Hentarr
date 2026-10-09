using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.ImportLists.AniList.NewReleases
{
    // Fork: adds every anime entry on AniList that started in the last N months (and, optionally, is still to come),
    // whatever the studio. The instance's adult filter decides what "every" means.
    public class AniListNewReleasesImport : AniListMediaImportBase<AniListNewReleasesSettings>
    {
        public override string Name => "AniList New Releases";

        public AniListNewReleasesImport(IAniListGraphQlClient client,
                                       IAniListMetadataOptions options,
                                       IAniListSeriesLookup lookup,
                                       IImportListStatusService importListStatusService,
                                       IConfigService configService,
                                       IParsingService parsingService,
                                       ILocalizationService localizationService,
                                       Logger logger)
            : base(client, options, lookup, importListStatusService, configService, parsingService, localizationService, logger)
        {
        }

        protected override List<AniListMedia> FetchMedia()
        {
            var today = DateTime.UtcNow.Date;
            var from = today.AddMonths(-Settings.MonthsBack);
            var to = Settings.IncludeUpcoming ? (DateTime?)null : today.AddDays(1);

            return _client.GetMediaByStartDate(from, to, _options.AdultFilter);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            try
            {
                // One page is enough to prove the query shape and the connection
                _client.GetMediaByStartDate(DateTime.UtcNow.Date.AddDays(-7), DateTime.UtcNow.Date.AddDays(1), _options.AdultFilter);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to test the AniList new releases list");
                failures.Add(new ValidationFailure(string.Empty, "Unable to reach AniList: " + ex.Message));
            }
        }
    }
}
