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
            return MapSeries(AniListChain.Single(media));
        }

        public static List<Episode> MapEpisodes(AniListMedia media)
        {
            return MapEpisodes(AniListChain.Single(media));
        }

        // Fork: a chain of AniList entries becomes one series. The root entry provides the series metadata,
        // every later entry is a season, SPECIAL entries form season 0.
        public static Series MapSeries(AniListChain chain)
        {
            var media = chain.Root;
            var title = GetMainTitle(media);
            var originalTitle = media.Title?.Native;
            var cleanOriginalTitle = originalTitle.IsNotNullOrWhiteSpace() ? originalTitle.CleanSeriesTitle() : null;

            if (cleanOriginalTitle.IsNullOrWhiteSpace())
            {
                cleanOriginalTitle = null;
            }

            var firstAired = media.StartDate?.ToDateTime();
            var lastAired = chain.Seasons.Select(m => m.EndDate?.ToDateTime()).Where(d => d.HasValue).Max();
            var seasons = new List<Season>();

            if (chain.Specials.Any())
            {
                seasons.Add(new Season { SeasonNumber = 0, Monitored = false });
            }

            for (var index = 0; index < chain.Seasons.Count; index++)
            {
                seasons.Add(new Season
                {
                    SeasonNumber = index + 1,
                    Monitored = true,
                    Title = chain.IsSingle ? null : GetMainTitle(chain.Seasons[index])
                });
            }

            var series = new Series
            {
                TvdbId = media.Id,
                AniListIds = chain.All.Select(m => m.Id).ToHashSet(),
                MalIds = chain.All.Where(m => m.IdMal.HasValue).Select(m => m.IdMal.Value).ToHashSet(),
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
                Status = MapStatus(chain),
                Runtime = media.Duration ?? 0,
                Network = media.Studios?.Nodes?.FirstOrDefault()?.Name,
                Genres = media.Genres?.ToList() ?? new List<string>(),
                Ratings = MapRatings(media.AverageScore),
                Images = MapImages(media),
                SeriesType = SeriesTypes.Anime,
                Monitored = true,
                Seasons = seasons,
                SeasonTypes = new List<SeasonType>
                {
                    new SeasonType
                    {
                        Name = "Aired Order",
                        Type = SeasonType.Official,
                        SeasonNumbers = seasons.Select(s => s.SeasonNumber).ToList(),
                        EpisodeCount = chain.Seasons.Sum(GetEpisodeCount)
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

        public static List<Episode> MapEpisodes(AniListChain chain)
        {
            var episodes = new List<Episode>();
            var absolute = 0;

            for (var index = 0; index < chain.Seasons.Count; index++)
            {
                var media = chain.Seasons[index];
                var seasonNumber = index + 1;
                var count = GetEpisodeCount(media);
                var airTimes = GetAirTimes(media);
                var fallbackAirDate = media.StartDate?.ToDateTime();

                for (var number = 1; number <= count; number++)
                {
                    absolute++;

                    var airDateUtc = airTimes.TryGetValue(number, out var scheduled) ? scheduled : fallbackAirDate;
                    var episode = new Episode
                    {
                        TvdbId = 0,
                        SeasonNumber = seasonNumber,
                        EpisodeNumber = number,
                        AbsoluteEpisodeNumber = absolute,
                        Title = $"Episode {number}",
                        AirDateUtc = airDateUtc,
                        AirDate = airDateUtc?.ToString(Episode.AIR_DATE_FORMAT),
                        Runtime = media.Duration ?? 0,
                        Ratings = new Ratings()
                    };

                    if (!chain.IsSingle)
                    {
                        // Releases of a sequel entry are numbered from 1 again ("Title 2 - 01" is S02E01)
                        episode.SceneSeasonNumber = seasonNumber;
                        episode.SceneEpisodeNumber = number;
                        episode.SceneAbsoluteEpisodeNumber = number;
                    }

                    episodes.Add(episode);
                }
            }

            var specialNumber = 0;
            var seasonCleanTitles = chain.Seasons.Select(m => GetMainTitle(m).CleanSeriesTitle()).ToHashSet();

            foreach (var special in chain.Specials)
            {
                var count = GetEpisodeCount(special);
                var airTimes = GetAirTimes(special);
                var fallbackAirDate = special.StartDate?.ToDateTime();
                var title = GetSpecialTitle(chain, special);

                // A special titled like a season entry would hijack numberless releases of that entry when matching by title
                if (seasonCleanTitles.Contains(title.CleanSeriesTitle()))
                {
                    title += " (Special)";
                }

                for (var number = 1; number <= count; number++)
                {
                    specialNumber++;

                    var airDateUtc = airTimes.TryGetValue(number, out var scheduled) ? scheduled : fallbackAirDate;

                    episodes.Add(new Episode
                    {
                        TvdbId = 0,
                        SeasonNumber = 0,
                        EpisodeNumber = specialNumber,
                        AbsoluteEpisodeNumber = null,
                        Title = count > 1 ? $"{title} - Part {number}" : title,
                        AirDateUtc = airDateUtc,
                        AirDate = airDateUtc?.ToString(Episode.AIR_DATE_FORMAT),
                        Runtime = special.Duration ?? 0,
                        Ratings = new Ratings()
                    });
                }
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

        // Specials are usually titled "<series>: <special>"; the series part is dropped so the special search
        // ("<series> <episode title>") does not repeat it and file names stay short.
        public static string GetSpecialTitle(AniListChain chain, AniListMedia special)
        {
            var title = GetMainTitle(special);
            var rootTitle = GetMainTitle(chain.Root);

            if (rootTitle.IsNotNullOrWhiteSpace() &&
                title.Length > rootTitle.Length &&
                title.StartsWith(rootTitle, StringComparison.OrdinalIgnoreCase))
            {
                var rest = title.Substring(rootTitle.Length).TrimStart(' ', ':', '-', '~', '\u2013', '\u2014');

                if (rest.Length >= 3)
                {
                    return rest;
                }
            }

            return title;
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

        private static SeriesStatusType MapStatus(AniListChain chain)
        {
            if (chain.Seasons.All(m => IsFinished(m.Status)))
            {
                return SeriesStatusType.Ended;
            }

            if (string.Equals(chain.Root.Status, "NOT_YET_RELEASED", StringComparison.OrdinalIgnoreCase))
            {
                return SeriesStatusType.Upcoming;
            }

            return SeriesStatusType.Continuing;
        }

        private static bool IsFinished(string status)
        {
            switch (status?.ToUpperInvariant())
            {
                case "FINISHED":
                case "CANCELLED":
                    return true;
                default:
                    return false;
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
