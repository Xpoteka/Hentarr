using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.MetadataSource.AniList;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.AniList.Studio
{
    public class AniListStudioSettingsValidator : AbstractValidator<AniListStudioSettings>
    {
        public AniListStudioSettingsValidator()
        {
            RuleFor(c => c.StudioName).NotEmpty();
        }
    }

    // Fork: lists every anime entry of an AniList studio. No OAuth, so this does not derive from AniListSettingsBase.
    public class AniListStudioSettings : ImportListSettingsBase<AniListStudioSettings>
    {
        private static readonly AniListStudioSettingsValidator Validator = new();

        public override string BaseUrl { get; set; } = AniListGraphQlClient.Endpoint;

        [FieldDefinition(0, Label = "Studio", HelpText = "Studio name as shown on AniList, e.g. Pink Pineapple or T-Rex", Type = FieldType.Textbox)]
        public string StudioName { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "Main studio only", HelpText = "Only entries where AniList marks this studio as the main studio. AniList rarely does so for hentai labels (Pink Pineapple: 6 of 272 entries), so leave this off unless the list is far too broad", Type = FieldType.Checkbox)]
        public bool MainStudioOnly { get; set; } = false;

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
