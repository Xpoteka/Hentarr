using System;
using System.Collections.Generic;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.ImportLists.Shoko
{
    public class ShokoImport : ImportListBase<ShokoSettings>
    {
        private readonly IShokoApiProxy _shokoApiProxy;

        public ShokoImport(IShokoApiProxy shokoApiProxy,
                           IImportListStatusService importListStatusService,
                           IConfigService configService,
                           IParsingService parsingService,
                           Logger logger)
            : base(importListStatusService, configService, parsingService, logger)
        {
            _shokoApiProxy = shokoApiProxy;
        }

        public override string Name => "Shoko";

        public override ImportListType ListType => ImportListType.Program;
        public override TimeSpan MinRefreshInterval => TimeSpan.FromMinutes(15);

        public override IList<ImportListItemInfo> Fetch()
        {
            var series = new List<ImportListItemInfo>();

            try
            {
                var remoteSeries = _shokoApiProxy.GetSeries(Settings);

                foreach (var item in remoteSeries)
                {
                    if (item.IDs == null || item.IDs.AniDB <= 0)
                    {
                        continue;
                    }

                    series.Add(new ImportListItemInfo
                    {
                        TpdbSiteId = item.IDs.AniDB,
                        Title = item.Name
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

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(_shokoApiProxy.Test(Settings));
        }
    }
}
