using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Marten.Events;
using Marten.Events.Projections;
using Marten.Testing.Harness;
using Npgsql;
using Shouldly;
using Weasel.Core;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;
using Xunit;
using Xunit.Abstractions;
using CommandExtensions = Weasel.Core.CommandExtensions;

namespace EventSourcingTests.Projections.Flattened;

public class flat_table_projection_with_event_member_identifier_end_to_end: OneOffConfigurationsContext
{
    private readonly ITestOutputHelper _output;

    public flat_table_projection_with_event_member_identifier_end_to_end(ITestOutputHelper output)
    {
        _output = output;
        StoreOptions(opts =>
        {
            opts.Projections.Add<WriteTableWithEventMemberIdentityProjection>(ProjectionLifecycle.Inline);
            opts.Events.StreamIdentity = StreamIdentity.AsString;
        }, true);
    }

    [Fact]
    public async Task table_should_be_built()
    {
        await theStore.Storage.ApplyAllConfiguredChangesToDatabaseAsync();

        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var table = await new Table(new PostgresqlObjectName(SchemaName, "member_values")).FetchExistingAsync(conn);

        table.PrimaryKeyColumns.Single().ShouldBe("name");
        table.Columns.Select(x => x.Name).OrderBy(x => x)
            .ShouldHaveTheSameElementsAs("a", "b", "c", "d", "guid", "name", "revision", "status", "time");
    }

    public class Data
    {
        public int A { get; set; }
        public int B { get; set; }
        public int C { get; set; }
        public int D { get; set; }
        public string Status { get; set; }
        public int Version { get; set; }
        public Guid Guid { get; set; }
        public DateTimeOffset Time { get; set; }
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
            Version = await reader.GetFieldValueAsync<int>(6),
            Guid = await reader.GetFieldValueAsync<Guid>(7),
            Time = await reader.GetFieldValueAsync<DateTimeOffset>(8)
        };
    }

    private async Task<Data> findData(string name)
    {
        await using var conn = theStore.Storage.Database.CreateConnection();
        await conn.OpenAsync();

        var all = await CommandExtensions
            .CreateCommand(conn, $"select * from {SchemaName}.member_values where name = :id")
            .With("id", name)
            .FetchListAsync(readData);

        return all.Single();
    }


    [Fact]
    public async Task set_values_on_new_row()
    {
        var streamId = Guid.NewGuid().ToString();
        var valuesSet = new ValuesSet
        {
            A = 3,
            B = 4,
            C = 5,
            D = 6,
            Name = "red"
        };
        theSession.Events.Append(streamId, valuesSet);

        await theSession.SaveChangesAsync();

        var data = await findData("red");

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
        var streamId = Guid.NewGuid().ToString();
        var guid = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        theSession.Events.Append(streamId, new ValuesSet
        {
            A = 1,
            B = 2,
            C = 3,
            D = 4,
            Name = "blue",
            Guid = guid,
            Time = now
        });

        await theSession.SaveChangesAsync();

        var valuesSet = new ValuesSet
        {
            A = 3,
            B = 4,
            C = 5,
            D = 6,
            Name = "blue",
            Guid = guid,
            Time = now
        };

        theSession.Events.Append(streamId, valuesSet);

        await theSession.SaveChangesAsync();

        var data = await findData("blue");

        data.A.ShouldBe(valuesSet.A);
        data.B.ShouldBe(valuesSet.B);
        data.C.ShouldBe(valuesSet.C);
        data.D.ShouldBe(valuesSet.D);

        data.Guid.ShouldBe(guid);
        data.Time.ShouldBeEqualWithDbPrecision(now);

        data.Status.ShouldBe("new");
        data.Version.ShouldBe(1);
    }

    [Fact]
    public async Task increment_values_on_existing_row()
    {
        var streamId = Guid.NewGuid().ToString();
        theSession.Events.Append(streamId, new ValuesSet
        {
            A = 1,
            B = 2,
            C = 3,
            D = 4,
            Name = "green"
        });

        await theSession.SaveChangesAsync();

        var valuesAdded = new ValuesAdded
        {
            A = 3,
            B = 4,
            C = 5,
            D = 6,
            Name = "green"
        };

        theSession.Events.Append(streamId, valuesAdded);

        await theSession.SaveChangesAsync();

        var data = await findData("green");

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
        var streamId = Guid.NewGuid().ToString();
        theSession.Events.Append(streamId, new ValuesSet
        {
            A = 10,
            B = 10,
            C = 10,
            D = 10,
            Name = "orange"
        });

        await theSession.SaveChangesAsync();

        var valuesAdded = new ValuesSubtracted
        {
            A = 3,
            B = 4,
            C = 5,
            D = 6,
            Name = "orange"
        };

        theSession.Events.Append("orange", valuesAdded);

        await theSession.SaveChangesAsync();

        var data = await findData("orange");

        data.A.ShouldBe(7);
        data.B.ShouldBe(6);
        data.C.ShouldBe(5);
        data.D.ShouldBe(4);
    }

    [Fact]
    public async Task delete_a_row()
    {
        var guid = Guid.NewGuid();
        var time = DateTimeOffset.UtcNow;

        var streamId = Guid.NewGuid().ToString();
        theSession.Events.Append(streamId, new ValuesSet
        {
            A = 10,
            B = 10,
            C = 10,
            D = 10,
            Name = "purple",
            Guid = guid,
            Time = time
        });

        await theSession.SaveChangesAsync();

        theSession.Events.Append(streamId, new ValuesDeleted { Name = "purple" });
        await theSession.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(ConnectionSource.ConnectionString);
        await conn.OpenAsync();
        var count = await CommandExtensions
            .CreateCommand(conn, $"select count(*) from {SchemaName}.member_values where name = :id")
            .With("id", "purple")
            .ExecuteScalarAsync();

        count.As<long>().ShouldBe(0);
    }
}
