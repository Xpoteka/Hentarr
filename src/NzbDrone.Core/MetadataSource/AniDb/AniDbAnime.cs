using System.Collections.Generic;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource.AniDb
{
    public class AniDbAnime
    {
        public AniDbAnime()
        {
            Episodes = new List<Episode>();
        }

        public Series Series { get; set; }
        public List<Episode> Episodes { get; set; }
        public bool Restricted { get; set; }
        public string Type { get; set; }
    }
}
