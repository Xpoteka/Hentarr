using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MetadataSource.AniDb.Catalog
{
    public class AniDbCatalogSyncService : IExecute<AniDbCatalogSyncCommand>
    {
        private readonly IAniDbCatalogService _catalogService;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly Logger _logger;

        public AniDbCatalogSyncService(IAniDbCatalogService catalogService,
                                       IConfigFileProvider configFileProvider,
                                       Logger logger)
        {
            _catalogService = catalogService;
            _configFileProvider = configFileProvider;
            _logger = logger;
        }

        public void Execute(AniDbCatalogSyncCommand message)
        {
            _logger.ProgressInfo("Starting AniDB catalog sync");

            var discovered = _catalogService.SeedNewTitles();

            // AniDB does not offer a studio to anime lookup, studio information is
            // aggregated locally by slowly walking the catalog. Keep the batches
            // small so the client stays well within AniDB's rate limits.
            var synced = _catalogService.SyncBatch(_configFileProvider.AniDbCatalogBatchSize);

            _logger.ProgressInfo("AniDB catalog sync completed. New titles: {0}, Studio info synced: {1}", discovered, synced);
        }
    }
}
