using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListSeriesLookup
    {
        // The series whose root id or member ids contain the given AniList id
        Series FindByAniListId(int aniListId);

        // Every AniList id owned by a library series (optionally excluding one series)
        HashSet<int> AllAniListIds(Series except = null);
    }

    public class AniListSeriesLookup : IAniListSeriesLookup
    {
        private readonly ISeriesService _seriesService;

        public AniListSeriesLookup(ISeriesService seriesService)
        {
            _seriesService = seriesService;
        }

        public Series FindByAniListId(int aniListId)
        {
            return _seriesService.GetAllSeries()
                                 .FirstOrDefault(s => s.TvdbId == aniListId || (s.AniListIds != null && s.AniListIds.Contains(aniListId)));
        }

        public HashSet<int> AllAniListIds(Series except = null)
        {
            var ids = new HashSet<int>();

            foreach (var series in _seriesService.GetAllSeries())
            {
                if (except != null && series.Id == except.Id)
                {
                    continue;
                }

                ids.Add(series.TvdbId);

                if (series.AniListIds != null)
                {
                    ids.UnionWith(series.AniListIds);
                }
            }

            return ids;
        }
    }
}
