using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.MetadataSource.AniList
{
    // Fork: AniList splits one story into many entries (sequels, side stories, spin-offs). When a series is added,
    // every related adult anime entry is added too, with the same folder, profile and monitoring. Each added entry
    // raises its own SeriesAddedEvent, so the whole franchise graph is walked; entries already in the library or on
    // the import list exclusions stop the walk.
    public class AniListRelatedSeriesService : IHandleAsync<SeriesAddedEvent>, IExecute<AddRelatedSeriesCommand>
    {
        public static readonly string[] FollowedRelationTypes = { "SEQUEL", "PREQUEL", "SIDE_STORY", "SPIN_OFF", "PARENT", "ALTERNATIVE", "SUMMARY" };

        private readonly IAniListGraphQlClient _client;
        private readonly IAniListMetadataOptions _options;
        private readonly ISeriesService _seriesService;
        private readonly IAddSeriesService _addSeriesService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IImportListExclusionService _exclusionService;
        private readonly Logger _logger;

        public AniListRelatedSeriesService(IAniListGraphQlClient client,
                                           IAniListMetadataOptions options,
                                           ISeriesService seriesService,
                                           IAddSeriesService addSeriesService,
                                           IRootFolderService rootFolderService,
                                           IImportListExclusionService exclusionService,
                                           Logger logger)
        {
            _client = client;
            _options = options;
            _seriesService = seriesService;
            _addSeriesService = addSeriesService;
            _rootFolderService = rootFolderService;
            _exclusionService = exclusionService;
            _logger = logger;
        }

        public void HandleAsync(SeriesAddedEvent message)
        {
            if (!_options.AddRelatedSeries)
            {
                return;
            }

            AddRelated(message.Series);
        }

        public void Execute(AddRelatedSeriesCommand message)
        {
            var series = message.SeriesId.HasValue
                ? new List<Series> { _seriesService.GetSeries(message.SeriesId.Value) }
                : _seriesService.GetAllSeries();

            foreach (var item in series)
            {
                AddRelated(item);
            }
        }

        public List<Series> AddRelated(Series series)
        {
            var added = new List<Series>();
            List<AniListRelationEdge> relations;

            try
            {
                relations = _client.GetRelations(series.TvdbId);
                relations.AddRange(FindEntriesLinkingBack(series));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to get related entries for {0} from AniList", series);
                return added;
            }

            foreach (var relation in relations.DistinctBy(r => r.Node?.Id ?? 0))
            {
                var node = relation.Node;

                if (!IsWanted(relation))
                {
                    continue;
                }

                if (_seriesService.FindByTvdbId(node.Id) != null)
                {
                    continue;
                }

                if (_exclusionService.FindByTvdbId(node.Id) != null)
                {
                    _logger.Debug("Skipping related entry {0} [{1}] of {2}, it is on the exclusion list", AniListMapper.GetMainTitle(node), node.Id, series);
                    continue;
                }

                var newSeries = new Series
                {
                    TvdbId = node.Id,
                    Title = AniListMapper.GetMainTitle(node),
                    RootFolderPath = series.RootFolderPath.IsNotNullOrWhiteSpace() ? series.RootFolderPath : _rootFolderService.GetBestRootFolderPath(series.Path),
                    QualityProfileId = series.QualityProfileId,
                    Monitored = series.Monitored,
                    MonitorNewItems = series.MonitorNewItems,
                    SeasonFolder = series.SeasonFolder,
                    SeriesType = SeriesTypes.Anime,
                    SeasonType = series.SeasonType,
                    Language = series.Language,
                    Tags = new HashSet<int>(series.Tags ?? new HashSet<int>()),
                    AddOptions = series.AddOptions ?? new AddSeriesOptions { Monitor = MonitorTypes.All }
                };

                try
                {
                    _logger.Info("Adding {0} [{1}] as {2} of {3}", newSeries.Title, node.Id, relation.RelationType.ToLowerInvariant().Replace('_', ' '), series);
                    added.Add(_addSeriesService.AddSeries(newSeries));
                }
                catch (ValidationException ex)
                {
                    _logger.Warn("Unable to add related entry {0} [{1}]: {2}", newSeries.Title, node.Id, ex.Message);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to add related entry {0} [{1}]", newSeries.Title, node.Id);
                }
            }

            return added;
        }

        // AniList relations are not symmetric: Taimanin Yukikaze lists Taimanin Asagi as an alternative, but Asagi does not
        // list Yukikaze. Search the franchise word of the title and keep entries whose own relations point at the library.
        private List<AniListRelationEdge> FindEntriesLinkingBack(Series series)
        {
            var result = new List<AniListRelationEdge>();
            var term = GetFranchiseTerm(series.Title);

            if (term.IsNullOrWhiteSpace())
            {
                return result;
            }

            var libraryIds = _seriesService.AllSeriesTvdbIds().Values.ToHashSet();
            libraryIds.Add(series.TvdbId);

            var candidates = _client.Search(term, _options.AdultFilter)
                                    .Where(m => m.Id != series.TvdbId && !libraryIds.Contains(m.Id))
                                    .Where(m => AniListMapper.GetMainTitle(m).StartsWith(term, StringComparison.InvariantCultureIgnoreCase))
                                    .Take(25)
                                    .ToList();

            foreach (var candidate in candidates)
            {
                var back = _client.GetRelations(candidate.Id)
                                  .FirstOrDefault(r => r.Node != null && libraryIds.Contains(r.Node.Id) && FollowedRelationTypes.Contains(r.RelationType?.ToUpperInvariant()));

                if (back != null)
                {
                    candidate.Type ??= "ANIME";
                    result.Add(new AniListRelationEdge { RelationType = back.RelationType, Node = candidate });
                }
            }

            return result;
        }

        public static string GetFranchiseTerm(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return null;
            }

            var head = title.Split(':')[0].Trim();
            var words = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (words.Length == 0)
            {
                return null;
            }

            var term = words[0];

            if (term.Length < 4 && words.Length > 1)
            {
                term = $"{words[0]} {words[1]}";
            }

            return term.Length >= 3 ? term : null;
        }

        private bool IsWanted(AniListRelationEdge relation)
        {
            var node = relation.Node;

            if (node == null || node.Id <= 0)
            {
                return false;
            }

            if (!string.Equals(node.Type, "ANIME", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!FollowedRelationTypes.Contains(relation.RelationType?.ToUpperInvariant()))
            {
                return false;
            }

            switch (_options.AdultFilter)
            {
                case AniListAdultFilter.Adult:
                    return node.IsAdult;
                case AniListAdultFilter.NonAdult:
                    return !node.IsAdult;
                default:
                    return true;
            }
        }
    }
}
