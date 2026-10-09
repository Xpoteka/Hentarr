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
    // Fork: delivers AniList english titles and synonyms of every library series as scene mappings,
    // which is the path Sonarr already uses both for indexer search terms and for matching release titles.
    public class AniListSceneMappingProvider : ISceneMappingProvider
    {
        // Must match the value SceneMappingService stores in SceneMapping.Type (the provider's type name).
        public const string MappingType = nameof(AniListSceneMappingProvider);

        private readonly ISeriesService _seriesService;
        private readonly IAniListTitleCache _titleCache;
        private readonly IAniListGraphQlClient _client;
        private readonly ISceneMappingRepository _repository;
        private readonly Logger _logger;

        public AniListSceneMappingProvider(ISeriesService seriesService,
                                           IAniListTitleCache titleCache,
                                           IAniListGraphQlClient client,
                                           ISceneMappingRepository repository,
                                           Logger logger)
        {
            _seriesService = seriesService;
            _titleCache = titleCache;
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

            var titlesById = GetAlternateTitles(allSeries);
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

                foreach (var title in titles.Distinct(StringComparer.InvariantCultureIgnoreCase))
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

                    mappings.Add(new SceneMapping
                    {
                        MappingId = $"anilist:{series.TvdbId}:{title.ToLowerInvariant()}",
                        Title = title,
                        SearchTerm = title,
                        ParseTerm = parseTerm,
                        TvdbId = series.TvdbId,
                        Type = MappingType
                    });
                }
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

            _logger.Debug("Generated {0} AniList alternate title mappings for {1} series", mappings.Count, allSeries.Count);

            return mappings;
        }

        private Dictionary<int, List<string>> GetAlternateTitles(List<Series> allSeries)
        {
            var titlesById = new Dictionary<int, List<string>>();
            var missing = new List<int>();

            foreach (var series in allSeries)
            {
                if (_titleCache.TryGet(series.TvdbId, out var titles))
                {
                    titlesById[series.TvdbId] = titles;
                }
                else
                {
                    missing.Add(series.TvdbId);
                }
            }

            if (missing.Empty())
            {
                return titlesById;
            }

            // Reuse what was stored on a previous run so a restart does not cost one AniList request per 50 series.
            var stored = _repository.GetAllByType(MappingType)
                                    .GroupBy(m => m.TvdbId)
                                    .ToDictionary(g => g.Key, g => g.Select(m => m.Title).ToList());

            foreach (var id in missing.ToList())
            {
                if (stored.TryGetValue(id, out var storedTitles))
                {
                    titlesById[id] = storedTitles;
                    missing.Remove(id);
                }
            }

            if (missing.Empty())
            {
                return titlesById;
            }

            _logger.Debug("Fetching alternate titles from AniList for {0} series", missing.Count);

            foreach (var media in _client.GetMediaByIds(missing))
            {
                _titleCache.Store(media);
                titlesById[media.Id] = AniListMapper.GetAlternateTitles(media);
            }

            return titlesById;
        }
    }
}
