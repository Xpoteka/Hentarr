using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(ApplicationStartedEvent))]
    [CheckOn(typeof(ConfigSavedEvent))]
    public class AniDbClientNameCheck : HealthCheckBase
    {
        private const string DefaultClientName = "hentarr";

        private readonly IConfigService _configService;

        public AniDbClientNameCheck(IConfigService configService, ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
        }

        public override HealthCheck Check()
        {
            // AniDB requires HTTP API clients to be registered, everyone should
            // use their own registered client name instead of the default.
            if (_configService.AniDbClientName == DefaultClientName)
            {
                return new HealthCheck(GetType(), HealthCheckResult.Notice, _localizationService.GetLocalizedString("AniDbClientNameHealthCheckMessage"), "#anidb-client-name");
            }

            return new HealthCheck(GetType());
        }
    }
}
