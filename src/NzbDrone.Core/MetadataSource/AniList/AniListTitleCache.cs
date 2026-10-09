using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MetadataSource.AniList.Resource;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListTitleCache
    {
        void Store(AniListMedia media);
        bool TryGet(int aniListId, out List<string> alternateTitles);
    }

    // Remembers the alternate titles of every AniList entry fetched during this process lifetime,
    // so the scene mapping provider does not have to call AniList again for series that were just added or refreshed.
    public class AniListTitleCache : IAniListTitleCache
    {
        private readonly ICached<List<string>> _cache;

        public AniListTitleCache(ICacheManager cacheManager)
        {
            _cache = cacheManager.GetCache<List<string>>(GetType(), "alternateTitles");
        }

        public void Store(AniListMedia media)
        {
            if (media == null || media.Id <= 0)
            {
                return;
            }

            _cache.Set(media.Id.ToString(), AniListMapper.GetAlternateTitles(media));
        }

        public bool TryGet(int aniListId, out List<string> alternateTitles)
        {
            var cached = _cache.Find(aniListId.ToString());

            if (cached == null)
            {
                alternateTitles = null;
                return false;
            }

            alternateTitles = cached.ToList();
            return true;
        }
    }
}
