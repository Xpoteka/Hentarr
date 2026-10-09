using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.MetadataSource.SkyHook;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniList
{
    // Fork: replaces SkyHookProxy as the only IProvideSeriesInfo / ISearchForNewSeries implementation.
    // The AniList media ID is stored in Series.TvdbId so no schema or API changes are needed.
    public class AniListMetadataProxy : IProvideSeriesInfo, ISearchForNewSeries
    {
        private static readonly string[] AniListPrefixes = { "anilist:", "anilistid:", "tvdb:", "tvdbid:" };
        private static readonly string[] MalPrefixes = { "mal:", "malid:", "myanimelist:" };
        private static readonly string[] UnsupportedPrefixes = { "imdb:", "imdbid:", "tmdb:", "tmdbid:" };

        private readonly IAniListGraphQlClient _client;
        private readonly ISeriesService _seriesService;
        private readonly IAniListMetadataOptions _options;
        private readonly IAniListTitleCache _titleCache;
        private readonly Logger _logger;

        public AniListMetadataProxy(IAniListGraphQlClient client,
                                    ISeriesService seriesService,
                                    IAniListMetadataOptions options,
                                    IAniListTitleCache titleCache,
                                    Logger logger)
        {
            _client = client;
            _seriesService = seriesService;
            _options = options;
            _titleCache = titleCache;
            _logger = logger;
        }

        public Tuple<Series, List<Episode>> GetSeriesInfo(int tvdbSeriesId, Language language, string seasonType)
        {
            var media = _client.GetMedia(tvdbSeriesId);

            _titleCache.Store(media);

            return new Tuple<Series, List<Episode>>(AniListMapper.MapSeries(media), AniListMapper.MapEpisodes(media));
        }

        public List<Series> SearchForNewSeriesByImdbId(string imdbId, Language language)
        {
            return new List<Series>();
        }

        public List<Series> SearchForNewSeriesByTmdbId(int tmdbId, Language language)
        {
            return new List<Series>();
        }

        public List<Series> SearchForNewSeriesByAniListId(int aniListId, Language language)
        {
            return SearchForNewSeries($"anilist:{aniListId}", language);
        }

        public List<Series> SearchForNewSeriesByMyAnimeListId(int malId, Language language)
        {
            return SearchForNewSeries($"mal:{malId}", language);
        }

        public List<Series> SearchForNewSeries(string title, Language language)
        {
            if (title.IsPathValid(PathValidationType.AnyOs))
            {
                throw new InvalidSearchTermException("Invalid search term '{0}'", title);
            }

            try
            {
                var lowerTitle = title.ToLowerInvariant().Trim();

                if (TryGetPrefixedId(lowerTitle, AniListPrefixes, out var aniListId))
                {
                    return aniListId.HasValue ? SearchByAniListId(aniListId.Value) : new List<Series>();
                }

                if (TryGetPrefixedId(lowerTitle, MalPrefixes, out var malId))
                {
                    return malId.HasValue ? SearchByMalId(malId.Value) : new List<Series>();
                }

                if (UnsupportedPrefixes.Any(p => lowerTitle.StartsWith(p)))
                {
                    return new List<Series>();
                }

                var results = _client.Search(title.Trim(), _options.AdultFilter);

                return results.Select(MapSearchResult).ToList();
            }
            catch (SeriesNotFoundException)
            {
                return new List<Series>();
            }
            catch (AniListException)
            {
                throw;
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new AniListException("Search for '{0}' failed. Unable to communicate with AniList. {1}", ex, title, ex.Message);
            }
            catch (WebException ex)
            {
                _logger.Warn(ex);
                throw new AniListException("Search for '{0}' failed. Unable to communicate with AniList. {1}", ex, title, ex.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new AniListException("Search for '{0}' failed. Invalid response received from AniList. {1}", ex, title, ex.Message);
            }
        }

        private List<Series> SearchByAniListId(int aniListId)
        {
            var existingSeries = _seriesService.FindByTvdbId(aniListId);

            if (existingSeries != null)
            {
                return new List<Series> { existingSeries };
            }

            return new List<Series> { AniListMapper.MapSeries(_client.GetMedia(aniListId)) };
        }

        private List<Series> SearchByMalId(int malId)
        {
            var media = _client.GetMediaByMalId(malId);

            if (media == null)
            {
                return new List<Series>();
            }

            return new List<Series> { MapSearchResult(media) };
        }

        private Series MapSearchResult(AniListMedia media)
        {
            return _seriesService.FindByTvdbId(media.Id) ?? AniListMapper.MapSeries(media);
        }

        // Returns true when the term carries one of the prefixes. The id is null when the remainder is not a positive integer.
        private static bool TryGetPrefixedId(string lowerTitle, IEnumerable<string> prefixes, out int? id)
        {
            id = null;

            foreach (var prefix in prefixes)
            {
                if (!lowerTitle.StartsWith(prefix))
                {
                    continue;
                }

                var slug = lowerTitle.Substring(prefix.Length).Trim();

                if (slug.IsNotNullOrWhiteSpace() && !slug.Any(char.IsWhiteSpace) && int.TryParse(slug, out var parsed) && parsed > 0)
                {
                    id = parsed;
                }

                return true;
            }

            return false;
        }
    }
}
