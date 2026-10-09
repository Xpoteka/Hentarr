using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.ImportLists.AniList.Studio
{
    // Fork: adds every anime entry of an AniList studio that passes the adult filter.
    public class AniListStudioImport : AniListMediaImportBase<AniListStudioSettings>
    {
        public override string Name => "AniList Studio";

        public AniListStudioImport(IAniListGraphQlClient client,
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
            return _client.GetMediaByStudio(Settings.StudioName.Trim(), Settings.MainStudioOnly);
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            if (Settings.StudioName.IsNullOrWhiteSpace())
            {
                failures.Add(new ValidationFailure("StudioName", "Studio name is required"));
                return;
            }

            try
            {
                var studio = _client.GetStudio(Settings.StudioName.Trim());

                if (studio == null)
                {
                    failures.Add(new ValidationFailure("StudioName", "Studio was not found on AniList"));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to test AniList studio {0}", Settings.StudioName);
                failures.Add(new ValidationFailure(string.Empty, "Unable to reach AniList: " + ex.Message));
            }
        }
    }
}
