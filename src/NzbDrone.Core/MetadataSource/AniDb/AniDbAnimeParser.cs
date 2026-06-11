using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public static class AniDbAnimeParser
    {
        private const string IMAGE_BASE_URL = "https://cdn.anidb.net/images/main/";

        private static readonly XNamespace Xml = XNamespace.Xml;
        private static readonly Regex LinkRegex = new Regex(@"https?://anidb\.net/\S+ \[([^\]]+)\]", RegexOptions.Compiled);

        public static AniDbAnime ParseAnime(XElement animeElement)
        {
            var anidbId = (int)animeElement.Attribute("id");
            var restricted = string.Equals((string)animeElement.Attribute("restricted"), "true", StringComparison.InvariantCultureIgnoreCase);

            var title = GetMainTitle(animeElement.Element("titles"));

            var series = new Series
            {
                TvdbId = anidbId,
                Title = title,
                CleanTitle = Parser.Parser.CleanSeriesTitle(title),
                SortTitle = SeriesTitleNormalizer.Normalize(title, anidbId),
                TitleSlug = anidbId.ToString(CultureInfo.InvariantCulture),
                OriginalLanguage = Language.Japanese,
                Overview = CleanDescription(animeElement.Element("description")?.Value),
                Monitored = true,
                SeriesType = SeriesTypes.Standard,
                Certification = restricted ? "X" : null,
                Network = GetStudio(animeElement.Element("creators")),
                Genres = GetGenres(animeElement.Element("tags")),
                Ratings = GetRatings(animeElement.Element("ratings"))
            };

            var startDate = ParseDate(animeElement.Element("startdate")?.Value);
            var endDate = ParseDate(animeElement.Element("enddate")?.Value);

            if (startDate.HasValue)
            {
                series.FirstAired = startDate;
                series.Year = startDate.Value.Year;
            }

            series.Status = endDate.HasValue && endDate.Value <= DateTime.UtcNow
                ? SeriesStatusType.Ended
                : SeriesStatusType.Continuing;

            var picture = animeElement.Element("picture")?.Value;

            if (picture.IsNotNullOrWhiteSpace())
            {
                series.Images.Add(new MediaCover.MediaCover(MediaCoverTypes.Poster, IMAGE_BASE_URL + picture));
            }

            var anime = new AniDbAnime
            {
                Series = series,
                Restricted = restricted,
                Type = animeElement.Element("type")?.Value
            };

            var episodesElement = animeElement.Element("episodes");

            if (episodesElement != null)
            {
                foreach (var episodeElement in episodesElement.Elements("episode"))
                {
                    var episode = ParseEpisode(episodeElement, series);

                    if (episode != null)
                    {
                        anime.Episodes.Add(episode);
                    }
                }
            }

            series.Runtime = anime.Episodes.Select(e => e.Runtime).FirstOrDefault(r => r > 0);
            series.Seasons = anime.Episodes.Select(e => e.SeasonNumber)
                                           .Distinct()
                                           .OrderBy(s => s)
                                           .Select(s => new Season
                                           {
                                               SeasonNumber = s,
                                               Monitored = true
                                           })
                                           .ToList();

            return anime;
        }

        private static Episode ParseEpisode(XElement episodeElement, Series series)
        {
            var epno = episodeElement.Element("epno");
            var epnoType = (int?)epno?.Attribute("type") ?? 0;

            // Type 1 are regular episodes, type 2 are specials. Everything else
            // (credits, trailers, parodies, other) is not useful for tracking.
            if (epnoType != 1 && epnoType != 2)
            {
                return null;
            }

            var airDate = ParseDate(episodeElement.Element("airdate")?.Value);

            var episode = new Episode
            {
                TvdbId = (int)episodeElement.Attribute("id"),
                Title = GetEpisodeTitle(episodeElement),
                Overview = episodeElement.Element("summary")?.Value,
                Runtime = (int?)episodeElement.Element("length") ?? 0,
                Ratings = new Ratings()
            };

            if (airDate.HasValue)
            {
                episode.AirDate = airDate.Value.ToString(Episode.AIR_DATE_FORMAT, CultureInfo.InvariantCulture);
                episode.AirDateUtc = DateTime.SpecifyKind(airDate.Value, DateTimeKind.Utc);
            }

            if (epnoType == 2)
            {
                episode.SeasonNumber = 0;
            }
            else
            {
                episode.SeasonNumber = airDate?.Year ?? series.Year;

                if (episode.SeasonNumber == 0)
                {
                    episode.SeasonNumber = 1;
                }

                if (int.TryParse(epno?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var episodeNumber))
                {
                    episode.AbsoluteEpisodeNumber = episodeNumber;
                }
            }

            return episode;
        }

        private static string GetMainTitle(XElement titlesElement)
        {
            if (titlesElement == null)
            {
                return null;
            }

            var titles = titlesElement.Elements("title").ToList();

            var mainTitle = titles.FirstOrDefault(t => string.Equals((string)t.Attribute("type"), "main", StringComparison.InvariantCultureIgnoreCase))?.Value;

            return mainTitle ?? titles.FirstOrDefault()?.Value;
        }

        private static string GetEpisodeTitle(XElement episodeElement)
        {
            var titles = episodeElement.Elements("title").ToList();

            var english = titles.FirstOrDefault(t => string.Equals((string)t.Attribute(Xml + "lang"), "en", StringComparison.InvariantCultureIgnoreCase))?.Value;

            if (english.IsNotNullOrWhiteSpace())
            {
                return english;
            }

            var romaji = titles.FirstOrDefault(t => string.Equals((string)t.Attribute(Xml + "lang"), "x-jat", StringComparison.InvariantCultureIgnoreCase))?.Value;

            if (romaji.IsNotNullOrWhiteSpace())
            {
                return romaji;
            }

            return titles.FirstOrDefault()?.Value;
        }

        private static string GetStudio(XElement creatorsElement)
        {
            if (creatorsElement == null)
            {
                return null;
            }

            return creatorsElement.Elements("name")
                                  .FirstOrDefault(c => string.Equals((string)c.Attribute("type"), "Animation Work", StringComparison.InvariantCultureIgnoreCase))?.Value;
        }

        private static List<string> GetGenres(XElement tagsElement)
        {
            if (tagsElement == null)
            {
                return new List<string>();
            }

            return tagsElement.Elements("tag")
                              .Select(t => new
                              {
                                  Name = t.Element("name")?.Value,
                                  Weight = (int?)t.Attribute("weight") ?? 0
                              })
                              .Where(t => t.Name.IsNotNullOrWhiteSpace())
                              .OrderByDescending(t => t.Weight)
                              .Take(10)
                              .Select(t => t.Name)
                              .ToList();
        }

        private static Ratings GetRatings(XElement ratingsElement)
        {
            var permanent = ratingsElement?.Element("permanent");

            if (permanent == null)
            {
                return new Ratings();
            }

            return new Ratings
            {
                Votes = (int?)permanent.Attribute("count") ?? 0,
                Value = decimal.TryParse(permanent.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0
            };
        }

        private static string CleanDescription(string description)
        {
            if (description.IsNullOrWhiteSpace())
            {
                return description;
            }

            return LinkRegex.Replace(description, "$1");
        }

        private static DateTime? ParseDate(string date)
        {
            if (date.IsNullOrWhiteSpace())
            {
                return null;
            }

            if (DateTime.TryParseExact(date, "yyyy-MM-dd", DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }
}
