using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.AniDbStudio
{
    public class AniDbStudioSettingsValidator : AbstractValidator<AniDbStudioSettings>
    {
        public AniDbStudioSettingsValidator()
        {
            RuleFor(c => c.Studio).NotEmpty();
        }
    }

    public class AniDbStudioSettings : IImportListSettings
    {
        private static readonly AniDbStudioSettingsValidator Validator = new AniDbStudioSettingsValidator();

        public AniDbStudioSettings()
        {
            BaseUrl = "";
            RestrictedOnly = true;
        }

        public string BaseUrl { get; set; }

        [FieldDefinition(0, Type = FieldType.Select, SelectOptionsProviderAction = "getStudios", Label = "Studio", HelpText = "All known works of this studio will be added automatically. Studio information is aggregated from AniDB in the background, so the list of known works grows over time.")]
        public string Studio { get; set; }

        [FieldDefinition(1, Type = FieldType.Checkbox, Label = "18+ Only", HelpText = "Only add titles that are age restricted (18+) on AniDB")]
        public bool RestrictedOnly { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
