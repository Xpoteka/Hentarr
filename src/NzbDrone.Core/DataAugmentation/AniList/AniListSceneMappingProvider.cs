using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.DataAugmentation.AniList
{
    // Fork: delivers AniList titles and synonyms of every library series as scene mappings, which is the path Sonarr
    // already uses both for indexer search terms and for matching release titles. For a chain (several AniList
    // entries as seasons) every entry's titles are emitted with the season they belong to, so "Title 2 - 01" is S02E01.
    public class AniListSceneMappingProvider : ISceneMappingProvider
    {
        // Must match the value SceneMappingService stores in SceneMapping.Type (the provider's type name).
        public const string MappingType = nameof(AniListSceneMappingProvider);

        private readonly ISeriesService _seriesService;
        private readonly IAniListTitleCache _titleCache;
        private readonly IAniListChainResolver _chainResolver;
        private readonly IAniListGraphQlClient _client;
        private readonly ISceneMappingRepository _repository;
        private readonly Logger _logger;

        public AniListSceneMappingProvider(ISeriesService seriesService,
                                           IAniListTitleCache titleCache,
                                           IAniListChainResolver chainResolver,
                                           IAniListGraphQlClient client,
                                           ISceneMappingRepository repository,
                                           Logger logger)
        {
            _seriesService = seriesService;
            _titleCache = titleCache;
            _chainResolver = chainResolver;
            _client = client;
            _repository = repository;
            _logger = logger;
        }

        public List<SceneMapping> GetSceneMappings()
        {
            var allSeries = _seriesService.GetAllSeries();

            if (allSeries.Empty())
            {
                return new List<SceneMapping>();
            }

            var titlesById = GetTitles(allSeries);
            var mainCleanTitles = allSeries.Select(s => s.CleanTitle)
                                           .Where(t => t.IsNotNullOrWhiteSpace())
                                           .ToHashSet(StringComparer.InvariantCultureIgnoreCase);

            var mappings = new List<SceneMapping>();

            foreach (var series in allSeries)
            {
                if (!titlesById.TryGetValue(series.TvdbId, out var titles))
                {
                    continue;
                }

                var seriesMappings = new List<SceneMapping>();

                foreach (var (title, season) in titles.DistinctBy(t => (t.Title.ToLowerInvariant(), t.Season)))
                {
                    var parseTerm = title.CleanSeriesTitle();

                    if (parseTerm.IsNullOrWhiteSpace())
                    {
                        continue;
                    }

                    // An alternate title that cleans to another library series' main title would hijack that series' releases.
                    if (parseTerm != series.CleanTitle && mainCleanTitles.Contains(parseTerm))
                    {
                        _logger.Debug("Skipping alternate title '{0}' for {1} because it matches the title of another series", title, series);
                        continue;
                    }

                    seriesMappings.Add(new SceneMapping
                    {
                        MappingId = season.HasValue ? $"anilist:{series.TvdbId}:s{season.Value}:{title.ToLowerInvariant()}" : $"anilist:{series.TvdbId}:{title.ToLowerInvariant()}",
                        Title = title,
                        SearchTerm = title,
                        ParseTerm = parseTerm,
                        TvdbId = series.TvdbId,
                        SceneSeasonNumber = season,
                        Type = MappingType
                    });
                }

                // A title shared by several seasons of one series cannot select a season, keep it as a series-wide mapping.
                foreach (var group in seriesMappings.GroupBy(m => m.ParseTerm).Where(g => g.Select(m => m.SceneSeasonNumber).Distinct().Count() > 1).ToList())
                {
                    var first = group.First();
                    seriesMappings.RemoveAll(m => m.ParseTerm == group.Key);
                    seriesMappings.Add(new SceneMapping
                    {
                        MappingId = $"anilist:{series.TvdbId}:{first.Title.ToLowerInvariant()}",
                        Title = first.Title,
                        SearchTerm = first.Title,
                        ParseTerm = first.ParseTerm,
                        TvdbId = series.TvdbId,
                        Type = MappingType
                    });
                }

                mappings.AddRange(seriesMappings);
            }

            // The same alternate title on two different series cannot be resolved when parsing, so drop it from both.
            var ambiguous = mappings.GroupBy(m => m.ParseTerm)
                                    .Where(g => g.Select(m => m.TvdbId).Distinct().Count() > 1)
                                    .Select(g => g.Key)
                                    .ToHashSet();

            if (ambiguous.Any())
            {
                _logger.Debug("Dropping {0} alternate titles shared by more than one series: {1}", ambiguous.Count, string.Join(", ", ambiguous));
                mappings.RemoveAll(m => ambiguous.Contains(m.ParseTerm));
            }

            _logger.Debug("Generated {0} AniList title mappings for {1} series", mappings.Count, allSeries.Count);

            return mappings;
        }

        // Titles per series root id with the season they identify (null = whole series).
        private Dictionary<int, List<(string Title, int? Season)>> GetTitles(List<Series> allSeries)
        {
            var result = new Dictionary<int, List<(string Title, int? Season)>>();
            var missing = new List<Series>();

            foreach (var series in allSeries)
            {
                if (_chainResolver.TryGetCached(series.TvdbId, out var chain))
                {
                    result[series.TvdbId] = TitlesFromChain(chain);
                }
                else if (series.AniListIds == null || series.AniListIds.Count <= 1)
                {
                    if (_titleCache.TryGetMedia(series.TvdbId, out var media))
                    {
                        result[series.TvdbId] = TitlesFromChain(AniListChain.Single(media));
                    }
                    else
                    {
                        missing.Add(series);
                    }
                }
                else
                {
                    missing.Add(series);
                }
            }

            if (missing.Empty())
            {
                return result;
            }

            // Reuse what was stored on a previous run so a restart does not cost AniList requests.
            var stored = _repository.GetAllByType(MappingType)
                                    .GroupBy(m => m.TvdbId)
                                    .ToDictionary(g => g.Key, g => g.Select(m => (m.Title, m.SceneSeasonNumber)).ToList());

            foreach (var series in missing.ToList())
            {
                if (stored.TryGetValue(series.TvdbId, out var storedTitles))
                {
                    result[series.TvdbId] = storedTitles;
                    missing.Remove(series);
                }
            }

            if (missing.Empty())
            {
                return result;
            }

            _logger.Debug("Fetching titles from AniList for {0} series", missing.Count);

            var ids = missing.SelectMany(s => (s.AniListIds != null && s.AniListIds.Any()) ? s.AniListIds : new HashSet<int> { s.TvdbId }).Distinct().ToList();
            var mediaById = _client.GetMediaByIds(ids).ToDictionary(m => m.Id);

            foreach (var media in mediaById.Values)
            {
                _titleCache.Store(media);
            }

            foreach (var series in missing)
            {
                var titles = new List<(string Title, int? Season)>();
                var memberIds = (series.AniListIds != null && series.AniListIds.Any()) ? series.AniListIds : new HashSet<int> { series.TvdbId };
                var multiSeason = series.Seasons.Count(s => s.SeasonNumber > 0) > 1;

                foreach (var id in memberIds)
                {
                    if (!mediaById.TryGetValue(id, out var media))
                    {
                        continue;
                    }

                    var mainTitle = AniListMapper.GetMainTitle(media);
                    int? season = null;

                    if (multiSeason)
                    {
                        season = series.Seasons.FirstOrDefault(s => s.SeasonNumber > 0 && string.Equals(s.Title, mainTitle, StringComparison.InvariantCultureIgnoreCase))?.SeasonNumber;

                        if (season.HasValue)
                        {
                            titles.Add((mainTitle, season));
                        }
                    }

                    titles.AddRange(AniListMapper.GetAlternateTitles(media).Select(t => (t, season)));
                }

                result[series.TvdbId] = titles;
            }

            return result;
        }

        private static List<(string Title, int? Season)> TitlesFromChain(AniListChain chain)
        {
            var titles = new List<(string Title, int? Season)>();

            if (chain.IsSingle)
            {
                titles.AddRange(AniListMapper.GetAlternateTitles(chain.Root).Select(t => (t, (int?)null)));
                return titles;
            }

            for (var index = 0; index < chain.Seasons.Count; index++)
            {
                var media = chain.Seasons[index];
                int? season = index + 1;

                titles.Add((AniListMapper.GetMainTitle(media), season));
                titles.AddRange(AniListMapper.GetAlternateTitles(media).Select(t => (t, season)));
            }

            return titles;
        }
    }
}
