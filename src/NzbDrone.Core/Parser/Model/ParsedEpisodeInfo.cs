using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Parser.Model
{
    public class ParsedEpisodeInfo
    {
        public string ReleaseTitle { get; set; }
        public string SeriesTitle { get; set; } // Site Title
        public SeriesTitleInfo SeriesTitleInfo { get; set; }
        public QualityModel Quality { get; set; }
        public string AirDate { get; set; } // Release Date
        public int[] AbsoluteEpisodeNumbers { get; set; }
        public List<Language> Languages { get; set; }
        public string ReleaseGroup { get; set; }
        public string ReleaseHash { get; set; }
        public int SeasonPart { get; set; }
        public string ReleaseTokens { get; set; }
        public string ExternalId { get; set; }

        public ParsedEpisodeInfo()
        {
            AbsoluteEpisodeNumbers = Array.Empty<int>();
            Languages = new List<Language>();
        }

        public bool IsDaily
        {
            get
            {
                return !string.IsNullOrWhiteSpace(AirDate);
            }

            private set
            {
            }
        }

        public bool IsAbsoluteNumbering
        {
            get
            {
                return AbsoluteEpisodeNumbers.Any();
            }

            private set
            {
            }
        }

        public override string ToString()
        {
            var episodeString = "[Unknown Episode]";

            if (IsDaily)
            {
                episodeString = string.Format("{0}", AirDate);
            }
            else if (IsAbsoluteNumbering)
            {
                episodeString = string.Join("-", AbsoluteEpisodeNumbers.Select(e => e.ToString("000")));
            }

            return string.Format("{0} - {1} {2}", SeriesTitle, episodeString, Quality);
        }
    }
}
