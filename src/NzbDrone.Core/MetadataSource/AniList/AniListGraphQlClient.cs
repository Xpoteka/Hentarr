using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource.AniList.Resource;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public interface IAniListGraphQlClient
    {
        AniListMedia GetMedia(int aniListId);
        AniListMedia GetMediaByMalId(int malId);
        List<AniListMedia> Search(string term, AniListAdultFilter adultFilter);
        List<AniListMedia> GetMediaByIds(IEnumerable<int> aniListIds);
        List<AniListRelationEdge> GetRelations(int aniListId);
    }

    public class AniListGraphQlClient : IAniListGraphQlClient
    {
        public const string Endpoint = "https://graphql.anilist.co";
        public const int MaxPerPage = 50;

        // AniList has throttled its public API to 30 requests/minute per IP, so space requests at 2s.
        private const double RateLimitSeconds = 2.0;
        private const int MaxRetries = 2;
        private const int MaxRetryAfterSeconds = 60;

        private const string MediaFields = @"
            id
            idMal
            isAdult
            title { romaji english native }
            synonyms
            format
            status
            episodes
            duration
            startDate { year month day }
            endDate { year month day }
            description(asHtml: false)
            coverImage { extraLarge large }
            bannerImage
            genres
            averageScore
            studios(isMain: true) { nodes { name } }
            airingSchedule(perPage: 50) { nodes { episode airingAt } }";

        private const string MediaByIdQuery = $@"
            query ($id: Int) {{
                Media(id: $id, type: ANIME) {{ {MediaFields} }}
            }}";

        private const string MediaByMalIdQuery = $@"
            query ($idMal: Int) {{
                Media(idMal: $idMal, type: ANIME) {{ {MediaFields} }}
            }}";

        private const string RelationsQuery = @"
            query ($id: Int) {
                Media(id: $id, type: ANIME) {
                    id
                    relations {
                        edges {
                            relationType
                            node { id idMal type isAdult format status title { romaji english native } }
                        }
                    }
                }
            }";

        private const string MediaByIdsQuery = $@"
            query ($ids: [Int], $page: Int, $perPage: Int) {{
                Page(page: $page, perPage: $perPage) {{
                    pageInfo {{ total currentPage lastPage hasNextPage }}
                    media(id_in: $ids, type: ANIME) {{ {MediaFields} }}
                }}
            }}";

        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly Logger _logger;

        public AniListGraphQlClient(IHttpClient httpClient, Logger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _requestBuilder = new HttpRequestBuilder(Endpoint)
                .Post()
                .Accept(HttpAccept.Json)
                .WithRateLimit(RateLimitSeconds)
                .CreateFactory();
        }

        public AniListMedia GetMedia(int aniListId)
        {
            var response = Execute<AniListMediaData>(MediaByIdQuery, new { id = aniListId });

            if (IsNotFound(response) || response.Resource?.Data?.Media == null)
            {
                throw new SeriesNotFoundException(aniListId, "AniList entry {0} was not found", aniListId);
            }

            EnsureNoErrors(response);

            return response.Resource.Data.Media;
        }

        public AniListMedia GetMediaByMalId(int malId)
        {
            var response = Execute<AniListMediaData>(MediaByMalIdQuery, new { idMal = malId });

            if (IsNotFound(response))
            {
                return null;
            }

            EnsureNoErrors(response);

            return response.Resource?.Data?.Media;
        }

        public List<AniListMedia> Search(string term, AniListAdultFilter adultFilter)
        {
            // The isAdult argument is only emitted when a filter is active, since passing null would be ambiguous.
            var adultArgument = adultFilter switch
            {
                AniListAdultFilter.Adult => ", isAdult: true",
                AniListAdultFilter.NonAdult => ", isAdult: false",
                _ => string.Empty
            };

            var query = $@"
                query ($search: String, $page: Int, $perPage: Int) {{
                    Page(page: $page, perPage: $perPage) {{
                        pageInfo {{ total currentPage lastPage hasNextPage }}
                        media(search: $search, type: ANIME, sort: SEARCH_MATCH{adultArgument}) {{ {MediaFields} }}
                    }}
                }}";

            var response = Execute<AniListPageData>(query, new { search = term, page = 1, perPage = 25 });

            EnsureNoErrors(response);

            return response.Resource?.Data?.Page?.Media ?? new List<AniListMedia>();
        }

        public List<AniListMedia> GetMediaByIds(IEnumerable<int> aniListIds)
        {
            var result = new List<AniListMedia>();
            var ids = aniListIds.Distinct().ToList();

            foreach (var batch in ids.Chunk(MaxPerPage))
            {
                var page = 1;

                while (true)
                {
                    var response = Execute<AniListPageData>(MediaByIdsQuery, new { ids = batch, page, perPage = MaxPerPage });

                    EnsureNoErrors(response);

                    var pageData = response.Resource?.Data?.Page;

                    if (pageData?.Media == null)
                    {
                        break;
                    }

                    result.AddRange(pageData.Media);

                    if (pageData.PageInfo == null || !pageData.PageInfo.HasNextPage)
                    {
                        break;
                    }

                    page++;
                }
            }

            return result;
        }

        public List<AniListRelationEdge> GetRelations(int aniListId)
        {
            var response = Execute<AniListRelationsData>(RelationsQuery, new { id = aniListId });

            if (IsNotFound(response))
            {
                return new List<AniListRelationEdge>();
            }

            EnsureNoErrors(response);

            return response.Resource?.Data?.Media?.Relations?.Edges?.Where(e => e.Node != null).ToList() ?? new List<AniListRelationEdge>();
        }

        private HttpResponse<AniListResponse<T>> Execute<T>(string query, object variables)
            where T : new()
        {
            var body = new { query, variables }.ToJson();

            for (var attempt = 0; ; attempt++)
            {
                var request = _requestBuilder.Create().Build();
                request.Headers.ContentType = "application/json";
                request.SuppressHttpError = true;
                request.SetContent(body);

                var response = _httpClient.Post<AniListResponse<T>>(request);

                var remaining = response.Headers.GetSingleValue("X-RateLimit-Remaining");

                if (remaining.IsNotNullOrWhiteSpace())
                {
                    _logger.Trace("AniList rate limit remaining: {0}", remaining);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    if (attempt >= MaxRetries)
                    {
                        throw new AniListException("AniList rate limit exceeded, giving up after {0} retries", MaxRetries);
                    }

                    var retryAfter = ParseRetryAfter(response.Headers.GetSingleValue("Retry-After"));
                    _logger.Warn("AniList rate limit exceeded, retrying in {0} seconds", retryAfter);
                    Thread.Sleep(TimeSpan.FromSeconds(retryAfter));
                    continue;
                }

                if (response.HasHttpError && response.StatusCode != HttpStatusCode.NotFound)
                {
                    throw new HttpException(request, response);
                }

                return response;
            }
        }

        private static int ParseRetryAfter(string header)
        {
            if (header.IsNotNullOrWhiteSpace() && int.TryParse(header, out var seconds) && seconds > 0)
            {
                return Math.Min(seconds, MaxRetryAfterSeconds);
            }

            return MaxRetryAfterSeconds;
        }

        private static bool IsNotFound<T>(HttpResponse<AniListResponse<T>> response)
            where T : new()
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return true;
            }

            return response.Resource?.Errors?.Any(e => e.Status == 404) == true;
        }

        private static void EnsureNoErrors<T>(HttpResponse<AniListResponse<T>> response)
            where T : new()
        {
            var errors = response.Resource?.Errors;

            if (errors != null && errors.Any())
            {
                throw new AniListException("AniList returned an error: {0}", string.Join("; ", errors.Select(e => e.Message)));
            }
        }
    }
}
