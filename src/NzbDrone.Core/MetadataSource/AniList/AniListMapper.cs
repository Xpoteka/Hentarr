using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MetadataSource.AniList.Resource;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public static class AniListMapper
    {
        private static readonly Regex HtmlLineBreakRegex = new Regex(@"<br\s*/?>|</p>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagRegex = new Regex(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex ExcessNewLinesRegex = new Regex(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex SlugInvalidCharsRegex = new Regex(@"[^a-z0-9]+", RegexOptions.Compiled);
        private static readonly Regex LatinScriptRegex = new Regex(@"^[\p{IsBasicLatin}\p{IsLatin-1Supplement}\p{IsLatinExtended-A}\p{IsLatinExtended-B}\p{IsGeneralPunctuation}\p{IsLatinExtendedAdditional}]+$", RegexOptions.Compiled);

        public static Series MapSeries(AniListMedia media)
        {
            var title = GetMainTitle(media);
            var originalTitle = media.Title?.Native;
            var cleanOriginalTitle = originalTitle.IsNotNullOrWhiteSpace() ? originalTitle.CleanSeriesTitle() : null;

            if (cleanOriginalTitle.IsNullOrWhiteSpace())
            {
                cleanOriginalTitle = null;
            }

            var firstAired = media.StartDate?.ToDateTime();
            var lastAired = media.EndDate?.ToDateTime();
            var episodeCount = GetEpisodeCount(media);

            var series = new Series
            {
                TvdbId = media.Id,
                AniListIds = new HashSet<int> { media.Id },
                MalIds = media.IdMal.HasValue ? new HashSet<int> { media.IdMal.Value } : new HashSet<int>(),
                Title = title,
                CleanTitle = title.CleanSeriesTitle(),
                SortTitle = SeriesTitleNormalizer.Normalize(title, media.Id),
                TitleSlug = GenerateSlug(title, media.Id),
                OriginalTitle = originalTitle,
                CleanOriginalTitle = cleanOriginalTitle,
                OriginalLanguage = Language.Japanese,
                OriginalCountry = "JP",
                Overview = StripHtml(media.Description),
                FirstAired = firstAired,
                LastAired = lastAired,
                Year = firstAired?.Year ?? 0,
                Status = MapStatus(media.Status),
                Runtime = media.Duration ?? 0,
                Network = media.Studios?.Nodes?.FirstOrDefault()?.Name,
                Genres = media.Genres?.ToList() ?? new List<string>(),
                Ratings = MapRatings(media.AverageScore),
                Images = MapImages(media),
                SeriesType = SeriesTypes.Anime,
                Monitored = true,
                Seasons = new List<Season>
                {
                    new Season { SeasonNumber = 1, Monitored = true }
                },
                SeasonTypes = new List<SeasonType>
                {
                    new SeasonType
                    {
                        Name = "Aired Order",
                        Type = SeasonType.Official,
                        SeasonNumbers = new List<int> { 1 },
                        EpisodeCount = episodeCount
                    }
                },
                Translations = new List<SeriesTranslation>(),
                Actors = new List<Actor>()
            };

            if (media.IsAdult)
            {
                series.Certification = "R18+";
            }

            return series;
        }

        public static List<Episode> MapEpisodes(AniListMedia media)
        {
            var count = GetEpisodeCount(media);
            var airTimes = GetAirTimes(media);
            var fallbackAirDate = media.StartDate?.ToDateTime();
            var episodes = new List<Episode>(count);

            for (var number = 1; number <= count; number++)
            {
                var airDateUtc = airTimes.TryGetValue(number, out var scheduled) ? scheduled : fallbackAirDate;

                episodes.Add(new Episode
                {
                    TvdbId = 0,
                    SeasonNumber = 1,
                    EpisodeNumber = number,
                    AbsoluteEpisodeNumber = number,
                    Title = $"Episode {number}",
                    AirDateUtc = airDateUtc,
                    AirDate = airDateUtc?.ToString(Episode.AIR_DATE_FORMAT),
                    Runtime = media.Duration ?? 0,
                    Ratings = new Ratings()
                });
            }

            return episodes;
        }

        // Titles that are useful as additional search and parse terms. The main title is excluded.
        public static List<string> GetAlternateTitles(AniListMedia media)
        {
            var mainTitle = GetMainTitle(media);
            var candidates = new List<string>();

            if (media.Title != null)
            {
                candidates.Add(media.Title.Romaji);
                candidates.Add(media.Title.English);
            }

            if (media.Synonyms != null)
            {
                candidates.AddRange(media.Synonyms);
            }

            // "PRETTY×CATION" is released as "Pretty x Cation", so add a variant with the multiplication sign spelled out.
            candidates.AddRange(candidates.Where(c => c != null && c.Contains('×')).Select(c => c.Replace("×", " x ")).ToList());

            return candidates.Where(IsUsefulSearchTitle)
                             .Select(t => t.Trim())
                             .Where(t => !t.Equals(mainTitle, StringComparison.InvariantCultureIgnoreCase))
                             .Distinct(StringComparer.InvariantCultureIgnoreCase)
                             .ToList();
        }

        public static bool IsUsefulSearchTitle(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return false;
            }

            var trimmed = title.Trim();

            if (trimmed.Length < 3)
            {
                return false;
            }

            if (!LatinScriptRegex.IsMatch(trimmed))
            {
                return false;
            }

            return trimmed.Any(char.IsLetterOrDigit);
        }

        public static string GetMainTitle(AniListMedia media)
        {
            var title = media.Title?.Romaji;

            if (title.IsNullOrWhiteSpace())
            {
                title = media.Title?.English;
            }

            if (title.IsNullOrWhiteSpace())
            {
                title = media.Title?.Native;
            }

            if (title.IsNullOrWhiteSpace())
            {
                title = $"AniList {media.Id}";
            }

            return title.Trim();
        }

        public static int GetEpisodeCount(AniListMedia media)
        {
            if (media.Episodes.HasValue && media.Episodes.Value > 0)
            {
                return media.Episodes.Value;
            }

            var scheduled = media.AiringSchedule?.Nodes?.Select(n => n.Episode).DefaultIfEmpty(0).Max() ?? 0;

            return scheduled > 0 ? scheduled : 1;
        }

        public static string GenerateSlug(string title, int aniListId)
        {
            var slug = SlugInvalidCharsRegex.Replace((title ?? string.Empty).RemoveDiacritics().ToLowerInvariant(), "-").Trim('-');

            if (slug.IsNullOrWhiteSpace())
            {
                slug = "anilist";
            }

            return $"{slug}-{aniListId}";
        }

        public static string StripHtml(string html)
        {
            if (html.IsNullOrWhiteSpace())
            {
                return null;
            }

            var text = HtmlLineBreakRegex.Replace(html, "\n");
            text = HtmlTagRegex.Replace(text, string.Empty);
            text = WebUtility.HtmlDecode(text);
            text = text.Replace("\r\n", "\n");
            text = ExcessNewLinesRegex.Replace(text, "\n\n");

            return text.Trim();
        }

        private static SeriesStatusType MapStatus(string status)
        {
            switch (status?.ToUpperInvariant())
            {
                case "FINISHED":
                case "CANCELLED":
                    return SeriesStatusType.Ended;
                case "NOT_YET_RELEASED":
                    return SeriesStatusType.Upcoming;
                default:
                    return SeriesStatusType.Continuing;
            }
        }

        private static Ratings MapRatings(int? averageScore)
        {
            if (!averageScore.HasValue)
            {
                return new Ratings();
            }

            return new Ratings
            {
                Votes = 0,
                Value = averageScore.Value / 10m
            };
        }

        private static List<MediaCover.MediaCover> MapImages(AniListMedia media)
        {
            var images = new List<MediaCover.MediaCover>();
            var poster = media.CoverImage?.ExtraLarge;

            if (poster.IsNullOrWhiteSpace())
            {
                poster = media.CoverImage?.Large;
            }

            if (poster.IsNotNullOrWhiteSpace())
            {
                images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, poster));
            }

            if (media.BannerImage.IsNotNullOrWhiteSpace())
            {
                images.Add(new MediaCover.MediaCover(MediaCoverTypes.Banner, media.BannerImage));
            }

            return images;
        }

        private static Dictionary<int, DateTime> GetAirTimes(AniListMedia media)
        {
            var result = new Dictionary<int, DateTime>();

            if (media.AiringSchedule?.Nodes == null)
            {
                return result;
            }

            foreach (var node in media.AiringSchedule.Nodes.Where(n => n.Episode > 0 && n.AiringAt > 0))
            {
                result[node.Episode] = DateTimeOffset.FromUnixTimeSeconds(node.AiringAt).UtcDateTime;
            }

            return result;
        }
    }
}
