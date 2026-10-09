using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.DataAugmentation.AniList
{
    // Fork: SceneMappingService only refreshes mappings on the first series added after startup.
    // Every added series brings its own alternate titles, so queue a mapping update on each add.
    public class AniListSceneMappingTrigger : IHandle<SeriesAddedEvent>, IHandle<SeriesImportedEvent>
    {
        private readonly IManageCommandQueue _commandQueueManager;

        public AniListSceneMappingTrigger(IManageCommandQueue commandQueueManager)
        {
            _commandQueueManager = commandQueueManager;
        }

        public void Handle(SeriesAddedEvent message)
        {
            _commandQueueManager.Push(new UpdateSceneMappingCommand());
        }

        public void Handle(SeriesImportedEvent message)
        {
            _commandQueueManager.Push(new UpdateSceneMappingCommand());
        }
    }
}
