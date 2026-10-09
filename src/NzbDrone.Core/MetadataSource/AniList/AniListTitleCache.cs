using System.Collections.Generic;
using NzbDrone.Common.Cache;
using NzbDrone.Core.MetadataSource.AniList.Resource;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListTitleCache
    {
        void Store(AniListMedia media);
        bool TryGet(int aniListId, out List<string> alternateTitles);
        bool TryGetMedia(int aniListId, out AniListMedia media);
    }

    // Remembers every AniList entry fetched during this process lifetime, so the scene mapping provider does not
    // have to call AniList again for series that were just added or refreshed.
    public class AniListTitleCache : IAniListTitleCache
    {
        private readonly ICached<AniListMedia> _cache;

        public AniListTitleCache(ICacheManager cacheManager)
        {
            _cache = cacheManager.GetCache<AniListMedia>(GetType(), "media");
        }

        public void Store(AniListMedia media)
        {
            if (media == null || media.Id <= 0)
            {
                return;
            }

            _cache.Set(media.Id.ToString(), media);
        }

        public bool TryGet(int aniListId, out List<string> alternateTitles)
        {
            if (TryGetMedia(aniListId, out var media))
            {
                alternateTitles = AniListMapper.GetAlternateTitles(media);
                return true;
            }

            alternateTitles = null;
            return false;
        }

        public bool TryGetMedia(int aniListId, out AniListMedia media)
        {
            media = _cache.Find(aniListId.ToString());
            return media != null;
        }
    }
}
