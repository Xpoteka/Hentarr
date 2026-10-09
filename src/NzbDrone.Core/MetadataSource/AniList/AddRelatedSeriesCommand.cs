using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MetadataSource.AniList
{
    // Fork: adds every related AniList entry (sequels, prequels, side stories, spin-offs) of the given series,
    // or of all series in the library when no SeriesId is given.
    public class AddRelatedSeriesCommand : Command
    {
        public int? SeriesId { get; set; }

        public override bool SendUpdatesToClient => true;
    }
}
