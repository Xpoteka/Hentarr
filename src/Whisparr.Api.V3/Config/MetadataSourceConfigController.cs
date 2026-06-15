using FluentValidation;
using NzbDrone.Core.Configuration;
using Whisparr.Http;

namespace Whisparr.Api.V3.Config
{
    [V3ApiController("config/metadatasource")]
    public class MetadataSourceConfigController : ConfigController<MetadataSourceConfigResource>
    {
        public MetadataSourceConfigController(IConfigService configService)
            : base(configService)
        {
            SharedValidator.RuleFor(c => c.AniDbClientName)
                           .NotEmpty();

            SharedValidator.RuleFor(c => c.AniDbClientVersion)
                           .GreaterThan(0);
        }

        protected override MetadataSourceConfigResource ToResource(IConfigService model)
        {
            return MetadataSourceConfigResourceMapper.ToResource(model);
        }
    }
}
