using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.ImportLists.Exclusions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.ImportLists.Shoko
{
    public class ShokoSyncService : IExecute<ShokoSyncCommand>
    {
        private readonly IImportListFactory _importListFactory;
        private readonly IShokoApiProxy _shokoApiProxy;
        private readonly IImportListExclusionService _importListExclusionService;
        private readonly ISeriesService _seriesService;
        private readonly IEpisodeService _episodeService;
        private readonly Logger _logger;

        public ShokoSyncService(IImportListFactory importListFactory,
                                IShokoApiProxy shokoApiProxy,
                                IImportListExclusionService importListExclusionService,
                                ISeriesService seriesService,
                                IEpisodeService episodeService,
                                Logger logger)
        {
            _importListFactory = importListFactory;
            _shokoApiProxy = shokoApiProxy;
            _importListExclusionService = importListExclusionService;
            _seriesService = seriesService;
            _episodeService = episodeService;
            _logger = logger;
        }

        public void Execute(ShokoSyncCommand message)
        {
            var definitions = _importListFactory.All()
                                                .Where(d => d.Enable && d.Implementation == nameof(ShokoImport))
                                                .ToList();

            if (definitions.Empty())
            {
                _logger.Debug("No enabled Shoko import lists, skipping have sync");

                return;
            }

            foreach (var definition in definitions)
            {
                var settings = (ShokoSettings)definition.Settings;

                if (!settings.SyncHaveAsExclusions && !settings.UnmonitorOwnedEpisodes)
                {
                    continue;
                }

                _logger.ProgressInfo("Syncing library state from Shoko list '{0}'", definition.Name);

                List<ShokoSeriesResource> shokoSeries;

                try
                {
                    shokoSeries = _shokoApiProxy.GetSeries(settings)
                                                .Where(s => s.IDs != null && s.IDs.AniDB > 0)
                                                .ToList();
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to fetch library from Shoko list '{0}'", definition.Name);

                    continue;
                }

                if (settings.SyncHaveAsExclusions)
                {
                    SyncExclusions(shokoSeries);
                }

                if (settings.UnmonitorOwnedEpisodes)
                {
                    UnmonitorOwnedEpisodes(settings, shokoSeries);
                }
            }
        }

        private void SyncExclusions(List<ShokoSeriesResource> shokoSeries)
        {
            var existingExclusions = _importListExclusionService.All()
                                                                .Select(e => e.TvdbId)
                                                                .ToHashSet();

            var added = 0;

            foreach (var series in shokoSeries)
            {
                if (existingExclusions.Contains(series.IDs.AniDB))
                {
                    continue;
                }

                _importListExclusionService.Add(new ImportListExclusion
                {
                    TvdbId = series.IDs.AniDB,
                    Title = series.Name
                });

                added++;
            }

            if (added > 0)
            {
                _logger.Info("Added {0} import list exclusions for titles already in the Shoko library", added);
            }
        }

        private void UnmonitorOwnedEpisodes(ShokoSettings settings, List<ShokoSeriesResource> shokoSeries)
        {
            var allSeries = _seriesService.GetAllSeries().ToDictionary(s => s.TvdbId);
            var unmonitored = 0;

            foreach (var shoko in shokoSeries)
            {
                if (!allSeries.TryGetValue(shoko.IDs.AniDB, out var series))
                {
                    continue;
                }

                HashSet<int> ownedEpisodeIds;

                try
                {
                    ownedEpisodeIds = _shokoApiProxy.GetOwnedEpisodeAniDbIds(settings, shoko.IDs.ID);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to fetch episodes from Shoko for series '{0}'", shoko.Name);

                    continue;
                }

                if (ownedEpisodeIds.Empty())
                {
                    continue;
                }

                foreach (var episode in _episodeService.GetEpisodeBySeries(series.Id))
                {
                    if (episode.Monitored && !episode.HasFile && ownedEpisodeIds.Contains(episode.TvdbId))
                    {
                        _episodeService.SetEpisodeMonitored(episode.Id, false);
                        unmonitored++;
                    }
                }
            }

            if (unmonitored > 0)
            {
                _logger.Info("Unmonitored {0} episodes that already have files in Shoko", unmonitored);
            }
        }
    }
}
