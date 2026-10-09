using System;
using System.Net;
using NzbDrone.Core.Exceptions;

namespace NzbDrone.Core.MetadataSource.AniList
{
    public class AniListException : NzbDroneClientException
    {
        public AniListException(string message)
            : base(HttpStatusCode.ServiceUnavailable, message)
        {
        }

        public AniListException(string message, params object[] args)
            : base(HttpStatusCode.ServiceUnavailable, message, args)
        {
        }

        public AniListException(string message, Exception innerException, params object[] args)
            : base(HttpStatusCode.ServiceUnavailable, message, innerException, args)
        {
        }
    }
}
