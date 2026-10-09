using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.AniList.Studio
{
    // Fork: adds every anime entry of an AniList studio that passes the adult filter. Entries that are already part
    // of a library series are reported under that series' root id so the sync skips them cheaply; new sequel entries
    // resolve to their chain root when they are added.
    public class AniListStudioImport : ImportListBase<AniListStudioSettings>
    {
        private readonly IAniListGraphQlClient _client;
        private readonly IAniListMetadataOptions _options;
        private readonly IAniListSeriesLookup _lookup;

        public override string Name => "AniList Studio";
        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(12);

        public AniListStudioImport(IAniListGraphQlClient client,
                                   IAniListMetadataOptions options,
                                   IAniListSeriesLookup lookup,
                                   IImportListStatusService importListStatusService,
                                   IConfigService configService,
                                   IParsingService parsingService,
                                   ILocalizationService localizationService,
                                   Logger logger)
            : base(importListStatusService, configService, parsingService, localizationService, logger)
        {
            _client = client;
            _options = options;
            _lookup = lookup;
        }

        public override ImportListFetchResult Fetch()
        {
            var items = new List<ImportListItemInfo>();
            var anyFailure = false;

            try
            {
                var media = _client.GetMediaByStudio(Settings.StudioName.Trim(), Settings.MainStudioOnly);

                foreach (var entry in media.Where(IsWanted))
                {
                    var owner = _lookup.FindByAniListId(entry.Id);

                    items.Add(new ImportListItemInfo
                    {
                        TvdbId = owner?.TvdbId ?? entry.Id,
                        AniListId = entry.Id,
                        MalId = entry.IdMal ?? 0,
                        Title = AniListMapper.GetMainTitle(entry),
                        Year = entry.StartDate?.Year ?? 0
                    });
                }

                _importListStatusService.RecordSuccess(Definition.Id);
            }
            catch (Exception ex)
            {
                anyFailure = true;
                _logger.Warn(ex, "Failed to fetch AniList studio list {0} ({1})", Definition.Name, Settings.StudioName);
                _importListStatusService.RecordFailure(Definition.Id);
            }

            return new ImportListFetchResult(CleanupListItems(items), anyFailure);
        }

        private bool IsWanted(NzbDrone.Core.MetadataSource.AniList.Resource.AniListMedia media)
        {
            switch (_options.AdultFilter)
            {
                case AniListAdultFilter.Adult:
                    return media.IsAdult;
                case AniListAdultFilter.NonAdult:
                    return !media.IsAdult;
                default:
                    return true;
            }
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            return new { };
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
