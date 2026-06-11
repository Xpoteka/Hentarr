using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MetadataSource.AniDb.Catalog
{
    public class AniDbCatalogSyncService : IExecute<AniDbCatalogSyncCommand>
    {
        // AniDB does not offer a studio to anime lookup, studio information is
        // aggregated locally by slowly walking the catalog. Keep the batches
        // small so the client stays well within AniDB's rate limits.
        private const int SYNC_BATCH_SIZE = 40;

        private readonly IAniDbCatalogService _catalogService;
        private readonly Logger _logger;

        public AniDbCatalogSyncService(IAniDbCatalogService catalogService, Logger logger)
        {
            _catalogService = catalogService;
            _logger = logger;
        }

        public void Execute(AniDbCatalogSyncCommand message)
        {
            _logger.ProgressInfo("Starting AniDB catalog sync");

            var discovered = _catalogService.SeedNewTitles();
            var synced = _catalogService.SyncBatch(SYNC_BATCH_SIZE);

            _logger.ProgressInfo("AniDB catalog sync completed. New titles: {0}, Studio info synced: {1}", discovered, synced);
        }
    }
}
