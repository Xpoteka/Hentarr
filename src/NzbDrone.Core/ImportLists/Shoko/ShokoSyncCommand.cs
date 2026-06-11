using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.ImportLists.Shoko
{
    public class ShokoSyncCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool UpdateScheduledTask => true;
    }
}
