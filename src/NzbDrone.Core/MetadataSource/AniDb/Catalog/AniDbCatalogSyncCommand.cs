using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MetadataSource.AniDb.Catalog
{
    public class AniDbCatalogSyncCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => true;
    }
}
