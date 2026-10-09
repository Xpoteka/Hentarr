using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MetadataSource.AniList.Resource;

namespace NzbDrone.Core.MetadataSource.AniList
{
    // Fork: one series is a chain of AniList entries. Seasons[i] is season i + 1, Specials is season 0.
    public class AniListChain
    {
        public List<AniListMedia> Seasons { get; } = new List<AniListMedia>();
        public List<AniListMedia> Specials { get; } = new List<AniListMedia>();
        public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;

        public AniListMedia Root => Seasons[0];
        public IEnumerable<AniListMedia> All => Seasons.Concat(Specials);
        public bool IsSingle => Seasons.Count == 1 && Specials.Count == 0;

        public int SeasonNumberOf(int aniListId)
        {
            var index = Seasons.FindIndex(m => m.Id == aniListId);

            if (index >= 0)
            {
                return index + 1;
            }

            return Specials.Any(m => m.Id == aniListId) ? 0 : -1;
        }

        public static AniListChain Single(AniListMedia media)
        {
            var chain = new AniListChain();
            chain.Seasons.Add(media);
            return chain;
        }
    }
}
