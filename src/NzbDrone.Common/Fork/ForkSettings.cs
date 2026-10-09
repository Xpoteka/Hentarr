namespace NzbDrone.Common.Fork
{
    // Fork: single place that switches off everything that would contact Sonarr's own services.
    // Referenced from the few upstream call sites that are disabled; grep for ForkSettings to find them.
    public static class ForkSettings
    {
        // Crash reports to sentry.sonarr.tv
        public static bool CrashReportingEnabled => false;

        // Update checks and installs from services.sonarr.tv
        public static bool UpdaterEnabled => false;

        // Health checks and notifications that call services.sonarr.tv
        public static bool UpstreamServicesEnabled => false;
    }
}
