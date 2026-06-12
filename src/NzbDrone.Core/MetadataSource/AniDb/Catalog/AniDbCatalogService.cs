using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.AniDb.Catalog
{
    public interface IAniDbCatalogService
    {
        int SeedNewTitles();
        int SyncBatch(int batchSize);
        void RecordAnime(AniDbAnime anime);
        List<CatalogItem> GetStudioWorks(string studio);
        List<CatalogItem> SearchStudioWorks(string query, int limit = 100);
        List<string> GetStudios();
    }

    public class AniDbCatalogService : IAniDbCatalogService
    {
        private readonly ICatalogItemRepository _catalogItemRepository;
        private readonly IAniDbTitlesService _titlesService;
        private readonly IAniDbClient _aniDbClient;
        private readonly Logger _logger;

        public AniDbCatalogService(ICatalogItemRepository catalogItemRepository,
                                   IAniDbTitlesService titlesService,
                                   IAniDbClient aniDbClient,
                                   Logger logger)
        {
            _catalogItemRepository = catalogItemRepository;
            _titlesService = titlesService;
            _aniDbClient = aniDbClient;
            _logger = logger;
        }

        public int SeedNewTitles()
        {
            _titlesService.EnsureCurrent();

            var known = _catalogItemRepository.AllAniDbIds().ToHashSet();
            var added = DateTime.UtcNow;

            var newItems = _titlesService.GetAllAnimeIds()
                                         .Where(id => !known.Contains(id))
                                         .Select(id => new CatalogItem
                                         {
                                             AniDbId = id,
                                             Title = _titlesService.GetMainTitle(id),
                                             Added = added
                                         })
                                         .ToList();

            if (newItems.Any())
            {
                _logger.Info("Discovered {0} new AniDB titles for the catalog", newItems.Count);
                _catalogItemRepository.InsertMany(newItems);
            }

            return newItems.Count;
        }

        public int SyncBatch(int batchSize)
        {
            var batch = _catalogItemRepository.GetNextToSync(batchSize);

            if (batch.Empty())
            {
                return 0;
            }

            var synced = 0;

            foreach (var item in batch)
            {
                try
                {
                    var anime = _aniDbClient.GetAnime(item.AniDbId);

                    item.LastInfoSync = DateTime.UtcNow;

                    if (anime != null)
                    {
                        item.Title = anime.Series.Title;
                        item.Year = anime.Series.Year;
                        item.Studio = anime.Series.Network;
                        item.Restricted = anime.Restricted;
                    }

                    _catalogItemRepository.Update(item);
                    synced++;
                }
                catch (AniDbBannedException)
                {
                    _logger.Warn("AniDB has banned this client, stopping catalog sync until the next scheduled run");

                    break;
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to sync catalog info for AniDB anime {0}", item.AniDbId);

                    item.LastInfoSync = DateTime.UtcNow;
                    _catalogItemRepository.Update(item);
                }
            }

            _logger.Info("Synced studio info for {0} catalog items", synced);

            return synced;
        }

        public void RecordAnime(AniDbAnime anime)
        {
            if (anime?.Series == null)
            {
                return;
            }

            var item = _catalogItemRepository.FindByAniDbId(anime.Series.TvdbId) ?? new CatalogItem
            {
                AniDbId = anime.Series.TvdbId,
                Added = DateTime.UtcNow
            };

            item.Title = anime.Series.Title;
            item.Year = anime.Series.Year;
            item.Studio = anime.Series.Network;
            item.Restricted = anime.Restricted;
            item.LastInfoSync = DateTime.UtcNow;

            if (item.Id == 0)
            {
                _catalogItemRepository.Insert(item);
            }
            else
            {
                _catalogItemRepository.Update(item);
            }
        }

        public List<CatalogItem> GetStudioWorks(string studio)
        {
            return _catalogItemRepository.GetByStudio(studio);
        }

        public List<CatalogItem> SearchStudioWorks(string query, int limit = 100)
        {
            return _catalogItemRepository.SearchByStudio(query)
                                         .OrderByDescending(c => c.Year)
                                         .ThenBy(c => c.Title, StringComparer.InvariantCultureIgnoreCase)
                                         .Take(limit)
                                         .ToList();
        }

        public List<string> GetStudios()
        {
            return _catalogItemRepository.AllStudios()
                                         .OrderBy(s => s, StringComparer.InvariantCultureIgnoreCase)
                                         .ToList();
        }
    }
}
