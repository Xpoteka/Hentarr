using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListChainResolver
    {
        // Refresh of an existing series: the series' own id stays season 1, later entries become seasons, specials season 0.
        // Ids owned by another library series are never entered.
        AniListChain ResolveForSeries(Series existing, bool forceRefresh);

        // A new id: walk the whole chain. When a visited entry already belongs to a library series that series is
        // returned as owner and no chain is built.
        AniListChain ResolveForNewId(int aniListId, out Series owner);

        bool TryGetCached(int anyMemberId, out AniListChain chain);
    }

    // Fork: AniList lists sequels, side stories and specials as separate entries. This walks those relations and
    // groups the entries that share the franchise title into one chain (seasons ordered by air date).
    public class AniListChainResolver : IAniListChainResolver
    {
        public static readonly string[] ChainRelationTypes = { "SEQUEL", "PREQUEL", "SIDE_STORY", "SPIN_OFF", "PARENT" };
        public static readonly string[] SpecialRelationTypes = { "SEQUEL", "PREQUEL", "SIDE_STORY", "SPIN_OFF", "PARENT", "SUMMARY" };

        private static readonly Regex TrailingTokenRegex = new Regex(@"\s+(the animation|the motion anime|\d+|[ivx]+|\d+(st|nd|rd|th)|season\s*\d*|part\s*\d*|s\d+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(24);

        // The refresh that follows an add would walk the chain a second time; a chain this young is reused
        private static readonly TimeSpan RecentEnough = TimeSpan.FromMinutes(10);

        private readonly IAniListGraphQlClient _client;
        private readonly IAniListSeriesLookup _lookup;
        private readonly IAniListTitleCache _titleCache;
        private readonly ICached<AniListChain> _cache;
        private readonly Logger _logger;

        public AniListChainResolver(IAniListGraphQlClient client,
                                    IAniListSeriesLookup lookup,
                                    IAniListTitleCache titleCache,
                                    ICacheManager cacheManager,
                                    Logger logger)
        {
            _client = client;
            _lookup = lookup;
            _titleCache = titleCache;
            _cache = cacheManager.GetCache<AniListChain>(GetType(), "chains");
            _logger = logger;
        }

        public AniListChain ResolveForSeries(Series existing, bool forceRefresh)
        {
            if (TryGetCached(existing.TvdbId, out var cached) &&
                cached.Root.Id == existing.TvdbId &&
                (!forceRefresh || cached.ResolvedAt > DateTime.UtcNow - RecentEnough))
            {
                return cached;
            }

            var foreign = _lookup.AllAniListIds(existing);
            var members = Collect(existing.TvdbId, id => foreign.Contains(id));
            var start = members.Seasons.Single(m => m.Id == existing.TvdbId);
            var startDate = start.StartDate?.ToDateTime() ?? DateTime.MinValue;
            var earlier = members.Seasons.Where(m => m.Id != existing.TvdbId && (m.StartDate?.ToDateTime() ?? DateTime.MaxValue) < startDate).ToList();

            foreach (var entry in earlier)
            {
                _logger.Warn("AniList lists {0} [{1}] before {2}; delete and re-add the series to make it season 1", AniListMapper.GetMainTitle(entry), entry.Id, existing);
                members.Seasons.Remove(entry);
            }

            var chain = Order(members);
            Store(chain, forceRefresh);

            return chain;
        }

        public AniListChain ResolveForNewId(int aniListId, out Series owner)
        {
            owner = _lookup.FindByAniListId(aniListId);

            if (owner != null)
            {
                return null;
            }

            if (TryGetCached(aniListId, out var cached))
            {
                return cached;
            }

            var members = Collect(aniListId, _ => false);

            foreach (var member in members.All)
            {
                owner = _lookup.FindByAniListId(member.Id);

                if (owner != null)
                {
                    return null;
                }
            }

            var chain = Order(members);
            Store(chain, false);

            return chain;
        }

        public bool TryGetCached(int anyMemberId, out AniListChain chain)
        {
            chain = _cache.Find(anyMemberId.ToString());
            return chain != null;
        }

        // Breadth-first over the relation edges: one request for the start entry, then one request per level.
        private AniListChain Collect(int startId, Func<int, bool> isBarrier)
        {
            var start = _client.GetMedia(startId);
            _titleCache.Store(start);

            var baseTitle = GetBaseTitle(AniListMapper.GetMainTitle(start));
            var members = new Dictionary<int, AniListMedia> { { start.Id, start } };
            var specials = new Dictionary<int, AniListMedia>();
            var pending = new List<int>();

            void Visit(AniListMedia media)
            {
                foreach (var edge in media.Relations?.Edges ?? new List<AniListRelationEdge>())
                {
                    var node = edge.Node;

                    if (node == null || node.Id <= 0 || members.ContainsKey(node.Id) || specials.ContainsKey(node.Id) || pending.Contains(node.Id))
                    {
                        continue;
                    }

                    if (!string.Equals(node.Type, "ANIME", StringComparison.OrdinalIgnoreCase) || isBarrier(node.Id))
                    {
                        continue;
                    }

                    if (!TitleBelongsTo(baseTitle, AniListMapper.GetMainTitle(node)))
                    {
                        continue;
                    }

                    var relationType = edge.RelationType?.ToUpperInvariant();

                    if (IsSpecial(node))
                    {
                        if (SpecialRelationTypes.Contains(relationType))
                        {
                            specials[node.Id] = node;
                        }
                    }
                    else if (ChainRelationTypes.Contains(relationType))
                    {
                        pending.Add(node.Id);
                    }
                }
            }

            Visit(start);

            while (pending.Any())
            {
                var level = _client.GetMediaByIds(pending);
                pending.Clear();

                foreach (var media in level)
                {
                    _titleCache.Store(media);
                    members[media.Id] = media;
                }

                foreach (var media in level)
                {
                    Visit(media);
                }
            }

            // Specials only carry the relation node's fields; fetch them fully for episode counts and dates
            if (specials.Any())
            {
                foreach (var media in _client.GetMediaByIds(specials.Keys))
                {
                    _titleCache.Store(media);
                    specials[media.Id] = media;
                }
            }

            var chain = new AniListChain();
            chain.Seasons.AddRange(members.Values);
            chain.Specials.AddRange(specials.Values);

            return chain;
        }

        private static AniListChain Order(AniListChain unordered)
        {
            var chain = new AniListChain();
            chain.Seasons.AddRange(unordered.Seasons.OrderBy(m => m.StartDate?.ToDateTime() ?? DateTime.MaxValue).ThenBy(m => m.Id));
            chain.Specials.AddRange(unordered.Specials.OrderBy(m => m.StartDate?.ToDateTime() ?? DateTime.MaxValue).ThenBy(m => m.Id));

            return chain;
        }

        private void Store(AniListChain chain, bool forceRefresh)
        {
            if (forceRefresh)
            {
                foreach (var member in chain.All)
                {
                    var previous = _cache.Find(member.Id.ToString());

                    if (previous != null)
                    {
                        foreach (var old in previous.All)
                        {
                            _cache.Remove(old.Id.ToString());
                        }
                    }
                }
            }

            foreach (var member in chain.All)
            {
                _cache.Set(member.Id.ToString(), chain, CacheLifetime);
            }
        }

        public static bool IsSpecial(AniListMedia media)
        {
            return string.Equals(media.Format, "SPECIAL", StringComparison.OrdinalIgnoreCase);
        }

        // "Taimanin Asagi 2" -> "Taimanin Asagi", "PRETTY×CATION 2 THE ANIMATION" -> "PRETTY×CATION", "Makai Kishi Ingrid: Re" -> "Makai Kishi Ingrid"
        public static string GetBaseTitle(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var head = title.Split(':')[0].Trim();

            if (head.Length < 4)
            {
                head = title.Trim();
            }

            string previous;

            do
            {
                previous = head;
                head = TrailingTokenRegex.Replace(head, string.Empty).Trim();
            }
            while (head != previous && head.Length >= 4);

            return head.Length >= 4 ? head : previous;
        }

        public static bool TitleBelongsTo(string baseTitle, string candidateTitle)
        {
            var cleanBase = baseTitle.CleanSeriesTitle();
            var cleanCandidate = candidateTitle.CleanSeriesTitle();

            if (cleanBase.IsNullOrWhiteSpace() || cleanCandidate.IsNullOrWhiteSpace())
            {
                return false;
            }

            return cleanCandidate.StartsWith(cleanBase, StringComparison.Ordinal) || cleanBase.StartsWith(cleanCandidate, StringComparison.Ordinal);
        }
    }
}
