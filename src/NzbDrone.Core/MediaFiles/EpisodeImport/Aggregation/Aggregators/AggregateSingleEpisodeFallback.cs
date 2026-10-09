using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MediaFiles.EpisodeImport.Aggregation.Aggregators
{
    // Fork: single-episode AniList entries (OVAs, specials) are often released as "[Group] Title [1080p].mkv" with no
    // episode number, which the parser cannot map. When the series has exactly one episode, a video file that is named
    // after the series (or is the only video file in the download) is that episode.
    public class AggregateSingleEpisodeFallback : IAggregateLocalEpisode
    {
        public int Order => 2;

        private readonly IEpisodeService _episodeService;
        private readonly ISceneMappingService _sceneMappingService;
        private readonly Logger _logger;

        public AggregateSingleEpisodeFallback(IEpisodeService episodeService, ISceneMappingService sceneMappingService, Logger logger)
        {
            _episodeService = episodeService;
            _sceneMappingService = sceneMappingService;
            _logger = logger;
        }

        public LocalEpisode Aggregate(LocalEpisode localEpisode, DownloadClientItem downloadClientItem)
        {
            if (localEpisode.Episodes != null && localEpisode.Episodes.Any())
            {
                return localEpisode;
            }

            var series = localEpisode.Series;

            if (series == null || localEpisode.Path.IsNullOrWhiteSpace())
            {
                return localEpisode;
            }

            if (!MediaFileExtensions.Extensions.Contains(Path.GetExtension(localEpisode.Path)))
            {
                return localEpisode;
            }

            var episodes = _episodeService.GetEpisodeBySeries(series.Id).Where(e => e.SeasonNumber > 0).ToList();

            if (episodes.Count != 1)
            {
                return localEpisode;
            }

            var fileName = Path.GetFileNameWithoutExtension(localEpisode.Path);
            var seasonTitleInfo = Parser.Parser.ParseSeasonTitle(fileName);
            var titleMatches = seasonTitleInfo != null && TitleMatchesSeries(seasonTitleInfo.SeriesTitle, fileName, series);

            if (!titleMatches && localEpisode.OtherVideoFiles)
            {
                return localEpisode;
            }

            var episode = episodes[0];

            _logger.Debug("Mapping numberless file {0} to the only episode of {1}", fileName, series);

            localEpisode.Episodes = episodes;
            localEpisode.FileEpisodeInfo ??= new ParsedEpisodeInfo
            {
                ReleaseTitle = fileName,
                SeriesTitle = series.Title,
                SeriesTitleInfo = new SeriesTitleInfo { Title = series.Title, TitleWithoutYear = series.Title },
                SeasonNumber = episode.SeasonNumber,
                EpisodeNumbers = new[] { episode.EpisodeNumber },
                AbsoluteEpisodeNumbers = episode.AbsoluteEpisodeNumber.HasValue ? new[] { episode.AbsoluteEpisodeNumber.Value } : Array.Empty<int>(),
                Quality = QualityParser.ParseQuality(fileName),
                ReleaseGroup = ReleaseGroupParser.ParseReleaseGroup(fileName),
                Languages = LanguageParser.ParseLanguages(fileName)
            };

            if (localEpisode.Quality == null || localEpisode.Quality.Quality == Quality.Unknown)
            {
                localEpisode.Quality = localEpisode.FileEpisodeInfo.Quality;
            }

            return localEpisode;
        }

        private bool TitleMatchesSeries(string title, string releaseTitle, Series series)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return false;
            }

            var cleanTitle = title.CleanSeriesTitle();

            if (cleanTitle == series.CleanTitle || (series.CleanOriginalTitle.IsNotNullOrWhiteSpace() && cleanTitle == series.CleanOriginalTitle))
            {
                return true;
            }

            return _sceneMappingService.FindTvdbId(title, releaseTitle, -1) == series.TvdbId;
        }
    }
}
