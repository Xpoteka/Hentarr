namespace NzbDrone.Core.MetadataSource.AniDb
{
    public class AniDbBannedException : AniDbException
    {
        public AniDbBannedException()
            : base("AniDB has temporarily banned this client. Pausing all AniDB requests, they will resume automatically later.")
        {
        }
    }
}
