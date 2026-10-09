using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DataAugmentation.AniList;
using NzbDrone.Core.DataAugmentation.Scene;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DataAugmentation.AniList
{
    [TestFixture]
    public class AniListSceneMappingCleanupFixture : CoreTest<AniListSceneMappingCleanup>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.GetAllByType(It.IsAny<string>()))
                  .Returns(new List<SceneMapping>());
        }

        [Test]
        public void should_do_nothing_when_no_obsolete_mappings_exist()
        {
            Subject.Handle(new ApplicationStartedEvent());

            Mocker.GetMock<ISceneMappingRepository>().Verify(r => r.DeleteMany(It.IsAny<List<SceneMapping>>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>().Verify(c => c.Push(It.IsAny<UpdateSceneMappingCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_delete_xem_and_services_mappings_and_refresh()
        {
            var xem = new SceneMapping { Id = 1, Type = "XemService", TvdbId = 360261, Title = "euphoria" };
            var services = new SceneMapping { Id = 2, Type = "ServicesProvider", TvdbId = 360261, Title = "euphoria" };
            IEnumerable<SceneMapping> deleted = null;

            Mocker.GetMock<ISceneMappingRepository>().Setup(r => r.GetAllByType("XemService")).Returns(new List<SceneMapping> { xem });
            Mocker.GetMock<ISceneMappingRepository>().Setup(r => r.GetAllByType("ServicesProvider")).Returns(new List<SceneMapping> { services });
            Mocker.GetMock<ISceneMappingRepository>()
                  .Setup(r => r.DeleteMany(It.IsAny<List<SceneMapping>>()))
                  .Callback<List<SceneMapping>>(m => deleted = m);

            Subject.Handle(new ApplicationStartedEvent());

            deleted.Should().BeEquivalentTo(new[] { xem, services });
            Mocker.GetMock<IManageCommandQueue>().Verify(c => c.Push(It.IsAny<UpdateSceneMappingCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }
    }
}
