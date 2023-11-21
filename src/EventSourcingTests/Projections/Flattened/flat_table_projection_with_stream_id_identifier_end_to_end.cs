using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;
using Xunit.Abstractions;
using CommandExtensions = Weasel.Postgresql.CommandExtensions;

namespace EventSourcingTests.Projections.Flattened;

public class flat_table_projection_with_stream_id_identifier_end_to_end: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public flat_table_projection_with_stream_id_identifier_end_to_end(ITestOutputHelper output)
    {
        _output = output;
        StoreOptions(opts =>
        {
            opts.Projections.Add<WriteTableWithGuidIdentifierProjection>(ProjectionLifecycle.Inline);
        });
    }

    [Fact]
    public async Task table_should_be_built()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var table = await new Table(new PostgresqlObjectName(SchemaName, "values")).FetchExistingAsync(conn);

        table.PrimaryKeyColumns.Single().ShouldBe("id");
        table.Columns.Select(x => x.Name).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("a", "b", "c", "d", "id", "revision", "status");
    }

    public class Data
    {
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public int D { get; set; }
        public string Status { get; set; }
        public int Version { get; set; }
    }

    private async Task<Data> readData(DbDataReader reader)
    {
        return new Data
        {
            A = await reader.GetFieldValueAsync<int>(1),
            B = await reader.GetFieldValueAsync<int>(2),
            C = await reader.GetFieldValueAsync<int>(3),
            D = await reader.GetFieldValueAsync<int>(4),
            Status = await reader.GetFieldValueAsync<string>(5),
            Version = await reader.GetFieldValueAsync<int>(6)
        };
    }

    private async Task<Data> findData(Guid streamId)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var all = await Weasel.Core.CommandExtensions
            .CreateCommand(conn, $"select * from {SchemaName}.values where id = :id")
            .With("id", streamId)
            .FetchListAsync(readData);

        return all.Single();
    }

    [Fact]
    public async Task functions_are_built()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        var functions = await theStore.Storage.Database.Functions();
        functions.Any(x => x.QualifiedName == $"{SchemaName}.mt_upsert_values_valuesadded").ShouldBeTrue();
        functions.Any(x => x.QualifiedName == $"{SchemaName}.mt_upsert_values_valuesset").ShouldBeTrue();
        functions.Any(x => x.QualifiedName == $"{SchemaName}.mt_upsert_values_valuessubtracted").ShouldBeTrue();

        functions.Any().ShouldBeTrue();
    }

    [Fact]
    public async Task set_values_on_new_row()
    {
        var streamId = Guid.NewGuid();
        var valuesSet = new ValuesSet { A = 3, B = 4, C = 5, D = 6 };
        theSession.Events.Append(streamId, valuesSet);

        await theSession.SaveChangesAsync();

        var data = await findData(streamId);

        data.A.ShouldBe(valuesSet.A);
        data.B.ShouldBe(valuesSet.B);
        data.C.ShouldBe(valuesSet.C);
        data.D.ShouldBe(valuesSet.D);

        data.Status.ShouldBe("new");
        data.Version.ShouldBe(1);
    }

    [Fact]
    public async Task set_values_on_existing_row()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new ValuesSet { A = 1, B = 2, C = 3, D = 4 });

        await theSession.SaveChangesAsync();

        var valuesSet = new ValuesSet { A = 3, B = 4, C = 5, D = 6 };

        theSession.Events.Append(streamId, valuesSet);

        await theSession.SaveChangesAsync();

        var data = await findData(streamId);

        data.A.ShouldBe(valuesSet.A);
        data.B.ShouldBe(valuesSet.B);
        data.C.ShouldBe(valuesSet.C);
        data.D.ShouldBe(valuesSet.D);

        data.Status.ShouldBe("new");
        data.Version.ShouldBe(1);
    }

    [Fact]
    public async Task increment_values_on_existing_row()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new ValuesSet { A = 1, B = 2, C = 3, D = 4 });

        await theSession.SaveChangesAsync();

        var valuesAdded = new ValuesAdded { A = 3, B = 4, C = 5, D = 6 };

        theSession.Events.Append(streamId, valuesAdded);

        await theSession.SaveChangesAsync();

        var data = await findData(streamId);

        data.A.ShouldBe(4);
        data.B.ShouldBe(6);
        data.C.ShouldBe(8);
        data.D.ShouldBe(10);

        data.Status.ShouldBe("old");
        data.Version.ShouldBe(2);
    }

    [Fact]
    public async Task decrement_values_on_existing_row()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new ValuesSet { A = 10, B = 10, C = 10, D = 10 });

        await theSession.SaveChangesAsync();

        var valuesAdded = new ValuesSubtracted { A = 3, B = 4, C = 5, D = 6 };

        theSession.Events.Append(streamId, valuesAdded);

        await theSession.SaveChangesAsync();

        var data = await findData(streamId);

        data.A.ShouldBe(7);
        data.B.ShouldBe(6);
        data.C.ShouldBe(5);
        data.D.ShouldBe(4);
    }

    [Fact]
    public async Task delete_a_row()
    {
        var streamId = Guid.NewGuid();
        theSession.Events.Append(streamId, new ValuesSet { A = 10, B = 10, C = 10, D = 10 });

        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new ValuesDeleted());
        await theSession.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var count = await Weasel.Core.CommandExtensions
            .CreateCommand(conn, $"select count(*) from {SchemaName}.values where id = :id")
            .With("id", streamId)
            .ExecuteScalarAsync();

        count.As<long>().ShouldBe(0);
    }
}
