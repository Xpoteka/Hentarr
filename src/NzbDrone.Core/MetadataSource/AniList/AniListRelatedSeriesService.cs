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
    // Fork: AniList splits one story into many entries. Sequels and specials of the same title become seasons of one
    // series (see AniListChainResolver); the remaining related entries (side stories, spin-offs, alternatives) are
    // separate stories and are added as their own series when a series is added, with the same folder, profile and
    // monitoring. Each added series raises its own SeriesAddedEvent, so the whole franchise graph is walked; entries
    // already in the library or on the import list exclusions stop the walk.
    public class AniListRelatedSeriesService : IHandleAsync<SeriesAddedEvent>, IExecute<AddRelatedSeriesCommand>
    {
        public static readonly string[] FollowedRelationTypes = { "SIDE_STORY", "SPIN_OFF", "PARENT", "ALTERNATIVE", "SUMMARY", "SEQUEL", "PREQUEL" };

        private readonly IAniListGraphQlClient _client;
        private readonly IAniListMetadataOptions _options;
        private readonly ISeriesService _seriesService;
        private readonly IAddSeriesService _addSeriesService;
        private readonly IRootFolderService _rootFolderService;
        private readonly IImportListExclusionService _exclusionService;
        private readonly IAniListChainResolver _chainResolver;
        private readonly IAniListSeriesLookup _lookup;
        private readonly Logger _logger;

        public AniListRelatedSeriesService(IAniListGraphQlClient client,
                                           IAniListMetadataOptions options,
                                           ISeriesService seriesService,
                                           IAddSeriesService addSeriesService,
                                           IRootFolderService rootFolderService,
                                           IImportListExclusionService exclusionService,
                                           IAniListChainResolver chainResolver,
                                           IAniListSeriesLookup lookup,
                                           Logger logger)
        {
            _client = client;
            _options = options;
            _seriesService = seriesService;
            _addSeriesService = addSeriesService;
            _rootFolderService = rootFolderService;
            _exclusionService = exclusionService;
            _chainResolver = chainResolver;
            _lookup = lookup;
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
                relations = GetRelations(series);
                relations.AddRange(FindEntriesLinkingBack(series));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to get related entries for {0} from AniList", series);
                return added;
            }

            var ownIds = series.AniListIds != null && series.AniListIds.Any() ? series.AniListIds : new HashSet<int> { series.TvdbId };

            foreach (var relation in relations.DistinctBy(r => r.Node?.Id ?? 0))
            {
                var node = relation.Node;

                if (!IsWanted(relation) || ownIds.Contains(node.Id))
                {
                    continue;
                }

                if (_lookup.FindByAniListId(node.Id) != null)
                {
                    continue;
                }

                if (_exclusionService.FindByTvdbId(node.Id) != null)
                {
                    _logger.Debug("Skipping related entry {0} [{1}] of {2}, it is on the exclusion list", AniListMapper.GetMainTitle(node), node.Id, series);
                    continue;
                }

                // The entry may be a later season of a story not in the library yet: add that story by its root
                AniListMedia root;

                try
                {
                    var chain = _chainResolver.ResolveForNewId(node.Id, out var owner);

                    if (owner != null || chain == null)
                    {
                        continue;
                    }

                    root = chain.Root;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Unable to resolve related entry {0} [{1}]", AniListMapper.GetMainTitle(node), node.Id);
                    continue;
                }

                if (root.Id != node.Id && (_lookup.FindByAniListId(root.Id) != null || _exclusionService.FindByTvdbId(root.Id) != null))
                {
                    continue;
                }

                var newSeries = new Series
                {
                    TvdbId = root.Id,
                    Title = AniListMapper.GetMainTitle(root),
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

        // Relations of every entry in the series' chain (from the resolver cache), or fetched per entry.
        private List<AniListRelationEdge> GetRelations(Series series)
        {
            if (_chainResolver.TryGetCached(series.TvdbId, out var chain))
            {
                return chain.All.SelectMany(m => m.Relations?.Edges ?? new List<AniListRelationEdge>()).ToList();
            }

            var ids = series.AniListIds != null && series.AniListIds.Any() ? series.AniListIds : new HashSet<int> { series.TvdbId };

            return ids.SelectMany(id => _client.GetRelations(id)).ToList();
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

            var libraryIds = _lookup.AllAniListIds();
            libraryIds.Add(series.TvdbId);

            var candidates = _client.Search(term, _options.AdultFilter)
                                    .Where(m => !libraryIds.Contains(m.Id))
                                    .Where(m => AniListMapper.GetMainTitle(m).StartsWith(term, StringComparison.InvariantCultureIgnoreCase))
                                    .Take(25)
                                    .ToList();

            foreach (var candidate in candidates)
            {
                var edges = candidate.Relations?.Edges ?? _client.GetRelations(candidate.Id);
                var back = edges.FirstOrDefault(r => r.Node != null && libraryIds.Contains(r.Node.Id) && FollowedRelationTypes.Contains(r.RelationType?.ToUpperInvariant()));

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

            // Specials belong to their parent's season 0, never to a series of their own
            if (AniListChainResolver.IsSpecial(node))
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
