using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.AniList.NewReleases
{
    public class AniListNewReleasesSettingsValidator : AbstractValidator<AniListNewReleasesSettings>
    {
        public AniListNewReleasesSettingsValidator()
        {
            RuleFor(c => c.MonthsBack).InclusiveBetween(1, 120);
        }
    }

    // Fork: every anime entry on AniList that started recently, whatever the studio.
    public class AniListNewReleasesSettings : ImportListSettingsBase<AniListNewReleasesSettings>
    {
        private static readonly AniListNewReleasesSettingsValidator Validator = new();

        public override string BaseUrl { get; set; } = AniListGraphQlClient.Endpoint;

        [FieldDefinition(0, Label = "Months back", HelpText = "Entries that started within this many months before today are listed (1 to 120)", Type = FieldType.Number)]
        public int MonthsBack { get; set; } = 3;

        [FieldDefinition(1, Label = "Include upcoming", HelpText = "Also list entries AniList dates in the future, so they are picked up on release day", Type = FieldType.Checkbox)]
        public bool IncludeUpcoming { get; set; } = true;

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
