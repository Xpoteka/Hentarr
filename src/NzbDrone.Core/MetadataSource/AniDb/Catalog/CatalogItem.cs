using System;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.MetadataSource.AniDb.Catalog
{
    public class CatalogItem : ModelBase
    {
        public int AniDbId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string Studio { get; set; }
        public bool Restricted { get; set; }
        public DateTime? LastInfoSync { get; set; }
        public DateTime Added { get; set; }
    }
}
