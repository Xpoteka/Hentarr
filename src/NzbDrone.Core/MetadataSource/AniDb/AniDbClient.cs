using System;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public interface IAniDbClient
    {
        AniDbAnime GetAnime(int anidbId);
    }

    public class AniDbClient : IAniDbClient
    {
        private const string RATE_LIMIT_KEY = "anidb";

        private static readonly TimeSpan RequestInterval = TimeSpan.FromSeconds(3);

        private readonly IHttpClient _httpClient;
        private readonly IRateLimitService _rateLimitService;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly Logger _logger;

        public AniDbClient(IHttpClient httpClient,
                           IRateLimitService rateLimitService,
                           IConfigFileProvider configFileProvider,
                           Logger logger)
        {
            _httpClient = httpClient;
            _rateLimitService = rateLimitService;
            _configFileProvider = configFileProvider;
            _logger = logger;
        }

        public AniDbAnime GetAnime(int anidbId)
        {
            var request = new HttpRequestBuilder(_configFileProvider.AniDbApiUrl)
                .AddQueryParam("client", _configFileProvider.AniDbClientName)
                .AddQueryParam("clientver", "1")
                .AddQueryParam("protover", "1")
                .AddQueryParam("request", "anime")
                .AddQueryParam("aid", anidbId.ToString())
                .Build();

            request.SuppressHttpError = true;

            _rateLimitService.WaitAndPulse(RATE_LIMIT_KEY, RequestInterval);

            var response = _httpClient.Get(request);

            if (response.HasHttpError)
            {
                throw new AniDbException("AniDB request for anime {0} failed with status code {1}", anidbId, response.StatusCode);
            }

            XDocument document;

            try
            {
                document = XDocument.Parse(response.Content);
            }
            catch (Exception ex)
            {
                throw new AniDbException("AniDB returned an invalid XML response for anime {0}: {1}", anidbId, ex.Message);
            }

            var root = document.Root;

            if (root == null)
            {
                throw new AniDbException("AniDB returned an empty response for anime {0}", anidbId);
            }

            if (root.Name.LocalName == "error")
            {
                var message = root.Value;

                if (message.ContainsIgnoreCase("banned"))
                {
                    _logger.Warn("AniDB has banned this client, backing off");

                    throw new AniDbBannedException();
                }

                if (message.ContainsIgnoreCase("no such anime"))
                {
                    return null;
                }

                throw new AniDbException("AniDB returned an error for anime {0}: {1}", anidbId, message);
            }

            return AniDbAnimeParser.ParseAnime(root);
        }
    }
}
