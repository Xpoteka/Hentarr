using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.AniDb.Catalog;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.AniDbStudio
{
    public class AniDbStudioImport : ImportListBase<AniDbStudioSettings>
    {
        private readonly IAniDbCatalogService _catalogService;

        public AniDbStudioImport(IAniDbCatalogService catalogService,
                                 IImportListStatusService importListStatusService,
                                 IConfigService configService,
                                 IParsingService parsingService,
                                 Logger logger)
            : base(importListStatusService, configService, parsingService, logger)
        {
            _catalogService = catalogService;
        }

        public override string Name => "AniDB Studio";

        public override ImportListType ListType => ImportListType.Other;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromHours(6);

        public override IList<ImportListItemInfo> Fetch()
        {
            var series = new List<ImportListItemInfo>();

            try
            {
                var works = _catalogService.GetStudioWorks(Settings.Studio);

                foreach (var work in works)
                {
                    if (Settings.RestrictedOnly && !work.Restricted)
                    {
                        continue;
                    }

                    series.Add(new ImportListItemInfo
                    {
                        TpdbSiteId = work.AniDbId,
                        Title = work.Title,
                        Year = work.Year
                    });
                }

                _importListStatusService.RecordSuccess(Definition.Id);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to fetch data for list {0} ({1})", Definition.Name, Name);

                _importListStatusService.RecordFailure(Definition.Id);
            }

            return CleanupListItems(series);
        }

        public override object RequestAction(string action, IDictionary<string, string> query)
        {
            if (action == "getStudios")
            {
                var studios = _catalogService.GetStudios();

                return new
                {
                    options = studios.Select(s => new
                    {
                        value = s,
                        name = s
                    })
                };
            }

            return new { };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            if (Settings.Studio.IsNullOrWhiteSpace())
            {
                failures.Add(new ValidationFailure("Studio", "A studio is required"));

                return;
            }

            if (_catalogService.GetStudioWorks(Settings.Studio).Empty())
            {
                failures.Add(new ValidationFailure("Studio", "No works known for this studio yet. The catalog is aggregated from AniDB in the background, try again later."));
            }
        }
    }
}
