using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Localization;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.AniList
{
    // Fork: shared shape of the fork's AniList lists (the upstream AniListImportBase is the OAuth user-list base). Entries that are already part of a library series are reported
    // under that series' root id so the sync skips them cheaply; new sequel entries resolve to their chain root when
    // they are added. The adult filter of the instance applies to every list.
    public abstract class AniListMediaImportBase<TSettings> : ImportListBase<TSettings>
        where TSettings : ImportListSettingsBase<TSettings>, new()
    {
        protected readonly IAniListGraphQlClient _client;
        protected readonly IAniListMetadataOptions _options;
        private readonly IAniListSeriesLookup _lookup;

        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(12);

        protected AniListMediaImportBase(IAniListGraphQlClient client,
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

        protected abstract List<AniListMedia> FetchMedia();

        public override ImportListFetchResult Fetch()
        {
            var items = new List<ImportListItemInfo>();
            var anyFailure = false;

            try
            {
                foreach (var entry in FetchMedia().Where(IsWanted))
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
                _logger.Warn(ex, "Failed to fetch AniList list {0}", Definition.Name);
                _importListStatusService.RecordFailure(Definition.Id);
            }

            return new ImportListFetchResult(CleanupListItems(items), anyFailure);
        }

        protected bool IsWanted(AniListMedia media)
        {
            if (!string.Equals(media.Type, "ANIME", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

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
    }
}
