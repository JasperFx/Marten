using System.Linq;
using System.Threading.Tasks;
using Marten;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace EventSourcingTests.Projections;

public class include_extra_schema_objects_from_projections: OneOffConfigurationsContext
{
    public include_extra_schema_objects_from_projections()
    {
        StoreOptions(opts =>
        {
            opts.Projections.Add<TableCreatingProjection>(ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public void has_the_additional_table_in_events_schema_feature()
    {
        var tableName = new PostgresqlObjectName("extra", "names");

        var feature = theStore.Options.Storage.FindFeature(typeof(IEvent));
        feature.Objects.OfType<Table>().Any(x => Equals(x.Identifier, tableName)).ShouldBeTrue();
    }

    [Fact]
    public async Task build_feature_adds_the_table()
    {
        var tableName = new PostgresqlObjectName("extra", "names");

        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();

            await conn.DropSchemaAsync("extra");
        }

        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using (var conn = theStore.Storage.Database.CreateConnection())
        {
            await conn.OpenAsync();

            (await conn.ExistingTablesAsync()).Any(x => x.Equals(tableName)).ShouldBeTrue();
        }
    }
}

public class NameAdded
{
    public string Name { get; set; }
}

public class TableCreatingProjection: EventProjection
{
    public TableCreatingProjection()
    {
        var table = new Table(new PostgresqlObjectName("extra", "names"));
        table.AddColumn<string>("name").AsPrimaryKey();

        SchemaObjects.Add(table);
    }

    public void Project(NameAdded added, IDocumentOperations operations)
    {
    }
}
