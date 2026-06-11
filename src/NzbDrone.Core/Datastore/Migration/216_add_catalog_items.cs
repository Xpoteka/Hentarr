using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(216)]
    public class add_catalog_items : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("CatalogItems")
                  .WithColumn("AniDbId").AsInt32().NotNullable().Unique()
                  .WithColumn("Title").AsString().Nullable()
                  .WithColumn("Year").AsInt32().NotNullable().WithDefaultValue(0)
                  .WithColumn("Studio").AsString().Nullable()
                  .WithColumn("Restricted").AsBoolean().NotNullable().WithDefaultValue(false)
                  .WithColumn("LastInfoSync").AsDateTimeOffset().Nullable()
                  .WithColumn("Added").AsDateTimeOffset().NotNullable();

            Create.Index().OnTable("CatalogItems").OnColumn("Studio");
        }
    }
}
