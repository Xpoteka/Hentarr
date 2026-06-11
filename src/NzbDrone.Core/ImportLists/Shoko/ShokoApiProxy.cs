using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Http;

namespace NzbDrone.Core.ImportLists.Shoko
{
    public interface IShokoApiProxy
    {
        List<ShokoSeriesResource> GetSeries(ShokoSettings settings);
        HashSet<int> GetOwnedEpisodeAniDbIds(ShokoSettings settings, int shokoSeriesId);
        ValidationFailure Test(ShokoSettings settings);
    }

    public class ShokoApiProxy : IShokoApiProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;

        public ShokoApiProxy(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public List<ShokoSeriesResource> GetSeries(ShokoSettings settings)
        {
            var request = BuildRequest(settings, "api/v3/Series")
                .AddQueryParam("page", "1")
                .AddQueryParam("pageSize", "0")
                .Build();

            var response = _httpClient.Get<ShokoListResult<ShokoSeriesResource>>(request);

            return response.Resource?.List ?? new List<ShokoSeriesResource>();
        }

        public HashSet<int> GetOwnedEpisodeAniDbIds(ShokoSettings settings, int shokoSeriesId)
        {
            var request = BuildRequest(settings, $"api/v3/Series/{shokoSeriesId}/Episode")
                .AddQueryParam("page", "1")
                .AddQueryParam("pageSize", "0")
                .AddQueryParam("includeMissing", "false")
                .Build();

            var response = _httpClient.Get<ShokoListResult<ShokoEpisodeResource>>(request);

            return (response.Resource?.List ?? new List<ShokoEpisodeResource>())
                .Where(e => e.Size > 0 && e.IDs != null && e.IDs.AniDB > 0)
                .Select(e => e.IDs.AniDB)
                .ToHashSet();
        }

        public ValidationFailure Test(ShokoSettings settings)
        {
            try
            {
                GetSeries(settings);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.Unauthorized || ex.Response.StatusCode == HttpStatusCode.Forbidden)
                {
                    _logger.Warn(ex, "Shoko API key is invalid");

                    return new ValidationFailure("ApiKey", "API key is invalid");
                }

                _logger.Warn(ex, "Unable to connect to Shoko server");

                return new ValidationFailure("BaseUrl", "Unable to connect to Shoko server, check the log for more details");
            }
            catch (System.Exception ex)
            {
                _logger.Warn(ex, "Unable to connect to Shoko server");

                return new ValidationFailure("BaseUrl", "Unable to connect to Shoko server, check the log for more details");
            }

            return null;
        }

        private HttpRequestBuilder BuildRequest(ShokoSettings settings, string resource)
        {
            return new HttpRequestBuilder(settings.BaseUrl.TrimEnd('/'))
                .Resource(resource)
                .Accept(HttpAccept.Json)
                .SetHeader("apikey", settings.ApiKey);
        }
    }
}
