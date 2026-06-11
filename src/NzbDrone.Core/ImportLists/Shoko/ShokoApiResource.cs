using System.Collections.Generic;

namespace NzbDrone.Core.ImportLists.Shoko
{
    public class ShokoListResult<T>
    {
        public int Total { get; set; }
        public List<T> List { get; set; }
    }

    public class ShokoSeriesResource
    {
        public ShokoIdsResource IDs { get; set; }
        public string Name { get; set; }
        public int Size { get; set; }
    }

    public class ShokoEpisodeResource
    {
        public ShokoIdsResource IDs { get; set; }
        public string Name { get; set; }
        public int Size { get; set; }
    }

    public class ShokoIdsResource
    {
        public int ID { get; set; }
        public int AniDB { get; set; }
    }
}
