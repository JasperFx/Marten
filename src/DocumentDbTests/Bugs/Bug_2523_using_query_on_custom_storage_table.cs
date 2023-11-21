using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Marten;
using Marten.Testing.Documents;
using Marten.Testing.Harness;
using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_2523_using_query_on_custom_storage_table: BugIntegrationContext
{
    [Fact]
    public async Task WhenCustomTableIsUsedInABatchWithOtherDocumentResetAllShouldWork()
    {
        StoreOptions(opts =>
        {
            opts.Storage.Add<CustomTableStorage>();
        });

        var store = theStore;

        await store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var session = store.LightweightSession();
        session.QueueSqlCommand(CustomTableStorage.InsertSql, Guid.NewGuid().ToString());
        session.Insert(new User { FirstName = "John", LastName = "Doe" });
        await session.SaveChangesAsync();

        await store.Advanced.ResetAllData();
    }
}

public class CustomTableStorage: FeatureSchemaBase
{
    private const string TableName = "mt_custom_table";
    public const string InsertSql = $"insert into bugs.{TableName}(id) values(?)";

    private readonly StoreOptions _options;

    public CustomTableStorage(StoreOptions options): base("custom_table", options.Advanced.Migrator) =>
        _options = options;

    protected override IEnumerable<ISchemaObject> schemaObjects()
    {
        var table = new Table(new PostgresqlObjectName("bugs", TableName));
        table.AddColumn<string>("id").AsPrimaryKey();
        yield return table;
    }
}
