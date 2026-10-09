using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.DataAugmentation.AniList
{
    // Fork: mappings fetched from TheXEM or services.sonarr.tv are keyed by TVDB id. Since Series.TvdbId now holds
    // AniList ids, any leftover rows would map a release title to a series that does not exist and the release
    // would be rejected. Purge them once at startup.
    public class AniListSceneMappingCleanup : IHandle<ApplicationStartedEvent>
    {
        public static readonly string[] ObsoleteTypes = { "XemService", "ServicesProvider" };

        private readonly ISceneMappingRepository _repository;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public AniListSceneMappingCleanup(ISceneMappingRepository repository, IManageCommandQueue commandQueueManager, Logger logger)
        {
            _repository = repository;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            var obsolete = new List<SceneMapping>();

            foreach (var type in ObsoleteTypes)
            {
                obsolete.AddRange(_repository.GetAllByType(type));
            }

            if (obsolete.Empty())
            {
                return;
            }

            _logger.Info("Removing {0} TVDB-keyed scene mappings left over from upstream providers", obsolete.Count);
            _repository.DeleteMany(obsolete);
            _commandQueueManager.Push(new UpdateSceneMappingCommand());
        }
    }
}
