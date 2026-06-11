using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public class AniDbProxy : IProvideSeriesInfo, ISearchForNewSeries
    {
        private readonly IAniDbClient _aniDbClient;
        private readonly IAniDbTitlesService _titlesService;
        private readonly ISeriesService _seriesService;
        private readonly Logger _logger;

        public AniDbProxy(IAniDbClient aniDbClient,
                          IAniDbTitlesService titlesService,
                          ISeriesService seriesService,
                          Logger logger)
        {
            _aniDbClient = aniDbClient;
            _titlesService = titlesService;
            _seriesService = seriesService;
            _logger = logger;
        }

        public Tuple<Series, List<Episode>> GetSeriesInfo(int anidbId)
        {
            var anime = _aniDbClient.GetAnime(anidbId);

            if (anime == null)
            {
                throw new SeriesNotFoundException(anidbId);
            }

            return new Tuple<Series, List<Episode>>(anime.Series, anime.Episodes);
        }

        public List<Series> SearchForNewSeries(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return new List<Series>();
            }

            var lowerTitle = title.ToLowerInvariant();

            if (lowerTitle.StartsWith("anidb:") || lowerTitle.StartsWith("anidbid:") || lowerTitle.StartsWith("aid:"))
            {
                var slug = lowerTitle.Split(':')[1].Trim();

                if (slug.IsNullOrWhiteSpace() || slug.Any(char.IsWhiteSpace) || !int.TryParse(slug, out var anidbId) || anidbId <= 0)
                {
                    return new List<Series>();
                }

                try
                {
                    var existingSeries = _seriesService.FindByTvdbId(anidbId);

                    if (existingSeries != null)
                    {
                        return new List<Series> { existingSeries };
                    }

                    return new List<Series> { GetSeriesInfo(anidbId).Item1 };
                }
                catch (SeriesNotFoundException)
                {
                    return new List<Series>();
                }
            }

            try
            {
                return _titlesService.Search(title)
                                     .Select(MapSearchResult)
                                     .ToList();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);

                throw new AniDbException("Search for '{0}' failed: {1}", title, ex.Message);
            }
        }

        private Series MapSearchResult(AniDbTitle title)
        {
            var series = _seriesService.FindByTvdbId(title.AniDbId);

            if (series != null)
            {
                return series;
            }

            return new Series
            {
                TvdbId = title.AniDbId,
                Title = title.MainTitle,
                CleanTitle = Parser.Parser.CleanSeriesTitle(title.MainTitle),
                SortTitle = SeriesTitleNormalizer.Normalize(title.MainTitle, title.AniDbId),
                TitleSlug = title.AniDbId.ToString(CultureInfo.InvariantCulture),
                OriginalLanguage = Language.Japanese,
                Monitored = true
            };
        }
    }
}
