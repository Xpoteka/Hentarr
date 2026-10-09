using System;
using System.Xml.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public enum AniListAdultFilter
    {
        Adult,
        NonAdult,
        All
    }

    public interface IAniListMetadataOptions
    {
        AniListAdultFilter AdultFilter { get; }
    }

    // Fork setting. Read from the HENTARR_ADULT_FILTER environment variable first
    // (adult | nonadult | all), then from an <AniListAdultFilter> element in config.xml.
    // Defaults to adult-only. Kept out of ConfigFileProvider to keep the upstream diff small.
    public class AniListMetadataOptions : IAniListMetadataOptions
    {
        public const string EnvironmentVariable = "HENTARR_ADULT_FILTER";
        public const string ConfigElement = "AniListAdultFilter";

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;
        private readonly Lazy<AniListAdultFilter> _adultFilter;

        public AniListMetadataOptions(IAppFolderInfo appFolderInfo, IDiskProvider diskProvider, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _logger = logger;
            _adultFilter = new Lazy<AniListAdultFilter>(ReadAdultFilter);
        }

        public AniListAdultFilter AdultFilter => _adultFilter.Value;

        public static bool TryParse(string value, out AniListAdultFilter filter)
        {
            switch (value?.Trim().ToLowerInvariant())
            {
                case "adult":
                case "adultonly":
                case "true":
                    filter = AniListAdultFilter.Adult;
                    return true;
                case "nonadult":
                case "non-adult":
                case "sfw":
                case "false":
                    filter = AniListAdultFilter.NonAdult;
                    return true;
                case "all":
                case "both":
                case "any":
                    filter = AniListAdultFilter.All;
                    return true;
                default:
                    filter = AniListAdultFilter.Adult;
                    return false;
            }
        }

        private AniListAdultFilter ReadAdultFilter()
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);

            if (fromEnvironment.IsNotNullOrWhiteSpace())
            {
                if (TryParse(fromEnvironment, out var envFilter))
                {
                    _logger.Debug("AniList adult filter set to {0} from {1}", envFilter, EnvironmentVariable);
                    return envFilter;
                }

                _logger.Warn("Unrecognised value '{0}' for {1}, expected adult, nonadult or all", fromEnvironment, EnvironmentVariable);
            }

            try
            {
                var configPath = _appFolderInfo.GetConfigPath();

                if (_diskProvider.FileExists(configPath))
                {
                    var element = XDocument.Parse(_diskProvider.ReadAllText(configPath)).Root?.Element(ConfigElement);

                    if (element != null && element.Value.IsNotNullOrWhiteSpace())
                    {
                        if (TryParse(element.Value, out var configFilter))
                        {
                            _logger.Debug("AniList adult filter set to {0} from config.xml", configFilter);
                            return configFilter;
                        }

                        _logger.Warn("Unrecognised value '{0}' for {1} in config.xml, expected adult, nonadult or all", element.Value, ConfigElement);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to read {0} from config.xml", ConfigElement);
            }

            return AniListAdultFilter.Adult;
        }
    }
}
