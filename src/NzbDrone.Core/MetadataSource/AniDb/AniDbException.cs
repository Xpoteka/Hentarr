using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public class AniDbException : NzbDroneException
    {
        public AniDbException(string message)
            : base(message)
        {
        }

        public AniDbException(string message, params object[] args)
            : base(message, args)
        {
        }
    }
}
