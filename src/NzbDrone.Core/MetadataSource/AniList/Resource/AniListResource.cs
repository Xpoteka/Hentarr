using System;
using System.Collections.Generic;

namespace NzbDrone.Core.MetadataSource.AniList.Resource
{
    public class AniListResponse<T>
        where T : new()
    {
        public T Data { get; set; }
        public List<AniListError> Errors { get; set; }
    }

    public class AniListError
    {
        public string Message { get; set; }
        public int? Status { get; set; }
    }

    public class AniListMediaData
    {
        public AniListMedia Media { get; set; }
    }

    public class AniListPageData
    {
        public AniListPage Page { get; set; }
    }

    public class AniListPage
    {
        public AniListPageInfo PageInfo { get; set; }
        public List<AniListMedia> Media { get; set; } = new List<AniListMedia>();
    }

    public class AniListPageInfo
    {
        public int? Total { get; set; }
        public int? CurrentPage { get; set; }
        public int? LastPage { get; set; }
        public bool HasNextPage { get; set; }
    }

    public class AniListMedia
    {
        public int Id { get; set; }
        public int? IdMal { get; set; }
        public string Type { get; set; }
        public bool IsAdult { get; set; }
        public AniListTitle Title { get; set; }
        public List<string> Synonyms { get; set; } = new List<string>();
        public string Format { get; set; }
        public string Status { get; set; }
        public int? Episodes { get; set; }
        public int? Duration { get; set; }
        public AniListFuzzyDate StartDate { get; set; }
        public AniListFuzzyDate EndDate { get; set; }
        public string Description { get; set; }
        public AniListCoverImage CoverImage { get; set; }
        public string BannerImage { get; set; }
        public List<string> Genres { get; set; } = new List<string>();
        public int? AverageScore { get; set; }
        public AniListStudioConnection Studios { get; set; }
        public AniListAiringScheduleConnection AiringSchedule { get; set; }
    }

    public class AniListRelationsData
    {
        public AniListRelationsMedia Media { get; set; }
    }

    public class AniListRelationsMedia
    {
        public int Id { get; set; }
        public AniListRelationConnection Relations { get; set; }
    }

    public class AniListRelationConnection
    {
        public List<AniListRelationEdge> Edges { get; set; } = new List<AniListRelationEdge>();
    }

    public class AniListRelationEdge
    {
        public string RelationType { get; set; }
        public AniListMedia Node { get; set; }
    }

    public class AniListTitle
    {
        public string Romaji { get; set; }
        public string English { get; set; }
        public string Native { get; set; }
    }

    public class AniListFuzzyDate
    {
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }

        public DateTime? ToDateTime()
        {
            if (!Year.HasValue)
            {
                return null;
            }

            var month = Math.Clamp(Month ?? 1, 1, 12);
            var day = Math.Clamp(Day ?? 1, 1, DateTime.DaysInMonth(Year.Value, month));

            return new DateTime(Year.Value, month, day, 0, 0, 0, DateTimeKind.Utc);
        }
    }

    public class AniListCoverImage
    {
        public string ExtraLarge { get; set; }
        public string Large { get; set; }
    }

    public class AniListStudioConnection
    {
        public List<AniListStudio> Nodes { get; set; } = new List<AniListStudio>();
    }

    public class AniListStudio
    {
        public string Name { get; set; }
    }

    public class AniListAiringScheduleConnection
    {
        public List<AniListAiringNode> Nodes { get; set; } = new List<AniListAiringNode>();
    }

    public class AniListAiringNode
    {
        public int Episode { get; set; }
        public long AiringAt { get; set; }
    }
}
