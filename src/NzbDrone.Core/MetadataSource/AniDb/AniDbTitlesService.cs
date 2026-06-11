using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public interface IAniDbTitlesService
    {
        List<AniDbTitle> Search(string term, int limit = 50);
        string GetMainTitle(int anidbId);
        List<int> GetAllAnimeIds();
        void EnsureCurrent();
    }

    public class AniDbTitle
    {
        public int AniDbId { get; set; }
        public string MainTitle { get; set; }
        public List<string> AllTitles { get; set; }
    }

    public class AniDbTitlesService : IAniDbTitlesService
    {
        private const string TITLES_FILE_NAME = "anidb-titles.xml.gz";

        private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);
        private static readonly TimeSpan RetryInterval = TimeSpan.FromHours(1);
        private static readonly object Mutex = new object();

        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly string _titlesPath;
        private readonly Logger _logger;

        private Dictionary<int, AniDbTitle> _titles;
        private DateTime _lastDownloadAttempt = DateTime.MinValue;

        public AniDbTitlesService(IHttpClient httpClient,
                                  IDiskProvider diskProvider,
                                  IAppFolderInfo appFolderInfo,
                                  IConfigFileProvider configFileProvider,
                                  Logger logger)
        {
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _configFileProvider = configFileProvider;
            _titlesPath = Path.Combine(appFolderInfo.AppDataFolder, TITLES_FILE_NAME);
            _logger = logger;
        }

        public List<AniDbTitle> Search(string term, int limit = 50)
        {
            var titles = GetTitles();

            if (titles.Empty() || term.IsNullOrWhiteSpace())
            {
                return new List<AniDbTitle>();
            }

            var cleanTerm = term.Trim();

            return titles.Values
                         .Select(t => new
                         {
                             Title = t,
                             Score = Score(t, cleanTerm)
                         })
                         .Where(t => t.Score > 0)
                         .OrderByDescending(t => t.Score)
                         .ThenByDescending(t => t.Title.AniDbId)
                         .Take(limit)
                         .Select(t => t.Title)
                         .ToList();
        }

        public string GetMainTitle(int anidbId)
        {
            return GetTitles().GetValueOrDefault(anidbId)?.MainTitle;
        }

        public List<int> GetAllAnimeIds()
        {
            return GetTitles().Keys.ToList();
        }

        public void EnsureCurrent()
        {
            lock (Mutex)
            {
                if (!IsCacheStale())
                {
                    return;
                }

                DownloadTitles();
                _titles = null;
            }

            GetTitles();
        }

        private static int Score(AniDbTitle title, string term)
        {
            var score = 0;

            foreach (var candidate in title.AllTitles)
            {
                if (candidate.EqualsIgnoreCase(term))
                {
                    return 100;
                }

                if (candidate.StartsWithIgnoreCase(term))
                {
                    score = Math.Max(score, 50);
                }
                else if (candidate.ContainsIgnoreCase(term))
                {
                    score = Math.Max(score, 10);
                }
            }

            return score;
        }

        private Dictionary<int, AniDbTitle> GetTitles()
        {
            lock (Mutex)
            {
                if (_titles != null)
                {
                    return _titles;
                }

                if (!_diskProvider.FileExists(_titlesPath))
                {
                    DownloadTitles();
                }

                if (!_diskProvider.FileExists(_titlesPath))
                {
                    return new Dictionary<int, AniDbTitle>();
                }

                try
                {
                    _titles = ParseTitles(_titlesPath);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Failed to parse cached AniDB titles, removing cache file");
                    _diskProvider.DeleteFile(_titlesPath);

                    return new Dictionary<int, AniDbTitle>();
                }

                _logger.Debug("Loaded {0} anime titles from the AniDB titles dump", _titles.Count);

                return _titles;
            }
        }

        private bool IsCacheStale()
        {
            if (!_diskProvider.FileExists(_titlesPath))
            {
                return true;
            }

            return _diskProvider.FileGetLastWrite(_titlesPath).Add(RefreshInterval) < DateTime.UtcNow;
        }

        private void DownloadTitles()
        {
            // AniDB only allows fetching the titles dump once per day per IP,
            // hold off retrying for a while after a failed attempt.
            if (_lastDownloadAttempt.Add(RetryInterval) > DateTime.UtcNow)
            {
                return;
            }

            _lastDownloadAttempt = DateTime.UtcNow;

            try
            {
                _logger.Info("Downloading AniDB titles dump");

                var request = new HttpRequestBuilder(_configFileProvider.AniDbTitlesUrl).Build();
                var response = _httpClient.Get(request);

                using (var stream = _diskProvider.OpenWriteStream(_titlesPath))
                {
                    stream.Write(response.ResponseData, 0, response.ResponseData.Length);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to download the AniDB titles dump");
            }
        }

        private Dictionary<int, AniDbTitle> ParseTitles(string path)
        {
            var titles = new Dictionary<int, AniDbTitle>();

            using (var fileStream = File.OpenRead(path))
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (var reader = XmlReader.Create(gzipStream))
            {
                AniDbTitle current = null;

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "anime")
                    {
                        var aid = reader.GetAttribute("aid");

                        current = null;

                        if (int.TryParse(aid, out var anidbId))
                        {
                            current = new AniDbTitle
                            {
                                AniDbId = anidbId,
                                AllTitles = new List<string>()
                            };

                            titles[anidbId] = current;
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.Element && reader.Name == "title" && current != null)
                    {
                        var type = reader.GetAttribute("type");
                        var value = reader.ReadElementContentAsString();

                        if (value.IsNullOrWhiteSpace())
                        {
                            continue;
                        }

                        current.AllTitles.Add(value);

                        if (string.Equals(type, "main", StringComparison.InvariantCultureIgnoreCase) || current.MainTitle == null)
                        {
                            current.MainTitle = value;
                        }
                    }
                }
            }

            return titles;
        }
    }
}
