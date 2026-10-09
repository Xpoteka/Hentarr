using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.AniList;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Test.DataAugmentation.AniList
{
    [TestFixture]
    public class AniListSceneMappingTriggerFixture : CoreTest<AniListSceneMappingTrigger>
    {
        [Test]
        public void should_queue_scene_mapping_update_when_series_is_added()
        {
            Subject.Handle(new SeriesAddedEvent(new Series { Id = 1, TvdbId = 6987 }));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<UpdateSceneMappingCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void should_queue_scene_mapping_update_when_series_are_imported()
        {
            Subject.Handle(new SeriesImportedEvent(new List<int> { 1, 2 }));

            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(c => c.Push(It.IsAny<UpdateSceneMappingCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }
    }
}
