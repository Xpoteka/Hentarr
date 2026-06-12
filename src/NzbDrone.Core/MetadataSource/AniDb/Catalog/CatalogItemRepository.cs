using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.AniDb.Catalog
{
    public interface ICatalogItemRepository : IBasicRepository<CatalogItem>
    {
        CatalogItem FindByAniDbId(int anidbId);
        List<CatalogItem> GetByStudio(string studio);
        List<CatalogItem> SearchByStudio(string query);
        List<CatalogItem> GetNextToSync(int limit);
        List<int> AllAniDbIds();
        List<string> AllStudios();
    }

    public class CatalogItemRepository : BasicRepository<CatalogItem>, ICatalogItemRepository
    {
        public CatalogItemRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public CatalogItem FindByAniDbId(int anidbId)
        {
            return Query(c => c.AniDbId == anidbId).SingleOrDefault();
        }

        public List<CatalogItem> GetByStudio(string studio)
        {
            return Query(c => c.Studio == studio);
        }

        public List<CatalogItem> SearchByStudio(string query)
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<CatalogItem>("SELECT * FROM \"CatalogItems\" WHERE \"Studio\" IS NOT NULL AND lower(\"Studio\") LIKE @query", new { query = "%" + query.ToLowerInvariant() + "%" }).ToList();
            }
        }

        public List<CatalogItem> GetNextToSync(int limit)
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<CatalogItem>("SELECT * FROM \"CatalogItems\" WHERE \"LastInfoSync\" IS NULL ORDER BY \"AniDbId\" DESC LIMIT @limit", new { limit }).ToList();
            }
        }

        public List<int> AllAniDbIds()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<int>("SELECT \"AniDbId\" FROM \"CatalogItems\"").ToList();
            }
        }

        public List<string> AllStudios()
        {
            using (var conn = _database.OpenConnection())
            {
                return conn.Query<string>("SELECT DISTINCT \"Studio\" FROM \"CatalogItems\" WHERE \"Studio\" IS NOT NULL AND \"Studio\" <> ''").ToList();
            }
        }
    }
}
