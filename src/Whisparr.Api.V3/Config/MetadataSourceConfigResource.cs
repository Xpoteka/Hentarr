using NzbDrone.Core.Configuration;
using Whisparr.Http.REST;

namespace Whisparr.Api.V3.Config
{
    public class MetadataSourceConfigResource : RestResource
    {
        public string AniDbClientName { get; set; }
        public int AniDbClientVersion { get; set; }
    }

    public static class MetadataSourceConfigResourceMapper
    {
        public static MetadataSourceConfigResource ToResource(IConfigService model)
        {
            return new MetadataSourceConfigResource
            {
                AniDbClientName = model.AniDbClientName,
                AniDbClientVersion = model.AniDbClientVersion
            };
        }
    }
}
