using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Marten.Testing.Harness;
using Npgsql;
using NSubstitute.ClearExtensions;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;

namespace DocumentDbTests.Bugs;

public class Bug_1338_Validate_Null_ForeignKeyDefinition_ReferenceDocumenType: BugIntegrationContext, IAsyncLifetime
{
    [Fact]
    public void StorageFeatures_AllActiveFeatures_Should_Not_Throw_With_ExternalForeignKeyDefinitions()
    {
        theStore.StorageFeatures.AllActiveFeatures(theStore.Tenancy.Default.Database).All(x => x != null)
            .ShouldBeTrue();
    }

    public async Task InitializeAsync()
    {
        var table = new Table(new PostgresqlObjectName(SchemaName, "external_table"));
        table.AddColumn("id", "integer").AsPrimaryKey();

        await using var dbConn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await dbConn.OpenAsync();
        await table.CreateAsync(dbConn);

        await dbConn.CreateCommand("insert into bugs.external_table (id) values (1)").ExecuteNonQueryAsync();

        await dbConn.CloseAsync();

        StoreOptions(_ =>
        {
            _.Schema.For<ClassWithExternalForeignKey>()
                .ForeignKey(x => x.ForeignId, _.DatabaseSchemaName, "external_table", "id");
        }, false);

        await theStore.Advanced.Clean.DeleteDocumentsByTypeAsync(typeof(ClassWithExternalForeignKey));
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task UnitOfWork_GetTypeDependencies_Should_Not_Throw_With_ExternalForeignKeyDefinitions()
    {
        // Inserting a new document will force a call to:
        //  UnitOfWork.ApplyChanges()
        //  UnitOfWork.buildChangeSet()
        //  UnitOfWork.determineChanges()
        //  UnitOfWork.shouldSort()
        //  and finally, the function that we want to regression test"
        //  UnitOfWork.GetTypeDependencies(ClassWithExternalForeignKey)
        await using var session = theStore.LightweightSession();
        session.Insert(new ClassWithExternalForeignKey { Id = 1, ForeignId = 1 });
        await session.SaveChangesAsync();
    }
}

public class ClassWithExternalForeignKey
{
    public int Id { get; set; }
    public int ForeignId { get; set; }
}
