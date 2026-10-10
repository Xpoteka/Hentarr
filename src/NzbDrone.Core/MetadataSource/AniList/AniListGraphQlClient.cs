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
        AniListStudioResource GetStudio(string studioName);
        List<AniListMedia> GetMediaByStudio(string studioName, bool mainStudioOnly);
        List<AniListMedia> GetMediaByStartDate(DateTime from, DateTime? to, AniListAdultFilter adultFilter);
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
            type
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
            airingSchedule(perPage: 50) { nodes { episode airingAt } }
            relations {
                edges {
                    relationType
                    node { id idMal type isAdult format status title { romaji english native } startDate { year month day } }
                }
            }";

        private const string StudioQuery = $@"
            query ($search: String, $isMain: Boolean, $page: Int, $perPage: Int) {{
                Studio(search: $search) {{
                    id
                    name
                    media(isMain: $isMain, sort: START_DATE, page: $page, perPage: $perPage) {{
                        pageInfo {{ total currentPage lastPage hasNextPage }}
                        nodes {{ {MediaFields} }}
                    }}
                }}
            }}";

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

        public AniListStudioResource GetStudio(string studioName)
        {
            var response = Execute<AniListStudioData>(StudioQuery, new { search = studioName, page = 1, perPage = 1 });

            if (IsNotFound(response))
            {
                return null;
            }

            EnsureNoErrors(response);

            return response.Resource?.Data?.Studio;
        }

        public List<AniListMedia> GetMediaByStudio(string studioName, bool mainStudioOnly)
        {
            var result = new List<AniListMedia>();
            var page = 1;

            while (true)
            {
                // AniList treats an explicit null isMain as "no entries"; the variable must be left out to get every entry
                var variables = mainStudioOnly
                    ? new { search = studioName, isMain = true, page, perPage = MaxPerPage }
                    : (object)new { search = studioName, page, perPage = MaxPerPage };
                var response = Execute<AniListStudioData>(StudioQuery, variables);

                if (IsNotFound(response))
                {
                    break;
                }

                EnsureNoErrors(response);

                var media = response.Resource?.Data?.Studio?.Media;

                if (media?.Nodes == null)
                {
                    break;
                }

                result.AddRange(media.Nodes.Where(m => string.Equals(m.Type, "ANIME", StringComparison.OrdinalIgnoreCase)));

                if (media.PageInfo == null || !media.PageInfo.HasNextPage)
                {
                    break;
                }

                page++;
            }

            return result;
        }

        public List<AniListMedia> GetMediaByStartDate(DateTime from, DateTime? to, AniListAdultFilter adultFilter)
        {
            var adultArgument = adultFilter switch
            {
                AniListAdultFilter.Adult => ", isAdult: true",
                AniListAdultFilter.NonAdult => ", isAdult: false",
                _ => string.Empty
            };

            // startDate_lesser is only declared when an upper bound is wanted; a null variable would match nothing
            var toArgument = to.HasValue ? ", startDate_lesser: $to" : string.Empty;
            var toVariable = to.HasValue ? ", $to: FuzzyDateInt" : string.Empty;

            var query = $@"
                query ($from: FuzzyDateInt{toVariable}, $page: Int, $perPage: Int) {{
                    Page(page: $page, perPage: $perPage) {{
                        pageInfo {{ total currentPage lastPage hasNextPage }}
                        media(type: ANIME, startDate_greater: $from{toArgument}, sort: START_DATE_DESC{adultArgument}) {{ {MediaFields} }}
                    }}
                }}";

            var result = new List<AniListMedia>();
            var page = 1;

            while (true)
            {
                var variables = to.HasValue
                    ? new { from = ToFuzzyDate(from), to = ToFuzzyDate(to.Value), page, perPage = MaxPerPage }
                    : (object)new { from = ToFuzzyDate(from), page, perPage = MaxPerPage };
                var response = Execute<AniListPageData>(query, variables);

                EnsureNoErrors(response);

                var pageData = response.Resource?.Data?.Page;

                _logger.Debug("AniList start date page {0} (from {1}): status {2}, {3} entries, has next page {4}",
                              page,
                              ToFuzzyDate(from),
                              (int)response.StatusCode,
                              pageData?.Media?.Count ?? -1,
                              pageData?.PageInfo?.HasNextPage);

                if (pageData?.Media == null || !pageData.Media.Any())
                {
                    if (page == 1)
                    {
                        _logger.Warn("AniList returned no entries for the start date query (from {0}); response body starts with: {1}", ToFuzzyDate(from), (response.Content ?? string.Empty).Truncate(200));
                    }

                    break;
                }

                result.AddRange(pageData.Media);

                if (pageData.PageInfo == null || !pageData.PageInfo.HasNextPage)
                {
                    break;
                }

                page++;
            }

            _logger.Debug("AniList start date query returned {0} entries over {1} page(s)", result.Count, page);

            return result;
        }

        private static int ToFuzzyDate(DateTime date)
        {
            return (date.Year * 10000) + (date.Month * 100) + date.Day;
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
