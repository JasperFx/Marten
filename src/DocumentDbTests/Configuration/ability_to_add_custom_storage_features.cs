using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Configuration;

public class ability_to_add_custom_storage_features: OneOffConfigurationsContext
{
    [Fact]
    public async Task can_register_a_custom_feature()
    {
        StoreOptions(_ =>
        {
            _.Storage.Add<FakeStorage>();
        });

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
        (await theStore.Tenancy.Default.Database.SchemaTables()).Any(x => x.Name == "mt_fake_table")
            .ShouldBeTrue();
    }

    public void using_custom_feature_schema()
    {
        #region sample_adding-schema-feature

        var store = DocumentStore.For(_ =>
        {
            // Creates a new instance of FakeStorage and
            // passes along the current StoreOptions
            _.Storage.Add<FakeStorage>();

            // or

            _.Storage.Add(new FakeStorage(_));
        });

        #endregion
    }
}

#region sample_creating-a-fake-schema-feature

public class FakeStorage: FeatureSchemaBase
{
    private readonly StoreOptions _options;

    public FakeStorage(StoreOptions options): base("fake", options.Advanced.Migrator)
    {
        _options = options;
    }

    protected override IEnumerable<ISchemaObject> schemaObjects()
    {
        var table = new Table(new PostgresqlObjectName(_options.DatabaseSchemaName, "mt_fake_table"));
        table.AddColumn("name", "varchar");

        yield return table;
    }
}

#endregion
