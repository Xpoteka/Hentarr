using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Shoko
{
    public class ShokoSettingsValidator : AbstractValidator<ShokoSettings>
    {
        public ShokoSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class ShokoSettings : IImportListSettings
    {
        private static readonly ShokoSettingsValidator Validator = new ShokoSettingsValidator();

        public ShokoSettings()
        {
            BaseUrl = "http://localhost:8111";
        }

        [FieldDefinition(0, Label = "Full URL", HelpText = "URL, including port, of the Shoko server instance")]
        public string BaseUrl { get; set; }

        [FieldDefinition(1, Label = "API Key", HelpText = "Shoko API key, can be generated with a POST to /api/auth on the Shoko server")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Type = FieldType.Checkbox, Label = "Sync Library As Exclusions", HelpText = "Everything already in the Shoko library is added to the import list exclusions, so other lists will not queue titles you already have")]
        public bool SyncHaveAsExclusions { get; set; }

        [FieldDefinition(3, Type = FieldType.Checkbox, Label = "Unmonitor Owned Episodes", HelpText = "Episodes with files in Shoko are unmonitored in matching series, so they are not downloaded again")]
        public bool UnmonitorOwnedEpisodes { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
